using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Ntfy.WinUI.Client.Models;
using Ntfy.WinUI.Client.Services;

namespace Ntfy.WinUI.Client.Views;

public sealed partial class TemplatesPage : Page
{
    private readonly TemplateService _templateService = new();

    public TemplatesPage()
    {
        InitializeComponent();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var templates = await _templateService.LoadAsync();
        if (templates.Count == 0)
        {
            templates =
            [
                new MessageTemplate { Name = "Assignment Reminder", Topic = "study", Title = "Assignment due", Body = "{{course}} due on {{due_date}}", Priority = 4 },
                new MessageTemplate { Name = "Daily Check-in", Topic = "daily", Title = "Daily status", Body = "What are today's top 3 tasks?", Priority = 2 }
            ];
            await _templateService.SaveAsync(templates);
        }

        TemplateList.ItemsSource = templates;
    }
}
