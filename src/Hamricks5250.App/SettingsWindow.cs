using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hamricks5250.Core;

namespace Hamricks5250.App;

/// <summary>Connection profiles and keyboard/display options.</summary>
public sealed class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly List<SessionProfile> _profiles;
    private SessionProfile _current;

    private readonly ComboBox _profileBox = new() { MinWidth = 220 };
    private readonly TextBox _name = new(), _host = new(), _port = new(), _device = new(), _prefix = new(), _thumb = new();
    private readonly CheckBox _tls = new() { Content = "Use TLS encryption (port 992)" };
    private readonly CheckBox _untrusted = new() { Content = "Accept untrusted certificates (lab only)" };
    private readonly CheckBox _wide = new() { Content = "Allow 27 x 132 screens" };
    private readonly CheckBox _autoConnect = new() { Content = "Connect automatically when opened" };
    private readonly CheckBox _autoReconnect = new() { Content = "Reconnect automatically if the connection drops" };
    private readonly ComboBox _codePage = new();
    private readonly ComboBox _enterKey = new();
    private readonly CheckBox _colSep = new() { Content = "Show column separators" };
    private readonly CheckBox _hotspots = new() { Content = "Click F-key text on screen (e.g. \"F3=Exit\") to press it" };
    private readonly CheckBox _blink = new() { Content = "Blinking cursor" };
    private readonly CheckBox _cross = new() { Content = "Crosshair (highlight cursor row and column)" };
    private readonly CheckBox _confirm = new() { Content = "Ask before closing a connected session" };
    private readonly TextBlock _devicePreview = new() { Foreground = Theme.TextMuted, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };

    private static readonly (int cp, string name)[] CodePages =
    {
        (37, "37 - US / Canada English"), (285, "285 - UK English"), (500, "500 - International"),
        (273, "273 - German"), (297, "297 - French"), (284, "284 - Spanish"), (280, "280 - Italian"),
    };

    public SettingsWindow(AppSettings settings, string? selectProfile)
    {
        _settings = settings;
        _profiles = settings.Profiles.Select(Clone).ToList();
        if (_profiles.Count == 0) _profiles.Add(new SessionProfile());
        _current = _profiles.FirstOrDefault(p => p.Name == selectProfile) ?? _profiles[0];

        Title = "Settings - Ham 5250";
        Icon = Theme.AppIcon();
        Width = 620;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = Theme.UiFont;
        FontSize = 13;
        Background = Brushes.White;

        foreach (var (cp, name) in CodePages) _codePage.Items.Add(new ComboBoxItem { Content = name, Tag = cp });
        _enterKey.Items.Add(new ComboBoxItem { Content = "Windows style: Enter sends, Right Ctrl = Field Exit", Tag = EnterKeyMode.Windows });
        _enterKey.Items.Add(new ComboBoxItem { Content = "Classic PC5250: Right Ctrl sends, Enter = Field Exit", Tag = EnterKeyMode.Classic });

        var root = new DockPanel();
        var header = new Border { Background = Theme.Brand, Padding = new Thickness(20, 14, 20, 14) };
        header.Child = new TextBlock { Text = "Settings", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 4, 20, 18) };
        var save = new Button { Content = "Save", Style = Theme.PrimaryButton(), IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
        var cancel = new Button { Content = "Cancel", Style = Theme.SecondaryButton(), IsCancel = true };
        save.Click += (_, _) => Save();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var body = new StackPanel { Margin = new Thickness(20, 16, 20, 8) };

        // Profile picker
        var picker = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        picker.Children.Add(new TextBlock { Text = "Profile", Width = 150, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        picker.Children.Add(_profileBox);
        var add = new Button { Content = "Add", Style = Theme.SecondaryButton(), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
        var remove = new Button { Content = "Remove", Style = Theme.SecondaryButton(), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
        add.Click += (_, _) =>
        {
            Collect();
            var p = new SessionProfile { Name = $"Session {_profiles.Count + 1}", Host = _current.Host };
            _profiles.Add(p);
            _current = p;
            RefreshPicker();
        };
        remove.Click += (_, _) =>
        {
            if (_profiles.Count <= 1) return;
            _profiles.Remove(_current);
            _current = _profiles[0];
            RefreshPicker();
        };
        picker.Children.Add(add);
        picker.Children.Add(remove);
        body.Children.Add(picker);
        _profileBox.SelectionChanged += (_, _) =>
        {
            if (_profileBox.SelectedItem is ComboBoxItem { Tag: SessionProfile p } && p != _current)
            {
                Collect();
                _current = p;
                ShowProfile(p);
            }
        };

        body.Children.Add(Section("Connection"));
        body.Children.Add(Row("Profile name", _name));
        body.Children.Add(Row("IBM i host", _host, "Host name or IP address, e.g. hamibm.corp.ham.com"));
        body.Children.Add(Row("Port", _port, "992 with TLS, 23 without"));
        body.Children.Add(Row("", _tls));
        body.Children.Add(Row("", _untrusted));
        body.Children.Add(Row("Trusted certificate", _thumb, "Optional SHA-1 thumbprint of the IBM i certificate"));
        body.Children.Add(Row("Code page", _codePage));
        body.Children.Add(Row("", _wide));

        body.Children.Add(Section("Device name"));
        var devicePanel = new StackPanel();
        devicePanel.Children.Add(_device);
        devicePanel.Children.Add(_devicePreview);
        body.Children.Add(Row("Device name", devicePanel, "Leave blank to name it from this PC automatically"));
        body.Children.Add(Row("Automatic prefix", _prefix));
        _device.TextChanged += (_, _) => UpdatePreview();
        _prefix.TextChanged += (_, _) => UpdatePreview();

        body.Children.Add(Section("Behavior"));
        body.Children.Add(Row("", _autoConnect));
        body.Children.Add(Row("", _autoReconnect));
        body.Children.Add(Row("Enter key", _enterKey));
        body.Children.Add(Row("", _colSep));
        body.Children.Add(Row("", _hotspots));
        body.Children.Add(Row("", _blink));
        body.Children.Add(Row("", _cross));
        body.Children.Add(Row("", _confirm));

        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 640 });
        Content = root;

        Select(_enterKey, settings.EnterKey);
        _colSep.IsChecked = settings.ShowColumnSeparators;
        _hotspots.IsChecked = settings.Hotspots;
        _blink.IsChecked = settings.CursorBlink;
        _cross.IsChecked = settings.Crosshair;
        _confirm.IsChecked = settings.ConfirmOnClose;
        RefreshPicker();
    }

    private static SessionProfile Clone(SessionProfile p) => new()
    {
        Name = p.Name, Host = p.Host, Port = p.Port, UseTls = p.UseTls, AllowUntrustedCertificate = p.AllowUntrustedCertificate,
        TrustedThumbprint = p.TrustedThumbprint, DeviceName = p.DeviceName, DevicePrefix = p.DevicePrefix, Wide = p.Wide,
        CodePage = p.CodePage, AutoConnect = p.AutoConnect, AutoReconnect = p.AutoReconnect,
    };

    private static TextBlock Section(string text) => new()
    {
        Text = text, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Theme.Brand, Margin = new Thickness(0, 14, 0, 6),
    };

    private static UIElement Row(string label, UIElement control, string? hint = null)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.TextDark };
        g.Children.Add(l);
        if (control is TextBox tb) { tb.Padding = new Thickness(6, 4, 6, 4); if (hint != null) tb.ToolTip = hint; }
        if (control is FrameworkElement fe && hint != null) fe.ToolTip = hint;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private void RefreshPicker()
    {
        _profileBox.Items.Clear();
        foreach (var p in _profiles)
        {
            var item = new ComboBoxItem { Content = p.Name, Tag = p };
            _profileBox.Items.Add(item);
            if (p == _current) _profileBox.SelectedItem = item;
        }
        ShowProfile(_current);
    }

    private void ShowProfile(SessionProfile p)
    {
        _name.Text = p.Name;
        _host.Text = p.Host;
        _port.Text = p.Port.ToString();
        _tls.IsChecked = p.UseTls;
        _untrusted.IsChecked = p.AllowUntrustedCertificate;
        _thumb.Text = p.TrustedThumbprint ?? "";
        _wide.IsChecked = p.Wide;
        _device.Text = p.DeviceName;
        _prefix.Text = p.DevicePrefix;
        _autoConnect.IsChecked = p.AutoConnect;
        _autoReconnect.IsChecked = p.AutoReconnect;
        Select(_codePage, p.CodePage);
        UpdatePreview();
    }

    private static void Select(ComboBox box, object tag)
    {
        foreach (ComboBoxItem item in box.Items)
            if (Equals(item.Tag, tag)) { box.SelectedItem = item; return; }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private void UpdatePreview()
    {
        var tmp = new SessionProfile { DeviceName = _device.Text.Trim(), DevicePrefix = _prefix.Text.Trim() };
        _devicePreview.Text = $"This PC will use: {tmp.ResolveDeviceName(1)}";
    }

    private void Collect()
    {
        var p = _current;
        p.Name = string.IsNullOrWhiteSpace(_name.Text) ? p.Name : _name.Text.Trim();
        p.Host = _host.Text.Trim();
        p.Port = int.TryParse(_port.Text.Trim(), out var port) && port is > 0 and < 65536 ? port : (_tls.IsChecked == true ? 992 : 23);
        p.UseTls = _tls.IsChecked == true;
        p.AllowUntrustedCertificate = _untrusted.IsChecked == true;
        p.TrustedThumbprint = string.IsNullOrWhiteSpace(_thumb.Text) ? null : _thumb.Text.Trim();
        p.Wide = _wide.IsChecked == true;
        p.DeviceName = _device.Text.Trim();
        p.DevicePrefix = _prefix.Text.Trim();
        p.AutoConnect = _autoConnect.IsChecked == true;
        p.AutoReconnect = _autoReconnect.IsChecked == true;
        if (_codePage.SelectedItem is ComboBoxItem { Tag: int cp }) p.CodePage = cp;
    }

    private void Save()
    {
        Collect();
        var names = _profiles.Select(p => p.Name).ToList();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
        {
            MessageBox.Show(this, "Each profile needs a different name.", "Ham 5250");
            return;
        }
        _settings.Profiles = _profiles;
        if (!_profiles.Any(p => p.Name == _settings.DefaultProfile)) _settings.DefaultProfile = _profiles[0].Name;
        if (_enterKey.SelectedItem is ComboBoxItem { Tag: EnterKeyMode m }) _settings.EnterKey = m;
        _settings.ShowColumnSeparators = _colSep.IsChecked == true;
        _settings.Hotspots = _hotspots.IsChecked == true;
        _settings.CursorBlink = _blink.IsChecked == true;
        _settings.Crosshair = _cross.IsChecked == true;
        _settings.ConfirmOnClose = _confirm.IsChecked == true;
        DialogResult = true;
    }
}
