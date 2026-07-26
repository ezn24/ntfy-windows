using Microsoft.UI.Xaml;
using Ntfy.Windows.Services;

namespace Ntfy.Windows.Models;

public sealed class TopicListItem
{
    public string Topic { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int UnreadCount { get; set; }
    public bool IsAllTopics { get; set; }
    public bool IsMuted { get; set; }
    public string UnreadText => UnreadCount > 99 ? "99+" : UnreadCount > 0 ? UnreadCount.ToString() : string.Empty;
    public string MuteGlyph => IsMuted ? "\uE74F" : "\uE767";
    public Visibility BadgeVisibility => UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MutedVisibility => IsMuted ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ActionVisibility => IsAllTopics ? Visibility.Collapsed : Visibility.Visible;
    public string PullHistoryTooltip => Localizer.T("PullTopicHistory");
    public string SendTestTooltip => Localizer.T("SendTestMessage");
    public string MuteTooltip => Localizer.T(IsMuted ? "UnmuteTopic" : "MuteTopic");
    public string MutedIndicatorTooltip => Localizer.T("TopicMuted");
    public string DeleteTooltip => Localizer.T("DeleteTopic");
}
