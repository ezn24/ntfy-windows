using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Ntfy.WinUI.Client.Models;

namespace Ntfy.WinUI.Client.Services;

public sealed class MessageLogService
{
    private readonly string _filePath;

    public MessageLogService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NtfyWinUI");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "messages.json");
    }

    public async Task<List<NtfyMessage>> LoadAsync(int max = 500)
    {
        if (!File.Exists(_filePath)) return [];
        var json = await File.ReadAllTextAsync(_filePath);
        var list = JsonSerializer.Deserialize<List<NtfyMessage>>(json) ?? [];
        return list.OrderByDescending(x => x.Timestamp).Take(max).ToList();
    }

    public async Task SaveAsync(IEnumerable<NtfyMessage> messages, int max = 500)
    {
        var trimmed = messages.OrderByDescending(x => x.Timestamp).Take(max).ToList();
        var json = JsonSerializer.Serialize(trimmed, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json);
    }
}
