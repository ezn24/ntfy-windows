using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Ntfy.Windows.Services;

namespace Ntfy.Windows.Models;

public sealed class NtfyMessage : INotifyPropertyChanged
{
    private bool _isRead;
    private bool _showTopic;

    public string Id { get; set; } = string.Empty;
    public string Event { get; set; } = "message";
    public string Topic { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string? TagsCsv { get; set; }
    public string? ClickUrl { get; set; }
    public string? AttachmentUrl { get; set; }
    public string? AttachmentName { get; set; }
    public string? IconUrl { get; set; }
    public string? ActionsJson { get; set; }
    public bool Markdown { get; set; }
    public DateTimeOffset? Expires { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public bool IsRead
    {
        get => _isRead;
        set
        {
            if (_isRead == value) return;
            _isRead = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UnreadIndicator));
        }
    }

    public bool ShowTopic
    {
        get => _showTopic;
        set
        {
            if (_showTopic == value) return;
            _showTopic = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TopicVisibility));
        }
    }

    public Microsoft.UI.Xaml.Visibility TopicVisibility => ShowTopic ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public string DisplayTimestamp => Timestamp.ToLocalTime().ToString(RuntimePreferences.DateTimeFormat);
    public string UnreadIndicator => IsRead ? string.Empty : "\u2022";
    public Visibility IconVisibility => TryGetIconUri(out _) ? Visibility.Visible : Visibility.Collapsed;
    public ImageSource? IconSource => TryGetIconUri(out var uri) ? new BitmapImage(uri) : null;

    private bool TryGetIconUri(out Uri? uri)
    {
        if (Uri.TryCreate(IconUrl, UriKind.Absolute, out var candidate) &&
            (candidate.Scheme == Uri.UriSchemeHttp || candidate.Scheme == Uri.UriSchemeHttps))
        {
            uri = candidate;
            return true;
        }

        uri = null;
        return false;
    }
    public string MetadataSummary => string.Join("  ", new[]
    {
        string.IsNullOrWhiteSpace(TagsCsv) ? null : EmojiTagFormatter.Format(TagsCsv),
        string.IsNullOrWhiteSpace(ClickUrl) ? null : Localizer.T("HasClickLink"),
        string.IsNullOrWhiteSpace(AttachmentName) && string.IsNullOrWhiteSpace(AttachmentUrl) ? null : $"{Localizer.T("Attachment")}: {AttachmentName ?? AttachmentUrl}",
        string.IsNullOrWhiteSpace(ActionsJson) ? null : Localizer.T("HasActions"),
        Markdown ? "Markdown" : null
    }.Where(x => !string.IsNullOrWhiteSpace(x)));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
