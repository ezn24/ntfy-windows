using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Ntfy.Windows.Models;
using Ntfy.Windows.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Ntfy.Windows.Views;

public sealed partial class InboxPage : Page
{
    private const string AllTopicsKey = "__all__";
    private readonly NtfyApiService _api = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly CredentialVaultService _vault = new();
    private readonly MessageLogService _messageLogService = new();
    private readonly Dictionary<string, CancellationTokenSource> _topicCts = [];
    private readonly List<NtfyMessage> _allMessages = [];
    private AppSettings _settings = new();
    private bool _initializing;

    public ObservableCollection<NtfyMessage> Messages { get; } = [];
    public ObservableCollection<TopicListItem> TopicItems { get; } = [];

    public InboxPage()
    {
        InitializeComponent();
        RuntimePreferences.Changed += ApplyLocalizedUi;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        _initializing = true;
        _settings = await _settingsService.LoadAsync();
        _settings.MutedTopics ??= [];
        _settings.TopicDisplayNames ??= [];
        _settings.LastMessageIds ??= [];
        _allMessages.AddRange(await _messageLogService.LoadAsync());
        try { await MergeRemoteSubscriptionsAsync(); } catch { }
        ApplyLocalizedUi();
        InitializeFilters();
        RefreshTopicItems(string.IsNullOrWhiteSpace(_settings.ActiveTopic) ? AllTopicsKey : _settings.ActiveTopic);
        ApplyFilters();
        _initializing = false;
        await PullHistoryForTopicsAsync(_settings.Topics);
        await StartAllSubscriptionsAsync();
    }

    private void ApplyLocalizedUi()
    {
        Localizer.Reload();
        TopicsTitleText.Text = Localizer.T("TopicNav");
        AllTopicsButtonText.Text = Localizer.T("AllTopics");
        CurrentInstanceText.Text = GetCurrentInstanceLabel();
        ToolTipService.SetToolTip(CurrentInstanceText, _settings.Server.BaseUrl);
        NewTopicBox.PlaceholderText = Localizer.T("TopicName");
        InboxTitleText.Text = Localizer.T("AllTopics");
        MessageHintText.Text = Localizer.T("MessageHintDetails");
        SearchBox.PlaceholderText = Localizer.T("SearchMessages");
        ComposeBox.PlaceholderText = Localizer.T("WriteMessage");
        SendText.Text = Localizer.T("Send");
        ToolTipService.SetToolTip(AddTopicButton, Localizer.T("AddTopic"));
        ToolTipService.SetToolTip(SyncSubscriptionsButton, Localizer.T("SyncSubscriptions"));
        ToolTipService.SetToolTip(OpenPublishDialogButton, Localizer.T("PublishOptions"));
        ToolTipService.SetToolTip(ClearHistoryButton, Localizer.T("ClearHistory"));
        ToolTipService.SetToolTip(MarkAllReadButton, Localizer.T("MarkAllRead"));
        ToolTipService.SetToolTip(UnreadOnlyToggle, Localizer.T("UnreadOnly"));
        ToolTipService.SetToolTip(MultiSelectToggle, Localizer.T("MultiSelect"));
        ToolTipService.SetToolTip(CopySelectedButton, Localizer.T("CopySelected"));
        ToolTipService.SetToolTip(DeleteSelectedButton, Localizer.T("DeleteSelected"));
    }

    private void InitializeFilters()
    {
        PriorityFilter.Items.Clear();
        PriorityFilter.Items.Add(Localizer.T("AllPriorities"));
        PriorityFilter.Items.Add(Localizer.T("PriorityMin"));
        PriorityFilter.Items.Add(Localizer.T("PriorityLow"));
        PriorityFilter.Items.Add(Localizer.T("PriorityNormal"));
        PriorityFilter.Items.Add(Localizer.T("PriorityHigh"));
        PriorityFilter.Items.Add(Localizer.T("PriorityUrgent"));
        PriorityFilter.SelectedIndex = 0;
    }

    private void RefreshTopicItems(string? selectedKey = null)
    {
        selectedKey ??= (TopicList.SelectedItem as TopicListItem)?.Topic ?? AllTopicsKey;
        TopicItems.Clear();
        foreach (var topic in _settings.Topics.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TopicItems.Add(new TopicListItem
            {
                Topic = topic,
                DisplayName = _settings.TopicDisplayNames.TryGetValue(topic, out var name) && !string.IsNullOrWhiteSpace(name) ? name : topic,
                UnreadCount = _allMessages.Count(x => !x.IsRead && x.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase)),
                IsMuted = _settings.MutedTopics.Contains(topic, StringComparer.OrdinalIgnoreCase)
            });
        }
        var showAllTopics = selectedKey.Equals(AllTopicsKey, StringComparison.OrdinalIgnoreCase);
        TopicList.SelectedItem = showAllTopics ? null : TopicItems.FirstOrDefault(x => x.Topic.Equals(selectedKey, StringComparison.OrdinalIgnoreCase));
        AllTopicsButton.IsChecked = showAllTopics || TopicList.SelectedItem is null;
    }

    private void MessageCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
            card.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
    }

    private void MessageCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
            card.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
    }

    private void ApplyFilters()
    {
        if (MessagesList is null) return;
        var topic = (TopicList.SelectedItem as TopicListItem)?.Topic ?? AllTopicsKey;
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        var priority = PriorityFilter?.SelectedIndex ?? 0;
        var unreadOnly = UnreadOnlyToggle?.IsChecked == true;

        var filtered = _allMessages.Where(message =>
            (topic == AllTopicsKey || message.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase)) &&
            (!unreadOnly || !message.IsRead) &&
            (priority == 0 || message.Priority == priority) &&
            (string.IsNullOrWhiteSpace(query) ||
             message.Message.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             (message.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
             message.Topic.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             (message.TagsCsv?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)))
            .OrderByDescending(x => x.Timestamp)
            .ToList();

        Messages.Clear();
        foreach (var message in filtered)
        {
            message.ShowTopic = topic == AllTopicsKey;
            Messages.Add(message);
        }
        InboxTitleText.Text = topic == AllTopicsKey ? Localizer.T("AllTopics") : TopicItems.FirstOrDefault(x => x.Topic == topic)?.DisplayName ?? topic;
        var canPublish = topic != AllTopicsKey;
        ComposeBox.IsEnabled = canPublish;
        SendButton.IsEnabled = canPublish;
        OpenPublishDialogButton.IsEnabled = canPublish;
        MessageHintText.Text = string.Format(Localizer.T("MessageCountSummary"), filtered.Count, filtered.Count(x => !x.IsRead));
    }

    private async Task StartAllSubscriptionsAsync()
    {
        foreach (var cts in _topicCts.Values) cts.Cancel();
        _topicCts.Clear();
        foreach (var topic in _settings.Topics.Distinct(StringComparer.OrdinalIgnoreCase))
            _topicCts[topic] = StartTopicSubscription(topic);
        StatusText.Text = _settings.Topics.Count == 0 ? Localizer.T("NoTopicYet") : string.Format(Localizer.T("SubscribedCount"), _settings.Topics.Count);
        await _settingsService.SaveAsync(_settings);
    }

    private CancellationTokenSource StartTopicSubscription(string topic)
    {
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            var retrySeconds = 1;
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var server = ResolveProfileWithSecret(_settings.Server);
                    _settings.LastMessageIds.TryGetValue(topic, out var lastId);
                    await foreach (var message in _api.SubscribeJsonStreamAsync(server, topic, lastId, cts.Token))
                    {
                        await DispatcherQueue.EnqueueAsync(() => ApplyIncomingEvent(message));
                        retrySeconds = 1;
                    }
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    await DispatcherQueue.EnqueueAsync(() => StatusText.Text = string.Format(Localizer.T("ReconnectingTopic"), topic, retrySeconds, ex.Message));
                    try { await Task.Delay(TimeSpan.FromSeconds(retrySeconds), cts.Token); } catch (OperationCanceledException) { break; }
                    retrySeconds = Math.Min(retrySeconds * 2, 60);
                }
            }
        });
        return cts;
    }

    private void ApplyIncomingEvent(NtfyMessage incoming)
    {
        if (incoming.Event == "message_clear")
        {
            _allMessages.RemoveAll(x => x.Topic.Equals(incoming.Topic, StringComparison.OrdinalIgnoreCase));
        }
        else if (incoming.Event == "message_delete")
        {
            _allMessages.RemoveAll(x => x.Id == incoming.Id && x.Topic.Equals(incoming.Topic, StringComparison.OrdinalIgnoreCase));
        }
        else if (incoming.Event == "message")
        {
            if (IsDuplicateMessage(incoming)) return;
            incoming.IsRead = false;
            _allMessages.Insert(0, incoming);
            if (_allMessages.Count > 2000) _allMessages.RemoveRange(2000, _allMessages.Count - 2000);
            if (!string.IsNullOrWhiteSpace(incoming.Id)) _settings.LastMessageIds[incoming.Topic] = incoming.Id;
            if (!_settings.MutedTopics.Contains(incoming.Topic, StringComparer.OrdinalIgnoreCase)) DesktopNotificationService.ShowMessage(incoming);
        }
        else return;

        RefreshTopicItems();
        ApplyFilters();
        _ = PersistStateAsync();
    }

    private async Task PersistStateAsync()
    {
        await _messageLogService.SaveAsync(_allMessages.ToList());
        await _settingsService.SaveAsync(_settings);
    }

    private async Task<(int Found, int Added)> MergeRemoteSubscriptionsAsync()
    {
        var remoteSubscriptions = await _api.FetchAccountSubscriptionsAsync(ResolveProfileWithSecret(_settings.Server));
        var currentBaseUrl = _settings.Server.BaseUrl.TrimEnd('/');
        var matchingSubscriptions = remoteSubscriptions
            .Where(subscription => subscription.BaseUrl.TrimEnd('/').Equals(currentBaseUrl, StringComparison.OrdinalIgnoreCase))
            .Where(subscription => !string.IsNullOrWhiteSpace(subscription.Topic))
            .ToList();
        var added = 0;

        foreach (var subscription in matchingSubscriptions)
        {
            if (!_settings.Topics.Contains(subscription.Topic, StringComparer.OrdinalIgnoreCase))
            {
                _settings.Topics.Add(subscription.Topic);
                added++;
            }
            if (!string.IsNullOrWhiteSpace(subscription.DisplayName))
                _settings.TopicDisplayNames[subscription.Topic] = subscription.DisplayName;
        }

        await _settingsService.SaveAsync(_settings);
        return (matchingSubscriptions.Count, added);
    }
    private async Task<(int Added, IReadOnlyList<string> Errors)> PullHistoryForTopicsAsync(IEnumerable<string> topics)
    {
        var added = 0;
        var errors = new List<string>();
        var server = ResolveProfileWithSecret(_settings.Server);

        foreach (var topic in topics.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var history = await _api.FetchTopicHistoryAsync(server, topic);
                foreach (var message in history.Where(x => x.Event == "message"))
                {
                    if (IsDuplicateMessage(message)) continue;
                    message.IsRead = true;
                    _allMessages.Add(message);
                    added++;
                }

                var latest = history.Where(x => x.Event == "message" && !string.IsNullOrWhiteSpace(x.Id)).MaxBy(x => x.Timestamp);
                if (latest is not null) _settings.LastMessageIds[topic] = latest.Id;
            }
            catch (Exception ex)
            {
                errors.Add($"{topic}: {ex.Message}");
            }
        }

        _allMessages.Sort((left, right) => right.Timestamp.CompareTo(left.Timestamp));
        if (_allMessages.Count > 2000) _allMessages.RemoveRange(2000, _allMessages.Count - 2000);
        RefreshTopicItems();
        ApplyFilters();
        await PersistStateAsync();
        return (added, errors);
    }

    private async void SyncSubscriptions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        try
        {
            button.IsEnabled = false;
            StatusText.Text = Localizer.T("SyncingSubscriptions");
            var selectedTopic = (TopicList.SelectedItem as TopicListItem)?.Topic ?? AllTopicsKey;
            _settings = await _settingsService.LoadAsync();
            _settings.MutedTopics ??= [];
            _settings.TopicDisplayNames ??= [];
            _settings.LastMessageIds ??= [];
            var remote = await MergeRemoteSubscriptionsAsync();
            RefreshTopicItems(selectedTopic);

            var result = await PullHistoryForTopicsAsync(_settings.Topics);
            await StartAllSubscriptionsAsync();
            StatusText.Text = result.Errors.Count == 0
                ? string.Format(Localizer.T("SubscriptionsSynced"), remote.Found, remote.Added, result.Added)
                : string.Format(Localizer.T("HistorySyncFailed"), string.Join("; ", result.Errors));
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(Localizer.T("HistorySyncFailed"), ex.Message);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private bool TryResolveTopicItem(object sender, out TopicListItem item)
    {
        if (sender is FrameworkElement { DataContext: TopicListItem dataItem })
        {
            item = dataItem;
            return true;
        }
        if (sender is FrameworkElement { Tag: string topic })
        {
            item = TopicItems.FirstOrDefault(candidate => candidate.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase))!;
            return item is not null;
        }
        item = null!;
        return false;
    }

    private async void PullTopicHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveTopicItem(sender, out var item) || item.IsAllTopics) return;
        var control = sender as Control;
        try
        {
            if (control is not null) control.IsEnabled = false;
            StatusText.Text = string.Format(Localizer.T("PullingTopicHistory"), item.Topic);
            var result = await PullHistoryForTopicsAsync([item.Topic]);
            StatusText.Text = result.Errors.Count == 0
                ? string.Format(Localizer.T("TopicHistoryPulled"), item.Topic, result.Added)
                : string.Format(Localizer.T("HistorySyncFailed"), string.Join("; ", result.Errors));
        }
        finally
        {
            if (control is not null) control.IsEnabled = true;
        }
    }

    private string GetCurrentInstanceLabel()
    {
        if (Uri.TryCreate(_settings.Server.BaseUrl, UriKind.Absolute, out var uri))
            return uri.IsDefaultPort ? uri.Host : uri.Authority;
        return string.IsNullOrWhiteSpace(_settings.Server.Name) ? _settings.Server.BaseUrl : _settings.Server.Name;
    }
    private ServerProfile ResolveProfileWithSecret(ServerProfile source)
    {
        var resolved = new ServerProfile { Name = source.Name, BaseUrl = source.BaseUrl, AuthMode = source.AuthMode, Username = source.Username, SecretRef = source.SecretRef };
        if (!string.IsNullOrWhiteSpace(source.SecretRef))
        {
            var secret = _vault.ReadSecret(source.SecretRef);
            if (!string.IsNullOrWhiteSpace(secret)) resolved.SecretRef = secret;
        }
        return resolved;
    }

    private async void AddTopic_Click(object sender, RoutedEventArgs e)
    {
        var topic = NewTopicBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(topic) || _settings.Topics.Contains(topic, StringComparer.OrdinalIgnoreCase)) return;
        _settings.Topics.Add(topic);
        NewTopicBox.Text = string.Empty;
        _topicCts[topic] = StartTopicSubscription(topic);
        RefreshTopicItems(topic);
        ApplyFilters();
        await _settingsService.SaveAsync(_settings);
    }

    private async Task RemoveTopicAsync(string topic)
    {
        _settings.Topics.RemoveAll(x => x.Equals(topic, StringComparison.OrdinalIgnoreCase));
        _settings.MutedTopics.RemoveAll(x => x.Equals(topic, StringComparison.OrdinalIgnoreCase));
        _settings.TopicDisplayNames.Remove(topic);
        _settings.LastMessageIds.Remove(topic);
        if (_topicCts.Remove(topic, out var cts)) cts.Cancel();
        if (_settings.ActiveTopic.Equals(topic, StringComparison.OrdinalIgnoreCase)) _settings.ActiveTopic = string.Empty;
        RefreshTopicItems(AllTopicsKey);
        ApplyFilters();
        await _settingsService.SaveAsync(_settings);
    }

    private async void DeleteTopicInline_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveTopicItem(sender, out var item) || item.IsAllTopics) return;
        await RemoveTopicAsync(item.Topic);
    }

    private async void SendTopicTest_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveTopicItem(sender, out var item) || item.IsAllTopics) return;
        var control = sender as Control;

        try
        {
            if (control is not null) control.IsEnabled = false;
            StatusText.Text = Localizer.T("SendingTestMessage");
            await _api.PublishAsync(ResolveProfileWithSecret(_settings.Server), item.Topic, Localizer.T("TestMessageBody"), new PublishOptions
            {
                Title = Localizer.T("TestMessageTitle"),
                TagsCsv = "white_check_mark",
                Priority = 3
            });
            StatusText.Text = string.Format(Localizer.T("TestMessageSent"), item.Topic);
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(Localizer.T("TestMessageFailed"), ex.Message);
        }
        finally
        {
            if (control is not null) control.IsEnabled = true;
        }
    }

    private async void ToggleTopicMute_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveTopicItem(sender, out var item) || item.IsAllTopics) return;
        if (_settings.MutedTopics.Contains(item.Topic, StringComparer.OrdinalIgnoreCase))
            _settings.MutedTopics.RemoveAll(x => x.Equals(item.Topic, StringComparison.OrdinalIgnoreCase));
        else
            _settings.MutedTopics.Add(item.Topic);
        RefreshTopicItems(item.Topic);
        await _settingsService.SaveAsync(_settings);
    }

    private async void AllTopics_Click(object sender, RoutedEventArgs e)
    {
        TopicList.SelectedItem = null;
        AllTopicsButton.IsChecked = true;
        _settings.ActiveTopic = string.Empty;
        ApplyFilters();
        DispatcherQueue.TryEnqueue(RefreshTopicSelectionFrames);
        await _settingsService.SaveAsync(_settings);
    }

    private async void TopicList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || TopicList.SelectedItem is not TopicListItem selected) return;
        AllTopicsButton.IsChecked = false;
        _settings.ActiveTopic = selected.Topic;
        ApplyFilters();
        DispatcherQueue.TryEnqueue(RefreshTopicSelectionFrames);
        await _settingsService.SaveAsync(_settings);
    }
    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var message = ComposeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(message)) return;
            await PublishCurrentTopicAsync(message, new PublishOptions());
            ComposeBox.Text = string.Empty;
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void OpenPublishDialog_Click(object sender, RoutedEventArgs e)
    {
        var activeTopic = _settings.ActiveTopic;
        if (string.IsNullOrWhiteSpace(activeTopic))
        {
            StatusText.Text = Localizer.T("TopicRequired");
            return;
        }

        var topicOption = CreateHiddenOptionField(Localizer.T("PublishTopic"), activeTopic);
        var titleBox = new TextBox { Header = Localizer.T("PublishTitle"), MinHeight = 40 };
        var messageBox = new TextBox
        {
            Header = Localizer.T("PublishMessage"),
            Text = ComposeBox.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 84
        };
        var markdownBox = new CheckBox { Content = Localizer.T("PublishMarkdown") };
        var tagsBox = new TextBox { Header = Localizer.T("PublishTags"), MinHeight = 40 };
        var priorityBox = new ComboBox { Header = Localizer.T("PublishPriority"), MinHeight = 40, SelectedIndex = 0 };
        var iconBox = new TextBox { Header = Localizer.T("PublishIconUrl"), MinHeight = 40 };
        var filenameBox = new TextBox { Header = Localizer.T("PublishFilename"), MinHeight = 40 };
        StorageFile? selectedFile = null;
        var selectedFileText = new TextBlock
        {
            Text = Localizer.T("NoFileSelected"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var chooseFileButton = new Button
        {
            Content = Localizer.T("ChooseFile"),
            Height = 38,
            CornerRadius = new CornerRadius(8)
        };
        chooseFileButton.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            if (App.MainWindow is not null)
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            selectedFile = await picker.PickSingleFileAsync();
            if (selectedFile is not null)
            {
                selectedFileText.Text = selectedFile.Name;
                filenameBox.Text = selectedFile.Name;
            }
        };
        var filePickerRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            Children = { chooseFileButton, selectedFileText }
        };
        Grid.SetColumn(selectedFileText, 1);
        var actionsBox = new TextBox
        {
            Header = Localizer.T("PublishActions"),
            PlaceholderText = Localizer.T("PublishActionsPlaceholder"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
            MaxHeight = 96
        };
        var advancedFields = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Width = 540,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                iconBox,
                filePickerRow,
                filenameBox,
                actionsBox
            }
        };
        foreach (var item in new[]
                 {
                     Localizer.T("PriorityDefault"),
                     Localizer.T("PriorityMin"),
                     Localizer.T("PriorityLow"),
                     Localizer.T("PriorityNormal"),
                     Localizer.T("PriorityHigh"),
                     Localizer.T("PriorityUrgent")
                 })
            priorityBox.Items.Add(item);

        var clickOption = CreateHiddenOptionField(Localizer.T("OptionClickUrl"));
        var emailOption = CreateHiddenOptionField(Localizer.T("OptionEmail"));
        var attachOption = CreateHiddenOptionField(Localizer.T("OptionAttachUrl"));
        var delayOption = CreateHiddenOptionField(Localizer.T("OptionDelayHint"));
        var callOption = CreateHiddenOptionField(Localizer.T("OptionCall"));

        var optionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        optionsPanel.Children.Add(MakeOptionButton(Localizer.T("OptionClickUrl"), clickOption.Container));
        optionsPanel.Children.Add(MakeOptionButton(Localizer.T("OptionEmail"), emailOption.Container));
        optionsPanel.Children.Add(MakeOptionButton(Localizer.T("OptionAttachUrl"), attachOption.Container));
        optionsPanel.Children.Add(MakeOptionButton(Localizer.T("OptionDelay"), delayOption.Container));
        optionsPanel.Children.Add(MakeOptionButton(Localizer.T("OptionChangeTopic"), topicOption.Container));
        optionsPanel.Children.Add(MakeOptionButton(Localizer.T("OptionCall"), callOption.Container));
        var advancedButton = MakeOptionButton(Localizer.T("AdvancedPublishOptions"), advancedFields);

        var content = new ScrollViewer
        {
            MinWidth = 584,
            MaxWidth = 584,
            MaxHeight = 380,
            Padding = new Thickness(0, 0, 16, 0),
            Margin = new Thickness(28, 0, 28, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Width = 540,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Left,
                Children =
                {
                    topicOption.Container,
                    titleBox,
                    messageBox,
                    markdownBox,
                    new Grid
                    {
                        ColumnSpacing = 12,
                        ColumnDefinitions =
                        {
                            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                            new ColumnDefinition { Width = new GridLength(160) }
                        },
                        Children =
                        {
                            tagsBox,
                            priorityBox
                        }
                    },
                    new TextBlock { Text = Localizer.T("OtherFeatures"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    optionsPanel,
                    advancedButton
                }
            }
        };
        Grid.SetColumn(priorityBox, 1);

        var optionFieldsPanel = new StackPanel
        {
            Width = 540,
            Spacing = 8,
            Margin = new Thickness(28, 8, 28, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                    clickOption.Container,
                    emailOption.Container,
                    attachOption.Container,
                    delayOption.Container,
                    callOption.Container,
                    advancedFields
            }
        };

        var sendButton = new Button
        {
            Content = Localizer.T("Send"),
            Height = 40,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var cancelButton = new Button
        {
            Content = Localizer.T("Cancel"),
            Height = 40,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var footer = new Grid
        {
            Padding = new Thickness(28, 14, 28, 20),
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            Children =
            {
                sendButton,
                cancelButton
            }
        };
        Grid.SetColumn(cancelButton, 1);

        var dialogFrame = new Grid
        {
            Width = 640,
            MaxHeight = 620,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            }
        };
        dialogFrame.Children.Add(new TextBlock
        {
            Text = string.Format(Localizer.T("SendTemplateTitle"), BuildPublishTargetText(activeTopic)),
            Margin = new Thickness(28, 24, 28, 12),
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"]
        });
        Grid.SetRow(content, 1);
        dialogFrame.Children.Add(content);
        Grid.SetRow(optionFieldsPanel, 2);
        dialogFrame.Children.Add(optionFieldsPanel);
        Grid.SetRow(footer, 3);
        dialogFrame.Children.Add(footer);

        var card = new Border
        {
            Width = 640,
            MaxHeight = 620,
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = dialogFrame
        };
        App.MainWindow?.ShowWindowOverlay(card);

        void CloseOverlay()
        {
            App.MainWindow?.HideWindowOverlay();
        }

        cancelButton.Click += (_, _) => CloseOverlay();

        sendButton.Click += async (_, _) =>
        {
            sendButton.IsEnabled = false;
            try
            {
                var topic = topicOption.Input.Text.Trim();
                var message = messageBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(topic) || (string.IsNullOrWhiteSpace(message) && selectedFile is null))
                {
                    StatusText.Text = Localizer.T("TopicRequired");
                    return;
                }

                var options = new PublishOptions
                {
                    Title = string.IsNullOrWhiteSpace(titleBox.Text) ? null : titleBox.Text.Trim(),
                    TagsCsv = string.IsNullOrWhiteSpace(tagsBox.Text) ? null : tagsBox.Text.Trim(),
                    Priority = priorityBox.SelectedIndex <= 0 ? null : priorityBox.SelectedIndex,
                    Markdown = markdownBox.IsChecked,
                    ClickUrl = ReadVisibleOption(clickOption),
                    Email = ReadVisibleOption(emailOption),
                    AttachUrl = ReadVisibleOption(attachOption),
                    Filename = string.IsNullOrWhiteSpace(filenameBox.Text) ? null : filenameBox.Text.Trim(),
                    IconUrl = string.IsNullOrWhiteSpace(iconBox.Text) ? null : iconBox.Text.Trim(),
                    Actions = string.IsNullOrWhiteSpace(actionsBox.Text) ? null : actionsBox.Text.Trim(),
                    Delay = ReadVisibleOption(delayOption),
                    Call = ReadVisibleOption(callOption)
                };

                if (selectedFile is null)
                {
                    await PublishToTopicAsync(topic, message, options);
                }
                else
                {
                    await using var fileStream = await selectedFile.OpenStreamForReadAsync();
                    options.AttachUrl = null;
                    await _api.PublishFileAsync(ResolveProfileWithSecret(_settings.Server), topic, fileStream, filenameBox.Text.Trim(), options);
                    await EnsureSubscribedTopicAsync(topic);
                    StatusText.Text = Localizer.T("Sent");
                }
                ComposeBox.Text = string.Empty;
                CloseOverlay();
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
            finally
            {
                sendButton.IsEnabled = true;
            }
        };
    }

    private async Task PublishCurrentTopicAsync(string message, PublishOptions options)
    {
        var topic = _settings.ActiveTopic;
        if (string.IsNullOrWhiteSpace(topic))
        {
            StatusText.Text = Localizer.T("TopicRequired");
            return;
        }

        await PublishToTopicAsync(topic, message, options);
    }

    private async Task EnsureSubscribedTopicAsync(string topic)
    {
        if (!_settings.Topics.Any(x => x.Equals(topic, StringComparison.OrdinalIgnoreCase)))
        {
            _settings.Topics.Add(topic);
            _topicCts[topic] = StartTopicSubscription(topic);
        }
        _settings.ActiveTopic = topic;
        RefreshTopicItems(topic);
        ApplyFilters();
        await _settingsService.SaveAsync(_settings);
    }
    private async Task PublishToTopicAsync(string topic, string message, PublishOptions options)
    {
        var server = ResolveProfileWithSecret(_settings.Server);
        await _api.PublishAsync(server, topic, message, options);
        StatusText.Text = Localizer.T("Sent");

        if (!_settings.Topics.Any(x => x.Equals(topic, StringComparison.OrdinalIgnoreCase)))
        {
            _settings.Topics.Add(topic);

            _topicCts[topic] = StartTopicSubscription(topic);
        }

        _settings.ActiveTopic = topic;
        TopicList.SelectedItem = topic;
        await _settingsService.SaveAsync(_settings);
    }

    private string BuildPublishTargetText(string topic)
    {
        if (Uri.TryCreate(_settings.Server.BaseUrl, UriKind.Absolute, out var uri))
            return $"{uri.Host}/{topic}";

        return $"{_settings.Server.BaseUrl.TrimEnd('/')}/{topic}";
    }

    private sealed record OptionField(Grid Container, TextBox Input);

    private static OptionField CreateHiddenOptionField(string header, string text = "")
    {
        var input = new TextBox
        {
            Header = header,
            Text = text,
            MinHeight = 40
        };

        var removeButton = new Button
        {
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(20),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Content = new FontIcon { Glyph = "\uE711", FontSize = 14 }
        };

        var container = new Grid
        {
            Visibility = Visibility.Collapsed,
            Width = 430,
            MaxWidth = 430,
            HorizontalAlignment = HorizontalAlignment.Left,
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(40) }
            }
        };

        Grid.SetColumn(input, 0);
        Grid.SetColumn(removeButton, 1);
        container.Children.Add(input);
        container.Children.Add(removeButton);

        removeButton.Click += (_, _) =>
        {
            input.Text = string.Empty;
            container.Visibility = Visibility.Collapsed;
        };

        return new OptionField(container, input);
    }

    private static Button MakeOptionButton(string text, FrameworkElement target)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 32,
            CornerRadius = new CornerRadius(16),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(7, 0, 7, 0),
            FontSize = 12
        };

        button.Click += (_, _) =>
        {
            target.Visibility = target.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (target.Visibility == Visibility.Visible)
            {
                target.UpdateLayout();
                target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            }
        };

        return button;
    }

    private static string? ReadVisibleOption(OptionField field)
    {
        return field.Container.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(field.Input.Text)
            ? field.Input.Text.Trim()
            : null;
    }

    private void TopicRow_ContextRequested(object sender, ContextRequestedEventArgs args)
    {
        args.Handled = true;
        if (sender is not Grid { DataContext: TopicListItem item } row || item.IsAllTopics) return;

        var menu = new MenuFlyout();
        menu.Items.Add(CreateTopicMenuItem(item.PullHistoryTooltip, "\uE72C", item.Topic, PullTopicHistory_Click));
        menu.Items.Add(CreateTopicMenuItem(item.SendTestTooltip, "\uE724", item.Topic, SendTopicTest_Click));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreateTopicMenuItem(item.MuteTooltip, item.MuteGlyph, item.Topic, ToggleTopicMute_Click));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreateTopicMenuItem(item.DeleteTooltip, "\uE74D", item.Topic, DeleteTopicInline_Click));

        if (args.TryGetPosition(row, out var position))
            menu.ShowAt(row, new FlyoutShowOptions { Position = position });
        else
            menu.ShowAt(row);
    }

    private static MenuFlyoutItem CreateTopicMenuItem(string text, string glyph, string topic, RoutedEventHandler clickHandler)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph },
            Tag = topic
        };
        item.Click += clickHandler;
        return item;
    }
    private void TopicRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid { Children.Count: > 0 } row && row.Children[0] is Border frame) frame.Opacity = 1;
    }

    private void TopicRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Grid { Children.Count: > 0 } row || row.Children[0] is not Border frame) return;
        var item = FindParent<ListViewItem>(row);
        frame.Opacity = item?.IsSelected == true ? 1 : 0;
    }

    private void RefreshTopicSelectionFrames()
    {
        foreach (var item in TopicItems)
        {
            if (TopicList.ContainerFromItem(item) is not ListViewItem container || container.ContentTemplateRoot is not Grid { Children.Count: > 0 } row || row.Children[0] is not Border frame) continue;
            frame.Opacity = container.IsSelected ? 1 : 0;
        }
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
    private void Filter_Changed(object sender, TextChangedEventArgs e) => ApplyFilters();
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilters();
    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilters();

    private void MultiSelectToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = MultiSelectToggle.IsChecked == true;
        try
        {
            if (!enabled && MessagesList.SelectionMode != ListViewSelectionMode.None)
            {
                MessagesList.SelectedItem = null;
                MessagesList.SelectedItems.Clear();
            }

            MessagesList.SelectionMode = enabled ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
            MessagesList.IsItemClickEnabled = !enabled;
            SelectionActionsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MultiSelectToggle.IsChecked = false;
            MessagesList.SelectionMode = ListViewSelectionMode.None;
            MessagesList.IsItemClickEnabled = true;
            SelectionActionsPanel.Visibility = Visibility.Collapsed;
            StatusText.Text = ex.Message;
        }
    }

    private async void MessagesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (MultiSelectToggle.IsChecked == true || e.ClickedItem is not NtfyMessage message) return;
        message.IsRead = true;
        RefreshTopicItems();
        ApplyFilters();
        await PersistStateAsync();
        ShowMessageDetails(message);
    }

    private void ShowMessageDetails(NtfyMessage message)
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = message.Topic, FontSize = 12, Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"] });
        if (!string.IsNullOrWhiteSpace(message.Title)) body.Children.Add(new TextBlock { Text = message.Title, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(message.Markdown ? BuildMarkdownView(message.Message) : new TextBlock { Text = message.Message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        if (!string.IsNullOrWhiteSpace(message.MetadataSummary)) body.Children.Add(new TextBlock { Text = message.MetadataSummary, FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], TextWrapping = TextWrapping.Wrap });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (!string.IsNullOrWhiteSpace(message.ClickUrl)) actions.Children.Add(CreateUriButton(Localizer.T("OpenLink"), message.ClickUrl));
        if (!string.IsNullOrWhiteSpace(message.AttachmentUrl)) actions.Children.Add(CreateUriButton(Localizer.T("OpenAttachment"), message.AttachmentUrl));
        foreach (var action in _api.ParseActions(message.ActionsJson))
        {
            var button = new Button { Content = string.IsNullOrWhiteSpace(action.Label) ? action.Action : action.Label, MinHeight = 36, CornerRadius = new CornerRadius(8) };
            button.Click += async (_, _) => await ExecuteMessageActionAsync(action);
            actions.Children.Add(button);
        }
        if (actions.Children.Count > 0) body.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = actions });

        var close = new Button { Content = Localizer.T("Close"), Height = 40, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8) };
        close.Click += (_, _) => App.MainWindow?.HideWindowOverlay();
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(24), Children = { body, close } };
        var card = new Border
        {
            Width = 620, MaxHeight = 600, CornerRadius = new CornerRadius(8), Padding = new Thickness(4),
            Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"], BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = new ScrollViewer { MaxHeight = 590, Content = panel }
        };
        App.MainWindow?.ShowWindowOverlay(card);
    }

    private static FrameworkElement BuildMarkdownView(string markdown)
    {
        var panel = new StackPanel { Spacing = 5 };
        foreach (var rawLine in markdown.Replace("\r", string.Empty).Split('\n'))
        {
            var line = rawLine;
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            if (line.StartsWith("### ")) { block.Text = line[4..]; block.FontSize = 16; block.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; }
            else if (line.StartsWith("## ")) { block.Text = line[3..]; block.FontSize = 18; block.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; }
            else if (line.StartsWith("# ")) { block.Text = line[2..]; block.FontSize = 22; block.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; }
            else if (line.StartsWith("- ") || line.StartsWith("* ")) block.Text = $"\u2022  {line[2..]}";
            else if (line.StartsWith("> ")) { block.Text = line[2..]; block.FontStyle = global::Windows.UI.Text.FontStyle.Italic; }
            else block.Text = line;
            panel.Children.Add(block);
        }
        return panel;
    }

    private Button CreateUriButton(string label, string uri)
    {
        var button = new Button { Content = label, MinHeight = 36, CornerRadius = new CornerRadius(8) };
        button.Click += async (_, _) =>
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out var target)) await Launcher.LaunchUriAsync(target);
            else StatusText.Text = Localizer.T("InvalidUrl");
        };
        return button;
    }

    private async Task ExecuteMessageActionAsync(NtfyAction action)
    {
        try
        {
            if (action.Action.Equals("view", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(action.Url, UriKind.Absolute, out var target)) await Launcher.LaunchUriAsync(target);
            }
            else if (action.Action.Equals("copy", StringComparison.OrdinalIgnoreCase))
            {
                var package = new DataPackage();
                package.SetText(action.Intent ?? action.Url ?? action.Body ?? string.Empty);
                Clipboard.SetContent(package);
                StatusText.Text = Localizer.T("CopiedToClipboard");
            }
            else
            {
                await _api.ExecuteActionAsync(ResolveProfileWithSecret(_settings.Server), action);
                StatusText.Text = Localizer.T("ActionCompleted");
            }
            if (action.Clear) App.MainWindow?.HideWindowOverlay();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void MarkAllRead_Click(object sender, RoutedEventArgs e)
    {
        var topic = (TopicList.SelectedItem as TopicListItem)?.Topic ?? AllTopicsKey;
        foreach (var message in _allMessages.Where(x => topic == AllTopicsKey || x.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase))) message.IsRead = true;
        RefreshTopicItems(topic);
        ApplyFilters();
        await PersistStateAsync();
    }

    private async void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = MessagesList.SelectedItems.Cast<NtfyMessage>().ToList();
        if (selected.Count == 0) { StatusText.Text = Localizer.T("NoSelectedMessages"); return; }
        var package = new DataPackage();
        package.SetText(string.Join("\n\n", selected.Select(x => string.IsNullOrWhiteSpace(x.Title) ? $"[{x.Topic}] {x.Message}" : $"[{x.Topic}] {x.Title}\n{x.Message}")));
        Clipboard.SetContent(package);
        StatusText.Text = string.Format(Localizer.T("CopiedCount"), selected.Count);
        await Task.CompletedTask;
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = MessagesList.SelectedItems.Cast<NtfyMessage>().ToList();
        if (selected.Count == 0) { StatusText.Text = Localizer.T("NoSelectedMessages"); return; }
        foreach (var message in selected) _allMessages.Remove(message);
        ApplyFilters();
        RefreshTopicItems();
        await PersistStateAsync();
        StatusText.Text = string.Format(Localizer.T("DeletedCount"), selected.Count);
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var topic = (TopicList.SelectedItem as TopicListItem)?.Topic ?? AllTopicsKey;
        if (topic == AllTopicsKey) _allMessages.Clear();
        else _allMessages.RemoveAll(x => x.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase));
        ApplyFilters();
        RefreshTopicItems(topic);
        await PersistStateAsync();
        StatusText.Text = Localizer.T("HistoryCleared");
    }

    private void MessagesList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private bool IsDuplicateMessage(NtfyMessage incoming) => _allMessages.Any(existing =>
        (!string.IsNullOrWhiteSpace(incoming.Id) && existing.Id == incoming.Id) ||
        (existing.Topic.Equals(incoming.Topic, StringComparison.OrdinalIgnoreCase) && existing.Message == incoming.Message && existing.Title == incoming.Title && Math.Abs((existing.Timestamp - incoming.Timestamp).TotalSeconds) < 2));
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
