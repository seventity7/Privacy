using System;
using System.Collections.Generic;

namespace Privacy.Models;

[Serializable]
public sealed class FfxivVenueEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DataCenter { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public int Ward { get; set; }
    public int Plot { get; set; }
    public int Apartment { get; set; }
    public int Room { get; set; }
    public bool Subdivision { get; set; }
    public string Address { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;
    public string DiscordUrl { get; set; } = string.Empty;
    public string TeleportCommand { get; set; } = string.Empty;
    public string OpeningTime { get; set; } = string.Empty;
    public string ClosingTime { get; set; } = string.Empty;
    public List<string> OpeningScheduleLines { get; set; } = new();
    public bool IsOpenNow { get; set; }

    public string LocationTooltip => string.IsNullOrWhiteSpace(BuildFullLocation()) ? Name : BuildFullLocation();

    public string BuildAddress()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(District)) parts.Add(District);
        if (Ward > 0) parts.Add($"w{Ward}{(Subdivision ? " sub" : string.Empty)}");
        if (Plot > 0) parts.Add($"p{Plot}");
        else if (Apartment > 0) parts.Add($"apt {Apartment}");
        if (Room > 0) parts.Add($"room {Room}");
        return string.Join(", ", parts);
    }

    public string BuildFullLocation()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(DataCenter)) parts.Add(DataCenter);
        if (!string.IsNullOrWhiteSpace(World)) parts.Add(World);
        var address = BuildAddress();
        if (!string.IsNullOrWhiteSpace(address)) parts.Add(address);
        return string.Join(", ", parts);
    }
}
