using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Privacy.Models;
using Privacy.Services;
using Privacy.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Privacy;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly Configuration config;
    private readonly IChatGui chatGui;
    private readonly IClientState clientState;
    private readonly WindowSystem windowSystem = new("Privacy");
    private readonly NativeCommandExecutor nativeCommands;
    private readonly PrivacyService privacyService;
    private readonly ProfileImageCache profileImages;
    private readonly GameIconCache gameIcons;
    private readonly FriendListService friendListService;
    private readonly FfxivVenuesService ffxivVenuesService;
    private readonly PrivacyCloudService cloudService;
    private readonly LoginWindow loginWindow;
    private readonly MyProfileWindow myProfileWindow;
    private readonly OnlineProfileWindow onlineProfileWindow;
    private readonly NotesWindow notesWindow;
    private readonly SettingsWindow settingsWindow;
    private readonly EstateTeleportWindow estateTeleportWindow;
    private readonly ContactProfileWindow contactProfileWindow;
    private readonly PrivacyWindow privacyWindow;
    private readonly PrivacyContextMenuService contextMenuService;

    private DateTime nextRuntimeRefresh = DateTime.MinValue;
    private readonly Dictionary<string, Models.ContactStatus> lastContactStatuses = new(StringComparer.Ordinal);
    private bool seededStatusNotifications;
    private bool wasLoggedIn;
    private DateTime loginNotificationDueAt = DateTime.MinValue;
    private bool pendingLoginNotifications;
    private bool sentLoginOnlineSummary;
    private readonly Dictionary<string, bool> shortcutWasDown = new(StringComparer.OrdinalIgnoreCase);

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IDataManager dataManager,
        IFramework framework,
        IObjectTable objectTable,
        ITargetManager targetManager,
        IClientState clientState,
        ICondition condition,
        IChatGui chatGui,
        ITextureProvider textureProvider,
        IContextMenu contextMenu,
        IPluginLog pluginLog)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.framework = framework;
        this.chatGui = chatGui;
        this.clientState = clientState;
        wasLoggedIn = clientState.IsLoggedIn;

        ConfigMigration.Run(pluginInterface);

        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Initialize(pluginInterface);

        var buildCloudApiBaseUrl = BuildSettings.CloudApiBaseUrl;
        if (string.IsNullOrWhiteSpace(config.CloudApiBaseUrl) && !string.IsNullOrWhiteSpace(buildCloudApiBaseUrl))
            config.CloudApiBaseUrl = buildCloudApiBaseUrl;

        if (!string.IsNullOrWhiteSpace(config.CloudApiBaseUrl) &&
            !string.IsNullOrWhiteSpace(buildCloudApiBaseUrl) &&
            !string.Equals(config.CloudApiBaseUrl.Trim().TrimEnd('/'), buildCloudApiBaseUrl.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            config.CloudApiBaseUrl = buildCloudApiBaseUrl;

        config.CloudEnabled = true;
        config.CloudAutoSync = true;
        config.CloudHeartbeatEnabled = true;
        config.CloudProfileLookupEnabled = true;

        nativeCommands = new NativeCommandExecutor(commandManager, pluginLog);
        privacyService = new PrivacyService(config, dataManager, objectTable, targetManager, clientState, chatGui, pluginLog);
        profileImages = new ProfileImageCache(pluginInterface, textureProvider, pluginLog);
        gameIcons = new GameIconCache(textureProvider, pluginLog);
        friendListService = new FriendListService(dataManager, pluginLog);
        ffxivVenuesService = new FfxivVenuesService(pluginInterface, pluginLog);
        ffxivVenuesService.EnsureFreshAsync();
        cloudService = new PrivacyCloudService(config, dataManager, clientState, objectTable, chatGui, pluginLog, ffxivVenuesService, condition);
        loginWindow = new LoginWindow(config, cloudService);
        myProfileWindow = new MyProfileWindow(config, cloudService, profileImages, gameIcons, ffxivVenuesService, dataManager, clientState);
        onlineProfileWindow = new OnlineProfileWindow(config, profileImages, gameIcons, ffxivVenuesService, cloudService);
        notesWindow = new NotesWindow(config, pluginLog);
        settingsWindow = new SettingsWindow(config, profileImages, pluginInterface);
        estateTeleportWindow = new EstateTeleportWindow(config, privacyService, nativeCommands, pluginLog);
        contactProfileWindow = new ContactProfileWindow(config, pluginLog);
        privacyWindow = new PrivacyWindow(config, privacyService, nativeCommands, pluginInterface, profileImages, gameIcons, friendListService, ffxivVenuesService, cloudService, pluginLog, notesWindow, settingsWindow, estateTeleportWindow, contactProfileWindow, loginWindow, myProfileWindow, onlineProfileWindow);

        windowSystem.AddWindow(privacyWindow);
        windowSystem.AddWindow(loginWindow);
        windowSystem.AddWindow(myProfileWindow);
        windowSystem.AddWindow(onlineProfileWindow);
        windowSystem.AddWindow(notesWindow);
        windowSystem.AddWindow(settingsWindow);
        windowSystem.AddWindow(estateTeleportWindow);
        windowSystem.AddWindow(contactProfileWindow);

        contextMenuService = new PrivacyContextMenuService(contextMenu, pluginInterface, config, privacyService, chatGui, objectTable, () => privacyWindow.IsOpen = true, contact => onlineProfileWindow.Open(contact), pluginLog);

        AddCommand("/plist");
        AddCommand("/privacy");
        AddCommand("/list");
        AddCommand("/plistaccount");

        pluginInterface.UiBuilder.Draw += Draw;
        pluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        pluginInterface.UiBuilder.Draw -= Draw;
        pluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;

        commandManager.RemoveHandler("/plist");
        commandManager.RemoveHandler("/privacy");
        commandManager.RemoveHandler("/list");
        commandManager.RemoveHandler("/plistaccount");

        contextMenuService.Dispose();
        cloudService.Dispose();
        ffxivVenuesService.Dispose();
        profileImages.Dispose();
        gameIcons.Dispose();
        privacyWindow.Dispose();
        windowSystem.RemoveAllWindows();
        config.Save();
    }

    private void AddCommand(string command)
    {
        commandManager.AddHandler(command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Privacy.",
        });
    }

    private void OnCommand(string command, string args)
    {
        if (string.Equals(command, "/plistaccount", StringComparison.OrdinalIgnoreCase))
        {
            loginWindow.Toggle();
            return;
        }

        privacyWindow.Toggle();
    }

    private void OpenMainUi()
        => privacyWindow.IsOpen = true;

    private void OpenConfigUi()
        => settingsWindow.IsOpen = true;

    private void Draw()
        => windowSystem.Draw();

    private void OnFrameworkUpdate(IFramework framework)
    {
        ProcessShortcuts();

        if (DateTime.UtcNow < nextRuntimeRefresh) return;
        nextRuntimeRefresh = DateTime.UtcNow.AddSeconds(1);

        friendListService.Refresh();
        privacyService.RefreshRuntimeState(friendListService.Friends);
        cloudService.FrameworkTick(privacyService, profileImages);
        ProcessStatusNotifications();
    }

    private void ProcessShortcuts()
    {
        TriggerShortcut(config.OpenMainWindowShortcut, "open-main", () => privacyWindow.IsOpen = !privacyWindow.IsOpen);
        TriggerShortcut(config.OpenTargetProfileShortcut, "open-target-profile", () =>
        {
            var contact = privacyService.GetCurrentTargetContact();
            if (contact != null && contact.CloudAccountLinked)
                ToggleOnlineProfile(contact);
        });
        TriggerShortcut(config.OpenTargetNotesShortcut, "open-target-notes", () =>
        {
            var contact = privacyService.GetCurrentTargetContact();
            if (contact != null)
                ToggleNotes(contact);
        });
    }


    private void ToggleOnlineProfile(PrivateContact contact)
    {
        if (onlineProfileWindow.IsOpen)
            onlineProfileWindow.IsOpen = false;
        else
            onlineProfileWindow.Open(contact);
    }

    private void ToggleNotes(PrivateContact contact)
    {
        if (notesWindow.IsOpen)
            notesWindow.IsOpen = false;
        else
            notesWindow.Open(contact);
    }

    private void TriggerShortcut(string shortcut, string key, Action action)
    {
        var down = IsShortcutDown(shortcut);
        shortcutWasDown.TryGetValue(key, out var wasDown);
        if (down && !wasDown)
            action();
        shortcutWasDown[key] = down;
    }

    private static bool IsShortcutDown(string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return false;
        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        foreach (var part in parts)
        {
            var vk = ParseVirtualKey(part);
            if (vk == 0 || (GetAsyncKeyState(vk) & 0x8000) == 0)
                return false;
        }

        return true;
    }

    private static int ParseVirtualKey(string key)
    {
        key = key.Trim();
        if (key.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || key.Equals("Control", StringComparison.OrdinalIgnoreCase)) return 0x11;
        if (key.Equals("Shift", StringComparison.OrdinalIgnoreCase)) return 0x10;
        if (key.Equals("Alt", StringComparison.OrdinalIgnoreCase)) return 0x12;
        if (key.Length == 1 && char.IsLetterOrDigit(key[0])) return char.ToUpperInvariant(key[0]);
        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[1..], out var fn) && fn is >= 1 and <= 24) return 0x70 + fn - 1;
        return key.ToUpperInvariant() switch
        {
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "ENTER" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            _ => 0,
        };
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private void ProcessStatusNotifications()
    {
        var isLoggedIn = clientState.IsLoggedIn;
        if (!wasLoggedIn && isLoggedIn)
        {
            pendingLoginNotifications = true;
            loginNotificationDueAt = DateTime.UtcNow.AddSeconds(8);
        }
        wasLoggedIn = isLoggedIn;

        if (!sentLoginOnlineSummary)
        {
            sentLoginOnlineSummary = true;

            if (config.NotifyOnlineCountOnLogin)
            {
                var onlineCount = config.Contacts.Count(IsOnline);
                chatGui.Print($"[Privacy] {onlineCount} Contacts Online now!");
            }
        }

        if (!seededStatusNotifications)
        {
            seededStatusNotifications = true;
            foreach (var contact in config.Contacts)
                lastContactStatuses[contact.Id] = contact.Status;
            return;
        }

        if (pendingLoginNotifications && DateTime.UtcNow >= loginNotificationDueAt)
        {
            pendingLoginNotifications = false;
            foreach (var contact in config.Contacts.Where(contact => ShouldNotify(contact) && IsOnline(contact)))
                chatGui.Print($"[Privacy] {contact.DisplayName} is {GetStatusDisplayName(contact.Status)}.");
        }

        foreach (var contact in config.Contacts.ToList())
        {
            var currentStatus = contact.Status;
            if (!lastContactStatuses.TryGetValue(contact.Id, out var previousStatus))
            {
                lastContactStatuses[contact.Id] = currentStatus;
                continue;
            }

            if (previousStatus == currentStatus)
                continue;

            lastContactStatuses[contact.Id] = currentStatus;
            if (!ShouldNotify(contact))
                continue;

            chatGui.Print(currentStatus == Models.ContactStatus.Offline
                ? $"[Privacy] {contact.DisplayName} is now Offline."
                : $"[Privacy] {contact.DisplayName} is now {GetStatusDisplayName(currentStatus)}.");
        }

        var validIds = config.Contacts.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var staleId in lastContactStatuses.Keys.Where(id => !validIds.Contains(id)).ToList())
            lastContactStatuses.Remove(staleId);
    }

    private bool ShouldNotify(Models.PrivateContact contact)
    {
        if (config.NotifyOnlyFavorites && !contact.Favorite)
            return false;

        if (contact.EnableStatusNotification)
            return true;

        if (config.NotifyFavoriteContacts && contact.Favorite)
            return true;

        return config.Groups.Any(group =>
            group.EnableStatusNotification &&
            group.ContactIds.Contains(contact.Id, StringComparer.Ordinal));
    }

    private static bool IsOnline(Models.PrivateContact contact)
        => contact.Status != Models.ContactStatus.Offline;

    private static string GetStatusDisplayName(Models.ContactStatus status)
        => status switch
        {
            Models.ContactStatus.Afk => "AFK",
            Models.ContactStatus.Busy => "Busy",
            Models.ContactStatus.Content => "Content",
            Models.ContactStatus.Streaming => "Streaming",
            Models.ContactStatus.RolePlaying => "Role-Playing",
            Models.ContactStatus.Online => "Online",
            _ => "Offline",
        };
}
