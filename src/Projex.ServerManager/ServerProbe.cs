using System.Net;
using System.ServiceProcess;

namespace Projex.ServerManager;

internal enum ServiceAction { Start, Stop, Restart }

internal sealed record ServerSnapshot(
    string ApiService,
    string DatabaseService,
    string OtherPostgres,
    string Live,
    string Ready,
    DateTimeOffset At)
{
    public bool ApiInstalled => ApiService != "Not installed" && !ApiService.StartsWith("Access denied");
    public bool ApiRunning => ApiService == "Running";
    public bool DatabaseInstalled => DatabaseService != "Not installed";
    public bool DatabaseRunning => DatabaseService == "Running";
    public bool AllGood => ApiRunning && DatabaseRunning && Live == "Online" && Ready == "Ready";

    public static ServerSnapshot Empty { get; } =
        new("Checking…", "Checking…", "", "Checking…", "Checking…", DateTimeOffset.Now);
}

internal sealed class ServerProbe : IDisposable
{
    public const string ApiServiceName = "PROJEX API";
    public const string DatabaseServiceName = "postgresql-x64-16";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<ServerSnapshot> ReadAsync(Uri apiAddress)
    {
        var (api, db, other) = await Task.Run(ReadServiceStates);
        var live = await CheckEndpointAsync(apiAddress, "health/live");
        var ready = live == "Online" ? await CheckEndpointAsync(apiAddress, "health/ready") : "Not checked";
        return new ServerSnapshot(api, db, other, live, ready, DateTimeOffset.Now);
    }

    private static (string Api, string Database, string Other) ReadServiceStates()
    {
        var api = ReadService(ApiServiceName);
        var database = ReadService(DatabaseServiceName);
        var other = "";
        try
        {
            var others = ServiceController.GetServices()
                .Where(s => s.ServiceName.StartsWith("postgresql-x64-", StringComparison.OrdinalIgnoreCase)
                            && !s.ServiceName.Equals(DatabaseServiceName, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.ServiceName)
                .ToList();
            other = string.Join(", ", others);
        }
        catch { /* ignore */ }
        return (api, database, other);
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
        catch (PlatformNotSupportedException) { return "Not installed"; }
    }

    private async Task<string> CheckEndpointAsync(Uri apiAddress, string relativePath)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri(apiAddress, relativePath));
            if (relativePath == "health/live")
                return response.StatusCode == HttpStatusCode.OK ? "Online" : $"HTTP {(int)response.StatusCode}";
            return response.StatusCode == HttpStatusCode.OK ? "Ready" : $"Unavailable (HTTP {(int)response.StatusCode})";
        }
        catch (TaskCanceledException) { return "Timed out"; }
        catch (HttpRequestException) { return "Offline"; }
    }

    public static Task ChangeApiServiceAsync(ServiceAction action) => Task.Run(() =>
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

    public void Dispose() => _http.Dispose();
}
