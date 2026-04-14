namespace OpenEtradeMcp.Tests;

public class MainTest
{
    [Fact]
    public void BaseUrl_UsesProductionByDefault()
    {
        var config = new ETradeConfig();

        Assert.Equal(ETradeConfig.ProductionBaseUrl, config.BaseUrl);
    }

    [Fact]
    public void BaseUrl_UsesSandboxWhenEnabled()
    {
        var config = new ETradeConfig { UseSandbox = true };

        Assert.Equal(ETradeConfig.SandboxBaseUrl, config.BaseUrl);
    }

    [Fact]
    public void DefaultOrderConfirmationSettings_AreAsExpected()
    {
        var config = new ETradeConfig();

        Assert.False(config.EnableOrderConfirmation);
        Assert.Equal("placeOrder,cancelOrder,placeChangeOrder", config.GuardedTools);
        Assert.Equal(300, config.ConfirmationTimeoutSeconds);
    }
}

public class EtradeOAuthSessionTests
{
    [Fact]
    public void IsAuthenticated_FalseWhenTokensMissing()
    {
        var session = new EtradeOAuthSession();

        Assert.False(session.IsAuthenticated);
    }

    [Fact]
    public void IsAuthenticated_TrueWhenBothTokensPresent()
    {
        var session = new EtradeOAuthSession
        {
            AccessToken = "token",
            AccessTokenSecret = "secret"
        };

        Assert.True(session.IsAuthenticated);
    }
}

public class EtradeOAuth1AuthenticationHandlerTests
{
    private static ETradeConfig CreateConfig() => new()
    {
        ConsumerKey = "test-consumer-key",
        ConsumerSecret = "test-consumer-secret"
    };

    [Fact]
    public void Constructor_ThrowsWhenHttpClientIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EtradeOAuth1AuthenticationHandler(null!, CreateConfig()));
    }

    [Fact]
    public void Constructor_ThrowsWhenConfigIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EtradeOAuth1AuthenticationHandler(new HttpClient(), null!));
    }

    [Fact]
    public void GetAuthorizationUrl_FormatsExpectedUrl()
    {
        using var httpClient = new HttpClient();
        var handler = new EtradeOAuth1AuthenticationHandler(httpClient, CreateConfig());

        var url = handler.GetAuthorizationUrl("request-token");

        Assert.Equal("https://us.etrade.com/e/t/etws/authorize?key=test-consumer-key&token=request-token", url);
    }

    [Fact]
    public async Task CompleteAuthenticationAsync_ThrowsWhenRequestTokenMissing()
    {
        using var httpClient = new HttpClient();
        var handler = new EtradeOAuth1AuthenticationHandler(httpClient, CreateConfig());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.CompleteAuthenticationAsync("verifier", string.Empty, "secret"));
    }

    [Fact]
    public async Task RenewAccessTokenAsync_ThrowsWhenTokenMissing()
    {
        using var httpClient = new HttpClient();
        var handler = new EtradeOAuth1AuthenticationHandler(httpClient, CreateConfig());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.RenewAccessTokenAsync(string.Empty, "secret"));
    }

    [Fact]
    public async Task RevokeAccessTokenAsync_ThrowsWhenTokenMissing()
    {
        using var httpClient = new HttpClient();
        var handler = new EtradeOAuth1AuthenticationHandler(httpClient, CreateConfig());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.RevokeAccessTokenAsync(string.Empty, "secret"));
    }
}
