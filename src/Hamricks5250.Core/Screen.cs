namespace Hamricks5250.Core;

/// <summary>An entry in the 5250 format table (an input field defined by a Start of Field order).</summary>
public sealed class Field
{
    public int Start { get; set; }          // linear position of the first data character
    public int Length { get; set; }
    public byte Ffw1 { get; set; }
    public byte Ffw2 { get; set; }
    public byte Attribute { get; set; }
    public List<ushort> Fcws { get; } = new();

    public int End => Start + Length - 1;
    public bool Bypass => (Ffw1 & 0x20) != 0;
    public bool DupEnabled => (Ffw1 & 0x10) != 0;
    public bool Mdt
    {
        get => (Ffw1 & 0x08) != 0;
        set => Ffw1 = (byte)(value ? Ffw1 | 0x08 : Ffw1 & ~0x08);
    }
    /// <summary>0 alpha, 1 alpha only, 2 numeric shift, 3 numeric only, 4 katakana, 5 digits only, 6 I/O, 7 signed numeric.</summary>
    public int Shift => Ffw1 & 0x07;
    public bool AutoEnter => (Ffw2 & 0x80) != 0;
    public bool FieldExitRequired => (Ffw2 & 0x40) != 0;
    public bool Monocase => (Ffw2 & 0x20) != 0;
    public bool MandatoryEnter => (Ffw2 & 0x08) != 0;
    /// <summary>5 right-adjust zero fill, 6 right-adjust blank fill, 7 mandatory fill.</summary>
    public int Adjust => Ffw2 & 0x07;
    public bool IsSigned => Shift == 7;
    /// <summary>Last position the user can type in (signed numeric reserves the last one for the sign).</summary>
    public int TypeableEnd => IsSigned ? End - 1 : End;
    public bool Enterable => !Bypass;
    public bool Contains(int pos) => pos >= Start && pos <= End;
}

public enum CellColor { Green, White, Red, Turquoise, Yellow, Pink, Blue }

[Flags]
public enum CellFlags { None = 0, Reverse = 1, Underline = 2, Blink = 4, ColumnSeparator = 8, NonDisplay = 16 }

public readonly record struct CellStyle(CellColor Color, CellFlags Flags)
{
    private static readonly CellStyle[] Table = Build();

    /// <summary>Decode a 5250 display attribute byte (0x20-0x3F) the way a color display shows it.</summary>
    public static CellStyle FromAttribute(byte a) => Table[(a & 0x1F)];

    private static CellStyle[] Build()
    {
        const CellFlags R = CellFlags.Reverse, U = CellFlags.Underline, B = CellFlags.Blink,
                        C = CellFlags.ColumnSeparator, N = CellFlags.NonDisplay, _ = CellFlags.None;
        return new CellStyle[]
        {
            new(CellColor.Green, _), new(CellColor.Green, R), new(CellColor.White, _), new(CellColor.White, R),
            new(CellColor.Green, U), new(CellColor.Green, U | R), new(CellColor.White, U), new(CellColor.Green, N),
            new(CellColor.Red, _), new(CellColor.Red, R), new(CellColor.Red, B), new(CellColor.Red, R | B),
            new(CellColor.Red, U), new(CellColor.Red, U | R), new(CellColor.Red, U | B), new(CellColor.Red, N),
            new(CellColor.Turquoise, C), new(CellColor.Turquoise, C | R), new(CellColor.Yellow, C), new(CellColor.Yellow, C | R),
            new(CellColor.Turquoise, U), new(CellColor.Turquoise, U | R), new(CellColor.Yellow, U), new(CellColor.Turquoise, N),
            new(CellColor.Pink, _), new(CellColor.Pink, R), new(CellColor.Blue, _), new(CellColor.Blue, R),
            new(CellColor.Pink, U), new(CellColor.Pink, U | R), new(CellColor.Blue, U), new(CellColor.Pink, N),
        };
    }
}

/// <summary>The display buffer: characters, attribute bytes, the format table and the cursor.</summary>
public sealed class Screen
{
    public int Rows { get; private set; } = 24;
    public int Cols { get; private set; } = 80;
    public int Size => Rows * Cols;
    public byte[] Buffer { get; private set; } = new byte[24 * 80];
    public List<Field> Fields { get; } = new();
    public int Cursor { get; set; }
    /// <summary>1-based row used for error messages (set by Start of Header, default last row).</summary>
    public int ErrorRow { get; set; } = 24;

    public int CursorRow => Cursor / Cols;
    public int CursorCol => Cursor % Cols;

    public void Reset(int rows, int cols)
    {
        Rows = rows;
        Cols = cols;
        Buffer = new byte[rows * cols];
        Fields.Clear();
        Cursor = 0;
        ErrorRow = rows;
    }

    public int Pos(int row, int col) => Math.Clamp(row, 0, Rows - 1) * Cols + Math.Clamp(col, 0, Cols - 1);

    /// <summary>Position from 1-based host row/column.</summary>
    public int HostPos(byte row, byte col) => Pos(row - 1, col - 1);

    public int Wrap(int pos) => ((pos % Size) + Size) % Size;

    public Field? FieldAt(int pos) => Fields.FirstOrDefault(f => f.Contains(pos));

    public Field? EnterableFieldAt(int pos) => Fields.FirstOrDefault(f => f.Enterable && f.Contains(pos));

    public Field? FirstEnterable() => Fields.FirstOrDefault(f => f.Enterable);

    public Field? NextEnterable(int pos)
    {
        var list = Fields.Where(f => f.Enterable).ToList();
        if (list.Count == 0) return null;
        return list.FirstOrDefault(f => f.Start > pos) ?? list[0];
    }

    public Field? PreviousEnterable(int pos)
    {
        var list = Fields.Where(f => f.Enterable).ToList();
        if (list.Count == 0) return null;
        var current = list.FirstOrDefault(f => f.Contains(pos));
        // From inside a field (not at its start) Field Backspace goes to that field's start.
        if (current != null && pos > current.Start) return current;
        return list.LastOrDefault(f => f.Start < (current?.Start ?? pos)) ?? list[^1];
    }

    public void AddField(Field field)
    {
        Fields.RemoveAll(f => f.Start == field.Start);
        int i = Fields.FindIndex(f => f.Start > field.Start);
        if (i < 0) Fields.Add(field); else Fields.Insert(i, field);
    }

    public byte[] FieldBytes(Field f)
    {
        var data = new byte[f.Length];
        for (int i = 0; i < f.Length; i++) data[i] = Buffer[Wrap(f.Start + i)];
        return data;
    }

    /// <summary>Visible style for every cell; attribute positions draw blank in the previous style.</summary>
    public CellStyle[] Styles()
    {
        var styles = new CellStyle[Size];
        var current = CellStyle.FromAttribute(0x20);
        for (int i = 0; i < Size; i++)
        {
            byte b = Buffer[i];
            if (b >= 0x20 && b <= 0x3F)
            {
                styles[i] = new CellStyle(current.Color, CellFlags.None);
                current = CellStyle.FromAttribute(b);
            }
            else styles[i] = current;
        }
        return styles;
    }

    public bool IsAttribute(int pos) => Buffer[pos] >= 0x20 && Buffer[pos] <= 0x3F;

    public string RowText(int row, Ebcdic ebcdic)
    {
        var styles = Styles();
        var chars = new char[Cols];
        for (int c = 0; c < Cols; c++)
        {
            int p = row * Cols + c;
            chars[c] = IsAttribute(p) || (styles[p].Flags & CellFlags.NonDisplay) != 0 ? ' ' : ebcdic.Display(Buffer[p]);
        }
        return new string(chars);
    }

    public string Text(Ebcdic ebcdic) =>
        string.Join(Environment.NewLine, Enumerable.Range(0, Rows).Select(r => RowText(r, ebcdic)));
}
