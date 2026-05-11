using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using Privacy.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Privacy.Services;

internal sealed unsafe class FriendListService
{
    private const uint OfflineIcon = 61504;
    private const uint BusyIcon = 61559;
    private const uint OnlineIcon = 61505;

    private readonly IDataManager dataManager;
    private readonly IPluginLog log;
    private readonly List<FriendSnapshot> friends = new();

    public FriendListService(IDataManager dataManager, IPluginLog log)
    {
        this.dataManager = dataManager;
        this.log = log;
    }

    public IReadOnlyList<FriendSnapshot> Friends => friends;
    public int FriendCount => friends.Count;

    public void Refresh()
    {
        friends.Clear();

        try
        {
            var proxy = InfoProxyFriendList.Instance();
            if (proxy == null) return;

            for (uint i = 0; i < proxy->EntryCount; i++)
            {
                var entry = proxy->GetEntry(i);
                if (entry == null) continue;

                var name = CleanName(entry->Name);
                if (string.IsNullOrWhiteSpace(name)) continue;

                var homeWorldId = (uint)entry->HomeWorld;
                var currentWorldId = (uint)entry->CurrentWorld;
                var locationWorldId = currentWorldId != 0 ? currentWorldId : homeWorldId;
                var status = ResolveStatus(entry->State);
                var locationId = (uint)entry->Location;
                var zoneName = ResolveTerritoryName(locationId);

                friends.Add(new FriendSnapshot
                {
                    Name = name,
                    World = ResolveWorldName(homeWorldId),
                    WorldId = homeWorldId,
                    CurrentWorld = ResolveWorldName(locationWorldId),
                    CurrentWorldId = locationWorldId,
                    CurrentDataCenter = ResolveDataCenterName(locationWorldId),
                    CurrentZone = zoneName,
                    ResidentialDetails = BuildResidentialDetails(zoneName),
                    DataCenter = ResolveDataCenterName(homeWorldId),
                    Status = status,
                    StatusIconId = ResolveStatusIcon(status),
                    ContentId = entry->ContentId,
                });
            }

            friends.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Failed to read native friend list.");
        }
    }

    private static ContactStatus ResolveStatus(InfoProxyCommonList.CharacterData.OnlineStatus state)
    {
        if (state == InfoProxyCommonList.CharacterData.OnlineStatus.Offline ||
            state.HasFlag(InfoProxyCommonList.CharacterData.OnlineStatus.OfflineExd) ||
            state.HasFlag(InfoProxyCommonList.CharacterData.OnlineStatus.Disconnected) ||
            state.HasFlag(InfoProxyCommonList.CharacterData.OnlineStatus.NotFound))
            return ContactStatus.Offline;

        if (state.HasFlag(InfoProxyCommonList.CharacterData.OnlineStatus.Busy))
            return ContactStatus.Busy;

        return ContactStatus.Online;
    }

    private static uint ResolveStatusIcon(ContactStatus status)
        => status switch
        {
            ContactStatus.Busy => BusyIcon,
            ContactStatus.Online => OnlineIcon,
            _ => OfflineIcon,
        };

    private static string BuildResidentialDetails(string zoneName)
    {
        if (string.IsNullOrWhiteSpace(zoneName)) return string.Empty;

        var district = ExtractResidentialDistrict(zoneName);
        if (string.IsNullOrWhiteSpace(district)) return string.Empty;

        var housingNumbers = ExtractHousingNumbers(zoneName);
        return string.IsNullOrWhiteSpace(housingNumbers)
            ? $"Residential District: {district}"
            : $"Residential District: {district} - {housingNumbers}";
    }

    private static string ExtractHousingNumbers(string zoneName)
    {
        var ward = ExtractNumberAfter(zoneName, "Ward");
        var plot = ExtractNumberAfter(zoneName, "Plot");
        if (!string.IsNullOrWhiteSpace(ward) && !string.IsNullOrWhiteSpace(plot)) return $"w{ward} - p{plot}";
        if (!string.IsNullOrWhiteSpace(ward)) return $"w{ward}";
        if (!string.IsNullOrWhiteSpace(plot)) return $"p{plot}";
        return string.Empty;
    }

    private static string ExtractNumberAfter(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return string.Empty;

        index += marker.Length;
        while (index < text.Length && !char.IsDigit(text[index])) index++;
        var start = index;
        while (index < text.Length && char.IsDigit(text[index])) index++;
        return index > start ? text[start..index] : string.Empty;
    }

    private static string ExtractResidentialDistrict(string zoneName)
    {
        var text = zoneName.Trim();
        var separators = new[] { " - ", ": " };
        foreach (var separator in separators)
        {
            var index = text.LastIndexOf(separator, StringComparison.Ordinal);
            if (index >= 0 && index + separator.Length < text.Length)
                return text[(index + separator.Length)..].Trim();
        }

        return text.Contains("House", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Estate", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Apartment", StringComparison.OrdinalIgnoreCase)
            ? text
            : string.Empty;
    }

    private string ResolveWorldName(uint worldId)
    {
        if (worldId == 0) return string.Empty;
        try
        {
            var world = dataManager.GetExcelSheet<World>().GetRow(worldId);
            return world.Name.ToString();
        }
        catch { return string.Empty; }
    }


    private string ResolveTerritoryName(uint territoryId)
    {
        if (territoryId == 0) return string.Empty;
        try
        {
            var territory = dataManager.GetExcelSheet<TerritoryType>().GetRow(territoryId);
            return territory.PlaceName.Value.Name.ToString();
        }
        catch { return string.Empty; }
    }

    private string ResolveDataCenterName(uint worldId)
    {
        if (worldId == 0) return string.Empty;
        try
        {
            var world = dataManager.GetExcelSheet<World>().GetRow(worldId);
            return world.DataCenter.Value.Name.ToString();
        }
        catch { return string.Empty; }
    }

    private static string CleanName(ReadOnlySpan<byte> name)
    {
        var zeroIndex = name.IndexOf((byte)0);
        if (zeroIndex >= 0)
        {
            name = name[..zeroIndex];
        }

        return CleanName(Encoding.UTF8.GetString(name));
    }

    private static string CleanName(string name)
        => name.Replace("\uE05D", string.Empty, StringComparison.Ordinal)
            .Replace("\uE05E", string.Empty, StringComparison.Ordinal)
            .Trim();
}
