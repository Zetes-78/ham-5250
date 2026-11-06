using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Hamricks5250.App;

/// <summary>Version, credits and the keyboard cheat sheet.</summary>
public sealed class AboutWindow : Window
{
    private static readonly (string key, string does)[] Keys =
    {
        ("Enter", "Send (Windows style) / Field Exit (classic)"),
        ("Right Ctrl", "Field Exit (Windows style) / Send (classic)"),
        ("F1 - F12", "F1 - F12"),
        ("Shift + F1 - F12", "F13 - F24"),
        ("Page Up / Page Down", "Roll down / Roll up"),
        ("Tab / Shift + Tab", "Next / previous field"),
        ("Home / End", "First field / end of typed text"),
        ("Keypad + / Keypad -", "Field+ / Field-"),
        ("Insert", "Insert mode on / off"),
        ("Ctrl + Delete", "Erase to end of field"),
        ("Ctrl + Shift + Delete", "Erase all input"),
        ("Shift + Insert", "Dup"),
        ("Esc / Shift + Esc", "Attention / System Request"),
        ("Left Ctrl or Ctrl + R", "Reset (clear an error)"),
        ("Ctrl + F1", "Help"),
        ("Pause", "Clear"),
        ("Ctrl + C / Ctrl + V", "Copy / paste"),
        ("Ctrl + P", "Print this screen on a Windows printer"),
        ("Ctrl + T / Ctrl + Tab", "New session / switch session"),
        ("Alt + Enter", "Full screen"),
        ("Mouse", "Click to place the cursor; click \"F3=Exit\" style text to press that key"),
    };

    public AboutWindow()
    {
        Title = "About Ham 5250";
        Icon = Theme.AppIcon();
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = Theme.UiFont;
        Background = Brushes.White;

        var root = new StackPanel();
        var header = new Border { Background = Theme.Brand, Padding = new Thickness(24, 20, 24, 20) };
        var hp = new StackPanel();
        var logo = Theme.Logo();
        if (logo != null) hp.Children.Add(new Image { Source = logo, Height = 40, HorizontalAlignment = HorizontalAlignment.Left });
        else hp.Children.Add(new TextBlock { Text = "Hamrick's", FontFamily = Theme.BrandFont, FontWeight = FontWeights.Bold, FontSize = 32, Foreground = Brushes.White });
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        hp.Children.Add(new TextBlock { Text = $"5250 terminal for IBM i · version {version?.ToString(3)}", Foreground = Brushes.White, FontSize = 14, Margin = new Thickness(0, 6, 0, 0) });
        header.Child = hp;
        root.Children.Add(header);

        var body = new StackPanel { Margin = new Thickness(24, 16, 24, 8) };
        body.Children.Add(new TextBlock { Text = "Keyboard", FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Theme.Brand, Margin = new Thickness(0, 0, 0, 8) });
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < Keys.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var k = new TextBlock { Text = Keys[i].key, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Margin = new Thickness(0, 2, 8, 2) };
            var d = new TextBlock { Text = Keys[i].does, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Theme.TextDark, Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetRow(k, i); Grid.SetRow(d, i); Grid.SetColumn(d, 1);
            grid.Children.Add(k);
            grid.Children.Add(d);
        }
        body.Children.Add(grid);
        root.Children.Add(body);

        var footer = new Border { BorderBrush = Theme.ChromeBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(24, 12, 24, 16), Margin = new Thickness(0, 8, 0, 0) };
        var credit = new TextBlock { FontSize = 12, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap };
        credit.Inlines.Add(new Run("Built by Night Tech Solutions · "));
        var link = new Hyperlink(new Run("nighttech.solutions")) { NavigateUri = new Uri("https://nighttech.solutions") };
        link.RequestNavigate += (_, e) => { try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { } };
        credit.Inlines.Add(link);
        credit.Inlines.Add(new Run("\nHamrick's name and logo are trademarks of Hamrick's, Inc."));
        footer.Child = credit;
        root.Children.Add(footer);
        Content = root;
    }
}
