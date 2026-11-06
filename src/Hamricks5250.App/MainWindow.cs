using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Hamricks5250.Core;

namespace Hamricks5250.App;

public sealed class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly TabControl _tabs = new();
    private readonly List<SessionTab> _sessions = new();
    private readonly Border _keyBar;
    private readonly StackPanel _toolbar;
    private readonly TextBlock _stConn = new(), _stDevice = new(), _stInhibit = new(), _stMessage = new(),
                               _stInsert = new(), _stMw = new(), _stCursor = new();
    private readonly System.Windows.Shapes.Ellipse _stDot = new() { Width = 9, Height = 9, Margin = new Thickness(0, 0, 8, 0) };
    private WindowState _restoreState;
    private WindowStyle _restoreStyle;

    public MainWindow(AppSettings settings, string? profileName)
    {
        _settings = settings;
        Title = "Ham 5250";
        Icon = Theme.AppIcon();
        Width = 1240;
        Height = 820;
        MinWidth = 760;
        MinHeight = 480;
        Background = Theme.Chrome;
        FontFamily = Theme.UiFont;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new DockPanel { LastChildFill = true };

        // ---- Branded header ----------------------------------------------------------------
        var header = new Border { Background = Theme.Brand, Height = 52, Padding = new Thickness(16, 0, 8, 0) };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var logo = Theme.Logo();
        if (logo != null) brand.Children.Add(new Image { Source = logo, Height = 30, Margin = new Thickness(0, 0, 12, 0) });
        else brand.Children.Add(new TextBlock { Text = "Hamrick's", FontFamily = Theme.BrandFont, FontWeight = FontWeights.Bold, FontSize = 24, Foreground = Brushes.White, Margin = new Thickness(0, 0, 12, 0) });
        brand.Children.Add(new Border
        {
            Background = Theme.BrandDark, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "5250", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13 },
        });
        Grid.SetColumn(brand, 0);
        headerGrid.Children.Add(brand);

        var flat = Theme.FlatButton(Brushes.White, Theme.BrandHover, Theme.BrandPressed);
        _toolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Button Tool(string glyph, string label, string tip, Action click)
        {
            var b = new Button { Content = Theme.IconLabel(glyph, label), Style = flat, ToolTip = tip, Margin = new Thickness(2, 0, 2, 0) };
            b.Click += (_, _) => { click(); FocusTerminal(); };
            _toolbar.Children.Add(b);
            return b;
        }
        var newBtn = Tool("", "New session", "Open another session (Ctrl+T)", () => { });
        newBtn.Click += (_, _) => NewSessionMenu(newBtn);
        Tool("", "Connect", "Connect or reconnect this session", () => _ = Current?.ConnectAsync());
        Tool("", "Disconnect", "Disconnect this session", () => Current?.Disconnect());
        Tool("", "Copy", "Copy selection, or the whole screen (Ctrl+C)", () => CurrentView?.CopyToClipboard());
        Tool("", "Paste", "Paste into fields (Ctrl+V)", () => CurrentView?.PasteFromClipboard());
        Tool("", "Print", "Print this screen (Ctrl+P)", PrintScreen);
        Tool("", "Keys", "Show or hide the function key bar", ToggleKeyBar);
        if (!_settings.LockProfiles) Tool("", "Settings", "Connection and keyboard settings", OpenSettings);
        Tool("", "About", "About Ham 5250 and keyboard help", () => new AboutWindow { Owner = this }.ShowDialog());
        Grid.SetColumn(_toolbar, 1);
        headerGrid.Children.Add(_toolbar);
        header.Child = headerGrid;
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // ---- Status bar (operator information area) --------------------------------------------
        var status = new Border { Background = Theme.StatusBar, Height = 30, Padding = new Thickness(14, 0, 14, 0) };
        var sg = new Grid();
        for (int i = 0; i < 7; i++)
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var connPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        connPanel.Children.Add(_stDot);
        connPanel.Children.Add(_stConn);
        void Place(UIElement el, int col) { Grid.SetColumn(el, col); sg.Children.Add(el); }
        Place(connPanel, 0);
        Place(_stDevice, 1);
        Place(_stInhibit, 2);
        Place(_stMessage, 3);
        Place(_stInsert, 4);
        Place(_stMw, 5);
        Place(_stCursor, 6);
        foreach (var t in new[] { _stConn, _stDevice, _stInhibit, _stMessage, _stInsert, _stMw, _stCursor })
        {
            t.Foreground = Theme.StatusText;
            t.FontSize = 12;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(0, 0, 18, 0);
            t.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        }
        _stConn.FontFamily = Theme.UiFont;
        _stMessage.FontFamily = Theme.UiFont;
        _stMessage.TextTrimming = TextTrimming.CharacterEllipsis;
        _stInhibit.Foreground = Theme.Warn;
        _stMessage.Foreground = Theme.Warn;
        _stCursor.Margin = new Thickness(0);
        status.Child = sg;
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        // ---- Function key bar --------------------------------------------------------------------
        _keyBar = BuildKeyBar();
        DockPanel.SetDock(_keyBar, Dock.Bottom);
        root.Children.Add(_keyBar);

        // ---- Sessions ----------------------------------------------------------------------------
        _tabs.Background = Brushes.Black;
        _tabs.BorderThickness = new Thickness(0);
        _tabs.Padding = new Thickness(0);
        _tabs.Margin = new Thickness(0);
        _tabs.SelectionChanged += (_, e) => { if (e.Source == _tabs) { RefreshStatus(); FocusTerminal(); } };
        root.Children.Add(_tabs);

        Content = root;

        if (_settings.Kiosk) EnterFullScreen();

        PreviewKeyDown += OnWindowKeys;
        Closing += OnClosing;
        Loaded += (_, _) => OpenSession(_settings.GetProfile(profileName));
    }

    /// <summary>Set when Windows is signing out or shutting down, so kiosk mode never blocks it.</summary>
    public static bool WindowsShuttingDown { get; set; }

    private SessionTab? Current =>(_tabs.SelectedItem as TabItem)?.Tag as SessionTab;
    private TerminalView? CurrentView => ((_tabs.SelectedItem as TabItem)?.Content as Border)?.Child as TerminalView;

    private void FocusTerminal()
    {
        var v = CurrentView;
        if (v != null) Dispatcher.InvokeAsync(() => Keyboard.Focus(v));
    }

    private void OpenSession(SessionProfile profile)
    {
        int number = 1;
        while (_sessions.Any(s => s.Profile.Name == profile.Name && s.Number == number)) number++;
        var tab = new SessionTab(profile, number, Dispatcher);
        var view = new TerminalView(tab, _settings);
        view.SessionChanged += () => { if (Current == tab) RefreshStatus(); };
        tab.StateChanged += () => { if (Current == tab) RefreshStatus(); };

        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        headerPanel.Children.Add(new TextBlock { Text = tab.Title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) });
        var close = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = Theme.IconFont, FontSize = 10 },
            Style = Theme.FlatButton(Theme.TextMuted, Theme.ChromeBorder, Theme.ChromeBorder, null, 4),
            Padding = new Thickness(4), ToolTip = "Close session",
        };
        headerPanel.Children.Add(close);

        var item = new TabItem
        {
            Header = headerPanel,
            Tag = tab,
            Content = new Border { Background = Brushes.Black, Child = view },
        };
        close.Click += (_, _) => CloseSession(item);
        _sessions.Add(tab);
        _tabs.Items.Add(item);
        _tabs.SelectedItem = item;
        UpdateTabStrip();
        if (profile.AutoConnect && !string.IsNullOrWhiteSpace(profile.Host)) _ = tab.ConnectAsync();
        RefreshStatus();
    }

    private void CloseSession(TabItem item)
    {
        if (item.Tag is not SessionTab tab) return;
        if (_sessions.Count == 1) { Close(); return; }
        if (tab.Session.Connected && _settings.ConfirmOnClose &&
            MessageBox.Show(this, "Close this session? Make sure you've signed off the IBM i first.", "Ham 5250",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        tab.Close();
        _sessions.Remove(tab);
        _tabs.Items.Remove(item);
        UpdateTabStrip();
    }

    private void UpdateTabStrip()
    {
        // A single session doesn't need a tab strip - looks like a normal terminal window.
        foreach (TabItem t in _tabs.Items) t.Visibility = _tabs.Items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NewSessionMenu(Button anchor)
    {
        if (_settings.Profiles.Count <= 1) { OpenSession(_settings.GetProfile()); return; }
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var p in _settings.Profiles)
        {
            var mi = new MenuItem { Header = p.Name };
            mi.Click += (_, _) => OpenSession(p);
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    private Border BuildKeyBar()
    {
        var key = Theme.FlatButton(Brushes.White, Theme.KeyHover, Theme.Brand, Theme.KeyFace, 5);
        var accent = Theme.FlatButton(Brushes.White, Theme.BrandDark, Theme.BrandDark, Theme.Brand, 5);
        var grid = new UniformGrid { Columns = 13, Rows = 2, Margin = new Thickness(8, 6, 8, 6) };
        void Add(string label, Action act, Style? style = null, string? tip = null)
        {
            var b = new Button { Content = label, Style = style ?? key, Margin = new Thickness(3), Padding = new Thickness(4, 5, 4, 5), FontSize = 12, ToolTip = tip };
            b.Click += (_, _) => { act(); FocusTerminal(); };
            grid.Children.Add(b);
        }
        for (int n = 1; n <= 12; n++) { int k = n; Add($"F{k}", () => Current?.Session.SendAid(Aid.Function(k))); }
        Add("Enter", () => Current?.Session.SendAid(Aid.Enter), accent, "Send (Enter)");
        for (int n = 13; n <= 24; n++) { int k = n; Add($"F{k}", () => Current?.Session.SendAid(Aid.Function(k)), null, $"Shift+F{k - 12}"); }
        Add("Field Exit", () => Current?.Session.FieldExit(), null, _settings.EnterKey == EnterKeyMode.Classic ? "Enter key" : "Right Ctrl");

        var extras = new WrapPanel { Margin = new Thickness(8, 0, 8, 6) };
        void Extra(string label, Action act, string tip)
        {
            var b = new Button { Content = label, Style = key, Margin = new Thickness(3), Padding = new Thickness(10, 4, 10, 4), FontSize = 12, ToolTip = tip, MinWidth = 70 };
            b.Click += (_, _) => { act(); FocusTerminal(); };
            extras.Children.Add(b);
        }
        Extra("Page Up", () => Current?.Session.SendAid(Aid.RollDown), "Roll down (Page Up)");
        Extra("Page Down", () => Current?.Session.SendAid(Aid.RollUp), "Roll up (Page Down)");
        Extra("Field -", () => Current?.Session.FieldExit(minus: true), "Field minus (keypad -)");
        Extra("Help", () => Current?.Session.SendAid(Aid.Help), "Ctrl+F1");
        Extra("Print", () => Current?.Session.SendAid(Aid.Print), "Host print (Ctrl+Shift+P)");
        Extra("Clear", () => Current?.Session.SendAid(Aid.Clear), "Pause");
        Extra("Attn", () => Current?.Session.Attention(), "Esc");
        Extra("Sys Req", () => Current?.Session.SystemRequest(), "Shift+Esc");
        Extra("Reset", () => Current?.Session.ResetKey(), "Left Ctrl or Ctrl+R");
        Extra("Erase Input", () => Current?.Session.EraseInput(), "Ctrl+Shift+Delete");
        Extra("Dup", () => Current?.Session.Dup(), "Shift+Insert");

        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(extras);
        return new Border { Background = Theme.KeyBar, Child = panel, Visibility = Visibility.Collapsed };
    }

    private void ToggleKeyBar() =>
        _keyBar.Visibility = _keyBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    public void RefreshStatus()
    {
        var tab = Current;
        if (tab == null) return;
        var s = tab.Session;
        if (s.Connected)
        {
            _stDot.Fill = Theme.Ok;
            _stConn.Text = $"Connected · {tab.Profile.Host}:{tab.Profile.Port}" + (tab.IsTls ? " · \U0001F512 TLS" : " · not encrypted");
        }
        else
        {
            _stDot.Fill = Theme.Bad;
            _stConn.Text = "Not connected";
        }
        _stDevice.Text = tab.DeviceName;
        _stInhibit.Text = s.Connected && s.InputInhibited ? $"X {s.InhibitReason}" : "";
        _stMessage.Text = s.Message;
        _stInsert.Text = s.InsertMode ? "INS" : "";
        _stMw.Text = s.MessageWaiting ? "MW" : "";
        _stMw.Foreground = Theme.Warn;
        _stCursor.Text = $"{s.Screen.CursorRow + 1:00}/{s.Screen.CursorCol + 1:000}";
        Title = $"Ham 5250 - {tab.Title}";
    }

    private void OnWindowKeys(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Enter && mods == ModifierKeys.Alt) { ToggleFullScreen(); e.Handled = true; }
        else if (key == Key.T && mods == ModifierKeys.Control) { OpenSession(_settings.GetProfile()); e.Handled = true; }
        else if (key == Key.P && mods == ModifierKeys.Control) { PrintScreen(); e.Handled = true; }
        else if (key == Key.Tab && mods.HasFlag(ModifierKeys.Control) && _tabs.Items.Count > 1)
        {
            int dir = mods.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            _tabs.SelectedIndex = (_tabs.SelectedIndex + dir + _tabs.Items.Count) % _tabs.Items.Count;
            e.Handled = true;
        }
        // IT escape hatch out of kiosk mode
        else if (_settings.Kiosk && key == Key.F12 && mods == (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift))
        {
            _settings.Kiosk = false;
            ExitFullScreen();
            e.Handled = true;
        }
    }

    private void ToggleFullScreen()
    {
        if (WindowStyle == WindowStyle.None) ExitFullScreen(); else EnterFullScreen();
    }

    private void EnterFullScreen()
    {
        _restoreState = WindowState;
        _restoreStyle = WindowStyle;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Normal;   // toggle so the taskbar is covered
        WindowState = WindowState.Maximized;
        if (_settings.Kiosk) _toolbar.Visibility = Visibility.Collapsed;
    }

    private void ExitFullScreen()
    {
        WindowStyle = _restoreStyle == WindowStyle.None ? WindowStyle.SingleBorderWindow : _restoreStyle;
        ResizeMode = ResizeMode.CanResize;
        WindowState = _restoreState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        _toolbar.Visibility = Visibility.Visible;
    }

    private void PrintScreen()
    {
        var view = CurrentView;
        if (view == null) return;
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        double w = dlg.PrintableAreaWidth, h = dlg.PrintableAreaHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double margin = 48;
            var area = new Size(w - 2 * margin, (w - 2 * margin) * 0.45);
            dc.PushTransform(new TranslateTransform(margin, margin));
            var title = new FormattedText($"Ham 5250 · {Current?.Title} · {DateTime.Now:g}",
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(Theme.UiFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 12, Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(title, new Point(0, 0));
            dc.PushTransform(new TranslateTransform(0, 28));
            view.Draw(dc, area, print: true);
            dc.Pop();
            dc.Pop();
        }
        dlg.PrintVisual(visual, "Ham 5250 screen");
    }

    private void OpenSettings()
    {
        var dlg = new SettingsWindow(_settings, Current?.Profile.Name) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        try { _settings.Save(); }
        catch (Exception ex) { MessageBox.Show(this, $"Couldn't save settings: {ex.Message}", "Ham 5250"); }
        foreach (var tab in _sessions.ToList())
        {
            var updated = _settings.Profiles.FirstOrDefault(p => p.Name == tab.Profile.Name) ?? _settings.GetProfile();
            _ = tab.ApplyProfileAsync(updated);
        }
        foreach (var item in _tabs.Items.OfType<TabItem>())
            ((item.Content as Border)?.Child as TerminalView)?.InvalidateVisual();
        RefreshStatus();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_settings.Kiosk && !WindowsShuttingDown) { e.Cancel = true; return; }
        if (!WindowsShuttingDown && _settings.ConfirmOnClose && _sessions.Any(s => s.Session.Connected) &&
            MessageBox.Show(this, "Close Ham 5250? Make sure you've signed off the IBM i first.", "Ham 5250",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        foreach (var s in _sessions) s.Close();
    }
}
