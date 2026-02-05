using System;
using System.Net.Http;
using System.Net.Http.Headers;
using OpenApiMcpNet;

namespace OpenEtradeMcp;

/// <summary>
/// E*TRADE OAuth 1.0a authentication handler.
/// Implements the E*TRADE specific OAuth 1.0a flow with request token, authorization, and access token steps.
/// </summary>
public class EtradeOAuth1AuthenticationHandler : OAuth1AuthenticationHandler
{
    private readonly HttpClient _httpClient;
    private readonly ETradeConfig _etradeConfig;

    // E*TRADE API endpoints
    private readonly string _accessTokenUrl;
    private readonly string _renewAccessTokenUrl;
    private readonly string _revokeAccessTokenUrl;
    private readonly string _authorizationUrl = "https://us.etrade.com/e/t/etws/authorize?key={0}&token={1}";

    /// <summary>
    /// Creates an E*TRADE OAuth 1.0a authentication handler.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to make OAuth requests.</param>
    /// <param name="etradeConfig">The E*TRADE configuration settings.</param>
    public EtradeOAuth1AuthenticationHandler(
        HttpClient httpClient,
        ETradeConfig etradeConfig)
        : base(httpClient, $"{etradeConfig.AuthorizationBaseUrl}/oauth/request_token", $"{etradeConfig.AuthorizationBaseUrl}/oauth/access_token", etradeConfig.ConsumerKey, etradeConfig.ConsumerSecret, etradeConfig.SignatureMethod)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _etradeConfig = etradeConfig ?? throw new ArgumentNullException(nameof(etradeConfig));
        
        var baseUrl = etradeConfig.AuthorizationBaseUrl;

        _accessTokenUrl = $"{baseUrl}/oauth/access_token";
        _renewAccessTokenUrl = $"{baseUrl}/oauth/renew_access_token";
        _revokeAccessTokenUrl = $"{baseUrl}/oauth/revoke_access_token";
    }

    /// <summary>
    /// Generates the authorization URL for the given request token.
    /// The user should visit this URL to authorize the application and obtain the verifier code.
    /// </summary>
    /// <param name="requestToken">The request token obtained from GetRequestTokenPublicAsync.</param>
    /// <returns>The authorization URL.</returns>
    public string GetAuthorizationUrl(string requestToken)
    {
        return string.Format(_authorizationUrl, _etradeConfig.ConsumerKey, requestToken);
    }

    /// <summary>
    /// Step 1: Gets a request token from E*TRADE.
    /// This is the first step in the OAuth 1.0a flow.
    /// </summary>
    /// <returns>A tuple containing the request token and request token secret.</returns>
    public Task<(string token, string tokenSecret)> GetRequestTokenPublicAsync()
    {
        return GetRequestTokenInternalAsync();
    }

    internal new void SetAuthenticationResult(string accessToken, string accessTokenSecret)
    {
        base.SetAuthenticationResult(accessToken, accessTokenSecret);
    }

    protected override async Task<(string token, string tokenSecret)> GetAccessTokenAsync(string requestToken, string requestTokenSecret)
    {
        var verifier = await GetVerifierAsync(requestToken);
        return await CompleteAuthenticationAsync(verifier, requestToken, requestTokenSecret);
    }

    protected virtual Task<string> GetVerifierAsync(string requestToken)
    {
        throw new NotSupportedException("Automated verifier retrieval not supported. Use MCP tools to guide the user through authorization.");
    }

    /// <summary>
    /// Step 3: Completes authentication by exchanging the verifier code for an access token.
    /// Call this after the user has authorized the application and obtained a verifier code.
    /// </summary>
    /// <param name="verifier">The verifier code obtained from the authorization callback.</param>
    /// <param name="requestToken">The request token obtained from GetRequestTokenAsync.</param>
    /// <param name="requestTokenSecret">The request token secret obtained from GetRequestTokenAsync.</param>
    public async Task<(string AccessToken, string AccessTokenSecret)> CompleteAuthenticationAsync(string verifier, string requestToken, string requestTokenSecret)
    {
        if (string.IsNullOrEmpty(requestToken) || string.IsNullOrEmpty(requestTokenSecret))
        {
            throw new InvalidOperationException("Request token not obtained. Call GetRequestTokenAsync first.");
        }

        var timestamp = GetTimestamp();
        var nonce = GetNonce();

        var oauthParams = GetOAuthParameters(requestToken);
        oauthParams["oauth_verifier"] = verifier;

        var signature = GenerateSignature("GET", new Uri(_accessTokenUrl), oauthParams, requestTokenSecret);
        oauthParams[KeyOAuthSignature] = signature;

        var request = new HttpRequestMessage(HttpMethod.Get, _accessTokenUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", BuildAuthorizationHeader(oauthParams));

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Failed to obtain E*TRADE access token. Status: {response.StatusCode}, Response: {errorContent}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        return ParseTokenResponse(responseContent);
    }

    /// <summary>
    /// Renews the current access token.
    /// E*TRADE access tokens expire at midnight US Eastern time and must be renewed.
    /// </summary>
    public async Task RenewAccessTokenAsync(string token, string tokenSecret)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(tokenSecret))
        {
            throw new InvalidOperationException("No access token to renew. Complete authentication first.");
        }

        var timestamp = GetTimestamp();
        var nonce = GetNonce();

        var oauthParams = GetOAuthParameters(token);

        var signature = GenerateSignature("GET", new Uri(_renewAccessTokenUrl), oauthParams, tokenSecret);
        oauthParams[KeyOAuthSignature] = signature;

        var request = new HttpRequestMessage(HttpMethod.Get, _renewAccessTokenUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", BuildAuthorizationHeader(oauthParams));

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Failed to renew E*TRADE access token. Status: {response.StatusCode}, Response: {errorContent}");
        }

        // Token renewal successful - the same token remains valid
    }

    /// <summary>
    /// Revokes the current access token.
    /// </summary>
    public async Task RevokeAccessTokenAsync(string token, string tokenSecret)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(tokenSecret))
        {
            throw new InvalidOperationException("No access token to revoke.");
        }

        var timestamp = GetTimestamp();
        var nonce = GetNonce();

        var oauthParams = GetOAuthParameters(token);

        var signature = GenerateSignature("GET", new Uri(_revokeAccessTokenUrl), oauthParams, tokenSecret);
        oauthParams[KeyOAuthSignature] = signature;

        var request = new HttpRequestMessage(HttpMethod.Get, _revokeAccessTokenUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", BuildAuthorizationHeader(oauthParams));

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Failed to revoke E*TRADE access token. Status: {response.StatusCode}, Response: {errorContent}");
        }
    }
}
