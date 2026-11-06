using System.Text;

namespace Hamricks5250.Core;

/// <summary>EBCDIC &lt;-&gt; Unicode for a given IBM code page (37 = US English).</summary>
public sealed class Ebcdic
{
    private readonly char[] _toChar = new char[256];
    private readonly Dictionary<char, byte> _toByte = new();

    public int CodePage { get; }

    static Ebcdic() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public Ebcdic(int codePage = 37)
    {
        CodePage = codePage;
        var enc = Encoding.GetEncoding(codePage);
        for (int b = 0; b < 256; b++)
        {
            char c = enc.GetString(new[] { (byte)b })[0];
            _toChar[b] = c;
            // Only printable range is typeable; 0x40 and up in EBCDIC.
            if (b >= 0x40 && b != 0xFF && !char.IsControl(c) && !_toByte.ContainsKey(c))
                _toByte[c] = (byte)b;
        }
    }

    /// <summary>Character to draw for a screen byte (attributes and controls draw as blanks).</summary>
    public char Display(byte b) => b switch
    {
        < 0x40 when b == 0x1C => '*',   // DUP
        < 0x40 when b == 0x1E => ';',   // field mark
        < 0x40 => ' ',
        _ => char.IsControl(_toChar[b]) ? ' ' : _toChar[b],
    };

    public bool TryEncode(char c, out byte b) => _toByte.TryGetValue(c, out b);

    public byte Encode(char c) => TryEncode(c, out var b) ? b : (byte)0x40;

    public byte[] Encode(string s) => s.Select(Encode).ToArray();

    public string Decode(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        foreach (var b in bytes) sb.Append(Display(b));
        return sb.ToString();
    }

    /// <summary>RFC 4777 CHARSET value for the code page, when known.</summary>
    public static string? CharsetFor(int codePage) => codePage switch
    {
        37 => "697",
        273 => "697",
        277 => "697",
        278 => "697",
        280 => "697",
        284 => "697",
        285 => "697",
        297 => "697",
        500 => "697",
        _ => null,
    };
}
