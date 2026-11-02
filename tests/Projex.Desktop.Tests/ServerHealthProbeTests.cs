using System.Net;
using Projex.Desktop;

namespace Projex.Desktop.Tests;

public sealed class ServerHealthProbeTests
{
    [Fact]
    public async Task CheckAsync_ReturnsReadyWhenApiAndDatabaseAreHealthy()
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var status = await ServerHealthProbe.CheckAsync(client, new Uri("https://projex.example.com/"));

        Assert.Equal(ServerHealthStatus.Ready, status);
    }

    [Fact]
    public async Task CheckAsync_ReturnsApiUnavailableWhenLivenessFails()
    {
        var calls = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }));

        var status = await ServerHealthProbe.CheckAsync(client, new Uri("https://projex.example.com/"));

        Assert.Equal(ServerHealthStatus.ApiUnavailable, status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CheckAsync_ReturnsDatabaseUnavailableWhenReadinessFails()
    {
        using var client = new HttpClient(new StubHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/health/live", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var status = await ServerHealthProbe.CheckAsync(client, new Uri("https://projex.example.com/"));

        Assert.Equal(ServerHealthStatus.DatabaseUnavailable, status);
    }

    [Fact]
    public async Task CheckAsync_ReturnsApiUnavailableWhenServerCannotBeReached()
    {
        using var client = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline")));

        var status = await ServerHealthProbe.CheckAsync(client, new Uri("https://projex.example.com/"));

        Assert.Equal(ServerHealthStatus.ApiUnavailable, status);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
