using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Ntfy.Windows.Models;
using Ntfy.Windows.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Ntfy.Windows.Views;

public sealed partial class InboxPage : Page
{
    private readonly NtfyApiService _api = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly CredentialVaultService _vault = new();
    private readonly MessageLogService _messageLogService = new();
    private AppSettings _settings = new();

    private readonly Dictionary<string, CancellationTokenSource> _topicCts = [];
    private readonly DispatcherTimer _singleSelectFadeTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };

    public ObservableCollection<NtfyMessage> Messages { get; } = [];
    public ObservableCollection<string> Topics { get; } = [];
    public List<MessageTemplate> Templates { get; private set; } = [];

    public InboxPage()
    {
        InitializeComponent();
        RuntimePreferences.Changed += ApplyLocalizedUi;
        _singleSelectFadeTimer.Tick += SingleSelectFadeTimer_Tick;
        MessagesList.SelectionMode = ListViewSelectionMode.None;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        _settings = await _settingsService.LoadAsync();

        Topics.Clear();
        foreach (var t in _settings.Topics.Distinct(StringComparer.OrdinalIgnoreCase))
            Topics.Add(t);

        TopicList.ItemsSource = Topics;
        if (!string.IsNullOrWhiteSpace(_settings.ActiveTopic))
            TopicList.SelectedItem = _settings.ActiveTopic;

        var history = await _messageLogService.LoadAsync();
        foreach (var h in history) Messages.Add(h);

        Templates = BuildNtfyStyleTemplates();
        TemplateBox.ItemsSource = Templates;
        TemplateBox.DisplayMemberPath = nameof(MessageTemplate.Name);

        ApplyLocalizedUi();
        await StartAllSubscriptionsAsync();
    }

    private void ApplyLocalizedUi()
    {
        Localizer.Reload();
        TopicsTitleText.Text = Localizer.T("TopicNav");
        NewTopicBox.PlaceholderText = Localizer.T("TopicName");
        CopySelectedButton.Content = Localizer.T("CopySelected");
        DeleteSelectedButton.Content = Localizer.T("DeleteSelected");
        ClearHistoryButton.Content = Localizer.T("ClearHistory");
        MultiSelectLabel.Text = Localizer.T("MultiSelect");
        TemplateBox.PlaceholderText = Localizer.T("TypeTemplate");
        ComposeBox.PlaceholderText = Localizer.T("WriteMessage");
        SendButton.Content = Localizer.T("Send");
    }

    private static List<MessageTemplate> BuildNtfyStyleTemplates() =>
    [
        new() { Name = "Plain", Topic = "", Body = "", Priority = 3 },
        new() { Name = "Warning (priority 4)", Topic = "", Title = "Warning", Body = "", Priority = 4, TagsCsv = "warning" },
        new() { Name = "Alert (priority 5)", Topic = "", Title = "Alert", Body = "", Priority = 5, TagsCsv = "rotating_light" },
        new() { Name = "Success", Topic = "", Title = "Success", Body = "", Priority = 3, TagsCsv = "white_check_mark" },
        new() { Name = "Markdown", Topic = "", Body = "**bold** _markdown_", Priority = 3, TagsCsv = "memo" },
        new() { Name = "Link / click", Topic = "", Title = "Open link", Body = "https://example.com", Priority = 3, TagsCsv = "link" },
        new() { Name = "Delayed (10m)", Topic = "", Title = "Reminder", Body = "Do the thing", Priority = 3, TagsCsv = "alarm_clock" },
    ];

    private async Task StartAllSubscriptionsAsync()
    {
        foreach (var c in _topicCts.Values) c.Cancel();
        _topicCts.Clear();

        foreach (var topic in Topics)
            _topicCts[topic] = StartTopicSubscription(topic);

        StatusText.Text = Topics.Count == 0 ? Localizer.T("NoTopicYet") : string.Format(Localizer.T("SubscribedCount"), Topics.Count);
        await _settingsService.SaveAsync(_settings);
    }

    private CancellationTokenSource StartTopicSubscription(string topic)
    {
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                var server = ResolveProfileWithSecret(_settings.Server);
                await foreach (var msg in _api.SubscribeJsonStreamAsync(server, topic, cts.Token))
                {
                    await DispatcherQueue.EnqueueAsync(() =>
                    {
                        Messages.Insert(0, msg);
                        if (Messages.Count > 500) Messages.RemoveAt(Messages.Count - 1);
                    });
                    DesktopNotificationService.ShowMessage(msg);
                    await _messageLogService.SaveAsync(Messages);
                }
            }
            catch (Exception ex)
            {
                await DispatcherQueue.EnqueueAsync(() => StatusText.Text = $"{topic}: {ex.Message}");
            }
        });
        return cts;
    }

    private ServerProfile ResolveProfileWithSecret(ServerProfile source)
    {
        var resolved = new ServerProfile
        {
            Name = source.Name,
            BaseUrl = source.BaseUrl,
            AuthMode = source.AuthMode,
            Username = source.Username,
            SecretRef = source.SecretRef
        };

        if (!string.IsNullOrWhiteSpace(source.SecretRef))
        {
            var secret = _vault.ReadSecret(source.SecretRef!);
            if (!string.IsNullOrWhiteSpace(secret))
                resolved.SecretRef = secret;
        }

        return resolved;
    }

    private async void AddTopic_Click(object sender, RoutedEventArgs e)
    {
        var t = NewTopicBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(t) || Topics.Any(x => x.Equals(t, StringComparison.OrdinalIgnoreCase))) return;

        Topics.Add(t);
        _settings.Topics = Topics.ToList();
        NewTopicBox.Text = string.Empty;

        _topicCts[t] = StartTopicSubscription(t);
        await _settingsService.SaveAsync(_settings);
    }

    private async Task RemoveTopicAsync(string selected)
    {
        Topics.Remove(selected);
        if (_topicCts.TryGetValue(selected, out var cts))
        {
            cts.Cancel();
            _topicCts.Remove(selected);
        }

        _settings.Topics = Topics.ToList();
        if (_settings.ActiveTopic == selected)
            _settings.ActiveTopic = Topics.FirstOrDefault() ?? string.Empty;

        await _settingsService.SaveAsync(_settings);
    }

    private async void DeleteTopicInline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.DataContext is not string topic) return;
        await RemoveTopicAsync(topic);
    }

    private async void TopicList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TopicList.SelectedItem is not string selected) return;
        _settings.ActiveTopic = selected;
        await _settingsService.SaveAsync(_settings);

        try
        {
            var server = ResolveProfileWithSecret(_settings.Server);
            var all = await _api.FetchTopicHistoryAsync(server, selected, 300);
            Messages.Clear();
            foreach (var m in all) Messages.Add(m);

            ClearMessageSelectionSafe();
            ResetAllVisibleFrames();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void ApplyTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateBox.SelectedItem is not MessageTemplate t) return;
        ComposeBox.Text = t.Body;
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var topic = _settings.ActiveTopic;
            if (string.IsNullOrWhiteSpace(topic)) { StatusText.Text = Localizer.T("TopicRequired"); return; }
            var message = ComposeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(message)) return;

            var server = ResolveProfileWithSecret(_settings.Server);
            var options = BuildOptionsFromTemplate(TemplateBox.SelectedItem as MessageTemplate, message);

            await _api.PublishAsync(server, topic, message, options);

            StatusText.Text = Localizer.T("Sent");
            Messages.Insert(0, new NtfyMessage
            {
                Topic = topic,
                Title = options.Title,
                Message = message,
                Priority = options.Priority ?? 3,
                Timestamp = DateTimeOffset.Now
            });
            await _messageLogService.SaveAsync(Messages);

            if (!Topics.Any(x => x.Equals(topic, StringComparison.OrdinalIgnoreCase)))
            {
                Topics.Add(topic);
                _settings.Topics = Topics.ToList();
                _topicCts[topic] = StartTopicSubscription(topic);
            }

            _settings.ActiveTopic = topic;
            await _settingsService.SaveAsync(_settings);
            ComposeBox.Text = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void MultiSelectSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        _singleSelectFadeTimer.Stop();
        ClearMessageSelectionSafe();
        MessagesList.SelectionMode = MultiSelectSwitch.IsOn ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
        ResetAllVisibleFrames();
    }

    private void MessagesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        try
        {
            if (e.ClickedItem is not NtfyMessage m) return;

            var text = string.IsNullOrWhiteSpace(m.Title)
                ? $"[{m.Topic}] {m.Message}"
                : $"[{m.Topic}] {m.Title}\n{m.Message}";

            var dp = new DataPackage();
            dp.SetText(text);
            Clipboard.SetContent(dp);
            StatusText.Text = Localizer.T("CopiedToClipboard");

            if (!MultiSelectSwitch.IsOn)
            {
                SetItemHoverFrame(m, true);
                _singleSelectFadeTimer.Stop();
                _singleSelectFadeTimer.Start();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = MessagesList.SelectedItems.Cast<NtfyMessage>().ToList();
        if (selected.Count == 0)
        {
            StatusText.Text = Localizer.T("NoSelectedMessages");
            return;
        }

        var text = string.Join("\n\n", selected.Select(m =>
            string.IsNullOrWhiteSpace(m.Title)
                ? $"[{m.Topic}] {m.Message}"
                : $"[{m.Topic}] {m.Title}\n{m.Message}"));

        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
        StatusText.Text = string.Format(Localizer.T("CopiedCount"), selected.Count);        await Task.CompletedTask;
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = MessagesList.SelectedItems.Cast<NtfyMessage>().ToList();
        if (selected.Count == 0)
        {
            StatusText.Text = Localizer.T("NoSelectedMessages");
            return;
        }

        foreach (var m in selected)
            Messages.Remove(m);

        await _messageLogService.SaveAsync(Messages);
        StatusText.Text = string.Format(Localizer.T("DeletedCount"), selected.Count);    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        Messages.Clear();
        await _messageLogService.SaveAsync(Messages);
        StatusText.Text = Localizer.T("HistoryCleared");    }

    private void HoverFrame_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (TryGetHoverFrame(sender, out var hoverFrame))
            hoverFrame.Opacity = 1;
    }

    private void HoverFrame_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!TryGetHoverFrame(sender, out var hoverFrame))
            return;

        var item = FindParent<ListViewItem>((DependencyObject)sender);
        hoverFrame.Opacity = MultiSelectSwitch.IsOn && item?.IsSelected == true ? 1 : 0;
    }

    private void MessagesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!MultiSelectSwitch.IsOn)
            return;

        foreach (var added in e.AddedItems) SetItemHoverFrame(added, true);
        foreach (var removed in e.RemovedItems) SetItemHoverFrame(removed, false);
    }

    private void SingleSelectFadeTimer_Tick(object? sender, object e)
    {
        try
        {
            _singleSelectFadeTimer.Stop();
            if (MultiSelectSwitch.IsOn) return;
            ClearMessageSelectionSafe();
            ResetAllVisibleFrames();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private bool TryGetHoverFrame(object sender, out Border hoverFrame)
    {
        hoverFrame = null!;
        if (sender is not Grid grid || grid.Children.Count == 0 || grid.Children[0] is not Border frame)
            return false;

        hoverFrame = frame;
        return true;
    }

    private void SetItemHoverFrame(object item, bool visible)
    {
        if (MessagesList.ContainerFromItem(item) is not ListViewItem lvi) return;
        if (lvi.ContentTemplateRoot is not Grid grid || grid.Children.Count == 0 || grid.Children[0] is not Border hoverFrame) return;
        hoverFrame.Opacity = visible ? 1 : 0;
    }

    private void ResetAllVisibleFrames()
    {
        foreach (var item in MessagesList.Items)
            SetItemHoverFrame(item, false);
    }

    private void ClearMessageSelectionSafe()
    {
        try { MessagesList.SelectedItem = null; } catch { }
        try { MessagesList.SelectedItems.Clear(); } catch { }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T typed) return typed;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static PublishOptions BuildOptionsFromTemplate(MessageTemplate? t, string message)
    {
        if (t is null || t.Name == "Plain") return new PublishOptions();

        return t.Name switch
        {
            "Warning (priority 4)" => new PublishOptions { Title = "Warning", Priority = 4, TagsCsv = "warning" },
            "Alert (priority 5)" => new PublishOptions { Title = "Alert", Priority = 5, TagsCsv = "rotating_light" },
            "Success" => new PublishOptions { Title = "Success", Priority = 3, TagsCsv = "white_check_mark" },
            "Markdown" => new PublishOptions { Markdown = true, TagsCsv = "memo" },
            "Link / click" => new PublishOptions { Title = "Open link", ClickUrl = message, TagsCsv = "link" },
            "Delayed (10m)" => new PublishOptions { Title = "Reminder", Delay = "10m", TagsCsv = "alarm_clock" },
            _ => new PublishOptions()
        };
    }
}

file static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, Action action)
    {
        var tcs = new TaskCompletionSource<bool>();
        dispatcherQueue.TryEnqueue(() =>
        {
            try { action(); tcs.SetResult(true); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }
}
