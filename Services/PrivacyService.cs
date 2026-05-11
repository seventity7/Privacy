using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Privacy.Models;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace Privacy.Services;

internal sealed class PrivacyService
{
    private readonly Configuration config;
    private readonly IDataManager dataManager;
    private readonly IObjectTable objectTable;
    private readonly ITargetManager targetManager;
    private readonly IClientState clientState;
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;

    public PrivacyService(Configuration config, IDataManager dataManager, IObjectTable objectTable, ITargetManager targetManager, IClientState clientState, IChatGui chatGui, IPluginLog log)
    {
        this.config = config;
        this.dataManager = dataManager;
        this.objectTable = objectTable;
        this.targetManager = targetManager;
        this.clientState = clientState;
        this.chatGui = chatGui;
        this.log = log;
    }

    public bool AddFromContextTarget(MenuTargetDefault target, out PrivateContact? contact, out string message)
    {
        contact = null;
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(target.TargetName))
        {
            message = "Target name was empty.";
            return false;
        }

        var targetPlayer = FindPlayerFromContext(target);
        var worldId = target.TargetHomeWorld.RowId;
        var world = ResolveWorldName(worldId);
        var dataCenter = ResolveDataCenterName(worldId);
        var currentWorldId = targetPlayer != null ? GetPlayerCurrentWorldId(targetPlayer) : ResolveLocalCurrentWorldId();
        var currentWorld = ResolveWorldName(currentWorldId);
        var currentDataCenter = ResolveDataCenterName(currentWorldId);
        var location = ResolveCurrentLocation();
        var zone = location.Zone;
        var residentialDetails = location.ResidentialDetails;
        var status = targetPlayer != null ? ResolveStatus(targetPlayer) : ContactStatus.Online;

        contact = AddOrUpdate(target.TargetName, world, worldId, dataCenter, zone, status, currentWorld, currentWorldId, currentDataCenter);
        contact.ResidentialDetails = residentialDetails;
        config.Save();
        log.Information("Privacy: added/updated context target {Name}@{World}; current={CurrentWorld}; zone={Zone}; residential={Residential}.", contact.Name, contact.World, contact.CurrentWorld, contact.LastKnownZone, contact.ResidentialDetails);
        message = $"Added {contact.Name}@{contact.World} to Privacy.";
        return true;
    }

    public PrivateContact AddOrUpdate(
        string name,
        string world,
        uint worldId,
        string dataCenter,
        string zone,
        ContactStatus status,
        string currentWorld = "",
        uint currentWorldId = 0,
        string currentDataCenter = "")
    {
        if (string.IsNullOrWhiteSpace(currentWorld))
            currentWorld = world;
        if (currentWorldId == 0)
            currentWorldId = worldId;
        if (string.IsNullOrWhiteSpace(currentDataCenter))
            currentDataCenter = ResolveDataCenterName(currentWorldId != 0 ? currentWorldId : worldId);
        var contact = FindExistingContact(name, world, 0);
        if (contact == null)
        {
            contact = new PrivateContact
            {
                Name = CleanName(name),
                World = world,
                WorldId = worldId,
                DataCenter = dataCenter,
                CurrentWorld = currentWorld,
                CurrentWorldId = currentWorldId,
                CurrentDataCenter = currentDataCenter,
                LastKnownZone = zone,
                ResidentialDetails = BuildResidentialDetails(zone),
                Status = status,
                AddedAt = DateTimeOffset.UtcNow,
                LastSeenAt = status == ContactStatus.Offline ? DateTimeOffset.MinValue : DateTimeOffset.UtcNow,
            };
            config.Contacts.Add(contact);
            AddHistory(contact, "Added", "Added to Privacy");
        }
        else
        {
            contact.Name = CleanName(name);
            contact.World = world;
            contact.WorldId = worldId;
            contact.DataCenter = dataCenter;
            contact.CurrentWorld = currentWorld;
            contact.CurrentWorldId = currentWorldId;
            contact.CurrentDataCenter = currentDataCenter;
            if (!string.IsNullOrWhiteSpace(zone))
            {
                contact.LastKnownZone = zone;
                contact.ResidentialDetails = BuildResidentialDetails(zone);
            }
            var previousStatus = contact.Status;
            var previousLocation = contact.DisplayLocation;
            contact.Status = status;
            if (status != ContactStatus.Offline) contact.LastSeenAt = DateTimeOffset.UtcNow;
            RecordStateChanges(contact, previousStatus, previousLocation);
        }

        config.Save();
        return contact;
    }

    public PrivateContact AddFromFriend(FriendSnapshot friend)
    {
        var zone = string.IsNullOrWhiteSpace(friend.CurrentZone) ? ResolveCurrentZoneName() : friend.CurrentZone;
        var currentWorld = string.IsNullOrWhiteSpace(friend.CurrentWorld) ? friend.World : friend.CurrentWorld;
        var currentWorldId = friend.CurrentWorldId != 0 ? friend.CurrentWorldId : friend.WorldId;
        var currentDataCenter = string.IsNullOrWhiteSpace(friend.CurrentDataCenter) ? ResolveDataCenterName(currentWorldId) : friend.CurrentDataCenter;
        var duplicate = FindExistingContact(friend.Name, friend.World, friend.ContentId);
        var contact = duplicate ?? AddOrUpdate(friend.Name, friend.World, friend.WorldId, friend.DataCenter, zone, friend.Status, currentWorld, currentWorldId, currentDataCenter);
        if (duplicate != null)
        {
            log.Information("Privacy: duplicate friend detected for {Name}@{World}; updating existing contact {ExistingName}@{ExistingWorld}.", friend.Name, friend.World, duplicate.Name, duplicate.World);
            contact.Name = CleanName(friend.Name);
            contact.World = friend.World;
            contact.WorldId = friend.WorldId;
            contact.DataCenter = friend.DataCenter;
            contact.CurrentWorld = currentWorld;
            contact.CurrentWorldId = currentWorldId;
            contact.CurrentDataCenter = currentDataCenter;
            contact.LastKnownZone = zone;
            contact.Status = friend.Status;
            if (friend.Status != ContactStatus.Offline) contact.LastSeenAt = DateTimeOffset.UtcNow;
        }
        contact.ContentId = friend.ContentId;
        contact.ResidentialDetails = string.IsNullOrWhiteSpace(friend.ResidentialDetails) ? BuildResidentialDetails(zone) : friend.ResidentialDetails;
        config.Save();
        log.Information("Privacy: added/updated friend {Name}@{World}; current={CurrentWorld}; zone={Zone}; residential={Residential}.", contact.Name, contact.World, contact.CurrentWorld, contact.LastKnownZone, contact.ResidentialDetails);
        return contact;
    }

    public void Remove(PrivateContact contact)
    {
        log.Information("Privacy: removing contact {Name}@{World}.", contact.Name, contact.World);
        config.Contacts.Remove(contact);
        foreach (var group in config.Groups)
            group.ContactIds.RemoveAll(id => string.Equals(id, contact.Id, StringComparison.Ordinal));
        config.Save();
    }

    public void RefreshRuntimeState(IReadOnlyList<FriendSnapshot>? nativeFriends = null)
    {
        if (config.Contacts.Count == 0) return;

        try
        {
            var currentLocation = ResolveCurrentLocation();
            var currentZone = currentLocation.Zone;
            var currentResidentialDetails = currentLocation.ResidentialDetails;
            var seen = config.Contacts.ToDictionary(c => c.Id, _ => false, StringComparer.Ordinal);
            var visibleInCurrentWorld = new HashSet<string>(StringComparer.Ordinal);

            foreach (var player in objectTable.OfType<IPlayerCharacter>())
            {
                var name = CleanName(player.Name.ToString());
                var worldId = GetPlayerHomeWorldId(player);
                var world = ResolveWorldName(worldId);
                if (string.IsNullOrWhiteSpace(world)) continue;

                var contact = FindContact(name, world);
                if (contact == null) continue;

                var previousStatus = contact.Status;
                var previousLocation = contact.DisplayLocation;
                var currentWorldId = GetPlayerCurrentWorldId(player);
                contact.WorldId = worldId;
                contact.World = world;
                contact.DataCenter = ResolveDataCenterName(worldId);
                contact.CurrentWorldId = currentWorldId != 0 ? currentWorldId : worldId;
                contact.CurrentWorld = ResolveWorldName(contact.CurrentWorldId);
                contact.CurrentDataCenter = ResolveDataCenterName(contact.CurrentWorldId);
                contact.Status = ResolveStatus(player);
                contact.LastSeenAt = DateTimeOffset.UtcNow;
                if (!string.IsNullOrWhiteSpace(currentZone))
                {
                    contact.LastKnownZone = currentZone;
                    contact.ResidentialDetails = string.IsNullOrWhiteSpace(currentResidentialDetails)
                        ? BuildResidentialDetails(currentZone)
                        : currentResidentialDetails;
                }
                RecordStateChanges(contact, previousStatus, previousLocation);
                seen[contact.Id] = true;
                visibleInCurrentWorld.Add(contact.Id);
            }

            foreach (var contact in config.Contacts)
            {
                if (seen.TryGetValue(contact.Id, out var isVisible) && isVisible) continue;

                // When the native friend list has this contact loaded, let MergeNativeFriendState
                // apply the final state. This avoids creating a fake Offline -> Online transition
                // once per refresh for friends who are online but not visible in ObjectTable.
                var hasNativeFriendState = nativeFriends?.Any(friend =>
                    (friend.ContentId != 0 && contact.ContentId != 0 && friend.ContentId == contact.ContentId) ||
                    (string.Equals(CleanName(friend.Name), contact.Name, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(friend.World, contact.World, StringComparison.OrdinalIgnoreCase))) == true;
                if (hasNativeFriendState) continue;

                if (HasFreshCloudPresence(contact))
                    continue;

                var previousStatus = contact.Status;
                var previousLocation = contact.DisplayLocation;
                contact.Status = ContactStatus.Offline;
                RecordStateChanges(contact, previousStatus, previousLocation);
                if (!config.KeepLastKnownLocationWhenOffline)
                    contact.LastKnownZone = string.Empty;
            }

            if (nativeFriends != null && nativeFriends.Count > 0)
                MergeNativeFriendState(nativeFriends, visibleInCurrentWorld);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Failed to refresh Privacy runtime state.");
        }
    }


    public bool TryTargetContact(PrivateContact contact)
    {
        try
        {
            var cleanContactName = CleanName(contact.Name);

            foreach (var player in objectTable.OfType<IPlayerCharacter>())
            {
                var playerName = CleanName(player.Name.ToString());
                if (!string.Equals(playerName, cleanContactName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var playerWorldId = GetPlayerHomeWorldId(player);
                var worldMatches = contact.WorldId == 0 || playerWorldId == contact.WorldId;

                if (!worldMatches && !string.IsNullOrWhiteSpace(contact.World))
                {
                    var playerWorld = ResolveWorldName(playerWorldId);
                    worldMatches = string.Equals(playerWorld, contact.World, StringComparison.OrdinalIgnoreCase);
                }

                if (!worldMatches)
                    continue;

                targetManager.Target = player;
                log.Information("Privacy: set target to {Name}@{World}; objectId={ObjectId}.", contact.Name, contact.World, player.GameObjectId);
                return true;
            }

            log.Information("Privacy: could not target {Name}@{World}; no nearby matching player was found.", contact.Name, contact.World);
            return false;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Privacy: failed to target {Name}@{World}.", contact.Name, contact.World);
            return false;
        }
    }


    public bool IsInLocalZone(PrivateContact contact)
    {
        var localZone = ResolveCurrentZoneName();
        return !string.IsNullOrWhiteSpace(localZone) &&
               !string.IsNullOrWhiteSpace(contact.LastKnownZone) &&
               string.Equals(localZone, contact.LastKnownZone, StringComparison.OrdinalIgnoreCase) &&
               IsInLocalWorld(contact);
    }

    public bool IsInLocalWorld(PrivateContact contact)
    {
        var localWorldId = ResolveLocalCurrentWorldId();
        if (localWorldId != 0 && contact.CurrentWorldId != 0)
            return localWorldId == contact.CurrentWorldId;

        var localWorld = ResolveWorldName(localWorldId);
        return !string.IsNullOrWhiteSpace(localWorld) &&
               !string.IsNullOrWhiteSpace(contact.CurrentWorld) &&
               string.Equals(localWorld, contact.CurrentWorld, StringComparison.OrdinalIgnoreCase);
    }

    public PrivateContact? FindExistingContact(string name, string world, ulong contentId)
    {
        if (contentId != 0)
        {
            var byContentId = config.Contacts.FirstOrDefault(c => c.ContentId == contentId);
            if (byContentId != null) return byContentId;
        }

        var cleanName = CleanName(name);
        var exact = config.Contacts.FirstOrDefault(c =>
            string.Equals(c.Name, cleanName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.World, world, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        return config.Contacts.FirstOrDefault(c =>
            string.Equals(c.Name, cleanName, StringComparison.OrdinalIgnoreCase) &&
            c.WorldId != 0 &&
            string.Equals(ResolveWorldName(c.WorldId), world, StringComparison.OrdinalIgnoreCase));
    }

    public void AddHistory(PrivateContact contact, string kind, string message)
    {
        config.History ??= new List<PrivacyEvent>();
        config.History.Add(new PrivacyEvent
        {
            ContactId = contact.Id,
            ContactName = contact.Name,
            ContactWorld = contact.World,
            Kind = kind,
            Message = message,
            Timestamp = DateTimeOffset.UtcNow,
        });

        config.Save();
    }

    private void RecordStateChanges(PrivateContact contact, ContactStatus previousStatus, string previousLocation)
    {
        if (previousStatus != contact.Status)
        {
            AddHistory(contact, "Status", contact.Status == ContactStatus.Offline ? "Went offline" : $"Is now {contact.Status}");
            log.Information("Privacy: status changed for {Name}@{World}: {OldStatus} -> {NewStatus}.", contact.Name, contact.World, previousStatus, contact.Status);
        }

        var newLocation = contact.DisplayLocation;
        if (!string.Equals(previousLocation, newLocation, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(contact.LastKnownZone))
        {
            AddHistory(contact, "Location", $"Location updated: {newLocation}");
            log.Information("Privacy: location changed for {Name}@{World}: {OldLocation} -> {NewLocation}.", contact.Name, contact.World, previousLocation, newLocation);
        }
    }

    public PrivateContact? FindContact(string name, string world)
    {
        var cleanName = CleanName(name);
        return config.Contacts.FirstOrDefault(c =>
            string.Equals(c.Name, cleanName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.World, world, StringComparison.OrdinalIgnoreCase));
    }

    public string BuildTravelCommand(PrivateContact contact)
    {
        var world = string.IsNullOrWhiteSpace(contact.CurrentWorld) ? contact.World : contact.CurrentWorld;
        var zone = (contact.LastKnownZone ?? string.Empty).Trim();
        var destination = BuildLifestreamDestination(zone);

        if (string.IsNullOrWhiteSpace(world))
        {
            var fallback = config.TravelCommandTemplate
                .Replace("{DataCenter}", contact.CurrentDataCenter ?? contact.DataCenter ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("{World}", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("{Zone}", destination, StringComparison.OrdinalIgnoreCase)
                .Trim();
            log.Information("Privacy: built fallback Lifestream command for {Name}: {Command}", contact.Name, fallback);
            return fallback;
        }

        var command = !string.IsNullOrWhiteSpace(destination)
            ? $"/li {world},{destination}"
            : $"/li {world}";
        log.Information("Privacy: built Lifestream command for {Name}@{World}: {Command}", contact.Name, contact.World, command);
        return command;
    }

    private void MergeNativeFriendState(IReadOnlyList<FriendSnapshot> nativeFriends, HashSet<string> visibleInCurrentWorld)
    {
        foreach (var friend in nativeFriends)
        {
            var contact = FindContact(friend.Name, friend.World);
            if (contact == null) continue;
            if (visibleInCurrentWorld.Contains(contact.Id)) continue;
            if (HasFreshCloudPresence(contact)) continue;

            var previousStatus = contact.Status;
            var previousLocation = contact.DisplayLocation;

            contact.WorldId = friend.WorldId;
            contact.ContentId = friend.ContentId;
            if (!string.IsNullOrWhiteSpace(friend.World)) contact.World = friend.World;
            if (!string.IsNullOrWhiteSpace(friend.DataCenter)) contact.DataCenter = friend.DataCenter;

            var currentWorldId = friend.CurrentWorldId != 0 ? friend.CurrentWorldId : friend.WorldId;
            var currentWorld = string.IsNullOrWhiteSpace(friend.CurrentWorld) ? friend.World : friend.CurrentWorld;
            var currentDataCenter = string.IsNullOrWhiteSpace(friend.CurrentDataCenter)
                ? ResolveDataCenterName(currentWorldId != 0 ? currentWorldId : friend.WorldId)
                : friend.CurrentDataCenter;

            if (currentWorldId != 0) contact.CurrentWorldId = currentWorldId;
            if (!string.IsNullOrWhiteSpace(currentWorld)) contact.CurrentWorld = currentWorld;
            if (!string.IsNullOrWhiteSpace(currentDataCenter)) contact.CurrentDataCenter = currentDataCenter;
            if (!string.IsNullOrWhiteSpace(friend.CurrentZone))
            {
                contact.LastKnownZone = friend.CurrentZone;
                contact.ResidentialDetails = string.IsNullOrWhiteSpace(friend.ResidentialDetails) ? BuildResidentialDetails(friend.CurrentZone) : friend.ResidentialDetails;
            }

            contact.Status = friend.Status;
            if (friend.Status != ContactStatus.Offline)
                contact.LastSeenAt = DateTimeOffset.UtcNow;
            RecordStateChanges(contact, previousStatus, previousLocation);
        }
    }


    private bool HasFreshCloudPresence(PrivateContact contact)
    {
        return config.CloudEnabled
            && contact.CloudAccountLinked
            && contact.CloudLastSyncedAt != DateTimeOffset.MinValue
            && DateTimeOffset.UtcNow - contact.CloudLastSyncedAt < TimeSpan.FromMinutes(6);
    }

    private static string BuildLifestreamDestination(string zone)
    {
        if (string.IsNullOrWhiteSpace(zone) || zone.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var district = ExtractResidentialDistrict(zone);
        if (!string.IsNullOrWhiteSpace(district))
            return district;

        return zone
            .Replace("Private House - ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Free Company Estate - ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Apartment - ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    public static string BuildResidentialDetails(string zoneName)
    {
        if (string.IsNullOrWhiteSpace(zoneName)) return string.Empty;

        var housingNumbers = ExtractHousingNumbers(zoneName);
        var district = ExtractResidentialDistrict(zoneName);

        if (string.IsNullOrWhiteSpace(district))
            return housingNumbers;

        if (string.IsNullOrWhiteSpace(housingNumbers))
            return string.Empty;

        return $"{district} - {housingNumbers}";
    }

    private static string ExtractHousingNumbers(string zoneName)
    {
        var ward = ExtractNumberAfter(zoneName, "Ward");
        if (string.IsNullOrWhiteSpace(ward)) ward = ExtractNumberAfter(zoneName, "w");

        var plot = ExtractNumberAfter(zoneName, "Plot");
        if (string.IsNullOrWhiteSpace(plot)) plot = ExtractNumberAfter(zoneName, "p");

        if (!string.IsNullOrWhiteSpace(ward) && !string.IsNullOrWhiteSpace(plot)) return $"w{ward} - p{plot}";
        if (!string.IsNullOrWhiteSpace(ward)) return $"w{ward}";
        if (!string.IsNullOrWhiteSpace(plot)) return $"p{plot}";
        return string.Empty;
    }

    private static string ExtractNumberAfter(string text, string marker)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(marker)) return string.Empty;

        var comparison = StringComparison.OrdinalIgnoreCase;
        var index = -1;
        while (true)
        {
            index = text.IndexOf(marker, index + 1, comparison);
            if (index < 0) return string.Empty;

            var isShortMarker = marker.Length == 1;
            var validPrefix = !isShortMarker || index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            if (!validPrefix)
                continue;

            var numberStart = index + marker.Length;
            while (numberStart < text.Length && !char.IsDigit(text[numberStart]))
                numberStart++;

            if (numberStart >= text.Length)
                continue;

            var numberEnd = numberStart;
            while (numberEnd < text.Length && char.IsDigit(text[numberEnd]))
                numberEnd++;

            return text[numberStart..numberEnd];
        }
    }

    private static string ExtractResidentialDistrict(string zoneName)
    {
        var text = zoneName.Trim();

        if (text.Contains("Mist", StringComparison.OrdinalIgnoreCase)) return "Mist";
        if (text.Contains("Lavender", StringComparison.OrdinalIgnoreCase) || text.Contains("Lavander", StringComparison.OrdinalIgnoreCase)) return "Lavender Beds";
        if (text.Contains("Goblet", StringComparison.OrdinalIgnoreCase)) return "Goblet";
        if (text.Contains("Shirogane", StringComparison.OrdinalIgnoreCase)) return "Shirogane";
        if (text.Contains("Empyreum", StringComparison.OrdinalIgnoreCase)) return "Empyreum";

        if (!text.Contains("House", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("Estate", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("Apartment", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var separators = new[] { " - ", ": " };
        foreach (var separator in separators)
        {
            var index = text.LastIndexOf(separator, StringComparison.Ordinal);
            if (index >= 0 && index + separator.Length < text.Length)
                return text[(index + separator.Length)..].Trim();
        }

        return text;
    }

    public string ResolveCurrentZoneName()
        => ResolveCurrentLocation().Zone;

    private GameLocationSnapshot ResolveCurrentLocation()
        => GameLocationResolver.GetCurrent(dataManager, clientState);

    private IPlayerCharacter? FindPlayerFromContext(MenuTargetDefault target)
    {
        return objectTable.OfType<IPlayerCharacter>().FirstOrDefault(player =>
            player.GameObjectId == target.TargetObjectId ||
            (string.Equals(CleanName(player.Name.ToString()), CleanName(target.TargetName), StringComparison.OrdinalIgnoreCase) &&
             GetPlayerHomeWorldId(player) == target.TargetHomeWorld.RowId));
    }

    private uint GetPlayerHomeWorldId(IPlayerCharacter player)
    {
        try
        {
            return player.HomeWorld.RowId;
        }
        catch
        {
            return 0;
        }
    }

    private uint GetPlayerCurrentWorldId(IPlayerCharacter player)
    {
        try
        {
            var currentWorld = player.GetType()
                .GetProperty("CurrentWorld", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(player);

            var rowId = currentWorld?.GetType()
                .GetProperty("RowId", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(currentWorld);

            var currentWorldId = rowId switch
            {
                uint u => u,
                ushort us => us,
                int i when i > 0 => (uint)i,
                _ => 0u,
            };

            if (currentWorldId != 0) return currentWorldId;
        }
        catch
        {
            // Older API surfaces may not expose CurrentWorld here.
        }

        var localCurrentWorldId = ResolveLocalCurrentWorldId();
        return localCurrentWorldId != 0 ? localCurrentWorldId : GetPlayerHomeWorldId(player);
    }

    private uint ResolveLocalCurrentWorldId()
    {
        try
        {
            var localPlayer = clientState.GetType()
                .GetProperty("LocalPlayer", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(clientState);

            var currentWorld = localPlayer?.GetType()
                .GetProperty("CurrentWorld", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(localPlayer);

            var rowId = currentWorld?.GetType()
                .GetProperty("RowId", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(currentWorld);

            return rowId switch
            {
                uint u => u,
                ushort us => us,
                int i when i > 0 => (uint)i,
                _ => 0u,
            };
        }
        catch
        {
            return 0u;
        }
    }

    private string ResolveWorldName(uint worldId)
    {
        if (worldId == 0) return string.Empty;

        try
        {
            var world = dataManager.GetExcelSheet<World>().GetRow(worldId);
            return world.Name.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private string ResolveDataCenterName(uint worldId)
    {
        if (worldId == 0) return string.Empty;

        try
        {
            var world = dataManager.GetExcelSheet<World>().GetRow(worldId);
            return world.DataCenter.Value.Name.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private ContactStatus ResolveStatus(IPlayerCharacter player)
    {
        var statusText = ReadOnlineStatusText(player);
        if (statusText.Contains("busy", StringComparison.OrdinalIgnoreCase) ||
            statusText.Contains("do not disturb", StringComparison.OrdinalIgnoreCase) ||
            statusText.Contains("ocupado", StringComparison.OrdinalIgnoreCase))
            return ContactStatus.Busy;

        return ContactStatus.Online;
    }

    private static string ReadOnlineStatusText(object player)
    {
        try
        {
            var value = player.GetType().GetProperty("OnlineStatus", BindingFlags.Public | BindingFlags.Instance)?.GetValue(player);
            if (value == null) return string.Empty;

            var name = value.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(value)?.ToString();
            if (!string.IsNullOrWhiteSpace(name)) return name;

            return value.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string CleanName(string name)
    {
        return name
            .Replace("\uE05D", string.Empty, StringComparison.Ordinal)
            .Replace("\uE05E", string.Empty, StringComparison.Ordinal)
            .Trim();
    }
}
