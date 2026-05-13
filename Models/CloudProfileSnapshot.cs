using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Privacy.Models;

[Serializable]
public sealed class CloudProfileSnapshot
{
    public string ProfileId { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public string HomeWorld { get; set; } = string.Empty;
    public uint HomeWorldId { get; set; }
    public ulong ContentId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string TagColorHex { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public string StatusColorHex { get; set; } = "#2BE5B5";
    public string ThemeName { get; set; } = "Default";
    public string ThemeColorHex { get; set; } = "#2BE5B5";
    public string DisplayNameEffect { get; set; } = "None";
    public string BannerUrl { get; set; } = string.Empty;
    public string AvatarBorderColorHex { get; set; } = "#2BE5B5";
    public string AvatarPlaceholderStyle { get; set; } = "Question Mark";

    [JsonPropertyName("display_name")]
    public string DisplayNameSnake { set => DisplayName = value ?? string.Empty; }
    [JsonPropertyName("avatar_url")]
    public string AvatarUrlSnake { set => AvatarUrl = value ?? string.Empty; }
    [JsonPropertyName("status_message")]
    public string StatusMessageSnake { set => StatusMessage = value ?? string.Empty; }
    [JsonPropertyName("status_color_hex")]
    public string StatusColorHexSnake { set => StatusColorHex = value ?? "#2BE5B5"; }
    [JsonPropertyName("theme_name")]
    public string ThemeNameSnake { set => ThemeName = value ?? "Default"; }
    [JsonPropertyName("theme_color_hex")]
    public string ThemeColorHexSnake { set => ThemeColorHex = value ?? "#2BE5B5"; }
    [JsonPropertyName("display_name_effect")]
    public string DisplayNameEffectSnake { set => DisplayNameEffect = value ?? "None"; }
    [JsonPropertyName("banner_url")]
    public string BannerUrlSnake { set => BannerUrl = value ?? string.Empty; }
    [JsonPropertyName("avatar_border_color_hex")]
    public string AvatarBorderColorHexSnake { set => AvatarBorderColorHex = value ?? "#2BE5B5"; }
    [JsonPropertyName("avatar_placeholder_style")]
    public string AvatarPlaceholderStyleSnake { set => AvatarPlaceholderStyle = value ?? "Question Mark"; }
    public string StatusVisibility { get; set; } = "All";
    public string VenuesVisibility { get; set; } = "All";
    public string LocationVisibility { get; set; } = "All";
    public string BioVisibility { get; set; } = "All";
    public List<PrivateVenueBookmark> Venues { get; set; } = new();
    public ContactStatus Status { get; set; } = ContactStatus.Offline;
    public string CurrentDataCenter { get; set; } = string.Empty;
    public string CurrentWorld { get; set; } = string.Empty;
    public uint CurrentWorldId { get; set; }
    public string CurrentZone { get; set; } = string.Empty;
    public string ResidentialDetails { get; set; } = string.Empty;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset ProfileUpdatedAt { get; set; } = DateTimeOffset.MinValue;
}
