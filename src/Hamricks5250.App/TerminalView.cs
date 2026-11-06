using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hamricks5250.Core;

namespace Hamricks5250.App;

/// <summary>Draws a 5250 screen and turns Windows keyboard/mouse input into 5250 keys.</summary>
public sealed class TerminalView : FrameworkElement
{
    private readonly SessionTab _tab;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _blink;
    private bool _cursorOn = true;

    // geometry from the last render
    private double _cellW, _cellH, _offX, _offY, _fontSize;
    private Typeface _typeface = new(Theme.TerminalFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    // selection (rectangular, in cells)
    private (int r, int c)? _dragStart;
    private (int r1, int c1, int r2, int c2)? _selection;
    private bool _dragged;

    // Left Ctrl pressed and released alone = Reset (IBM convention)
    private bool _leftCtrlAlone;

    private static readonly Regex FKey = new(@"\b(?:F|PF|CMD)(\d{1,2})\s?[=-]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public TerminalView(SessionTab tab, AppSettings settings)
    {
        _tab = tab;
        _settings = settings;
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        _blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _blink.Tick += (_, _) => { if (_settings.CursorBlink) { _cursorOn = !_cursorOn; InvalidateVisual(); } };
        _blink.Start();
        tab.Session.Changed += OnSessionChanged;
        tab.SessionReplaced += (oldSession, newSession) =>
        {
            oldSession.Changed -= OnSessionChanged;
            newSession.Changed += OnSessionChanged;
            InvalidateVisual();
        };
        tab.StateChanged += InvalidateVisual;
        Loaded += (_, _) => Keyboard.Focus(this);
        Unloaded += (_, _) => _blink.Stop();
    }

    private Session5250 S => _tab.Session;

    private void OnSessionChanged()
    {
        _cursorOn = true;
        InvalidateVisual();
        SessionChanged?.Invoke();
    }

    /// <summary>Raised after any screen/keyboard change (the window refreshes its status bar).</summary>
    public event Action? SessionChanged;

    // =====================================================================================
    // Rendering
    // =====================================================================================

    protected override void OnRender(DrawingContext dc) => Draw(dc, new Size(ActualWidth, ActualHeight), print: false);

    /// <summary>Draws the screen. print=true uses black-on-white for paper.</summary>
    public void Draw(DrawingContext dc, Size size, bool print)
    {
        var scr = S.Screen;
        var background = print ? Brushes.White : Theme.TermBackground;
        dc.DrawRectangle(background, null, new Rect(size));
        if (size.Width < 10 || size.Height < 10) return;

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var probe = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 100, Brushes.White, pixelsPerDip);
        double advance = probe.WidthIncludingTrailingWhitespace / 100.0, lineH = probe.Height / 100.0;
        double padding = 10 * _settings.FontScale;
        double availW = size.Width - 2 * padding, availH = size.Height - 2 * padding;
        double fontSize = Math.Max(6, Math.Min(availW / scr.Cols / advance, availH / scr.Rows / lineH));
        double cellW = fontSize * advance, cellH = fontSize * lineH;
        double offX = Math.Round((size.Width - cellW * scr.Cols) / 2), offY = Math.Round((size.Height - cellH * scr.Rows) / 2);
        if (!print) { _cellW = cellW; _cellH = cellH; _offX = offX; _offY = offY; _fontSize = fontSize; }

        var styles = scr.Styles();
        var sepPen = new Pen(print ? Brushes.Gray : new SolidColorBrush(Color.FromArgb(0x70, 0x3C, 0xE0, 0xE0)), 1);
        sepPen.Freeze();

        for (int r = 0; r < scr.Rows; r++)
        {
            int c = 0;
            while (c < scr.Cols)
            {
                int p = r * scr.Cols + c;
                var style = styles[p];
                int start = c;
                var text = new StringBuilder();
                while (c < scr.Cols && styles[r * scr.Cols + c] == style)
                {
                    int q = r * scr.Cols + c;
                    text.Append(scr.IsAttribute(q) ? ' ' : S.Ebcdic.Display(scr.Buffer[q]));
                    c++;
                }
                var rect = new Rect(offX + start * cellW, offY + r * cellH, (c - start) * cellW, cellH);
                Brush fg = print ? Brushes.Black : Theme.ForScreen(style.Color);
                var flags = style.Flags;

                if (flags.HasFlag(CellFlags.Reverse))
                {
                    dc.DrawRectangle(fg, null, rect);
                    fg = background;
                }
                if (flags.HasFlag(CellFlags.NonDisplay)) continue;

                var s = text.ToString();
                if (s.Trim().Length > 0)
                {
                    var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, fontSize, fg, pixelsPerDip);
                    dc.DrawText(ft, rect.TopLeft);
                }
                if (flags.HasFlag(CellFlags.Underline))
                {
                    var pen = new Pen(fg, Math.Max(1, fontSize / 14));
                    double y = rect.Bottom - Math.Max(1.5, cellH * 0.08);
                    dc.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
                }
                if (flags.HasFlag(CellFlags.ColumnSeparator) && _settings.ShowColumnSeparators)
                {
                    for (int k = start; k <= c; k++)
                    {
                        double x = Math.Round(offX + k * cellW) + 0.5;
                        dc.DrawLine(sepPen, new Point(x, rect.Bottom - cellH * 0.22), new Point(x, rect.Bottom));
                    }
                }
            }
        }

        if (print) return;

        if (_settings.Crosshair)
        {
            dc.DrawRectangle(Theme.Crosshair, null, new Rect(offX, offY + scr.CursorRow * cellH, cellW * scr.Cols, cellH));
            dc.DrawRectangle(Theme.Crosshair, null, new Rect(offX + scr.CursorCol * cellW, offY, cellW, cellH * scr.Rows));
        }

        if (_selection is { } sel)
        {
            var rect = new Rect(offX + sel.c1 * cellW, offY + sel.r1 * cellH, (sel.c2 - sel.c1 + 1) * cellW, (sel.r2 - sel.r1 + 1) * cellH);
            dc.DrawRectangle(Theme.Selection, null, rect);
        }

        if (S.Connected && (_cursorOn || !_settings.CursorBlink))
        {
            var cur = new Rect(offX + scr.CursorCol * cellW, offY + scr.CursorRow * cellH, cellW, cellH);
            var cursorBrush = new SolidColorBrush(Color.FromArgb(0xD0, 0xF4, 0xF4, 0xF5));
            if (S.InsertMode) dc.DrawRectangle(cursorBrush, null, new Rect(cur.Left, cur.Top + cellH / 2, cellW, cellH / 2));
            else dc.DrawRectangle(cursorBrush, null, new Rect(cur.Left, cur.Bottom - Math.Max(2, cellH * 0.14), cellW, Math.Max(2, cellH * 0.14)));
        }

        if (!S.Connected)
        {
            var msg = _tab.OverlayText;
            if (!string.IsNullOrEmpty(msg))
            {
                var ft = new FormattedText(msg, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(Theme.UiFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    Math.Max(14, fontSize * 0.9), Brushes.White, pixelsPerDip) { TextAlignment = TextAlignment.Center };
                var box = new Rect(size.Width / 2 - ft.Width / 2 - 24, size.Height / 2 - ft.Height / 2 - 16, ft.Width + 48, ft.Height + 32);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0x27, 0x27, 0x2A)), new Pen(Theme.Brand, 2), box, 10, 10);
                dc.DrawText(ft, new Point(size.Width / 2, box.Top + 16));
            }
        }
    }

    // =====================================================================================
    // Keyboard
    // =====================================================================================

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool shift = mods.HasFlag(ModifierKeys.Shift), ctrl = mods.HasFlag(ModifierKeys.Control), alt = mods.HasFlag(ModifierKeys.Alt);

        _leftCtrlAlone = key == Key.LeftCtrl && !e.IsRepeat;
        if (key is Key.LeftCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt) return;

        if (!S.Connected)
        {
            if (key == Key.Enter) { _ = _tab.ConnectAsync(); e.Handled = true; }
            return;
        }

        _selection = null;
        bool classic = _settings.EnterKey == EnterKeyMode.Classic;
        bool handled = true;

        switch (key)
        {
            case Key.Enter when alt: handled = false; break; // full screen handled by the window
            case Key.Enter: if (classic) S.FieldExit(); else S.SendAid(Aid.Enter); break;
            case Key.RightCtrl: if (classic) S.SendAid(Aid.Enter); else S.FieldExit(); break;
            case Key.Tab when ctrl: handled = false; break;    // switch tabs
            case Key.Tab: if (shift) S.PreviousField(); else S.NextField(); break;
            case Key.Home: S.Home(); break;
            case Key.End when ctrl: S.EraseEof(); break;
            case Key.End: S.EndOfField(); break;
            case Key.Left: S.MoveCursor(0, -1); break;
            case Key.Right: S.MoveCursor(0, 1); break;
            case Key.Up: S.MoveCursor(-1, 0); break;
            case Key.Down: S.MoveCursor(1, 0); break;
            case Key.Back: S.Backspace(); break;
            case Key.Delete when ctrl && shift: S.EraseInput(); break;
            case Key.Delete when ctrl: S.EraseEof(); break;
            case Key.Delete: S.Delete(); break;
            case Key.Insert when shift: S.Dup(); break;
            case Key.Insert: S.ToggleInsert(); break;
            case Key.PageUp: S.SendAid(Aid.RollDown); break;
            case Key.PageDown: S.SendAid(Aid.RollUp); break;
            case Key.Escape when shift: S.SystemRequest(); break;
            case Key.Escape: S.Attention(); break;
            case Key.Pause: S.SendAid(Aid.Clear); break;
            case Key.Add: S.FieldExit(); break;               // keypad + = Field+
            case Key.Subtract: S.FieldExit(minus: true); break; // keypad - = Field-
            case Key.R when ctrl: S.ResetKey(); break;
            case Key.C when ctrl: CopyToClipboard(); break;
            case Key.V when ctrl: PasteFromClipboard(); break;
            case Key.A when ctrl: SelectAll(); break;
            case Key.P when ctrl && shift: S.SendAid(Aid.Print); break;
            case Key.F1 when ctrl: S.SendAid(Aid.Help); break;
            case >= Key.F1 and <= Key.F12 when !ctrl && !alt:
                S.SendAid(Aid.Function((key - Key.F1) + 1 + (shift ? 12 : 0)));
                break;
            case >= Key.F13 and <= Key.F24:
                S.SendAid(Aid.Function((key - Key.F13) + 13));
                break;
            default:
                handled = false;
                break;
        }
        if (handled)
        {
            _leftCtrlAlone = false;
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.LeftCtrl && _leftCtrlAlone && S.Connected) { S.ResetKey(); e.Handled = true; }
        _leftCtrlAlone = false;
        base.OnPreviewKeyUp(e);
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (!S.Connected || string.IsNullOrEmpty(e.Text)) return;
        _leftCtrlAlone = false;
        foreach (var ch in e.Text)
            if (!char.IsControl(ch)) S.TypeChar(ch);
        e.Handled = true;
    }

    // =====================================================================================
    // Mouse: click to place cursor, click F-key text to press it, drag to select
    // =====================================================================================

    private (int r, int c)? CellAt(Point p)
    {
        if (_cellW <= 0 || _cellH <= 0) return null;
        int c = (int)Math.Floor((p.X - _offX) / _cellW), r = (int)Math.Floor((p.Y - _offY) / _cellH);
        if (r < 0 || c < 0 || r >= S.Screen.Rows || c >= S.Screen.Cols) return null;
        return (r, c);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Keyboard.Focus(this);
        _dragStart = CellAt(e.GetPosition(this));
        _dragged = false;
        _selection = null;
        CaptureMouse();
        if (e.ClickCount == 2 && _dragStart is { } cell) SelectWord(cell.r, cell.c);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var cell = CellAt(e.GetPosition(this));
        if (e.LeftButton == MouseButtonState.Pressed && _dragStart is { } a && cell is { } b && b != a)
        {
            _dragged = true;
            _selection = (Math.Min(a.r, b.r), Math.Min(a.c, b.c), Math.Max(a.r, b.r), Math.Max(a.c, b.c));
            InvalidateVisual();
        }
        Cursor = cell is { } h && _settings.Hotspots && HotspotAt(h.r, h.c) != null ? Cursors.Hand : Cursors.IBeam;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        var cell = CellAt(e.GetPosition(this));
        if (!_dragged && cell is { } c && S.Connected && e.ClickCount == 1)
        {
            var aid = _settings.Hotspots ? HotspotAt(c.r, c.c) : null;
            if (aid is byte a) S.SendAid(a);
            else S.SetCursor(c.r, c.c);
        }
        _dragStart = null;
        e.Handled = true;
    }

    /// <summary>If the click is on text like "F3=Exit", returns that key's AID.</summary>
    private byte? HotspotAt(int row, int col)
    {
        var text = S.Screen.RowText(row, S.Ebcdic);
        foreach (Match m in FKey.Matches(text))
        {
            int end = text.IndexOf("  ", m.Index + m.Length, StringComparison.Ordinal);
            if (end < 0) end = text.Length;
            if (col >= m.Index && col < end && int.TryParse(m.Groups[1].Value, out int n) && n is >= 1 and <= 24)
                return Aid.Function(n);
        }
        return null;
    }

    private void SelectWord(int r, int c)
    {
        var text = S.Screen.RowText(r, S.Ebcdic);
        if (c >= text.Length || text[c] == ' ') return;
        int a = c, b = c;
        while (a > 0 && text[a - 1] != ' ') a--;
        while (b < text.Length - 1 && text[b + 1] != ' ') b++;
        _selection = (r, a, r, b);
        _dragged = true;
    }

    public void SelectAll()
    {
        _selection = (0, 0, S.Screen.Rows - 1, S.Screen.Cols - 1);
        InvalidateVisual();
    }

    public string SelectedText()
    {
        var scr = S.Screen;
        var (r1, c1, r2, c2) = _selection ?? (0, 0, scr.Rows - 1, scr.Cols - 1);
        var sb = new StringBuilder();
        for (int r = r1; r <= r2; r++)
        {
            var line = scr.RowText(r, S.Ebcdic).Substring(c1, c2 - c1 + 1).TrimEnd();
            sb.Append(line);
            if (r < r2) sb.AppendLine();
        }
        return sb.ToString();
    }

    public void CopyToClipboard()
    {
        try { Clipboard.SetText(SelectedText()); } catch { /* clipboard busy */ }
        _selection = null;
        InvalidateVisual();
    }

    public void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsText()) S.Paste(Clipboard.GetText());
        }
        catch { /* clipboard busy */ }
    }
}
