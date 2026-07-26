using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Ntfy.Windows.Services;

public static class EmojiTagFormatter
{
    private sealed class EmojiEntry
    {
        public string Emoji { get; set; } = string.Empty;
        public string[] Aliases { get; set; } = [];
    }

    private static readonly Lazy<IReadOnlyDictionary<string, string>> EmojiByAlias = new(LoadEmojiMap);

    public static string Format(string? tagsCsv)
    {
        if (string.IsNullOrWhiteSpace(tagsCsv)) return string.Empty;

        return string.Join(" ", tagsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(FormatTag)
            .Where(tag => tag.Length > 0));
    }

    private static string FormatTag(string tag)
    {
        var normalized = tag.Trim().Trim(':');
        if (normalized.Length == 0) return string.Empty;
        if (EmojiByAlias.Value.TryGetValue(normalized, out var emoji)) return emoji;

        // Preserve literal emoji and other non-ASCII custom tags as they were sent.
        return normalized.Any(character => character > 0x7f) ? normalized : $"#{normalized}";
    }

    private static IReadOnlyDictionary<string, string> LoadEmojiMap()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["white_check_mark"] = "\u2705"
        };

        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "emoji.json");
            using var stream = File.OpenRead(path);
            var entries = JsonSerializer.Deserialize<List<EmojiEntry>>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Emoji)) continue;
                foreach (var alias in entry.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
                    result[alias] = entry.Emoji;
            }
        }
        catch
        {
            // The built-in fallback keeps common ntfy tags readable if the asset is unavailable.
        }

        return result;
    }
}