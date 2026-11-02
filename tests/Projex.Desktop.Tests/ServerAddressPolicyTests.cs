using Projex.Desktop;

namespace Projex.Desktop.Tests;

public sealed class ServerAddressPolicyTests
{
    [Theory]
    [InlineData("https://projex.example.com", "https://projex.example.com/")]
    [InlineData("http://localhost:5080", "http://localhost:5080/")]
    [InlineData("http://127.0.0.1:5080/", "http://127.0.0.1:5080/")]
    [InlineData("http://[::1]:5080", "http://[::1]:5080/")]
    public void AcceptsHttpsAndLoopback(string input, string expected)
    {
        var accepted = ServerAddressPolicy.TryNormalize(input, out var address, out var error);
        Assert.True(accepted, error);
        Assert.Empty(error);
        Assert.Equal(expected, address.ToString());
    }

    [Theory]
    [InlineData("http://projex.example.com")]
    [InlineData("ftp://projex.example.com")]
    [InlineData("https://name@projex.example.com")]
    [InlineData("https://projex.example.com?tenant=value")]
    [InlineData("https://projex.example.com/#section")]
    [InlineData("")]
    [InlineData("not a url")]
    public void RejectsInsecureOrInvalidAddresses(string input)
    {
        var accepted = ServerAddressPolicy.TryNormalize(input, out _, out var error);
        Assert.False(accepted);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void NormalizesWhitespaceAndTrailingSlashes()
    {
        var accepted = ServerAddressPolicy.TryNormalize("  https://projex.example.com/portal///  ", out var address, out var error);
        Assert.True(accepted, error);
        Assert.Empty(error);
        Assert.Equal("https://projex.example.com/portal/", address.ToString());
    }
}
