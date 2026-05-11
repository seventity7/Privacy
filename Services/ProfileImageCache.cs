using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Privacy.Services;

internal sealed class ProfileImageCache : IDisposable
{
    public const long MaxFileBytes = 2L * 1024L * 1024L;
    public const int MaxImageSide = 512;
    public const int MaxBackgroundWidth = 577;
    public const int MaxBackgroundHeight = 697;

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ITextureProvider textureProvider;
    private readonly IPluginLog log;
    private static readonly HttpClient remoteHttpClient = new();
    private readonly Dictionary<string, IDalamudTextureWrap> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> failedTexturePaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> failedRemoteVenueImages = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> remoteVenueDownloadLocks = new(StringComparer.OrdinalIgnoreCase);

    public ProfileImageCache(IDalamudPluginInterface pluginInterface, ITextureProvider textureProvider, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.textureProvider = textureProvider;
        this.log = log;
        Directory.CreateDirectory(ProfileImageDirectory);
    }

    public string ProfileImageDirectory => Path.Combine(pluginInterface.ConfigDirectory.FullName, "profile_images");

    public void Dispose()
    {
        foreach (var texture in cache.Values.Distinct())
            texture.Dispose();
        cache.Clear();
    }

    public bool TryImportImage(string sourcePath, string contactId, out string storedPath, out string error)
    {
        storedPath = string.Empty;
        error = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                error = "Image file was not found.";
                return false;
            }

            var info = new FileInfo(sourcePath);
            if (info.Length > MaxFileBytes)
            {
                error = "Image must be 2 MB or smaller.";
                return false;
            }

            var imageInfo = Image.Identify(sourcePath);
            if (imageInfo == null)
            {
                error = "File is not a supported image.";
                return false;
            }

            if (imageInfo.Width > MaxImageSide || imageInfo.Height > MaxImageSide)
            {
                error = "Image dimensions must be 512x512 or smaller.";
                return false;
            }

            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".webp")
            {
                error = "Only PNG, JPG, JPEG and WEBP images are supported.";
                return false;
            }

            var fileName = $"{SanitizeFileName(contactId)}{extension}";
            storedPath = Path.Combine(ProfileImageDirectory, fileName);
            File.Copy(sourcePath, storedPath, overwrite: true);
            Invalidate(storedPath);
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to import profile image.");
            error = "Failed to import image. Check the plugin log for details.";
            return false;
        }
    }

    public bool TryImportBackgroundImage(string sourcePath, out string storedPath, out string error)
    {
        storedPath = string.Empty;
        error = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                error = "Image file was not found.";
                return false;
            }

            var imageInfo = Image.Identify(sourcePath);
            if (imageInfo == null)
            {
                error = "File is not a supported image.";
                return false;
            }

            if (imageInfo.Width > MaxBackgroundWidth || imageInfo.Height > MaxBackgroundHeight)
            {
                error = "Background image must be 577x697 or smaller.";
                return false;
            }

            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".webp")
            {
                error = "Only PNG, JPG, JPEG and WEBP images are supported.";
                return false;
            }

            var normalizedExtension = extension == ".jpeg" ? ".jpg" : extension;
            storedPath = Path.Combine(ProfileImageDirectory, $"main_window_background{normalizedExtension}");
            File.Copy(sourcePath, storedPath, overwrite: true);
            Invalidate(storedPath);
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to import main window background image.");
            error = "Failed to import background image. Check the plugin log for details.";
            return false;
        }
    }

    public async Task<string> DownloadRemoteImageAsync(string imageUrl, string cacheKey, CancellationToken cancellationToken = default, string cacheVersion = "")
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return string.Empty;

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return string.Empty;

        try
        {
            var requestUri = BuildCacheBustedUri(uri, cacheVersion);
            using var response = await remoteHttpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return string.Empty;

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > MaxFileBytes)
                return string.Empty;

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.Length > MaxFileBytes)
                return string.Empty;

            var safeKey = SanitizeFileName(cacheKey);
            var tempPath = Path.Combine(ProfileImageDirectory, $"{safeKey}.remote.tmp");
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);

            var imageInfo = Image.Identify(tempPath);
            if (imageInfo == null || imageInfo.Width > MaxImageSide || imageInfo.Height > MaxImageSide)
            {
                File.Delete(tempPath);
                return string.Empty;
            }

            var extension = ResolveImageExtension(response.Content.Headers.ContentType?.MediaType, uri);
            var finalPath = Path.Combine(ProfileImageDirectory, $"{safeKey}.cloud{extension}");
            if (File.Exists(finalPath))
                Invalidate(finalPath);

            File.Move(tempPath, finalPath, overwrite: true);
            await File.WriteAllTextAsync(GetRemoteMarkerPath(safeKey), BuildRemoteMarker(imageUrl, cacheVersion), cancellationToken).ConfigureAwait(false);
            return finalPath;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Failed to download remote profile image from {Url}", imageUrl);
            return string.Empty;
        }
    }


    public bool IsRemoteVenueImageTemporarilyFailed(string cacheKey)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
            return false;

        var safeKey = SanitizeFileName("venue_" + cacheKey);
        return failedRemoteVenueImages.TryGetValue(safeKey, out var failedAt) && DateTime.UtcNow - failedAt < TimeSpan.FromHours(6);
    }

    public async Task<string> DownloadRemoteVenueImageAsync(string imageUrl, string cacheKey, CancellationToken cancellationToken = default, string cacheVersion = "")
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return string.Empty;

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return string.Empty;

        var safeKey = SanitizeFileName("venue_" + cacheKey);
        if (failedRemoteVenueImages.TryGetValue(safeKey, out var failedAt) && DateTime.UtcNow - failedAt < TimeSpan.FromHours(6))
            return string.Empty;

        var markerPath = GetRemoteMarkerPath(safeKey);
        var downloadLock = remoteVenueDownloadLocks.GetOrAdd(safeKey, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = Directory.GetFiles(ProfileImageDirectory, $"{safeKey}.cloud.*").FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(existing) && File.Exists(existing) && File.Exists(markerPath))
            {
                var marker = await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false);
                if (string.Equals(marker, BuildRemoteMarker(imageUrl, cacheVersion), StringComparison.Ordinal))
                    return existing;
            }

            var requestUri = BuildCacheBustedUri(uri, cacheVersion);
            using var response = await remoteHttpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return string.Empty;

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > 4L * 1024L * 1024L)
                return string.Empty;

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.Length > 4L * 1024L * 1024L)
                return string.Empty;

            await using var imageStream = new MemoryStream(bytes);
            using var image = await Image.LoadAsync(imageStream, cancellationToken).ConfigureAwait(false);
            if (image.Width <= 0 || image.Height <= 0 || image.Width > 4096 || image.Height > 4096)
            {
                failedRemoteVenueImages[safeKey] = DateTime.UtcNow;
                return string.Empty;
            }

            foreach (var oldFile in Directory.GetFiles(ProfileImageDirectory, $"{safeKey}.cloud.*"))
            {
                Invalidate(oldFile);
                TryDeleteFile(oldFile);
            }

            var tempPath = Path.Combine(ProfileImageDirectory, $"{safeKey}.{Guid.NewGuid():N}.remote.png");
            var finalPath = Path.Combine(ProfileImageDirectory, $"{safeKey}.cloud.png");
            await image.SaveAsPngAsync(tempPath, new PngEncoder(), cancellationToken).ConfigureAwait(false);

            if (File.Exists(finalPath))
                Invalidate(finalPath);

            File.Move(tempPath, finalPath, overwrite: true);
            await File.WriteAllTextAsync(markerPath, BuildRemoteMarker(imageUrl, cacheVersion), cancellationToken).ConfigureAwait(false);
            failedRemoteVenueImages.TryRemove(safeKey, out _);
            return finalPath;
        }
        catch (Exception ex)
        {
            failedRemoteVenueImages[safeKey] = DateTime.UtcNow;
            if (!IsImageDecodeFailure(ex))
                log.Debug(ex, "Failed to download remote venue image from {Url}", imageUrl);
            return string.Empty;
        }
        finally
        {
            downloadLock.Release();
        }
    }

    public IDalamudTextureWrap? GetTexture(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        var fileName = Path.GetFileName(path);
        if (fileName.StartsWith("venue_", StringComparison.OrdinalIgnoreCase) && fileName.Contains(".cloud.", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".cloud.png", StringComparison.OrdinalIgnoreCase))
        {
            failedTexturePaths.Add(path);
            TryDeleteFile(path);
            return null;
        }

        if (cache.TryGetValue(path, out var existing)) return existing;
        if (failedTexturePaths.Contains(path)) return null;

        try
        {
            var bytes = File.ReadAllBytes(path);
            var texture = textureProvider.CreateFromImageAsync(bytes).Result;
            cache[path] = texture;
            return texture;
        }
        catch (Exception ex)
        {
            failedTexturePaths.Add(path);
            if (Path.GetFileName(path).StartsWith("venue_", StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(path);
                return null;
            }

            log.Debug(ex, "Failed to load profile texture from {Path}", path);
            return null;
        }
    }

    public void Invalidate(string path)
    {
        failedTexturePaths.Remove(path);
        if (!cache.Remove(path, out var texture)) return;
        texture.Dispose();
    }


    public bool IsRemoteImageCurrent(string cacheKey, string imageUrl, string cacheVersion = "")
    {
        if (string.IsNullOrWhiteSpace(cacheKey) || string.IsNullOrWhiteSpace(imageUrl))
            return false;

        try
        {
            var markerPath = GetRemoteMarkerPath(SanitizeFileName(cacheKey));
            return File.Exists(markerPath) &&
                   string.Equals(File.ReadAllText(markerPath), BuildRemoteMarker(imageUrl, cacheVersion), StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsImageDecodeFailure(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            var name = current.GetType().Name;
            if (name.Contains("UnknownImageFormat", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("InvalidImageContent", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("ImageFormat", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static Uri BuildCacheBustedUri(Uri uri, string cacheVersion)
    {
        if (string.IsNullOrWhiteSpace(cacheVersion))
            return uri;

        var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
        return new Uri(uri + separator + "privacyCache=" + Uri.EscapeDataString(cacheVersion));
    }

    private string GetRemoteMarkerPath(string safeKey)
        => Path.Combine(ProfileImageDirectory, $"{safeKey}.cloud.url");

    private static string BuildRemoteMarker(string imageUrl, string cacheVersion)
        => string.IsNullOrWhiteSpace(cacheVersion) ? imageUrl.Trim() : $"{imageUrl.Trim()}|{cacheVersion.Trim()}";


    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static string ResolveImageExtension(string? mediaType, Uri uri)
    {
        var normalized = mediaType?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            _ => ResolveImageExtensionFromPath(uri.AbsolutePath),
        };
    }

    private static string ResolveImageExtensionFromPath(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" => extension == ".jpeg" ? ".jpg" : extension,
            _ => ".png",
        };
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }
}
