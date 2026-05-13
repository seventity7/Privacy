using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Privacy.Services;

internal static unsafe class GameLocationResolver
{
    public static GameLocationSnapshot GetCurrent(IDataManager dataManager, IClientState clientState)
    {
        var territoryName = ResolveTerritoryName(dataManager, clientState.TerritoryType);
        var housingManager = HousingManager.Instance();
        var isResidential = false;
        var residentialDistrict = string.Empty;
        var residentialDetails = string.Empty;
        var zone = territoryName;
        var subdivision = false;

        if (housingManager != null)
        {
            var originalTerritoryId = 0u;

            try
            {
                if (housingManager->IsInside())
                    originalTerritoryId = HousingManager.GetOriginalHouseTerritoryTypeId();
            }
            catch
            {
                originalTerritoryId = 0;
            }

            if (originalTerritoryId != 0)
            {
                var originalTerritoryName = ResolveTerritoryName(dataManager, originalTerritoryId);
                if (!string.IsNullOrWhiteSpace(originalTerritoryName))
                    zone = originalTerritoryName;
            }

            var ward = housingManager->GetCurrentWard() + 1;
            var plot = housingManager->GetCurrentPlot();
            var room = housingManager->GetCurrentRoom();
            var division = housingManager->GetCurrentDivision();
            subdivision = division == 2;
            var hasResidentialContext = housingManager->CurrentTerritory is not null || IsKnownResidentialDistrict(zone);

            if (ward > 0 && hasResidentialContext)
            {
                isResidential = true;
                residentialDistrict = ResolveResidentialDistrict(zone);
                residentialDetails = BuildResidentialDetails(ward, plot, room, division);
            }
        }

        if (string.IsNullOrWhiteSpace(zone))
            zone = ResolveBestFallbackZone(dataManager);

        return new GameLocationSnapshot
        {
            Zone = zone.Trim(),
            ResidentialDetails = residentialDetails,
            ResidentialDistrict = residentialDistrict,
            Ward = ExtractNumber(residentialDetails, 'w'),
            Plot = ExtractNumber(residentialDetails, 'p'),
            Room = ExtractRoom(residentialDetails),
            Subdivision = subdivision,
            IsResidential = isResidential,
        };
    }

    private static string BuildResidentialDetails(int ward, int plot, int room, int division)
    {
        var parts = new List<string>();

        if (ward > 0)
            parts.Add($"w{ward}");

        if (division == 2)
            parts.Add("Subdivision");

        if (plot < -1)
        {
            parts.Add(room == 0 ? "Apartment Lobby" : $"Apartment {room}");
        }
        else if (plot > -1)
        {
            parts.Add($"p{plot + 1}");
            if (room > 0)
                parts.Add($"Room {room}");
        }

        return string.Join(" - ", parts);
    }

    private static string ResolveBestFallbackZone(IDataManager dataManager)
    {
        try
        {
            var territoryInfo = TerritoryInfo.Instance();
            if (territoryInfo == null)
                return string.Empty;

            var names = new[]
            {
                ResolvePlaceName(dataManager, territoryInfo->AreaPlaceNameId),
                ResolvePlaceName(dataManager, territoryInfo->SubAreaPlaceNameId),
            }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            return string.Join(" - ", names);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolveTerritoryName(IDataManager dataManager, uint territoryId)
    {
        if (territoryId == 0)
            return string.Empty;

        try
        {
            var territory = dataManager.GetExcelSheet<TerritoryType>().GetRow(territoryId);
            return territory.PlaceName.Value.Name.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolvePlaceName(IDataManager dataManager, uint placeNameId)
    {
        if (placeNameId == 0)
            return string.Empty;

        try
        {
            return dataManager.GetExcelSheet<PlaceName>().GetRow(placeNameId).Name.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolveResidentialDistrict(string zone)
    {
        if (zone.Contains("Mist", StringComparison.OrdinalIgnoreCase)) return "Mist";
        if (zone.Contains("Lavender", StringComparison.OrdinalIgnoreCase) || zone.Contains("Lavander", StringComparison.OrdinalIgnoreCase)) return "Lavender Beds";
        if (zone.Contains("Goblet", StringComparison.OrdinalIgnoreCase)) return "Goblet";
        if (zone.Contains("Shirogane", StringComparison.OrdinalIgnoreCase)) return "Shirogane";
        if (zone.Contains("Empyreum", StringComparison.OrdinalIgnoreCase)) return "Empyreum";
        return string.Empty;
    }

    private static bool IsKnownResidentialDistrict(string zone)
        => !string.IsNullOrWhiteSpace(ResolveResidentialDistrict(zone));

    private static int ExtractNumber(string text, char prefix)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var marker = prefix.ToString();
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return 0;

        index += marker.Length;
        var start = index;
        while (index < text.Length && char.IsDigit(text[index]))
            index++;

        return index > start && int.TryParse(text[start..index], out var value) ? value : 0;
    }

    private static int ExtractRoom(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        const string marker = "Room";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return 0;

        index += marker.Length;
        while (index < text.Length && !char.IsDigit(text[index]))
            index++;

        var start = index;
        while (index < text.Length && char.IsDigit(text[index]))
            index++;

        return index > start && int.TryParse(text[start..index], out var value) ? value : 0;
    }
}
