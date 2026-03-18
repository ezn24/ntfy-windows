using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ntfy.Windows.Models;
using Ntfy.Windows.Services;

namespace Ntfy.Windows.Views;

public sealed partial class SendPage : Page
{
    private readonly NtfyApiService _api = new();
    private readonly ServerProfile _server = new() { Name = "Default", BaseUrl = "https://ntfy.sh" };

    public SendPage() => InitializeComponent();

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _api.PublishAsync(
                _server,
                TopicBox.Text.Trim(),
                BodyBox.Text,
                new PublishOptions
                {
                    Title = TitleBox.Text,
                    Priority = int.Parse((string)PriorityBox.SelectedItem),
                    TagsCsv = TagsBox.Text
                });
            StatusText.Text = "Sent";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}
