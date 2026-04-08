using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenApiMcpNet;

namespace OpenEtradeMcp;

/// <summary>
/// Provides a CallToolFilter that gates dangerous operations (order placement,
/// cancellation, modification) behind explicit user confirmation.
///
/// Supports two confirmation mechanisms:
/// 1. MCP Elicitation (preferred) — sends a native dialog to the client for
///    mechanical user confirmation that cannot be bypassed by the LLM.
/// 2. Token-based fallback — for clients that don't support elicitation,
///    returns a confirmation token that must be re-submitted with the request.
///
/// Enabled via configuration: ETRADE_EnableOrderConfirmation=true
/// Default: disabled (no change to existing behavior).
/// </summary>
public static class OrderConfirmationFilterExtensions
{
    private const string ConfirmationTokenParam = "confirmation_token";

    private static readonly ConcurrentDictionary<string, PendingConfirmation> _pendingTokens = new();

    // Cache for account key → friendly display mapping (fetched once per session)
    private static readonly ConcurrentDictionary<string, string> _accountDisplayCache = new();

    private sealed class PendingConfirmation
    {
        public required string ToolName { get; init; }
        public required string SerializedArguments { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
    }

    /// <summary>
    /// Registers the order confirmation filter on the MCP server builder.
    /// Call AFTER WithToolsFromOpenApi so the filter wraps the auto-generated tools.
    /// Does nothing if EnableOrderConfirmation is false.
    /// </summary>
    public static IMcpServerBuilder WithOrderConfirmation(
        this IMcpServerBuilder builder,
        ETradeConfig config)
    {
        if (!config.EnableOrderConfirmation)
        {
            return builder;
        }

        var guardedTools = new HashSet<string>(
            config.GuardedTools.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

        var tokenExpiry = TimeSpan.FromSeconds(config.ConfirmationTimeoutSeconds);

        // The filter wraps the next handler in the pipeline.
        // v1.2.0: filters are registered via WithRequestFilters → AddCallToolFilter
        builder.WithRequestFilters(filters => filters.AddCallToolFilter(next =>
        {
            return async (request, cancellationToken) =>
            {
                var toolName = request.Params?.Name;

                if (toolName == null || !guardedTools.Contains(toolName))
                {
                    return await next(request, cancellationToken);
                }

                // Check for fallback confirmation token
                var (token, cleanedArgs) = ExtractConfirmationToken(request.Params?.Arguments);

                if (token != null)
                {
                    if (_pendingTokens.TryRemove(token, out var pending) &&
                        DateTimeOffset.UtcNow - pending.CreatedAt < tokenExpiry &&
                        string.Equals(pending.ToolName, toolName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (request.Params?.Arguments != null && cleanedArgs != null)
                        {
                            request.Params.Arguments = cleanedArgs;
                        }
                        return await next(request, cancellationToken);
                    }

                    return new CallToolResult()
                    {
                        Content = [new TextContentBlock() { Text = JsonSerialize(new
                        {
                            status = "ORDER_BLOCKED",
                            message = "Confirmation token is invalid or expired. Please start the order process again."
                        }) }],
                        IsError = true
                    };
                }

                // First call — no token. Try elicitation, fall back to token.
                var serializedArgs = request.Params?.Arguments != null
                    ? JsonSerializer.Serialize(request.Params.Arguments, _jsonOptions)
                    : "{}";

                // For cancelOrder, fetch order details to show what's being canceled
                if (string.Equals(toolName, "cancelOrder", StringComparison.OrdinalIgnoreCase))
                {
                    serializedArgs = await EnrichCancelOrderMessage(
                        request.Params?.Arguments, request.Services, cancellationToken);
                }

                // Resolve accountIdKey to human-friendly account info for all guarded tools
                serializedArgs = await ResolveAccountDisplay(
                    serializedArgs, request.Params?.Arguments, request.Services, cancellationToken);

                var server = request.Services?.GetService<McpServer>();
                var supportsElicitation = server?.ClientCapabilities?.Elicitation != null;

                if (supportsElicitation && server != null)
                {
                    return await HandleWithElicitation(
                        server, toolName, serializedArgs, request, next, cancellationToken);
                }
                else
                {
                    return HandleWithToken(toolName, serializedArgs, tokenExpiry);
                }
            };
        }));

        return builder;
    }

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private static string JsonSerialize(object obj) =>
        JsonSerializer.Serialize(obj, _jsonOptions);

    private static async ValueTask<CallToolResult> HandleWithElicitation(
        McpServer server,
        string toolName,
        string serializedArgs,
        RequestContext<CallToolRequestParams> request,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        CancellationToken cancellationToken)
    {
        var orderSummary = FormatOrderSummary(toolName, serializedArgs);

        try
        {
            var result = await server.ElicitAsync(new ElicitRequestParams
            {
                Message = orderSummary,
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        ["confirm"] = new ElicitRequestParams.BooleanSchema
                        {
                            Description = "I have reviewed the order details and approve execution"
                        }
                    }
                }
            }, cancellationToken);

            if (result.Action == "accept" &&
                result.Content != null &&
                result.Content.TryGetValue("confirm", out var confirmValue) &&
                confirmValue.ValueKind == JsonValueKind.True)
            {
                return await next(request, cancellationToken);
            }

            var reason = result.Action switch
            {
                "decline" => "Order DECLINED by user.",
                "cancel" => "Order CANCELLED by user.",
                _ => $"Order not confirmed (action: {result.Action})."
            };

            return new CallToolResult()
            {
                Content = [new TextContentBlock() { Text = JsonSerialize(new
                {
                    status = "ORDER_BLOCKED",
                    action = result.Action,
                    message = reason
                }) }],
                IsError = false
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult()
            {
                Content = [new TextContentBlock() { Text = JsonSerialize(new
                {
                    status = "ORDER_BLOCKED",
                    message = $"Order confirmation failed: {ex.Message}. Order was NOT executed."
                }) }],
                IsError = true
            };
        }
    }

    private static CallToolResult HandleWithToken(string toolName, string serializedArgs, TimeSpan tokenExpiry)
    {
        CleanupExpiredTokens(tokenExpiry);

        var confirmToken = GenerateSecureToken();
        _pendingTokens[confirmToken] = new PendingConfirmation
        {
            ToolName = toolName,
            SerializedArguments = serializedArgs,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var expiryMinutes = (int)tokenExpiry.TotalMinutes;

        return new CallToolResult()
        {
            Content = [new TextContentBlock() { Text = JsonSerialize(new
            {
                status = "CONFIRMATION_REQUIRED",
                message = "This order requires user confirmation. Client does not support MCP elicitation; using token fallback.",
                instructions = new[]
                {
                    "Present the order details to the user clearly.",
                    "If confirmed, call this tool again with all parameters PLUS confirmation_token.",
                    "If declined, do NOT call the tool again.",
                    $"Token expires in {expiryMinutes} minute{(expiryMinutes != 1 ? "s" : "")}."
                },
                orderDetails = FormatOrderSummary(toolName, serializedArgs),
                confirmation_token = confirmToken,
                toolName
            }) }],
            IsError = false
        };
    }

    private static string FormatOrderSummary(string toolName, string serializedArgs)
    {
        var sb = new System.Text.StringBuilder();
        var toolLower = toolName.ToLowerInvariant();
        var title = toolLower switch
        {
            "cancelorder" => "ORDER CANCELLATION",
            "placechangeorder" => "ORDER MODIFICATION",
            _ => "ORDER PLACEMENT"
        };
        sb.AppendLine("══════════════════════════════════════════");
        sb.AppendLine($"  {title} — CONFIRMATION REQUIRED");
        sb.AppendLine("══════════════════════════════════════════");
        sb.AppendLine();
        try
        {
            using var doc = JsonDocument.Parse(serializedArgs);
            FormatJson(doc.RootElement, sb, 2);
        }
        catch
        {
            sb.AppendLine($"  Parameters: {serializedArgs}");
        }
        sb.AppendLine();
        sb.AppendLine("══════════════════════════════════════════");
        sb.AppendLine("  Review details above. Check confirm box.");
        sb.AppendLine("══════════════════════════════════════════");
        return sb.ToString();
    }

    private static void FormatJson(JsonElement el, System.Text.StringBuilder sb, int indent)
    {
        var p = new string(' ', indent);
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        sb.AppendLine($"{p}{prop.Name}:");
                        FormatJson(prop.Value, sb, indent + 2);
                    }
                    else sb.AppendLine($"{p}{prop.Name}: {prop.Value}");
                }
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in el.EnumerateArray())
                {
                    sb.AppendLine($"{p}[{i++}]:");
                    FormatJson(item, sb, indent + 2);
                }
                break;
            default:
                sb.AppendLine($"{p}{el}");
                break;
        }
    }

    private static (string? token, Dictionary<string, JsonElement>? args) ExtractConfirmationToken(
        IDictionary<string, JsonElement>? arguments)
    {
        if (arguments == null) return (null, null);
        var args = new Dictionary<string, JsonElement>(arguments);
        if (args.TryGetValue(ConfirmationTokenParam, out var val))
        {
            args.Remove(ConfirmationTokenParam);
            var tokenStr = val.ValueKind == JsonValueKind.String ? val.GetString() : val.ToString();
            return (tokenStr, args);
        }
        return (null, args);
    }

    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static void CleanupExpiredTokens(TimeSpan tokenExpiry)
    {
        var cutoff = DateTimeOffset.UtcNow - tokenExpiry;
        foreach (var kvp in _pendingTokens)
            if (kvp.Value.CreatedAt < cutoff)
                _pendingTokens.TryRemove(kvp.Key, out _);
    }

    /// <summary>
    /// For cancelOrder, fetches the actual order details from E*TRADE
    /// so the user can see exactly what they're canceling.
    /// Falls back to a basic message if the lookup fails.
    /// </summary>
    private static async Task<string> EnrichCancelOrderMessage(
        IDictionary<string, JsonElement>? arguments,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        string? accountIdKey = null;
        long? orderId = null;

        if (arguments != null)
        {
            if (arguments.TryGetValue("accountIdKey", out var acctEl))
                accountIdKey = acctEl.GetString();
            if (arguments.TryGetValue("orderId", out var orderEl))
            {
                if (orderEl.ValueKind == JsonValueKind.Number)
                    orderId = orderEl.GetInt64();
                else if (orderEl.ValueKind == JsonValueKind.String && long.TryParse(orderEl.GetString(), out var parsed))
                    orderId = parsed;
            }
        }

        // Try to fetch order details from the API
        if (accountIdKey != null && orderId != null && services != null)
        {
            try
            {
                var httpClient = services.GetService<HttpClient>();
                var authHandler = services.GetService<IAuthenticationHandler>();
                var config = services.GetService<ETradeConfig>();

                if (httpClient != null && authHandler != null && config != null)
                {
                    var url = $"{config.BaseUrl}/accounts/{accountIdKey}/orders";
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    authHandler.AuthenticateRequest(request,
                        Enumerable.Empty<KeyValuePair<string, string>>(),
                        Enumerable.Empty<KeyValuePair<string, JsonElement>>());
                    var response = await httpClient.SendAsync(request, cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(cancellationToken);
                        var orderInfo = ExtractOrderFromXml(body, orderId.Value);

                        if (orderInfo != null)
                        {
                            var enriched = new
                            {
                                WARNING = "YOU ARE ABOUT TO CANCEL THIS ORDER",
                                orderId,
                                symbol = orderInfo.Symbol,
                                description = orderInfo.Description,
                                action = orderInfo.OrderAction,
                                quantity = orderInfo.Quantity,
                                priceType = orderInfo.PriceType,
                                limitPrice = orderInfo.LimitPrice,
                                status = orderInfo.Status,
                                account = accountIdKey,
                                note = "This action CANNOT be undone."
                            };
                            return JsonSerializer.Serialize(enriched, _jsonOptions);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Fall through to basic message — log for diagnostics
                var logger = services?.GetService<ILoggerFactory>()?.CreateLogger("OrderConfirmation");
                logger?.LogWarning(ex, "Failed to fetch order details for cancel order enrichment (orderId: {OrderId})", orderId);
            }
        }

        // Fallback: basic message without order details
        var fallback = new
        {
            WARNING = "YOU ARE ABOUT TO CANCEL AN ORDER",
            orderId = orderId?.ToString() ?? "Unknown",
            account = accountIdKey ?? "Unknown",
            note = "This action CANNOT be undone. The order will be permanently cancelled.",
            suggestion = "If you are unsure, call listOrders first to review the order details."
        };
        return JsonSerializer.Serialize(fallback, _jsonOptions);
    }

    private static OrderInfo? ExtractOrderFromXml(string xml, long targetOrderId)
    {
        try
        {
            var orderTag = $"<orderId>{targetOrderId}</orderId>";
            var idx = xml.IndexOf(orderTag, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            var orderStart = xml.LastIndexOf("<Order>", idx, StringComparison.OrdinalIgnoreCase);
            var orderEnd = xml.IndexOf("</Order>", idx, StringComparison.OrdinalIgnoreCase);
            if (orderStart < 0 || orderEnd < 0) return null;

            var orderXml = xml.Substring(orderStart, orderEnd - orderStart + "</Order>".Length);

            return new OrderInfo
            {
                Symbol = ExtractXmlValue(orderXml, "symbol") ?? "Unknown",
                OrderAction = ExtractXmlValue(orderXml, "orderAction") ?? "Unknown",
                Quantity = ExtractXmlValue(orderXml, "orderedQuantity") ?? "Unknown",
                PriceType = ExtractXmlValue(orderXml, "priceType") ?? "Unknown",
                LimitPrice = ExtractXmlValue(orderXml, "limitPrice") ?? "N/A",
                Status = ExtractXmlValue(orderXml, "status") ?? "Unknown",
                SecurityType = ExtractXmlValue(orderXml, "securityType") ?? "EQ",
                Description = ExtractXmlValue(orderXml, "symbolDescription") ?? ""
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractXmlValue(string xml, string tag)
    {
        var openTag = $"<{tag}>";
        var closeTag = $"</{tag}>";
        var start = xml.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += openTag.Length;
        var end = xml.IndexOf(closeTag, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return null;
        return xml.Substring(start, end - start);
    }

    private sealed class OrderInfo
    {
        public string Symbol { get; init; } = "";
        public string OrderAction { get; init; } = "";
        public string Quantity { get; init; } = "";
        public string PriceType { get; init; } = "";
        public string LimitPrice { get; init; } = "";
        public string Status { get; init; } = "";
        public string SecurityType { get; init; } = "";
        public string Description { get; init; } = "";
    }

    /// <summary>
    /// Replaces the opaque accountIdKey in the serialized args with a
    /// human-friendly "accountId (description)" display string.
    /// Caches the account list so only one API call is made per session.
    /// </summary>
    private static async Task<string> ResolveAccountDisplay(
        string serializedArgs,
        IDictionary<string, JsonElement>? arguments,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        if (arguments == null || services == null)
            return serializedArgs;

        // Get the accountIdKey from args
        if (!arguments.TryGetValue("accountIdKey", out var acctKeyEl))
            return serializedArgs;

        var accountIdKey = acctKeyEl.GetString();
        if (string.IsNullOrEmpty(accountIdKey))
            return serializedArgs;

        // Check cache first
        if (_accountDisplayCache.TryGetValue(accountIdKey, out var cached))
        {
            return serializedArgs.Replace(accountIdKey, cached);
        }

        // Fetch account list from API
        try
        {
            var httpClient = services.GetService<HttpClient>();
            var authHandler = services.GetService<IAuthenticationHandler>();
            var config = services.GetService<ETradeConfig>();

            if (httpClient == null || authHandler == null || config == null)
                return serializedArgs;

            var url = $"{config.BaseUrl}/accounts/list";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            authHandler.AuthenticateRequest(request,
                Enumerable.Empty<KeyValuePair<string, string>>(),
                Enumerable.Empty<KeyValuePair<string, JsonElement>>());
            var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return serializedArgs;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // Parse all accounts and cache them
            ParseAndCacheAccounts(body);

            // Try cache again after populating
            if (_accountDisplayCache.TryGetValue(accountIdKey, out var resolved))
            {
                return serializedArgs.Replace(accountIdKey, resolved);
            }
        }
        catch (Exception ex)
        {
            // Fall through — show raw key. Log for diagnostics.
            var logger = services?.GetService<ILoggerFactory>()?.CreateLogger("OrderConfirmation");
            logger?.LogWarning(ex, "Failed to resolve account display for key: {AccountIdKey}", accountIdKey);
        }

        return serializedArgs;
    }

    /// <summary>
    /// Parses the accounts XML response and caches key → display mappings.
    /// </summary>
    private static void ParseAndCacheAccounts(string xml)
    {
        // Find each <Account> block and extract accountIdKey, accountId, accountDesc, accountName
        var searchFrom = 0;
        while (true)
        {
            var acctStart = xml.IndexOf("<Account>", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (acctStart < 0) break;

            var acctEnd = xml.IndexOf("</Account>", acctStart, StringComparison.OrdinalIgnoreCase);
            if (acctEnd < 0) break;

            var acctXml = xml.Substring(acctStart, acctEnd - acctStart + "</Account>".Length);

            var idKey = ExtractXmlValue(acctXml, "accountIdKey");
            var accountId = ExtractXmlValue(acctXml, "accountId");
            var accountDesc = ExtractXmlValue(acctXml, "accountDesc");
            var accountName = ExtractXmlValue(acctXml, "accountName");
            var accountMode = ExtractXmlValue(acctXml, "accountMode");

            if (!string.IsNullOrEmpty(idKey) && !string.IsNullOrEmpty(accountId))
            {
                // Build friendly display: "****5980 - Brokerage (NickName-1) [MARGIN]"
                var maskedId = accountId.Length > 4
                    ? "****" + accountId.Substring(accountId.Length - 4)
                    : accountId;

                var parts = new List<string> { maskedId };

                if (!string.IsNullOrEmpty(accountDesc))
                    parts.Add(accountDesc);

                if (!string.IsNullOrEmpty(accountName))
                    parts.Add($"({accountName})");

                if (!string.IsNullOrEmpty(accountMode))
                    parts.Add($"[{accountMode}]");

                _accountDisplayCache[idKey] = string.Join(" - ", parts);
            }

            searchFrom = acctEnd + 1;
        }
    }
}
