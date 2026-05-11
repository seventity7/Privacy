using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Privacy.Models;
using Privacy.UI;
using System.Numerics;

namespace Privacy.Windows;

internal sealed class NotesWindow : Window
{
    private readonly Configuration config;
    private readonly IPluginLog log;
    private PrivateContact? contact;
    private string buffer = string.Empty;
    private int pushedColorCount;
    private int pushedStyleVarCount;

    public NotesWindow(Configuration config, IPluginLog log)
        : base("Privacy Notes###PrivacyNotes")
    {
        this.config = config;
        this.log = log;

        Size = new Vector2(473f, 360f);
        SizeCondition = ImGuiCond.FirstUseEver;

        WindowBuilder.For(this)
            .AllowPinning(true)
            .AllowClickthrough(false)
            .SetSizeConstraints(new Vector2(380f, 260f), new Vector2(900f, 900f))
            .AddFlags(ImGuiWindowFlags.NoDocking)
            .Apply();
    }

    public void Open(PrivateContact selected)
    {
        contact = selected;
        buffer = selected.Notes ?? string.Empty;
        WindowName = $"Notes - {selected.DisplayName}###PrivacyNotes";
        IsOpen = true;
        log.Information("Privacy: opening notes window for {Name}@{World}.", selected.Name, selected.World);
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
        
        WindowEdgeFade.DrawUnified(config.WindowBackgroundColor, config.AccentColor);
var drawList = ImGui.GetWindowDrawList();
        var headerPos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var headerHeight = 58f * ImGuiHelpers.GlobalScale;

        var headerTop = new Vector4(
            MathF.Min(1f, config.WindowBackgroundColor.X + 0.028f),
            MathF.Min(1f, config.WindowBackgroundColor.Y + 0.028f),
            MathF.Min(1f, config.WindowBackgroundColor.Z + 0.028f),
            MathF.Min(1f, MathF.Max(0.38f, config.WindowBackgroundColor.W)));
        var headerBottom = new Vector4(config.WindowBackgroundColor.X, config.WindowBackgroundColor.Y, config.WindowBackgroundColor.Z, 0f);

        drawList.AddRectFilledMultiColor(
            headerPos,
            headerPos + new Vector2(width, headerHeight),
            ImGui.GetColorU32(headerTop),
            ImGui.GetColorU32(headerTop),
            ImGui.GetColorU32(headerBottom),
            ImGui.GetColorU32(headerBottom));

        var title = contact == null ? "Privacy Notes" : $"Notes - {contact.DisplayName}";
        DrawTextWithShadow(drawList, headerPos + new Vector2(20f, 13f) * ImGuiHelpers.GlobalScale, ImGui.GetColorU32(config.AccentColor), title, 20f * ImGuiHelpers.GlobalScale);
        ImGui.Dummy(new Vector2(width, headerHeight));

        using var child = ImRaii.Child("notes-body", new Vector2(-1f, -1f), false);
        if (!child) return;

        if (contact == null)
        {
            ImGui.TextUnformatted("No contact selected.");
            return;
        }

        ImGui.TextColored(config.AccentColor, string.IsNullOrWhiteSpace(contact.World) ? contact.DisplayName : $"{contact.Name}@{contact.World}");
        ImGui.TextDisabled("This note is also used as the name tooltip in the main list.");
        ImGui.Separator();

        var size = new Vector2(-1f, ImGui.GetContentRegionAvail().Y - 38f * ImGuiHelpers.GlobalScale);
        ImGui.InputTextMultiline("##privacy_notes", ref buffer, 8192, size);

        if (ImGui.Button("Save", new Vector2(100f, 0f) * ImGuiHelpers.GlobalScale))
        {
            contact.Notes = buffer;
            config.Save();
            log.Information("Privacy: saved notes for {Name}@{World}; length={Length}.", contact.Name, contact.World, buffer.Length);
        }

        ImGui.SameLine();
        if (ImGui.Button("Close", new Vector2(100f, 0f) * ImGuiHelpers.GlobalScale))
        {
            IsOpen = false;
        }
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
