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

    /// <summary>
    /// When true, dangerous operations (order placement, cancellation, modification)
    /// require explicit user confirmation via MCP elicitation before execution.
    /// Default: false (existing behavior unchanged).
    /// Set via ETRADE_EnableOrderConfirmation=true
    /// </summary>
    public bool EnableOrderConfirmation { get; set; } = false;

    /// <summary>
    /// Comma-separated list of tool names (operationIds) that require user confirmation.
    /// Only applies when EnableOrderConfirmation is true.
    /// Default: "placeOrder,cancelOrder,placeChangeOrder"
    /// Set via ETRADE_GuardedTools=placeOrder,cancelOrder,placeChangeOrder
    /// </summary>
    public string GuardedTools { get; set; } = "placeOrder,cancelOrder,placeChangeOrder";

    /// <summary>
    /// How long (in seconds) a confirmation token remains valid when using the
    /// fallback token-based confirmation for clients that don't support elicitation.
    /// Default: 300 (5 minutes).
    /// Set via ETRADE_ConfirmationTimeoutSeconds=300
    /// </summary>
    public int ConfirmationTimeoutSeconds { get; set; } = 300;
}