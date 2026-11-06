// Dependency-free test runner for the 5250 engine: `dotnet run --project tests/Hamricks5250.Tests`
// Exit code 0 = all passed. Runs in CI on every push.
using System.Net;
using System.Net.Sockets;
using Hamricks5250.Core;

int passed = 0, failed = 0;
void Check(bool ok, string what)
{
    if (ok) { passed++; Console.WriteLine($"  ok   {what}"); }
    else { failed++; Console.WriteLine($"  FAIL {what}"); }
}
void Test(string name, Action body)
{
    Console.WriteLine(name);
    try { body(); }
    catch (Exception ex) { failed++; Console.WriteLine($"  FAIL threw {ex.GetType().Name}: {ex.Message}"); }
}

var e37 = new Ebcdic(37);
byte[] E(string s) => e37.Encode(s);

// A GDS record from the host: header (opcode Put/Get) + data
byte[] Rec(params byte[][] parts)
{
    var data = parts.SelectMany(p => p).ToArray();
    int len = 10 + data.Length;
    var r = new byte[len];
    r[0] = (byte)(len >> 8); r[1] = (byte)len; r[2] = 0x12; r[3] = 0xA0; r[6] = 0x04; r[9] = 0x03;
    data.CopyTo(r, 10);
    return r;
}
byte[] B(params int[] v) => v.Select(x => (byte)x).ToArray();

// IBM i style sign-on screen: user field (monocase), password (non-display), a signed numeric field, Read MDT.
byte[] SignOn() => Rec(
    B(0x04, 0x40),                                   // Clear unit
    B(0x04, 0x11, 0x00, 0x18),                       // WTD, CC2 = unlock
    B(0x01, 0x07, 0x00, 0x00, 0x00, 0x18, 0x00, 0x00, 0x00), // SOH, error row 24
    B(0x11, 1, 35, 0x22), E("Sign On"), B(0x20),
    B(0x11, 6, 17, 0x20), E("User . . . ."),
    B(0x11, 6, 52), B(0x1D, 0x40, 0x20, 0x24, 0x00, 0x0A), // SF: FFW alpha + monocase, attr green UL, len 10
    B(0x11, 6, 63, 0x20),
    B(0x11, 7, 17), E("Password"),
    B(0x11, 7, 52), B(0x1D, 0x40, 0x00, 0x27, 0x00, 0x0A), // SF: non-display
    B(0x11, 7, 63, 0x20),
    B(0x11, 8, 17), E("Amount"),
    B(0x11, 8, 52), B(0x1D, 0x47, 0x00, 0x24, 0x00, 0x06), // SF: signed numeric, len 6
    B(0x11, 8, 59, 0x20),
    B(0x11, 24, 60, 0x2A), E("F3=Exit"), B(0x20),
    B(0x13, 6, 53),                                  // IC: cursor to user field
    B(0x04, 0x52, 0x00, 0x00));                       // Read MDT fields

Session5250 NewSession(out List<byte[]> sent)
{
    var s = new Session5250(37);
    var list = new List<byte[]>();
    s.Send = r => list.Add(r);
    s.OnConnected();
    sent = list;
    return s;
}

Test("EBCDIC code page 37", () =>
{
    Check(e37.Encode('A') == 0xC1 && e37.Encode('a') == 0x81 && e37.Encode('0') == 0xF0 && e37.Encode(' ') == 0x40, "A/a/0/space encode");
    Check(e37.Display(0xC1) == 'A' && e37.Display(0x5B) == '$' && e37.Display(0x7C) == '@', "decode A $ @");
    Check(e37.Display(0x22) == ' ' && e37.Display(0x1C) == '*', "attributes blank, DUP shows *");
});

Test("Sign-on screen renders and builds the format table", () =>
{
    var s = NewSession(out _);
    s.ProcessRecord(SignOn());
    var text = s.Screen.Text(s.Ebcdic);
    Check(s.Screen.RowText(0, s.Ebcdic).Contains("Sign On"), "title on row 1");
    Check(s.Screen.RowText(5, s.Ebcdic).Contains("User . . . ."), "label on row 6");
    Check(s.Screen.Fields.Count == 3, $"3 input fields (got {s.Screen.Fields.Count})");
    Check(s.Screen.CursorRow == 5 && s.Screen.CursorCol == 52, "IC put cursor in user field");
    Check(!s.InputInhibited, "keyboard unlocked by Read");
    Check(s.Screen.ErrorRow == 24, "error row from SOH");
    var styles = s.Screen.Styles();
    Check(styles[s.Screen.Pos(0, 36)].Color == CellColor.White, "title drawn white (0x22)");
    Check(styles[s.Screen.Pos(5, 53)].Flags.HasFlag(CellFlags.Underline), "input field underlined");
    Check(styles[s.Screen.Pos(6, 53)].Flags.HasFlag(CellFlags.NonDisplay), "password non-display");
    Check(styles[s.Screen.Pos(23, 61)].Color == CellColor.Red, "F3=Exit in red (0x2A)");
});

Test("Typing, monocase, Field Exit and Enter send the right record", () =>
{
    var s = NewSession(out var sent);
    s.ProcessRecord(SignOn());
    foreach (var c in "jscates") s.TypeChar(c);
    Check(s.Ebcdic.Decode(s.Screen.FieldBytes(s.Screen.Fields[0])).TrimEnd() == "JSCATES", "monocase uppercased");
    s.FieldExit();
    Check(s.Screen.Cursor == s.Screen.Fields[1].Start, "Field Exit -> password field");
    foreach (var c in "Pw1") s.TypeChar(c);
    s.SendAid(Aid.Enter);
    Check(sent.Count == 1, "one record sent");
    var r = sent[0];
    Check(r[2] == 0x12 && r[3] == 0xA0 && r[9] == 0x03, "GDS header, opcode Put/Get");
    Check(r[10] == 7 && r[11] == 56 && r[12] == Aid.Enter, "cursor 7/56 + Enter AID");
    var expect = B(0x11, 6, 53).Concat(E("JSCATES")).Concat(B(0x11, 7, 53)).Concat(E("Pw1")).ToArray();
    Check(r.Skip(13).SequenceEqual(expect), "MDT fields: SBA+USER, SBA+password, trailing nulls stripped");
    Check(s.InputInhibited && s.InhibitReason == "SYSTEM", "keyboard locked waiting on host");
});

Test("Type-ahead while the host is busy", () =>
{
    var s = NewSession(out var sent);
    s.ProcessRecord(SignOn());
    s.SendAid(Aid.Enter);
    s.TypeChar('x');                                   // keyboard locked: buffered
    s.ProcessRecord(SignOn());                         // host answers, unlocks
    Check(s.Ebcdic.Decode(s.Screen.FieldBytes(s.Screen.Fields[0])).TrimEnd() == "X", "buffered key applied after unlock");
});

Test("Signed numeric field with Field-", () =>
{
    var s = NewSession(out var sent);
    s.ProcessRecord(SignOn());
    s.Screen.Cursor = s.Screen.Fields[2].Start;
    foreach (var c in "125") s.TypeChar(c);
    s.TypeChar('a');
    Check(s.Message.Contains("Numeric"), "letters rejected in numeric field");
    s.FieldExit(minus: true);
    var f = s.Screen.Fields[2];
    Check(s.Ebcdic.Decode(s.Screen.FieldBytes(f).AsSpan(0, f.Length - 1)).Replace(" ", "") == "125", "digits kept");
    Check(s.Screen.Buffer[f.End] == 0x60, "sign position holds '-'");
    s.SendAid(Aid.Enter);
    var data = sent[0].Skip(13).ToArray();
    // SBA 8,53 + 5 digit positions, trailing nulls stripped, last digit zone D (negative)
    Check(data[0] == 0x11 && data[1] == 8 && data[2] == 53, "SBA for amount field");
    Check(data.Skip(3).SequenceEqual(B(0xF1, 0xF2, 0xD5)), "125- sent as F1 F2 D5");
});

Test("Right-adjust zero fill on Field Exit", () =>
{
    var s = NewSession(out _);
    s.ProcessRecord(Rec(B(0x04, 0x40), B(0x04, 0x11, 0x00, 0x08),
        B(0x11, 2, 10), B(0x1D, 0x43, 0x05, 0x24, 0x00, 0x05), B(0x11, 2, 16, 0x20),
        B(0x04, 0x52, 0x00, 0x00)));
    s.TypeChar('4'); s.TypeChar('2');
    s.FieldExit();
    Check(s.Ebcdic.Decode(s.Screen.FieldBytes(s.Screen.Fields[0])) == "00042", "42 -> 00042");
});

Test("Protected area and Write Error Code / Reset", () =>
{
    var s = NewSession(out _);
    s.ProcessRecord(SignOn());
    s.SetCursor(0, 0);
    s.TypeChar('z');
    Check(s.Message.Contains("protected"), "typing outside a field refused");
    var before = s.Screen.RowText(23, s.Ebcdic);
    s.ProcessRecord(Rec(B(0x04, 0x21, 0x22), E("CPF1107 Password not correct")));
    Check(s.ErrorState && s.InputInhibited, "error state locks keyboard");
    Check(s.Screen.RowText(23, s.Ebcdic).Contains("Password not correct"), "error text on error row");
    s.ResetKey();
    Check(!s.ErrorState && !s.InputInhibited, "Reset unlocks");
    Check(s.Screen.RowText(23, s.Ebcdic) == before, "Reset restores the error row");
});

Test("Help / Print send no field data; PF keys do", () =>
{
    var s = NewSession(out var sent);
    s.ProcessRecord(SignOn());
    s.TypeChar('q');
    s.SendAid(Aid.Help);
    Check(sent[^1].Length == 13 && sent[^1][12] == Aid.Help, "Help = cursor + AID only");
    s.ProcessRecord(SignOn());
    s.TypeChar('q');
    s.SendAid(Aid.Function(3));
    Check(sent[^1][12] == 0x33 && sent[^1].Length > 13, "F3 = 0x33 with data");
    Check(Aid.Function(13) == 0xB1 && Aid.Function(24) == 0xBC, "F13-F24 codes");
});

Test("Query reply", () =>
{
    var s = NewSession(out var sent);
    s.ProcessRecord(Rec(B(0x04, 0xF3, 0x00, 0x05, 0xD9, 0x70, 0x00)));
    Check(sent.Count == 1, "reply sent");
    var r = sent[0];
    Check(r.Length == 71 && r[12] == 0x88 && r[13] == 0 && r[14] == 0x3A && r[15] == 0xD9 && r[16] == 0x70, "inbound WSF query reply, 58-byte body");
    Check(s.Ebcdic.Decode(r.AsSpan(40, 7)) == "3179002", "device 3179 model 002");
});

Test("Save screen / restore screen round trip", () =>
{
    var s = NewSession(out var sent);
    s.ProcessRecord(SignOn());
    s.TypeChar('a');
    var bufferBefore = (byte[])s.Screen.Buffer.Clone();
    var fieldsBefore = s.Screen.Fields.Select(f => (f.Start, f.Length, f.Ffw1, f.Attribute)).ToList();
    s.ProcessRecord(Rec(B(0x04, 0x02)));
    var saved = sent[^1].Skip(10).ToArray();
    s.ProcessRecord(Rec(B(0x04, 0x40)));            // host clears the screen (e.g. Sys Req menu)
    s.ProcessRecord(Rec(saved));                     // host restores what we saved
    Check(s.Screen.Buffer.SequenceEqual(bufferBefore), "buffer identical after restore");
    Check(s.Screen.Fields.Select(f => (f.Start, f.Length, f.Ffw1, f.Attribute)).SequenceEqual(fieldsBefore), "format table identical (incl. MDT)");
});

Test("Clear unit alternate (27x132) and roll", () =>
{
    var wide = new Session5250(37, wideCapable: true);
    wide.OnConnected();
    wide.ProcessRecord(Rec(B(0x04, 0x20, 0x00), B(0x04, 0x11, 0x00, 0x08, 0x11, 27, 120), E("WIDE")));
    Check(wide.Screen.Rows == 27 && wide.Screen.Cols == 132, "27x132");
    Check(wide.Screen.RowText(26, wide.Ebcdic).Contains("WIDE"), "text on row 27");
    var s = NewSession(out _);
    s.ProcessRecord(Rec(B(0x04, 0x40), B(0x04, 0x11, 0, 0, 0x11, 3, 1), E("LINE3"), B(0x11, 4, 1), E("LINE4")));
    s.ProcessRecord(Rec(B(0x04, 0x23, 0x01, 3, 10)));   // roll up 1 line between rows 3-10
    Check(s.Screen.RowText(2, s.Ebcdic).StartsWith("LINE4"), "roll up moved row 4 to row 3");
});

Test("Repeat to address and erase to address", () =>
{
    var s = NewSession(out _);
    s.ProcessRecord(Rec(B(0x04, 0x40), B(0x04, 0x11, 0, 0, 0x11, 2, 1, 0x02, 2, 80, 0x60)));
    Check(s.Screen.RowText(1, s.Ebcdic) == new string('-', 80), "RA drew a full line of dashes");
    s.ProcessRecord(Rec(B(0x04, 0x11, 0, 0, 0x11, 2, 11, 0x03, 2, 20, 0x01)));
    Check(s.Screen.RowText(1, s.Ebcdic).Substring(10, 10).Trim() == "", "EA cleared columns 11-20");
});

Test("Telnet negotiation (RFC 4777)", () =>
{
    var conn = new TnConnection();
    var outBytes = new List<byte>();
    conn.RawWriter = b => outBytes.AddRange(b);
    typeof(TnConnection).GetField("_options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
        .SetValue(conn, new ConnectionOptions { DeviceName = "HGAFOFF014", CodePage = 37 });
    conn.Feed(B(255, 253, 39));                       // DO NEW-ENVIRON
    Check(outBytes.SequenceEqual(B(255, 251, 39)), "WILL NEW-ENVIRON");
    outBytes.Clear();
    conn.Feed(B(255, 250, 39, 1, 255, 240));          // SB NEW-ENVIRON SEND SE
    var env = System.Text.Encoding.ASCII.GetString(outBytes.ToArray());
    Check(outBytes.Take(4).SequenceEqual(B(255, 250, 39, 0)) && env.Contains("DEVNAME") && env.Contains("HGAFOFF014") && env.Contains("697"), "IS USERVAR DEVNAME/CODEPAGE/CHARSET");
    outBytes.Clear();
    conn.Feed(B(255, 253, 24, 255, 250, 24, 1, 255, 240));   // DO TTYPE, SB TTYPE SEND
    Check(System.Text.Encoding.ASCII.GetString(outBytes.ToArray()).Contains("IBM-3179-2"), "terminal type IBM-3179-2");
    outBytes.Clear();
    conn.Feed(B(255, 253, 25, 255, 251, 25, 255, 253, 0, 255, 251, 0));
    Check(outBytes.SequenceEqual(B(255, 251, 25, 255, 253, 25, 255, 251, 0, 255, 253, 0)), "EOR + BINARY both ways");
    outBytes.Clear();
    conn.Feed(B(255, 253, 1));                         // DO ECHO -> WONT
    Check(outBytes.SequenceEqual(B(255, 252, 1)), "refuses unsupported options");
    byte[]? got = null;
    conn.RecordReceived += r => got = r;
    conn.Feed(B(1, 255, 255, 2, 255, 239));
    Check(got != null && got.SequenceEqual(B(1, 255, 2)), "IAC IAC unescaped, record ends at IAC EOR");
    outBytes.Clear();
    conn.SendRecord(B(9, 255, 9));
    Check(outBytes.SequenceEqual(B(9, 255, 255, 9, 255, 239)), "outbound 0xFF doubled + IAC EOR");
});

Test("End to end over a real socket with a simulated IBM i", () =>
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var hostGot = new TaskCompletionSource<byte[]>();
    var hostTask = Task.Run(async () =>
    {
        using var client = await listener.AcceptTcpClientAsync();
        var ns = client.GetStream();
        async Task Send(byte[] b) => await ns.WriteAsync(b);
        var buf = new byte[4096];
        async Task<byte[]> Read() { int n = await ns.ReadAsync(buf); return buf[..n]; }
        await Send(B(255, 253, 39)); await Read();
        await Send(B(255, 250, 39, 1, 255, 240)); var env = await Read();
        await Send(B(255, 253, 24)); await Read();
        await Send(B(255, 250, 24, 1, 255, 240)); var tt = await Read();
        await Send(B(255, 253, 25, 255, 251, 25, 255, 253, 0, 255, 251, 0)); await Read();
        var screen = SignOn();
        await Send(screen.Concat(B(255, 239)).ToArray());
        // collect the client's AID record
        var all = new List<byte>();
        while (!(all.Count >= 2 && all[^2] == 255 && all[^1] == 239)) all.AddRange(await Read());
        hostGot.SetResult(all.ToArray());
    });

    var s = new Session5250(37);
    var conn = new TnConnection();
    var screenShown = new ManualResetEventSlim();
    conn.RecordReceived += r => { lock (s) { s.ProcessRecord(r); } if (!s.InputInhibited) screenShown.Set(); };
    s.Send = conn.SendRecord;
    conn.ConnectAsync(new ConnectionOptions { Host = "127.0.0.1", Port = port, UseTls = false, DeviceName = "HTEST" }).Wait(5000);
    s.OnConnected();
    Check(screenShown.Wait(5000), "sign-on screen received through telnet");
    lock (s) { foreach (var c in "qsecofr") s.TypeChar(c); s.SendAid(Aid.Enter); }
    Check(hostGot.Task.Wait(5000), "host received the Enter record");
    var rec = hostGot.Task.Result;
    Check(rec[2] == 0x12 && rec[3] == 0xA0 && rec[12] == Aid.Enter && e37.Decode(rec.AsSpan(16, 7)) == "QSECOFR", "record = header + cursor + Enter + SBA + QSECOFR");
    conn.Dispose();
    listener.Stop();
});

Test("Device names", () =>
{
    var p = new SessionProfile { DevicePrefix = "H" };
    var n1 = p.ResolveDeviceName(1);
    Check(n1.Length <= 10 && n1.StartsWith("H") && n1.All(char.IsLetterOrDigit), $"auto name '{n1}' valid");
    p.DeviceName = "GAFWHSE0012";
    Check(p.ResolveDeviceName(1) == "GAFWHSE001" && p.ResolveDeviceName(2) == "GAFWHSE002", "10-char limit and per-tab suffix");
});

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
