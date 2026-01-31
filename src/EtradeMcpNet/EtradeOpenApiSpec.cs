using System.Reflection;

namespace EtradeMcpNet;

/// <summary>
/// Provides access to the embedded E*TRADE OpenAPI specification.
/// </summary>
public static class EtradeOpenApiSpec
{
    private const string ResourceName = "EtradeMcpNet.etrade-api.yaml";

    /// <summary>
    /// Reads the embedded E*TRADE OpenAPI specification as a string.
    /// </summary>
    /// <returns>The OpenAPI specification YAML content.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the embedded resource cannot be found.</exception>
    public static string GetOpenApiSpec()
    {
        var assembly = typeof(EtradeOpenApiSpec).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        
        if (stream == null)
        {
            throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found in assembly.");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
