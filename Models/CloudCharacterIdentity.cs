using System;

namespace Privacy.Models;

[Serializable]
public sealed class CloudCharacterIdentity
{
    public string CharacterName { get; set; } = string.Empty;
    public string HomeWorld { get; set; } = string.Empty;
    public string HomeWorldName => HomeWorld;
    public uint HomeWorldId { get; set; }
    public ulong ContentId { get; set; }
    public string CurrentWorld { get; set; } = string.Empty;
    public string CurrentWorldName => CurrentWorld;
    public uint CurrentWorldId { get; set; }
    public string DataCenter { get; set; } = string.Empty;
    public string CurrentDataCenter { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string CurrentZone => Zone;
    public string ResidentialDetails { get; set; } = string.Empty;
    public string ResidentialInfo => ResidentialDetails;

    public bool IsUsable
        => !string.IsNullOrWhiteSpace(CharacterName) && HomeWorldId != 0;
}
