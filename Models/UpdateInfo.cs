namespace Ntfy.Windows.Models;

public sealed record UpdateInfo(
    Version CurrentVersion,
    Version LatestVersion,
    string TagName,
    string ReleaseUrl,
    string AssetName,
    string DownloadUrl,
    string Sha256);
