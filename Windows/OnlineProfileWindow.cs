
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Privacy.Models;
using Privacy.Services;
using Privacy.UI;
using System;
using System.Numerics;

namespace Privacy.Windows;

internal sealed class OnlineProfileWindow : Window
{
    private readonly Configuration config;
    private readonly ProfileImageCache profileImages;
    private PrivateContact? contact;
    private int pushedColorCount;
    private int pushedStyleVarCount;

    public OnlineProfileWindow(Configuration config, ProfileImageCache profileImages)
        : base("Online Profile###PrivacyOnlineProfile")
    {
        this.config = config;
        this.profileImages = profileImages;
        Size = new Vector2(430f, 420f);
        SizeCondition = ImGuiCond.FirstUseEver;

        WindowBuilder.For(this)
            .AllowPinning(true)
            .AllowClickthrough(false)
            .SetSizeConstraints(new Vector2(390f, 300f), new Vector2(620f, 720f))
            .AddFlags(ImGuiWindowFlags.NoDocking)
            .Apply();
    }

    public void Open(PrivateContact selectedContact)
    {
        contact = selectedContact;
        IsOpen = true;
    }

    public override void PreDraw()
    {
        pushedColorCount = 0;
        pushedStyleVarCount = 0;

        PushColor(ImGuiCol.Text, UiColors.Text);
        PushColor(ImGuiCol.TextDisabled, UiColors.TextDim);
        PushColor(ImGuiCol.WindowBg, Vector4.Zero);
        PushColor(ImGuiCol.ChildBg, Vector4.Zero);
        PushColor(ImGuiCol.Border, Vector4.Zero);
        PushColor(ImGuiCol.FrameBg, UiColors.Get("PrivateFrameBg"));
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
DrawHeader();

        using var body = ImRaii.Child("online-profile-body", new Vector2(-1f, -1f), false);
        if (!body) return;

        if (contact == null)
        {
            ImGui.TextDisabled("No profile selected.");
            return;
        }

        DrawProfile(contact);
    }

    private void DrawHeader()
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = 52f * ImGuiHelpers.GlobalScale;
        var top = new Vector4(
            MathF.Min(1f, config.WindowBackgroundColor.X + 0.028f),
            MathF.Min(1f, config.WindowBackgroundColor.Y + 0.028f),
            MathF.Min(1f, config.WindowBackgroundColor.Z + 0.028f),
            MathF.Min(1f, MathF.Max(0.38f, config.WindowBackgroundColor.W)));
        var bottom = new Vector4(config.WindowBackgroundColor.X, config.WindowBackgroundColor.Y, config.WindowBackgroundColor.Z, 0f);

        drawList.AddRectFilledMultiColor(pos, pos + new Vector2(width, height), ImGui.GetColorU32(top), ImGui.GetColorU32(top), ImGui.GetColorU32(bottom), ImGui.GetColorU32(bottom));
        DrawTextWithShadow(drawList, pos + new Vector2(20f, 12f) * ImGuiHelpers.GlobalScale, ImGui.GetColorU32(config.AccentColor), "Online Profile", 20f * ImGuiHelpers.GlobalScale);
        ImGui.Dummy(new Vector2(width, height));
    }

    private void DrawProfile(PrivateContact profile)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();

        var avatarSize = new Vector2(92f, 92f) * scale;
        var avatarMin = ImGui.GetCursorScreenPos();
        var avatarMax = avatarMin + avatarSize;
        var texture = profileImages.GetTexture(profile.ProfileImagePath);

        drawList.AddRectFilled(avatarMin, avatarMax, ImGui.GetColorU32(new Vector4(0.06f, 0.09f, 0.08f, 0.92f)), 6f * scale);
        if (texture != null)
            drawList.AddImageRounded(texture.Handle, avatarMin, avatarMax, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), 6f * scale);
        else
            drawList.AddText(avatarMin + new Vector2(34f, 34f) * scale, ImGui.GetColorU32(UiColors.TextDim), "?");
        drawList.AddRect(avatarMin, avatarMax, ImGui.GetColorU32(new Vector4(config.AccentColor.X, config.AccentColor.Y, config.AccentColor.Z, 0.45f)), 6f * scale);

        ImGui.SameLine();
        ImGui.BeginGroup();
        DrawTextWithShadow(drawList, ImGui.GetCursorScreenPos(), ImGui.GetColorU32(UiColors.Text), profile.CloudDisplayName.Length > 0 ? profile.CloudDisplayName : profile.DisplayName, 20f * scale);
        ImGui.Dummy(new Vector2(1f, 24f * scale));
        ImGui.TextDisabled($"{profile.Name}@{profile.World}");
        ImGui.TextDisabled(profile.Status == ContactStatus.Offline ? "Offline" : "Online");
        ImGui.EndGroup();

        ImGui.Dummy(new Vector2(1f, 8f * scale));
        ImGui.Separator();

        ImGui.TextColored(config.AccentColor, "Status");
        ImGui.TextWrapped(string.IsNullOrWhiteSpace(profile.CloudStatusMessage) ? "No status message set." : profile.CloudStatusMessage);

        ImGui.Spacing();
        ImGui.TextColored(config.AccentColor, "Bio");
        ImGui.TextWrapped(string.IsNullOrWhiteSpace(profile.CloudBio) ? "No bio set." : profile.CloudBio);

        ImGui.Spacing();
        ImGui.TextColored(config.AccentColor, "Location");
        ImGui.TextDisabled(string.IsNullOrWhiteSpace(profile.DisplayLocation) ? "Unknown location." : profile.DisplayLocation);
        if (!string.IsNullOrWhiteSpace(profile.ResidentialDetails))
            ImGui.TextDisabled(profile.ResidentialDetails);

        ImGui.Spacing();
        ImGui.TextDisabled(profile.CloudLastSyncedAt == DateTimeOffset.MinValue
            ? "Cloud profile has not been synced recently."
            : $"Last synced: {profile.CloudLastSyncedAt.LocalDateTime:g}");
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
