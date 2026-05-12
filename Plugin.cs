using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Privacy.Services;
using Privacy.Windows;
using System;
using System.Collections.Generic;
using System.Linq;

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

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IDataManager dataManager,
        IFramework framework,
        IObjectTable objectTable,
        ITargetManager targetManager,
        IClientState clientState,
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

        if (string.IsNullOrWhiteSpace(config.CloudApiBaseUrl) ||
            string.Equals(config.CloudApiBaseUrl.Trim().TrimEnd('/'), "https://privacy-api.kkevinbhrain.workers.dev", StringComparison.OrdinalIgnoreCase))
            config.CloudApiBaseUrl = "https://REMOVED_PRIVACY_CLOUD_API_URL";

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
        cloudService = new PrivacyCloudService(config, dataManager, clientState, objectTable, chatGui, pluginLog);
        loginWindow = new LoginWindow(config, cloudService);
        myProfileWindow = new MyProfileWindow(config, cloudService, profileImages, ffxivVenuesService, dataManager, clientState);
        onlineProfileWindow = new OnlineProfileWindow(config, profileImages, ffxivVenuesService);
        notesWindow = new NotesWindow(config, pluginLog);
        settingsWindow = new SettingsWindow(config, profileImages, pluginInterface);
        estateTeleportWindow = new EstateTeleportWindow(config, privacyService, nativeCommands, pluginLog);
        contactProfileWindow = new ContactProfileWindow(config, pluginLog);
        privacyWindow = new PrivacyWindow(config, privacyService, nativeCommands, profileImages, gameIcons, friendListService, ffxivVenuesService, pluginLog, notesWindow, settingsWindow, estateTeleportWindow, contactProfileWindow, loginWindow, myProfileWindow, onlineProfileWindow);

        windowSystem.AddWindow(privacyWindow);
        windowSystem.AddWindow(loginWindow);
        windowSystem.AddWindow(myProfileWindow);
        windowSystem.AddWindow(onlineProfileWindow);
        windowSystem.AddWindow(notesWindow);
        windowSystem.AddWindow(settingsWindow);
        windowSystem.AddWindow(estateTeleportWindow);
        windowSystem.AddWindow(contactProfileWindow);

        contextMenuService = new PrivacyContextMenuService(contextMenu, pluginInterface, config, privacyService, chatGui, objectTable, () => privacyWindow.IsOpen = true, pluginLog);

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
        if (DateTime.UtcNow < nextRuntimeRefresh) return;
        nextRuntimeRefresh = DateTime.UtcNow.AddSeconds(1);

        friendListService.Refresh();
        privacyService.RefreshRuntimeState(friendListService.Friends);
        cloudService.FrameworkTick(privacyService, profileImages);
        ProcessStatusNotifications();
    }

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
            Models.ContactStatus.Idle => "Idle",
            Models.ContactStatus.Busy => "Busy",
            Models.ContactStatus.Content => "Content",
            Models.ContactStatus.Streaming => "Streaming",
            Models.ContactStatus.Online => "Online",
            _ => "Offline",
        };
}
