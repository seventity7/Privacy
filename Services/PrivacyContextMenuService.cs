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
        IPluginLog log)
    {
        this.contextMenu = contextMenu;
        this.pluginInterface = pluginInterface;
        this.config = config;
        this.privacyService = privacyService;
        this.chatGui = chatGui;
        this.objectTable = objectTable;
        this.openMainWindow = openMainWindow;
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

            args.AddMenuItem(new MenuItem
            {
                Name = "Add to privacy",
                PrefixChar = 'P',
                UseDefaultPrefix = false,
                PrefixColor = PrefixColor,
                OnClicked = _ => AddTarget(target),
            });
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

    private void AddTarget(MenuTargetDefault target)
    {
        if (privacyService.AddFromContextTarget(target, out _, out var message))
        {
            chatGui.Print($"[Privacy] {message}");
            if (config.OpenWindowAfterAdd) openMainWindow();
            return;
        }

        chatGui.PrintError($"[Privacy] {message}");
    }
}
