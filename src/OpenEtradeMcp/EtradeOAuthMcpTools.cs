using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace OpenEtradeMcp;

/// <summary>
/// Holds the OAuth session state during the authentication flow.
/// </summary>
public class EtradeOAuthSession
{
    public string? RequestToken { get; set; }
    public string? RequestTokenSecret { get; set; }
    public string? AuthorizationUrl { get; set; }
    public string? AccessToken { get; set; }
    public string? AccessTokenSecret { get; set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(AccessTokenSecret);
}

/// <summary>
/// MCP tools for E*TRADE OAuth 1.0a authentication flow.
/// These tools allow an AI agent to guide a user through the OAuth process interactively.
/// </summary>
[McpServerToolType]
public class EtradeOAuthMcpTools
{
    private readonly EtradeOAuth1AuthenticationHandler _authHandler;
    private readonly EtradeOAuthSession _session;
    private readonly ETradeConfig _etradeConfig;

    public EtradeOAuthMcpTools(EtradeOAuth1AuthenticationHandler authHandler, EtradeOAuthSession session, ETradeConfig etradeConfig)
    {
        _authHandler = authHandler ?? throw new ArgumentNullException(nameof(authHandler));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _etradeConfig = etradeConfig ?? throw new ArgumentNullException(nameof(etradeConfig));
    }

    /// <summary>
    /// Step 1: Starts the E*TRADE OAuth authentication flow.
    /// Gets a request token and returns the authorization URL that the user must visit.
    /// </summary>
    /// <returns>
    /// A JSON object containing:
    /// - authorizationUrl: The URL the user must click to authorize the application
    /// - instructions: Instructions for the user
    /// </returns>
    [McpServerTool(Name = "etrade_oauth_start")]
    [Description("Starts the E*TRADE OAuth authentication flow. Returns an authorization URL that the user must click to log in and authorize the application. After authorization, the user will receive a verifier code that must be provided to the etrade_oauth_complete tool.")]
    public async Task<string> StartOAuthAsync()
    {
        try
        {
            // Get request token from E*TRADE
            var (requestToken, requestTokenSecret) = await _authHandler.GetRequestTokenPublicAsync();

            // Store in session
            _session.RequestToken = requestToken;
            _session.RequestTokenSecret = requestTokenSecret;

            // Generate authorization URL
            var authorizationUrl = _authHandler.GetAuthorizationUrl(requestToken);
            _session.AuthorizationUrl = authorizationUrl;

            var result = new
            {
                success = true,
                authorizationUrl = authorizationUrl,
                instructions = "Please click the authorization URL above to log in to E*TRADE and authorize this application. " +
                              "After you authorize, you will see a verification code on the page. " +
                              "Copy that code and provide it using the etrade_oauth_complete tool."
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            var error = new
            {
                success = false,
                error = ex.Message
            };
            return JsonSerializer.Serialize(error, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>
    /// Step 2: Completes the E*TRADE OAuth authentication flow.
    /// Exchanges the verifier code for access tokens.
    /// </summary>
    /// <param name="verifierCode">The verification code displayed after the user authorizes the application.</param>
    /// <returns>A JSON object indicating success or failure.</returns>
    [McpServerTool(Name = "etrade_oauth_complete")]
    [Description("Completes the E*TRADE OAuth authentication flow by exchanging the verifier code for access tokens. Call this after the user has clicked the authorization URL and obtained the verifier code.")]
    public async Task<string> CompleteOAuthAsync(
        [Description("The verification code that was displayed after the user authorized the application in their browser.")]
        string verifierCode)
    {
        try
        {
            if (string.IsNullOrEmpty(_session.RequestToken) || string.IsNullOrEmpty(_session.RequestTokenSecret))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "OAuth flow not started. Please call etrade_oauth_start first."
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            if (string.IsNullOrEmpty(verifierCode))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Verifier code is required."
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            // Exchange verifier for access token
            var (accessToken, accessTokenSecret) = await _authHandler.CompleteAuthenticationAsync(
                verifierCode.Trim(),
                _session.RequestToken,
                _session.RequestTokenSecret);

            // Store access tokens in session
            _session.AccessToken = accessToken;
            _session.AccessTokenSecret = accessTokenSecret;

            // Clear request tokens (no longer needed)
            _session.RequestToken = null;
            _session.RequestTokenSecret = null;
            _session.AuthorizationUrl = null;

            _authHandler.SetAuthenticationResult(accessToken, accessTokenSecret);

            return JsonSerializer.Serialize(new
            {
                success = true,
                message = "Authentication successful! You can now use E*TRADE API tools."
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>
    /// Gets the current OAuth authentication status.
    /// </summary>
    /// <returns>A JSON object with the current authentication state.</returns>
    [McpServerTool(Name = "etrade_oauth_status")]
    [Description("Checks the current E*TRADE OAuth authentication status.")]
    public string GetOAuthStatus()
    {
        var result = new
        {
            isAuthenticated = _session.IsAuthenticated,
            hasPendingAuthorization = !string.IsNullOrEmpty(_session.RequestToken),
            authorizationUrl = _session.AuthorizationUrl
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Renews the current E*TRADE access token.
    /// </summary>
    /// <returns>A JSON object indicating success or failure.</returns>
    [McpServerTool(Name = "etrade_oauth_renew")]
    [Description("Renews the current E*TRADE access token. E*TRADE tokens expire at midnight US Eastern time and must be renewed daily.")]
    public async Task<string> RenewOAuthAsync()
    {
        try
        {
            if (!_session.IsAuthenticated)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Not authenticated. Please complete the OAuth flow first using etrade_oauth_start and etrade_oauth_complete."
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            await _authHandler.RenewAccessTokenAsync(_session.AccessToken!, _session.AccessTokenSecret!);

            return JsonSerializer.Serialize(new
            {
                success = true,
                message = "Access token renewed successfully."
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>
    /// Revokes the current E*TRADE access token and clears the session.
    /// </summary>
    /// <returns>A JSON object indicating success or failure.</returns>
    [McpServerTool(Name = "etrade_oauth_revoke")]
    [Description("Revokes the current E*TRADE access token and logs out.")]
    public async Task<string> RevokeOAuthAsync()
    {
        try
        {
            if (!_session.IsAuthenticated)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Not authenticated. Nothing to revoke."
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            await _authHandler.RevokeAccessTokenAsync(_session.AccessToken!, _session.AccessTokenSecret!);

            // Clear session
            _session.AccessToken = null;
            _session.AccessTokenSecret = null;

            return JsonSerializer.Serialize(new
            {
                success = true,
                message = "Access token revoked. You are now logged out."
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
