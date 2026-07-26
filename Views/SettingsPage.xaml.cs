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
    private readonly NtfyApiService _api = new();
    private AppSettings _settings = new();

    public SettingsPage()
    {
        InitializeComponent();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _settings = await _settingsService.LoadAsync();
        RuntimePreferences.Set(_settings.ThemeMode, _settings.LanguageCode, _settings.DateTimeFormat, _settings.CloseToTray);
        DesktopNotificationService.Configure(_settings.StickyNotifications, _settings.TimedNotificationSeconds);
        Localizer.Reload();

        var p = _settings.Server;

        ServerNameBox.Text = p.Name;
        BaseUrlBox.Text = p.BaseUrl;
        AuthModeBox.SelectedIndex = (int)p.AuthMode;
        UsernameBox.Text = p.Username ?? string.Empty;

        TopicsCsvBox.Text = string.Join(", ", _settings.Topics);
        ThemeModeBox.SelectedItem = _settings.ThemeMode;
        LanguageBox.SelectedIndex = IsTraditionalChinese(_settings.LanguageCode) ? 1 : 0;

        DateFormatBox.Text = _settings.DateTimeFormat;
        StickyNotifySwitch.IsOn = _settings.StickyNotifications;
        TimedNotifyBox.Text = _settings.TimedNotificationSeconds.ToString();

        CloseToTraySwitch.IsOn = _settings.CloseToTray;
        StartMinimizedToTraySwitch.IsOn = _settings.StartMinimizedToTray;
        InstanceTestStatusText.Text = string.Empty;
        AuthTestStatusText.Text = string.Empty;

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
        TestInstanceButton.Content = Localizer.T("TestConnection");
        AuthModeBox.Header = Localizer.T("AuthMode");
        UsernameBox.Header = Localizer.T("UsernameBasic");
        SecretBox.Header = Localizer.T("TokenOrPassword");
        TestConnectionButton.Content = Localizer.T("TestConnection");
        TopicsCsvBox.Header = Localizer.T("DefaultTopicsCsv");
        ThemeModeBox.Header = Localizer.T("Theme");
        LanguageBox.Header = Localizer.T("Language");
        DateFormatBox.Header = Localizer.T("DateFormat");
        StickyNotifySwitch.Header = Localizer.T("StickyNotifications");
        TimedNotifyBox.Header = Localizer.T("TimedNotifySeconds");
        CloseToTraySwitch.Header = Localizer.T("CloseToTray");
        StartMinimizedToTraySwitch.Header = Localizer.T("StartMinimizedToTray");

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
        LanguageBox.SelectedIndex = IsTraditionalChinese(_settings.LanguageCode) ? 1 : 0;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var topics = TopicsCsvBox.Text
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _settings.Server = BuildServerProfileFromInputs(persistSecret: true);

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
        _settings.LanguageCode = LanguageBox.SelectedIndex == 1 ? "zh_Hant" : "en";

        _settings.DateTimeFormat = string.IsNullOrWhiteSpace(DateFormatBox.Text) ? "yyyy-MM-dd HH:mm:ss" : DateFormatBox.Text.Trim();
        _settings.StickyNotifications = StickyNotifySwitch.IsOn;
        _settings.TimedNotificationSeconds = int.TryParse(TimedNotifyBox.Text, out var t) ? Math.Max(1, t) : 8;

        _settings.CloseToTray = CloseToTraySwitch.IsOn;
        _settings.StartMinimizedToTray = StartMinimizedToTraySwitch.IsOn;

        await _settingsService.SaveAsync(_settings);
        RuntimePreferences.Set(_settings.ThemeMode, _settings.LanguageCode, _settings.DateTimeFormat, _settings.CloseToTray);
        DesktopNotificationService.Configure(_settings.StickyNotifications, _settings.TimedNotificationSeconds);
        Localizer.Reload();
        ApplyLocalizedUi();

        try
        {
            global::Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = _settings.LanguageCode;
        }
        catch { }

        StatusText.Text = string.Format(Localizer.T("SavedStatus"), _settings.Server.Name, _settings.Server.AuthMode, _settings.Topics.Count);
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var statusText = ReferenceEquals(sender, TestInstanceButton)
            ? InstanceTestStatusText
            : AuthTestStatusText;

        try
        {
            TestInstanceButton.IsEnabled = false;
            TestConnectionButton.IsEnabled = false;
            statusText.Text = Localizer.T("TestingConnection");

            var server = BuildServerProfileFromInputs(persistSecret: false);
            var topic = TopicsCsvBox.Text
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            await _api.TestConnectionAsync(server, topic);
            statusText.Text = Localizer.T("ConnectionSucceeded");
        }
        catch (Exception ex)
        {
            statusText.Text = string.Format(Localizer.T("ConnectionFailed"), ex.Message);
        }
        finally
        {
            TestInstanceButton.IsEnabled = true;
            TestConnectionButton.IsEnabled = true;
        }
    }

    private ServerProfile BuildServerProfileFromInputs(bool persistSecret)
    {
        var mode = (AuthMode)AuthModeBox.SelectedIndex;
        var secret = SecretBox.Password;
        var secretRef = mode == AuthMode.None ? null : _settings.Server.SecretRef;

        if (mode != AuthMode.None)
        {
            if (!string.IsNullOrWhiteSpace(secret))
            {
                if (persistSecret)
                {
                    if (string.IsNullOrWhiteSpace(secretRef))
                        secretRef = $"{ServerNameBox.Text.Trim()}_{Guid.NewGuid():N}";

                    _vault.SaveSecret(secretRef, secret);
                }
                else
                {
                    secretRef = secret;
                }
            }
            else if (!persistSecret && !string.IsNullOrWhiteSpace(secretRef))
            {
                secretRef = _vault.ReadSecret(secretRef);
            }
        }

        return new ServerProfile
        {
            Name = ServerNameBox.Text.Trim(),
            BaseUrl = BaseUrlBox.Text.Trim(),
            AuthMode = mode,
            Username = mode == AuthMode.Basic && !string.IsNullOrWhiteSpace(UsernameBox.Text) ? UsernameBox.Text.Trim() : null,
            SecretRef = secretRef
        };
    }

    private static bool IsTraditionalChinese(string languageCode)
    {
        return languageCode.Equals("zh_Hant", StringComparison.OrdinalIgnoreCase) ||
               languageCode.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase);
    }
}
