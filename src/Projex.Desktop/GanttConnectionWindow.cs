using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Projex.Desktop;

public sealed class GanttConnectionWindow : Window
{
    private readonly TextBox _api = new() { Text = "https://localhost:5001", Watermark = "https://api.example.com" };
    private readonly TextBox _project = new() { Watermark = "Project UUID" };
    private readonly TextBox _token = new() { Watermark = "JWT access token" };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly Button _connect = new() { Content = "Connect and load schedule", HorizontalAlignment = HorizontalAlignment.Stretch };

    public GanttConnectionWindow()
    {
        Title = "Connect to PROJEX API"; Width = 560; Height = 440; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _token.PasswordChar = '●';
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Connect to a project", FontSize = 23, FontWeight = FontWeight.Bold, Foreground = Brush("#172B4D") });
        panel.Children.Add(new TextBlock { Text = "Enter your API endpoint, project ID, and a short-lived access token. Tokens are used in memory only and are not saved.", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#64748B") });
        panel.Children.Add(Labelled("API base URL", _api)); panel.Children.Add(Labelled("Project ID", _project)); panel.Children.Add(Labelled("Bearer access token", _token));
        panel.Children.Add(_error); panel.Children.Add(_connect);
        Content = panel;
        _connect.Click += async (_, _) => await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        _connect.IsEnabled = false; _error.Text = "Connecting…";
        try
        {
            if (!Uri.TryCreate(_api.Text?.Trim(), UriKind.Absolute, out var uri)) throw new ArgumentException("Enter a valid absolute API URL.");
            if (!Guid.TryParse(_project.Text?.Trim(), out var projectId)) throw new ArgumentException("Enter a valid project UUID.");
            var client = new GanttApiClient(uri, _token.Text ?? string.Empty);
            try
            {
                var schedule = await client.GetScheduleAsync(projectId);
                new LiveGanttWindow(client, schedule).Show();
                Close();
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        catch (Exception ex) { _error.Text = ex.Message; }
        finally { _connect.IsEnabled = true; }
    }

    private static Control Labelled(string label, Control input)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Foreground = Brush("#334155") });
        input.MinHeight = 36; stack.Children.Add(input); return stack;
    }
    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
