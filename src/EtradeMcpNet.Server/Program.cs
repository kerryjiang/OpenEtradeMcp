using EtradeMcpNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenApiMcpNet;
using Serilog;

try
{
    Console.Error.WriteLine("E*TRADE MCP Server starting...");

    var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables(prefix: "ETRADE_")
            .AddCommandLine(args)
            .Build();

    // Create E*TRADE configuration
    var etradeConfig = new ETradeConfig();
    configuration.Bind(etradeConfig);

    if (string.IsNullOrEmpty(etradeConfig.ConsumerKey) || string.IsNullOrEmpty(etradeConfig.ConsumerSecret))
    {
        Console.Error.WriteLine("E*TRADE API credentials not provided.");
        Console.Error.WriteLine("Set environment variables ETRADE_CONSUMERKEY and ETRADE_CONSUMERSECRET");
        Console.Error.WriteLine("Or pass them as command line arguments: --ConsumerKey=your-key --ConsumerSecret=your-secret --UseSandbox=true");
        return 1;
    }

    Console.Error.WriteLine($"E*TRADE API Base URL: {etradeConfig.BaseUrl}");
    Console.Error.WriteLine($"Using sandbox: {etradeConfig.UseSandbox}");

    // Load the E*TRADE OpenAPI spec from embedded resource
    string openApiSpec;
    try
    {
        openApiSpec = EtradeOpenApiSpec.GetOpenApiSpec();
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"Failed to load E*TRADE OpenAPI spec: {ex.Message}");
        return 1;
    }
    Console.Error.WriteLine("E*TRADE OpenAPI spec loaded successfully from embedded resource");

    // Build the MCP server - pass empty args to avoid parsing URL as host args
    var builder = Host.CreateApplicationBuilder(Array.Empty<string>());

    // IMPORTANT: Disable default console logging - it writes to stdout which breaks MCP protocol!
    builder.Logging.ClearProviders();

    var logPath = Path.Combine(AppContext.BaseDirectory, "EtradeMcpServer.log");

    Console.Error.WriteLine("logPath: " + logPath);

    builder.Services.AddSerilog(new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.File(logPath)
        .CreateLogger());

    Console.Error.WriteLine("Configuring MCP server...");

    // Create shared HttpClient and session
    var httpClient = new HttpClient { BaseAddress = new Uri(etradeConfig.BaseUrl) };
    var authHandler = new EtradeOAuth1AuthenticationHandler(httpClient, etradeConfig);
    var oauthSession = new EtradeOAuthSession();
    var oauthTools = new EtradeOAuthMcpTools(authHandler, oauthSession, etradeConfig);

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        // Register E*TRADE OAuth tools for interactive authentication
        .WithTools(oauthTools)
        // Register API tools from OpenAPI spec
        .WithToolsFromOpenApi(openApiSpec, etradeConfig.BaseUrl)
        .Services
        .AddSingleton(etradeConfig)
        .AddSingleton(httpClient)
        .AddSingleton(authHandler)
        .AddSingleton<IAuthenticationHandler>(authHandler)
        .AddSingleton(oauthSession)
        .AddSingleton(oauthTools);

    var app = builder.Build();

    await app.RunAsync();

    Console.Error.WriteLine("E*TRADE MCP Server ready. Listening on stdio...");

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"E*TRADE MCP Server terminated unexpectedly: {ex.Message}");
    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
    return 1;
}