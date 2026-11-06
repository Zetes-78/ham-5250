using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Hamricks5250.Core;

public sealed class ConnectionOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 992;
    public bool UseTls { get; set; } = true;
    /// <summary>Accept a certificate that doesn't chain to a trusted root (lab only).</summary>
    public bool AllowUntrustedCertificate { get; set; }
    /// <summary>SHA-1 thumbprint of a specific certificate to trust even if it isn't chained (e.g. self-signed IBM i cert).</summary>
    public string? TrustedThumbprint { get; set; }
    public string DeviceName { get; set; } = "";
    public bool Wide { get; set; }   // 27x132 capable terminal type
    public int CodePage { get; set; } = 37;
    public int ConnectTimeoutSeconds { get; set; } = 15;

    public string TerminalType => Wide ? "IBM-3477-FC" : "IBM-3179-2";
}

/// <summary>TN5250E (RFC 1205 / 2877 / 4777) transport: telnet negotiation, record framing with IAC EOR, TLS.</summary>
public sealed class TnConnection : IDisposable
{
    private const byte IAC = 255, DONT = 254, DO = 253, WONT = 252, WILL = 251, SB = 250, SE = 240, EOR = 239;
    private const byte OPT_BINARY = 0, OPT_TTYPE = 24, OPT_EOR = 25, OPT_NEW_ENVIRON = 39;
    private const byte TT_IS = 0, TT_SEND = 1;
    private const byte ENV_IS = 0, ENV_SEND = 1, ENV_VAR = 0, ENV_VALUE = 1, ENV_ESC = 2, ENV_USERVAR = 3;

    private TcpClient? _tcp;
    private Stream? _stream;
    private readonly object _writeLock = new();
    private ConnectionOptions _options = new();
    private CancellationTokenSource? _cts;

    public event Action<byte[]>? RecordReceived;
    public event Action<string>? Disconnected;

    public bool IsConnected { get; private set; }
    public bool IsTls { get; private set; }
    public string? CertificateSubject { get; private set; }

    /// <summary>For tests: bypass sockets and feed/collect raw bytes.</summary>
    public Action<byte[]>? RawWriter { get; set; }

    public async Task ConnectAsync(ConnectionOptions options, CancellationToken ct = default)
    {
        _options = options;
        _tcp = new TcpClient { NoDelay = true };
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(options.ConnectTimeoutSeconds));
            await _tcp.ConnectAsync(options.Host, options.Port, timeout.Token).ConfigureAwait(false);
        }
        Stream stream = _tcp.GetStream();
        if (options.UseTls)
        {
            var ssl = new SslStream(stream, false, ValidateCertificate);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = options.Host }, ct)
                .ConfigureAwait(false);
            CertificateSubject = ssl.RemoteCertificate?.Subject;
            IsTls = true;
            stream = ssl;
        }
        _stream = stream;
        IsConnected = true;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoop(_cts.Token));
    }

    private bool ValidateCertificate(object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None) return true;
        if (cert != null && !string.IsNullOrWhiteSpace(_options.TrustedThumbprint))
        {
            var thumb = new X509Certificate2(cert).Thumbprint;
            if (string.Equals(thumb, _options.TrustedThumbprint.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        LastCertificateError = $"{errors} (certificate: {cert?.Subject ?? "none"}, thumbprint: {(cert == null ? "-" : new X509Certificate2(cert).Thumbprint)})";
        return _options.AllowUntrustedCertificate;
    }

    public string? LastCertificateError { get; private set; }

    private async Task ReadLoop(CancellationToken ct)
    {
        var buffer = new byte[16384];
        string reason = "Connection closed by host";
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await _stream!.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (n <= 0) break;
                Feed(buffer.AsSpan(0, n));
            }
        }
        catch (OperationCanceledException) { reason = "Disconnected"; }
        catch (Exception ex) { reason = ex.Message; }
        IsConnected = false;
        Disconnected?.Invoke(reason);
    }

    // ---- Telnet state machine ---------------------------------------------------------
    private enum State { Data, Iac, Option, Sb, SbIac }
    private State _state = State.Data;
    private byte _verb;
    private readonly List<byte> _record = new();
    private readonly List<byte> _sb = new();
    private readonly HashSet<byte> _weWill = new(), _theyWill = new();

    /// <summary>Process raw bytes from the host (public so tests can drive it without a socket).</summary>
    public void Feed(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            switch (_state)
            {
                case State.Data:
                    if (b == IAC) _state = State.Iac; else _record.Add(b);
                    break;
                case State.Iac:
                    switch (b)
                    {
                        case IAC: _record.Add(IAC); _state = State.Data; break;
                        case EOR:
                            var rec = _record.ToArray();
                            _record.Clear();
                            _state = State.Data;
                            if (rec.Length > 0) RecordReceived?.Invoke(rec);
                            break;
                        case DO: case DONT: case WILL: case WONT: _verb = b; _state = State.Option; break;
                        case SB: _sb.Clear(); _state = State.Sb; break;
                        default: _state = State.Data; break;   // NOP, GA, etc.
                    }
                    break;
                case State.Option:
                    HandleOption(_verb, b);
                    _state = State.Data;
                    break;
                case State.Sb:
                    if (b == IAC) _state = State.SbIac; else _sb.Add(b);
                    break;
                case State.SbIac:
                    if (b == SE) { HandleSubnegotiation(_sb.ToArray()); _state = State.Data; }
                    else { if (b == IAC) _sb.Add(IAC); _state = State.Sb; }
                    break;
            }
        }
    }

    private static bool Supported(byte opt) => opt is OPT_BINARY or OPT_TTYPE or OPT_EOR or OPT_NEW_ENVIRON;

    private void HandleOption(byte verb, byte opt)
    {
        switch (verb)
        {
            case DO:
                if (Supported(opt)) { if (_weWill.Add(opt)) WriteRaw(IAC, WILL, opt); }
                else WriteRaw(IAC, WONT, opt);
                break;
            case DONT:
                if (_weWill.Remove(opt)) WriteRaw(IAC, WONT, opt);
                break;
            case WILL:
                if (opt is OPT_BINARY or OPT_EOR) { if (_theyWill.Add(opt)) WriteRaw(IAC, DO, opt); }
                else WriteRaw(IAC, DONT, opt);
                break;
            case WONT:
                if (_theyWill.Remove(opt)) WriteRaw(IAC, DONT, opt);
                break;
        }
    }

    private void HandleSubnegotiation(byte[] sb)
    {
        if (sb.Length < 2) return;
        if (sb[0] == OPT_TTYPE && sb[1] == TT_SEND)
        {
            var reply = new List<byte> { IAC, SB, OPT_TTYPE, TT_IS };
            reply.AddRange(Encoding.ASCII.GetBytes(_options.TerminalType));
            reply.AddRange(new[] { IAC, SE });
            WriteRaw(reply.ToArray());
        }
        else if (sb[0] == OPT_NEW_ENVIRON && sb[1] == ENV_SEND)
        {
            var reply = new List<byte> { IAC, SB, OPT_NEW_ENVIRON, ENV_IS };
            void Var(byte kind, string name, string value)
            {
                reply.Add(kind);
                AddEscaped(reply, Encoding.ASCII.GetBytes(name));
                reply.Add(ENV_VALUE);
                AddEscaped(reply, Encoding.ASCII.GetBytes(value));
            }
            if (!string.IsNullOrWhiteSpace(_options.DeviceName))
                Var(ENV_USERVAR, "DEVNAME", _options.DeviceName.ToUpperInvariant());
            var charset = Ebcdic.CharsetFor(_options.CodePage);
            if (charset != null)
            {
                Var(ENV_USERVAR, "KBDTYPE", "USB");
                Var(ENV_USERVAR, "CODEPAGE", _options.CodePage.ToString());
                Var(ENV_USERVAR, "CHARSET", charset);
            }
            reply.AddRange(new[] { IAC, SE });
            WriteRaw(reply.ToArray());
        }
    }

    private static void AddEscaped(List<byte> list, byte[] bytes)
    {
        foreach (var b in bytes)
        {
            if (b <= 3) list.Add(ENV_ESC);
            if (b == IAC) list.Add(IAC);
            list.Add(b);
        }
    }

    // ---- Sending ------------------------------------------------------------------------
    /// <summary>Send one 5250 record: escapes 0xFF and terminates with IAC EOR.</summary>
    public void SendRecord(byte[] record)
    {
        var outBytes = new List<byte>(record.Length + 8);
        foreach (var b in record)
        {
            outBytes.Add(b);
            if (b == IAC) outBytes.Add(IAC);
        }
        outBytes.Add(IAC);
        outBytes.Add(EOR);
        WriteRaw(outBytes.ToArray());
    }

    private void WriteRaw(params byte[] bytes)
    {
        if (RawWriter != null) { RawWriter(bytes); return; }
        lock (_writeLock)
        {
            try { _stream?.Write(bytes, 0, bytes.Length); _stream?.Flush(); }
            catch (Exception ex) { IsConnected = false; Disconnected?.Invoke(ex.Message); }
        }
    }

    public void Disconnect()
    {
        try { _cts?.Cancel(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        IsConnected = false;
    }

    public void Dispose() => Disconnect();
}
