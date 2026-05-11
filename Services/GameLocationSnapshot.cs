namespace Privacy.Services;

internal sealed class GameLocationSnapshot
{
    public string Zone { get; init; } = string.Empty;
    public string ResidentialDetails { get; init; } = string.Empty;
    public string ResidentialDistrict { get; init; } = string.Empty;
    public int Ward { get; init; }
    public int Plot { get; init; }
    public int Room { get; init; }
    public bool IsResidential { get; init; }
}
