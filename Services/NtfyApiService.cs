using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ntfy.Windows.Models;

namespace Ntfy.Windows.Services;

public sealed class NtfyApiService
{
    private readonly HttpClient _httpClient = new();

    public async Task PublishAsync(ServerProfile server, string topic, string message, PublishOptions? options = null)
    {
        var url = $"{server.BaseUrl.TrimEnd('/')}/{topic}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(message, Encoding.UTF8, "text/plain")
        };

        ApplyAuth(req, server);
        ApplyPublishHeaders(req, options);
        using var res = await _httpClient.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }

    public async Task PublishFileAsync(ServerProfile server, string topic, Stream stream, string fileName, PublishOptions? options = null, CancellationToken token = default)
    {
        var url = $"{server.BaseUrl.TrimEnd('/')}/{topic}";
        using var req = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StreamContent(stream)
        };
        AddNtfyHeader(req, "Filename", fileName);
        ApplyAuth(req, server);
        ApplyPublishHeaders(req, options, includeFilename: false);
        using var res = await _httpClient.SendAsync(req, token);
        res.EnsureSuccessStatusCode();
    }

    public async Task TestConnectionAsync(ServerProfile server, string? topic = null, CancellationToken token = default)
    {
        var path = string.IsNullOrWhiteSpace(topic) ? "v1/health" : $"{topic.Trim()}/json?poll=1&since=all";
        var url = $"{server.BaseUrl.TrimEnd('/')}/{path}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(req, server);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        res.EnsureSuccessStatusCode();
    }

    public async Task<List<NtfyAccountSubscription>> FetchAccountSubscriptionsAsync(ServerProfile server, CancellationToken token = default)
    {
        var url = $"{server.BaseUrl.TrimEnd('/')}/v1/account";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(req, server);
        using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (!document.RootElement.TryGetProperty("subscriptions", out var subscriptions) || subscriptions.ValueKind != JsonValueKind.Array)
            return [];

        return JsonSerializer.Deserialize<List<NtfyAccountSubscription>>(subscriptions.GetRawText()) ?? [];
    }

    public async Task<List<NtfyMessage>> FetchTopicHistoryAsync(ServerProfile server, string topic, int limit = 500, CancellationToken token = default)
    {
        var url = $"{server.BaseUrl.TrimEnd('/')}/{topic}/json?poll=1&since=all";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(req, server);
        using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
        res.EnsureSuccessStatusCode();

        var list = new List<NtfyMessage>();
        await using var stream = await res.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream && !token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(token);
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("event", out var ev) && ev.GetString() is "open" or "keepalive") continue;
            list.Add(ParseMessage(root, topic));
        }
        return list.OrderByDescending(x => x.Timestamp).Take(limit).ToList();
    }

    public async IAsyncEnumerable<NtfyMessage> SubscribeJsonStreamAsync(ServerProfile server, string topic, string? sinceId = null, [EnumeratorCancellation] CancellationToken token = default)
    {
        var since = string.IsNullOrWhiteSpace(sinceId) ? string.Empty : $"?since={Uri.EscapeDataString(sinceId)}";
        var url = $"{server.BaseUrl.TrimEnd('/')}/{topic}/json{since}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(req, server);
        using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(token);
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("event", out var ev) && ev.GetString() is "open" or "keepalive") continue;
            yield return ParseMessage(root, topic);
        }
    }

    public IReadOnlyList<NtfyAction> ParseActions(string? actionsJson)
    {
        if (string.IsNullOrWhiteSpace(actionsJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<NtfyAction>>(actionsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch { return []; }
    }

    public async Task ExecuteActionAsync(ServerProfile server, NtfyAction action, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(action.Url)) throw new InvalidOperationException("Action URL is missing.");
        var method = string.IsNullOrWhiteSpace(action.Method) ? HttpMethod.Post : new HttpMethod(action.Method);
        using var req = new HttpRequestMessage(method, action.Url);
        if (action.Body is not null) req.Content = new StringContent(action.Body, Encoding.UTF8, "text/plain");
        foreach (var header in action.Headers)
        {
            if (!req.Headers.TryAddWithoutValidation(header.Key, header.Value))
                req.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        if (Uri.TryCreate(server.BaseUrl, UriKind.Absolute, out var serverUri) &&
            Uri.TryCreate(action.Url, UriKind.Absolute, out var actionUri) &&
            serverUri.Scheme == actionUri.Scheme &&
            serverUri.Host.Equals(actionUri.Host, StringComparison.OrdinalIgnoreCase) &&
            serverUri.Port == actionUri.Port)
        {
            ApplyAuth(req, server);
        }
        using var res = await _httpClient.SendAsync(req, token);
        res.EnsureSuccessStatusCode();
    }

    private static NtfyMessage ParseMessage(JsonElement root, string fallbackTopic)
    {
        string? attachmentUrl = null;
        string? attachmentName = null;
        if (root.TryGetProperty("attachment", out var attachment) && attachment.ValueKind == JsonValueKind.Object)
        {
            attachmentUrl = attachment.TryGetProperty("url", out var url) ? url.GetString() : null;
            attachmentName = attachment.TryGetProperty("name", out var name) ? name.GetString() : null;
        }

        return new NtfyMessage
        {
            Id = root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            Event = root.TryGetProperty("event", out var eventElement) ? eventElement.GetString() ?? "message" : "message",
            Topic = root.TryGetProperty("topic", out var topic) ? topic.GetString() ?? fallbackTopic : fallbackTopic,
            Title = root.TryGetProperty("title", out var title) ? title.GetString() : null,
            Message = root.TryGetProperty("message", out var message) ? message.GetString() ?? "" : "",
            Priority = root.TryGetProperty("priority", out var priority) && priority.TryGetInt32(out var priorityValue) ? priorityValue : 0,
            TagsCsv = root.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array
                ? string.Join(", ", tags.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
                : null,
            ClickUrl = root.TryGetProperty("click", out var click) ? click.GetString() : null,
            AttachmentUrl = attachmentUrl,
            AttachmentName = attachmentName,
            IconUrl = root.TryGetProperty("icon", out var icon) ? icon.GetString() : null,
            ActionsJson = root.TryGetProperty("actions", out var actions) ? actions.GetRawText() : null,
            Markdown = root.TryGetProperty("content_type", out var contentType) && contentType.GetString()?.Equals("text/markdown", StringComparison.OrdinalIgnoreCase) == true,
            Expires = root.TryGetProperty("expires", out var expires) && expires.TryGetInt64(out var expiresUnix) ? DateTimeOffset.FromUnixTimeSeconds(expiresUnix) : null,
            Timestamp = root.TryGetProperty("time", out var timestamp) && timestamp.TryGetInt64(out var unix) ? DateTimeOffset.FromUnixTimeSeconds(unix) : DateTimeOffset.Now
        };
    }

    private static void ApplyPublishHeaders(HttpRequestMessage req, PublishOptions? options, bool includeFilename = true)
    {
        if (!string.IsNullOrWhiteSpace(options?.Title)) AddNtfyHeader(req, "Title", options.Title);
        if (options?.Priority is not null) AddNtfyHeader(req, "Priority", options.Priority.Value.ToString());
        if (!string.IsNullOrWhiteSpace(options?.TagsCsv)) AddNtfyHeader(req, "Tags", options.TagsCsv);
        if (!string.IsNullOrWhiteSpace(options?.ClickUrl)) AddNtfyHeader(req, "Click", options.ClickUrl);
        if (!string.IsNullOrWhiteSpace(options?.AttachUrl)) AddNtfyHeader(req, "Attach", options.AttachUrl);
        if (includeFilename && !string.IsNullOrWhiteSpace(options?.Filename)) AddNtfyHeader(req, "Filename", options.Filename);
        if (!string.IsNullOrWhiteSpace(options?.IconUrl)) AddNtfyHeader(req, "Icon", options.IconUrl);
        if (!string.IsNullOrWhiteSpace(options?.Actions)) AddNtfyHeader(req, "Actions", options.Actions);
        if (!string.IsNullOrWhiteSpace(options?.Delay)) AddNtfyHeader(req, "Delay", options.Delay);
        if (!string.IsNullOrWhiteSpace(options?.Email)) AddNtfyHeader(req, "Email", options.Email);
        if (!string.IsNullOrWhiteSpace(options?.Call)) AddNtfyHeader(req, "Call", options.Call);
        if (options?.Markdown is not null) AddNtfyHeader(req, "Markdown", options.Markdown.Value ? "yes" : "no");
    }

    private static void AddNtfyHeader(HttpRequestMessage request, string name, string value)
    {
        var headerValue = value.All(character => character <= 0x7f)
            ? value
            : $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";
        request.Headers.TryAddWithoutValidation(name, headerValue);
    }

    private static void ApplyAuth(HttpRequestMessage req, ServerProfile server)
    {
        if (server.AuthMode == AuthMode.Bearer && !string.IsNullOrWhiteSpace(server.SecretRef))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.SecretRef);
        if (server.AuthMode == AuthMode.Basic && !string.IsNullOrWhiteSpace(server.Username) && !string.IsNullOrWhiteSpace(server.SecretRef))
        {
            var raw = Encoding.UTF8.GetBytes($"{server.Username}:{server.SecretRef}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
        }
    }
}
