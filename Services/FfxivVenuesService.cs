using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Privacy.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Privacy.Services;

internal sealed class FfxivVenuesService : IDisposable
{
    private const string ApiUrl = "https://api.ffxivvenues.com/v1.0/venue";
    private static readonly HashSet<string> NorthAmericaDataCenters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Aether", "Crystal", "Dynamis", "Primal",
    };

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly HttpClient http = new();
    private readonly object gate = new();
    private List<FfxivVenueEntry> venues = new();
    private DateTime lastFetchUtc = DateTime.MinValue;
    private DateTime lastRefreshAttemptUtc = DateTime.MinValue;
    private bool fetching;

    public FfxivVenuesService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Privacy-Dalamud/1.0");
        LoadCache();
    }

    public IReadOnlyList<FfxivVenueEntry> Venues
    {
        get
        {
            lock (gate)
                return venues.ToList();
        }
    }

    public void EnsureFreshAsync(bool force = false)
    {
        var now = DateTime.UtcNow;
        lock (gate)
        {
            if (fetching)
                return;

            if (!force && venues.Count > 0 && now - lastFetchUtc < TimeSpan.FromHours(24))
                return;

            if (!force && now - lastRefreshAttemptUtc < TimeSpan.FromMinutes(15))
                return;

            fetching = true;
            lastRefreshAttemptUtc = now;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var fetched = await FetchAsync(CancellationToken.None).ConfigureAwait(false);
                if (fetched.Count == 0)
                    return;

                lock (gate)
                {
                    venues = fetched;
                    lastFetchUtc = DateTime.UtcNow;
                }

                SaveCache(fetched);
                log.Debug("Privacy: loaded {Count} North America venues from FFXIV Venues.", fetched.Count);
            }
            catch (Exception ex)
            {
                log.Debug(ex, "Privacy: failed to refresh FFXIV Venues catalog.");
            }
            finally
            {
                lock (gate)
                    fetching = false;
            }
        });
    }

    public List<FfxivVenueEntry> Search(string text, int limit = 80)
    {
        var query = (text ?? string.Empty).Trim();
        lock (gate)
        {
            IEnumerable<FfxivVenueEntry> source = venues;
            if (!string.IsNullOrWhiteSpace(query))
                source = source.Where(v => v.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

            return source
                .OrderBy(v => v.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();
        }
    }

    public FfxivVenueEntry? FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        lock (gate)
            return venues.FirstOrDefault(v => string.Equals(v.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public FfxivVenueEntry? FindBestMatch(string name, string location)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var normalizedName = name.Trim();
        var normalizedLocation = NormalizeComparableLocation(location);
        lock (gate)
        {
            var candidates = venues.Where(v => string.Equals(v.Name, normalizedName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(normalizedLocation))
            {
                var locationMatch = candidates
                    .Where(v => normalizedLocation.Contains(NormalizeComparableLocation(v.BuildFullLocation()), StringComparison.OrdinalIgnoreCase)
                        || NormalizeComparableLocation(v.BuildFullLocation()).Contains(normalizedLocation, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(v => !string.IsNullOrWhiteSpace(v.DiscordUrl))
                    .FirstOrDefault();
                if (locationMatch != null)
                    return locationMatch;
            }

            return candidates
                .OrderByDescending(v => !string.IsNullOrWhiteSpace(v.DiscordUrl))
                .FirstOrDefault();
        }
    }


    public FfxivVenueEntry? FindByAddress(string dataCenter, string world, string district, int ward, int plot)
    {
        if (string.IsNullOrWhiteSpace(world) || string.IsNullOrWhiteSpace(district) || ward <= 0 || plot <= 0)
            return null;

        lock (gate)
        {
            return venues.FirstOrDefault(v =>
                string.Equals(v.DataCenter, dataCenter, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(v.World, world, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeDistrict(v.District), NormalizeDistrict(district), StringComparison.OrdinalIgnoreCase) &&
                v.Ward == ward && v.Plot == plot);
        }
    }

    public static PrivateVenueBookmark ToBookmark(FfxivVenueEntry entry, bool favorite = false)
        => new()
        {
            Name = entry.Name,
            DataCenter = entry.DataCenter,
            World = entry.World,
            District = entry.District,
            Ward = entry.Ward <= 0 ? 1 : entry.Ward,
            Plot = entry.Plot <= 0 ? 1 : entry.Plot,
            Address = entry.BuildFullLocation(),
            ImageUrl = entry.ImageUrl,
            WebsiteUrl = entry.WebsiteUrl,
            DiscordUrl = entry.DiscordUrl,
            TeleportCommand = entry.TeleportCommand,
            Favorite = favorite,
            Source = "FFXIVVenues",
        };

    private async Task<List<FfxivVenueEntry>> FetchAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(ApiUrl, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return new List<FfxivVenueEntry>();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return new List<FfxivVenueEntry>();

        var dedupe = new Dictionary<string, FfxivVenueEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var entry = ParseVenue(item);
            if (entry == null)
                continue;

            var key = BuildDedupeKey(entry);
            if (!dedupe.ContainsKey(key))
                dedupe[key] = entry;
        }

        return dedupe.Values.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static FfxivVenueEntry? ParseVenue(JsonElement item)
    {
        var name = ReadString(item, "name").Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Equals("Not a venue", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!item.TryGetProperty("location", out var loc) || loc.ValueKind != JsonValueKind.Object)
            return null;

        var dataCenter = ReadString(loc, "dataCenter");
        if (!NorthAmericaDataCenters.Contains(dataCenter))
            return null;

        var world = ReadString(loc, "world");
        if (string.IsNullOrWhiteSpace(world))
            return null;

        var district = NormalizeDistrict(ReadString(loc, "district"));
        var ward = ReadInt(loc, "ward");
        var plot = ReadInt(loc, "plot");
        var apartment = ReadInt(loc, "apartment");
        var room = ReadInt(loc, "room");
        var subdivision = ReadBool(loc, "subdivision");
        var id = ReadString(item, "id");
        var image = ReadString(item, "bannerUri");
        if (string.IsNullOrWhiteSpace(image)) image = ReadString(item, "iconUri");
        if (string.IsNullOrWhiteSpace(image)) image = ReadString(item, "imageUri");
        if (string.IsNullOrWhiteSpace(image)) image = ReadString(item, "thumbnailUri");
        if (string.IsNullOrWhiteSpace(image)) image = ReadString(item, "logoUri");
        if (string.IsNullOrWhiteSpace(image)) image = ReadString(item, "image");
        image = NormalizeRemoteUrl(image);

        var website = ReadString(item, "website");
        if (string.IsNullOrWhiteSpace(website) && !string.IsNullOrWhiteSpace(id))
            website = $"https://ffxivvenues.com/{id}";

        var discord = ReadFlexibleUrl(item, "discord");
        if (string.IsNullOrWhiteSpace(discord)) discord = ReadFlexibleUrl(item, "discordUrl");
        if (string.IsNullOrWhiteSpace(discord)) discord = ReadFlexibleUrl(item, "discordUri");
        if (string.IsNullOrWhiteSpace(discord)) discord = ReadNestedString(item, "links", "discord");
        if (string.IsNullOrWhiteSpace(discord)) discord = ReadNestedString(item, "socials", "discord");
        if (string.IsNullOrWhiteSpace(discord)) discord = ReadNestedString(item, "contact", "discord");
        if (string.IsNullOrWhiteSpace(discord)) discord = ReadNestedString(item, "contacts", "discord");
        discord = NormalizeDiscordUrl(discord);

        var entry = new FfxivVenueEntry
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            Name = name,
            DataCenter = dataCenter,
            World = world,
            District = district,
            Ward = ward,
            Plot = plot,
            Apartment = apartment,
            Room = room,
            Subdivision = subdivision,
            ImageUrl = image,
            WebsiteUrl = website,
            DiscordUrl = discord,
            OpeningTime = ResolveOpeningTime(item),
            ClosingTime = ResolveClosingTime(item),
            IsOpenNow = ResolveIsOpenNow(item),
            OpeningScheduleLines = ResolveOpeningScheduleLines(item),
        };
        entry.Address = entry.BuildFullLocation();
        entry.TeleportCommand = BuildTeleportCommand(entry);
        return entry;
    }

    private static string NormalizeDiscordUrl(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return text;
        if (text.StartsWith("discord.gg/", StringComparison.OrdinalIgnoreCase) || text.StartsWith("discord.com/", StringComparison.OrdinalIgnoreCase))
            return "https://" + text;
        return text;
    }


    private static bool TeleportCommandHasDataCenter(FfxivVenueEntry venue)
    {
        if (string.IsNullOrWhiteSpace(venue.TeleportCommand) || string.IsNullOrWhiteSpace(venue.DataCenter))
            return false;
        var text = venue.TeleportCommand.Trim();
        if (text.StartsWith("/li ", StringComparison.OrdinalIgnoreCase))
            text = text[4..].Trim();
        return text.StartsWith(venue.DataCenter + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildTeleportCommand(FfxivVenueEntry venue)
    {
        if (string.IsNullOrWhiteSpace(venue.World) || venue.Ward <= 0)
            return string.Empty;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(venue.DataCenter)) parts.Add(venue.DataCenter);
        parts.Add(venue.World);
        if (!string.IsNullOrWhiteSpace(venue.District)) parts.Add(venue.District.Equals("Lavender Beds", StringComparison.OrdinalIgnoreCase) ? "Lb" : venue.District);
        parts.Add($"w{venue.Ward}");
        if (venue.Plot > 0) parts.Add($"p{venue.Plot}");
        else if (venue.Apartment > 0) parts.Add($"a{venue.Apartment}");
        if (venue.Room > 0) parts.Add($"r{venue.Room}");
        return "/li " + string.Join(" ", parts);
    }

    private static string BuildDedupeKey(FfxivVenueEntry entry)
        => $"{entry.Name}|{entry.DataCenter}|{entry.World}|{NormalizeDistrict(entry.District)}|{entry.Ward}|{entry.Plot}";

    private static string NormalizeDistrict(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Equals("The Lavender Beds", StringComparison.OrdinalIgnoreCase) || text.Equals("Lavander Beds", StringComparison.OrdinalIgnoreCase))
            return "Lavender Beds";
        if (text.Equals("Lb", StringComparison.OrdinalIgnoreCase))
            return "Lavender Beds";
        return text;
    }



    private static bool ResolveIsOpenNow(JsonElement item)
    {
        var now = DateTimeOffset.UtcNow;
        if (item.TryGetProperty("schedule", out var schedules) && schedules.ValueKind == JsonValueKind.Array)
        {
            foreach (var schedule in schedules.EnumerateArray())
            {
                if (!schedule.TryGetProperty("resolution", out var resolution) || resolution.ValueKind != JsonValueKind.Object)
                    continue;

                var startText = ReadString(resolution, "start");
                if (!DateTimeOffset.TryParse(startText, out var start))
                    continue;
                var endText = ReadString(resolution, "end");
                var end = DateTimeOffset.TryParse(endText, out var parsedEnd) ? parsedEnd : start.AddHours(4);
                if (end < start)
                    end = end.AddDays(1);

                if (now >= start && now <= end)
                    return true;
            }
        }

        if (item.TryGetProperty("scheduleOverrides", out var overridesElement) && overridesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var overrideItem in overridesElement.EnumerateArray())
            {
                if (!ReadBool(overrideItem, "open"))
                    continue;
                var startText = ReadString(overrideItem, "start");
                if (!DateTimeOffset.TryParse(startText, out var start))
                    continue;
                var endText = ReadString(overrideItem, "end");
                var end = DateTimeOffset.TryParse(endText, out var parsedEnd) ? parsedEnd : start.AddHours(4);
                if (end < start)
                    end = end.AddDays(1);
                if (now >= start && now <= end)
                    return true;
            }
        }

        return false;
    }

    private static List<string> ResolveOpeningScheduleLines(JsonElement item)
    {
        var ranges = new List<(DateTimeOffset Start, DateTimeOffset? End)>();
        CollectScheduleRanges(item, ranges);

        return ranges
            .OrderBy(r => r.Start)
            .Take(12)
            .Select(r => FormatScheduleRange(r.Start, r.End))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectScheduleRanges(JsonElement item, List<(DateTimeOffset Start, DateTimeOffset? End)> ranges)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-12);
        if (item.TryGetProperty("schedule", out var schedules) && schedules.ValueKind == JsonValueKind.Array)
        {
            foreach (var schedule in schedules.EnumerateArray())
            {
                if (!schedule.TryGetProperty("resolution", out var resolution) || resolution.ValueKind != JsonValueKind.Object)
                    continue;
                var startText = ReadString(resolution, "start");
                if (!DateTimeOffset.TryParse(startText, out var start) || start < cutoff)
                    continue;
                var endText = ReadString(resolution, "end");
                var end = DateTimeOffset.TryParse(endText, out var parsedEnd) ? parsedEnd : (DateTimeOffset?)null;
                ranges.Add((start, end));
            }
        }

        if (item.TryGetProperty("scheduleOverrides", out var overridesElement) && overridesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var overrideItem in overridesElement.EnumerateArray())
            {
                if (!ReadBool(overrideItem, "open"))
                    continue;
                var startText = ReadString(overrideItem, "start");
                if (!DateTimeOffset.TryParse(startText, out var start) || start < cutoff)
                    continue;
                var endText = ReadString(overrideItem, "end");
                var end = DateTimeOffset.TryParse(endText, out var parsedEnd) ? parsedEnd : (DateTimeOffset?)null;
                ranges.Add((start, end));
            }
        }
    }

    private static string FormatScheduleRange(DateTimeOffset startUtc, DateTimeOffset? endUtc)
    {
        var start = startUtc.ToLocalTime();
        var end = endUtc?.ToLocalTime();
        var timeFormat = UsesTwelveHourClock() ? "h:mm tt" : "HH:mm";
        var startText = start.ToString(timeFormat, CultureInfo.CurrentCulture).ToLowerInvariant();
        var endText = end.HasValue ? end.Value.ToString(timeFormat, CultureInfo.CurrentCulture).ToLowerInvariant() : "?";
        return $"{start:ddd}: {startText} - {endText}";
    }

    private static bool UsesTwelveHourClock()
        => CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('t');

    private static string ResolveOpeningTime(JsonElement item)
    {
        if (TryFindNextSchedule(item, out var start, out _))
            return start.ToLocalTime().ToString("ddd HH:mm");
        return "Not listed";
    }

    private static string ResolveClosingTime(JsonElement item)
    {
        if (TryFindNextSchedule(item, out _, out var end) && end.HasValue)
            return end.Value.ToLocalTime().ToString("ddd HH:mm");
        return "Not listed";
    }

    private static bool TryFindNextSchedule(JsonElement item, out DateTimeOffset start, out DateTimeOffset? end)
    {
        start = DateTimeOffset.MaxValue;
        end = null;
        var found = false;

        if (item.TryGetProperty("schedule", out var schedules) && schedules.ValueKind == JsonValueKind.Array)
        {
            foreach (var schedule in schedules.EnumerateArray())
            {
                if (!schedule.TryGetProperty("resolution", out var resolution) || resolution.ValueKind != JsonValueKind.Object)
                    continue;
                var startText = ReadString(resolution, "start");
                if (!DateTimeOffset.TryParse(startText, out var candidateStart))
                    continue;
                if (candidateStart < DateTimeOffset.UtcNow.AddHours(-12))
                    continue;
                if (candidateStart >= start)
                    continue;

                start = candidateStart;
                var endText = ReadString(resolution, "end");
                end = DateTimeOffset.TryParse(endText, out var candidateEnd) ? candidateEnd : null;
                found = true;
            }
        }

        if (item.TryGetProperty("scheduleOverrides", out var overridesElement) && overridesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var overrideItem in overridesElement.EnumerateArray())
            {
                if (!ReadBool(overrideItem, "open"))
                    continue;
                var startText = ReadString(overrideItem, "start");
                if (!DateTimeOffset.TryParse(startText, out var candidateStart))
                    continue;
                var endText = ReadString(overrideItem, "end");
                var candidateEnd = DateTimeOffset.TryParse(endText, out var parsedEnd) ? parsedEnd : (DateTimeOffset?)null;
                if (candidateEnd.HasValue && candidateEnd.Value < DateTimeOffset.UtcNow)
                    continue;
                if (!candidateEnd.HasValue && candidateStart < DateTimeOffset.UtcNow.AddHours(-12))
                    continue;
                if (candidateStart >= start)
                    continue;

                start = candidateStart;
                end = candidateEnd;
                found = true;
            }
        }

        return found;
    }

    private static string NormalizeRemoteUrl(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        if (text.StartsWith("//", StringComparison.Ordinal))
            return "https:" + text;
        if (text.StartsWith("/", StringComparison.Ordinal))
            return "https://ffxivvenues.com" + text;
        return text;
    }

    private string CachePath => Path.Combine(pluginInterface.ConfigDirectory.FullName, "ffxivvenues_na_cache_v4.json");

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
                return;

            var cached = JsonSerializer.Deserialize<List<FfxivVenueEntry>>(File.ReadAllText(CachePath));
            if (cached == null)
                return;

            venues = cached.Where(v => !string.IsNullOrWhiteSpace(v.Name)).Select(NormalizeCachedEntry).ToList();
            lastFetchUtc = File.GetLastWriteTimeUtc(CachePath);
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Privacy: failed to load FFXIV Venues cache.");
        }
    }

    private static FfxivVenueEntry NormalizeCachedEntry(FfxivVenueEntry entry)
    {
        entry.District = NormalizeDistrict(entry.District);
        entry.Address = entry.BuildFullLocation();
        entry.TeleportCommand = BuildTeleportCommand(entry);
        entry.DiscordUrl = NormalizeDiscordUrl(entry.DiscordUrl);
        entry.OpeningScheduleLines ??= new List<string>();
        return entry;
    }

    private void SaveCache(List<FfxivVenueEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(pluginInterface.ConfigDirectory.FullName);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(entries));
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Privacy: failed to save FFXIV Venues cache.");
        }
    }

    private static string ReadFlexibleUrl(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
            return string.Empty;

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;

        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "url", "uri", "href", "invite", "value" })
            {
                var nested = ReadString(value, key);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var direct = item.GetString();
                    if (!string.IsNullOrWhiteSpace(direct))
                        return direct;
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    foreach (var key in new[] { "discord", "url", "uri", "href", "invite", "value" })
                    {
                        var nested = ReadString(item, key);
                        if (!string.IsNullOrWhiteSpace(nested))
                            return nested;
                    }
                }
            }
        }

        return string.Empty;
    }

    private static string ReadNestedString(JsonElement element, string parent, string child)
    {
        if (!element.TryGetProperty(parent, out var container) || container.ValueKind == JsonValueKind.Null)
            return string.Empty;

        if (container.ValueKind == JsonValueKind.Object)
            return ReadFlexibleUrl(container, child);

        if (container.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in container.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var value = ReadFlexibleUrl(item, child);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return string.Empty;
    }

    private static string NormalizeComparableLocation(string text)
    {
        var value = (text ?? string.Empty).Trim().ToLowerInvariant();
        value = value.Replace(" - ", ", ", StringComparison.Ordinal).Replace(" / ", ", ", StringComparison.Ordinal);
        value = Regex.Replace(value, "\\s+", " ");
        return value;
    }

    private static string ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() ?? string.Empty : string.Empty;

    private static int ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }

    private static bool ReadBool(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return false;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        return bool.TryParse(value.ToString(), out var parsed) && parsed;
    }

    public void Dispose() => http.Dispose();
}
