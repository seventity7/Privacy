using Dalamud.Configuration;
using Dalamud.Plugin;
using Privacy.Models;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Privacy;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public int Version { get; set; } = 1;
    public List<PrivateContact> Contacts = new();
    public List<PrivateContact> Venues = new();
    public List<PrivateGroup> Groups = new();
    public List<PrivacyEvent> History = new();
    public List<PrivateVenueBookmark> CloudSavedVenues = new();
    public bool ShowFavoriteVenuesOnProfile = true;
    public string FavoriteVenueTooltipTag = string.Empty;
    public string FavoriteVenueTooltipTagColorHex = "#B56CFF";

    public bool EnableContextMenu = true;
    public bool OpenWindowAfterAdd = true;
    public bool FavoritesFirst = true;
    public bool ShowOfflineContacts = true;
    public bool ShowSidebar = true;
    public bool KeepLastKnownLocationWhenOffline = true;
    public bool NotifyOnlineCountOnLogin = true;
    public bool NotifyFavoriteContacts = false;
    public bool NotifyOnlyFavorites = false;
    public bool MinimalMode = false;
    public bool WindowCollapsed = false;
    public bool HideTopBar = false;
    public bool HideUserRowBackground = false;
    public bool HideVenuesRowBackground = false;
    public bool HideDivisors = false;
    public bool HideVenuesDivisor = false;
    public bool CloudEnabled = true;
    public bool CloudAutoSync = true;
    public bool CloudHeartbeatEnabled = true;
    public bool CloudProfileLookupEnabled = true;
    public string CloudApiBaseUrl = string.Empty;
    public string CloudAccessToken = string.Empty;
    public string CloudRefreshToken = string.Empty;
    public string CloudUserId = string.Empty;
    public string CloudDisplayName = string.Empty;
    public string CloudProfileDisplayName = string.Empty;
    public string CloudProfileBio = string.Empty;
    public string CloudProfileStatusMessage = string.Empty;
    public string CloudProfileStatusColorHex = "#2BE5B5";
    public string CloudProfileAvatarUrl = string.Empty;
    public string CloudProfileAvatarLocalPath = string.Empty;
    public string CloudProfileTag = string.Empty;
    public ContactStatus CloudPresenceStatus = ContactStatus.Online;
    public string CloudAccountProvider = string.Empty;
    public string CloudLinkedCharacterName = string.Empty;
    public string CloudLinkedWorld = string.Empty;
    public uint CloudLinkedWorldId;
    public ulong CloudLinkedContentId;
    public DateTimeOffset CloudTokenExpiresAt = DateTimeOffset.MinValue;
    public DateTimeOffset CloudLastHeartbeatAt = DateTimeOffset.MinValue;
    public DateTimeOffset CloudLastProfileSyncAt = DateTimeOffset.MinValue;
    public DateTimeOffset CloudLastResolveAt = DateTimeOffset.MinValue;
    public bool HighlightSameZone = true;
    public bool HighlightSameWorld = true;
    public int MaxHistoryEntries = 250;
    public int MaxContacts = 50;
    public int ActiveView = 0;
    public int DiscoverFilterMode = 0;

    public string TravelCommandTemplate = "/li {World}";
    public string ThemePresetName = "Default";
    public string SearchText = string.Empty;
    public string ContactsSearchText = string.Empty;
    public string FavoritesSearchText = string.Empty;
    public string NotebookSearchText = string.Empty;
    public string VenuesSearchText = string.Empty;
    public string DiscoverSearchText = string.Empty;
    public string GroupsSearchText = string.Empty;

    public Vector4 AccentColor = new(0.16f, 0.86f, 0.67f, 1f);
    public Vector4 SidebarColor = new(0.08f, 0.55f, 0.43f, 1f);
    public Vector4 WindowBackgroundColor = new(0.010f, 0.020f, 0.018f, 0.88f);
    public Vector4 TopBarBackgroundColor = new(0.188f, 0.200f, 0.192f, 0.34f);
    public Vector4 BottomBarBackgroundColor = new(0.188f, 0.200f, 0.192f, 0.34f);
    public Vector4 UserRowBackgroundColor = new(0.067f, 0.110f, 0.098f, 0.66f);
    public string CustomMainBackgroundImagePath = string.Empty;
    public bool UseCustomMainBackgroundImage = false;
    public string CustomBackgroundEffectName = string.Empty;
    public Dictionary<string, string> CustomBackgroundEffectColorHex = new();

    public void Initialize(IDalamudPluginInterface pluginInterface)
        => this.pluginInterface = pluginInterface;

    public void Save()
        => pluginInterface?.SavePluginConfig(this);
}
