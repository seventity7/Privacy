using System;

namespace Privacy.Models;

[Serializable]
public sealed class PrivateVenueBookmark
{
    public string Name { get; set; } = string.Empty;
    public string DataCenter { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public int Ward { get; set; } = 1;
    public int Plot { get; set; } = 1;
    public string Address { get; set; } = string.Empty;

    public string CommandDistrict
        => District.Equals("Lavender Beds", StringComparison.OrdinalIgnoreCase) ? "Lb" : District;

    public string BuildAddress()
        => $"{DataCenter} {World} {CommandDistrict} w{Ward} p{Plot}".Trim();
}
