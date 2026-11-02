using System.Net;
using System.Net.Http;

namespace Projex.Desktop;

internal enum ServerHealthStatus
{
    ApiUnavailable,
    DatabaseUnavailable,
    Ready
}

internal static class ServerHealthProbe
{
    public static async Task<ServerHealthStatus> CheckAsync(
        HttpClient client,
        Uri serverAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var live = await client.GetAsync(
                new Uri(serverAddress, "health/live"),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (live.StatusCode != HttpStatusCode.OK)
                return ServerHealthStatus.ApiUnavailable;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServerHealthStatus.ApiUnavailable;
        }
        catch (HttpRequestException)
        {
            return ServerHealthStatus.ApiUnavailable;
        }

        try
        {
            using var ready = await client.GetAsync(
                new Uri(serverAddress, "health/ready"),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return ready.StatusCode == HttpStatusCode.OK
                ? ServerHealthStatus.Ready
                : ServerHealthStatus.DatabaseUnavailable;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServerHealthStatus.DatabaseUnavailable;
        }
        catch (HttpRequestException)
        {
            return ServerHealthStatus.DatabaseUnavailable;
        }
    }
}
