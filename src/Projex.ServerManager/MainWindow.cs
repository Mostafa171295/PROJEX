using System.Diagnostics;
using System.Net;
using System.ServiceProcess;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Projex.ServerManager;

public sealed class MainWindow : Window
{
    private const string ApiServiceName = "PROJEX API";
    private static readonly Uri ApiAddress = new("http://127.0.0.1:5080/");
    private readonly TextBlock _apiServiceValue = ValueText("Checking…");
    private readonly TextBlock _databaseServiceValue = ValueText("Checking…");
    private readonly TextBlock _apiHealthValue = ValueText("Checking…");
    private readonly TextBlock _databaseHealthValue = ValueText("Checking…");
    private readonly TextBlock _lastCheckedValue = new() { Foreground = Brush("#64748B"), FontSize = 11 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#64748B") };
    private readonly Button _startButton = ActionButton("Start server");
    private readonly Button _stopButton = ActionButton("Stop server");
    private readonly Button _restartButton = ActionButton("Restart server");
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    public MainWindow()
    {
        Title = "PROJEX — Server Control Center";
        Width = 980;
        Height = 700;
        MinWidth = 820;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#F4F6FA");

        _startButton.Click += async (_, _) => await ChangeApiServiceAsync(ServiceAction.Start);
        _stopButton.Click += async (_, _) => await ChangeApiServiceAsync(ServiceAction.Stop);
        _restartButton.Click += async (_, _) => await ChangeApiServiceAsync(ServiceAction.Restart);
        _refreshTimer.Tick += async (_, _) => await RefreshStatusAsync();
        Closed += (_, _) => { _refreshTimer.Stop(); _http.Dispose(); };

        Content = BuildLayout();
        Opened += async (_, _) =>
        {
            _refreshTimer.Start();
            await RefreshStatusAsync();
        };
    }

    private Control BuildLayout()
    {
        var page = new StackPanel { Margin = new Thickness(30), Spacing = 18 };
        page.Children.Add(new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = "PROJEX SERVER CONTROL CENTER", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("#2563A6") },
                new TextBlock { Text = "Server administration", FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Brush("#172B4D") },
                new TextBlock { Text = "Manage the dedicated PROJEX server, check service health and run maintenance tools.", FontSize = 14, Foreground = Brush("#64748B"), TextWrapping = TextWrapping.Wrap }
            }
        });

        var serviceGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 14,
            RowSpacing = 14
        };
        serviceGrid.Children.Add(StatusCard("PROJEX API service", _apiServiceValue, "Windows background service", 0, 0));
        serviceGrid.Children.Add(StatusCard("PostgreSQL service", _databaseServiceValue, "Local database engine", 1, 0));
        serviceGrid.Children.Add(StatusCard("API liveness", _apiHealthValue, "HTTP health/live", 0, 1));
        serviceGrid.Children.Add(StatusCard("Database readiness", _databaseHealthValue, "HTTP health/ready", 1, 1));
        page.Children.Add(serviceGrid);

        var serviceActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        serviceActions.Children.Add(_startButton);
        serviceActions.Children.Add(_stopButton);
        serviceActions.Children.Add(_restartButton);
        serviceActions.Children.Add(ActionButton("Refresh", async () => await RefreshStatusAsync()));
        page.Children.Add(Section("Service controls", serviceActions));

        var tools = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = 10
        };
        tools.Children.Add(ActionButton("Run server setup", LaunchSetup, 0, 0));
        tools.Children.Add(ActionButton("Open deployment guide", OpenDeploymentGuide, 1, 0));
        tools.Children.Add(ActionButton("Open backup folder", OpenBackupFolder, 0, 1));
        tools.Children.Add(ActionButton("Open Windows Services", OpenServices, 1, 1));
        tools.Children.Add(ActionButton("Enable secure LAN access", LaunchRemoteAccessSetup, 0, 2));
        tools.Children.Add(ActionButton("Disable secure LAN access", DisableRemoteAccess, 1, 2));
        page.Children.Add(Section("Setup and maintenance", tools));

        page.Children.Add(new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E1E7EF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Server status", FontSize = 16, FontWeight = FontWeight.Bold, Foreground = Brush("#172B4D") },
                    _status,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock { Text = "Last checked:", Foreground = Brush("#94A3B8"), FontSize = 11 },
                            _lastCheckedValue
                        }
                    }
                }
            }
        });

        page.Children.Add(new TextBlock
        {
            Text = "Security: PostgreSQL is intended to remain local to this server. Remote desktop clients must connect through a secured HTTPS endpoint; never expose database port 5432.",
            Foreground = Brush("#9A5B17"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        return new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static Border StatusCard(string title, TextBlock value, string detail, int column, int row)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E1E7EF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeight.Bold, Foreground = Brush("#64748B") },
                    value,
                    new TextBlock { Text = detail, FontSize = 11, Foreground = Brush("#94A3B8") }
                }
            }
        };
        Grid.SetColumn(card, column);
        Grid.SetRow(card, row);
        return card;
    }

    private static Control Section(string title, Control content) => new StackPanel
    {
        Spacing = 9,
        Children =
        {
            new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = Brush("#172B4D") },
            content
        }
    };

    private static TextBlock ValueText(string value) => new()
    {
        Text = value,
        FontSize = 22,
        FontWeight = FontWeight.Bold,
        Foreground = Brush("#172B4D")
    };

    private static Button ActionButton(string label) => new()
    {
        Content = label,
        MinHeight = 40,
        MinWidth = 130,
        Padding = new Thickness(14, 8)
    };

    private static Button ActionButton(string label, Func<Task> action)
    {
        var button = ActionButton(label);
        button.Click += async (_, _) => await action();
        return button;
    }

    private static Button ActionButton(string label, Action action, int column, int row)
    {
        var button = ActionButton(label);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Click += (_, _) => action();
        Grid.SetColumn(button, column);
        Grid.SetRow(button, row);
        return button;
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var serviceStates = await Task.Run(ReadServiceStates);
            _apiServiceValue.Text = serviceStates.Api;
            _databaseServiceValue.Text = serviceStates.Database;

            var live = await CheckEndpointAsync("health/live");
            var ready = live == "Online" ? await CheckEndpointAsync("health/ready") : "Not checked";
            _apiHealthValue.Text = live;
            _databaseHealthValue.Text = ready;
            _status.Text = live == "Online" && ready == "Ready"
                ? "All local server checks passed. PROJEX is ready to accept authenticated client connections."
                : "One or more checks failed. Review the service states and confirm PostgreSQL and the API are running.";
            _lastCheckedValue.Text = DateTimeOffset.Now.ToString("g");
            _startButton.IsEnabled = serviceStates.Api != "Running";
            _stopButton.IsEnabled = serviceStates.Api == "Running";
            _restartButton.IsEnabled = serviceStates.Api == "Running";
        }
        catch (Exception ex)
        {
            _status.Text = $"Status check failed: {ex.Message}";
        }
    }

    private static (string Api, string Database) ReadServiceStates()
    {
        var api = ReadService(ApiServiceName);
        var databaseService = ServiceController.GetServices()
            .FirstOrDefault(service => service.ServiceName.StartsWith("postgresql-x64-", StringComparison.OrdinalIgnoreCase));
        var database = databaseService is null ? "Not installed" : databaseService.Status.ToString();
        databaseService?.Dispose();
        return (api, database);
    }

    private static string ReadService(string name)
    {
        try
        {
            using var service = new ServiceController(name);
            return service.Status.ToString();
        }
        catch (InvalidOperationException) { return "Not installed"; }
        catch (System.ComponentModel.Win32Exception ex) { return $"Access denied ({ex.NativeErrorCode})"; }
    }

    private async Task<string> CheckEndpointAsync(string relativePath)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri(ApiAddress, relativePath));
            if (relativePath == "health/live")
                return response.StatusCode == HttpStatusCode.OK ? "Online" : $"HTTP {(int)response.StatusCode}";
            return response.StatusCode == HttpStatusCode.OK ? "Ready" : $"Unavailable (HTTP {(int)response.StatusCode})";
        }
        catch (TaskCanceledException) { return "Timed out"; }
        catch (HttpRequestException) { return "Offline"; }
    }

    private async Task ChangeApiServiceAsync(ServiceAction action)
    {
        SetActionsEnabled(false);
        _status.Text = $"{action} requested for the PROJEX API service…";
        try
        {
            await Task.Run(() =>
            {
                using var service = new ServiceController(ApiServiceName);
                service.Refresh();
                if ((action is ServiceAction.Stop or ServiceAction.Restart) && service.Status != ServiceControllerStatus.Stopped)
                {
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                }
                if ((action is ServiceAction.Start or ServiceAction.Restart) && service.Status != ServiceControllerStatus.Running)
                {
                    service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                }
            });
            _status.Text = $"PROJEX API service {action.ToString().ToLowerInvariant()} operation completed.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not {action.ToString().ToLowerInvariant()} the API service: {ex.Message}";
        }
        finally
        {
            SetActionsEnabled(true);
            await RefreshStatusAsync();
        }
    }

    private void SetActionsEnabled(bool enabled)
    {
        _startButton.IsEnabled = enabled;
        _stopButton.IsEnabled = enabled;
        _restartButton.IsEnabled = enabled;
    }

    private void LaunchSetup() => LaunchElevatedScript("Install-PROJEX-Local.ps1");
    private void LaunchRemoteAccessSetup() => LaunchElevatedScript("Enable-PROJEX-RemoteAccess.ps1");
    private void DisableRemoteAccess() => LaunchElevatedScript("Disable-PROJEX-RemoteAccess.ps1");

    private void LaunchElevatedScript(string fileName)
    {
        var script = GetServerPath("Scripts", fileName);
        if (!File.Exists(script))
        {
            _status.Text = $"Setup script not found: {script}. Extract the complete PROJEX server package first.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\""
            });
        }
        catch (Exception ex) { _status.Text = $"Could not launch {fileName}: {ex.Message}"; }
    }

    private void OpenDeploymentGuide() => OpenPath(GetServerPath("Docs", "WINDOWS_HOME_SERVER.md"));
    private void OpenBackupFolder() => OpenPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PROJEX", "Backups"));
    private void OpenServices() => OpenPath("services.msc");

    private static string GetServerPath(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        return Path.Combine(new[] { root }.Concat(parts).ToArray());
    }

    private void OpenPath(string path)
    {
        try
        {
            if (Directory.Exists(path) || File.Exists(path) || path.EndsWith(".msc", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                _status.Text = $"Path not found: {path}";
        }
        catch (Exception ex) { _status.Text = $"Could not open item: {ex.Message}"; }
    }

    private enum ServiceAction { Start, Stop, Restart }
    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
}
