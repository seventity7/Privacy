using System;
using System.Collections.Generic;

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
