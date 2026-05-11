using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Privacy.Models;
using Privacy.Services;
using Privacy.UI;
using System;
using System.Numerics;

namespace Privacy.Windows;

internal sealed unsafe class EstateTeleportWindow : Window
{
    private readonly Configuration config;
    private readonly IPluginLog log;
    private PrivateContact? contact;
    private string statusMessage = string.Empty;
    private int pushedColorCount;
    private int pushedStyleVarCount;

    public EstateTeleportWindow(Configuration config, PrivacyService listService, NativeCommandExecutor nativeCommands, IPluginLog log)
        : base("Estate Teleportation###PrivacyEstateTeleport")
    {
        this.config = config;
        this.log = log;

        Size = new Vector2(473f, 230f);
        SizeCondition = ImGuiCond.FirstUseEver;

        WindowBuilder.For(this)
            .AllowPinning(true)
            .AllowClickthrough(false)
            .SetSizeConstraints(new Vector2(390f, 200f), new Vector2(640f, 420f))
            .AddFlags(ImGuiWindowFlags.NoDocking)
            .Apply();
    }

    public void Open(PrivateContact selected)
    {
        contact = selected;
        WindowName = $"Estate Teleportation - {selected.Name}###PrivacyEstateTeleport";

        if (TryOpenNativeEstateTeleport(selected))
        {
            IsOpen = false;
            return;
        }

        statusMessage = "Could not open the native Estate Teleportation window automatically.";
        IsOpen = true;
    }

    private bool TryOpenNativeEstateTeleport(PrivateContact selected)
    {
        try
        {
            log.Information("Privacy: opening native friend estate teleport for {Name}@{World}; contentId={ContentId}; homeWorldId={WorldId}.", selected.Name, selected.World, selected.ContentId, selected.WorldId);

            if (selected.ContentId == 0)
            {
                statusMessage = "This contact does not have a native friend ContentId saved yet. Open Discover once so Privacy can refresh the native friend entry.";
                log.Warning("Privacy: Estate Teleportation aborted for {Name}@{World}: missing ContentId.", selected.Name, selected.World);
                return false;
            }

            var friendListAgent = AgentFriendlist.Instance();
            if (friendListAgent == null)
            {
                statusMessage = "Could not access the native Friend List agent.";
                log.Warning("Privacy: AgentFriendlist.Instance returned null while opening Estate Teleportation for {Name}@{World}.", selected.Name, selected.World);
                return false;
            }

            friendListAgent->OpenFriendEstateTeleportation(selected.ContentId);

            log.Information("Privacy: AgentFriendlist.OpenFriendEstateTeleportation sent for {Name}@{World}; contentId={ContentId}.", selected.Name, selected.World, selected.ContentId);
            return true;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Privacy: failed to open native Estate Teleportation for {Name}@{World}.", selected.Name, selected.World);
            return false;
        }
    }

    public override void PreDraw()
    {
        pushedColorCount = 0;
        pushedStyleVarCount = 0;

        PushColor(ImGuiCol.Text, UiColors.Text);
        PushColor(ImGuiCol.TextDisabled, UiColors.TextDim);
        PushColor(ImGuiCol.WindowBg, Vector4.Zero);
        PushColor(ImGuiCol.ChildBg, Vector4.Zero);
        PushColor(ImGuiCol.PopupBg, UiColors.Get("PrivatePopupBg"));
        PushColor(ImGuiCol.Border, Vector4.Zero);
        PushColor(ImGuiCol.FrameBg, UiColors.Get("PrivateFrameBg"));
        PushColor(ImGuiCol.FrameBgHovered, UiColors.Get("PrivateFrameBgHovered"));
        PushColor(ImGuiCol.FrameBgActive, UiColors.Get("PrivateFrameBgActive"));
        PushColor(ImGuiCol.TitleBg, UiColors.Get("PrivateTitleBg"));
        PushColor(ImGuiCol.TitleBgActive, UiColors.Get("PrivateTitleBgActive"));
        PushColor(ImGuiCol.Button, UiColors.Get("ButtonDefault"));
        PushColor(ImGuiCol.ButtonHovered, config.AccentColor);
        PushColor(ImGuiCol.ButtonActive, UiColors.Get("LightlessPurpleActive"));
        PushColor(ImGuiCol.Separator, new Vector4(1f, 1f, 1f, 0.12f));
        PushColor(ImGuiCol.ScrollbarBg, UiColors.WithAlpha(config.WindowBackgroundColor, 0.16f));
        PushColor(ImGuiCol.ScrollbarGrab, UiColors.WithAlpha(config.AccentColor, 0.55f));
        PushColor(ImGuiCol.ScrollbarGrabHovered, UiColors.WithAlpha(config.AccentColor, 0.72f));
        PushColor(ImGuiCol.ScrollbarGrabActive, UiColors.WithAlpha(config.AccentColor, 0.92f));

        PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 0f) * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 4f) * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f, 6f) * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.WindowRounding, 7f * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.ChildRounding, 4f * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.FrameRounding, 4f * ImGuiHelpers.GlobalScale);
    }

    public override void PostDraw()
    {
        if (pushedStyleVarCount > 0) ImGui.PopStyleVar(pushedStyleVarCount);
        if (pushedColorCount > 0) ImGui.PopStyleColor(pushedColorCount);
    }

    public override void Draw()
    {
        
        WindowEdgeFade.DrawUnified(config.WindowBackgroundColor, config.AccentColor, UiColors.Get("HeaderGradientTop"), UiColors.Get("HeaderGradientBottom"), 52f * ImGuiHelpers.GlobalScale);
var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var scale = ImGuiHelpers.GlobalScale;
        var headerHeight = 52f * scale;

        drawList.AddRectFilledMultiColor(
            pos,
            pos + new Vector2(width, headerHeight),
            ImGui.GetColorU32(UiColors.Get("HeaderGradientTop")),
            ImGui.GetColorU32(UiColors.Get("HeaderGradientTop")),
            ImGui.GetColorU32(UiColors.Get("HeaderGradientBottom")),
            ImGui.GetColorU32(UiColors.Get("HeaderGradientBottom")));
        DrawTextWithShadow(drawList, pos + new Vector2(16f, 12f) * scale, ImGui.GetColorU32(config.AccentColor), "Estate Teleportation", 20f * scale);
        ImGui.Dummy(new Vector2(width, headerHeight));

        using var child = ImRaii.Child("estate-body", new Vector2(-1f, -1f), false);
        if (!child) return;

        if (contact == null)
        {
            ImGui.TextUnformatted("No contact selected.");
            return;
        }

        ImGui.TextColored(config.AccentColor, $"{contact.Name}@{contact.World}");
        ImGui.TextWrapped(statusMessage);
        ImGui.Spacing();
        ImGui.TextDisabled("This function must use the game's native TeleportHousingFriend addon. The estate rows are loaded by the game after the native friend estate request, so this plugin no longer shows fabricated house entries.");
        ImGui.Spacing();

        if (ImGui.Button("Try native window again", new Vector2(180f, 0f) * scale))
        {
            if (TryOpenNativeEstateTeleport(contact))
                IsOpen = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Close", new Vector2(100f, 0f) * scale))
            IsOpen = false;
    }

    private void PushColor(ImGuiCol target, Vector4 color)
    {
        ImGui.PushStyleColor(target, color);
        pushedColorCount++;
    }

    private void PushStyleVar(ImGuiStyleVar target, Vector2 value)
    {
        ImGui.PushStyleVar(target, value);
        pushedStyleVarCount++;
    }

    private void PushStyleVar(ImGuiStyleVar target, float value)
    {
        ImGui.PushStyleVar(target, value);
        pushedStyleVarCount++;
    }

    private static void DrawTextWithShadow(ImDrawListPtr drawList, Vector2 pos, uint color, string text, float size)
    {
        var font = ImGui.GetFont();
        var shadow = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.78f));
        drawList.AddText(font, size, pos + new Vector2(1.2f, 1.2f), shadow, text);
        drawList.AddText(font, size, pos, color, text);
    }
}
