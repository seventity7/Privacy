using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Conditions;
using Lumina.Excel.Sheets;
using Privacy.Models;
using Privacy.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
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
    private const int StableHeartbeatSeconds = 45;
    private const int PresenceChangedHeartbeatSeconds = 1;
    private const int CloudManagedProfileResolveSeconds = 60;
    private const int UnresolvedProfileResolveSeconds = 120;
    private const string DefaultRepoJsonUrl = "https://raw.githubusercontent.com/seventity7/Privacy/main/repo.json";

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
    private readonly ICondition condition;
    private readonly FfxivVenuesService ffxivVenuesService;
    private readonly HttpClient httpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(12),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(35),
        };
    }

    private DateTime nextHeartbeat = DateTime.MinValue;
    private ContactStatus lastPresenceStatusSent = ContactStatus.Offline;
    private DateTime nextProfileResolve = DateTime.MinValue;
    private DateTime nextAutoStatusCheck = DateTime.MinValue;
    private bool syncInProgress;
    private string lastLoggedIdentitySignature = string.Empty;
    private DateTime lastLoggedIdentityAt = DateTime.MinValue;

    public PrivacyCloudService(Configuration config, IDataManager dataManager, IClientState clientState, IObjectTable objectTable, IChatGui chatGui, IPluginLog log, FfxivVenuesService ffxivVenuesService, ICondition condition)
    {
        this.config = config;
        this.dataManager = dataManager;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.chatGui = chatGui;
        this.log = log;
        this.ffxivVenuesService = ffxivVenuesService;
        this.condition = condition;
        lastPresenceStatusSent = config.CloudPresenceStatus;
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

        var now = DateTime.UtcNow;
        var currentPresenceStatus = config.CloudPresenceStatus;
        var presenceChanged = false;

        if (config.AutoStatusByActivity && now >= nextAutoStatusCheck)
        {
            nextAutoStatusCheck = now.AddMilliseconds(450);
            var detectedPresenceStatus = ResolveAutomaticPresenceStatus();
            if (config.CloudPresenceStatus != detectedPresenceStatus)
            {
                config.CloudPresenceStatus = detectedPresenceStatus;
                currentPresenceStatus = detectedPresenceStatus;
                presenceChanged = true;
                config.Save();
            }
        }

        if (syncInProgress)
            return;

        var hasUnresolvedContacts = config.Contacts.Any(contact => !contact.CloudAccountLinked);
        var shouldPollProfiles = now >= nextProfileResolve;

        if (now < nextHeartbeat && !shouldPollProfiles && !presenceChanged)
            return;

        var shouldSendHeartbeat = config.CloudHeartbeatEnabled && (now >= nextHeartbeat || presenceChanged);
        var shouldResolveProfiles = config.CloudProfileLookupEnabled && shouldPollProfiles;
        var heartbeatIdentity = shouldSendHeartbeat ? GetLocalCharacterIdentity() : null;

        if (shouldSendHeartbeat)
            nextHeartbeat = DateTime.UtcNow.AddSeconds(presenceChanged ? PresenceChangedHeartbeatSeconds : StableHeartbeatSeconds);

        if (shouldResolveProfiles)
            nextProfileResolve = DateTime.UtcNow.AddSeconds(hasUnresolvedContacts ? UnresolvedProfileResolveSeconds : CloudManagedProfileResolveSeconds);

        syncInProgress = true;
        _ = Task.Run(async () =>
        {
            try
            {
                if (shouldSendHeartbeat && heartbeatIdentity is { IsUsable: true })
                    await SendHeartbeatAsync(heartbeatIdentity, currentPresenceStatus, CancellationToken.None).ConfigureAwait(false);

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

    public ContactStatus GetCurrentPresenceStatusForRefresh()
        => config.AutoStatusByActivity ? ResolveAutomaticPresenceStatus() : config.CloudPresenceStatus;

    public async Task<string> RefreshSyncNowAsync(CloudCharacterIdentity identity, ContactStatus presenceStatus, ProfileImageCache profileImages, bool refreshOnlineContactProfiles = false, Action<string, float>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!config.CloudEnabled)
            return "Cloud sync is disabled.";

        if (!HasApiBaseUrl)
            return "Cloud API URL is not configured.";

        if (!IsLoggedIn)
            return "You need to log in first.";

        if (!identity.IsUsable)
            return "Could not identify the current character.";

        if (syncInProgress)
            return "Cloud sync is already running.";

        syncInProgress = true;
        try
        {
            progress?.Invoke("Refreshing FFXIV Venues catalog...", 0.22f);
            await ffxivVenuesService.RefreshAsync(true, cancellationToken).ConfigureAwait(false);

            progress?.Invoke("Sending status and current location...", 0.44f);
            await SendHeartbeatAsync(identity, presenceStatus, cancellationToken).ConfigureAwait(false);

            progress?.Invoke("Uploading profile settings...", 0.64f);
            await SyncOwnProfileAsync(profileImages, identity, cancellationToken).ConfigureAwait(false);

            progress?.Invoke("Resolving online profiles...", 0.78f);
            await ResolveProfilesAsync(config.Contacts, profileImages, cancellationToken).ConfigureAwait(false);

            if (refreshOnlineContactProfiles)
            {
                var onlineContacts = config.Contacts
                    .Where(contact => contact.Status != ContactStatus.Offline)
                    .ToList();

                progress?.Invoke("Refreshing all online contact data...", 0.88f);
                if (onlineContacts.Count > 0)
                    await ResolveProfilesAsync(onlineContacts, profileImages, cancellationToken).ConfigureAwait(false);
            }

            progress?.Invoke("Saving refreshed data...", 0.94f);
            config.CloudLastResolveAt = DateTimeOffset.UtcNow;
            config.Save();
            nextHeartbeat = DateTime.UtcNow.AddSeconds(StableHeartbeatSeconds);
            nextProfileResolve = DateTime.UtcNow.AddSeconds(CloudManagedProfileResolveSeconds);
            progress?.Invoke("Success!", 1f);
            return "Refresh sync completed.";
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Privacy: manual refresh sync failed.");
            return "Refresh sync failed. Check /xllog for details.";
        }
        finally
        {
            syncInProgress = false;
        }
    }

    public async Task<string> RefreshSyncNowAsync(PrivacyService listService, FriendListService friendListService, ProfileImageCache profileImages, CancellationToken cancellationToken = default)
    {
        friendListService.Refresh();
        listService.RefreshRuntimeState(friendListService.Friends);
        var identity = GetLocalCharacterIdentity();
        var presenceStatus = GetCurrentPresenceStatusForRefresh();
        return await RefreshSyncNowAsync(identity, presenceStatus, profileImages, false, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> SyncOwnProfileAsync(ProfileImageCache profileImages, CancellationToken cancellationToken = default)
    {
        if (!IsLoggedIn)
            return "You need to log in first.";

        var identity = GetLinkedOrLocalCharacterIdentity();
        return await SyncOwnProfileAsync(profileImages, identity, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> SyncOwnProfileAsync(ProfileImageCache profileImages, CloudCharacterIdentity identity, CancellationToken cancellationToken = default)
    {
        if (!IsLoggedIn)
            return "You need to log in first.";

        if (!identity.IsUsable)
            return "Could not identify the current character.";

        if (string.IsNullOrWhiteSpace(config.CloudProfileDisplayName))
            config.CloudProfileDisplayName = string.IsNullOrWhiteSpace(config.CloudDisplayName) ? identity.CharacterName : config.CloudDisplayName;

        var payload = new
        {
            character = identity,
            pluginVersion = CurrentPluginVersion,
            plugin_version = CurrentPluginVersion,
            displayName = config.CloudProfileDisplayName,
            displayNameEffect = ProfileNameEffects.Normalize(config.CloudProfileDisplayNameEffect),
            display_name_effect = ProfileNameEffects.Normalize(config.CloudProfileDisplayNameEffect),
            bio = config.CloudProfileBio,
            statusMessage = config.CloudProfileStatusMessage,
            status_message = config.CloudProfileStatusMessage,
            statusColorHex = config.CloudProfileStatusColorHex,
            status_color_hex = config.CloudProfileStatusColorHex,
            avatarUrl = config.CloudProfileAvatarUrl,
            avatar_url = config.CloudProfileAvatarUrl,
            themeName = ProfileVisuals.NormalizeThemeName(config.CloudProfileThemeName),
            theme_name = ProfileVisuals.NormalizeThemeName(config.CloudProfileThemeName),
            themeColorHex = config.CloudProfileThemeColorHex,
            theme_color_hex = config.CloudProfileThemeColorHex,
            bannerUrl = config.CloudProfileBannerUrl,
            banner_url = config.CloudProfileBannerUrl,
            avatarBorderColorHex = config.CloudProfileAvatarBorderColorHex,
            avatar_border_color_hex = config.CloudProfileAvatarBorderColorHex,
            avatarPlaceholderStyle = config.CloudProfileAvatarPlaceholderStyle,
            avatar_placeholder_style = config.CloudProfileAvatarPlaceholderStyle,
            permissions = BuildProfilePermissions(),
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

    public async Task<string> SaveCloudProfileAsync(string displayName, string displayNameEffect, string bio, string statusMessage, string statusColorHex, string avatarUrl, string themeName, string themeColorHex, string bannerUrl, string avatarBorderColorHex, string avatarPlaceholderStyle, CancellationToken cancellationToken = default)
    {
        if (!IsLoggedIn)
            return "You need to log in first.";

        var identity = GetLinkedOrLocalCharacterIdentity();
        if (!identity.IsUsable)
            return "Could not identify the current character.";

        var payload = new
        {
            character = identity,
            pluginVersion = CurrentPluginVersion,
            plugin_version = CurrentPluginVersion,
            displayName,
            display_name = displayName,
            displayNameEffect = ProfileNameEffects.Normalize(displayNameEffect),
            display_name_effect = ProfileNameEffects.Normalize(displayNameEffect),
            bio = bio.Trim().Length > 120 ? bio.Trim()[..120] : bio.Trim(),
            statusMessage = statusMessage.Trim().Length > 60 ? statusMessage.Trim()[..60] : statusMessage.Trim(),
            status_message = statusMessage.Trim().Length > 60 ? statusMessage.Trim()[..60] : statusMessage.Trim(),
            statusColorHex = NormalizeHex(statusColorHex, "#2BE5B5"),
            status_color_hex = NormalizeHex(statusColorHex, "#2BE5B5"),
            avatarUrl,
            avatar_url = avatarUrl,
            themeName = ProfileVisuals.NormalizeThemeName(themeName),
            theme_name = ProfileVisuals.NormalizeThemeName(themeName),
            themeColorHex = NormalizeHex(themeColorHex, "#2BE5B5"),
            theme_color_hex = NormalizeHex(themeColorHex, "#2BE5B5"),
            bannerUrl,
            banner_url = bannerUrl,
            avatarBorderColorHex = NormalizeHex(avatarBorderColorHex, "#2BE5B5"),
            avatar_border_color_hex = NormalizeHex(avatarBorderColorHex, "#2BE5B5"),
            avatarPlaceholderStyle,
            avatar_placeholder_style = avatarPlaceholderStyle,
            permissions = BuildProfilePermissions(),
            tag = string.Empty,
            venues = GetCloudProfileVenues(),
        };

        var response = await PostAsync<ProfileSaveResponse>("profile/me", payload, cancellationToken).ConfigureAwait(false);
        if (!response.Success)
            return response.Error;

        config.CloudProfileDisplayName = displayName.Trim();
        config.CloudProfileDisplayNameEffect = ProfileNameEffects.Normalize(displayNameEffect);
        config.CloudProfileBio = bio.Trim();
        config.CloudProfileStatusMessage = statusMessage.Trim();
        config.CloudProfileStatusColorHex = NormalizeHex(statusColorHex, "#2BE5B5");
        config.CloudProfileAvatarUrl = avatarUrl.Trim();
        config.CloudProfileThemeName = ProfileVisuals.NormalizeThemeName(themeName);
        config.CloudProfileThemeColorHex = NormalizeHex(themeColorHex, "#2BE5B5");
        config.CloudProfileBannerUrl = bannerUrl.Trim();
        config.CloudProfileAvatarBorderColorHex = NormalizeHex(avatarBorderColorHex, "#2BE5B5");
        config.CloudProfileAvatarPlaceholderStyle = string.IsNullOrWhiteSpace(avatarPlaceholderStyle) ? "Question Mark" : avatarPlaceholderStyle.Trim();
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

        var presenceStatus = config.AutoStatusByActivity ? ResolveAutomaticPresenceStatus() : config.CloudPresenceStatus;
        await SendHeartbeatAsync(identity, presenceStatus, cancellationToken).ConfigureAwait(false);
    }


    public async Task NotifyPluginUpdateAvailableAsync(CloudCharacterIdentity identity, ContactStatus presenceStatus, string latestPluginVersion, CancellationToken cancellationToken = default)
    {
        if (!config.CloudEnabled || !HasApiBaseUrl || !IsLoggedIn || !identity.IsUsable)
            return;

        await SendHeartbeatAsync(identity, presenceStatus, cancellationToken, latestPluginVersion).ConfigureAwait(false);
    }

    private async Task SendHeartbeatAsync(CloudCharacterIdentity identity, ContactStatus presenceStatus, CancellationToken cancellationToken = default, string? detectedLatestPluginVersion = null)
    {
        if (!identity.IsUsable)
            return;

        var response = await PostAsync<object>("presence/heartbeat", new
        {
            character = identity,
            pluginVersion = CurrentPluginVersion,
            plugin_version = CurrentPluginVersion,
            detectedLatestPluginVersion = string.IsNullOrWhiteSpace(detectedLatestPluginVersion) ? null : detectedLatestPluginVersion,
            detected_latest_plugin_version = string.IsNullOrWhiteSpace(detectedLatestPluginVersion) ? null : detectedLatestPluginVersion,
            status = NormalizePresenceStatus(presenceStatus),
            timestamp = DateTimeOffset.UtcNow,
            profile = new
            {
                displayName = string.IsNullOrWhiteSpace(config.CloudProfileDisplayName) ? config.CloudDisplayName : config.CloudProfileDisplayName,
                display_name = string.IsNullOrWhiteSpace(config.CloudProfileDisplayName) ? config.CloudDisplayName : config.CloudProfileDisplayName,
                displayNameEffect = ProfileNameEffects.Normalize(config.CloudProfileDisplayNameEffect),
                display_name_effect = ProfileNameEffects.Normalize(config.CloudProfileDisplayNameEffect),
                bio = config.CloudProfileBio,
                statusMessage = config.CloudProfileStatusMessage,
                status_message = config.CloudProfileStatusMessage,
                statusColorHex = config.CloudProfileStatusColorHex,
                status_color_hex = config.CloudProfileStatusColorHex,
                avatarUrl = config.CloudProfileAvatarUrl,
                avatar_url = config.CloudProfileAvatarUrl,
                themeName = ProfileVisuals.NormalizeThemeName(config.CloudProfileThemeName),
                theme_name = ProfileVisuals.NormalizeThemeName(config.CloudProfileThemeName),
                themeColorHex = config.CloudProfileThemeColorHex,
                theme_color_hex = config.CloudProfileThemeColorHex,
                bannerUrl = config.CloudProfileBannerUrl,
                banner_url = config.CloudProfileBannerUrl,
                avatarBorderColorHex = config.CloudProfileAvatarBorderColorHex,
                avatar_border_color_hex = config.CloudProfileAvatarBorderColorHex,
                avatarPlaceholderStyle = config.CloudProfileAvatarPlaceholderStyle,
                avatar_placeholder_style = config.CloudProfileAvatarPlaceholderStyle,
                permissions = BuildProfilePermissions(),
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
            lastPresenceStatusSent = presenceStatus;
            config.Save();
        }
    }

    public async Task ResolveProfilesAsync(IReadOnlyList<PrivateContact> contacts, ProfileImageCache profileImages, CancellationToken cancellationToken = default)
    {
        var profiles = await FetchProfileSnapshotsAsync(contacts, cancellationToken).ConfigureAwait(false);
        if (profiles.Count == 0)
            return;

        foreach (var profile in profiles)
        {
            var contact = FindMatchingContact(contacts, profile);
            if (contact == null)
                continue;

            await ApplyCloudProfileAsync(contact, profile, profileImages, cancellationToken).ConfigureAwait(false);
        }

        config.CloudLastResolveAt = DateTimeOffset.UtcNow;
    }

    public async Task<CloudProfileSnapshot?> FetchProfileSnapshotAsync(PrivateContact contact, CancellationToken cancellationToken = default)
    {
        var results = await FetchProfileSnapshotsAsync(new[] { contact }, cancellationToken).ConfigureAwait(false);
        return results.FirstOrDefault();
    }

    public async Task<bool> ResolveAndApplyProfileAsync(PrivateContact contact, ProfileImageCache profileImages, CancellationToken cancellationToken = default)
    {
        var profile = await FetchProfileSnapshotAsync(contact, cancellationToken).ConfigureAwait(false);
        if (profile == null)
        {
            log.Information("Privacy: no cloud profile found for {Name}@{World}.", contact.Name, contact.World);
            return false;
        }

        await ApplyCloudProfileAsync(contact, profile, profileImages, cancellationToken).ConfigureAwait(false);
        config.Save();
        log.Information("Privacy: cloud profile resolved for {Name}@{World}; profileId={ProfileId}.", contact.Name, contact.World, contact.CloudProfileId);
        return true;
    }

    public async Task<List<CloudProfileSnapshot>> FetchProfileSnapshotsAsync(IReadOnlyList<PrivateContact> contacts, CancellationToken cancellationToken = default)
    {
        if (contacts.Count == 0)
            return new List<CloudProfileSnapshot>();

        var candidates = contacts
            .SelectMany(BuildResolveCandidates)
            .ToList();

        if (candidates.Count == 0)
            return new List<CloudProfileSnapshot>();

        log.Information("Privacy: resolving {ContactCount} contact(s) through cloud using {CandidateCount} lookup candidate(s).", contacts.Count, candidates.Count);

        var response = await PostAsync<ResolveProfilesResponse>("profiles/resolve", new
        {
            contacts = candidates,
            force = true,
            bypassCache = true,
        }, cancellationToken).ConfigureAwait(false);

        if (!response.Success || response.Value == null)
            return new List<CloudProfileSnapshot>();

        return response.Value.GetProfiles().ToList();
    }

    private IEnumerable<object> BuildResolveCandidates(PrivateContact contact)
    {
        if (!HasResolveIdentity(contact))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in BuildResolveCandidatesForWorld(contact, contact.World, contact.WorldId, contact.DataCenter))
        {
            if (seen.Add(BuildResolveCandidateKey(candidate)))
                yield return candidate;
        }

        if (!string.IsNullOrWhiteSpace(contact.CurrentWorld) || contact.CurrentWorldId != 0)
        {
            foreach (var candidate in BuildResolveCandidatesForWorld(contact, contact.CurrentWorld, contact.CurrentWorldId, contact.CurrentDataCenter))
            {
                if (seen.Add(BuildResolveCandidateKey(candidate)))
                    yield return candidate;
            }
        }
    }

    private static bool HasResolveIdentity(PrivateContact contact)
        => !string.IsNullOrWhiteSpace(contact.CloudProfileId) ||
           (!string.IsNullOrWhiteSpace(contact.Name) &&
            (!string.IsNullOrWhiteSpace(contact.World) ||
             !string.IsNullOrWhiteSpace(contact.CurrentWorld) ||
             contact.WorldId != 0 ||
             contact.CurrentWorldId != 0 ||
             contact.ContentId != 0));

    private IEnumerable<object> BuildResolveCandidatesForWorld(PrivateContact contact, string world, uint worldId, string dataCenter)
    {
        var cleanWorld = (world ?? string.Empty).Trim();
        var cleanName = (contact.Name ?? string.Empty).Trim();
        var cleanDataCenter = (dataCenter ?? string.Empty).Trim();
        var resolvedWorldId = worldId != 0 ? worldId : ResolveWorldIdForCloudLookup(cleanWorld);
        var cleanCurrentWorld = (contact.CurrentWorld ?? string.Empty).Trim();
        var resolvedCurrentWorldId = contact.CurrentWorldId != 0
            ? contact.CurrentWorldId
            : ResolveWorldIdForCloudLookup(cleanCurrentWorld);

        if (string.IsNullOrWhiteSpace(contact.CloudProfileId) &&
            string.IsNullOrWhiteSpace(cleanName) &&
            string.IsNullOrWhiteSpace(cleanWorld) &&
            resolvedWorldId == 0 &&
            contact.ContentId == 0)
            yield break;

        yield return new
        {
            localId = contact.Id,
            profileId = contact.CloudProfileId,
            profile_id = contact.CloudProfileId,
            userId = contact.CloudProfileId,
            user_id = contact.CloudProfileId,
            name = cleanName,
            characterName = cleanName,
            character_name = cleanName,
            world = cleanWorld,
            worldName = cleanWorld,
            world_name = cleanWorld,
            homeWorld = cleanWorld,
            homeWorldName = cleanWorld,
            home_world = cleanWorld,
            home_world_name = cleanWorld,
            worldId = resolvedWorldId,
            world_id = resolvedWorldId,
            homeWorldId = resolvedWorldId,
            home_world_id = resolvedWorldId,
            currentWorld = cleanCurrentWorld,
            currentWorldName = cleanCurrentWorld,
            current_world = cleanCurrentWorld,
            current_world_name = cleanCurrentWorld,
            currentWorldId = resolvedCurrentWorldId,
            current_world_id = resolvedCurrentWorldId,
            dataCenter = cleanDataCenter,
            data_center = cleanDataCenter,
            contentId = contact.ContentId,
            content_id = contact.ContentId,
        };
    }


    private uint ResolveWorldIdForCloudLookup(string worldName)
    {
        if (string.IsNullOrWhiteSpace(worldName))
            return 0u;

        try
        {
            var cleanWorldName = worldName.Trim();
            foreach (var world in dataManager.GetExcelSheet<World>())
            {
                if (string.Equals(world.Name.ToString(), cleanWorldName, StringComparison.OrdinalIgnoreCase))
                    return world.RowId;
            }
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Privacy: failed to resolve world id for cloud lookup world {World}.", worldName);
        }

        return 0u;
    }

    private static string BuildResolveCandidateKey(object candidate)
        => JsonSerializer.Serialize(candidate, JsonOptions);

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
            identity.ResidentialDetails = location.ResidentialDetails ?? string.Empty;

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
        contact.CloudDisplayNameEffect = ProfileNameEffects.Normalize(profile.DisplayNameEffect);
        contact.CloudAvatarUrl = profile.AvatarUrl;
        contact.CloudStatusMessage = profile.StatusMessage;
        contact.CloudStatusColorHex = NormalizeHex(profile.StatusColorHex, "#2BE5B5");
        contact.CloudBio = profile.Bio;
        contact.CloudThemeName = ProfileVisuals.NormalizeThemeName(profile.ThemeName);
        contact.CloudThemeColorHex = NormalizeHex(profile.ThemeColorHex, "#2BE5B5");
        contact.CloudBannerUrl = profile.BannerUrl;
        contact.CloudAvatarBorderColorHex = NormalizeHex(profile.AvatarBorderColorHex, "#2BE5B5");
        contact.CloudAvatarPlaceholderStyle = string.IsNullOrWhiteSpace(profile.AvatarPlaceholderStyle) ? "Question Mark" : profile.AvatarPlaceholderStyle;
        contact.CloudStatusVisibility = string.IsNullOrWhiteSpace(profile.StatusVisibility) ? "All" : profile.StatusVisibility;
        contact.CloudVenuesVisibility = string.IsNullOrWhiteSpace(profile.VenuesVisibility) ? "All" : profile.VenuesVisibility;
        contact.CloudLocationVisibility = string.IsNullOrWhiteSpace(profile.LocationVisibility) ? "All" : profile.LocationVisibility;
        contact.CloudBioVisibility = string.IsNullOrWhiteSpace(profile.BioVisibility) ? "All" : profile.BioVisibility;
        contact.CloudVenues = profile.Venues ?? new List<PrivateVenueBookmark>();
        contact.PluginVersion = profile.PluginVersion;
        contact.OutdatedPluginVersion = profile.OutdatedPluginVersion;
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
        var previousZone = contact.LastKnownZone;
        if (!string.IsNullOrWhiteSpace(profile.CurrentZone))
        {
            contact.LastKnownZone = profile.CurrentZone;
            if (!string.Equals(NormalizeLocationForComparison(previousZone), NormalizeLocationForComparison(profile.CurrentZone), StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(profile.ResidentialDetails))
                contact.ResidentialDetails = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(profile.ResidentialDetails))
            contact.ResidentialDetails = profile.ResidentialDetails;
        else if (!string.IsNullOrWhiteSpace(profile.CurrentZone) && !LooksLikeResidentialLocation(profile.CurrentZone))
            contact.ResidentialDetails = string.Empty;

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


    private static string NormalizeLocationForComparison(string value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");

    private static bool LooksLikeResidentialLocation(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("Mist", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Lavender", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Lavander", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Goblet", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Shirogane", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Empyreum", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Private Mansion", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Private Cottage", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Private House", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Apartment", StringComparison.OrdinalIgnoreCase);
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
            MatchesAnyKnownWorld(c, profile));
    }

    private static bool MatchesAnyKnownWorld(PrivateContact contact, CloudProfileSnapshot profile)
    {
        if (profile.HomeWorldId != 0)
        {
            if (contact.WorldId != 0 && contact.WorldId == profile.HomeWorldId)
                return true;

            if (contact.CurrentWorldId != 0 && contact.CurrentWorldId == profile.HomeWorldId)
                return true;
        }

        if (!string.IsNullOrWhiteSpace(profile.HomeWorld))
        {
            if (string.Equals(contact.World, profile.HomeWorld, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(contact.CurrentWorld, profile.HomeWorld, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return profile.HomeWorldId == 0 && string.IsNullOrWhiteSpace(profile.HomeWorld);
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
        config.CloudProfileDisplayNameEffect = ProfileNameEffects.Normalize(response.Profile?.DisplayNameEffect ?? config.CloudProfileDisplayNameEffect);
        config.CloudProfileBio = response.Profile?.Bio ?? config.CloudProfileBio;
        config.CloudProfileStatusMessage = response.Profile?.StatusMessage ?? config.CloudProfileStatusMessage;
        config.CloudProfileStatusColorHex = NormalizeHex(response.Profile?.StatusColorHex, config.CloudProfileStatusColorHex);
        config.CloudProfileAvatarUrl = response.Profile?.AvatarUrl ?? config.CloudProfileAvatarUrl;
        config.CloudProfileThemeName = ProfileVisuals.NormalizeThemeName(response.Profile?.ThemeName ?? config.CloudProfileThemeName);
        config.CloudProfileThemeColorHex = NormalizeHex(response.Profile?.ThemeColorHex, config.CloudProfileThemeColorHex);
        config.CloudProfileBannerUrl = response.Profile?.BannerUrl ?? config.CloudProfileBannerUrl;
        config.CloudProfileAvatarBorderColorHex = NormalizeHex(response.Profile?.AvatarBorderColorHex, config.CloudProfileAvatarBorderColorHex);
        config.CloudProfileAvatarPlaceholderStyle = response.Profile?.AvatarPlaceholderStyle ?? config.CloudProfileAvatarPlaceholderStyle;
        ApplyProfilePermissions(response.Profile?.Permissions);
        config.CloudProfileTag = response.Profile?.Tag ?? config.CloudProfileTag;
        MergeCloudProfileVenues(response.Profile?.Venues);
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
        config.CloudProfileDisplayNameEffect = ProfileNameEffects.Normalize(response.Profile.DisplayNameEffect ?? config.CloudProfileDisplayNameEffect);
        config.CloudProfileBio = response.Profile.Bio ?? config.CloudProfileBio;
        config.CloudProfileStatusMessage = response.Profile.StatusMessage ?? config.CloudProfileStatusMessage;
        config.CloudProfileStatusColorHex = NormalizeHex(response.Profile.StatusColorHex, config.CloudProfileStatusColorHex);
        config.CloudProfileAvatarUrl = response.Profile.AvatarUrl ?? config.CloudProfileAvatarUrl;
        config.CloudProfileThemeName = ProfileVisuals.NormalizeThemeName(response.Profile.ThemeName ?? config.CloudProfileThemeName);
        config.CloudProfileThemeColorHex = NormalizeHex(response.Profile.ThemeColorHex, config.CloudProfileThemeColorHex);
        config.CloudProfileBannerUrl = response.Profile.BannerUrl ?? config.CloudProfileBannerUrl;
        config.CloudProfileAvatarBorderColorHex = NormalizeHex(response.Profile.AvatarBorderColorHex, config.CloudProfileAvatarBorderColorHex);
        config.CloudProfileAvatarPlaceholderStyle = response.Profile.AvatarPlaceholderStyle ?? config.CloudProfileAvatarPlaceholderStyle;
        ApplyProfilePermissions(response.Profile.Permissions);
        config.CloudProfileTag = response.Profile.Tag ?? config.CloudProfileTag;
        MergeCloudProfileVenues(response.Profile.Venues);
    }


    private void MergeCloudProfileVenues(IReadOnlyList<PrivateVenueBookmark>? remoteVenues)
    {
        if (remoteVenues == null)
            return;

        // The cloud profile returns only venues that are public on the profile.
        // Keep locally saved venue-location entries too, otherwise the My Profile > Venues
        // list loses manual/local venues as soon as a profile save/heartbeat response arrives.
        if (remoteVenues.Count == 0)
            return;

        var existingLocal = config.CloudSavedVenues ?? new List<PrivateVenueBookmark>();
        var merged = new List<PrivateVenueBookmark>();
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var remote in remoteVenues.Where(v => v != null))
        {
            var key = GetVenueMergeKey(remote);
            var local = existingLocal.FirstOrDefault(v => string.Equals(GetVenueMergeKey(v), key, StringComparison.OrdinalIgnoreCase));

            var copy = new PrivateVenueBookmark
            {
                Name = remote.Name,
                DataCenter = remote.DataCenter,
                World = remote.World,
                District = remote.District,
                Ward = remote.Ward,
                Plot = remote.Plot,
                Address = remote.Address,
                ImageUrl = remote.ImageUrl,
                ImageLocalPath = !string.IsNullOrWhiteSpace(local?.ImageLocalPath) ? local.ImageLocalPath : remote.ImageLocalPath,
                WebsiteUrl = remote.WebsiteUrl,
                DiscordUrl = remote.DiscordUrl,
                TeleportCommand = remote.TeleportCommand,
                Favorite = true,
                TooltipTag = remote.TooltipTag,
                TooltipTagColorHex = NormalizeHex(remote.TooltipTagColorHex, "#B56CFF"),
                Source = string.IsNullOrWhiteSpace(remote.Source) ? local?.Source ?? "FFXIVVenues" : remote.Source,
            };

            merged.Add(copy);
            usedKeys.Add(key);
        }

        foreach (var local in existingLocal)
        {
            var key = GetVenueMergeKey(local);
            if (usedKeys.Contains(key))
                continue;

            merged.Add(local);
            usedKeys.Add(key);
        }

        config.CloudSavedVenues = merged;
    }

    private static string GetVenueMergeKey(PrivateVenueBookmark venue)
    {
        var address = venue.BuildAddress();
        if (!string.IsNullOrWhiteSpace(address))
            return NormalizeVenueAddress(address);

        if (!string.IsNullOrWhiteSpace(venue.Address))
            return NormalizeVenueAddress(venue.Address);

        return NormalizeVenueAddress($"{venue.DataCenter}|{venue.World}|{venue.District}|{venue.Ward}|{venue.Plot}|{venue.Name}");
    }

    private object BuildProfilePermissions()
        => new
        {
            status = Privacy.UI.ProfileVisuals.NormalizeVisibility(config.ProfileStatusVisibility, "All"),
            venues = Privacy.UI.ProfileVisuals.NormalizeVisibility(config.ProfileVenuesVisibility, "All"),
            location = Privacy.UI.ProfileVisuals.NormalizeVisibility(config.ProfileLocationVisibility, "All"),
            bio = Privacy.UI.ProfileVisuals.NormalizeVisibility(config.ProfileBioVisibility, "All"),
        };

    private void ApplyProfilePermissions(CloudProfilePermissionsDto? permissions)
    {
        if (permissions == null)
            return;

        config.ProfileStatusVisibility = Privacy.UI.ProfileVisuals.NormalizeVisibility(permissions.Status ?? config.ProfileStatusVisibility, config.ProfileStatusVisibility);
        config.ProfileVenuesVisibility = Privacy.UI.ProfileVisuals.NormalizeVisibility(permissions.Venues ?? config.ProfileVenuesVisibility, config.ProfileVenuesVisibility);
        config.ProfileLocationVisibility = Privacy.UI.ProfileVisuals.NormalizeVisibility(permissions.Location ?? config.ProfileLocationVisibility, config.ProfileLocationVisibility);
        config.ProfileBioVisibility = Privacy.UI.ProfileVisuals.NormalizeVisibility(permissions.Bio ?? config.ProfileBioVisibility, config.ProfileBioVisibility);
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


    public static string CurrentPluginVersion
    {
        get
        {
            var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
                return info.Split('+')[0].Trim();

            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        }
    }

    public async Task<bool> IsCurrentPluginVersionOutdatedAsync(CancellationToken cancellationToken = default)
    {
        var latest = await FetchLatestPluginVersionAsync(cancellationToken).ConfigureAwait(false);
        return IsVersionNewer(latest, CurrentPluginVersion);
    }

    private async Task<string> FetchLatestPluginVersionAsync(CancellationToken cancellationToken)
    {
        var workerVersion = await FetchLatestPluginVersionFromWorkerAsync(cancellationToken).ConfigureAwait(false);
        var repoVersion = await FetchLatestPluginVersionFromRepoJsonAsync(cancellationToken).ConfigureAwait(false);

        return PickNewestVersion(workerVersion, repoVersion);
    }

    private async Task<string> FetchLatestPluginVersionFromWorkerAsync(CancellationToken cancellationToken)
    {
        if (!TryGetApiBaseUri(out var apiBase))
            return string.Empty;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(apiBase, "v1/plugin/version"));
            if (!string.IsNullOrWhiteSpace(config.CloudAccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.CloudAccessToken);

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return string.Empty;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var value = JsonSerializer.Deserialize<PluginVersionResponse>(body, JsonOptions);
            return value?.LatestVersion ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<string> FetchLatestPluginVersionFromRepoJsonAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, DefaultRepoJsonUrl);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return string.Empty;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return TryReadRepoAssemblyVersion(document.RootElement, out var version) ? version : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryReadRepoAssemblyVersion(JsonElement root, out string version)
    {
        version = string.Empty;

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in root.EnumerateArray())
            {
                if (TryReadRepoAssemblyVersion(entry, out version))
                    return true;
            }

            return false;
        }

        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (root.TryGetProperty("Plugins", out var plugins) && TryReadRepoAssemblyVersion(plugins, out version))
            return true;

        var name = ReadJsonString(root, "InternalName");
        if (string.IsNullOrWhiteSpace(name))
            name = ReadJsonString(root, "Name");

        if (!string.Equals(name, "Privacy", StringComparison.OrdinalIgnoreCase))
            return false;

        version = ReadJsonString(root, "AssemblyVersion");
        return !string.IsNullOrWhiteSpace(version);
    }

    private static string ReadJsonString(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string PickNewestVersion(params string[] versions)
    {
        var newest = string.Empty;

        foreach (var version in versions)
        {
            if (string.IsNullOrWhiteSpace(version))
                continue;

            if (string.IsNullOrWhiteSpace(newest) || IsVersionNewer(version, newest))
                newest = version.Trim();
        }

        return newest;
    }

    private static bool IsVersionNewer(string latest, string current)
    {
        if (string.IsNullOrWhiteSpace(latest) || string.IsNullOrWhiteSpace(current))
            return false;

        if (Version.TryParse(TrimVersion(latest), out var latestVersion) && Version.TryParse(TrimVersion(current), out var currentVersion))
            return latestVersion > currentVersion;

        return string.Compare(latest.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static string TrimVersion(string value)
    {
        var text = (value ?? string.Empty).Trim();
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
            text = text[..dash];
        return text;
    }

    private async Task<ApiResult<T>> PostAsync<T>(string path, object payload, CancellationToken cancellationToken)
    {
        if (!TryGetApiBaseUri(out var apiBase))
            return ApiResult<T>.Fail("Cloud API URL is not configured.");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(apiBase, $"v1/{path.TrimStart('/')}"));
            request.Content = JsonContent.Create(payload, options: JsonOptions);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            if (IsPresenceHeartbeatPath(path))
                request.Headers.ConnectionClose = true;
            if (!string.IsNullOrWhiteSpace(config.CloudAccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.CloudAccessToken);

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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

    private static bool IsPresenceHeartbeatPath(string path)
        => string.Equals(path.Trim('/'), "presence/heartbeat", StringComparison.OrdinalIgnoreCase);

    private bool TryGetApiBaseUri(out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(config.CloudApiBaseUrl) && !string.IsNullOrWhiteSpace(BuildSettings.CloudApiBaseUrl))
            config.CloudApiBaseUrl = BuildSettings.CloudApiBaseUrl;

        var value = config.CloudApiBaseUrl.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;
        if (!value.EndsWith("/", StringComparison.Ordinal))
            value += "/";

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
            return false;

        uri = parsed;
        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }



    private List<PrivateVenueBookmark> GetCloudProfileVenues()
    {
        ffxivVenuesService.EnsureFreshAsync();

        var selected = config.CloudSavedVenues
            .Where(v => v.Favorite && !string.IsNullOrWhiteSpace(v.Name))
            .ToList();

        if (selected.Count == 0)
        {
            selected = config.CloudSavedVenues
                .Where(v => !string.IsNullOrWhiteSpace(v.Name))
                .ToList();
        }

        return selected
            .Select(BuildCloudVenueBookmark)
            .GroupBy(v => NormalizeVenueAddress(v.BuildAddress()), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(24)
            .ToList();
    }

    private PrivateVenueBookmark BuildCloudVenueBookmark(PrivateVenueBookmark source)
    {
        var venue = new PrivateVenueBookmark
        {
            Name = source.Name,
            DataCenter = source.DataCenter,
            World = source.World,
            District = source.District,
            Ward = source.Ward,
            Plot = source.Plot,
            Address = source.Address,
            ImageUrl = source.ImageUrl,
            WebsiteUrl = source.WebsiteUrl,
            DiscordUrl = source.DiscordUrl,
            TeleportCommand = source.TeleportCommand,
            Favorite = true,
            TooltipTag = source.TooltipTag,
            TooltipTagColorHex = NormalizeHex(source.TooltipTagColorHex, "#B56CFF"),
            Source = source.Source,
        };

        var catalog = ffxivVenuesService.FindBestMatch(venue.Name, venue.BuildAddress())
            ?? ffxivVenuesService.FindByAddress(venue.DataCenter, venue.World, venue.District, venue.Ward, venue.Plot);

        if (catalog != null)
        {
            venue.Name = string.IsNullOrWhiteSpace(catalog.Name) ? venue.Name : catalog.Name;
            venue.DataCenter = string.IsNullOrWhiteSpace(catalog.DataCenter) ? venue.DataCenter : catalog.DataCenter;
            venue.World = string.IsNullOrWhiteSpace(catalog.World) ? venue.World : catalog.World;
            venue.District = string.IsNullOrWhiteSpace(catalog.District) ? venue.District : catalog.District;
            venue.Ward = catalog.Ward > 0 ? catalog.Ward : venue.Ward;
            venue.Plot = catalog.Plot > 0 ? catalog.Plot : venue.Plot;
            venue.Address = string.IsNullOrWhiteSpace(catalog.BuildFullLocation()) ? venue.Address : catalog.BuildFullLocation();
            venue.ImageUrl = string.IsNullOrWhiteSpace(catalog.ImageUrl) ? venue.ImageUrl : catalog.ImageUrl;
            venue.WebsiteUrl = string.IsNullOrWhiteSpace(catalog.WebsiteUrl) ? venue.WebsiteUrl : catalog.WebsiteUrl;
            venue.DiscordUrl = string.IsNullOrWhiteSpace(catalog.DiscordUrl) ? venue.DiscordUrl : catalog.DiscordUrl;
            venue.TeleportCommand = string.IsNullOrWhiteSpace(catalog.TeleportCommand) ? venue.TeleportCommand : catalog.TeleportCommand;
            venue.Source = "FFXIVVenues";
        }

        venue.ImageLocalPath = string.Empty;
        return venue;
    }

    private static string NormalizeVenueAddress(string value)
        => System.Text.RegularExpressions.Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");

    private ContactStatus ResolveAutomaticPresenceStatus()
    {
        if (!config.AutoStatusByActivity)
            return config.CloudPresenceStatus;

        try
        {
            if (!clientState.IsLoggedIn)
                return ContactStatus.Offline;

            if (IsInDuty())
                return ContactStatus.Content;

            var localPlayer = ResolveLocalPlayer();
            var onlineStatus = localPlayer?.GetType().GetProperty("OnlineStatus", BindingFlags.Public | BindingFlags.Instance)?.GetValue(localPlayer);
            var statusName = ReadLuminaName(onlineStatus);
            var statusId = ReadRowId(onlineStatus);
            var statusRaw = onlineStatus?.ToString() ?? string.Empty;

            if (LooksLikeAfk(statusName, statusId) || LooksLikeAfk(statusRaw, statusId))
                return ContactStatus.Afk;
            if (LooksLikeBusy(statusName, statusId) || LooksLikeBusy(statusRaw, statusId))
                return ContactStatus.Busy;
            if (LooksLikeRolePlaying(statusName, statusId) || LooksLikeRolePlaying(statusRaw, statusId))
                return ContactStatus.RolePlaying;
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "Privacy: automatic status detection unavailable on this frame.");
        }

        return ContactStatus.Online;
    }

    private bool IsInDuty()
    {
        try
        {
            foreach (var name in DutyConditionNames)
            {
                if (Enum.TryParse<ConditionFlag>(name, out var flag) && condition[flag])
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static readonly string[] DutyConditionNames =
    [
        "BoundByDuty", "BoundByDuty56", "BoundByDuty95"
    ];

    private static bool LooksLikeBusy(string value, uint rowId)
        => ContainsAny(value, "busy", "do not disturb", "dnd") || rowId is 12;

    private static bool LooksLikeAfk(string value, uint rowId)
        => ContainsAny(value, "away", "keyboard", "afk", "away from keyboard") || rowId is 17 or 18;

    private static bool LooksLikeRolePlaying(string value, uint rowId)
        => ContainsAny(value, "role-playing", "roleplaying", "role playing", "role player", "rp") || rowId is 22 or 23;

    private static bool ContainsAny(string value, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static uint ReadRowId(object? value)
    {
        if (value == null) return 0;
        try
        {
            var rowId = value.GetType().GetProperty("RowId")?.GetValue(value)
                ?? value.GetType().GetProperty("RowID")?.GetValue(value)
                ?? value.GetType().GetProperty("Id")?.GetValue(value)
                ?? value.GetType().GetProperty("ID")?.GetValue(value);
            if (rowId != null && uint.TryParse(rowId.ToString(), out var parsed))
                return parsed;

            var inner = value.GetType().GetProperty("Value")?.GetValue(value)
                ?? value.GetType().GetProperty("GameData")?.GetValue(value)
                ?? value.GetType().GetProperty("Row")?.GetValue(value);
            if (inner != null && !ReferenceEquals(inner, value))
                return ReadRowId(inner);
        }
        catch
        {
            return 0;
        }
        return 0;
    }

    private static string ReadLuminaName(object? value)
    {
        try
        {
            if (value == null) return string.Empty;
            var nameValue = value.GetType().GetProperty("Name")?.GetValue(value)
                ?? value.GetType().GetProperty("StatusName")?.GetValue(value)
                ?? value.GetType().GetProperty("Text")?.GetValue(value);
            var name = nameValue?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name) && !name.Contains(value.GetType().Name, StringComparison.OrdinalIgnoreCase))
                return name;

            var inner = value.GetType().GetProperty("Value")?.GetValue(value)
                ?? value.GetType().GetProperty("GameData")?.GetValue(value)
                ?? value.GetType().GetProperty("Row")?.GetValue(value);
            if (inner != null && !ReferenceEquals(inner, value))
            {
                var innerName = ReadLuminaName(inner);
                if (!string.IsNullOrWhiteSpace(innerName))
                    return innerName;
            }

            return value.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizePresenceStatus(ContactStatus status)
        => status switch
        {
            ContactStatus.Busy => "Busy",
            ContactStatus.Afk => "AFK",
            ContactStatus.Content => "Content",
            ContactStatus.Streaming => "Streaming",
            ContactStatus.RolePlaying => "RolePlaying",
            ContactStatus.Online => "Online",
            _ => "Online",
        };

    private static ContactStatus ResolveCloudPresenceStatus(CloudPresenceDto? presence)
    {
        if (presence?.Online != true)
            return ContactStatus.Offline;

        var value = (presence.Status ?? presence.StatusText ?? string.Empty).Trim();
        if (value.Equals("Busy", StringComparison.OrdinalIgnoreCase))
            return ContactStatus.Busy;
        if (value.Equals("AFK", StringComparison.OrdinalIgnoreCase) || value.Equals("Afk", StringComparison.OrdinalIgnoreCase))
            return ContactStatus.Afk;
        if (value.Equals("Content", StringComparison.OrdinalIgnoreCase))
            return ContactStatus.Content;
        if (value.Equals("Streaming", StringComparison.OrdinalIgnoreCase))
            return ContactStatus.Streaming;
        if (value.Equals("RolePlaying", StringComparison.OrdinalIgnoreCase) || value.Equals("Role-playing", StringComparison.OrdinalIgnoreCase))
            return ContactStatus.RolePlaying;

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
        try
        {
            if (localPlayer is IPlayerCharacter player)
            {
                var directRowId = string.Equals(propertyName, "CurrentWorld", StringComparison.OrdinalIgnoreCase)
                    ? player.CurrentWorld.RowId
                    : player.HomeWorld.RowId;
                if (directRowId != 0)
                    return directRowId;
            }
        }
        catch
        {
        }

        foreach (var name in BuildWorldPropertyCandidates(propertyName))
        {
            var world = localPlayer.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(localPlayer);
            var directValue = ReadWorldIdValue(world);
            if (directValue != 0)
                return directValue;
        }

        foreach (var name in BuildWorldIdPropertyCandidates(propertyName))
        {
            var value = localPlayer.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(localPlayer);
            var directValue = ReadWorldIdValue(value);
            if (directValue != 0)
                return directValue;
        }

        return 0u;
    }

    private static IEnumerable<string> BuildWorldPropertyCandidates(string propertyName)
    {
        yield return propertyName;
        if (string.Equals(propertyName, "CurrentWorld", StringComparison.OrdinalIgnoreCase))
        {
            yield return "World";
            yield return "CurrentWorldGameData";
        }
        else if (string.Equals(propertyName, "HomeWorld", StringComparison.OrdinalIgnoreCase))
        {
            yield return "HomeWorldGameData";
        }
    }

    private static IEnumerable<string> BuildWorldIdPropertyCandidates(string propertyName)
    {
        if (string.Equals(propertyName, "CurrentWorld", StringComparison.OrdinalIgnoreCase))
        {
            yield return "CurrentWorldId";
            yield return "CurrentWorldID";
            yield return "WorldId";
            yield return "WorldID";
        }
        else if (string.Equals(propertyName, "HomeWorld", StringComparison.OrdinalIgnoreCase))
        {
            yield return "HomeWorldId";
            yield return "HomeWorldID";
        }
    }

    private static uint ReadWorldIdValue(object? value)
    {
        if (value == null)
            return 0u;

        switch (value)
        {
            case uint u: return u;
            case ushort us: return us;
            case int i when i > 0: return (uint)i;
            case long l when l > 0: return (uint)l;
        }

        var rowId = value.GetType().GetProperty("RowId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(value);
        return rowId switch
        {
            uint u => u,
            ushort us => us,
            int i when i > 0 => (uint)i,
            long l when l > 0 => (uint)l,
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
                        DisplayNameEffect = ProfileNameEffects.Normalize(result.Profile?.DisplayNameEffect),
                        AvatarUrl = result.Profile?.AvatarUrl ?? string.Empty,
                        Bio = result.Profile?.Bio ?? string.Empty,
                        Tag = result.Profile?.Tag ?? string.Empty,
                        StatusMessage = result.Profile?.StatusMessage ?? string.Empty,
                        StatusColorHex = NormalizeHex(result.Profile?.StatusColorHex, "#2BE5B5"),
                        ThemeName = ProfileVisuals.NormalizeThemeName(result.Profile?.ThemeName),
                        ThemeColorHex = NormalizeHex(result.Profile?.ThemeColorHex, "#2BE5B5"),
                        BannerUrl = result.Profile?.BannerUrl ?? string.Empty,
                        AvatarBorderColorHex = NormalizeHex(result.Profile?.AvatarBorderColorHex, "#2BE5B5"),
                        AvatarPlaceholderStyle = result.Profile?.AvatarPlaceholderStyle ?? "Question Mark",
                        StatusVisibility = result.Profile?.Permissions?.Status ?? "All",
                        VenuesVisibility = result.Profile?.Permissions?.Venues ?? "All",
                        LocationVisibility = result.Profile?.Permissions?.Location ?? "All",
                        BioVisibility = result.Profile?.Permissions?.Bio ?? "All",
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
                        PluginVersion = result.PluginVersion ?? result.Presence?.PluginVersion ?? string.Empty,
                        OutdatedPluginVersion = result.OutdatedPluginVersion || (result.Presence?.OutdatedPluginVersion ?? false),
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
        public string? PluginVersion { get; set; }
        public bool OutdatedPluginVersion { get; set; }
        [JsonPropertyName("user_id")] public string? UserIdSnake { set => UserId = value; }
        [JsonPropertyName("profile_id")] public string? ProfileIdSnake { set => UserId = value; }
        [JsonPropertyName("character_name")] public string? CharacterNameSnake { set => CharacterName = value; }
        [JsonPropertyName("name")] public string? NameAlias { set => CharacterName = value; }
        [JsonPropertyName("home_world_name")] public string? HomeWorldNameSnake { set => HomeWorldName = value; }
        [JsonPropertyName("home_world")] public string? HomeWorldAlias { set => HomeWorldName = value; }
        [JsonPropertyName("world")] public string? WorldAlias { set => HomeWorldName = value; }
        [JsonPropertyName("world_name")] public string? WorldNameAlias { set => HomeWorldName = value; }
        [JsonPropertyName("home_world_id")] public uint HomeWorldIdSnake { set => HomeWorldId = value; }
        [JsonPropertyName("world_id")] public uint WorldIdAlias { set => HomeWorldId = value; }
        [JsonPropertyName("content_id")] public string? ContentIdSnake { set => ContentId = value; }
        [JsonPropertyName("plugin_version")] public string? PluginVersionSnake { set => PluginVersion = value; }
        [JsonPropertyName("outdated_plugin_version")] public bool OutdatedPluginVersionSnake { set => OutdatedPluginVersion = value; }
    }

    private sealed class CloudProfileDto
    {
        public string? UserId { get; set; }
        public string? DisplayName { get; set; }
        public string? DisplayNameEffect { get; set; }
        public string? Bio { get; set; }
        public string? StatusMessage { get; set; }
        public string? StatusColorHex { get; set; }
        public string? ThemeName { get; set; }
        public string? ThemeColorHex { get; set; }
        public string? BannerUrl { get; set; }
        public string? AvatarBorderColorHex { get; set; }
        public string? AvatarPlaceholderStyle { get; set; }
        public CloudProfilePermissionsDto? Permissions { get; set; }
        public string? AvatarUrl { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayNameSnake { set => DisplayName = value; }
        [JsonPropertyName("display_name_effect")]
        public string? DisplayNameEffectSnake { set => DisplayNameEffect = value; }
        [JsonPropertyName("status_message")]
        public string? StatusMessageSnake { set => StatusMessage = value; }
        [JsonPropertyName("status_color_hex")]
        public string? StatusColorHexSnake { set => StatusColorHex = value; }
        [JsonPropertyName("avatar_url")]
        public string? AvatarUrlSnake { set => AvatarUrl = value; }
        [JsonPropertyName("theme_name")]
        public string? ThemeNameSnake { set => ThemeName = value; }
        [JsonPropertyName("theme_color_hex")]
        public string? ThemeColorHexSnake { set => ThemeColorHex = value; }
        [JsonPropertyName("banner_url")]
        public string? BannerUrlSnake { set => BannerUrl = value; }
        [JsonPropertyName("avatar_border_color_hex")]
        public string? AvatarBorderColorHexSnake { set => AvatarBorderColorHex = value; }
        [JsonPropertyName("avatar_placeholder_style")]
        public string? AvatarPlaceholderStyleSnake { set => AvatarPlaceholderStyle = value; }
        public string? Tag { get; set; }
        public List<PrivateVenueBookmark> Venues { get; set; } = new();
        public string? UpdatedAt { get; set; }
        public string? Status { get; set; }
        public string? StatusText { get; set; }
        public string? PluginVersion { get; set; }
        public bool OutdatedPluginVersion { get; set; }
        [JsonPropertyName("plugin_version")] public string? PluginVersionSnake { set => PluginVersion = value; }
        [JsonPropertyName("outdated_plugin_version")] public bool OutdatedPluginVersionSnake { set => OutdatedPluginVersion = value; }
    }

    private sealed class CloudProfilePermissionsDto
    {
        public string? Status { get; set; }
        public string? Venues { get; set; }
        public string? Location { get; set; }
        public string? Bio { get; set; }
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
        public string? PluginVersion { get; set; }
        public bool OutdatedPluginVersion { get; set; }
        [JsonPropertyName("plugin_version")] public string? PluginVersionSnake { set => PluginVersion = value; }
        [JsonPropertyName("outdated_plugin_version")] public bool OutdatedPluginVersionSnake { set => OutdatedPluginVersion = value; }
    }

    private sealed class AvatarUploadResponse
    {
        public string? AvatarUrl { get; set; }
    }

    private sealed class PluginVersionResponse
    {
        public string? LatestVersion { get; set; }
        [JsonPropertyName("latest_version")] public string? LatestVersionSnake { set => LatestVersion = value; }
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
