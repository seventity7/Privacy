namespace Privacy.Models;

internal sealed class FriendSnapshot
{
    public string Name { get; init; } = string.Empty;
    public string World { get; init; } = string.Empty;
    public uint WorldId { get; init; }
    public string DataCenter { get; init; } = string.Empty;
    public string CurrentDataCenter { get; init; } = string.Empty;
    public string CurrentWorld { get; init; } = string.Empty;
    public uint CurrentWorldId { get; init; }
    public string CurrentZone { get; init; } = string.Empty;
    public string ResidentialDetails { get; init; } = string.Empty;
    public ContactStatus Status { get; init; } = ContactStatus.Offline;
    public uint StatusIconId { get; init; }
    public ulong ContentId { get; init; }
    public string FullName => string.IsNullOrWhiteSpace(World) ? Name : $"{Name}@{World}";
}
