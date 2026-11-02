using System.Net;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Projex.Desktop;

internal static class ServerAddressPolicy
{
    public static bool TryNormalize(string? value, out Uri address, out string error)
    {
        address = null!;
        error = "Enter a valid server URL.";
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed))
            return false;

        var isSecure = parsed.Scheme == Uri.UriSchemeHttps;
        var isLocalHttp = parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback;
        if ((!isSecure && !isLocalHttp) || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
        {
            error = "Use HTTPS for remote servers. HTTP is allowed only for localhost/loopback. Credentials, query strings and fragments are not allowed in the URL.";
            return false;
        }

        address = new Uri(parsed.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
        error = string.Empty;
        return true;
    }
}

internal sealed class ServerConnectionSettingsWindow : Window
{
    private readonly TextBox _addressBox;
    private readonly TextBlock _status;
    private readonly Button _testButton;
    private readonly Button _saveButton;

    public ServerConnectionSettingsWindow(string currentAddress)
    {
        Title = "PROJEX — Connection settings";
        Width = 620;
        Height = 440;
        MinWidth = 520;
        MinHeight = 390;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#F4F6FA"));

        _addressBox = new TextBox { Text = currentAddress, Watermark = "https://projex.company.com", MinHeight = 42 };
        _status = new TextBlock
        {
            Text = "Test the API and database before saving this server address.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#64748B"))
        };
        _testButton = new Button { Content = "Test connection", MinWidth = 130, MinHeight = 38 };
        _saveButton = new Button
        {
            Content = "Save server",
            MinWidth = 120,
            MinHeight = 38,
            Background = new SolidColorBrush(Color.Parse("#2563A6")),
            Foreground = Brushes.White
        };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 90, MinHeight = 38 };

        _testButton.Click += async (_, _) => await TestConnectionAsync();
        _saveButton.Click += (_, _) =>
        {
            if (!ServerAddressPolicy.TryNormalize(_addressBox.Text, out var address, out var error))
            {
                _status.Text = error;
                return;
            }
            Close(address);
        };
        cancelButton.Click += (_, _) => Close(null);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        actions.Children.Add(cancelButton);
        actions.Children.Add(_testButton);
        actions.Children.Add(_saveButton);

        var panel = new StackPanel { Margin = new Thickness(28), Spacing = 14 };
        panel.Children.Add(new TextBlock
        {
            Text = "Connect this device to your PROJEX server",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#172B4D")),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Enter the HTTPS address published by your company or the VPN address of the central server. Do not enter a PostgreSQL address or port.",
            Foreground = new SolidColorBrush(Color.Parse("#64748B")),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "SERVER ADDRESS",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#475569"))
        });
        panel.Children.Add(_addressBox);
        panel.Children.Add(_status);
        panel.Children.Add(actions);
        Content = panel;
    }

    private async Task TestConnectionAsync()
    {
        if (!ServerAddressPolicy.TryNormalize(_addressBox.Text, out var address, out var error))
        {
            _status.Text = error;
            return;
        }

        _testButton.IsEnabled = false;
        _saveButton.IsEnabled = false;
        _status.Text = "Checking API liveness and database readiness...";
        var checkedAt = DateTimeOffset.Now.ToString("g");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var live = await client.GetAsync(new Uri(address, "health/live"));
            var apiAlive = live.StatusCode == HttpStatusCode.OK;
            if (!apiAlive)
            {
                _status.Text = $"API: unavailable (HTTP {(int)live.StatusCode}). Checked {checkedAt}.";
                return;
            }

            using var ready = await client.GetAsync(new Uri(address, "health/ready"));
            var databaseReady = ready.StatusCode == HttpStatusCode.OK;
            _status.Text = databaseReady
                ? $"API: online  ·  Database: ready\nChecked: {checkedAt}"
                : $"API: online  ·  Database: not ready (HTTP {(int)ready.StatusCode})\nThe API is reachable, but PostgreSQL is unavailable or not ready. Checked: {checkedAt}";
        }
        catch (TaskCanceledException)
        {
            _status.Text = $"Connection timed out. Check the server address, VPN and firewall. Checked: {checkedAt}";
        }
        catch (HttpRequestException)
        {
            _status.Text = $"Could not reach the API. Check the server address, VPN, TLS certificate and firewall. Checked: {checkedAt}";
        }
        finally
        {
            _testButton.IsEnabled = true;
            _saveButton.IsEnabled = true;
        }
    }
}
