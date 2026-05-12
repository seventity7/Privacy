using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Privacy.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Privacy.Services;

internal sealed class PrivacyCloudService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();


    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private readonly Configuration config;
    private readonly IDataManager dataManager;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;
    private readonly HttpClient httpClient = new();

    private DateTime nextHeartbeat = DateTime.MinValue;
    private ContactStatus lastPresenceStatusSent = ContactStatus.Offline;
    private DateTime nextProfileResolve = DateTime.MinValue;
    private bool syncInProgress;
    private string lastLoggedIdentitySignature = string.Empty;
    private DateTime lastLoggedIdentityAt = DateTime.MinValue;

    public PrivacyCloudService(Configuration config, IDataManager dataManager, IClientState clientState, IObjectTable objectTable, IChatGui chatGui, IPluginLog log)
    {
        this.config = config;
        this.dataManager = dataManager;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.chatGui = chatGui;
        this.log = log;
    }

    public bool HasApiBaseUrl => TryGetApiBaseUri(out _);
    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(config.CloudAccessToken);
    public string StatusText
    {
        get
        {
            if (!HasApiBaseUrl) return "Cloud API URL is not configured.";
            if (!IsLoggedIn) return "Not logged in.";
            var provider = string.IsNullOrWhiteSpace(config.CloudAccountProvider) ? "Privacy" : config.CloudAccountProvider;
            var character = string.IsNullOrWhiteSpace(config.CloudLinkedCharacterName)
                ? "No character linked"
                : $"{config.CloudLinkedCharacterName}@{config.CloudLinkedWorld}";
            return $"Logged in through {provider} - {character}";
        }
    }

    public void Dispose()
        => httpClient.Dispose();

    public void Logout()
    {
        config.CloudAccessToken = string.Empty;
        config.CloudRefreshToken = string.Empty;
        config.CloudUserId = string.Empty;
        config.CloudDisplayName = string.Empty;
        config.CloudAccountProvider = string.Empty;
        config.CloudTokenExpiresAt = DateTimeOffset.MinValue;
        config.Save();
    }

    public async Task<string> RegisterWithUsernameAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var identity = GetLocalCharacterIdentity();
        if (!identity.IsUsable)
            return "Could not identify the current character. Log into the game before creating an account.";

        var response = await PostAsync<AuthResponse>("auth/register", new
        {
            username,
            password,
            character = identity,
        }, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
            return response.Error;

        ApplyAuthResponse(response.Value, "Username", identity);
        return "Account created and linked to this character.";
    }

    public async Task<string> LoginWithUsernameAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var identity = GetLocalCharacterIdentity();
        if (!identity.IsUsable)
            return "Could not identify the current character. Log into the game before logging in.";

        var response = await PostAsync<AuthResponse>("auth/login", new
        {
            username,
            password,
            character = identity,
        }, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
            return response.Error;

        ApplyAuthResponse(response.Value, "Username", identity);
        return "Logged in and linked to this character.";
    }

    public string OpenDiscordLogin()
    {
        var identity = GetLocalCharacterIdentity();
        if (!identity.IsUsable)
            return "Could not identify the current character. Log into the game before starting Discord login.";

        if (!TryGetApiBaseUri(out var apiBase))
            return "Cloud API URL is not configured.";

        var url = new Uri(apiBase, $"v1/auth/discord/start?characterName={Uri.EscapeDataString(identity.CharacterName)}&worldId={identity.HomeWorldId}&world={Uri.EscapeDataString(identity.HomeWorld)}&contentId={identity.ContentId}");
        try
        {
            Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
            return "Discord login opened in your browser. Paste the returned code here after authorizing.";
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Failed to open Discord login URL.");
            return $"Open this URL manually: {url}";
        }
    }

    public async Task<string> CompleteDiscordLoginAsync(string code, CancellationToken cancellationToken = default)
    {
        var identity = GetLocalCharacterIdentity();
        if (!identity.IsUsable)
            return "Could not identify the current character. Log into the game before completing Discord login.";

        var response = await PostAsync<AuthResponse>("auth/discord/complete", new
        {
            code,
            character = identity,
        }, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
            return response.Error;

        ApplyAuthResponse(response.Value, "Discord", identity);
        return "Discord account linked to this character.";
    }

    public void FrameworkTick(PrivacyService listService, ProfileImageCache profileImages)
    {
        if (!config.CloudEnabled || !config.CloudAutoSync || !IsLoggedIn || !HasApiBaseUrl)
            return;

        if (syncInProgress)
            return;

        var now = DateTime.UtcNow;
        if (now < nextHeartbeat && now < nextProfileResolve)
            return;

        var shouldSendHeartbeat = config.CloudHeartbeatEnabled && (now >= nextHeartbeat || lastPresenceStatusSent != config.CloudPresenceStatus);
        var shouldResolveProfiles = config.CloudProfileLookupEnabled && now >= nextProfileResolve;
        var heartbeatIdentity = shouldSendHeartbeat ? GetLocalCharacterIdentity() : null;

        if (shouldSendHeartbeat)
            nextHeartbeat = DateTime.UtcNow.AddSeconds(45);

        if (shouldResolveProfiles)
            nextProfileResolve = DateTime.UtcNow.AddSeconds(60);

        syncInProgress = true;
        _ = Task.Run(async () =>
        {
            try
            {
                if (shouldSendHeartbeat && heartbeatIdentity is { IsUsable: true })
                    await SendHeartbeatAsync(heartbeatIdentity, CancellationToken.None).ConfigureAwait(false);

                if (shouldResolveProfiles)
                {
                    await ResolveProfilesAsync(config.Contacts, profileImages, CancellationToken.None).ConfigureAwait(false);
                    config.Save();
                }
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Privacy: cloud sync failed.");
            }
            finally
            {
                syncInProgress = false;
            }
        });
    }

    public async Task<string> SyncOwnProfileAsync(ProfileImageCache profileImages, CancellationToken cancellationToken = default)
    {
        if (!IsLoggedIn)
            return "You need to log in first.";

        var identity = GetLinkedOrLocalCharacterIdentity();
        if (!identity.IsUsable)
            return "Could not identify the current character.";

        if (string.IsNullOrWhiteSpace(config.CloudProfileDisplayName))
            config.CloudProfileDisplayName = string.IsNullOrWhiteSpace(config.CloudDisplayName) ? identity.CharacterName : config.CloudDisplayName;

        var payload = new
        {
            character = identity,
            displayName = config.CloudProfileDisplayName,
            bio = config.CloudProfileBio,
            statusMessage = config.CloudProfileStatusMessage,
            statusColorHex = config.CloudProfileStatusColorHex,
            avatarUrl = config.CloudProfileAvatarUrl,
            venues = GetCloudProfileVenues(),
            localProfile = FindOwnLocalProfile(identity),
        };

        var response = await PostAsync<ProfileSaveResponse>("profile/me", payload, cancellationToken).ConfigureAwait(false);
        if (!response.Success)
            return response.Error;

        ApplyProfileSaveResponse(response.Value);
        config.CloudLastProfileSyncAt = DateTimeOffset.UtcNow;
        config.Save();
        return "Cloud profile synced.";
    }

    public async Task<string> SaveCloudProfileAsync(string displayName, string bio, string statusMessage, string statusColorHex, string avatarUrl, CancellationToken cancellationToken = default)
    {
        if (!IsLoggedIn)
            return "You need to log in first.";

        var identity = GetLinkedOrLocalCharacterIdentity();
        if (!identity.IsUsable)
            return "Could not identify the current character.";

        var payload = new
        {
            character = identity,
            displayName,
            bio = bio.Trim().Length > 120 ? bio.Trim()[..120] : bio.Trim(),
            statusMessage = statusMessage.Trim().Length > 60 ? statusMessage.Trim()[..60] : statusMessage.Trim(),
            statusColorHex = NormalizeHex(statusColorHex, "#2BE5B5"),
            avatarUrl,
            tag = string.Empty,
            venues = GetCloudProfileVenues(),
        };

        var response = await PostAsync<ProfileSaveResponse>("profile/me", payload, cancellationToken).ConfigureAwait(false);
        if (!response.Success)
            return response.Error;

        config.CloudProfileDisplayName = displayName.Trim();
        config.CloudProfileBio = bio.Trim();
        config.CloudProfileStatusMessage = statusMessage.Trim();
        config.CloudProfileStatusColorHex = NormalizeHex(statusColorHex, "#2BE5B5");
        config.CloudProfileAvatarUrl = avatarUrl.Trim();
        config.CloudProfileTag = string.Empty;
        ApplyProfileSaveResponse(response.Value);
        config.CloudLastProfileSyncAt = DateTimeOffset.UtcNow;
        config.Save();
        return "Cloud profile saved.";
    }


    private static string NormalizeHex(string? value, string fallback)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.StartsWith('#'))
            text = text[1..];

        if (text.Length != 6 || text.Any(ch => !Uri.IsHexDigit(ch)))
            return fallback;

        return "#" + text.ToUpperInvariant();
    }

    public async Task<(bool Success, string AvatarUrl, string Error)> UploadAvatarAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (!IsLoggedIn)
            return (false, string.Empty, "You need to log in first.");

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return (false, string.Empty, "Image file was not found.");

        var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0 || bytes.Length > ProfileImageCache.MaxFileBytes)
            return (false, string.Empty, "Image must be 2 MB or smaller.");

        var response = await PostAsync<AvatarUploadResponse>("profile/avatar/upload", new
        {
            fileName = Path.GetFileName(imagePath),
            contentType = ResolveContentType(imagePath),
            dataBase64 = Convert.ToBase64String(bytes),
        }, cancellationToken).ConfigureAwait(false);

        if (!response.Success || response.Value == null || string.IsNullOrWhiteSpace(response.Value.AvatarUrl))
            return (false, string.Empty, response.Error);

        return (true, response.Value.AvatarUrl, string.Empty);
    }

    public async Task SendHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        var identity = GetLocalCharacterIdentity();
        if (!identity.IsUsable)
            return;

        await SendHeartbeatAsync(identity, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendHeartbeatAsync(CloudCharacterIdentity identity, CancellationToken cancellationToken = default)
    {
        if (!identity.IsUsable)
            return;

        var response = await PostAsync<object>("presence/heartbeat", new
        {
            character = identity,
            status = NormalizePresenceStatus(config.CloudPresenceStatus),
            timestamp = DateTimeOffset.UtcNow,
            profile = new
            {
                displayName = string.IsNullOrWhiteSpace(config.CloudProfileDisplayName) ? config.CloudDisplayName : config.CloudProfileDisplayName,
                bio = config.CloudProfileBio,
                statusMessage = config.CloudProfileStatusMessage,
                statusColorHex = config.CloudProfileStatusColorHex,
                avatarUrl = config.CloudProfileAvatarUrl,
                tag = config.CloudProfileTag,
                venues = GetCloudProfileVenues(),
            },
        }, cancellationToken).ConfigureAwait(false);

        if (response.Success)
        {
            config.CloudLinkedCharacterName = identity.CharacterName;
            config.CloudLinkedWorld = identity.HomeWorld;
            config.CloudLinkedWorldId = identity.HomeWorldId;
            config.CloudLinkedContentId = identity.ContentId;
            config.CloudLastHeartbeatAt = DateTimeOffset.UtcNow;
            lastPresenceStatusSent = config.CloudPresenceStatus;
            config.Save();
        }
    }

    public async Task ResolveProfilesAsync(IReadOnlyList<PrivateContact> contacts, ProfileImageCache profileImages, CancellationToken cancellationToken = default)
    {
        if (contacts.Count == 0)
            return;

        var candidates = contacts
            .Where(c => !string.IsNullOrWhiteSpace(c.Name) && (!string.IsNullOrWhiteSpace(c.World) || c.WorldId != 0 || c.ContentId != 0))
            .Select(c => new
            {
                localId = c.Id,
                name = c.Name,
                world = c.World,
                worldId = c.WorldId,
                contentId = c.ContentId,
            })
            .ToList();

        if (candidates.Count == 0)
            return;

        var response = await PostAsync<ResolveProfilesResponse>("profiles/resolve", new
        {
            contacts = candidates,
        }, cancellationToken).ConfigureAwait(false);

        if (!response.Success || response.Value == null)
            return;

        var profiles = response.Value.GetProfiles();
        foreach (var profile in profiles)
        {
            var contact = FindMatchingContact(contacts, profile);
            if (contact == null)
                continue;

            await ApplyCloudProfileAsync(contact, profile, profileImages, cancellationToken).ConfigureAwait(false);
        }

        config.CloudLastResolveAt = DateTimeOffset.UtcNow;
    }

    public CloudCharacterIdentity GetLocalCharacterIdentity()
    {
        var identity = new CloudCharacterIdentity();

        try
        {
            var localPlayer = ResolveLocalPlayer();
            if (localPlayer == null)
                return identity;

            identity.CharacterName = CleanName(ResolveObjectName(localPlayer));
            identity.HomeWorldId = ResolveWorldRowId(localPlayer, "HomeWorld");
            identity.HomeWorld = ResolveWorldName(identity.HomeWorldId);
            identity.CurrentWorldId = ResolveWorldRowId(localPlayer, "CurrentWorld");
            if (identity.CurrentWorldId == 0)
                identity.CurrentWorldId = identity.HomeWorldId;
            identity.CurrentWorld = ResolveWorldName(identity.CurrentWorldId);
            identity.DataCenter = ResolveDataCenterName(identity.HomeWorldId);
            identity.CurrentDataCenter = ResolveDataCenterName(identity.CurrentWorldId);
            identity.ContentId = ResolveLocalContentId(localPlayer);
            var location = GameLocationResolver.GetCurrent(dataManager, clientState);
            identity.Zone = location.Zone;
            identity.ResidentialDetails = string.IsNullOrWhiteSpace(location.ResidentialDetails)
                ? PrivacyService.BuildResidentialDetails(identity.Zone)
                : location.ResidentialDetails;

            LogLocalIdentityResolved(identity);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Privacy: failed to build cloud character identity.");
        }

        return identity;
    }

    private void LogLocalIdentityResolved(CloudCharacterIdentity identity)
    {
        if (!identity.IsUsable)
            return;

        var signature = $"{identity.CharacterName}|{identity.HomeWorldId}|{identity.ContentId}";
        var now = DateTime.UtcNow;
        if (string.Equals(signature, lastLoggedIdentitySignature, StringComparison.Ordinal) && now - lastLoggedIdentityAt < TimeSpan.FromMinutes(2))
            return;

        lastLoggedIdentitySignature = signature;
        lastLoggedIdentityAt = now;

        log.Information(
            "Privacy: cloud local identity resolved as {Name}@{World} worldId={WorldId} contentId={ContentId}.",
            identity.CharacterName,
            identity.HomeWorld,
            identity.HomeWorldId,
            identity.ContentId);
    }

    private async Task ApplyCloudProfileAsync(PrivateContact contact, CloudProfileSnapshot profile, ProfileImageCache profileImages, CancellationToken cancellationToken)
    {
        contact.CloudProfileId = profile.ProfileId;
        contact.CloudAccountLinked = true;
        contact.CloudDisplayName = profile.DisplayName;
        contact.CloudAvatarUrl = profile.AvatarUrl;
        contact.CloudStatusMessage = profile.StatusMessage;
        contact.CloudStatusColorHex = NormalizeHex(profile.StatusColorHex, "#2BE5B5");
        contact.CloudBio = profile.Bio;
        contact.CloudVenues = profile.Venues ?? new List<PrivateVenueBookmark>();
        contact.CloudLastSyncedAt = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(profile.DisplayName) && string.IsNullOrWhiteSpace(contact.Nickname))
            contact.Nickname = profile.DisplayName;

        if (!string.IsNullOrWhiteSpace(profile.Bio))
            contact.ProfileBio = profile.Bio;

        if (!string.IsNullOrWhiteSpace(profile.Tag) && string.IsNullOrWhiteSpace(contact.Tag))
        {
            contact.Tag = profile.Tag;
            if (!string.IsNullOrWhiteSpace(profile.TagColorHex))
                contact.TagColorHex = profile.TagColorHex;
        }

        if (profile.Status != ContactStatus.Offline)
        {
            contact.Status = profile.Status;
            contact.LastSeenAt = profile.LastSeenAt == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : profile.LastSeenAt;
        }
        else
        {
            contact.Status = ContactStatus.Offline;
        }

        if (!string.IsNullOrWhiteSpace(profile.CurrentWorld))
            contact.CurrentWorld = profile.CurrentWorld;
        if (profile.CurrentWorldId != 0)
            contact.CurrentWorldId = profile.CurrentWorldId;
        if (!string.IsNullOrWhiteSpace(profile.CurrentDataCenter))
            contact.CurrentDataCenter = profile.CurrentDataCenter;
        if (!string.IsNullOrWhiteSpace(profile.CurrentZone))
            contact.LastKnownZone = profile.CurrentZone;
        if (!string.IsNullOrWhiteSpace(profile.ResidentialDetails))
            contact.ResidentialDetails = profile.ResidentialDetails;

        if (!string.IsNullOrWhiteSpace(profile.AvatarUrl))
        {
            var avatarVersion = BuildAvatarCacheVersion(profile);
            if (!contact.CloudManagedProfileImage ||
                string.IsNullOrWhiteSpace(contact.ProfileImagePath) ||
                !profileImages.IsRemoteImageCurrent(contact.Id, profile.AvatarUrl, avatarVersion))
            {
                var storedPath = await profileImages.DownloadRemoteImageAsync(profile.AvatarUrl, contact.Id, cancellationToken, avatarVersion).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(storedPath))
                {
                    contact.ProfileImagePath = storedPath;
                    contact.CloudManagedProfileImage = true;
                }
            }
        }
    }

    private static string BuildAvatarCacheVersion(CloudProfileSnapshot profile)
    {
        if (profile.ProfileUpdatedAt != DateTimeOffset.MinValue)
            return profile.ProfileUpdatedAt.ToUnixTimeMilliseconds().ToString();

        if (profile.UpdatedAt != DateTimeOffset.MinValue)
            return profile.UpdatedAt.ToUnixTimeMilliseconds().ToString();

        return profile.AvatarUrl;
    }

    private PrivateContact? FindMatchingContact(IReadOnlyList<PrivateContact> contacts, CloudProfileSnapshot profile)
    {
        if (profile.ContentId != 0)
        {
            var byContent = contacts.FirstOrDefault(c => c.ContentId != 0 && c.ContentId == profile.ContentId);
            if (byContent != null)
                return byContent;
        }

        return contacts.FirstOrDefault(c =>
            string.Equals(CleanName(c.Name), CleanName(profile.CharacterName), StringComparison.OrdinalIgnoreCase) &&
            (c.WorldId != 0 && profile.HomeWorldId != 0
                ? c.WorldId == profile.HomeWorldId
                : string.Equals(c.World, profile.HomeWorld, StringComparison.OrdinalIgnoreCase)));
    }

    private object? FindOwnLocalProfile(CloudCharacterIdentity identity)
    {
        var own = config.Contacts.FirstOrDefault(c =>
            (identity.ContentId != 0 && c.ContentId == identity.ContentId) ||
            (string.Equals(c.Name, identity.CharacterName, StringComparison.OrdinalIgnoreCase) &&
             (c.WorldId == identity.HomeWorldId || string.Equals(c.World, identity.HomeWorld, StringComparison.OrdinalIgnoreCase))));

        if (own == null)
            return null;

        return new
        {
            own.DisplayName,
            own.ProfileBio,
            own.ProfileImagePath,
            own.Tag,
            own.TagColorHex,
            own.MainJob,
            own.Role,
            own.Nameday,
            own.PreferredContent,
            own.RelationshipStatus,
        };
    }

    private void ApplyAuthResponse(AuthResponse? response, string fallbackProvider, CloudCharacterIdentity identity)
    {
        if (response == null)
            return;

        config.CloudAccessToken = response.AccessToken ?? response.Token ?? string.Empty;
        config.CloudRefreshToken = response.RefreshToken ?? string.Empty;
        config.CloudUserId = response.UserId ?? response.Profile?.UserId ?? string.Empty;
        config.CloudDisplayName = response.DisplayName ?? response.Profile?.DisplayName ?? identity.CharacterName;
        if (string.IsNullOrWhiteSpace(config.CloudProfileDisplayName))
            config.CloudProfileDisplayName = response.Profile?.DisplayName ?? config.CloudDisplayName;
        config.CloudProfileBio = response.Profile?.Bio ?? config.CloudProfileBio;
        config.CloudProfileStatusMessage = response.Profile?.StatusMessage ?? config.CloudProfileStatusMessage;
        config.CloudProfileStatusColorHex = NormalizeHex(response.Profile?.StatusColorHex, config.CloudProfileStatusColorHex);
        config.CloudProfileAvatarUrl = response.Profile?.AvatarUrl ?? config.CloudProfileAvatarUrl;
        config.CloudProfileTag = response.Profile?.Tag ?? config.CloudProfileTag;
        if (response.Profile?.Venues != null)
            config.CloudSavedVenues = response.Profile.Venues;
        config.CloudAccountProvider = string.IsNullOrWhiteSpace(response.Provider) ? fallbackProvider : response.Provider;
        config.CloudTokenExpiresAt = response.ExpiresAt == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow.AddDays(7) : response.ExpiresAt;
        config.CloudLinkedCharacterName = identity.CharacterName;
        config.CloudLinkedWorld = identity.HomeWorld;
        config.CloudLinkedWorldId = identity.HomeWorldId;
        config.CloudLinkedContentId = identity.ContentId;
        config.CloudEnabled = true;
        config.CloudAutoSync = true;
        config.Save();

        log.Information("Privacy: cloud account linked to {Name}@{World}.", identity.CharacterName, identity.HomeWorld);
    }

    private void ApplyProfileSaveResponse(ProfileSaveResponse? response)
    {
        if (response?.Profile == null)
            return;

        config.CloudUserId = string.IsNullOrWhiteSpace(response.Profile.UserId) ? config.CloudUserId : response.Profile.UserId;
        config.CloudDisplayName = string.IsNullOrWhiteSpace(response.Profile.DisplayName) ? config.CloudDisplayName : response.Profile.DisplayName;
        config.CloudProfileDisplayName = response.Profile.DisplayName ?? config.CloudProfileDisplayName;
        config.CloudProfileBio = response.Profile.Bio ?? config.CloudProfileBio;
        config.CloudProfileStatusMessage = response.Profile.StatusMessage ?? config.CloudProfileStatusMessage;
        config.CloudProfileStatusColorHex = NormalizeHex(response.Profile.StatusColorHex, config.CloudProfileStatusColorHex);
        config.CloudProfileAvatarUrl = response.Profile.AvatarUrl ?? config.CloudProfileAvatarUrl;
        config.CloudProfileTag = response.Profile.Tag ?? config.CloudProfileTag;
        if (response.Profile.Venues != null)
            config.CloudSavedVenues = response.Profile.Venues;
    }

    private CloudCharacterIdentity GetLinkedOrLocalCharacterIdentity()
    {
        if (!string.IsNullOrWhiteSpace(config.CloudLinkedCharacterName) && config.CloudLinkedWorldId != 0)
        {
            return new CloudCharacterIdentity
            {
                CharacterName = config.CloudLinkedCharacterName,
                HomeWorld = config.CloudLinkedWorld,
                HomeWorldId = config.CloudLinkedWorldId,
                ContentId = config.CloudLinkedContentId,
                CurrentWorld = config.CloudLinkedWorld,
                CurrentWorldId = config.CloudLinkedWorldId,
                CurrentDataCenter = string.Empty,
                DataCenter = string.Empty,
                Zone = string.Empty,
                ResidentialDetails = string.Empty,
            };
        }

        return GetLocalCharacterIdentity();
    }

    private async Task<ApiResult<T>> PostAsync<T>(string path, object payload, CancellationToken cancellationToken)
    {
        if (!TryGetApiBaseUri(out var apiBase))
            return ApiResult<T>.Fail("Cloud API URL is not configured.");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(apiBase, $"v1/{path.TrimStart('/')}"));
            request.Content = JsonContent.Create(payload, options: JsonOptions);
            if (!string.IsNullOrWhiteSpace(config.CloudAccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.CloudAccessToken);

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = response.Content == null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                log.Warning("Privacy: cloud API returned {StatusCode} for {Path}. Body: {Body}", (int)response.StatusCode, path, body);
                return ApiResult<T>.Fail(ReadError(body, $"Cloud API returned {(int)response.StatusCode}."));
            }

            if (typeof(T) == typeof(object))
                return ApiResult<T>.Ok(default);

            var value = JsonSerializer.Deserialize<T>(body, JsonOptions);
            return ApiResult<T>.Ok(value);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Privacy: cloud request failed for {Path}.", path);
            return ApiResult<T>.Fail("Cloud request failed. Check /xllog for details.");
        }
    }

    private bool TryGetApiBaseUri(out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(config.CloudApiBaseUrl))
            config.CloudApiBaseUrl = "https://privacy-api.kkevinbhrain.workers.dev";

        var value = config.CloudApiBaseUrl.Trim();
        if (!value.EndsWith("/", StringComparison.Ordinal))
            value += "/";

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
            return false;

        uri = parsed;
        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }



    private List<PrivateVenueBookmark> GetCloudProfileVenues()
    {
        return config.CloudSavedVenues
            .Where(v => v.Favorite)
            .GroupBy(v => NormalizeVenueAddress(v.BuildAddress()), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(24)
            .ToList();
    }

    private static string NormalizeVenueAddress(string value)
        => System.Text.RegularExpressions.Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");

    private static string NormalizePresenceStatus(ContactStatus status)
        => status switch
        {
            ContactStatus.Idle => "Idle",
            ContactStatus.Busy => "Busy",
            ContactStatus.Afk => "AFK",
            ContactStatus.Content => "Content",
            ContactStatus.Streaming => "Streaming",
            ContactStatus.Online => "Online",
            _ => "Online",
        };

    private static ContactStatus ResolveCloudPresenceStatus(CloudPresenceDto? presence)
    {
        if (presence?.Online != true)
            return ContactStatus.Offline;

        var value = (presence.Status ?? presence.StatusText ?? string.Empty).Trim();
        if (value.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            return ContactStatus.Idle;
        if (value.Equals("Busy", StringComparison.OrdinalIgnoreCase))
            return ContactStatus.Busy;
        if (value.Equals("AFK", StringComparison.OrdinalIgnoreCase) || value.Equals("Afk", StringComparison.OrdinalIgnoreCase))
            return ContactStatus.Afk;
        if (value.Equals("Content", StringComparison.OrdinalIgnoreCase))
            return ContactStatus.Content;
        if (value.Equals("Streaming", StringComparison.OrdinalIgnoreCase))
            return ContactStatus.Streaming;

        return ContactStatus.Online;
    }

    private static string ResolveContentType(string imagePath)
    {
        return Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png",
        };
    }

    private static string ReadError(string body, string fallback)
    {
        if (string.IsNullOrWhiteSpace(body))
            return fallback;

        try
        {
            var parsed = JsonSerializer.Deserialize<ErrorResponse>(body, JsonOptions);
            if (!string.IsNullOrWhiteSpace(parsed?.Detail))
                return parsed.Detail;
            if (!string.IsNullOrWhiteSpace(parsed?.Message))
                return parsed.Message;
            if (!string.IsNullOrWhiteSpace(parsed?.Error))
                return parsed.Error;
        }
        catch
        {
        }

        var trimmed = body.Trim();
        return trimmed.Length > 240 ? trimmed[..240] : trimmed;
    }

    private object? ResolveLocalPlayer()
    {
        try
        {
            var localPlayer = clientState.GetType()
                .GetProperty("LocalPlayer", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(clientState);

            if (localPlayer != null)
                return localPlayer;
        }
        catch
        {
            // Fall back to the object table on API surfaces that no longer expose LocalPlayer.
        }

        var localContentId = ResolveClientStateContentId();
        if (localContentId != 0)
        {
            var byContentId = objectTable
                .OfType<IPlayerCharacter>()
                .FirstOrDefault(player => ResolveLocalContentId(player) == localContentId);

            if (byContentId != null)
                return byContentId;
        }

        var byObjectIndex = objectTable
            .OfType<IPlayerCharacter>()
            .FirstOrDefault(player => ResolveObjectIndex(player) == 0 && ResolveWorldRowId(player, "HomeWorld") != 0);

        if (byObjectIndex != null)
            return byObjectIndex;

        return objectTable
            .OfType<IPlayerCharacter>()
            .FirstOrDefault(player => !string.IsNullOrWhiteSpace(ResolveObjectName(player)) && ResolveWorldRowId(player, "HomeWorld") != 0);
    }

    private ulong ResolveClientStateContentId()
    {
        foreach (var name in new[] { "LocalContentId", "ContentId", "ContentID" })
        {
            var value = clientState.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(clientState);
            if (value is ulong ul) return ul;
            if (value is long l && l > 0) return (ulong)l;
            if (value is uint u) return u;
        }

        return 0;
    }

    private static int ResolveObjectIndex(object obj)
    {
        foreach (var name in new[] { "ObjectIndex", "Index" })
        {
            var value = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj);
            if (value is int i) return i;
            if (value is short s) return s;
            if (value is byte b) return b;
        }

        return -1;
    }

    private static string ResolveObjectName(object obj)
    {
        var value = obj.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj);
        return CleanName(value?.ToString() ?? string.Empty);
    }

    private ulong ResolveLocalContentId(object localPlayer)
    {
        foreach (var source in new object?[] { clientState, localPlayer })
        {
            if (source == null)
                continue;

            foreach (var name in new[] { "LocalContentId", "ContentId", "ContentID" })
            {
                var value = source.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(source);
                if (value is ulong ul) return ul;
                if (value is long l && l > 0) return (ulong)l;
                if (value is uint u) return u;
            }
        }

        return 0;
    }

    private uint ResolveWorldRowId(object localPlayer, string propertyName)
    {
        var world = localPlayer.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(localPlayer);
        var rowId = world?.GetType().GetProperty("RowId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(world);
        return rowId switch
        {
            uint u => u,
            ushort us => us,
            int i when i > 0 => (uint)i,
            _ => 0u,
        };
    }

    private string ResolveCurrentZoneName()
    {
        try
        {
            var territory = dataManager.GetExcelSheet<TerritoryType>().GetRow(clientState.TerritoryType);
            return territory.PlaceName.Value.Name.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private string ResolveWorldName(uint worldId)
    {
        if (worldId == 0) return string.Empty;

        try
        {
            var world = dataManager.GetExcelSheet<World>().GetRow(worldId);
            return world.Name.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private string ResolveDataCenterName(uint worldId)
    {
        if (worldId == 0) return string.Empty;

        try
        {
            var world = dataManager.GetExcelSheet<World>().GetRow(worldId);
            return world.DataCenter.Value.Name.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string CleanName(string name)
    {
        return name
            .Replace("\uE05D", string.Empty, StringComparison.Ordinal)
            .Replace("\uE05E", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private sealed class AuthResponse
    {
        public string? AccessToken { get; set; }
        public string? Token { get; set; }
        public string? RefreshToken { get; set; }
        public string? UserId { get; set; }
        public string? DisplayName { get; set; }
        public string? Provider { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public CloudProfileDto? Profile { get; set; }
    }

    private sealed class ProfileSaveResponse
    {
        public string? ProfileId { get; set; }
        public CloudProfileDto? Profile { get; set; }
    }

    private sealed class ResolveProfilesResponse
    {
        public List<CloudProfileSnapshot> Profiles { get; set; } = new();
        public List<CloudResolveResult> Results { get; set; } = new();

        public IEnumerable<CloudProfileSnapshot> GetProfiles()
        {
            if (Profiles.Count > 0)
                return Profiles;

            return Results
                .Where(result => result.Found)
                .Select(result =>
                {
                    var snapshot = new CloudProfileSnapshot
                    {
                        ProfileId = result.UserId ?? string.Empty,
                        CharacterName = result.CharacterName ?? string.Empty,
                        HomeWorld = result.HomeWorldName ?? string.Empty,
                        HomeWorldId = result.HomeWorldId,
                        ContentId = ParseUlong(result.ContentId),
                        DisplayName = result.Profile?.DisplayName ?? string.Empty,
                        AvatarUrl = result.Profile?.AvatarUrl ?? string.Empty,
                        Bio = result.Profile?.Bio ?? string.Empty,
                        Tag = result.Profile?.Tag ?? string.Empty,
                        StatusMessage = result.Profile?.StatusMessage ?? string.Empty,
                        StatusColorHex = NormalizeHex(result.Profile?.StatusColorHex, "#2BE5B5"),
                        Venues = result.Profile?.Venues ?? new List<PrivateVenueBookmark>(),
                        Status = ResolveCloudPresenceStatus(result.Presence),
                        CurrentDataCenter = result.Presence?.CurrentDataCenter ?? string.Empty,
                        CurrentWorld = result.Presence?.CurrentWorld ?? string.Empty,
                        CurrentWorldId = result.Presence?.CurrentWorldId ?? 0,
                        CurrentZone = result.Presence?.CurrentZone ?? string.Empty,
                        ResidentialDetails = result.Presence?.ResidentialInfo ?? string.Empty,
                        LastSeenAt = ParseDate(result.Presence?.LastSeenAt),
                        UpdatedAt = ParseDate(result.Presence?.UpdatedAt),
                        ProfileUpdatedAt = ParseDate(result.Profile?.UpdatedAt),
                    };

                    if (snapshot.UpdatedAt == DateTimeOffset.MinValue)
                        snapshot.UpdatedAt = snapshot.ProfileUpdatedAt;

                    return snapshot;
                });
        }

        private static DateTimeOffset ParseDate(string? value)
            => DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;

        private static ulong ParseUlong(string? value)
            => ulong.TryParse(value, out var parsed) ? parsed : 0UL;
    }

    private sealed class CloudResolveResult
    {
        public bool Found { get; set; }
        public string? UserId { get; set; }
        public string? CharacterName { get; set; }
        public string? HomeWorldName { get; set; }
        public uint HomeWorldId { get; set; }
        public string? ContentId { get; set; }
        public CloudProfileDto? Profile { get; set; }
        public CloudPresenceDto? Presence { get; set; }
    }

    private sealed class CloudProfileDto
    {
        public string? UserId { get; set; }
        public string? DisplayName { get; set; }
        public string? Bio { get; set; }
        public string? StatusMessage { get; set; }
        public string? StatusColorHex { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Tag { get; set; }
        public List<PrivateVenueBookmark> Venues { get; set; } = new();
        public string? UpdatedAt { get; set; }
        public string? Status { get; set; }
        public string? StatusText { get; set; }
    }

    private sealed class CloudPresenceDto
    {
        public bool Online { get; set; }
        public string? CurrentDataCenter { get; set; }
        public string? CurrentWorld { get; set; }
        public uint CurrentWorldId { get; set; }
        public string? CurrentZone { get; set; }
        public string? ResidentialInfo { get; set; }
        public string? LastSeenAt { get; set; }
        public string? UpdatedAt { get; set; }
        public string? Status { get; set; }
        public string? StatusText { get; set; }
    }

    private sealed class AvatarUploadResponse
    {
        public string? AvatarUrl { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
        public string? Message { get; set; }
        public string? Detail { get; set; }
    }

    private readonly struct ApiResult<T>
    {
        public bool Success { get; }
        public T? Value { get; }
        public string Error { get; }

        private ApiResult(bool success, T? value, string error)
        {
            Success = success;
            Value = value;
            Error = error;
        }

        public static ApiResult<T> Ok(T? value)
            => new(true, value, string.Empty);

        public static ApiResult<T> Fail(string error)
            => new(false, default, error);
    }
}
