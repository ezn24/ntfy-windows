using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Ntfy.Windows.Models;

namespace Ntfy.Windows.Services;

public sealed class TemplateService
{
    private readonly string _filePath;

    public TemplateService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NtfyWindows");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "templates.json");
    }

    public async Task<List<MessageTemplate>> LoadAsync()
    {
        if (!File.Exists(_filePath)) return [];
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<MessageTemplate>>(json) ?? [];
    }

    public async Task SaveAsync(IEnumerable<MessageTemplate> templates)
    {
        var json = JsonSerializer.Serialize(templates, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json);
    }
}
