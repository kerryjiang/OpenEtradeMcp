# EtradeMcpNet

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-purple.svg)](https://dotnet.microsoft.com/)

An MCP (Model Context Protocol) server that exposes E*TRADE API operations as tools for AI agents. This allows AI assistants like Claude and GitHub Copilot to interact with E*TRADE's trading platform.

## Features

- **OAuth 1.0a Authentication**: Interactive OAuth flow designed to work seamlessly with AI agents
- **E*TRADE API Tools**: Auto-generated tools from E*TRADE's OpenAPI specification
- **Sandbox Support**: Test safely with E*TRADE's sandbox environment
- **Global Tool**: Install as a .NET global tool for easy access

## Prerequisites

- [.NET 8.0 or 9.0 SDK](https://dotnet.microsoft.com/download)
- E*TRADE Developer Account with API access
- Consumer Key and Consumer Secret from [E*TRADE Developer Portal](https://developer.etrade.com/)

## Installation

### As a .NET Global Tool

```bash
dotnet tool install --global EtradeMcpNet.Server
```

### From Source

```bash
git clone https://github.com/kerryjiang/EtradeMcpNet.git
cd EtradeMcpNet
dotnet build
```

## Configuration

### Environment Variables

```bash
export ETRADE_ConsumerKey="your-consumer-key"
export ETRADE_ConsumerSecret="your-consumer-secret"
export ETRADE_UseSandbox="true"  # Optional: use sandbox environment
```

### Command Line Arguments

```bash
etrade-mcp --ConsumerKey=your-key --ConsumerSecret=your-secret --UseSandbox=true
```

## Running the Server

### Using the Global Tool

```bash
etrade-mcp
```

### From Source

```bash
cd src/EtradeMcpNet.Server
dotnet run
```

## MCP Client Configuration

### Claude Desktop

Add to your Claude Desktop config file:

**macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`  
**Windows**: `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "etrade": {
      "command": "etrade-mcp",
      "env": {
        "ETRADE_ConsumerKey": "your-consumer-key",
        "ETRADE_ConsumerSecret": "your-consumer-secret",
        "ETRADE_UseSandbox": "true"
      }
    }
  }
}
```

### VS Code with GitHub Copilot

Configure in your VS Code MCP settings to use the `etrade-mcp` command with appropriate environment variables.

## OAuth Authentication Flow

The server provides interactive OAuth tools that allow an AI agent to guide users through authentication:

### 1. Start OAuth (`etrade_oauth_start`)

Begins the authentication process and returns an authorization URL.

```
Agent: "I'll start the E*TRADE authentication process."
[Calls etrade_oauth_start]
Agent: "Please click this link to authorize: https://us.etrade.com/e/t/etws/authorize?..."
```

### 2. Complete OAuth (`etrade_oauth_complete`)

After the user authorizes and receives the verifier code:

```
User: "I got the code: ABC123"
Agent: "Great, let me complete the authentication."
[Calls etrade_oauth_complete with verifierCode="ABC123"]
Agent: "Authentication successful! You can now use E*TRADE API tools."
```

### 3. Additional OAuth Tools

- `etrade_oauth_status` - Check authentication status
- `etrade_oauth_renew` - Renew access token (tokens expire at midnight Eastern)
- `etrade_oauth_revoke` - Log out and revoke access token

## Available Tools

### OAuth Tools
| Tool | Description |
|------|-------------|
| `etrade_oauth_start` | Start OAuth authentication flow |
| `etrade_oauth_complete` | Complete OAuth with verifier code |
| `etrade_oauth_status` | Check authentication status |
| `etrade_oauth_renew` | Renew access token |
| `etrade_oauth_revoke` | Revoke access token |

### E*TRADE API Tools

Tools are auto-generated from the E*TRADE OpenAPI specification and include:

- **Account Management** - List accounts, view account details
- **Portfolio** - View positions and holdings
- **Orders** - Place, preview, and manage orders
- **Market Data** - Get quotes, option chains, and market information
- **Alerts** - Manage price and trading alerts

## Project Structure

```
EtradeMcpNet/
├── src/
│   ├── EtradeMcpNet/              # Core library with E*TRADE API definitions
│   │   └── etrade-api.yaml        # E*TRADE OpenAPI specification
│   └── EtradeMcpNet.Server/       # MCP server executable
├── Directory.Build.props          # Shared build properties
├── Directory.Packages.props       # Centralized package management
└── EtradeMcpNet.sln              # Solution file
```

## Security Notes

- **Never commit credentials** - Use environment variables for your consumer key/secret
- **Memory-only tokens** - Access tokens are stored in memory and not persisted to disk
- **Use sandbox first** - Test with sandbox environment before using production credentials

## Troubleshooting

### "OpenAPI spec file not found"
Ensure the `etrade-api.yaml` file is in the output directory. Rebuild the project.

### "E*TRADE API credentials not provided"
Set the `ConsumerKey` and `ConsumerSecret` environment variables.

### Authentication fails
- Verify your credentials are correct
- Ensure sandbox credentials are used with `UseSandbox=true`
- Check that the verifier code is entered correctly (no extra spaces)

## License

This project is licensed under the Apache License 2.0 - see the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Author

**Kerry Jiang** - [GitHub](https://github.com/kerryjiang)
