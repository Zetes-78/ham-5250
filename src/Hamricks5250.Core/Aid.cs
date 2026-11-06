namespace Hamricks5250.Core;

/// <summary>5250 attention identifier (AID) codes sent to the host.</summary>
public static class Aid
{
    public const byte Enter = 0xF1;
    public const byte Help = 0xF3;
    public const byte RollDown = 0xF4;   // Page Up
    public const byte RollUp = 0xF5;     // Page Down
    public const byte Print = 0xF6;
    public const byte RecordBackspace = 0xF8;
    public const byte Clear = 0xBD;
    public const byte InboundWsf = 0x88;
    public const byte None = 0x00;

    /// <summary>F1-F24.</summary>
    public static byte Function(int n) => n switch
    {
        >= 1 and <= 12 => (byte)(0x30 + n),
        >= 13 and <= 24 => (byte)(0xB0 + n - 12),
        _ => throw new ArgumentOutOfRangeException(nameof(n)),
    };

    /// <summary>Keys that send only cursor + AID, never field data.</summary>
    public static bool SendsNoData(byte aid) => aid is Help or Print or Clear or RecordBackspace;

    public static string Name(byte aid) => aid switch
    {
        Enter => "Enter",
        Help => "Help",
        RollDown => "Page Up",
        RollUp => "Page Down",
        Print => "Print",
        Clear => "Clear",
        RecordBackspace => "Record Backspace",
        >= 0x31 and <= 0x3C => $"F{aid - 0x30}",
        >= 0xB1 and <= 0xBC => $"F{aid - 0xB0 + 12}",
        _ => $"0x{aid:X2}",
    };
}
