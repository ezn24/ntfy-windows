using Microsoft.UI.Xaml;
using Ntfy.Windows.Models;
using Ntfy.Windows.Services;

namespace Ntfy.Windows.Views;

public sealed partial class SettingsPage
{
    private readonly StartupService _startupService = new();
    private readonly UpdateService _updateService = new();
    private UpdateInfo? _availableUpdate;
    private bool _loadingSystemSettings;
    private bool _updatesInitialized;

    private async void SettingsPageUpdates_Loaded(object sender, RoutedEventArgs e)
    {
        if (_updatesInitialized)
            return;

        _updatesInitialized = true;
        RuntimePreferences.Changed += ApplyUpdateLocalizedUi;
        ReloadButton.Click += ReloadUpdateSettings_Click;
        Unloaded += SettingsPageUpdates_Unloaded;

        await ReloadSystemSettingsAsync();
        ApplyUpdateLocalizedUi();
    }

    private void SettingsPageUpdates_Unloaded(object sender, RoutedEventArgs e)
    {
        RuntimePreferences.Changed -= ApplyUpdateLocalizedUi;
        ReloadButton.Click -= ReloadUpdateSettings_Click;
        Unloaded -= SettingsPageUpdates_Unloaded;
        _updatesInitialized = false;
    }

    private async void ReloadUpdateSettings_Click(object sender, RoutedEventArgs e)
    {
        await ReloadSystemSettingsAsync();
    }

    private async Task ReloadSystemSettingsAsync()
    {
        _loadingSystemSettings = true;
        try
        {
            var settings = await _settingsService.LoadAsync();
            var startsWithWindows = _startupService.IsEnabled;
            StartWithWindowsSwitch.IsOn = startsWithWindows;
            AutoCheckUpdatesSwitch.IsOn = settings.AutoCheckForUpdates;

            _settings.StartWithWindows = startsWithWindows;
            _settings.AutoCheckForUpdates = settings.AutoCheckForUpdates;
        }
        finally
        {
            _loadingSystemSettings = false;
        }
    }

    private void ApplyUpdateLocalizedUi()
    {
        UpdatesTitle.Text = Localizer.T("Updates");
        CurrentVersionText.Text = string.Format(
            Localizer.T("CurrentVersion"),
            UpdateService.CurrentVersion.ToString(3));
        StartWithWindowsSwitch.Header = Localizer.T("StartWithWindows");
        AutoCheckUpdatesSwitch.Header = Localizer.T("AutoCheckForUpdates");
        CheckUpdateButton.Content = Localizer.T("CheckForUpdates");
        InstallUpdateButton.Content = Localizer.T("DownloadAndInstall");
    }

    private async void StartWithWindows_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSystemSettings)
            return;

        try
        {
            _startupService.SetEnabled(StartWithWindowsSwitch.IsOn);
            var settings = await _settingsService.LoadAsync();
            _settings.StartWithWindows = StartWithWindowsSwitch.IsOn;
            settings.StartWithWindows = StartWithWindowsSwitch.IsOn;
            await _settingsService.SaveAsync(settings);
            StatusText.Text = StartWithWindowsSwitch.IsOn
                ? Localizer.T("StartWithWindowsEnabled")
                : Localizer.T("StartWithWindowsDisabled");
        }
        catch (Exception ex)
        {
            _loadingSystemSettings = true;
            StartWithWindowsSwitch.IsOn = _startupService.IsEnabled;
            _loadingSystemSettings = false;
            StatusText.Text = string.Format(Localizer.T("StartWithWindowsFailed"), ex.Message);
        }
    }

    private async void AutoCheckUpdates_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSystemSettings)
            return;

        try
        {
            var settings = await _settingsService.LoadAsync();
            _settings.AutoCheckForUpdates = AutoCheckUpdatesSwitch.IsOn;
            settings.AutoCheckForUpdates = AutoCheckUpdatesSwitch.IsOn;
            await _settingsService.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            _loadingSystemSettings = true;
            AutoCheckUpdatesSwitch.IsOn = _settings.AutoCheckForUpdates;
            _loadingSystemSettings = false;
            StatusText.Text = string.Format(Localizer.T("UpdateSettingsFailed"), ex.Message);
        }
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        SetUpdateControlsBusy(true);
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = Localizer.T("CheckingForUpdates");

        try
        {
            _availableUpdate = await _updateService.CheckAsync();
            if (_availableUpdate is null)
            {
                UpdateStatusText.Text = Localizer.T("NoUpdatesAvailable");
                return;
            }

            UpdateStatusText.Text = string.Format(
                Localizer.T("UpdateAvailable"),
                _availableUpdate.LatestVersion.ToString(3));
            InstallUpdateButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = string.Format(Localizer.T("UpdateCheckFailed"), ex.Message);
        }
        finally
        {
            SetUpdateControlsBusy(false);
        }
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null)
            return;

        SetUpdateControlsBusy(true);
        UpdateProgressBar.Value = 0;
        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateStatusText.Text = Localizer.T("DownloadingUpdate");

        try
        {
            var progress = new Progress<double>(value =>
            {
                UpdateProgressBar.Value = value;
                UpdateStatusText.Text = string.Format(Localizer.T("DownloadingUpdateProgress"), Math.Round(value));
            });

            var installerPath = await _updateService.DownloadAsync(_availableUpdate, progress);
            UpdateStatusText.Text = Localizer.T("LaunchingInstaller");
            UpdateService.LaunchInstaller(installerPath);
            App.MainWindow?.ExitForUpdate();
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = string.Format(Localizer.T("UpdateInstallFailed"), ex.Message);
            SetUpdateControlsBusy(false);
        }
    }

    private void SetUpdateControlsBusy(bool busy)
    {
        CheckUpdateButton.IsEnabled = !busy;
        InstallUpdateButton.IsEnabled = !busy;
        AutoCheckUpdatesSwitch.IsEnabled = !busy;
    }
}
