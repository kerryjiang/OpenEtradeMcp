namespace OpenEtradeMcp;

/// <summary>
/// Configuration settings for the E*TRADE API client
/// </summary>
public class ETradeConfig
{
    /// <summary>
    /// The consumer key for OAuth authentication
    /// </summary>
    public string ConsumerKey { get; set; } = string.Empty;

    /// <summary>
    /// The consumer secret for OAuth authentication
    /// </summary>
    public string ConsumerSecret { get; set; } = string.Empty;

    /// <summary>
    /// The base URL for the E*TRADE API (production or sandbox)
    /// </summary>
    public string BaseUrl => UseSandbox ? SandboxBaseUrl : ProductionBaseUrl;

    /// <summary>
    /// The callback URL for OAuth authentication (set to "oob" for out-of-band)
    /// </summary>
    public string CallbackUrl { get; set; } = "oob";

    /// <summary>
    /// Timeout for HTTP requests in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to use sandbox environment
    /// </summary>
    public bool UseSandbox { get; set; } = false;

    /// <summary>
    /// The OAuth signature method
    /// </summary>
    public string SignatureMethod { get; set; } = "HMAC-SHA1";

    /// <summary>
    /// Gets the production base URL
    /// </summary>
    public static string ProductionBaseUrl => "https://api.etrade.com/v1";

    /// <summary>
    /// Gets the sandbox base URL
    /// </summary>
    public static string SandboxBaseUrl => "https://apisb.etrade.com/v1";

    /// <summary>
    /// The OAuth authorization endpoint URL
    /// </summary>
    public string AuthorizationBaseUrl => "https://api.etrade.com";
}