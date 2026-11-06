namespace Hamricks5250.Core;

/// <summary>
/// One 5250 display session: interprets the host data stream into the <see cref="Screen"/>,
/// and turns keystrokes into field edits and AID responses.
/// Not thread-safe: call everything (including <see cref="ProcessRecord"/>) from one thread.
/// </summary>
public sealed class Session5250
{
    // GDS record header
    private const byte OpNoOp = 0, OpInvite = 1, OpOutputOnly = 2, OpPutGet = 3, OpSaveScreen = 4,
                       OpRestoreScreen = 5, OpReadImmediate = 6, OpReadScreen = 8, OpCancelInvite = 10,
                       OpMessageOn = 11, OpMessageOff = 12;
    private const byte FlagAttention = 0x40, FlagSysReq = 0x04;

    // Commands (after ESC 0x04)
    private const byte ESC = 0x04;
    private const byte CmdClearUnit = 0x40, CmdClearUnitAlt = 0x20, CmdClearFormatTable = 0x50,
                       CmdWriteToDisplay = 0x11, CmdWriteErrorCode = 0x21, CmdWriteErrorCodeWindow = 0x22,
                       CmdReadInputFields = 0x42, CmdReadMdtFields = 0x52, CmdReadMdtFieldsAlt = 0x82,
                       CmdReadImmediate = 0x72, CmdReadImmediateAlt = 0x83, CmdReadScreen = 0x62,
                       CmdReadScreenPrint = 0x66, CmdSaveScreen = 0x02, CmdSavePartialScreen = 0x03,
                       CmdRestoreScreen = 0x12, CmdRestorePartialScreen = 0x13, CmdRoll = 0x23,
                       CmdWriteStructuredField = 0xF3;

    // Orders inside Write To Display
    private const byte OrdSOH = 0x01, OrdRA = 0x02, OrdEA = 0x03, OrdTD = 0x10, OrdSBA = 0x11, OrdWEA = 0x12,
                       OrdIC = 0x13, OrdMC = 0x14, OrdWDSF = 0x15, OrdSF = 0x1D;

    public Screen Screen { get; } = new();
    public Ebcdic Ebcdic { get; }
    public bool WideCapable { get; }

    /// <summary>Called with each complete outbound record (header + data, unescaped).</summary>
    public Action<byte[]>? Send { get; set; }

    public bool Connected { get; set; }
    public bool InputInhibited { get; private set; } = true;
    public string InhibitReason { get; private set; } = "";
    public bool InsertMode { get; private set; }
    public bool MessageWaiting { get; private set; }
    public bool ErrorState { get; private set; }
    /// <summary>Short status message for the status bar (key errors etc.).</summary>
    public string Message { get; private set; } = "";

    public event Action? Changed;
    public event Action? Alarm;

    private byte _readOp = CmdReadMdtFields;
    private byte[]? _savedErrorLine;
    private readonly Queue<Action> _typeAhead = new();
    private int _addr;

    public Session5250(int codePage = 37, bool wideCapable = false)
    {
        Ebcdic = new Ebcdic(codePage);
        WideCapable = wideCapable;
        Screen.Reset(24, 80);
    }

    // =====================================================================================
    // Inbound (host -> us)
    // =====================================================================================

    public void ProcessRecord(byte[] rec)
    {
        if (rec.Length < 10) return;
        int varLen = rec[6];
        byte opcode = rec[6 + varLen - 1];   // last byte of the variable header
        int i = 6 + varLen;

        switch (opcode)
        {
            case OpCancelInvite:
                SendRecord(0, OpCancelInvite, Array.Empty<byte>());
                break;
            case OpMessageOn: MessageWaiting = true; break;
            case OpMessageOff: MessageWaiting = false; break;
        }

        ProcessCommands(rec, i);
        Changed?.Invoke();
    }

    private void ProcessCommands(byte[] d, int i)
    {
        while (i < d.Length)
        {
            if (d[i] != ESC) { i++; continue; }
            if (i + 1 >= d.Length) return;
            byte cmd = d[i + 1];
            i += 2;
            switch (cmd)
            {
                case CmdClearUnit:
                    ClearUnit(24, 80);
                    break;
                case CmdClearUnitAlt:
                    {
                        byte p = i < d.Length ? d[i++] : (byte)0x80;
                        if (p == 0x00 && WideCapable) ClearUnit(27, 132); else ClearUnit(24, 80);
                        break;
                    }
                case CmdClearFormatTable:
                    Screen.Fields.Clear();
                    break;
                case CmdWriteToDisplay:
                    {
                        if (i + 1 >= d.Length) return;
                        byte cc1 = d[i], cc2 = d[i + 1];
                        i += 2;
                        ApplyCc1(cc1);
                        i = WriteOrders(d, i, errorLine: false);
                        ApplyCc2(cc2);
                        break;
                    }
                case CmdWriteErrorCode:
                case CmdWriteErrorCodeWindow:
                    i = WriteErrorCode(d, i);
                    break;
                case CmdReadInputFields:
                case CmdReadMdtFields:
                case CmdReadMdtFieldsAlt:
                    {
                        byte cc1 = i < d.Length ? d[i] : (byte)0, cc2 = i + 1 < d.Length ? d[i + 1] : (byte)0;
                        i += 2;
                        ApplyCc1(cc1);
                        _readOp = cmd;
                        ApplyCc2(cc2);
                        UnlockKeyboard();
                        break;
                    }
                case CmdReadImmediate:
                case CmdReadImmediateAlt:
                    SendFields(Aid.None, cmd == CmdReadImmediate ? CmdReadInputFields : CmdReadMdtFieldsAlt, includeCursor: true);
                    break;
                case CmdReadScreen:
                case CmdReadScreenPrint:
                    SendRecord(0, OpPutGet, (byte[])Screen.Buffer.Clone());
                    break;
                case CmdSaveScreen:
                case CmdSavePartialScreen:
                    SendRecord(0, OpPutGet, BuildSaveScreen());
                    break;
                case CmdRestoreScreen:
                case CmdRestorePartialScreen:
                    // The saved data stream follows; it is processed as ordinary commands.
                    break;
                case CmdRoll:
                    if (i + 2 < d.Length) Roll(d[i], d[i + 1], d[i + 2]);
                    i += 3;
                    break;
                case CmdWriteStructuredField:
                    i = WriteStructuredField(d, i);
                    break;
                default:
                    return; // unknown command: stop rather than misread the rest
            }
        }
    }

    private void ClearUnit(int rows, int cols)
    {
        Screen.Reset(rows, cols);
        _addr = 0;
        ErrorState = false;
        _savedErrorLine = null;
        InsertMode = false;
    }

    private void ApplyCc1(byte cc1)
    {
        switch (cc1 & 0xE0)
        {
            case 0x40: foreach (var f in Screen.Fields.Where(f => !f.Bypass)) f.Mdt = false; break;
            case 0x60: foreach (var f in Screen.Fields) f.Mdt = false; break;
            case 0x80: foreach (var f in Screen.Fields.Where(f => !f.Bypass && f.Mdt)) NullField(f); break;
            case 0xA0:
                foreach (var f in Screen.Fields.Where(f => !f.Bypass)) { if (f.Mdt) NullField(f); f.Mdt = false; }
                break;
            case 0xC0:
                foreach (var f in Screen.Fields.Where(f => !f.Bypass)) { NullField(f); f.Mdt = false; }
                break;
            case 0xE0:
                foreach (var f in Screen.Fields) { if (!f.Bypass) NullField(f); f.Mdt = false; }
                break;
        }
    }

    private void ApplyCc2(byte cc2)
    {
        if ((cc2 & 0x04) != 0) Alarm?.Invoke();
        if ((cc2 & 0x01) != 0) MessageWaiting = true;
        if ((cc2 & 0x02) != 0) MessageWaiting = false;
        if ((cc2 & 0x08) != 0) UnlockKeyboard();
    }

    private void NullField(Field f)
    {
        for (int p = f.Start; p <= f.End; p++) Screen.Buffer[Screen.Wrap(p)] = 0x00;
    }

    /// <summary>Process orders and data until the next command; returns the new index.</summary>
    private int WriteOrders(byte[] d, int i, bool errorLine)
    {
        var s = Screen;
        int? insertCursor = null;
        if (errorLine) _addr = s.Pos(s.ErrorRow - 1, 0);
        else _addr = s.Wrap(_addr);

        while (i < d.Length && d[i] != ESC)
        {
            byte b = d[i++];
            switch (b)
            {
                case OrdSOH:
                    {
                        int n = i < d.Length ? d[i++] : 0;
                        if (n >= 4 && i + 3 < d.Length && d[i + 3] >= 1 && d[i + 3] <= s.Rows) s.ErrorRow = d[i + 3];
                        i += n;
                        break;
                    }
                case OrdRA:
                    {
                        if (i + 2 >= d.Length) return d.Length;
                        int end = s.HostPos(d[i], d[i + 1]);
                        byte ch = d[i + 2];
                        i += 3;
                        Fill(end, ch);
                        break;
                    }
                case OrdEA:
                    {
                        if (i + 2 >= d.Length) return d.Length;
                        int end = s.HostPos(d[i], d[i + 1]);
                        int len = d[i + 2];
                        i += 2 + Math.Max(len, 1);
                        Fill(end, 0x00);
                        break;
                    }
                case OrdTD:
                    {
                        if (i + 1 >= d.Length) return d.Length;
                        int len = (d[i] << 8) | d[i + 1];
                        i += 2;
                        for (int k = 0; k < len && i < d.Length; k++) Put(d[i++]);
                        break;
                    }
                case OrdSBA:
                    if (i + 1 >= d.Length) return d.Length;
                    _addr = s.HostPos(d[i], d[i + 1]);
                    i += 2;
                    break;
                case OrdWEA:
                    i += 2;
                    break;
                case OrdIC:
                    if (i + 1 >= d.Length) return d.Length;
                    insertCursor = s.HostPos(d[i], d[i + 1]);
                    i += 2;
                    break;
                case OrdMC:
                    if (i + 1 >= d.Length) return d.Length;
                    s.Cursor = s.HostPos(d[i], d[i + 1]);
                    i += 2;
                    break;
                case OrdWDSF:
                    {
                        if (i + 1 >= d.Length) return d.Length;
                        int len = (d[i] << 8) | d[i + 1];
                        i += Math.Max(len, 2);
                        break;
                    }
                case OrdSF:
                    i = StartField(d, i);
                    break;
                default:
                    Put(b);
                    break;
            }
        }

        if (insertCursor.HasValue) s.Cursor = insertCursor.Value;
        else if (!errorLine) s.Cursor = s.FirstEnterable()?.Start ?? s.Cursor;
        return i;
    }

    private int StartField(byte[] d, int i)
    {
        var s = Screen;
        var field = new Field();
        bool input = false;
        if (i < d.Length && (d[i] & 0xC0) == 0x40)      // Field Format Word
        {
            if (i + 1 >= d.Length) return d.Length;
            field.Ffw1 = d[i];
            field.Ffw2 = d[i + 1];
            input = true;
            i += 2;
            while (i + 1 < d.Length && (d[i] & 0x80) != 0)   // Field Control Words
            {
                field.Fcws.Add((ushort)((d[i] << 8) | d[i + 1]));
                i += 2;
            }
        }
        if (i >= d.Length) return i;
        byte attr = d[i++];
        if (i + 1 >= d.Length) { Put(attr); return d.Length; }
        int len = (d[i] << 8) | d[i + 1];
        i += 2;

        field.Attribute = attr;
        int attrPos = _addr;
        Put(attr);                     // the attribute occupies the position before the field
        field.Start = s.Wrap(attrPos + 1);
        field.Length = Math.Max(len, 0);
        if (input && field.Length > 0) s.AddField(field);
        return i;
    }

    private void Put(byte b)
    {
        Screen.Buffer[_addr] = b;
        _addr = Screen.Wrap(_addr + 1);
    }

    private void Fill(int endInclusive, byte ch)
    {
        int guard = Screen.Size;
        while (guard-- > 0)
        {
            bool last = _addr == endInclusive;
            Put(ch);
            if (last) break;
        }
    }

    private int WriteErrorCode(byte[] d, int i)
    {
        var s = Screen;
        int rowStart = s.Pos(s.ErrorRow - 1, 0);
        _savedErrorLine ??= s.Buffer.AsSpan(rowStart, s.Cols).ToArray();
        int savedCursor = s.Cursor;
        i = WriteOrders(d, i, errorLine: true);
        s.Cursor = savedCursor;
        ErrorState = true;
        InputInhibited = true;
        InhibitReason = "Error - press Reset";
        Alarm?.Invoke();
        return i;
    }

    private void Roll(byte ctl, byte top, byte bottom)
    {
        var s = Screen;
        int lines = ctl & 0x1F;
        bool down = (ctl & 0x80) != 0;
        int t = Math.Clamp(top - 1, 0, s.Rows - 1), b = Math.Clamp(bottom - 1, 0, s.Rows - 1);
        if (lines == 0 || b < t) return;
        var rows = Enumerable.Range(t, b - t + 1).Select(r => s.Buffer.AsSpan(r * s.Cols, s.Cols).ToArray()).ToList();
        for (int r = t; r <= b; r++)
        {
            int src = down ? r - lines : r + lines;
            var row = src >= t && src <= b ? rows[src - t] : new byte[s.Cols];
            row.CopyTo(s.Buffer, r * s.Cols);
        }
    }

    private int WriteStructuredField(byte[] d, int i)
    {
        if (i + 1 >= d.Length) return d.Length;
        int len = (d[i] << 8) | d[i + 1];
        if (len < 2) return d.Length;
        byte cls = i + 2 < d.Length ? d[i + 2] : (byte)0;
        byte type = i + 3 < d.Length ? d[i + 3] : (byte)0;
        if (cls == 0xD9 && type == 0x70) SendQueryReply();
        return i + len;
    }

    // =====================================================================================
    // Outbound (us -> host)
    // =====================================================================================

    private void SendRecord(byte flags, byte opcode, byte[] data)
    {
        int len = 10 + data.Length;
        var rec = new byte[len];
        rec[0] = (byte)(len >> 8);
        rec[1] = (byte)len;
        rec[2] = 0x12;
        rec[3] = 0xA0;
        rec[6] = 0x04;
        rec[7] = flags;
        rec[9] = opcode;
        data.CopyTo(rec, 10);
        Send?.Invoke(rec);
    }

    private void SendQueryReply()
    {
        var r = new byte[61];
        r[2] = Aid.InboundWsf;
        r[3] = 0x00; r[4] = 0x3A;        // length of the query reply (58)
        r[5] = 0xD9; r[6] = 0x70; r[7] = 0x80;
        r[8] = 0x06; r[9] = 0x00;        // controller hardware class: other 5250 emulator
        r[10] = 0x01; r[11] = 0x01; r[12] = 0x00;  // code level
        r[29] = 0x01;                    // display emulation
        var dev = Ebcdic.Encode(WideCapable ? "3477FC " : "3179002");
        Array.Copy(dev, 0, r, 30, 7);    // device type (4) + model (3)
        r[37] = 0x02;                    // standard keyboard
        r[41] = 0x24; r[42] = 0x24;      // display serial number
        r[44] = 0x01; r[45] = 0x00;      // max 256 input fields
        r[49] = 0x01; r[50] = 0x11;      // controller/display capability
        SendRecord(0, OpNoOp, r);
    }

    private byte[] BuildSaveScreen()
    {
        var s = Screen;
        var o = new List<byte> { ESC, CmdRestoreScreen };
        if (s.Rows == 27) o.AddRange(new byte[] { ESC, CmdClearUnitAlt, 0x00 });
        else o.AddRange(new byte[] { ESC, CmdClearUnit });
        o.AddRange(new byte[] { ESC, CmdWriteToDisplay, 0x00, 0x00, OrdSBA, 1, 1 });
        var fieldByAttr = s.Fields.ToDictionary(f => s.Wrap(f.Start - 1));
        for (int p = 0; p < s.Size; p++)
        {
            if (fieldByAttr.TryGetValue(p, out var f))
            {
                o.Add(OrdSF); o.Add(f.Ffw1); o.Add(f.Ffw2);
                foreach (var w in f.Fcws) { o.Add((byte)(w >> 8)); o.Add((byte)w); }
                o.Add(f.Attribute); o.Add((byte)(f.Length >> 8)); o.Add((byte)f.Length);
                continue;
            }
            byte b = s.Buffer[p];
            // Never emit a byte that would be read back as an order or command.
            if (b is ESC or OrdSOH or OrdRA or OrdEA or OrdTD or OrdSBA or OrdWEA or OrdIC or OrdMC or OrdWDSF or OrdSF) b = 0x00;
            o.Add(b);
        }
        o.Add(OrdIC); o.Add((byte)(s.CursorRow + 1)); o.Add((byte)(s.CursorCol + 1));
        return o.ToArray();
    }

    private void SendFields(byte aid, byte readOp, bool includeCursor)
    {
        var s = Screen;
        var data = new List<byte>();
        if (includeCursor) { data.Add((byte)(s.CursorRow + 1)); data.Add((byte)(s.CursorCol + 1)); data.Add(aid); }

        if (!Aid.SendsNoData(aid))
        {
            bool mdtOnly = readOp != CmdReadInputFields;
            bool anyMdt = s.Fields.Any(f => f.Mdt);
            foreach (var f in s.Fields)
            {
                if (mdtOnly && !f.Mdt) continue;
                if (!mdtOnly && !anyMdt) break;
                var bytes = FieldDataForHost(f);
                if (mdtOnly)
                {
                    int n = bytes.Length;
                    while (n > 0 && bytes[n - 1] == 0x00) n--;
                    bytes = bytes[..n];
                    if (readOp == CmdReadMdtFields) for (int k = 0; k < bytes.Length; k++) if (bytes[k] == 0x00) bytes[k] = 0x40;
                    data.Add(OrdSBA);
                    data.Add((byte)(f.Start / s.Cols + 1));
                    data.Add((byte)(f.Start % s.Cols + 1));
                }
                else
                {
                    for (int k = 0; k < bytes.Length; k++) if (bytes[k] == 0x00) bytes[k] = 0x40;
                }
                data.AddRange(bytes);
            }
        }
        SendRecord(0, OpPutGet, data.ToArray());
    }

    private byte[] FieldDataForHost(Field f)
    {
        var bytes = Screen.FieldBytes(f);
        if (!f.IsSigned || bytes.Length < 2) return bytes;
        bool negative = bytes[^1] == 0x60;   // '-' in the sign position
        var digits = bytes[..^1];
        if (negative)
        {
            int last = Array.FindLastIndex(digits, b => b >= 0xF0 && b <= 0xF9);
            if (last >= 0) digits[last] = (byte)(0xD0 | (digits[last] & 0x0F));
        }
        return digits;
    }

    // =====================================================================================
    // Keyboard
    // =====================================================================================

    private void UnlockKeyboard()
    {
        if (ErrorState) return;
        InputInhibited = false;
        InhibitReason = "";
        while (_typeAhead.Count > 0 && !InputInhibited) _typeAhead.Dequeue()();
    }

    /// <summary>Locks the keyboard while we wait on the host (used on connect).</summary>
    public void LockForHost(string reason = "SYSTEM")
    {
        InputInhibited = true;
        InhibitReason = reason;
    }

    private bool Ready(Action retry)
    {
        if (!Connected) return false;
        if (ErrorState) { Alarm?.Invoke(); return false; }
        if (InputInhibited)
        {
            if (_typeAhead.Count < 256) _typeAhead.Enqueue(retry);
            return false;
        }
        Message = "";
        return true;
    }

    private void KeyError(string msg)
    {
        Message = msg;
        Alarm?.Invoke();
        Changed?.Invoke();
    }

    public void SendAid(byte aid)
    {
        if (!Ready(() => SendAid(aid))) return;
        if (aid == Aid.Enter || aid == Aid.RollUp || aid == Aid.RollDown || (aid >= 0x31 && aid <= 0x3C) || (aid >= 0xB1 && aid <= 0xBC))
        {
            var missing = Screen.Fields.FirstOrDefault(f => f.Enterable && f.MandatoryEnter && !f.Mdt);
            if (missing != null && aid == Aid.Enter)
            {
                Screen.Cursor = missing.Start;
                KeyError("Field requires data");
                return;
            }
        }
        SendFields(aid, _readOp, includeCursor: true);
        InsertMode = false;
        LockForHost();
        Changed?.Invoke();
    }

    public void Attention()
    {
        if (!Connected) return;
        SendRecord(FlagAttention, OpNoOp, Array.Empty<byte>());
        LockForHost();
        Changed?.Invoke();
    }

    public void SystemRequest()
    {
        if (!Connected) return;
        SendRecord(FlagSysReq, OpNoOp, Array.Empty<byte>());
        LockForHost();
        Changed?.Invoke();
    }

    public void ResetKey()
    {
        if (ErrorState)
        {
            var s = Screen;
            if (_savedErrorLine != null) _savedErrorLine.CopyTo(s.Buffer, s.Pos(s.ErrorRow - 1, 0));
            _savedErrorLine = null;
            ErrorState = false;
            InputInhibited = false;
            InhibitReason = "";
        }
        InsertMode = false;
        Message = "";
        Changed?.Invoke();
    }

    public void ToggleInsert()
    {
        InsertMode = !InsertMode;
        Changed?.Invoke();
    }

    public void TypeChar(char c)
    {
        if (!Ready(() => TypeChar(c))) return;
        var s = Screen;
        var f = s.FieldAt(s.Cursor);
        if (f == null || f.Bypass) { KeyError("Cursor is in a protected area"); return; }
        int pos = s.Cursor;
        if (pos > f.TypeableEnd) { KeyError("Use Field- or Field Exit for the sign position"); return; }

        if (f.Monocase || f.Shift is 1) c = char.ToUpperInvariant(c);
        if (!Allowed(f, c)) { KeyError(f.Shift is 3 or 5 or 7 ? "Numeric field" : "Character not allowed here"); return; }
        if (!Ebcdic.TryEncode(c, out byte e)) { KeyError("Character not supported"); return; }

        if (InsertMode)
        {
            byte last = s.Buffer[s.Wrap(f.TypeableEnd)];
            if (last != 0x00 && last != 0x40) { KeyError("No room to insert"); return; }
            for (int p = f.TypeableEnd; p > pos; p--) s.Buffer[s.Wrap(p)] = s.Buffer[s.Wrap(p - 1)];
        }
        s.Buffer[s.Wrap(pos)] = e;
        f.Mdt = true;

        if (pos >= f.TypeableEnd)
        {
            if (f.AutoEnter) { AdjustField(f); SendAid(Aid.Enter); return; }
            if (!f.FieldExitRequired) s.Cursor = s.NextEnterable(pos)?.Start ?? pos;
        }
        else s.Cursor = s.Wrap(pos + 1);
        Changed?.Invoke();
    }

    private static bool Allowed(Field f, char c) => f.Shift switch
    {
        1 => char.IsLetter(c) || c is ' ' or ',' or '.' or '-',
        3 => char.IsDigit(c) || c is '+' or '-' or ',' or '.' or ' ',
        5 or 7 => char.IsDigit(c),
        _ => !char.IsControl(c),
    };

    /// <summary>Field Exit (minus=false) / Field- (minus=true).</summary>
    public void FieldExit(bool minus = false)
    {
        if (!Ready(() => FieldExit(minus))) return;
        var s = Screen;
        var f = s.EnterableFieldAt(s.Cursor);
        if (f == null) { NextField(); return; }
        if (minus && f.Shift is not (3 or 7)) { KeyError("Field- only works in numeric fields"); return; }

        for (int p = Math.Max(s.Cursor, f.Start); p <= f.TypeableEnd; p++) s.Buffer[s.Wrap(p)] = 0x00;
        AdjustField(f);
        if (f.IsSigned) s.Buffer[s.Wrap(f.End)] = minus ? (byte)0x60 : (byte)0x00;
        else if (minus)
        {
            int last = f.TypeableEnd;
            while (last >= f.Start && (s.Buffer[s.Wrap(last)] < 0xF0 || s.Buffer[s.Wrap(last)] > 0xF9)) last--;
            if (last >= f.Start) s.Buffer[s.Wrap(last)] = (byte)(0xD0 | (s.Buffer[s.Wrap(last)] & 0x0F));
        }
        f.Mdt = true;

        if (f.AutoEnter) { SendAid(Aid.Enter); return; }
        s.Cursor = s.NextEnterable(f.Start)?.Start ?? s.Cursor;
        Changed?.Invoke();
    }

    /// <summary>Right-adjust with zero or blank fill when the field asks for it.</summary>
    private void AdjustField(Field f)
    {
        if (f.Adjust != 5 && f.Adjust != 6) return;
        var s = Screen;
        int len = f.TypeableEnd - f.Start + 1;
        var content = new List<byte>();
        for (int p = f.Start; p <= f.TypeableEnd; p++) content.Add(s.Buffer[s.Wrap(p)]);
        int n = content.Count;
        while (n > 0 && (content[n - 1] == 0x00 || content[n - 1] == 0x40)) n--;
        byte fill = f.Adjust == 5 ? (byte)0xF0 : (byte)0x40;
        for (int k = 0; k < len; k++)
        {
            int src = k - (len - n);
            s.Buffer[s.Wrap(f.Start + k)] = src >= 0 ? content[src] : fill;
        }
    }

    public void NextField()
    {
        var s = Screen;
        var f = s.NextEnterable(s.Cursor);
        if (f != null) s.Cursor = f.Start;
        Changed?.Invoke();
    }

    public void PreviousField()
    {
        var s = Screen;
        var f = s.PreviousEnterable(s.Cursor);
        if (f != null) s.Cursor = f.Start;
        Changed?.Invoke();
    }

    public void Home()
    {
        var s = Screen;
        var f = s.FirstEnterable();
        s.Cursor = f?.Start ?? 0;
        Changed?.Invoke();
    }

    /// <summary>End: cursor after the last character typed in the current field.</summary>
    public void EndOfField()
    {
        var s = Screen;
        var f = s.EnterableFieldAt(s.Cursor);
        if (f == null) return;
        int p = f.TypeableEnd;
        while (p >= f.Start && (s.Buffer[s.Wrap(p)] == 0x00 || s.Buffer[s.Wrap(p)] == 0x40)) p--;
        s.Cursor = Math.Min(p + 1, f.TypeableEnd);
        Changed?.Invoke();
    }

    public void MoveCursor(int dRow, int dCol)
    {
        var s = Screen;
        int r = (s.CursorRow + dRow + s.Rows) % s.Rows;
        int c = s.CursorCol + dCol;
        if (c < 0) { c = s.Cols - 1; if (dRow == 0) r = (r - 1 + s.Rows) % s.Rows; }
        if (c >= s.Cols) { c = 0; if (dRow == 0) r = (r + 1) % s.Rows; }
        s.Cursor = s.Pos(r, c);
        Changed?.Invoke();
    }

    public void SetCursor(int row, int col)
    {
        Screen.Cursor = Screen.Pos(row, col);
        Changed?.Invoke();
    }

    /// <summary>Destructive backspace inside a field.</summary>
    public void Backspace()
    {
        if (!Ready(Backspace)) return;
        var s = Screen;
        var f = s.EnterableFieldAt(s.Cursor);
        if (f == null || s.Cursor <= f.Start) { MoveCursor(0, -1); return; }
        s.Cursor--;
        DeleteAtCursor(f);
    }

    public void Delete()
    {
        if (!Ready(Delete)) return;
        var f = Screen.EnterableFieldAt(Screen.Cursor);
        if (f == null) { KeyError("Cursor is in a protected area"); return; }
        DeleteAtCursor(f);
    }

    private void DeleteAtCursor(Field f)
    {
        var s = Screen;
        for (int p = s.Cursor; p < f.TypeableEnd; p++) s.Buffer[s.Wrap(p)] = s.Buffer[s.Wrap(p + 1)];
        s.Buffer[s.Wrap(f.TypeableEnd)] = 0x00;
        f.Mdt = true;
        Changed?.Invoke();
    }

    public void EraseEof()
    {
        if (!Ready(EraseEof)) return;
        var s = Screen;
        var f = s.EnterableFieldAt(s.Cursor);
        if (f == null) { KeyError("Cursor is in a protected area"); return; }
        for (int p = s.Cursor; p <= f.TypeableEnd; p++) s.Buffer[s.Wrap(p)] = 0x00;
        f.Mdt = true;
        Changed?.Invoke();
    }

    public void EraseInput()
    {
        if (!Ready(EraseInput)) return;
        foreach (var f in Screen.Fields.Where(f => f.Enterable)) { NullField(f); f.Mdt = true; }
        Home();
    }

    public void Dup()
    {
        if (!Ready(Dup)) return;
        var s = Screen;
        var f = s.EnterableFieldAt(s.Cursor);
        if (f == null || !f.DupEnabled) { KeyError("Dup not allowed in this field"); return; }
        for (int p = s.Cursor; p <= f.TypeableEnd; p++) s.Buffer[s.Wrap(p)] = 0x1C;
        f.Mdt = true;
        s.Cursor = s.NextEnterable(f.Start)?.Start ?? s.Cursor;
        Changed?.Invoke();
    }

    /// <summary>Type text: tab moves to the next field, a newline acts as Field Exit.</summary>
    public void Paste(string text)
    {
        foreach (var ch in text.Replace("\r\n", "\n"))
        {
            if (ch == '\n') FieldExit();
            else if (ch == '\t') NextField();
            else if (!char.IsControl(ch)) TypeChar(ch);
        }
    }

    public void OnConnected()
    {
        Connected = true;
        ClearUnit(24, 80);
        LockForHost();
        Changed?.Invoke();
    }

    public void OnDisconnected(string reason)
    {
        Connected = false;
        InputInhibited = true;
        InhibitReason = "Disconnected";
        Message = reason;
        _typeAhead.Clear();
        Changed?.Invoke();
    }
}
