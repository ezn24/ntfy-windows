using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ntfy.Windows.Models;
using Ntfy.Windows.Services;

namespace Ntfy.Windows.Views;

public sealed partial class SettingsPage : Page
{
    private readonly CredentialVaultService _vault = new();
    private readonly AppSettingsService _settingsService = new();
    private AppSettings _settings = new();

    public SettingsPage()
    {
        InitializeComponent();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _settings = await _settingsService.LoadAsync();
        RuntimePreferences.Set(_settings.ThemeMode, _settings.LanguageCode);
        Localizer.Reload();

        var p = _settings.Server;

        ServerNameBox.Text = p.Name;
        BaseUrlBox.Text = p.BaseUrl;
        AuthModeBox.SelectedIndex = (int)p.AuthMode;
        UsernameBox.Text = p.Username ?? string.Empty;

        TopicsCsvBox.Text = string.Join(", ", _settings.Topics);
        ThemeModeBox.SelectedItem = _settings.ThemeMode;
        LanguageBox.SelectedIndex = _settings.LanguageCode == "zh-Hant" ? 1 : 0;

        PollIntervalBox.Text = _settings.PollIntervalSeconds.ToString();
        DateFormatBox.Text = _settings.DateTimeFormat;
        StickyNotifySwitch.IsOn = _settings.StickyNotifications;
        TimedNotifyBox.Text = _settings.TimedNotificationSeconds.ToString();

        QuitOnCloseSwitch.IsOn = _settings.QuitOnClose;
        StartHiddenSwitch.IsOn = _settings.StartHidden;
        HotkeysSwitch.IsOn = _settings.EnableHotkeys;

        ApplyLocalizedUi();
        StatusText.Text = Localizer.T("LoadedSavedSettings");
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    private void ApplyLocalizedUi()
    {
        SettingsTitleText.Text = Localizer.T("Settings");
        InstanceTitle.Text = Localizer.T("Instance");
        AuthTitle.Text = Localizer.T("Auth");
        TopicsTitle.Text = Localizer.T("Topics");
        AppearanceTitle.Text = Localizer.T("Appearance");
        NotificationsTitle.Text = Localizer.T("Notifications");
        GeneralTitle.Text = Localizer.T("General");

        ServerNameBox.Header = Localizer.T("ServerName");
        BaseUrlBox.Header = Localizer.T("BaseUrl");
        AuthModeBox.Header = Localizer.T("AuthMode");
        UsernameBox.Header = Localizer.T("UsernameBasic");
        SecretBox.Header = Localizer.T("TokenOrPassword");
        TopicsCsvBox.Header = Localizer.T("DefaultTopicsCsv");
        ThemeModeBox.Header = Localizer.T("Theme");
        LanguageBox.Header = Localizer.T("Language");
        PollIntervalBox.Header = Localizer.T("PollInterval");
        DateFormatBox.Header = Localizer.T("DateFormat");
        StickyNotifySwitch.Header = Localizer.T("StickyNotifications");
        TimedNotifyBox.Header = Localizer.T("TimedNotifySeconds");
        QuitOnCloseSwitch.Header = Localizer.T("QuitOnClose");
        StartHiddenSwitch.Header = Localizer.T("StartHidden");
        HotkeysSwitch.Header = Localizer.T("EnableHotkeys");

        SaveButton.Content = Localizer.T("Save");
        ReloadButton.Content = Localizer.T("Reload");

        ThemeModeBox.Items.Clear();
        ThemeModeBox.Items.Add(Localizer.T("ThemeSystem"));
        ThemeModeBox.Items.Add(Localizer.T("ThemeLight"));
        ThemeModeBox.Items.Add(Localizer.T("ThemeDark"));
        ThemeModeBox.SelectedIndex = _settings.ThemeMode switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };

        LanguageBox.Items.Clear();
        LanguageBox.Items.Add(Localizer.T("LangEnglish"));
        LanguageBox.Items.Add(Localizer.T("LangTraditionalChinese"));
        LanguageBox.SelectedIndex = _settings.LanguageCode == "zh-Hant" ? 1 : 0;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var mode = (AuthMode)AuthModeBox.SelectedIndex;

        var existingRef = _settings.Server.SecretRef;
        if (string.IsNullOrWhiteSpace(existingRef))
            existingRef = $"{ServerNameBox.Text.Trim()}_{Guid.NewGuid():N}";

        if (!string.IsNullOrWhiteSpace(SecretBox.Password))
            _vault.SaveSecret(existingRef!, SecretBox.Password);

        var topics = TopicsCsvBox.Text
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _settings.Server = new ServerProfile
        {
            Name = ServerNameBox.Text.Trim(),
            BaseUrl = BaseUrlBox.Text.Trim(),
            AuthMode = mode,
            Username = string.IsNullOrWhiteSpace(UsernameBox.Text) ? null : UsernameBox.Text.Trim(),
            SecretRef = existingRef
        };

        _settings.Topics = topics;
        _settings.ActiveTopic = topics.Contains(_settings.ActiveTopic, StringComparer.OrdinalIgnoreCase)
            ? _settings.ActiveTopic
            : topics.FirstOrDefault() ?? string.Empty;

        _settings.ThemeMode = ThemeModeBox.SelectedIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "System"
        };
        _settings.LanguageCode = LanguageBox.SelectedIndex == 1 ? "zh-Hant" : "en-US";

        _settings.PollIntervalSeconds = int.TryParse(PollIntervalBox.Text, out var p) ? Math.Max(5, p) : 30;
        _settings.DateTimeFormat = string.IsNullOrWhiteSpace(DateFormatBox.Text) ? "yyyy-MM-dd HH:mm:ss" : DateFormatBox.Text.Trim();
        _settings.StickyNotifications = StickyNotifySwitch.IsOn;
        _settings.TimedNotificationSeconds = int.TryParse(TimedNotifyBox.Text, out var t) ? Math.Max(1, t) : 8;

        _settings.QuitOnClose = QuitOnCloseSwitch.IsOn;
        _settings.StartHidden = StartHiddenSwitch.IsOn;
        _settings.EnableHotkeys = HotkeysSwitch.IsOn;

        await _settingsService.SaveAsync(_settings);
        RuntimePreferences.Set(_settings.ThemeMode, _settings.LanguageCode);
        Localizer.Reload();
        ApplyLocalizedUi();

        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = _settings.LanguageCode;
        }
        catch { }

        StatusText.Text = string.Format(Localizer.T("SavedStatus"), _settings.Server.Name, _settings.Server.AuthMode, _settings.Topics.Count);
    }
}
