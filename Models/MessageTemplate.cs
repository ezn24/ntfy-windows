using System;
using System.Collections.Generic;

namespace Ntfy.WinUI.Client.Models;

public sealed class MessageTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Body { get; set; } = string.Empty;
    public int Priority { get; set; } = 3;
    public string? TagsCsv { get; set; }

    public string Render(Dictionary<string, string> tokens)
    {
        var output = Body;
        foreach (var (k, v) in tokens)
            output = output.Replace("{{" + k + "}}", v, StringComparison.OrdinalIgnoreCase);
        return output;
    }
}
