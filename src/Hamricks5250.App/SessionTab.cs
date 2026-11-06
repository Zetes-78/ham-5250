using System.Media;
using System.Windows.Threading;
using Hamricks5250.Core;

namespace Hamricks5250.App;

/// <summary>One open session: the 5250 engine, its connection, and reconnect logic.</summary>
public sealed class SessionTab
{
    private readonly Dispatcher _ui;
    private TnConnection? _conn;
    private bool _userDisconnect;
    private int _reconnectAttempt;
    private DispatcherTimer? _reconnectTimer;

    public SessionProfile Profile { get; private set; }
    public int Number { get; }
    public Session5250 Session { get; private set; }
    public string DeviceName => Profile.ResolveDeviceName(Number);
    public bool IsTls => _conn?.IsTls == true;
    public string OverlayText { get; private set; } = "";
    public string Title => Profile.Name + (Number > 1 ? $" ({Number})" : "");

    public event Action? StateChanged;

    public SessionTab(SessionProfile profile, int number, Dispatcher ui)
    {
        Profile = profile;
        Number = number;
        _ui = ui;
        Session = CreateSession();
        OverlayText = string.IsNullOrWhiteSpace(profile.Host)
            ? "No IBM i host set. Open Settings to add one."
            : "Not connected. Press Enter to connect.";
    }

    private Session5250 CreateSession()
    {
        var s = new Session5250(Profile.CodePage, Profile.Wide);
        s.Alarm += () => _ui.InvokeAsync(() => SystemSounds.Beep.Play());
        return s;
    }

    public async Task ConnectAsync()
    {
        if (Session.Connected) return;
        if (string.IsNullOrWhiteSpace(Profile.Host))
        {
            SetOverlay("No IBM i host set. Open Settings to add one.");
            return;
        }
        _reconnectTimer?.Stop();
        _userDisconnect = false;
        _conn?.Dispose();

        var conn = new TnConnection();
        _conn = conn;
        conn.RecordReceived += rec => _ui.InvokeAsync(() => { if (_conn == conn) Session.ProcessRecord(rec); });
        conn.Disconnected += reason => _ui.InvokeAsync(() => { if (_conn == conn) OnDisconnected(reason); });
        Session.Send = conn.SendRecord;

        SetOverlay($"Connecting to {Profile.Host}...");
        try
        {
            Session.OnConnected();        // clear the screen and lock the keyboard until the host writes
            await conn.ConnectAsync(Profile.ToOptions(Number));
            _reconnectAttempt = 0;
            OverlayText = "";
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            if (_conn != conn) return;
            string why = conn.LastCertificateError != null
                ? $"The IBM i certificate isn't trusted:\n{conn.LastCertificateError}\nAsk IT to install the certificate or set its thumbprint in Settings."
                : ex.Message;
            Session.OnDisconnected(why);
            SetOverlay($"Couldn't connect to {Profile.Host}:{Profile.Port}\n{why}\n\nPress Enter to try again.");
            ScheduleReconnect();
        }
    }

    public void Disconnect()
    {
        _userDisconnect = true;
        _reconnectTimer?.Stop();
        _conn?.Disconnect();
        Session.OnDisconnected("Disconnected");
        SetOverlay("Disconnected. Press Enter to connect.");
    }

    private void OnDisconnected(string reason)
    {
        Session.OnDisconnected(reason);
        if (_userDisconnect) return;
        SetOverlay($"The connection to {Profile.Host} ended.\n{reason}\n\nPress Enter to reconnect.");
        ScheduleReconnect();
    }

    private void ScheduleReconnect()
    {
        if (!Profile.AutoReconnect || _userDisconnect) return;
        int[] delays = { 3, 10, 30, 60 };
        int delay = delays[Math.Min(_reconnectAttempt, delays.Length - 1)];
        _reconnectAttempt++;
        _reconnectTimer?.Stop();
        _reconnectTimer = new DispatcherTimer(DispatcherPriority.Normal, _ui) { Interval = TimeSpan.FromSeconds(delay) };
        _reconnectTimer.Tick += async (_, _) =>
        {
            _reconnectTimer?.Stop();
            if (!Session.Connected && !_userDisconnect) await ConnectAsync();
        };
        _reconnectTimer.Start();
        OverlayText += $"\nReconnecting automatically in {delay} seconds.";
        StateChanged?.Invoke();
    }

    /// <summary>Apply edited settings; reconnects if connection details changed.</summary>
    public async Task ApplyProfileAsync(SessionProfile profile)
    {
        bool changed = profile.Host != Profile.Host || profile.Port != Profile.Port || profile.UseTls != Profile.UseTls
                       || profile.CodePage != Profile.CodePage || profile.Wide != Profile.Wide
                       || profile.ResolveDeviceName(Number) != Profile.ResolveDeviceName(Number);
        Profile = profile;
        if (!changed) return;
        bool wasConnected = Session.Connected;
        Disconnect();
        var old = Session;
        Session = CreateSession();
        SessionReplaced?.Invoke(old, Session);
        if (wasConnected || profile.AutoConnect) await ConnectAsync();
    }

    public event Action<Session5250, Session5250>? SessionReplaced;

    private void SetOverlay(string text)
    {
        OverlayText = text;
        StateChanged?.Invoke();
    }

    public void Close()
    {
        _userDisconnect = true;
        _reconnectTimer?.Stop();
        _conn?.Dispose();
    }
}
