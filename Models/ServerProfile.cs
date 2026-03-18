namespace Ntfy.WinUI.Client.Models;

public enum AuthMode
{
    None,
    Bearer,
    Basic
}

public sealed class ServerProfile
{
    public string Name { get; set; } = "Default";
    public string BaseUrl { get; set; } = "https://ntfy.sh";
    public AuthMode AuthMode { get; set; } = AuthMode.None;
    public string? Username { get; set; }
    public string? SecretRef { get; set; } // DPAPI key reference
}
