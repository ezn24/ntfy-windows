using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Ntfy.Windows.Models;

namespace Ntfy.Windows.Services;

public sealed class AppSettingsService
{
    private readonly string _filePath;

    public AppSettingsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NtfyWindows");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_filePath)) return new AppSettings();
        var json = await File.ReadAllTextAsync(_filePath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        MigrateLegacySettings(json, settings);
        return settings;
    }

    private static void MigrateLegacySettings(string json, AppSettings settings)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty(nameof(AppSettings.CloseToTray), out _) &&
                root.TryGetProperty("QuitOnClose", out var quitOnClose) &&
                quitOnClose.ValueKind is JsonValueKind.True or JsonValueKind.False)
                settings.CloseToTray = !quitOnClose.GetBoolean();

            if (!root.TryGetProperty(nameof(AppSettings.StartMinimizedToTray), out _) &&
                root.TryGetProperty("StartHidden", out var startHidden) &&
                startHidden.ValueKind is JsonValueKind.True or JsonValueKind.False)
                settings.StartMinimizedToTray = startHidden.GetBoolean();
        }
        catch
        {
            // Keep deserialized defaults if migration cannot inspect the file.
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json);
    }
}
