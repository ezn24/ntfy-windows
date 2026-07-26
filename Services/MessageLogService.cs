using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ntfy.Windows.Models;

namespace Ntfy.Windows.Services;

public sealed class MessageLogService
{
    private static readonly SemaphoreSlim FileLock = new(1, 1);
    private readonly string _filePath;

    public MessageLogService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NtfyWindows");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "messages.json");
    }

    public async Task<List<NtfyMessage>> LoadAsync(int max = 2000)
    {
        await FileLock.WaitAsync();
        try
        {
            if (!File.Exists(_filePath)) return [];
            var json = await File.ReadAllTextAsync(_filePath);
            var list = JsonSerializer.Deserialize<List<NtfyMessage>>(json) ?? [];
            return list.OrderByDescending(x => x.Timestamp).Take(max).ToList();
        }
        finally { FileLock.Release(); }
    }

    public async Task SaveAsync(IEnumerable<NtfyMessage> messages, int max = 2000)
    {
        var trimmed = messages.OrderByDescending(x => x.Timestamp).Take(max).ToList();
        await FileLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(trimmed, new JsonSerializerOptions { WriteIndented = true });
            var tempPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, _filePath, true);
        }
        finally { FileLock.Release(); }
    }
}
