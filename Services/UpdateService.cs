using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ntfy.Windows.Models;

namespace Ntfy.Windows.Services;

public sealed class UpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/ezn24/ntfy-windows/releases/latest";

    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ntfy-windows", CurrentVersion.ToString(3)));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseApi, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("The update feed is unavailable. GitHub Releases must be publicly accessible.");

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("GitHub returned an empty release response.");

        var latestVersion = ParseVersion(release.TagName);
        if (latestVersion <= CurrentVersion)
            return null;

        var assetName = $"ntfy-windows-win-{GetArchitectureName()}-setup.exe";
        var asset = release.Assets.FirstOrDefault(x =>
            x.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The release does not contain {assetName}.");

        var sha256 = NormalizeDigest(asset.Digest);
        if (string.IsNullOrWhiteSpace(sha256))
            sha256 = await ReadChecksumAsync(release, assetName, cancellationToken);

        if (string.IsNullOrWhiteSpace(sha256))
            throw new InvalidOperationException($"The release does not provide a SHA-256 checksum for {assetName}.");

        return new UpdateInfo(
            CurrentVersion,
            latestVersion,
            release.TagName,
            release.HtmlUrl,
            asset.Name,
            asset.BrowserDownloadUrl,
            sha256);
    }

    public async Task<string> DownloadAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var updateDirectory = Path.Combine(
            Path.GetTempPath(),
            "ntfy-windows",
            "updates",
            update.TagName);
        Directory.CreateDirectory(updateDirectory);

        var destinationPath = Path.Combine(updateDirectory, update.AssetName);
        var temporaryPath = destinationPath + ".download";
        File.Delete(temporaryPath);

        using var response = await _httpClient.GetAsync(
            update.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         81920,
                         useAsync: true))
        {
            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                if (totalBytes > 0)
                    progress?.Report(downloaded * 100d / totalBytes.Value);
            }
        }

        var actualSha256 = await ComputeSha256Async(temporaryPath, cancellationToken);
        if (!actualSha256.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temporaryPath);
            throw new InvalidOperationException("The downloaded update failed SHA-256 verification.");
        }

        File.Move(temporaryPath, destinationPath, overwrite: true);
        progress?.Report(100);
        return destinationPath;
    }

    public static void LaunchInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART",
            UseShellExecute = true
        });
    }

    private async Task<string> ReadChecksumAsync(
        GitHubRelease release,
        string assetName,
        CancellationToken cancellationToken)
    {
        var checksumAsset = release.Assets.FirstOrDefault(x =>
            x.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        if (checksumAsset is null)
            return string.Empty;

        var content = await _httpClient.GetStringAsync(checksumAsset.BrowserDownloadUrl, cancellationToken);
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[^1].TrimStart('*').Equals(assetName, StringComparison.OrdinalIgnoreCase))
                return parts[0];
        }

        return string.Empty;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
            return string.Empty;

        return digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest[7..]
            : digest;
    }

    private static Version ParseVersion(string tagName)
    {
        var value = tagName.Trim().TrimStart('v', 'V');
        var separatorIndex = value.IndexOfAny(['-', '+']);
        if (separatorIndex >= 0)
            value = value[..separatorIndex];

        if (!Version.TryParse(value, out var version))
            throw new InvalidOperationException($"The release tag '{tagName}' is not a valid version.");

        return version;
    }

    private static string GetArchitectureName() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        _ => "x64"
    };

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }
    }
}
