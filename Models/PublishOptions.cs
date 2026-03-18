namespace Ntfy.WinUI.Client.Models;

public sealed class PublishOptions
{
    public string? Title { get; set; }
    public int? Priority { get; set; }
    public string? TagsCsv { get; set; }
    public string? ClickUrl { get; set; }
    public string? AttachUrl { get; set; }
    public string? Delay { get; set; }
    public string? Email { get; set; }
    public string? Call { get; set; }
    public bool? Markdown { get; set; }
}
