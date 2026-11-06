using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Hamricks5250.Core;

namespace Hamricks5250.App;

/// <summary>Hamrick's brand colors and shared styles. Change brand colors here.</summary>
public static class Theme
{
    // Brand - placeholder values until confirmed against the official Hamrick's brand guide.
    public static readonly Color BrandColor = Color.FromRgb(0x8E, 0x0B, 0x21);
    public static readonly Color BrandDarkColor = Color.FromRgb(0x5E, 0x07, 0x15);

    public static readonly Brush Brand = Freeze(new SolidColorBrush(BrandColor));
    public static readonly Brush BrandDark = Freeze(new SolidColorBrush(BrandDarkColor));
    public static readonly Brush BrandHover = Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush BrandPressed = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00)));
    public static readonly Brush Chrome = Freeze(new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF5)));
    public static readonly Brush ChromeBorder = Freeze(new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD8)));
    public static readonly Brush StatusBar = Freeze(new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x24)));
    public static readonly Brush StatusText = Freeze(new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE7)));
    public static readonly Brush KeyBar = Freeze(new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A)));
    public static readonly Brush KeyFace = Freeze(new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)));
    public static readonly Brush KeyHover = Freeze(new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)));
    public static readonly Brush TextDark = Freeze(new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x24)));
    public static readonly Brush TextMuted = Freeze(new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)));
    public static readonly Brush Ok = Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)));
    public static readonly Brush Warn = Freeze(new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15)));
    public static readonly Brush Bad = Freeze(new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)));

    // Terminal palette (on black), tuned for readability.
    public static readonly Brush TermBackground = Brushes.Black;
    public static readonly Brush Selection = Freeze(new SolidColorBrush(Color.FromArgb(0x66, 0x60, 0xA5, 0xFA)));
    public static readonly Brush Crosshair = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)));

    private static readonly Brush[] ScreenColors =
    {
        Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0xE5, 0x6A))), // green
        Freeze(new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF5))), // white
        Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C))), // red
        Freeze(new SolidColorBrush(Color.FromRgb(0x3C, 0xE0, 0xE0))), // turquoise
        Freeze(new SolidColorBrush(Color.FromRgb(0xFA, 0xE3, 0x4B))), // yellow
        Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0xE8))), // pink
        Freeze(new SolidColorBrush(Color.FromRgb(0x6C, 0xA8, 0xFF))), // blue
    };

    public static Brush ForScreen(CellColor c) => ScreenColors[(int)c];

    public static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily BrandFont = new("Georgia");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily TerminalFont = new("Cascadia Mono, Consolas, Courier New");

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>Flat button: transparent until hover. Not focusable so the terminal keeps the keyboard.</summary>
    public static Style FlatButton(Brush foreground, Brush hover, Brush pressed, Brush? background = null, double radius = 6)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "bd" };
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;

        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Border.BackgroundProperty, hover, "bd"));
        var down = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        down.Setters.Add(new Setter(Border.BackgroundProperty, pressed, "bd"));
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
        template.Triggers.Add(over);
        template.Triggers.Add(down);
        template.Triggers.Add(disabled);

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
        style.Setters.Add(new Setter(Control.BackgroundProperty, background ?? Brushes.Transparent));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
        return style;
    }

    /// <summary>Primary action button for dialogs (brand fill, white text).</summary>
    public static Style PrimaryButton()
    {
        var s = FlatButton(Brushes.White, BrandDark, BrandDark, Brand, 6);
        s.Setters.Add(new Setter(UIElement.FocusableProperty, true));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(18, 8, 18, 8)));
        return s;
    }

    public static Style SecondaryButton()
    {
        var s = FlatButton(TextDark, ChromeBorder, ChromeBorder, Chrome, 6);
        s.Setters.Add(new Setter(UIElement.FocusableProperty, true));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(18, 8, 18, 8)));
        return s;
    }

    /// <summary>Icon glyph + label stacked for the header toolbar.</summary>
    public static object IconLabel(string glyph, string label, double iconSize = 15)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = glyph, FontFamily = IconFont, FontSize = iconSize,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0),
        });
        panel.Children.Add(new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    public static ImageSource? Logo()
    {
        try
        {
            return new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
        }
        catch { return null; }
    }

    public static ImageSource? AppIcon()
    {
        try
        {
            return System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/hamricks.ico"));
        }
        catch { return null; }
    }
}
