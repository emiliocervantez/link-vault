using System.Text.Json;

namespace LinkVault.Core;

/// <summary>
/// On-disk layout under the root directory:
/// settings.json, settings.corrupt.json (only after a failed load), icons\{site}.png
/// </summary>
public sealed class VaultStorage
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _settingsFile;

    public VaultStorage(string rootDir)
    {
        RootDir = rootDir;
        IconsDir = Path.Combine(rootDir, "icons");
        _settingsFile = Path.Combine(rootDir, "settings.json");
        Directory.CreateDirectory(IconsDir);
    }

    public string RootDir { get; }
    public string IconsDir { get; }
    /// <summary>Copy of a settings.json that could not be read, made before defaults replace it.</summary>
    public string CorruptBackupFile => Path.Combine(RootDir, "settings.corrupt.json");

    public static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkVault");

    public Settings LoadSettings()
    {
        if (!File.Exists(_settingsFile)) return new Settings();
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsFile), Json) ?? new Settings();
        }
        catch (JsonException)
        {
            // The next save would overwrite the user's links; keep the unreadable file for manual recovery.
            File.Copy(_settingsFile, CorruptBackupFile, overwrite: true);
            return new Settings();
        }
    }

    public void SaveSettings(Settings settings)
    {
        // Write to a temp file and swap so a crash mid-write never leaves a truncated file.
        var tmp = _settingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
        File.Move(tmp, _settingsFile, overwrite: true);
    }

    /// <summary>Writes the settings to a file of the user's choosing, in the settings.json format.</summary>
    public static void Export(Settings settings, string path) => File.WriteAllText(path, JsonSerializer.Serialize(settings, Json));

    /// <summary>Reads settings written by <see cref="Export"/>. Throws JsonException or IOException when the file cannot be used.</summary>
    public static Settings Import(string path) =>
        JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? throw new JsonException("The file holds no settings.");
}
