using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Linq;

namespace Privacy.Services;

internal sealed class PrivacyContextMenuService : IDisposable
{
    private readonly IContextMenu contextMenu;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly Configuration config;
    private readonly PrivacyService privacyService;
    private readonly IChatGui chatGui;
    private readonly IObjectTable objectTable;
    private readonly Action openMainWindow;
    private readonly Action<Privacy.Models.PrivateContact> openOnlineProfile;
    private readonly IPluginLog log;

    private const ushort PrefixColor = 45;

    public PrivacyContextMenuService(
        IContextMenu contextMenu,
        IDalamudPluginInterface pluginInterface,
        Configuration config,
        PrivacyService privacyService,
        IChatGui chatGui,
        IObjectTable objectTable,
        Action openMainWindow,
        Action<Privacy.Models.PrivateContact> openOnlineProfile,
        IPluginLog log)
    {
        this.contextMenu = contextMenu;
        this.pluginInterface = pluginInterface;
        this.config = config;
        this.privacyService = privacyService;
        this.chatGui = chatGui;
        this.objectTable = objectTable;
        this.openMainWindow = openMainWindow;
        this.openOnlineProfile = openOnlineProfile;
        this.log = log;

        this.contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose()
        => contextMenu.OnMenuOpened -= OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        try
        {
            if (!pluginInterface.UiBuilder.ShouldModifyUi || !config.EnableContextMenu) return;
            if (args.Target is not MenuTargetDefault target) return;
            if (string.IsNullOrWhiteSpace(target.TargetName) || target.TargetObjectId == 0 || target.TargetHomeWorld.RowId == 0) return;
            if (!IsVisiblePlayerTarget(target)) return;

            var world = privacyService.ResolveWorldNameForContext(target.TargetHomeWorld.RowId);
            var existing = privacyService.FindExistingContact(target.TargetName, world, 0);

            args.AddMenuItem(new MenuItem
            {
                Name = existing == null ? "Add to private list" : "Remove from private list",
                PrefixChar = 'P',
                UseDefaultPrefix = false,
                PrefixColor = PrefixColor,
                OnClicked = _ => ToggleTarget(target, existing),
            });

            if (existing?.CloudAccountLinked == true)
            {
                args.AddMenuItem(new MenuItem
                {
                    Name = "Open Privacy Online Profile",
                    PrefixChar = 'P',
                    UseDefaultPrefix = false,
                    PrefixColor = PrefixColor,
                    OnClicked = _ => openOnlineProfile(existing),
                });
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Failed to add Privacy context menu item.");
        }
    }


    private bool IsVisiblePlayerTarget(MenuTargetDefault target)
    {
        try
        {
            if (target.TargetObject is IPlayerCharacter)
                return true;

            if (target.TargetObject != null)
                return false;

            return objectTable.OfType<IPlayerCharacter>()
                .Any(player => player.GameObjectId == target.TargetObjectId);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Failed to validate Privacy context menu target.");
            return false;
        }
    }

    private void ToggleTarget(MenuTargetDefault target, Privacy.Models.PrivateContact? existing)
    {
        if (existing != null)
        {
            privacyService.Remove(existing);
            chatGui.Print($"[Privacy] Removed {existing.Name}@{existing.World} from Privacy.");
            return;
        }

        if (privacyService.AddFromContextTarget(target, out _, out var message))
        {
            chatGui.Print($"[Privacy] {message}");
            if (config.OpenWindowAfterAdd) openMainWindow();
            return;
        }

        chatGui.PrintError($"[Privacy] {message}");
    }

    private void OpenTargetProfile(MenuTargetDefault target)
    {
        if (privacyService.AddFromContextTarget(target, out var contact, out var message) && contact != null)
        {
            openOnlineProfile(contact);
            return;
        }

        chatGui.PrintError($"[Privacy] {message}");
    }
}
