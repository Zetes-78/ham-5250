using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hamricks5250.Core;

/// <summary>A saved connection (one per IBM i system or purpose).</summary>
public sealed class SessionProfile
{
    public string Name { get; set; } = "Hamrick's IBM i";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 992;
    public bool UseTls { get; set; } = true;
    public bool AllowUntrustedCertificate { get; set; }
    public string? TrustedThumbprint { get; set; }
    /// <summary>IBM i device name. Empty = automatic (prefix + PC name, max 10 characters).</summary>
    public string DeviceName { get; set; } = "";
    public string DevicePrefix { get; set; } = "H";
    public bool Wide { get; set; }
    public int CodePage { get; set; } = 37;
    public bool AutoConnect { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;

    public string ResolveDeviceName(int sessionNumber)
    {
        string baseName = !string.IsNullOrWhiteSpace(DeviceName)
            ? DeviceName
            : DevicePrefix + Environment.MachineName;
        baseName = new string(baseName.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (baseName.Length == 0 || !char.IsLetter(baseName[0])) baseName = "H" + baseName;
        if (sessionNumber <= 1) return baseName.Length > 10 ? baseName[..10] : baseName;
        string suffix = sessionNumber.ToString();
        int keep = Math.Min(baseName.Length, 10 - suffix.Length);
        return baseName[..keep] + suffix;
    }

    public ConnectionOptions ToOptions(int sessionNumber) => new()
    {
        Host = Host,
        Port = Port,
        UseTls = UseTls,
        AllowUntrustedCertificate = AllowUntrustedCertificate,
        TrustedThumbprint = TrustedThumbprint,
        DeviceName = ResolveDeviceName(sessionNumber),
        Wide = Wide,
        CodePage = CodePage,
    };
}

public enum EnterKeyMode
{
    /// <summary>Enter key = Enter (send), Right Ctrl = Field Exit. Friendliest for Windows users.</summary>
    Windows,
    /// <summary>Classic IBM PC5250 layout: Right Ctrl = Enter (send), Enter key = Field Exit.</summary>
    Classic,
}

public sealed class AppSettings
{
    public List<SessionProfile> Profiles { get; set; } = new() { new SessionProfile() };
    public string DefaultProfile { get; set; } = "Hamrick's IBM i";
    public EnterKeyMode EnterKey { get; set; } = EnterKeyMode.Windows;
    public double FontScale { get; set; } = 1.0;
    public bool ShowColumnSeparators { get; set; } = true;
    public bool Hotspots { get; set; } = true;
    public bool CursorBlink { get; set; } = true;
    public bool Crosshair { get; set; }
    public bool Kiosk { get; set; }
    public bool ConfirmOnClose { get; set; } = true;
    /// <summary>Set by IT in the machine-wide file: users can't edit connection settings.</summary>
    public bool LockProfiles { get; set; }

    [JsonIgnore] public string? LoadedFrom { get; private set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string MachinePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Hamricks5250", "settings.json");

    public static string UserPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hamricks5250", "settings.json");

    /// <summary>User settings win, unless IT locked profiles in the machine-wide file.</summary>
    public static AppSettings Load()
    {
        var machine = TryRead(MachinePath);
        var user = TryRead(UserPath);
        if (machine is { LockProfiles: true })
        {
            if (user != null)
            {
                machine.FontScale = user.FontScale;
                machine.ShowColumnSeparators = user.ShowColumnSeparators;
                machine.Crosshair = user.Crosshair;
                machine.CursorBlink = user.CursorBlink;
            }
            return machine;
        }
        return user ?? machine ?? new AppSettings();
    }

    public static AppSettings? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json);
            if (s != null) s.LoadedFrom = path;
            return s;
        }
        catch { return null; }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserPath)!);
        File.WriteAllText(UserPath, JsonSerializer.Serialize(this, Json));
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public SessionProfile GetProfile(string? name = null) =>
        Profiles.FirstOrDefault(p => p.Name == (name ?? DefaultProfile)) ?? Profiles.FirstOrDefault() ?? new SessionProfile();
}
