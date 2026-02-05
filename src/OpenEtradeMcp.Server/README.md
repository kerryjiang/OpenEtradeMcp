# E*TRADE MCP Server

An MCP (Model Context Protocol) server that exposes E*TRADE API operations as tools for AI agents.

## Features

- **OAuth 1.0a Authentication**: Interactive OAuth flow that works with AI agents
- **E*TRADE API Tools**: Auto-generated tools from E*TRADE's OpenAPI specification
- **Sandbox Support**: Test with E*TRADE's sandbox environment

## Prerequisites

1. E*TRADE Developer Account with API access
2. Consumer Key and Consumer Secret from E*TRADE Developer Portal
3. .NET 9.0 SDK

## Configuration

Set environment variables:

```bash
export ETRADE_CONSUMER_KEY="your-consumer-key"
export ETRADE_CONSUMER_SECRET="your-consumer-secret"
export ETRADE_USE_SANDBOX="true"  # Optional: use sandbox environment
```

Or pass as command line arguments:

```bash
dotnet run -- <consumer-key> <consumer-secret> [--sandbox]
```

## Running the Server

```bash
# From the project directory
dotnet run

# Or with arguments
dotnet run -- your-key your-secret --sandbox
```

## OAuth Authentication Flow

The server provides interactive OAuth tools that allow an AI agent to guide users through authentication:

### 1. Start OAuth (`etrade_oauth_start`)

Call this tool to begin authentication. It returns an authorization URL.

**Agent workflow:**
```
Agent: "I'll start the E*TRADE authentication process."
[Calls etrade_oauth_start]
Agent: "Please click this link to authorize: https://us.etrade.com/e/t/etws/authorize?key=...&token=..."
```

### 2. Complete OAuth (`etrade_oauth_complete`)

After the user authorizes and gets the verifier code, call this tool.

**Agent workflow:**
```
User: "I got the code: ABC123"
Agent: "Great, let me complete the authentication."
[Calls etrade_oauth_complete with verifierCode="ABC123"]
Agent: "Authentication successful! You can now use E*TRADE API tools."
```

### 3. Check Status (`etrade_oauth_status`)

Check if the user is authenticated.

### 4. Renew Token (`etrade_oauth_renew`)

E*TRADE tokens expire at midnight Eastern time. Use this to renew.

### 5. Revoke Token (`etrade_oauth_revoke`)

Log out and revoke the access token.

## MCP Client Configuration

### Claude Desktop

Add to your Claude Desktop config (`~/Library/Application Support/Claude/claude_desktop_config.json` on macOS):

```json
{
  "mcpServers": {
    "etrade": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/OpenMcpApi.EtradeMcpServer"],
      "env": {
        "ETRADE_CONSUMER_KEY": "your-consumer-key",
        "ETRADE_CONSUMER_SECRET": "your-consumer-secret",
        "ETRADE_USE_SANDBOX": "true"
      }
    }
  }
}
```

### VS Code with Copilot

Configure in your workspace settings or MCP configuration.

## Available Tools

### OAuth Tools
- `etrade_oauth_start` - Start OAuth authentication flow
- `etrade_oauth_complete` - Complete OAuth with verifier code
- `etrade_oauth_status` - Check authentication status
- `etrade_oauth_renew` - Renew access token
- `etrade_oauth_revoke` - Revoke access token

### E*TRADE API Tools
Tools are auto-generated from the E*TRADE OpenAPI specification (`etrade-api.yaml`). These include account management, portfolio, orders, market quotes, and other E*TRADE API operations.

## Security Notes

- Never commit your consumer key/secret to source control
- Use environment variables for credentials
- Access tokens are stored in memory only (not persisted)
- Consider using the sandbox environment for testing

## Troubleshooting

### "OpenAPI spec file not found"
Ensure the `etrade-api.yaml` file is in the output directory. Check that the OpenMcpApi.Etrade project copies it to the output.

### "E*TRADE API credentials not provided"
Set the `ETRADE_CONSUMER_KEY` and `ETRADE_CONSUMER_SECRET` environment variables.

### Authentication fails
- Verify your credentials are correct
- Check if you're using sandbox credentials with sandbox mode enabled
- Ensure the verifier code is entered correctly (no extra spaces)
