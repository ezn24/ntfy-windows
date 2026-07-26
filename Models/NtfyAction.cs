using System.Collections.Generic;

namespace Ntfy.Windows.Models;

public sealed class NtfyAction
{
    public string Action { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Method { get; set; }
    public string? Body { get; set; }
    public string? Intent { get; set; }
    public bool Clear { get; set; }
    public Dictionary<string, string> Headers { get; set; } = [];
}
