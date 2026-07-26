using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Ntfy.Windows.Models;
using Ntfy.Windows.Services;
using Windows.UI;

namespace Ntfy.Windows;

public sealed partial class MainWindow
{
    private readonly UpdateService _updateService = new();
    private bool _startupUpdateChecked;

    public async Task CheckForUpdatesOnStartupAsync()
    {
        if (_startupUpdateChecked)
            return;

        var settings = await _settingsService.LoadAsync();
        if (!settings.AutoCheckForUpdates)
        {
            _startupUpdateChecked = true;
            return;
        }

        await Task.Delay(1200);
        if (RootGrid.XamlRoot is null)
            return;

        _startupUpdateChecked = true;
        try
        {
            var update = await _updateService.CheckAsync();
            if (update is null)
                return;

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = Localizer.T("UpdateAvailableTitle"),
                Content = string.Format(
                    Localizer.T("UpdateAvailablePrompt"),
                    update.LatestVersion.ToString(3)),
                PrimaryButtonText = Localizer.T("DownloadAndInstall"),
                CloseButtonText = Localizer.T("Later"),
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await DownloadAndInstallUpdateAsync(update);
        }
        catch
        {
            // Automatic checks stay silent; manual checks surface detailed errors.
        }
    }

    public void ExitForUpdate()
    {
        _allowClose = true;
        CleanupTray();
        try { PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); } catch { }
    }

    private async Task DownloadAndInstallUpdateAsync(UpdateInfo update)
    {
        var statusText = new TextBlock
        {
            Text = Localizer.T("DownloadingUpdate"),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var progressRing = new ProgressRing
        {
            IsActive = true,
            Width = 36,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var panel = new StackPanel
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { progressRing, statusText }
        };
        var surface = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 42, 42, 42)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = panel
        };

        ShowWindowOverlay(surface);
        try
        {
            var progress = new Progress<double>(value =>
            {
                statusText.Text = string.Format(Localizer.T("DownloadingUpdateProgress"), Math.Round(value));
            });
            var installerPath = await _updateService.DownloadAsync(update, progress);
            statusText.Text = Localizer.T("LaunchingInstaller");
            UpdateService.LaunchInstaller(installerPath);
            ExitForUpdate();
        }
        catch (Exception ex)
        {
            HideWindowOverlay();
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = Localizer.T("UpdateFailedTitle"),
                Content = string.Format(Localizer.T("UpdateInstallFailed"), ex.Message),
                CloseButtonText = Localizer.T("Close")
            };
            await dialog.ShowAsync();
        }
    }
}
