using System;

namespace Privacy.Models;

[Serializable]
public sealed class PrivateContact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    // Home world is kept for identity, tells and party invites.
    public string World { get; set; } = string.Empty;
    public uint WorldId { get; set; }
    public ulong ContentId { get; set; }
    public string DataCenter { get; set; } = string.Empty;

    // Current location is kept separately so the UI and Lifestream travel do not
    // incorrectly use the character's home world when they are visiting elsewhere.
    public string CurrentWorld { get; set; } = string.Empty;
    public uint CurrentWorldId { get; set; }
    public string CurrentDataCenter { get; set; } = string.Empty;
    public string LastKnownZone { get; set; } = string.Empty;
    public string ResidentialDetails { get; set; } = string.Empty;
    public string ProfileImagePath { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string ContactSymbol { get; set; } = string.Empty;
    public string ContactSymbolColorHex { get; set; } = "#2BE5B5";
    public string MainJob { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Nameday { get; set; } = string.Empty;
    public string PreferredContent { get; set; } = string.Empty;
    public string ProfileBio { get; set; } = string.Empty;
    public string RelationshipStatus { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string HoverNote { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string TagColorHex { get; set; } = "#FFD56A";
    public string VenueColorHex { get; set; } = "#2BE5B5";
    public string VenueTeleportCommand { get; set; } = string.Empty;
    public string VenueDiscordUrl { get; set; } = string.Empty;
    public string CloudProfileId { get; set; } = string.Empty;
    public bool CloudAccountLinked { get; set; }
    public string CloudDisplayName { get; set; } = string.Empty;
    public string CloudAvatarUrl { get; set; } = string.Empty;
    public string CloudStatusMessage { get; set; } = string.Empty;
    public string CloudStatusColorHex { get; set; } = "#2BE5B5";
    public string CloudBio { get; set; } = string.Empty;
    public string CloudThemeName { get; set; } = "Default";
    public string CloudThemeColorHex { get; set; } = "#2BE5B5";
    public string CloudDisplayNameEffect { get; set; } = "None";
    public string CloudBannerUrl { get; set; } = string.Empty;
    public string CloudBannerLocalPath { get; set; } = string.Empty;
    public string CloudAvatarBorderColorHex { get; set; } = "#2BE5B5";
    public string CloudAvatarPlaceholderStyle { get; set; } = "Question Mark";
    public string CloudStatusVisibility { get; set; } = "All";
    public string CloudVenuesVisibility { get; set; } = "All";
    public string CloudLocationVisibility { get; set; } = "All";
    public string CloudBioVisibility { get; set; } = "All";
    public List<PrivateVenueBookmark> CloudVenues { get; set; } = new();
    public bool CloudManagedProfileImage { get; set; }
    public DateTimeOffset CloudLastSyncedAt { get; set; } = DateTimeOffset.MinValue;
    public bool Favorite { get; set; }
    public bool IsPinned { get; set; }
    public bool EnableStatusNotification { get; set; }
    public ContactStatus Status { get; set; } = ContactStatus.Offline;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.MinValue;

    public string DisplayLocation
    {
        get
        {
            var dataCenter = string.IsNullOrWhiteSpace(CurrentDataCenter)
                ? string.IsNullOrWhiteSpace(DataCenter) ? "Unknown DC" : DataCenter
                : CurrentDataCenter;
            var world = string.IsNullOrWhiteSpace(CurrentWorld)
                ? string.IsNullOrWhiteSpace(World) ? "Unknown World" : World
                : CurrentWorld;
            var zone = string.IsNullOrWhiteSpace(LastKnownZone) ? "Unknown Zone" : LastKnownZone;
            return $"{dataCenter} - {world} - {zone}";
        }
    }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Nickname)) return Nickname.Trim();
            if (!string.IsNullOrWhiteSpace(CloudDisplayName)) return CloudDisplayName.Trim();
            return Name;
        }
    }

    public bool HasSoftWarning
        => RelationshipStatus.Equals("Avoid", StringComparison.OrdinalIgnoreCase);

    public string TellAddress
    {
        get
        {
            if (string.IsNullOrWhiteSpace(World)) return Name;
            return $"{Name}@{World}";
        }
    }
}
