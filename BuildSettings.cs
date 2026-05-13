using System;
using System.Linq;
using System.Reflection;

namespace Privacy;

internal static class BuildSettings
{
    public static string CloudApiBaseUrl { get; } = ReadMetadata("PrivacyCloudApiBaseUrl");

    private static string ReadMetadata(string key)
    {
        return Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value ?? string.Empty;
    }
}
