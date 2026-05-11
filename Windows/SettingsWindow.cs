using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Privacy.Services;
using Privacy.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Numerics;
using System.Security.Cryptography;

namespace Privacy.Windows;

internal sealed class SettingsWindow : Window
{
    private readonly Configuration config;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ProfileImageCache profileImages;
    private readonly FileDialogManager fileDialog = new();
    private readonly Dictionary<string, string> colorHexInputs = new(StringComparer.Ordinal);
    private string backgroundImageMessage = string.Empty;
    private string selectedBundledBackgroundPath = string.Empty;
    private string hoveredBundledBackgroundPath = string.Empty;
    private bool customBackgroundUploadedThisSession;
    private double hoveredBundledBackgroundStartedAt;
    private int bundledBackgroundCarouselIndex;
    private float bundledBackgroundCarouselOffset;
    private int activeCarouselDirection;
    private double nextCarouselRepeatAt;
    private Vector2 bundledBackgroundLeftArrowMin;
    private Vector2 bundledBackgroundLeftArrowMax;
    private Vector2 bundledBackgroundRightArrowMin;
    private Vector2 bundledBackgroundRightArrowMax;
    private bool confirmClear;
    private int pushedColorCount;
    private int pushedStyleVarCount;

    public SettingsWindow(Configuration config, ProfileImageCache profileImages, IDalamudPluginInterface pluginInterface)
        : base("Privacy Settings###PrivacySettings")
    {
        this.config = config;
        this.profileImages = profileImages;
        this.pluginInterface = pluginInterface;
        Size = new Vector2(620f, 520f);
        SizeCondition = ImGuiCond.FirstUseEver;

        WindowBuilder.For(this)
            .AllowPinning(true)
            .AllowClickthrough(false)
            .SetSizeConstraints(new Vector2(600f, 360f), new Vector2(760f, 780f))
            .AddFlags(ImGuiWindowFlags.NoDocking)
            .Apply();
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
        PushColor(ImGuiCol.FrameBgHovered, UiColors.Get("PrivateFrameBgHovered"));
        PushColor(ImGuiCol.FrameBgActive, UiColors.Get("PrivateFrameBgActive"));
        PushColor(ImGuiCol.TitleBg, UiColors.Get("PrivateTitleBg"));
        PushColor(ImGuiCol.TitleBgActive, UiColors.Get("PrivateTitleBgActive"));
        PushColor(ImGuiCol.Button, UiColors.Get("ButtonDefault"));
        PushColor(ImGuiCol.ButtonHovered, config.AccentColor);
        PushColor(ImGuiCol.ButtonActive, UiColors.Get("LightlessPurpleActive"));
        PushColor(ImGuiCol.CheckMark, config.AccentColor);
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
        fileDialog.Draw();
        if (pushedStyleVarCount > 0) ImGui.PopStyleVar(pushedStyleVarCount);
        if (pushedColorCount > 0) ImGui.PopStyleColor(pushedColorCount);
    }

    public override void Draw()
    {
        
        WindowEdgeFade.DrawUnified(config.WindowBackgroundColor, config.AccentColor);
var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var headerHeight = 58f * ImGuiHelpers.GlobalScale;
        var headerTop = new Vector4(
            MathF.Min(1f, config.WindowBackgroundColor.X + 0.028f),
            MathF.Min(1f, config.WindowBackgroundColor.Y + 0.028f),
            MathF.Min(1f, config.WindowBackgroundColor.Z + 0.028f),
            MathF.Min(1f, MathF.Max(0.38f, config.WindowBackgroundColor.W)));
        var headerBottom = new Vector4(config.WindowBackgroundColor.X, config.WindowBackgroundColor.Y, config.WindowBackgroundColor.Z, 0f);
        drawList.AddRectFilledMultiColor(
            pos,
            pos + new Vector2(width, headerHeight),
            ImGui.GetColorU32(headerTop),
            ImGui.GetColorU32(headerTop),
            ImGui.GetColorU32(headerBottom),
            ImGui.GetColorU32(headerBottom));
        DrawTextWithShadow(drawList, pos + new Vector2(20f, 13f) * ImGuiHelpers.GlobalScale, ImGui.GetColorU32(config.AccentColor), "Privacy Settings", 20f * ImGuiHelpers.GlobalScale);
        ImGui.Dummy(new Vector2(width, headerHeight));

        var changed = false;
        using var child = ImRaii.Child("settings-body", new Vector2(-1f, -1f), false);
        if (!child) return;

        changed |= ImGui.Checkbox("Enable native right-click menu item", ref config.EnableContextMenu);
        changed |= ImGui.Checkbox("Open list after adding a contact", ref config.OpenWindowAfterAdd);
        changed |= ImGui.Checkbox("Show offline contacts", ref config.ShowOfflineContacts);
        changed |= ImGui.Checkbox("Keep last known location while offline", ref config.KeepLastKnownLocationWhenOffline);
        if (!config.HighlightSameZone || !config.HighlightSameWorld)
        {
            config.HighlightSameZone = true;
            config.HighlightSameWorld = true;
            changed = true;
        }
        changed |= ImGui.Checkbox("Hide top counter bar", ref config.HideTopBar);

        ImGui.Separator();
        ImGui.TextColored(config.AccentColor, "Notifications");
        changed |= ImGui.Checkbox("Show online count on login", ref config.NotifyOnlineCountOnLogin);
        changed |= ImGui.Checkbox("Notify favorite contacts automatically", ref config.NotifyFavoriteContacts);
        changed |= ImGui.Checkbox("Only send status notifications for favorites", ref config.NotifyOnlyFavorites);

        ImGui.Separator();
        ImGui.TextColored(config.AccentColor, "Global colors");
        if (ImGui.BeginTable("global-colors-layout", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableSetupColumn("Colors", ImGuiTableColumnFlags.WidthStretch, 0.70f);
            ImGui.TableSetupColumn("Presets", ImGuiTableColumnFlags.WidthStretch, 0.30f);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            changed |= DrawColorSetting("Accent color", ref config.AccentColor);
            changed |= DrawColorSetting("Window background", ref config.WindowBackgroundColor);
            changed |= DrawColorSetting("Top bar background", ref config.TopBarBackgroundColor);
            changed |= DrawColorSetting("Bottom bar background", ref config.BottomBarBackgroundColor);
            changed |= DrawColorSetting("User row background", ref config.UserRowBackgroundColor);
            changed |= ImGui.Checkbox("Hide user row background", ref config.HideUserRowBackground);
            ImGui.SameLine();
            changed |= ImGui.Checkbox("Hide venues row background", ref config.HideVenuesRowBackground);

            ImGui.TableNextColumn();
            changed |= DrawThemePresetButtons();

            ImGui.EndTable();
        }

        ImGui.Separator();
        DrawMaintenanceButtons();
        DrawMainBackgroundImagePicker();

        if (changed)
            config.Save();
    }

    private void DrawMaintenanceButtons()
    {
        if (!confirmClear)
        {
            if (ImGui.Button("Clear all contacts", new Vector2(180f, 0f) * ImGuiHelpers.GlobalScale))
                confirmClear = true;

            ImGui.SameLine();
            if (ImGui.Button("Reset theme", new Vector2(130f, 0f) * ImGuiHelpers.GlobalScale))
            {
                ResetThemeToDefault();
                config.Save();
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.25f, 1f), "Click again to confirm clearing all contacts.");
            if (ImGui.Button("Confirm clear"))
            {
                config.Contacts.Clear();
                foreach (var group in config.Groups) group.ContactIds.Clear();
                config.Save();
                confirmClear = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                confirmClear = false;
        }

    }

    private void DrawMainBackgroundImagePicker()
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.Spacing();
        ImGui.TextColored(config.AccentColor, "Main window background");

        var applyWidth = 74f * scale;
        var removeWidth = 74f * scale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var previewSize = new Vector2(applyWidth + spacing + removeWidth, 96f * scale);
        var previewPos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var texture = profileImages.GetTexture(config.CustomMainBackgroundImagePath);

        drawList.AddRectFilled(previewPos, previewPos + previewSize, ImGui.GetColorU32(new Vector4(0.05f, 0.06f, 0.055f, 0.92f)), 4f * scale);
        if (texture != null)
            drawList.AddImageRounded(texture.Handle, previewPos, previewPos + previewSize, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), 4f * scale);
        else
            DrawTextWithShadow(drawList, previewPos + new Vector2(previewSize.X * 0.5f - 7f * scale, previewSize.Y * 0.5f - 13f * scale), ImGui.GetColorU32(UiColors.TextDim), "+", 22f * scale);

        drawList.AddRect(previewPos, previewPos + previewSize, ImGui.GetColorU32(UiColors.WithAlpha(config.AccentColor, 0.55f)), 4f * scale, ImDrawFlags.None, 1.1f * scale);
        ImGui.InvisibleButton("main-window-background-image", previewSize);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Upload an image to be the background. Max size: 577x697");

        if (ImGui.IsItemClicked())
            OpenBackgroundImagePicker();

        ImGui.SameLine();
        DrawBackgroundEffectButtons(previewSize.Y);

        ImGui.SetCursorScreenPos(new Vector2(previewPos.X, previewPos.Y + previewSize.Y + spacing));
        DrawMainBackgroundActions(previewSize.X);

        ImGui.SetCursorScreenPos(new Vector2(previewPos.X, ImGui.GetCursorScreenPos().Y + spacing));
        DrawBundledBackgroundGallery();

    }

    private void DrawBundledBackgroundGallery()
    {
        ImGui.Spacing();
        var titleLineStart = ImGui.GetCursorScreenPos();
        var titleLineWidth = ImGui.GetContentRegionAvail().X;
        ImGui.TextColored(config.AccentColor, "Image Suggestions");
        DrawBackgroundMessageOnImageSuggestionsLine(titleLineStart, titleLineWidth);

        var paths = GetBundledBackgroundImages();
        if (paths.Count == 0)
        {
            ImGui.TextDisabled("No bundled images were found in the plugin images folder.");
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var thumbSize = new Vector2(54f, 54f) * scale;
        var arrowSize = new Vector2(22f, thumbSize.Y);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var step = thumbSize.X + spacing;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var carouselWidth = MathF.Max(thumbSize.X, availableWidth - (arrowSize.X * 2f) - (spacing * 2f));
        var visibleCount = Math.Max(1, (int)MathF.Floor((carouselWidth + spacing) / step));

        if (bundledBackgroundCarouselIndex < 0 || bundledBackgroundCarouselIndex >= paths.Count)
            bundledBackgroundCarouselIndex = 0;

        var rowStart = ImGui.GetCursorScreenPos();
        bundledBackgroundLeftArrowMin = rowStart;
        bundledBackgroundLeftArrowMax = rowStart + arrowSize;
        bundledBackgroundRightArrowMin = new Vector2(rowStart.X + arrowSize.X + spacing + carouselWidth + spacing, rowStart.Y);
        bundledBackgroundRightArrowMax = bundledBackgroundRightArrowMin + arrowSize;

        DrawCarouselArrow("background_suggestions_previous", FontAwesomeIcon.ChevronLeft, -1, paths.Count, step, arrowSize);

        ImGui.SameLine();
        DrawBundledBackgroundCarousel(paths, thumbSize, carouselWidth, visibleCount, step);

        ImGui.SameLine();
        DrawCarouselArrow("background_suggestions_next", FontAwesomeIcon.ChevronRight, 1, paths.Count, step, arrowSize);

        ImGui.SetCursorScreenPos(new Vector2(rowStart.X, rowStart.Y + thumbSize.Y + spacing));
        DrawBundledBackgroundActions(availableWidth);
    }

    private void DrawBackgroundMessageOnImageSuggestionsLine(Vector2 lineStart, float lineWidth)
    {
        if (string.IsNullOrWhiteSpace(backgroundImageMessage))
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var message = backgroundImageMessage;
        var textSize = ImGui.CalcTextSize(message);
        var titleSize = ImGui.CalcTextSize("Image Suggestions");
        var minX = lineStart.X + titleSize.X + (18f * scale);
        var centeredX = lineStart.X + ((lineWidth - textSize.X) * 0.5f);
        var x = MathF.Max(minX, centeredX);
        var maxX = lineStart.X + lineWidth - textSize.X;

        if (maxX > minX)
            x = MathF.Min(x, maxX);

        DrawTextWithShadow(
            ImGui.GetWindowDrawList(),
            new Vector2(x, lineStart.Y),
            ImGui.GetColorU32(UiColors.TextDim),
            message,
            13f * scale);
    }

    private void DrawBundledBackgroundCarousel(IReadOnlyList<string> paths, Vector2 thumbSize, float carouselWidth, int visibleCount, float step)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var startPos = ImGui.GetCursorScreenPos();
        var carouselSize = new Vector2(carouselWidth, thumbSize.Y);
        var delta = ImGui.GetIO().DeltaTime;
        var speed = step * 10.0f * MathF.Max(1f, scale);

        if (bundledBackgroundCarouselOffset > 0f)
            bundledBackgroundCarouselOffset = MathF.Max(0f, bundledBackgroundCarouselOffset - speed * delta);
        else if (bundledBackgroundCarouselOffset < 0f)
            bundledBackgroundCarouselOffset = MathF.Min(0f, bundledBackgroundCarouselOffset + speed * delta);

        drawList.PushClipRect(startPos, startPos + carouselSize, true);

        var extraSlots = Math.Max(2, (int)MathF.Ceiling(MathF.Abs(bundledBackgroundCarouselOffset) / step) + 2);
        for (var slot = -extraSlots; slot < visibleCount + extraSlots; slot++)
        {
            var x = startPos.X + bundledBackgroundCarouselOffset + (slot * step);
            if (x + thumbSize.X < startPos.X || x > startPos.X + carouselWidth)
                continue;

            var path = paths[WrapIndex(bundledBackgroundCarouselIndex + slot, paths.Count)];
            DrawBundledBackgroundThumb(path, thumbSize, slot, new Vector2(x, startPos.Y));
        }

        drawList.PopClipRect();

        ImGui.SetCursorScreenPos(startPos);
        ImGui.Dummy(carouselSize);
        if (ImGui.IsMouseHoveringRect(startPos, startPos + carouselSize) && ImGui.GetIO().MouseWheel != 0f)
        {
            var direction = ImGui.GetIO().MouseWheel > 0f ? -1 : 1;
            MoveBundledBackgroundCarousel(direction, paths.Count, step);
        }
    }

    private void DrawCarouselArrow(string id, FontAwesomeIcon icon, int direction, int count, float step, Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var rectMin = pos;
        var rectMax = pos + size;
        var hovered = ImGui.IsMouseHoveringRect(rectMin, rectMax, true);
        var mouseDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var mouseReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        var now = ImGui.GetTime();

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            MoveBundledBackgroundCarousel(direction, count, step);
            activeCarouselDirection = direction;
            nextCarouselRepeatAt = now + 0.30d;
        }

        if (activeCarouselDirection == direction)
        {
            if (mouseReleased || !mouseDown)
            {
                activeCarouselDirection = 0;
                nextCarouselRepeatAt = 0d;
            }
            else if (now >= nextCarouselRepeatAt)
            {
                MoveBundledBackgroundCarousel(direction, count, step);
                nextCarouselRepeatAt = now + 0.16d;
            }
        }

        DrawThemeIcon(rectMin, size, icon, hovered || activeCarouselDirection == direction);
        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(size);

        if (hovered)
            ImGui.SetTooltip(direction < 0 ? "Previous image" : "Next image");
    }

    private void MoveBundledBackgroundCarousel(int direction, int count, float step)
    {
        if (count <= 0)
            return;

        bundledBackgroundCarouselIndex = WrapIndex(bundledBackgroundCarouselIndex + direction, count);
        bundledBackgroundCarouselOffset += direction * step;
        bundledBackgroundCarouselOffset = Math.Clamp(bundledBackgroundCarouselOffset, -step * 4f, step * 4f);
    }

    private bool IsMouseOverBundledBackgroundCarouselArrow()
    {
        return ImGui.IsMouseHoveringRect(bundledBackgroundLeftArrowMin, bundledBackgroundLeftArrowMax, true)
            || ImGui.IsMouseHoveringRect(bundledBackgroundRightArrowMin, bundledBackgroundRightArrowMax, true);
    }

    private bool DrawThemeIconButton(string id, FontAwesomeIcon icon, Vector2 size, string tooltip, bool enabled = true)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##{id}", size);

        var hovered = ImGui.IsItemHovered();
        var active = enabled && ImGui.IsItemActive();
        DrawThemeIcon(pos, size, icon, enabled && (hovered || active), active, enabled);

        if (hovered && !string.IsNullOrWhiteSpace(tooltip))
            ImGui.SetTooltip(tooltip);

        return enabled && ImGui.IsItemClicked();
    }

    private void DrawThemeIcon(Vector2 pos, Vector2 size, FontAwesomeIcon icon, bool highlighted, bool active = false, bool enabled = true)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var color = enabled ? (highlighted ? config.AccentColor : UiColors.TextDim) : UiColors.WithAlpha(UiColors.TextDim, 0.35f);
        var iconText = icon.ToIconString();

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var textSize = ImGui.CalcTextSize(iconText);
            var textPos = pos + new Vector2((size.X - textSize.X) * 0.5f, (size.Y - textSize.Y) * 0.5f);

            if (highlighted && enabled)
                drawList.AddCircleFilled(pos + size * 0.5f, MathF.Min(size.X, size.Y) * 0.36f, ImGui.GetColorU32(UiColors.WithAlpha(config.AccentColor, active ? 0.18f : 0.10f)), 32);

            drawList.AddText(UiBuilder.IconFont, 14f * scale, textPos, ImGui.GetColorU32(color), iconText);
        }
    }

    private void DrawMainBackgroundActions(float targetWidth)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var iconSize = new Vector2(30f, 26f) * scale;
        var spacing = 12f * scale;
        var totalWidth = (iconSize.X * 3f) + (spacing * 2f);
        var cursor = ImGui.GetCursorPosX();
        var offset = MathF.Max(0f, (targetWidth - totalWidth) * 0.5f);

        ImGui.SetCursorPosX(cursor + offset);
        if (DrawThemeIconButton("apply_main_background", FontAwesomeIcon.Check, iconSize, "Apply selected image"))
            ApplySelectedBackgroundImage();

        ImGui.SameLine(0f, spacing);
        if (DrawThemeIconButton("remove_main_background", FontAwesomeIcon.Times, iconSize, "Remove background image"))
            RemoveMainBackgroundImage();

        ImGui.SameLine(0f, spacing);
        var canSaveCustomImage = CanSaveCurrentBackgroundToCarousel();
        if (DrawThemeIconButton("save_custom_background_to_carousel", FontAwesomeIcon.Save, iconSize, "Save custom image to carrosel", canSaveCustomImage))
            SaveCurrentBackgroundToCarousel();
    }

    private void DrawBundledBackgroundActions(float targetWidth)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var iconSize = new Vector2(30f, 26f) * scale;
        var spacing = 14f * scale;
        var totalWidth = (iconSize.X * 2f) + spacing;
        var cursor = ImGui.GetCursorPosX();
        var offset = MathF.Max(0f, (targetWidth - totalWidth) * 0.5f);

        ImGui.SetCursorPosX(cursor + offset);
        if (DrawThemeIconButton("apply_carousel_background", FontAwesomeIcon.Check, iconSize, "Apply selected image"))
            ApplySelectedBackgroundImage();

        ImGui.SameLine(0f, spacing);
        if (DrawThemeIconButton("remove_carousel_background", FontAwesomeIcon.Times, iconSize, "Remove background image"))
            RemoveMainBackgroundImage();
    }

    private void RemoveMainBackgroundImage()
    {
        config.UseCustomMainBackgroundImage = false;
        config.CustomMainBackgroundImagePath = string.Empty;
        config.CustomBackgroundEffectName = string.Empty;
        selectedBundledBackgroundPath = string.Empty;
        customBackgroundUploadedThisSession = false;
        backgroundImageMessage = "Background image removed.";
        config.Save();
    }

    private static int WrapIndex(int index, int count)
    {
        if (count <= 0)
            return 0;

        index %= count;
        return index < 0 ? index + count : index;
    }

    private void DrawBundledBackgroundThumb(string path, Vector2 size, int visibleIndex, Vector2 position)
    {
        ImGui.SetCursorScreenPos(position);
        var texture = profileImages.GetTexture(path);
        var drawList = ImGui.GetWindowDrawList();
        var pos = position;
        var scale = ImGuiHelpers.GlobalScale;
        var selected = string.Equals(selectedBundledBackgroundPath, path, StringComparison.OrdinalIgnoreCase);

        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0.05f, 0.06f, 0.055f, 0.92f)), 4f * scale);
        if (texture != null)
            drawList.AddImageRounded(texture.Handle, pos, pos + size, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), 4f * scale);

        var borderColor = selected ? config.AccentColor : UiColors.WithAlpha(UiColors.TextDim, 0.30f);
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(borderColor), 4f * scale, ImDrawFlags.None, selected ? 2f * scale : 1f * scale);

        ImGui.InvisibleButton($"##bundled_background_{visibleIndex}_{Path.GetFileName(path)}", size);
        var arrowHovered = IsMouseOverBundledBackgroundCarouselArrow();
        if (ImGui.IsItemClicked() && !arrowHovered)
        {
            selectedBundledBackgroundPath = path;
            customBackgroundUploadedThisSession = false;
            backgroundImageMessage = "Background image selected. Click the check icon to use it.";
        }

        DrawCustomCarouselImageContextMenu(path, visibleIndex, arrowHovered);

        if (ImGui.IsItemHovered() && !arrowHovered)
            DrawBundledBackgroundTooltip(path, texture);
        else if (string.Equals(hoveredBundledBackgroundPath, path, StringComparison.OrdinalIgnoreCase))
        {
            hoveredBundledBackgroundPath = string.Empty;
            hoveredBundledBackgroundStartedAt = 0d;
        }
    }

    private void DrawCustomCarouselImageContextMenu(string path, int visibleIndex, bool arrowHovered)
    {
        if (!IsSavedCarouselBackground(path))
            return;

        var popupId = $"delete_custom_carousel_image_{visibleIndex}_{Path.GetFileNameWithoutExtension(path)}";
        if (!arrowHovered && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup(popupId);

        if (!ImGui.BeginPopup(popupId))
            return;

        if (ImGui.Selectable("Delete custom image"))
            DeleteCustomCarouselImage(path);

        ImGui.EndPopup();
    }

    private bool IsSavedCarouselBackground(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var savedDirectory = Path.GetFullPath(GetSavedCarouselBackgroundDirectory());
            var imagePath = Path.GetFullPath(path);

            return imagePath.StartsWith(savedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void DeleteCustomCarouselImage(string path)
    {
        try
        {
            if (!IsSavedCarouselBackground(path) || !File.Exists(path))
            {
                backgroundImageMessage = "Custom image was not found.";
                return;
            }

            var currentBackgroundPath = config.CustomMainBackgroundImagePath;
            var wasSelected = string.Equals(selectedBundledBackgroundPath, path, StringComparison.OrdinalIgnoreCase);
            var wasCurrentBackground = IsSameImageFile(path, currentBackgroundPath);

            File.Delete(path);
            profileImages.Invalidate(path);

            if (wasSelected)
                selectedBundledBackgroundPath = string.Empty;

            if (wasCurrentBackground)
            {
                DeleteFileIfSafe(currentBackgroundPath);
                profileImages.Invalidate(currentBackgroundPath);
                config.UseCustomMainBackgroundImage = false;
                config.CustomMainBackgroundImagePath = string.Empty;
                config.CustomBackgroundEffectName = string.Empty;
                customBackgroundUploadedThisSession = false;
                config.Save();
                backgroundImageMessage = "Custom image deleted and background reset.";
            }
            else
            {
                backgroundImageMessage = "Custom image deleted from carousel.";
            }

            hoveredBundledBackgroundPath = string.Empty;
            hoveredBundledBackgroundStartedAt = 0d;
        }
        catch (Exception)
        {
            backgroundImageMessage = "Failed to delete custom image.";
        }
    }

    private static bool IsSameImageFile(string firstPath, string secondPath)
    {
        if (string.IsNullOrWhiteSpace(firstPath) || string.IsNullOrWhiteSpace(secondPath))
            return false;

        try
        {
            if (!File.Exists(firstPath) || !File.Exists(secondPath))
                return false;

            var firstFullPath = Path.GetFullPath(firstPath);
            var secondFullPath = Path.GetFullPath(secondPath);
            if (string.Equals(firstFullPath, secondFullPath, StringComparison.OrdinalIgnoreCase))
                return true;

            var firstInfo = new FileInfo(firstFullPath);
            var secondInfo = new FileInfo(secondFullPath);
            if (firstInfo.Length != secondInfo.Length)
                return false;

            using var sha = SHA256.Create();
            using var firstStream = File.OpenRead(firstFullPath);
            using var secondStream = File.OpenRead(secondFullPath);
            var firstHash = sha.ComputeHash(firstStream);
            var secondHash = sha.ComputeHash(secondStream);

            return firstHash.SequenceEqual(secondHash);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool DeleteFileIfSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var profileDirectory = Path.GetFullPath(profileImages.ProfileImageDirectory);
            if (!fullPath.StartsWith(profileDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;

            File.Delete(fullPath);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void DrawBundledBackgroundTooltip(string path, IDalamudTextureWrap? texture)
    {
        if (texture == null)
            return;

        var now = ImGui.GetTime();
        if (!string.Equals(hoveredBundledBackgroundPath, path, StringComparison.OrdinalIgnoreCase))
        {
            hoveredBundledBackgroundPath = path;
            hoveredBundledBackgroundStartedAt = now;
            return;
        }

        if (now - hoveredBundledBackgroundStartedAt < 1d)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(180f, 180f) * scale;
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginTooltip();
        ImGui.Image(texture.Handle, size);
        ImGui.EndTooltip();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);
    }

    private void ApplySelectedBackgroundImage()
    {
        if (!string.IsNullOrWhiteSpace(selectedBundledBackgroundPath) && File.Exists(selectedBundledBackgroundPath))
        {
            if (profileImages.TryImportBackgroundImage(selectedBundledBackgroundPath, out var storedPath, out var error))
            {
                config.CustomMainBackgroundImagePath = storedPath;
                config.UseCustomMainBackgroundImage = true;
                config.ThemePresetName = string.Empty;
                customBackgroundUploadedThisSession = false;
                backgroundImageMessage = "Background image applied.";
                config.Save();
            }
            else
            {
                backgroundImageMessage = error;
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(config.CustomMainBackgroundImagePath) && File.Exists(config.CustomMainBackgroundImagePath))
        {
            config.UseCustomMainBackgroundImage = true;
            config.ThemePresetName = string.Empty;
            backgroundImageMessage = "Background image applied.";
            config.Save();
        }
        else
        {
            backgroundImageMessage = "Select or upload an image before applying.";
        }
    }

    private List<string> GetBundledBackgroundImages()
    {
        var extensions = GetSupportedBackgroundExtensions();
        var images = new List<string>();

        foreach (var directory in GetBundledImageDirectories())
        {
            if (!Directory.Exists(directory))
                continue;

            images.AddRange(Directory.EnumerateFiles(directory)
                .Where(path => extensions.Contains(Path.GetExtension(path))));
        }

        images.AddRange(ExtractEmbeddedBackgroundImages(extensions));

        return images
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HashSet<string> GetSupportedBackgroundExtensions()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".webp",
        };
    }

    private IEnumerable<string> GetBundledImageDirectories()
    {
        var assemblyDirectory = Path.GetDirectoryName(pluginInterface.AssemblyLocation.FullName) ?? AppContext.BaseDirectory;

        yield return Path.Combine(assemblyDirectory, "images");
        yield return Path.Combine(assemblyDirectory, "Privacy", "images");
        yield return Path.Combine(AppContext.BaseDirectory, "images");
        yield return Path.Combine(AppContext.BaseDirectory, "Privacy", "images");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "images");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "Privacy", "images");
        yield return GetSavedCarouselBackgroundDirectory();
    }

    private List<string> ExtractEmbeddedBackgroundImages(HashSet<string> extensions)
    {
        var extracted = new List<string>();
        var assembly = Assembly.GetExecutingAssembly();
        var outputDirectory = Path.Combine(pluginInterface.ConfigDirectory.FullName, "BundledBackgrounds");

        Directory.CreateDirectory(outputDirectory);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.Contains(".images.", StringComparison.OrdinalIgnoreCase))
                continue;

            var extension = extensions.FirstOrDefault(resourceName.EndsWith);
            if (extension == null)
                continue;

            var fileName = GetEmbeddedImageFileName(resourceName, extension);
            var targetPath = Path.Combine(outputDirectory, fileName);

            using var input = assembly.GetManifestResourceStream(resourceName);
            if (input == null)
                continue;

            using var output = File.Create(targetPath);
            input.CopyTo(output);
            extracted.Add(targetPath);
        }

        return extracted;
    }

    private static string GetEmbeddedImageFileName(string resourceName, string extension)
    {
        var withoutExtension = resourceName[..^extension.Length];
        var name = withoutExtension.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "background";

        foreach (var character in Path.GetInvalidFileNameChars())
            name = name.Replace(character, '_');

        return $"{name}{extension}";
    }

    private void DrawBackgroundEffectButtons(float previewHeight)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var hasImage = config.UseCustomMainBackgroundImage &&
            !string.IsNullOrWhiteSpace(config.CustomMainBackgroundImagePath) &&
            File.Exists(config.CustomMainBackgroundImagePath);

        var effects = new[]
        {
            ("Red", "All Red", "#FF4444"),
            ("Gold", "All Gold", "#FFC845"),
            ("White", "White Gold", "#FFECA6"),
            ("Choc", "Chocolate", "#B57345"),
            ("Cyber", "Cyberpunk", "#CF55FF"),
            ("Water", "Water", "#5EDBFF"),
            ("Fire", "Fire", "#FF872F"),
            ("Saku", "Sakura", "#FF9AC6"),
            ("Aut", "Autumn", "#E66F25"),
            ("Frost", "Frost", "#C9F8FF"),
            ("Stars", "Stars", "#D9D6FF"),
            ("Grid", "Grid", "#5AFFC8"),
        };

        var buttonSize = new Vector2(46f, 24f) * scale;
        var colorSize = new Vector2(17f, 17f) * scale;
        var itemSpacing = 3.5f * scale;
        var rowSpacing = 8f * scale;
        var start = ImGui.GetCursorScreenPos();

        ImGui.BeginGroup();
        for (var i = 0; i < effects.Length; i++)
        {
            if (i == 6)
                ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + buttonSize.Y + rowSpacing));
            else if (i > 0)
                ImGui.SameLine(0f, itemSpacing);

            DrawBackgroundEffectButton(effects[i].Item1, effects[i].Item2, effects[i].Item3, buttonSize, colorSize, hasImage);
        }
        ImGui.EndGroup();

        var usedHeight = (buttonSize.Y * 2f) + rowSpacing;
        if (previewHeight > usedHeight)
            ImGui.Dummy(new Vector2(1f, previewHeight - usedHeight));
    }

    private void DrawBackgroundEffectButton(string label, string effectName, string defaultHex, Vector2 buttonSize, Vector2 colorSize, bool hasImage)
    {
        var colorHex = GetBackgroundEffectColorHex(effectName, defaultHex);
        var color = UiColors.HexToRgba(colorHex);
        var selected = string.Equals(config.CustomBackgroundEffectName, effectName, StringComparison.Ordinal);

        using var disabled = ImRaii.Disabled(!hasImage);
        if (ImGui.Button(label, buttonSize) && hasImage)
        {
            config.CustomBackgroundEffectName = selected ? string.Empty : effectName;
            if (!selected)
                config.ThemePresetName = string.Empty;
            config.Save();
        }

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        if (selected && hasImage)
            ImGui.GetWindowDrawList().AddRect(min - new Vector2(1f), max + new Vector2(1f), ImGui.GetColorU32(color), 4f * ImGuiHelpers.GlobalScale, ImDrawFlags.None, 1.6f * ImGuiHelpers.GlobalScale);

        if (!hasImage && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Upload a background image first");

        ImGui.SameLine(0f, 2f * ImGuiHelpers.GlobalScale);
        DrawEffectColorSquare(effectName, defaultHex, colorSize, hasImage);
    }

    private void DrawEffectColorSquare(string effectName, string defaultHex, Vector2 size, bool hasImage)
    {
        var colorHex = GetBackgroundEffectColorHex(effectName, defaultHex);
        var color = UiColors.HexToRgba(colorHex);
        var popupId = $"##bg_effect_{effectName}_picker";

        using var disabled = ImRaii.Disabled(!hasImage);
        if (ImGui.ColorButton($"##bg_effect_{effectName}_color", color, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, size) && hasImage)
            ImGui.OpenPopup(popupId);

        if (!hasImage && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Upload a background image first");

        if (ImGui.BeginPopup(popupId))
        {
            var picker = new Vector3(color.X, color.Y, color.Z);
            if (ImGui.ColorPicker3($"##bg_effect_{effectName}_value", ref picker, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoLabel))
            {
                config.CustomBackgroundEffectColorHex[effectName] = HexFromColor(new Vector4(picker.X, picker.Y, picker.Z, 1f), includeAlpha: false);
                config.Save();
            }
            ImGui.EndPopup();
        }
    }

    private string GetBackgroundEffectColorHex(string effectName, string fallback)
    {
        if (config.CustomBackgroundEffectColorHex.TryGetValue(effectName, out var value) && TryHexToColor(value, 1f, out _))
            return value;

        config.CustomBackgroundEffectColorHex[effectName] = fallback;
        return fallback;
    }

    private string GetSavedCarouselBackgroundDirectory()
    {
        return Path.Combine(pluginInterface.ConfigDirectory.FullName, "SavedCarouselBackgrounds");
    }

    private bool CanSaveCurrentBackgroundToCarousel()
    {
        return customBackgroundUploadedThisSession
            && !string.IsNullOrWhiteSpace(config.CustomMainBackgroundImagePath)
            && File.Exists(config.CustomMainBackgroundImagePath);
    }

    private void SaveCurrentBackgroundToCarousel()
    {
        if (!CanSaveCurrentBackgroundToCarousel())
        {
            backgroundImageMessage = "Upload a custom background image before saving it to the carousel.";
            return;
        }

        try
        {
            var sourcePath = config.CustomMainBackgroundImagePath;
            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (extension == ".jpeg")
                extension = ".jpg";

            if (!GetSupportedBackgroundExtensions().Contains(extension))
            {
                backgroundImageMessage = "Only PNG, JPG, JPEG and WEBP images are supported.";
                return;
            }

            var directory = GetSavedCarouselBackgroundDirectory();
            Directory.CreateDirectory(directory);

            var fileName = $"custom_background_{DateTimeOffset.Now:yyyyMMdd_HHmmssfff}{extension}";
            var targetPath = Path.Combine(directory, fileName);
            File.Copy(sourcePath, targetPath, overwrite: false);

            selectedBundledBackgroundPath = targetPath;
            customBackgroundUploadedThisSession = false;
            backgroundImageMessage = "Custom image saved to carousel.";
        }
        catch (Exception)
        {
            backgroundImageMessage = "Failed to save custom image to carousel.";
        }
    }

    private void OpenBackgroundImagePicker()
    {
        fileDialog.OpenFileDialog(
            "Choose main window background",
            "Image files{.png,.jpg,.jpeg,.webp}",
            (success, paths) =>
            {
                if (!success || paths.Count == 0)
                    return;

                if (profileImages.TryImportBackgroundImage(paths[0], out var storedPath, out var error))
                {
                    config.CustomMainBackgroundImagePath = storedPath;
                    selectedBundledBackgroundPath = string.Empty;
                    customBackgroundUploadedThisSession = true;
                    config.Save();
                    backgroundImageMessage = "Background image ready. Click Apply to use it.";
                }
                else
                {
                    backgroundImageMessage = error;
                }
            },
            1,
            null,
            true);
    }

    private void ResetThemeToDefault()
    {
        config.AccentColor = new Vector4(0.16f, 0.86f, 0.67f, 1f);
        config.WindowBackgroundColor = new Vector4(0.010f, 0.020f, 0.018f, 0.88f);
        config.TopBarBackgroundColor = new Vector4(0.188f, 0.200f, 0.192f, 0.34f);
        config.BottomBarBackgroundColor = new Vector4(0.188f, 0.200f, 0.192f, 0.34f);
        config.UserRowBackgroundColor = new Vector4(0.067f, 0.110f, 0.098f, 0.66f);
        config.HideUserRowBackground = false;
        config.ThemePresetName = "Default";
        config.CustomBackgroundEffectName = string.Empty;
    }

    private bool DrawColorSetting(string label, ref Vector4 color)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var changed = false;
        var squareSize = new Vector2(24f, 24f) * scale;
        var popupId = $"##{label}_color_picker";
        var inputId = label.Replace(" ", "_", StringComparison.OrdinalIgnoreCase);
        var currentHex = HexFromColor(color, includeAlpha: true);

        if (!ImGui.IsPopupOpen(popupId))
            colorHexInputs[inputId] = currentHex;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine(185f * scale);

        var flags = ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop;
        if (ImGui.ColorButton($"##{label}_color_square", color, flags, squareSize))
        {
            colorHexInputs[inputId] = currentHex;
            ImGui.OpenPopup(popupId);
        }

        if (ImGui.BeginPopup(popupId))
        {
            var picker = new Vector3(color.X, color.Y, color.Z);
            if (ImGui.ColorPicker3($"##{label}_picker", ref picker, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoLabel))
            {
                color = new Vector4(picker.X, picker.Y, picker.Z, color.W);
                colorHexInputs[inputId] = HexFromColor(color, includeAlpha: true);
                changed = true;
            }

            ImGui.Separator();
            ImGui.TextDisabled("HEX");
            ImGui.SameLine();

            var hex = colorHexInputs.TryGetValue(inputId, out var stored) ? stored : currentHex;
            ImGui.SetNextItemWidth(112f * scale);
            if (ImGui.InputText($"##{label}_hex", ref hex, 9))
            {
                colorHexInputs[inputId] = NormalizeHexText(hex);
                if (TryHexToColor(colorHexInputs[inputId], color.W, out var parsed))
                {
                    color = parsed;
                    changed = true;
                }
            }

            ImGui.SameLine();
            if (ImGui.Button($"Copy##{label}_hex_copy"))
                ImGui.SetClipboardText(colorHexInputs.TryGetValue(inputId, out var copyHex) ? copyHex : currentHex);

            ImGui.EndPopup();
        }

        return changed;
    }

    private bool DrawThemePresetButtons()
    {
        var changed = false;
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TextDisabled("Theme presets");

        var buttonWidth = MathF.Max(96f * scale, ImGui.GetContentRegionAvail().X * 0.48f);
        var buttonSize = new Vector2(buttonWidth, 24f * scale);

        changed |= ThemePresetButton("All Red", buttonSize, () => ApplyThemePreset(
            new Vector4(1.00f, 0.28f, 0.30f, 1f),
            new Vector4(0.080f, 0.018f, 0.022f, 0.90f),
            new Vector4(0.42f, 0.08f, 0.10f, 0.44f),
            new Vector4(0.30f, 0.06f, 0.08f, 0.40f),
            new Vector4(0.20f, 0.045f, 0.052f, 0.68f)));

        ImGui.SameLine();
        changed |= ThemePresetButton("All Gold", buttonSize, () => ApplyThemePreset(
            new Vector4(1.00f, 0.78f, 0.27f, 1f),
            new Vector4(0.078f, 0.055f, 0.020f, 0.90f),
            new Vector4(0.42f, 0.30f, 0.10f, 0.44f),
            new Vector4(0.30f, 0.22f, 0.08f, 0.40f),
            new Vector4(0.20f, 0.145f, 0.055f, 0.68f)));

        changed |= ThemePresetButton("White Gold", buttonSize, () => ApplyThemePreset(
            new Vector4(1.00f, 0.84f, 0.34f, 1f),
            new Vector4(0.118f, 0.112f, 0.096f, 0.92f),
            new Vector4(1.00f, 0.94f, 0.72f, 0.32f),
            new Vector4(0.74f, 0.58f, 0.22f, 0.34f),
            new Vector4(1.00f, 0.95f, 0.74f, 0.20f)));

        ImGui.SameLine();
        changed |= ThemePresetButton("Chocolate", buttonSize, () => ApplyThemePreset(
            new Vector4(0.86f, 0.56f, 0.35f, 1f),
            new Vector4(0.072f, 0.038f, 0.020f, 0.92f),
            new Vector4(0.32f, 0.17f, 0.09f, 0.48f),
            new Vector4(0.24f, 0.13f, 0.07f, 0.44f),
            new Vector4(0.18f, 0.09f, 0.045f, 0.70f)));

        changed |= ThemePresetButton("Cyberpunk", buttonSize, () => ApplyThemePreset(
            new Vector4(0.78f, 0.34f, 1.00f, 1f),
            new Vector4(0.018f, 0.012f, 0.055f, 0.95f),
            new Vector4(0.12f, 0.84f, 1.00f, 0.30f),
            new Vector4(1.00f, 0.20f, 0.78f, 0.26f),
            new Vector4(0.070f, 0.045f, 0.180f, 0.74f)));

        ImGui.SameLine();
        changed |= ThemePresetButton("Water", buttonSize, () => ApplyThemePreset(
            new Vector4(0.38f, 0.84f, 1.00f, 1f),
            new Vector4(0.020f, 0.068f, 0.100f, 0.92f),
            new Vector4(0.06f, 0.32f, 0.46f, 0.46f),
            new Vector4(0.04f, 0.23f, 0.34f, 0.42f),
            new Vector4(0.045f, 0.18f, 0.25f, 0.70f)));

        changed |= ThemePresetButton("Fire", buttonSize, () => ApplyThemePreset(
            new Vector4(1.00f, 0.56f, 0.20f, 1f),
            new Vector4(0.035f, 0.010f, 0.010f, 0.95f),
            new Vector4(0.48f, 0.12f, 0.04f, 0.54f),
            new Vector4(0.28f, 0.06f, 0.02f, 0.48f),
            new Vector4(0.18f, 0.045f, 0.030f, 0.74f)));

        ImGui.SameLine();
        changed |= ThemePresetButton("Sakura", buttonSize, () => ApplyThemePreset(
            new Vector4(1.00f, 0.60f, 0.80f, 1f),
            new Vector4(0.070f, 0.036f, 0.050f, 0.92f),
            new Vector4(0.48f, 0.18f, 0.28f, 0.44f),
            new Vector4(0.20f, 0.10f, 0.08f, 0.42f),
            new Vector4(0.23f, 0.11f, 0.16f, 0.68f)));

        changed |= ThemePresetButton("Frost", buttonSize, () => ApplyThemePreset(
            new Vector4(0.76f, 0.97f, 1.00f, 1f),
            new Vector4(0.020f, 0.055f, 0.075f, 0.92f),
            new Vector4(0.78f, 0.96f, 1.00f, 0.22f),
            new Vector4(0.36f, 0.70f, 0.90f, 0.34f),
            new Vector4(0.88f, 0.98f, 1.00f, 0.12f)));

        ImGui.SameLine();
        changed |= ThemePresetButton("Autumn", buttonSize, () => ApplyThemePreset(
            new Vector4(0.95f, 0.42f, 0.16f, 1f),
            new Vector4(0.070f, 0.044f, 0.026f, 0.94f),
            new Vector4(0.34f, 0.18f, 0.08f, 0.46f),
            new Vector4(0.23f, 0.13f, 0.055f, 0.42f),
            new Vector4(0.21f, 0.105f, 0.045f, 0.70f)));

        return changed;
    }

    private bool ThemePresetButton(string label, Vector2 size, Action apply)
    {
        var selected = string.Equals(config.ThemePresetName, label, StringComparison.Ordinal);
        if (!ImGui.Button(label, size))
        {
            DrawThemePresetButtonBorder(label, selected);
            return false;
        }

        if (selected)
        {
            config.ThemePresetName = string.Empty;
            config.CustomBackgroundEffectName = string.Empty;
            return true;
        }

        apply();
        config.ThemePresetName = label;
        config.CustomBackgroundEffectName = string.Empty;
        DrawThemePresetButtonBorder(label, true);
        return true;
    }

    private void DrawThemePresetButtonBorder(string label, bool selected)
    {
        if (!selected)
            return;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var color = GetThemePresetBorderColor(label);
        ImGui.GetWindowDrawList().AddRect(
            min - new Vector2(1f),
            max + new Vector2(1f),
            ImGui.GetColorU32(color),
            4f * ImGuiHelpers.GlobalScale,
            ImDrawFlags.None,
            1.6f * ImGuiHelpers.GlobalScale);
    }

    private Vector4 GetThemePresetBorderColor(string label)
        => label switch
        {
            "All Red" => new Vector4(1.00f, 0.28f, 0.30f, 1f),
            "All Gold" => new Vector4(1.00f, 0.78f, 0.27f, 1f),
            "White Gold" => new Vector4(1.00f, 0.84f, 0.34f, 1f),
            "Chocolate" => new Vector4(0.86f, 0.56f, 0.35f, 1f),
            "Cyberpunk" => new Vector4(0.78f, 0.34f, 1.00f, 1f),
            "Water" => new Vector4(0.38f, 0.84f, 1.00f, 1f),
            "Fire" => new Vector4(1.00f, 0.56f, 0.20f, 1f),
            "Sakura" => new Vector4(1.00f, 0.60f, 0.80f, 1f),
            "Autumn" => new Vector4(0.95f, 0.42f, 0.16f, 1f),
            "Frost" => new Vector4(0.76f, 0.97f, 1.00f, 1f),
            _ => config.AccentColor,
        };

    private void ApplyThemePreset(Vector4 accent, Vector4 windowBg, Vector4 topBarBg, Vector4 bottomBarBg, Vector4 rowBg)
    {
        config.AccentColor = accent;
        config.WindowBackgroundColor = windowBg;
        config.TopBarBackgroundColor = topBarBg;
        config.BottomBarBackgroundColor = bottomBarBg;
        config.UserRowBackgroundColor = rowBg;
        config.HideUserRowBackground = false;
    }

    private static string HexFromColor(Vector4 color, bool includeAlpha)
    {
        var r = Math.Clamp((int)MathF.Round(color.X * 255f), 0, 255);
        var g = Math.Clamp((int)MathF.Round(color.Y * 255f), 0, 255);
        var b = Math.Clamp((int)MathF.Round(color.Z * 255f), 0, 255);
        var a = Math.Clamp((int)MathF.Round(color.W * 255f), 0, 255);
        return includeAlpha ? $"#{r:X2}{g:X2}{b:X2}{a:X2}" : $"#{r:X2}{g:X2}{b:X2}";
    }

    private static string NormalizeHexText(string value)
    {
        var text = value.Trim();
        if (!text.StartsWith('#'))
            text = "#" + text;

        return text.Length > 9 ? text[..9] : text;
    }

    private static bool TryHexToColor(string value, float fallbackAlpha, out Vector4 color)
    {
        color = Vector4.Zero;
        var text = value.Trim().TrimStart('#');
        if (text.Length is not (6 or 8))
            return false;

        if (!int.TryParse(text[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !int.TryParse(text.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !int.TryParse(text.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return false;

        var a = Math.Clamp((int)MathF.Round(fallbackAlpha * 255f), 0, 255);
        if (text.Length == 8 && !int.TryParse(text.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a))
            return false;

        color = new Vector4(r / 255f, g / 255f, b / 255f, a / 255f);
        return true;
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
