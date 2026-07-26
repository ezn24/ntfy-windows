using System.Text.Json.Serialization;

namespace Ntfy.Windows.Models;

public sealed class NtfyAccountSubscription
{
    [JsonPropertyName("base_url")]
    public string BaseUrl { get; set; } = string.Empty;

    [JsonPropertyName("topic")]
    public string Topic { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
}