using System;

namespace Ntfy.WinUI.Client.Models;

public sealed class NtfyMessage
{
    public string Id { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Priority { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
}
