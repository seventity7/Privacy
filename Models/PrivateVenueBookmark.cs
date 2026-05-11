using System;
using System.Collections.Generic;

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
    public string ImageUrl { get; set; } = string.Empty;
    public string ImageLocalPath { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;
    public string DiscordUrl { get; set; } = string.Empty;
    public string TeleportCommand { get; set; } = string.Empty;
    public bool Favorite { get; set; }
    public string TooltipTag { get; set; } = string.Empty;
    public string TooltipTagColorHex { get; set; } = "#B56CFF";
    public string Source { get; set; } = string.Empty;

    public string CommandDistrict
        => District.Equals("Lavender Beds", StringComparison.OrdinalIgnoreCase) ? "Lb" : District;

    public string BuildAddress()
    {
        if (!string.IsNullOrWhiteSpace(Address))
            return Address.Trim();

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(DataCenter)) parts.Add(DataCenter);
        if (!string.IsNullOrWhiteSpace(World)) parts.Add(World);
        if (!string.IsNullOrWhiteSpace(CommandDistrict)) parts.Add(CommandDistrict);
        if (Ward > 0) parts.Add($"w{Ward}");
        if (Plot > 0) parts.Add($"p{Plot}");
        return string.Join(" / ", parts);
    }

    public string BuildTeleportCommand()
    {
        if (!string.IsNullOrWhiteSpace(TeleportCommand))
        {
            var command = TeleportCommand.Trim();
            if (!command.StartsWith("/li ", StringComparison.OrdinalIgnoreCase))
                command = "/li " + command.TrimStart('/').Trim();
            return NormalizeWardPlot(command);
        }

        if (string.IsNullOrWhiteSpace(World) || Ward <= 0)
            return string.Empty;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(DataCenter)) parts.Add(DataCenter);
        parts.Add(World);
        if (!string.IsNullOrWhiteSpace(District)) parts.Add(CommandDistrict);
        parts.Add($"w{Ward}");
        if (Plot > 0) parts.Add($"p{Plot}");
        return "/li " + string.Join(" ", parts);
    }

    private static string NormalizeWardPlot(string command)
        => command.Replace(" W", " w", StringComparison.Ordinal).Replace(" P", " p", StringComparison.Ordinal);
}
