using Dalamud.Plugin;
using System;
using System.IO;
using System.Linq;

namespace Privacy;

internal static class ConfigMigration
{
    private static readonly string[] OldPluginNames =
    {
        "PrivateListPlugin",
        "PrivateList",
        "Private List",
    };

    public static void Run(IDalamudPluginInterface pluginInterface)
    {
        try
        {
            var newConfigFile = pluginInterface.ConfigFile;
            var configRoot = newConfigFile.Directory;
            if (configRoot == null)
                return;

            MigrateConfigFile(newConfigFile, configRoot.FullName);
            MigrateConfigDirectories(pluginInterface.ConfigDirectory);
        }
        catch
        {
            // Migration must never block the plugin from loading.
        }
    }

    private static void MigrateConfigFile(FileInfo newConfigFile, string configRoot)
    {
        if (ConfigHasUserData(newConfigFile.FullName))
            return;

        var oldConfigFile = OldPluginNames
            .Select(name => Path.Combine(configRoot, $"{name}.json"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(oldConfigFile))
            return;

        Directory.CreateDirectory(configRoot);

        if (newConfigFile.Exists && newConfigFile.Length > 0)
        {
            var backupPath = Path.Combine(configRoot, $"{Path.GetFileNameWithoutExtension(newConfigFile.Name)}.pre-migration-backup.{DateTime.UtcNow:yyyyMMddHHmmss}.json");
            File.Copy(newConfigFile.FullName, backupPath, overwrite: false);
        }

        File.Copy(oldConfigFile, newConfigFile.FullName, overwrite: true);
    }

    private static void MigrateConfigDirectories(DirectoryInfo newDirectory)
    {
        var parent = newDirectory.Parent;
        if (parent == null)
            return;

        Directory.CreateDirectory(newDirectory.FullName);

        foreach (var oldName in OldPluginNames)
        {
            var oldDirectory = Path.Combine(parent.FullName, oldName);
            if (!Directory.Exists(oldDirectory))
                continue;

            CopyDirectory(oldDirectory, newDirectory.FullName);
        }
    }

    private static bool ConfigHasUserData(string path)
    {
        if (!File.Exists(path))
            return false;

        var text = File.ReadAllText(path);
        return HasNonEmptyArray(text, "Contacts") ||
            HasNonEmptyArray(text, "Venues") ||
            HasNonEmptyArray(text, "Groups") ||
            HasNonEmptyArray(text, "History") ||
            HasNonEmptyArray(text, "CloudSavedVenues") ||
            HasNonEmptyString(text, "CloudAccessToken") ||
            HasNonEmptyString(text, "CloudRefreshToken") ||
            HasNonEmptyString(text, "CustomMainBackgroundImagePath");
    }

    private static bool HasNonEmptyArray(string text, string propertyName)
    {
        var marker = $"\"{propertyName}\"";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return false;

        var arrayStart = text.IndexOf('[', index);
        var arrayEnd = text.IndexOf(']', arrayStart >= 0 ? arrayStart : index);
        if (arrayStart < 0 || arrayEnd < 0 || arrayEnd <= arrayStart)
            return false;

        return text.Substring(arrayStart + 1, arrayEnd - arrayStart - 1).Any(ch => !char.IsWhiteSpace(ch));
    }

    private static bool HasNonEmptyString(string text, string propertyName)
    {
        var marker = $"\"{propertyName}\"";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return false;

        var colon = text.IndexOf(':', index);
        if (colon < 0)
            return false;

        var firstQuote = text.IndexOf('"', colon + 1);
        if (firstQuote < 0)
            return false;

        var secondQuote = text.IndexOf('"', firstQuote + 1);
        return secondQuote > firstQuote + 1;
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(targetPath, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, file);
            var targetFile = Path.Combine(targetPath, relative);
            var targetDirectory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            if (!File.Exists(targetFile))
                File.Copy(file, targetFile, overwrite: false);
        }
    }
}
