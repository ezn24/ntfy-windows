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
        if (!string.IsNullOrWhiteSpace(options?.Title)) req.Headers.Add("Title", options!.Title);
        if (options?.Priority is not null) req.Headers.Add("Priority", options.Priority.Value.ToString());
        if (!string.IsNullOrWhiteSpace(options?.TagsCsv)) req.Headers.Add("Tags", options!.TagsCsv);
        if (!string.IsNullOrWhiteSpace(options?.ClickUrl)) req.Headers.Add("Click", options!.ClickUrl);
        if (!string.IsNullOrWhiteSpace(options?.AttachUrl)) req.Headers.Add("Attach", options!.AttachUrl);
        if (!string.IsNullOrWhiteSpace(options?.Delay)) req.Headers.Add("Delay", options!.Delay);
        if (!string.IsNullOrWhiteSpace(options?.Email)) req.Headers.Add("Email", options!.Email);
        if (!string.IsNullOrWhiteSpace(options?.Call)) req.Headers.Add("Call", options!.Call);
        if (options?.Markdown is not null) req.Headers.Add("Markdown", options.Markdown.Value ? "yes" : "no");

        using var res = await _httpClient.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }

    public async Task<List<NtfyMessage>> FetchTopicHistoryAsync(ServerProfile server, string topic, int limit = 200, CancellationToken token = default)
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
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("event", out var ev) && ev.GetString() is "open" or "keepalive") continue;

            list.Add(new NtfyMessage
            {
                Id = root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                Topic = root.TryGetProperty("topic", out var tp) ? tp.GetString() ?? topic : topic,
                Title = root.TryGetProperty("title", out var t) ? t.GetString() : null,
                Message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "",
                Priority = root.TryGetProperty("priority", out var p) && p.TryGetInt32(out var pr) ? pr : 0,
                Timestamp = root.TryGetProperty("time", out var ts) && ts.TryGetInt64(out var unix)
                    ? DateTimeOffset.FromUnixTimeSeconds(unix)
                    : DateTimeOffset.Now
            });
        }

        return list.OrderByDescending(x => x.Timestamp).Take(limit).ToList();
    }

    public async IAsyncEnumerable<NtfyMessage> SubscribeJsonStreamAsync(ServerProfile server, string topic, [EnumeratorCancellation] CancellationToken token = default)
    {
        var url = $"{server.BaseUrl.TrimEnd('/')}/{topic}/json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(req, server);

        using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("event", out var ev) && ev.GetString() is "open" or "keepalive") continue;

            yield return new NtfyMessage
            {
                Id = root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                Topic = root.TryGetProperty("topic", out var tp) ? tp.GetString() ?? topic : topic,
                Title = root.TryGetProperty("title", out var t) ? t.GetString() : null,
                Message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "",
                Priority = root.TryGetProperty("priority", out var p) && p.TryGetInt32(out var pr) ? pr : 0,
                Timestamp = root.TryGetProperty("time", out var ts) && ts.TryGetInt64(out var unix)
                    ? DateTimeOffset.FromUnixTimeSeconds(unix)
                    : DateTimeOffset.Now
            };
        }
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
