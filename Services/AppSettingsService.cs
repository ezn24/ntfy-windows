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
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json);
    }
}
