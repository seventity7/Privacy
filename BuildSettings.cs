using System.Reflection;

namespace Privacy;

internal static class BuildSettings
{
    public static string CloudApiBaseUrl { get; } = LoadCloudApiBaseUrl();

    private static string LoadCloudApiBaseUrl()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (string.Equals(attribute.Key, "PrivacyCloudApiBaseUrl", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(attribute.Value))
                    return attribute.Value.Trim();
            }
        }
        catch
        {
        }

        return string.Empty;
    }
}
