using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Privacy.Models;
using Privacy.Services;
using Privacy.UI;
using Lumina.Excel.Sheets;
using System.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Privacy.Windows;

internal sealed class MyProfileWindow : Window
{
    private static readonly Dictionary<string, Dictionary<string, string[]>> DataCentersByRegion = new(StringComparer.Ordinal)
    {
        ["North America"] = new(StringComparer.Ordinal)
        {
            ["Aether"] = ["Adamantoise", "Cactuar", "Faerie", "Gilgamesh", "Jenova", "Midgardsormr", "Sargatanas", "Siren"],
            ["Crystal"] = ["Balmung", "Brynhildr", "Coeurl", "Diabolos", "Goblin", "Malboro", "Mateus", "Zalera"],
            ["Dynamis"] = ["Cuchulainn", "Golem", "Halicarnassus", "Kraken", "Maduin", "Marilith", "Rafflesia", "Seraph"],
            ["Primal"] = ["Behemoth", "Excalibur", "Exodus", "Famfrit", "Hyperion", "Lamia", "Leviathan", "Ultros"],
        },
        ["Europe"] = new(StringComparer.Ordinal)
        {
            ["Chaos"] = ["Cerberus", "Louisoix", "Moogle", "Omega", "Phantom", "Ragnarok", "Sagittarius", "Spriggan"],
            ["Light"] = ["Alpha", "Lich", "Odin", "Phoenix", "Raiden", "Shiva", "Twintania", "Zodiark"],
        },
        ["Oceania"] = new(StringComparer.Ordinal)
        {
            ["Materia"] = ["Bismarck", "Ravana", "Sephirot", "Sophia", "Zurvan"],
        },
        ["Japan"] = new(StringComparer.Ordinal)
        {
            ["Elemental"] = ["Aegis", "Atomos", "Carbuncle", "Garuda", "Gungnir", "Kujata", "Tonberry", "Typhon"],
            ["Gaia"] = ["Alexander", "Bahamut", "Durandal", "Fenrir", "Ifrit", "Ridill", "Tiamat", "Ultima"],
            ["Mana"] = ["Anima", "Asura", "Chocobo", "Hades", "Ixion", "Masamune", "Pandaemonium", "Titan"],
            ["Meteor"] = ["Belias", "Mandragora", "Ramuh", "Shinryu", "Unicorn", "Valefor", "Yojimbo", "Zeromus"],
        },
    };

    private static readonly string[] Districts = ["Mist", "Lavender Beds", "Goblet", "Shirogane", "Empyreum"];

    private readonly Configuration config;
    private readonly PrivacyCloudService cloudService;
    private readonly ProfileImageCache profileImages;
    private readonly GameIconCache gameIcons;
    private readonly FfxivVenuesService ffxivVenuesService;
    private readonly IDataManager dataManager;
    private readonly IClientState clientState;
    private readonly FileDialogManager fileDialog = new();

    private string profileDisplayName = string.Empty;
    private string profileBio = string.Empty;
    private string profileStatusMessage = string.Empty;
    private string profileStatusColorHex = "#2BE5B5";
    private string localAvatarPath = string.Empty;
    private string profileThemeName = "Default";
    private string profileThemeColorHex = "#2BE5B5";
    private string profileDisplayNameEffect = "None";
    private string profileBannerUrl = string.Empty;
    private string profileAvatarBorderColorHex = "#2BE5B5";
    private string profileAvatarPlaceholderStyle = "Question Mark";
    private string profileStatusVisibility = "All";
    private string profileVenuesVisibility = "All";
    private string profileLocationVisibility = "All";
    private float profileEditorFieldWidth;
    private string profileBioVisibility = "All";
    private string statusMessage = string.Empty;
    private string venueNameBuffer = string.Empty;
    private string selectedDataCenter = "Dynamis";
    private string selectedWorld = "Golem";
    private string selectedDistrict = "Goblet";
    private int selectedWard = 1;
    private int selectedPlot = 1;
    private string favoriteVenueSearch = string.Empty;
    private string selectedFavoriteVenueName = string.Empty;
    private FfxivVenueEntry? selectedFavoriteVenue;
    private string favoriteVenueTagBuffer = string.Empty;
    private string favoriteVenueTagColorHex = "#B56CFF";
    private string favoriteVenueTagError = string.Empty;
    private int favoriteVenueCarouselOffset;
    private readonly HashSet<string> pendingVenueImageDownloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pendingBannerDownloads = new(StringComparer.OrdinalIgnoreCase);
    private bool fieldsInitialized;
    private bool busy;
    private Vector2 resetFieldsPopupPos;
    private int pushedColorCount;
    private int pushedStyleVarCount;

    public MyProfileWindow(Configuration config, PrivacyCloudService cloudService, ProfileImageCache profileImages, GameIconCache gameIcons, FfxivVenuesService ffxivVenuesService, IDataManager dataManager, IClientState clientState)
        : base("My Profile###PrivacyMyProfile")
    {
        this.config = config;
        this.cloudService = cloudService;
        this.profileImages = profileImages;
        this.gameIcons = gameIcons;
        this.ffxivVenuesService = ffxivVenuesService;
        this.dataManager = dataManager;
        this.clientState = clientState;
        Size = new Vector2(540f, 780f);
        SizeCondition = ImGuiCond.FirstUseEver;

        WindowBuilder.For(this)
            .AllowPinning(true)
            .AllowClickthrough(false)
            .SetSizeConstraints(new Vector2(430f, 420f), new Vector2(780f, 980f))
            .AddFlags(ImGuiWindowFlags.NoDocking)
            .Apply();
    }

    public void Open()
    {
        fieldsInitialized = false;
        statusMessage = string.Empty;
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
        PushColor(ImGuiCol.FrameBg, Darken(UiColors.Get("PrivateFrameBg"), 0.015f));
        PushColor(ImGuiCol.FrameBgHovered, Darken(UiColors.Get("PrivateFrameBgHovered"), 0.01f));
        PushColor(ImGuiCol.FrameBgActive, Darken(UiColors.Get("PrivateFrameBgActive"), 0.005f));
        PushColor(ImGuiCol.TitleBg, UiColors.Get("PrivateTitleBg"));
        PushColor(ImGuiCol.TitleBgActive, UiColors.Get("PrivateTitleBgActive"));
        PushColor(ImGuiCol.TitleBgCollapsed, UiColors.Get("PrivateTitleBgCollapsed"));
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

        using var body = ImRaii.Child("my-profile-body", new Vector2(-1f, -1f), false);
        if (!body) return;

        if (!cloudService.IsLoggedIn)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.42f, 0.40f, 1f), "You need to Log In first.");
            return;
        }

        DrawHeader();
        EnsureProfileFieldsInitialized();

        using (ImRaii.Disabled(busy))
        {
            DrawProfileEditorSection();
        }

        if (busy)
            ImGui.TextDisabled("Working...");

        if (!string.IsNullOrWhiteSpace(statusMessage))
            ImGui.TextWrapped(statusMessage);

        if (HasUnsavedProfileChanges())
            ImGui.TextColored(new Vector4(1.00f, 0.78f, 0.28f, 1f), "Unsaved profile changes.");

        ImGui.Separator();
        DrawFavoriteVenuesSection();

        ImGui.TextColored(config.AccentColor, "Profile Preview:");
        ImGui.Dummy(new Vector2(1f, 3f * ImGuiHelpers.GlobalScale));
        DrawProfilePreview();

        ImGui.Separator();
        DrawVenuesSection();
    }

    private void DrawHeader()
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var scale = ImGuiHelpers.GlobalScale;
        var headerHeight = 58f * scale;
        HeaderDecor.Draw(drawList, pos, width, headerHeight, config.WindowBackgroundColor, config.AccentColor);

        DrawCenteredProfileTitle(drawList, pos, width, headerHeight, scale);
        ImGui.Dummy(new Vector2(width, headerHeight - 8f * scale));
    }

    private void DrawCenteredProfileTitle(ImDrawListPtr drawList, Vector2 headerPos, float headerWidth, float headerHeight, float scale)
    {
        var title = GetProfileHeaderTitle();
        var icon = char.ConvertFromUtf32(0xF2B9);
        var iconFont = UiBuilder.IconFont;
        var textFont = ImGui.GetFont();
        var iconSize = 18f * scale;
        var textSize = 20f * scale;
        var gap = 7f * scale;

        Vector2 iconTextSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            iconTextSize = ImGui.CalcTextSize(icon) * (iconSize / MathF.Max(1f, ImGui.GetFontSize()));

        var titleTextSize = ImGui.CalcTextSize(title) * (textSize / MathF.Max(1f, ImGui.GetFontSize()));
        var totalWidth = iconTextSize.X + gap + titleTextSize.X;
        var start = headerPos + new Vector2(MathF.Max(0f, (headerWidth - totalWidth) * 0.5f), 12f * scale);
        var shadow = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.78f));
        var color = ImGui.GetColorU32(config.AccentColor);
        var textColor = ImGui.GetColorU32(UiColors.Text);
        var shadowOffset = new Vector2(1.2f, 1.2f);
        var iconPos = start + new Vector2(0f, 1f * scale);
        var textPos = start + new Vector2(iconTextSize.X + gap, 0f);

        drawList.AddText(iconFont, iconSize, iconPos + shadowOffset, shadow, icon);
        drawList.AddText(iconFont, iconSize, iconPos, color, icon);
        drawList.AddText(textFont, textSize, textPos + shadowOffset, shadow, title);
        drawList.AddText(textFont, textSize, textPos, textColor, title);
    }

    private string GetProfileHeaderTitle()
    {
        if (!string.IsNullOrWhiteSpace(config.CloudLinkedCharacterName))
            return config.CloudLinkedCharacterName.Trim();

        if (!string.IsNullOrWhiteSpace(profileDisplayName))
            return profileDisplayName.Trim();

        if (!string.IsNullOrWhiteSpace(config.CloudDisplayName))
            return config.CloudDisplayName.Trim();

        return "My Profile";
    }

    private void DrawProfileEditorSection()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var avatarSize = new Vector2(76f, 76f) * scale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var start = ImGui.GetCursorScreenPos();

        ImGui.BeginGroup();
        DrawAvatarPicker(avatarSize);
        DrawProfileActionButtons(scale, avatarSize.X);
        ImGui.EndGroup();
        var afterAvatarColumn = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(new Vector2(start.X + avatarSize.X + spacing, start.Y));
        ImGui.BeginGroup();
        DrawProfileFields();
        ImGui.EndGroup();
        var afterFields = ImGui.GetCursorScreenPos();

        var nextY = MathF.Max(afterAvatarColumn.Y, afterFields.Y) + spacing;
        ImGui.SetCursorScreenPos(new Vector2(start.X + 4f * scale, nextY));
        DrawProfileVisualCustomization(MathF.Max(240f * scale, avatarSize.X + spacing + profileEditorFieldWidth + 42f * scale));

        ImGui.SetCursorScreenPos(new Vector2(start.X, ImGui.GetCursorScreenPos().Y + spacing));
        DrawResetFieldsPopup(scale);
    }

    private void DrawProfileActionButtons(float scale, float avatarWidth)
    {
        var buttonSize = new Vector2(28f, 24f) * scale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var rowWidth = buttonSize.X * 2f + spacing;
        var cursorX = ImGui.GetCursorScreenPos().X + MathF.Max(0f, (avatarWidth - rowWidth) * 0.5f);
        ImGui.SetCursorScreenPos(new Vector2(cursorX, ImGui.GetCursorScreenPos().Y + 5f * scale));

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (DrawProfileIconButton(char.ConvertFromUtf32(0xF00C), "save_profile_icon", scale, UiColors.HexToRgba("#9CFFB1")))
            {
                var displayName = profileDisplayName;
                var displayNameEffect = profileDisplayNameEffect;
                var bio = profileBio;
                var profileStatus = profileStatusMessage;
                var statusColorHex = profileStatusColorHex;
                var avatarPath = localAvatarPath;
                var themeName = profileThemeName;
                var themeColorHex = profileThemeColorHex;
                var bannerUrl = profileBannerUrl;
                var borderColorHex = profileAvatarBorderColorHex;
                var placeholderStyle = profileAvatarPlaceholderStyle;
                RunCloudAction(() => SaveProfileAsync(displayName, displayNameEffect, bio, profileStatus, statusColorHex, avatarPath, themeName, themeColorHex, bannerUrl, borderColorHex, placeholderStyle));
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Save Changes to your Profile");

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (DrawProfileIconButton(char.ConvertFromUtf32(0xF12D), "reset_fields_icon", scale, UiColors.HexToRgba("#FF8E8E")))
            {
                var resetButtonMin = ImGui.GetItemRectMin();
                var popupWidth = 178f * scale;
                resetFieldsPopupPos = new Vector2(resetButtonMin.X - popupWidth - 6f * scale, resetButtonMin.Y - 2f * scale);
                ImGui.OpenPopup("reset_fields_confirm_popup");
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Reset every field");
    }

    private void DrawResetFieldsPopup(float scale)
    {
        var popupWidth = 178f * scale;
        ImGui.SetNextWindowPos(resetFieldsPopupPos, ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new Vector2(popupWidth, 0f), ImGuiCond.Appearing);
        using (ImRaii.PushColor(ImGuiCol.PopupBg, UiColors.Get("PrivatePopupBg")))
        using (ImRaii.PushColor(ImGuiCol.Border, UiColors.WithAlpha(config.AccentColor, 0.38f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f) * scale))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 5f * scale))
        {
            if (!ImGui.BeginPopup("reset_fields_confirm_popup"))
                return;

            ImGui.TextUnformatted("Erase everything?");
            ImGui.Spacing();

            var buttonWidth = 58f * scale;
            if (ImGui.Button("Yes##confirm_reset_fields", new Vector2(buttonWidth, 0f)))
            {
                fieldsInitialized = false;
                EnsureProfileFieldsInitialized();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("No##cancel_reset_fields", new Vector2(buttonWidth, 0f)))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }

    private static bool DrawProfileIconButton(string icon, string id, float scale, Vector4 hoverColor)
    {
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(28f, 24f) * scale;
        var hovered = ImGui.IsMouseHoveringRect(pos, pos + size);
        using (ImRaii.PushColor(ImGuiCol.Text, hovered ? hoverColor : UiColors.TextDim))
        using (ImRaii.PushColor(ImGuiCol.Button, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.Border, Vector4.Zero))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 0f))
        {
            return ImGui.Button($"{icon}##{id}", size);
        }
    }

    private void DrawProfileFields()
    {
        var scale = ImGuiHelpers.GlobalScale;

        var totalWidth = MathF.Min(378f * scale, MathF.Max(230f * scale, ImGui.GetContentRegionAvail().X - 66f * scale));
        var colorButtonWidth = 34f * scale;
        var fieldWidth = MathF.Max(150f * scale, totalWidth - colorButtonWidth - ImGui.GetStyle().ItemSpacing.X);
        profileEditorFieldWidth = fieldWidth;
        ImGui.SetNextItemWidth(fieldWidth);
        var displayInputPos = ImGui.GetCursorScreenPos();
        using (ImRaii.PushColor(ImGuiCol.Text, UiColors.WithAlpha(UiColors.Text, ProfileNameEffects.Normalize(profileDisplayNameEffect).Equals("None", StringComparison.OrdinalIgnoreCase) ? 1f : 0.01f)))
            ImGui.InputTextWithHint("##my_profile_display", "Display name", ref profileDisplayName, 48);
        if (!ProfileNameEffects.Normalize(profileDisplayNameEffect).Equals("None", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(profileDisplayName))
        {
            var drawList = ImGui.GetWindowDrawList();
            var inputMax = displayInputPos + new Vector2(fieldWidth, ImGui.GetFrameHeight());
            drawList.PushClipRect(displayInputPos, inputMax, true);
            ProfileNameEffects.DrawEffectText(drawList, displayInputPos + new Vector2(8f * scale, 4f * scale), profileDisplayName, profileDisplayNameEffect, UiColors.Text, ImGui.GetFontSize(), scale);
            drawList.PopClipRect();
        }
        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Button, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, UiColors.WithAlpha(config.AccentColor, 0.16f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, UiColors.WithAlpha(config.AccentColor, 0.26f)))
        {
            if (ImGui.Button($"{char.ConvertFromUtf32(0xF53F)}##display_name_effect", new Vector2(28f, 0f) * scale))
                ImGui.OpenPopup("display_name_effect_popup");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Display name effect");
        ProfileNameEffects.DrawPickerPopup("display_name_effect_popup", ref profileDisplayNameEffect);

        var statusWidth = fieldWidth;
        ImGui.SetNextItemWidth(statusWidth);
        using (ImRaii.PushColor(ImGuiCol.Text, UiColors.HexToRgba(NormalizeHex(profileStatusColorHex, "#2BE5B5"))))
            ImGui.InputTextWithHint("##my_profile_status", "Status message", ref profileStatusMessage, 60);
        ImGui.SameLine();
        var statusColor = UiColors.HexToRgba(NormalizeHex(profileStatusColorHex, "#2BE5B5"));
        if (ImGui.ColorEdit4("##my_profile_status_color", ref statusColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.AlphaPreviewHalf))
            profileStatusColorHex = ColorToHex(statusColor);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Status message color: {NormalizeHex(profileStatusColorHex, "#2BE5B5")}");

        ImGui.SetNextItemWidth(fieldWidth);
        ImGui.InputTextMultiline("##my_profile_bio", ref profileBio, 120, new Vector2(fieldWidth, 74f * scale));
    }

    private static bool DrawThemeSelector(string id, ref int selectedIndex, float scale)
    {
        var changed = false;
        var current = ProfileVisuals.ThemeNames[Math.Clamp(selectedIndex, 0, ProfileVisuals.ThemeNames.Length - 1)];
        if (!ImGui.BeginCombo(id, current))
            return false;

        var visibleRows = MathF.Min(6f, ProfileVisuals.ThemeNames.Length);
        var childHeight = (ImGui.GetTextLineHeightWithSpacing() * visibleRows) + 6f * scale;
        if (ImGui.BeginChild($"{id}_scroll", new Vector2(0f, childHeight), false))
        {
            for (var i = 0; i < ProfileVisuals.ThemeNames.Length; i++)
            {
                var selected = i == selectedIndex;
                if (ImGui.Selectable(ProfileVisuals.ThemeNames[i], selected))
                {
                    selectedIndex = i;
                    changed = true;
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
        }
        ImGui.EndChild();
        ImGui.EndCombo();
        return changed;
    }

    private void DrawProfileVisualCustomization(float availableWidth)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.Spacing();
        ImGui.TextColored(config.AccentColor, "Profile Theme");
        var themeIndex = Array.FindIndex(ProfileVisuals.ThemeNames, t => string.Equals(t, profileThemeName, StringComparison.OrdinalIgnoreCase));
        if (themeIndex < 0) themeIndex = 0;
        ImGui.SetNextItemWidth(150f * scale);
        if (DrawThemeSelector("##profile_theme_name", ref themeIndex, scale))
            profileThemeName = ProfileVisuals.ThemeNames[Math.Clamp(themeIndex, 0, ProfileVisuals.ThemeNames.Length - 1)];

        ImGui.SameLine();
        ImGui.SetNextItemWidth(132f * scale);
        var placeholderIndex = Array.FindIndex(ProfileVisuals.PlaceholderStyles, t => string.Equals(t, profileAvatarPlaceholderStyle, StringComparison.OrdinalIgnoreCase));
        if (placeholderIndex < 0) placeholderIndex = 0;
        if (ImGui.Combo("##profile_placeholder_style", ref placeholderIndex, ProfileVisuals.PlaceholderStyles, ProfileVisuals.PlaceholderStyles.Length))
            profileAvatarPlaceholderStyle = ProfileVisuals.PlaceholderStyles[Math.Clamp(placeholderIndex, 0, ProfileVisuals.PlaceholderStyles.Length - 1)];
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Used when no profile picture is available.");

        var themeColor = UiColors.HexToRgba(NormalizeHex(profileThemeColorHex, "#2BE5B5"));
        if (ImGui.ColorEdit4("Theme color", ref themeColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaPreviewHalf))
            profileThemeColorHex = ColorToHex(themeColor);

        ImGui.SameLine();
        var borderColor = UiColors.HexToRgba(NormalizeHex(profileAvatarBorderColorHex, "#2BE5B5"));
        if (ImGui.ColorEdit4("Avatar border", ref borderColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaPreviewHalf))
            profileAvatarBorderColorHex = ColorToHex(borderColor);

        var bannerWidth = MathF.Min(MathF.Max(220f * scale, profileEditorFieldWidth), MathF.Max(180f * scale, availableWidth - 28f * scale));
        ImGui.SetNextItemWidth(bannerWidth);
        ImGui.InputTextWithHint("##profile_banner_url", "Banner image URL", ref profileBannerUrl, 260);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Optional public banner/header image URL shown on your Online Profile.");

        ImGui.TextColored(config.AccentColor, "Who can see my profile details?");
        DrawVisibilityGrid();
    }

    private void DrawVisibilityGrid()
    {
        var scale = ImGuiHelpers.GlobalScale;
        if (ImGui.BeginTable("profile_visibility_grid", 4, ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("status_label", ImGuiTableColumnFlags.WidthFixed, 52f * scale);
            ImGui.TableSetupColumn("status_combo", ImGuiTableColumnFlags.WidthFixed, 112f * scale);
            ImGui.TableSetupColumn("venues_label", ImGuiTableColumnFlags.WidthFixed, 58f * scale);
            ImGui.TableSetupColumn("venues_combo", ImGuiTableColumnFlags.WidthFixed, 112f * scale);

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("Status");
            ImGui.TableNextColumn(); DrawVisibilityCombo("Status", ref profileStatusVisibility, true);
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("Venues");
            ImGui.TableNextColumn(); DrawVisibilityCombo("Venues", ref profileVenuesVisibility, true);

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("Location");
            ImGui.TableNextColumn(); DrawVisibilityCombo("Location", ref profileLocationVisibility, true);
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("Bio");
            ImGui.TableNextColumn(); DrawVisibilityCombo("Bio", ref profileBioVisibility, true);

            ImGui.EndTable();
        }
    }

    private static void DrawVisibilityCombo(string label, ref string value, bool hiddenLabel = false)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var currentValue = value;
        var index = Array.FindIndex(ProfileVisuals.VisibilityOptions, option => string.Equals(option, currentValue, StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = 0;
        ImGui.SetNextItemWidth(112f * scale);
        var comboLabel = hiddenLabel ? $"##profile_visibility_{label}" : $"{label}##profile_visibility_{label}";
        if (ImGui.Combo(comboLabel, ref index, ProfileVisuals.VisibilityOptions, ProfileVisuals.VisibilityOptions.Length))
            value = ProfileVisuals.VisibilityOptions[Math.Clamp(index, 0, ProfileVisuals.VisibilityOptions.Length - 1)];
    }

    private void DrawAvatarPicker(Vector2 size)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        var texture = profileImages.GetTexture(localAvatarPath);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.06f, 0.09f, 0.08f, 0.92f)), 5f * scale);
        if (texture != null)
        {
            drawList.AddImageRounded(texture.Handle, min, max, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), 5f * scale);
        }
        else
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                var icon = FontAwesomeIcon.User.ToIconString();
                var iconSize = ImGui.CalcTextSize(icon);
                drawList.AddText(min + (size - iconSize) * 0.5f, ImGui.GetColorU32(UiColors.TextDim), icon);
            }
        }

        drawList.AddRect(min, max, ImGui.GetColorU32(UiColors.WithAlpha(UiColors.HexToRgba(NormalizeHex(profileAvatarBorderColorHex, "#2BE5B5")), 0.70f)), 5f * scale, ImDrawFlags.None, 1.6f * scale);
        ImGui.InvisibleButton("my-profile-avatar-picker", size);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Click to choose a profile picture. Max 512x512 and 2 MB.");

        if (ImGui.IsItemClicked() && cloudService.IsLoggedIn && !busy)
            OpenAvatarPicker();
    }

    private void DrawProfilePreview()
    {
        var preview = BuildSelfOnlineProfilePreview();
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var contentWidth = ImGui.GetContentRegionAvail().X;
        var palette = ProfileVisuals.ResolvePalette(preview.CloudThemeName, UiColors.HexToRgba(NormalizeHex(preview.CloudThemeColorHex, "#2BE5B5")), config.WindowBackgroundColor);
        var previewVenues = preview.CloudVenues.Where(v => !string.IsNullOrWhiteSpace(v.Name)).ToList();
        var previewHeight = (previewVenues.Count > 0 ? 382f : 322f) * scale;
        var previewPos = ImGui.GetCursorScreenPos();
        var previewSize = new Vector2(contentWidth, previewHeight);

        ProfileVisuals.DrawThemeBackdrop(drawList, previewPos, previewSize, preview.CloudThemeName, palette, scale);
        drawList.AddRect(previewPos, previewPos + previewSize, ImGui.GetColorU32(UiColors.WithAlpha(palette.Accent, 0.42f)), 7f * scale, ImDrawFlags.None, 1f * scale);

        var clipMin = previewPos;
        var clipMax = previewPos + previewSize;
        drawList.PushClipRect(clipMin, clipMax, true);
        ImGui.BeginGroup();
        DrawProfilePreviewOverlay(drawList, preview, contentWidth, scale);
        DrawPreviewLowerDecor(drawList, preview, contentWidth, scale, previewPos + previewSize);
        ImGui.Spacing();
        if (previewVenues.Count > 0)
            DrawFavoriteVenueCarousel(previewVenues, "preview", allowEditing: false);
        else
            DrawPreviewSection("Favorite Venues", "No favorite venues selected yet.", null, preview.CloudThemeName);

        DrawPreviewSection("Status", string.IsNullOrWhiteSpace(preview.CloudStatusMessage) ? "No status message set." : preview.CloudStatusMessage, UiColors.HexToRgba(NormalizeHex(preview.CloudStatusColorHex, "#2BE5B5")), preview.CloudThemeName);
        DrawPreviewSection("Bio", string.IsNullOrWhiteSpace(preview.CloudBio) ? "No bio set." : preview.CloudBio, null, preview.CloudThemeName);

        ImGui.Indent(14f * scale);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(palette.Accent, ProfileVisuals.GetSectionIcon(preview.CloudThemeName, "Location"));
        }
        ImGui.SameLine();
        ImGui.TextColored(palette.Title, "Location");
        ImGui.Indent(18f * scale);
        ImGui.TextColored(palette.Subtitle, string.IsNullOrWhiteSpace(preview.LastKnownZone) ? "Unknown location." : FormatVenueLocationText(preview.LastKnownZone));
        if (!string.IsNullOrWhiteSpace(preview.ResidentialDetails))
            ImGui.TextColored(palette.Subtitle, preview.ResidentialDetails);
        ImGui.Unindent(18f * scale);
        ImGui.Unindent(14f * scale);
        ImGui.Spacing();
        ImGui.EndGroup();
        drawList.PopClipRect();

        var usedHeight = MathF.Max(previewHeight, ImGui.GetCursorScreenPos().Y - previewPos.Y + 3f * scale);
        ImGui.SetCursorScreenPos(new Vector2(previewPos.X, previewPos.Y + usedHeight));
    }

    private void DrawPreviewLowerDecor(ImDrawListPtr drawList, PrivateContact profile, float contentWidth, float scale, Vector2 previewBottom)
    {
        var cursor = ImGui.GetCursorScreenPos();
        var protrude = 6f * scale;
        var pos = cursor + new Vector2(-protrude, -2f * scale);
        var size = new Vector2(contentWidth + protrude * 2f, MathF.Max(110f * scale, previewBottom.Y - pos.Y));
        var palette = ProfileVisuals.ResolvePalette(profile.CloudThemeName, UiColors.HexToRgba(NormalizeHex(profile.CloudThemeColorHex, "#2BE5B5")), config.WindowBackgroundColor);
        ProfileVisuals.DrawThemeOverlay(drawList, pos, size, profile.CloudThemeName, palette, scale);
    }

    private PrivateContact BuildSelfOnlineProfilePreview()
    {
        var displayName = string.IsNullOrWhiteSpace(profileDisplayName) ? config.CloudDisplayName : profileDisplayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = config.CloudLinkedCharacterName;
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = "Your profile";

        var name = string.IsNullOrWhiteSpace(config.CloudLinkedCharacterName) ? displayName : config.CloudLinkedCharacterName;
        var currentWorldId = ResolveLocalCurrentWorldId();
        var world = ResolveWorldName(currentWorldId);
        if (string.IsNullOrWhiteSpace(world))
            world = config.CloudLinkedWorld;
        var dataCenter = ResolveDataCenterName(currentWorldId);
        if (string.IsNullOrWhiteSpace(dataCenter))
            dataCenter = ResolveDataCenterForWorld(world);
        var location = BuildPreviewLocationLine(world, dataCenter, out var residentialDetails);

        return new PrivateContact
        {
            Id = "self-profile-preview",
            Name = name,
            World = world,
            CurrentWorld = world,
            DataCenter = dataCenter,
            CurrentDataCenter = dataCenter,
            LastKnownZone = location,
            ResidentialDetails = residentialDetails,
            ProfileImagePath = localAvatarPath,
            CloudManagedProfileImage = !string.IsNullOrWhiteSpace(localAvatarPath),
            CloudDisplayName = displayName,
            CloudStatusMessage = profileStatusMessage,
            CloudStatusColorHex = NormalizeHex(profileStatusColorHex, "#2BE5B5"),
            CloudBio = profileBio,
            CloudThemeName = profileThemeName,
            CloudThemeColorHex = NormalizeHex(profileThemeColorHex, "#2BE5B5"),
            CloudDisplayNameEffect = ProfileNameEffects.Normalize(profileDisplayNameEffect),
            CloudBannerUrl = profileBannerUrl,
            CloudAvatarBorderColorHex = NormalizeHex(profileAvatarBorderColorHex, "#2BE5B5"),
            CloudAvatarPlaceholderStyle = profileAvatarPlaceholderStyle,
            CloudStatusVisibility = profileStatusVisibility,
            CloudVenuesVisibility = profileVenuesVisibility,
            CloudLocationVisibility = ProfileVisuals.NormalizeVisibility(profileLocationVisibility, "All"),
            CloudBioVisibility = profileBioVisibility,
            CloudVenues = config.CloudSavedVenues.ToList(),
            Status = config.CloudPresenceStatus == ContactStatus.Offline ? ContactStatus.Online : config.CloudPresenceStatus,
        };
    }


    private Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? ResolvePreviewBannerTexture(PrivateContact profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.CloudBannerLocalPath))
        {
            var texture = profileImages.GetTexture(profile.CloudBannerLocalPath);
            if (texture != null)
                return texture;
        }

        if (string.IsNullOrWhiteSpace(profile.CloudBannerUrl))
            return null;

        var bannerUrl = profile.CloudBannerUrl.Trim();
        var cacheKey = BuildBannerCacheKey("self_profile_banner", bannerUrl);
        if (profileImages.TryGetCurrentRemoteVenueImagePath(bannerUrl, cacheKey, bannerUrl, out var cachedPath))
        {
            profile.CloudBannerLocalPath = cachedPath;
            config.CloudProfileBannerLocalPath = cachedPath;
            var texture = profileImages.GetTexture(cachedPath);
            if (texture != null)
                return texture;
        }

        if (!pendingBannerDownloads.Add(cacheKey))
            return null;
        Task.Run(async () =>
        {
            try
            {
                var storedPath = await profileImages.DownloadRemoteVenueImageAsync(bannerUrl, cacheKey, CancellationToken.None, bannerUrl).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(storedPath))
                {
                    profile.CloudBannerLocalPath = storedPath;
                    config.CloudProfileBannerLocalPath = storedPath;
                    config.CloudProfileBannerUrl = bannerUrl;
                    config.Save();
                }
            }
            finally
            {
                pendingBannerDownloads.Remove(cacheKey);
            }
        });

        return null;
    }

    private static string BuildBannerCacheKey(string ownerKey, string bannerUrl)
    {
        unchecked
        {
            var hash = 23;
            foreach (var ch in bannerUrl.Trim().ToLowerInvariant())
                hash = hash * 31 + ch;
            return $"{ownerKey}_{Math.Abs(hash)}";
        }
    }

    private void DrawProfilePreviewOverlay(ImDrawListPtr drawList, PrivateContact profile, float contentWidth, float scale)
    {
        var cursor = ImGui.GetCursorScreenPos();
        var protrude = 6f * scale;
        var pos = cursor + new Vector2(-protrude, 0f);
        var size = new Vector2(contentWidth + protrude * 2f, 126f * scale);
        var rounding = 7f * scale;

        var borderPad = 1f * scale;
        var palette = ProfileVisuals.ResolvePalette(profile.CloudThemeName, UiColors.HexToRgba(NormalizeHex(profile.CloudThemeColorHex, "#2BE5B5")), config.WindowBackgroundColor);
        var themeAccent = palette.Accent;
        var barFill = UiColors.WithAlpha(palette.Panel, 0.72f);
        var barOverlay = palette.Overlay;
        drawList.AddRectFilled(pos - new Vector2(borderPad, borderPad), pos + size + new Vector2(borderPad, borderPad), ImGui.GetColorU32(barFill), rounding + borderPad);
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(barOverlay), rounding);
        var bannerTexture = ResolvePreviewBannerTexture(profile);
        if (bannerTexture != null)
            drawList.AddImageRounded(bannerTexture.Handle, pos, pos + size, Vector2.Zero, Vector2.One, ImGui.GetColorU32(UiColors.WithAlpha(Vector4.One, 0.42f)), rounding);
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(UiColors.WithAlpha(themeAccent, 0.42f)), rounding, ImDrawFlags.None, 1f * scale);
        ProfileVisuals.DrawThemeOverlay(drawList, pos, size, profile.CloudThemeName, palette, scale);

        var avatarSize = new Vector2(88f, 88f) * scale;
        var avatarMin = pos + new Vector2(14f, 19f) * scale;
        var avatarMax = avatarMin + avatarSize;
        var texture = profileImages.GetTexture(localAvatarPath);

        drawList.AddRectFilled(avatarMin, avatarMax, ImGui.GetColorU32(UiColors.WithAlpha(config.WindowBackgroundColor, 0.34f)), 7f * scale);
        if (texture != null)
            drawList.AddImageRounded(texture.Handle, avatarMin, avatarMax, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), 7f * scale);
        else
            DrawProfilePlaceholder(drawList, profile, avatarMin, avatarSize, scale);
        drawList.AddRect(avatarMin, avatarMax, ImGui.GetColorU32(UiColors.WithAlpha(UiColors.HexToRgba(NormalizeHex(profile.CloudAvatarBorderColorHex, "#2BE5B5")), 0.70f)), 7f * scale, ImDrawFlags.None, 1.4f * scale);
        DrawStatusBadge(profile.Status, avatarMax - new Vector2(13f, 13f) * scale, 7.2f * scale, true);

        var textMin = new Vector2(avatarMax.X + 16f * scale, avatarMin.Y + 4f * scale);
        var textMax = pos + size - new Vector2(14f, 12f) * scale;
        var textWidth = MathF.Max(40f * scale, textMax.X - textMin.X);
        var displayName = profile.CloudDisplayName.Length > 0 ? profile.CloudDisplayName : profile.DisplayName;
        var identity = string.IsNullOrWhiteSpace(profile.World) ? profile.Name : $"{profile.Name}@{profile.World}";
        var statusText = GetStatusDisplayName(profile.Status);
        var statusColor = GetStatusColor(profile.Status);

        drawList.PushClipRect(textMin - new Vector2(1f, 1f) * scale, textMax, true);
        var displayFontSize = 15.2f * scale;
        var previewVenueName = ResolvePreviewVenueName(profile);
        var venueBadgeText = string.IsNullOrWhiteSpace(previewVenueName) || profile.Status == ContactStatus.Offline ? string.Empty : $"@At {previewVenueName}";
        var venueBadgeFontSize = 12.6f * scale;
        var rawBadgeWidth = string.IsNullOrWhiteSpace(venueBadgeText)
            ? 0f
            : ImGui.CalcTextSize(venueBadgeText).X * (venueBadgeFontSize / MathF.Max(1f, ImGui.GetFontSize())) + 10f * scale;
        var venueBadgeWidth = string.IsNullOrWhiteSpace(venueBadgeText)
            ? 0f
            : MathF.Min(MathF.Max(rawBadgeWidth, 72f * scale), textWidth * 0.48f);
        var displayVisible = TrimToWidth(displayName, MathF.Max(24f * scale, textWidth - venueBadgeWidth), displayFontSize);
        ProfileNameEffects.DrawEffectText(drawList, textMin, displayVisible, profile.CloudDisplayNameEffect, palette.Title, displayFontSize, scale);
        DrawPreviewVenueBadgeBesideName(drawList, textMin, displayVisible, textWidth, palette, scale, venueBadgeText);
        DrawTextWithShadow(drawList, textMin + new Vector2(0f, 24f) * scale, ImGui.GetColorU32(palette.Subtitle), TrimToWidth(identity, textWidth, 13.3f * scale), 13.3f * scale);
        DrawTextWithShadow(drawList, textMin + new Vector2(0f, 48f) * scale, ImGui.GetColorU32(statusColor), statusText, 13.3f * scale);
        drawList.PopClipRect();

        ImGui.Dummy(new Vector2(contentWidth, size.Y + 8f * scale));
    }


    private void DrawPreviewVenueBadgeBesideName(ImDrawListPtr drawList, Vector2 namePos, string visibleName, float textWidth, ProfileVisuals.ThemePalette palette, float scale, string venueBadgeText)
    {
        if (string.IsNullOrWhiteSpace(venueBadgeText))
            return;

        var fontSize = 12.6f * scale;
        var nameWidth = ImGui.CalcTextSize(visibleName).X * (15.2f * scale / MathF.Max(1f, ImGui.GetFontSize()));
        var badgePos = namePos + new Vector2(nameWidth + 8f * scale, 2.2f * scale);
        var available = MathF.Max(0f, textWidth - nameWidth - 10f * scale);
        if (available < 24f * scale)
            available = MathF.Max(24f * scale, textWidth * 0.40f);

        var text = TrimToWidth(venueBadgeText, available, fontSize);
        DrawTextWithShadow(drawList, badgePos, ImGui.GetColorU32(UiColors.WithAlpha(palette.Accent, 0.88f)), text, fontSize);
    }

    private string ResolvePreviewVenueName(PrivateContact profile)
    {
        var dataCenter = string.IsNullOrWhiteSpace(profile.CurrentDataCenter) ? profile.DataCenter : profile.CurrentDataCenter;
        var world = string.IsNullOrWhiteSpace(profile.CurrentWorld) ? profile.World : profile.CurrentWorld;
        var district = ResolvePreviewResidentialDistrict(profile);
        var wardText = ExtractPreviewHousingNumber(profile.ResidentialDetails, "Ward", "w");
        var plotText = ExtractPreviewHousingNumber(profile.ResidentialDetails, "Plot", "p");
        var ward = 0;
        var plot = 0;
        var hasHousingParts = int.TryParse(wardText, out ward) && int.TryParse(plotText, out plot);

        if (hasHousingParts)
        {
            var fieldMatch = config.CloudSavedVenues.FirstOrDefault(v =>
                !string.IsNullOrWhiteSpace(v.Name) &&
                VenueFieldMatches(v.DataCenter, dataCenter) &&
                VenueFieldMatches(v.World, world) &&
                SameVenueDistrict(v.District, district) &&
                v.Ward == ward &&
                v.Plot == plot);
            if (fieldMatch != null)
                return fieldMatch.Name;

            // Some game snapshots can miss the visited DC/world or format them differently.
            // If district/ward/plot are exact and there is only one saved venue at that address,
            // prefer showing the venue badge instead of hiding it completely.
            var housingOnlyMatches = config.CloudSavedVenues
                .Where(v => !string.IsNullOrWhiteSpace(v.Name) &&
                    SameVenueDistrict(v.District, district) &&
                    v.Ward == ward &&
                    v.Plot == plot)
                .Take(2)
                .ToList();
            if (housingOnlyMatches.Count == 1)
                return housingOnlyMatches[0].Name;
        }

        var address = BuildPreviewVenueAddress(profile);
        if (!string.IsNullOrWhiteSpace(address))
        {
            var normalizedAddress = NormalizePreviewVenueAddress(address);
            var match = config.CloudSavedVenues.FirstOrDefault(v =>
                !string.IsNullOrWhiteSpace(v.Name) &&
                (string.Equals(NormalizePreviewVenueAddress(v.Address), normalizedAddress, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(NormalizePreviewVenueAddress(v.BuildAddress()), normalizedAddress, StringComparison.OrdinalIgnoreCase)));
            if (match != null)
                return match.Name;
        }

        if (hasHousingParts)
            return ffxivVenuesService.FindByAddress(dataCenter, world, district, ward, plot)?.Name ?? string.Empty;

        return string.Empty;
    }

    private static string BuildPreviewVenueAddress(PrivateContact profile)
    {
        var dataCenter = string.IsNullOrWhiteSpace(profile.CurrentDataCenter) ? profile.DataCenter : profile.CurrentDataCenter;
        var world = string.IsNullOrWhiteSpace(profile.CurrentWorld) ? profile.World : profile.CurrentWorld;
        var district = ResolvePreviewResidentialDistrict(profile);
        var ward = ExtractPreviewHousingNumber(profile.ResidentialDetails, "Ward", "w");
        var plot = ExtractPreviewHousingNumber(profile.ResidentialDetails, "Plot", "p");
        if (string.IsNullOrWhiteSpace(dataCenter) || string.IsNullOrWhiteSpace(world) || string.IsNullOrWhiteSpace(district) || string.IsNullOrWhiteSpace(ward) || string.IsNullOrWhiteSpace(plot))
            return string.Empty;
        return $"{dataCenter} {world} {district} w{ward} p{plot}";
    }

    private static string ResolvePreviewResidentialDistrict(PrivateContact profile)
    {
        var text = $"{profile.LastKnownZone} {profile.ResidentialDetails}";
        if (text.Contains("Mist", StringComparison.OrdinalIgnoreCase)) return "Mist";
        if (text.Contains("Lavender", StringComparison.OrdinalIgnoreCase) || text.Contains("Lavander", StringComparison.OrdinalIgnoreCase)) return "Lb";
        if (text.Contains("Goblet", StringComparison.OrdinalIgnoreCase)) return "Goblet";
        if (text.Contains("Shirogane", StringComparison.OrdinalIgnoreCase)) return "Shirogane";
        if (text.Contains("Empyreum", StringComparison.OrdinalIgnoreCase)) return "Empyreum";
        return string.Empty;
    }

    private static string ExtractPreviewHousingNumber(string text, string longMarker, string shortMarker)
    {
        var value = ExtractPreviewNumberAfter(text, longMarker);
        return string.IsNullOrWhiteSpace(value) ? ExtractPreviewNumberAfter(text, shortMarker) : value;
    }

    private static string ExtractPreviewNumberAfter(string text, string marker)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(marker)) return string.Empty;
        var comparison = StringComparison.OrdinalIgnoreCase;
        var index = -1;
        while (true)
        {
            index = text.IndexOf(marker, index + 1, comparison);
            if (index < 0) return string.Empty;
            var shortMarker = marker.Length == 1;
            if (shortMarker && index > 0 && char.IsLetterOrDigit(text[index - 1]))
                continue;
            var start = index + marker.Length;
            while (start < text.Length && !char.IsDigit(text[start])) start++;
            if (start >= text.Length) continue;
            var end = start;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            return text[start..end];
        }
    }

    private static bool VenueFieldMatches(string left, string right)
        => string.IsNullOrWhiteSpace(left) ||
           string.IsNullOrWhiteSpace(right) ||
           string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameVenueWorld(string left, string right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameVenueDistrict(string left, string right)
        => string.Equals(NormalizeVenueDistrictName(left), NormalizeVenueDistrictName(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeVenueDistrictName(string value)
        => (value ?? string.Empty)
            .Replace("The ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Lavender Beds", "Lb", StringComparison.OrdinalIgnoreCase)
            .Replace("Lavander Beds", "Lb", StringComparison.OrdinalIgnoreCase)
            .Trim();

    private static string NormalizePreviewVenueAddress(string value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("Lavender Beds", "Lb", StringComparison.OrdinalIgnoreCase)
            .Replace("Lavander Beds", "Lb", StringComparison.OrdinalIgnoreCase)
            .Replace("The Lavender Beds", "Lb", StringComparison.OrdinalIgnoreCase)
            .Replace("Subdivision", "sub", StringComparison.OrdinalIgnoreCase)
            .Replace("Subdiv", "sub", StringComparison.OrdinalIgnoreCase)
            .Replace(",", " ", StringComparison.Ordinal)
            .Replace("/", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Trim();

        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);

        return normalized;
    }

    private void DrawProfilePlaceholder(ImDrawListPtr drawList, PrivateContact profile, Vector2 min, Vector2 size, float scale)
    {
        var style = profile.CloudAvatarPlaceholderStyle ?? string.Empty;
        var text = style.Contains("Initial", StringComparison.OrdinalIgnoreCase)
            ? (string.IsNullOrWhiteSpace(profile.CloudDisplayName) ? "?" : profile.CloudDisplayName.Trim()[0].ToString().ToUpperInvariant())
            : style.Contains("None", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : "?";
        if (string.IsNullOrEmpty(text)) return;
        var fontSize = style.Contains("Initial", StringComparison.OrdinalIgnoreCase) ? 23f * scale : 24f * scale;
        var textSize = ImGui.CalcTextSize(text) * (fontSize / MathF.Max(1f, ImGui.GetFontSize()));
        DrawTextWithShadow(drawList, min + (size - textSize) * 0.5f, ImGui.GetColorU32(UiColors.TextDim), text, fontSize);
    }

    private void DrawSubTitle(string title)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TextColored(config.AccentColor, title);
        ImGui.Dummy(new Vector2(1f, 3f * scale));
    }

    private static Vector4 Darken(Vector4 color, float amount)
        => new(
            MathF.Max(0f, color.X - amount),
            MathF.Max(0f, color.Y - amount),
            MathF.Max(0f, color.Z - amount),
            color.W);

    private void DrawPreviewSection(string title, string text, Vector4? textColor = null, string? themeName = null)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var palette = ProfileVisuals.ResolvePalette(themeName ?? profileThemeName, UiColors.HexToRgba(NormalizeHex(profileThemeColorHex, "#2BE5B5")), config.WindowBackgroundColor);
        ImGui.Indent(14f * scale);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(palette.Accent, ProfileVisuals.GetSectionIcon(themeName ?? profileThemeName, title));
        }
        ImGui.SameLine();
        ImGui.TextColored(palette.Title, title);
        ImGui.Indent(18f * scale);
        if (textColor.HasValue)
            ImGui.TextColored(textColor.Value, text);
        else
            ImGui.TextColored(palette.Body, text);
        ImGui.Unindent(18f * scale);
        ImGui.Unindent(14f * scale);
        ImGui.Spacing();
    }

    private static void DrawThemeOverlay(ImDrawListPtr drawList, Vector2 pos, Vector2 size, string themeName, ProfileVisuals.ThemePalette palette, float scale)
    {
        var key = (themeName ?? string.Empty).Trim();
        if (string.Equals(key, "Minimal", StringComparison.OrdinalIgnoreCase))
        {
            drawList.AddLine(pos + new Vector2(0f, size.Y - 1f * scale), pos + new Vector2(size.X, size.Y - 1f * scale), ImGui.GetColorU32(UiColors.WithAlpha(palette.Accent, 0.34f)), 1f * scale);
            return;
        }

        if (string.Equals(key, "Glass", StringComparison.OrdinalIgnoreCase))
        {
            drawList.AddRectFilledMultiColor(pos, pos + size, ImGui.GetColorU32(UiColors.WithAlpha(Vector4.One, 0.08f)), ImGui.GetColorU32(UiColors.WithAlpha(Vector4.One, 0.02f)), ImGui.GetColorU32(Vector4.Zero), ImGui.GetColorU32(Vector4.Zero));
            DrawDotOverlay(drawList, pos, size, UiColors.WithAlpha(palette.Accent, 0.06f), scale);
            return;
        }

        if (string.Equals(key, "Royal", StringComparison.OrdinalIgnoreCase))
        {
            drawList.AddCircleFilled(pos + new Vector2(size.X * 0.86f, size.Y * 0.18f), 42f * scale, ImGui.GetColorU32(palette.Glow), 48);
            DrawDotOverlay(drawList, pos, size, UiColors.WithAlpha(palette.Accent, 0.06f), scale);
            return;
        }

        if (string.Equals(key, "Pastel", StringComparison.OrdinalIgnoreCase))
        {
            drawList.AddCircleFilled(pos + new Vector2(size.X * 0.88f, size.Y * 0.24f), 38f * scale, ImGui.GetColorU32(palette.Glow), 48);
            drawList.AddCircleFilled(pos + new Vector2(size.X * 0.72f, size.Y * 0.78f), 24f * scale, ImGui.GetColorU32(UiColors.WithAlpha(palette.Accent, 0.10f)), 36);
            return;
        }

        drawList.AddCircleFilled(pos + new Vector2(size.X * 0.86f, size.Y * 0.22f), 34f * scale, ImGui.GetColorU32(palette.Glow), 42);
        DrawDotOverlay(drawList, pos, size, UiColors.WithAlpha(palette.Accent, 0.08f), scale);
    }

    private void DrawStatusBadge(ContactStatus status, Vector2 center, float radius, bool largeMoon)
    {
        var drawList = ImGui.GetWindowDrawList();
        var scale = ImGuiHelpers.GlobalScale;

        var iconSize = radius * (largeMoon ? 3.0f : 2.8f);
        if (ProfileStatusVisuals.TryDrawGameIcon(drawList, gameIcons, status, center, iconSize, scale))
            return;

        drawList.AddCircleFilled(center, radius + 1.9f * scale, ImGui.GetColorU32(new Vector4(0.010f, 0.018f, 0.015f, 0.96f)), 18);
        drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(GetStatusColor(status)), 18);
        drawList.AddCircle(center, radius + 0.6f * scale, ImGui.GetColorU32(UiColors.WithAlpha(Vector4.One, 0.20f)), 18, 1f * scale);
    }

    private static Vector4 GetStatusColor(ContactStatus status)
        => status switch
        {
            _ => ProfileStatusVisuals.GetColor(status),
        };


    private void DrawFavoriteVenuesSection()
    {
        ffxivVenuesService.EnsureFreshAsync();
        var scale = ImGuiHelpers.GlobalScale;
        DrawSubTitle("Favorite Venues");
        if (!string.IsNullOrWhiteSpace(favoriteVenueTagError))
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.42f, 0.40f, 1f), favoriteVenueTagError);
        }

        var tagWidth = 118f * scale;
        ImGui.SetNextItemWidth(tagWidth);
        var sanitized = SanitizeFavoriteVenueTag(favoriteVenueTagBuffer);
        if (!string.Equals(sanitized, favoriteVenueTagBuffer, StringComparison.Ordinal))
            favoriteVenueTagBuffer = sanitized;
        if (ImGui.InputTextWithHint("##favorite_venue_tag", "Tooltip tag", ref favoriteVenueTagBuffer, 32))
        {
            favoriteVenueTagBuffer = SanitizeFavoriteVenueTag(favoriteVenueTagBuffer);
            if (favoriteVenueTagBuffer.Length <= 20)
            {
                config.FavoriteVenueTooltipTag = favoriteVenueTagBuffer;
                favoriteVenueTagError = string.Empty;
                config.Save();
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Optional tag shown after the venue name on profile tooltips. Letters/numbers only, max 20 characters.");

        ImGui.SameLine();
        var tagColor = UiColors.HexToRgba(NormalizeHex(favoriteVenueTagColorHex, "#B56CFF"));
        if (ImGui.ColorEdit4("##favorite_venue_tag_color", ref tagColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.AlphaPreviewHalf))
        {
            favoriteVenueTagColorHex = ColorToHex(tagColor);
            config.FavoriteVenueTooltipTagColorHex = favoriteVenueTagColorHex;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Tag color: {NormalizeHex(favoriteVenueTagColorHex, "#B56CFF")}");

        ImGui.SameLine();
        var comboWidth = MathF.Max(170f * scale, ImGui.GetContentRegionAvail().X - 34f * scale);
        ImGui.SetNextItemWidth(comboWidth);
        ImGui.SetNextWindowSize(new Vector2(comboWidth, 226f * scale), ImGuiCond.Always);
        if (ImGui.BeginCombo("##favorite_venue_picker", string.IsNullOrWhiteSpace(selectedFavoriteVenueName) ? "Select venue" : selectedFavoriteVenueName))
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##favorite_venue_search", "Search venue name", ref favoriteVenueSearch, 80);
            ImGui.Separator();

            using (var child = ImRaii.Child("favorite-venue-picker-scroll", new Vector2(-1f, 168f * scale), false))
            {
                if (child)
                {
                    var matches = ffxivVenuesService.Search(favoriteVenueSearch, 150);
                    if (matches.Count == 0)
                    {
                        ImGui.TextDisabled("No North America venues found.");
                    }
                    else
                    {
                        foreach (var venue in matches)
                        {
                            if (ImGui.Selectable($"{venue.Name}##favorite_{venue.Id}", string.Equals(selectedFavoriteVenueName, venue.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                selectedFavoriteVenue = venue;
                                selectedFavoriteVenueName = venue.Name;
                                ImGui.CloseCurrentPopup();
                            }
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip(venue.LocationTooltip);
                        }
                    }
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (ImGui.Button(FontAwesomeIcon.Check.ToIconString() + "##add_favorite_venue", new Vector2(28f, 0f) * scale))
                AddSelectedFavoriteVenue();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Add venue to carousel");

        DrawFavoriteVenueCarousel(config.CloudSavedVenues);
    }

    private void AddSelectedFavoriteVenue()
    {
        favoriteVenueTagBuffer = SanitizeFavoriteVenueTag(favoriteVenueTagBuffer);
        if (!string.IsNullOrWhiteSpace(favoriteVenueTagBuffer) && favoriteVenueTagBuffer.Length > 20)
        {
            favoriteVenueTagError = "Tag must have 20 caracteres or less.";
            return;
        }
        favoriteVenueTagError = string.Empty;
        config.FavoriteVenueTooltipTag = favoriteVenueTagBuffer;
        config.FavoriteVenueTooltipTagColorHex = NormalizeHex(favoriteVenueTagColorHex, "#B56CFF");

        var venue = selectedFavoriteVenue ?? ffxivVenuesService.FindByName(selectedFavoriteVenueName);
        if (venue == null)
            return;

        var bookmark = FfxivVenuesService.ToBookmark(venue, favorite: true);
        bookmark.Favorite = true;
        bookmark.TooltipTag = favoriteVenueTagBuffer;
        bookmark.TooltipTagColorHex = NormalizeHex(favoriteVenueTagColorHex, "#B56CFF");
        var existing = config.CloudSavedVenues.FirstOrDefault(v =>
            string.Equals(v.Name, bookmark.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeVenueAddress(v.BuildAddress()), NormalizeVenueAddress(bookmark.BuildAddress()), StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            config.CloudSavedVenues.Add(bookmark);
            QueueVenueImageDownload(bookmark);
        }
        else
        {
            existing.Name = bookmark.Name;
            existing.DataCenter = bookmark.DataCenter;
            existing.World = bookmark.World;
            existing.District = bookmark.District;
            existing.Ward = bookmark.Ward;
            existing.Plot = bookmark.Plot;
            existing.Address = bookmark.Address;
            existing.ImageUrl = bookmark.ImageUrl;
            existing.WebsiteUrl = bookmark.WebsiteUrl;
            existing.TeleportCommand = bookmark.TeleportCommand;
            existing.Favorite = true;
            existing.Source = bookmark.Source;
            existing.TooltipTag = favoriteVenueTagBuffer;
            existing.TooltipTagColorHex = NormalizeHex(favoriteVenueTagColorHex, "#B56CFF");
            QueueVenueImageDownload(existing);
        }

        selectedFavoriteVenueName = string.Empty;
        selectedFavoriteVenue = null;
        config.Save();
    }

    private static bool IsFfxivVenueBookmark(PrivateVenueBookmark venue)
        => string.Equals(venue.Source, "FFXIVVenues", StringComparison.OrdinalIgnoreCase);

    private void DrawFavoriteVenueCarousel(List<PrivateVenueBookmark> venues)
        => DrawFavoriteVenueCarousel(venues, "edit", allowEditing: true);

    private void DrawFavoriteVenueCarousel(List<PrivateVenueBookmark> venues, string idSuffix, bool allowEditing)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var items = venues.Where(IsFfxivVenueBookmark).Where(v => !string.IsNullOrWhiteSpace(v.Name)).ToList();
        if (items.Count == 0)
        {
            ImGui.TextDisabled("No favorite venues selected yet.");
            return;
        }

        foreach (var item in items)
            EnsureVenueBookmarkMetadata(item);

        var imageSize = 64f * scale;
        var spacing = 10f * scale;
        var arrowWidth = 24f * scale;
        var available = ImGui.GetContentRegionAvail().X;
        var visible = Math.Max(1, (int)((available - arrowWidth * 2f - spacing * 2f) / (imageSize + spacing)));
        visible = Math.Min(visible, items.Count);
        var stripWidth = visible * imageSize + Math.Max(0, visible - 1) * spacing;
        var totalWidth = arrowWidth * 2f + spacing * 2f + stripWidth;
        var startPos = ImGui.GetCursorScreenPos();
        var startX = startPos.X + MathF.Max(0f, (available - totalWidth) * 0.5f);
        var y = startPos.Y;
        var drawList = ImGui.GetWindowDrawList();
        var carouselAccent = config.AccentColor;
        if (string.Equals(idSuffix, "preview", StringComparison.OrdinalIgnoreCase))
        {
            var palette = ProfileVisuals.ResolvePalette(profileThemeName, UiColors.HexToRgba(NormalizeHex(profileThemeColorHex, "#2BE5B5")), config.WindowBackgroundColor);
            carouselAccent = palette.Accent;
        }
        favoriteVenueCarouselOffset = NormalizeCarouselOffset(favoriteVenueCarouselOffset, items.Count);

        ImGui.SetCursorScreenPos(new Vector2(startX, y + (imageSize - 20f * scale) * 0.5f));
        DrawCarouselArrow($"fav_venues_left_{idSuffix}", FontAwesomeIcon.ChevronLeft.ToIconString(), arrowWidth, carouselAccent, () => favoriteVenueCarouselOffset = NormalizeCarouselOffset(favoriteVenueCarouselOffset - 1, items.Count));

        for (var i = 0; i < visible; i++)
        {
            var venue = items[(favoriteVenueCarouselOffset + i) % items.Count];
            var pos = new Vector2(startX + arrowWidth + spacing + i * (imageSize + spacing), y);
            var texture = ResolveVenueTexture(venue);

            drawList.AddRectFilled(pos, pos + new Vector2(imageSize), ImGui.GetColorU32(UiColors.WithAlpha(config.WindowBackgroundColor, 0.48f)), 5f * scale);
            if (texture != null)
                drawList.AddImageRounded(texture.Handle, pos, pos + new Vector2(imageSize), Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), 5f * scale);
            else
                DrawTextWithShadow(drawList, pos + new Vector2(22f, 20f) * scale, ImGui.GetColorU32(UiColors.TextDim), "?", 18f * scale);
            drawList.AddRect(pos, pos + new Vector2(imageSize), ImGui.GetColorU32(UiColors.WithAlpha(carouselAccent, 0.46f)), 5f * scale, ImDrawFlags.None, 1f * scale);

            if (allowEditing)
            {
                var starSize = 15.5f * scale;
                var starPad = 4.5f * scale;
                var starPos = pos + new Vector2(imageSize - starSize - starPad, imageSize - starSize - starPad);
                var starColor = venue.Favorite ? UiColors.Favorite : UiColors.WithAlpha(UiColors.TextDim, 0.82f);
                drawList.AddCircleFilled(starPos + new Vector2(starSize * 0.52f), starSize * 0.64f, ImGui.GetColorU32(UiColors.WithAlpha(Vector4.Zero, 0.42f)));
                using (ImRaii.PushFont(UiBuilder.IconFont))
                    drawList.AddText(UiBuilder.IconFont, starSize, starPos, ImGui.GetColorU32(starColor), FontAwesomeIcon.Star.ToIconString());
            }

            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton($"fav_venue_img_{idSuffix}_{venue.Name}_{i}", new Vector2(imageSize));
            if (ImGui.IsItemHovered())
                DrawFavoriteVenueTooltip(venue);
            if (allowEditing && ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                venue.Favorite = !venue.Favorite;
                config.Save();
            }
            if (allowEditing && ImGui.BeginPopupContextItem($"fav_venue_context_{idSuffix}_{venue.Name}_{i}"))
            {
                if (ImGui.MenuItem("Remove"))
                {
                    venues.Remove(venue);
                    if (favoriteVenueCarouselOffset >= venues.Count)
                        favoriteVenueCarouselOffset = NormalizeCarouselOffset(favoriteVenueCarouselOffset, venues.Count);
                    config.Save();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(startX + arrowWidth + spacing + stripWidth + spacing, y + (imageSize - 20f * scale) * 0.5f));
        DrawCarouselArrow($"fav_venues_right_{idSuffix}", FontAwesomeIcon.ChevronRight.ToIconString(), arrowWidth, carouselAccent, () => favoriteVenueCarouselOffset = NormalizeCarouselOffset(favoriteVenueCarouselOffset + 1, items.Count));

        ImGui.SetCursorScreenPos(startPos);
        var carouselBottomPad = string.Equals(idSuffix, "preview", StringComparison.OrdinalIgnoreCase) ? 2f * scale : 22f * scale;
        ImGui.Dummy(new Vector2(available, imageSize + carouselBottomPad));
    }

    private void DrawCarouselArrow(string id, string icon, float width, Vector4 accent, System.Action action)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, 20f * scale);
        ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            action();

        var drawList = ImGui.GetWindowDrawList();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var fontSize = 15.5f * scale;
            var textSize = ImGui.CalcTextSize(icon) * (fontSize / MathF.Max(1f, ImGui.GetFontSize()));
            var color = hovered ? accent : UiColors.WithAlpha(accent, 0.78f);
            drawList.AddText(UiBuilder.IconFont, fontSize, pos + (size - textSize) * 0.5f, ImGui.GetColorU32(color), icon);
        }
    }

    private FfxivVenueEntry? ResolveCatalogVenue(PrivateVenueBookmark venue)
    {
        if (!string.IsNullOrWhiteSpace(venue.DataCenter) || !string.IsNullOrWhiteSpace(venue.World) || venue.Ward > 0 || venue.Plot > 0)
        {
            var byAddress = ffxivVenuesService.FindByAddress(venue.DataCenter, venue.World, venue.District, venue.Ward, venue.Plot);
            if (byAddress != null)
                return byAddress;
        }

        return ffxivVenuesService.FindByName(venue.Name);
    }

    private void EnsureVenueBookmarkMetadata(PrivateVenueBookmark venue)
    {
        var catalog = ResolveCatalogVenue(venue);
        if (catalog == null)
            return;

        var changed = false;
        if (!string.IsNullOrWhiteSpace(catalog.ImageUrl) && !string.Equals(venue.ImageUrl, catalog.ImageUrl, StringComparison.OrdinalIgnoreCase))
        {
            venue.ImageUrl = catalog.ImageUrl;
            venue.ImageLocalPath = string.Empty;
            changed = true;
        }
        else if (!string.IsNullOrWhiteSpace(venue.ImageLocalPath) && !File.Exists(venue.ImageLocalPath))
        {
            venue.ImageLocalPath = string.Empty;
            changed = true;
        }
        if (!string.IsNullOrWhiteSpace(catalog.TeleportCommand) && !string.Equals(venue.TeleportCommand, catalog.TeleportCommand, StringComparison.OrdinalIgnoreCase))
        {
            venue.TeleportCommand = catalog.TeleportCommand;
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(venue.Address))
        {
            venue.Address = catalog.BuildFullLocation();
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(venue.DataCenter)) { venue.DataCenter = catalog.DataCenter; changed = true; }
        if (string.IsNullOrWhiteSpace(venue.World)) { venue.World = catalog.World; changed = true; }
        if (string.IsNullOrWhiteSpace(venue.District)) { venue.District = catalog.District; changed = true; }
        if (venue.Ward <= 0 && catalog.Ward > 0) { venue.Ward = catalog.Ward; changed = true; }
        if (venue.Plot <= 0 && catalog.Plot > 0) { venue.Plot = catalog.Plot; changed = true; }

        if (changed)
            config.Save();
    }


    private void DrawFavoriteVenueTooltip(PrivateVenueBookmark venue)
    {
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(venue.Name);
        var tag = SanitizeFavoriteVenueTag(venue.TooltipTag);
        if (!string.IsNullOrWhiteSpace(tag))
        {
            ImGui.SameLine();
            ImGui.TextColored(UiColors.HexToRgba(NormalizeHex(venue.TooltipTagColorHex, "#B56CFF")), "#" + tag);
        }
        ImGui.TextUnformatted(BuildVenueTooltip(venue));
        ImGui.EndTooltip();
    }

    private string BuildVenueTooltip(PrivateVenueBookmark venue)
    {
        EnsureVenueBookmarkMetadata(venue);
        var location = BuildVenueLocationText(venue);
        return string.IsNullOrWhiteSpace(location) ? "Unknown location" : location;
    }

    private Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? ResolveVenueTexture(PrivateVenueBookmark venue)
    {
        EnsureVenueBookmarkMetadata(venue);

        if (!string.IsNullOrWhiteSpace(venue.ImageLocalPath))
        {
            var fileName = Path.GetFileName(venue.ImageLocalPath);
            if (fileName.StartsWith("venue_", StringComparison.OrdinalIgnoreCase) && fileName.Contains(".cloud.", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".cloud.png", StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(venue.ImageLocalPath);
                venue.ImageLocalPath = string.Empty;
                config.Save();
            }
            else
            {
                var texture = profileImages.GetTexture(venue.ImageLocalPath);
                if (texture != null)
                    return texture;

                if (!File.Exists(venue.ImageLocalPath))
                {
                    venue.ImageLocalPath = string.Empty;
                    config.Save();
                }
                else if (fileName.StartsWith("venue_", StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(venue.ImageLocalPath);
                    venue.ImageLocalPath = string.Empty;
                    config.Save();
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(venue.ImageUrl) && !profileImages.IsRemoteVenueImageTemporarilyFailed(BuildVenueImageCacheKey(venue)))
            QueueVenueImageDownload(venue);

        return null;
    }

    private void QueueVenueImageDownload(PrivateVenueBookmark venue)
    {
        if (string.IsNullOrWhiteSpace(venue.ImageUrl))
            return;
        if (!string.IsNullOrWhiteSpace(venue.ImageLocalPath) && File.Exists(venue.ImageLocalPath))
            return;

        var cacheKey = string.IsNullOrWhiteSpace(BuildVenueImageCacheKey(venue)) ? venue.ImageUrl : BuildVenueImageCacheKey(venue);
        if (!pendingVenueImageDownloads.Add(cacheKey))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var path = await profileImages.DownloadRemoteVenueImageAsync(venue.ImageUrl, cacheKey, CancellationToken.None, venue.ImageUrl).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    venue.ImageLocalPath = path;
                    config.Save();
                }
            }
            finally
            {
                pendingVenueImageDownloads.Remove(cacheKey);
            }
        });
    }

    private static string BuildVenueImageCacheKey(PrivateVenueBookmark venue)
        => string.IsNullOrWhiteSpace(venue.Name) ? venue.ImageUrl : venue.Name + "_" + venue.World + "_" + venue.Ward + "_" + venue.Plot;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static string BuildVenueLocationText(PrivateVenueBookmark venue)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(venue.DataCenter)) parts.Add(venue.DataCenter);
        if (!string.IsNullOrWhiteSpace(venue.World)) parts.Add(venue.World);
        if (!string.IsNullOrWhiteSpace(venue.District)) parts.Add(venue.District);
        if (venue.Ward > 0) parts.Add($"w{venue.Ward}");
        if (venue.Plot > 0) parts.Add($"p{venue.Plot}");
        return string.Join(", ", parts);
    }


    private static string BuildVenueTooltipTitle(PrivateVenueBookmark venue)
    {
        var tag = SanitizeFavoriteVenueTag(venue.TooltipTag);
        return string.IsNullOrWhiteSpace(tag) ? venue.Name : $"{venue.Name} #{tag}";
    }

    private static string SanitizeFavoriteVenueTag(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = new List<char>();
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
                chars.Add(c);
        }

        return new string(chars.ToArray());
    }

    private static int NormalizeCarouselOffset(int value, int count)
    {
        if (count <= 0)
            return 0;
        value %= count;
        return value < 0 ? value + count : value;
    }

    private static string NormalizeVenueAddress(string value)
        => System.Text.RegularExpressions.Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");

    private void DrawVenuesSection()
    {
        ImGui.TextColored(config.AccentColor, "Venues");
        var helpX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - 18f * ImGuiHelpers.GlobalScale;
        ImGui.SameLine(helpX);
        ImGui.TextDisabled("?");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Saved venues is storaged between users to enhance\nlocalization precision by showing venue\nnames/address when possible");
        }

        ImGui.SetNextItemWidth(118f * ImGuiHelpers.GlobalScale);
        ImGui.TextUnformatted("Data Center:");
        ImGui.SameLine();
        DrawDataCenterCombo();

        ImGui.SameLine();
        ImGui.TextUnformatted("World:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(126f * ImGuiHelpers.GlobalScale);
        DrawWorldCombo();

        ImGui.TextUnformatted("District:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(118f * ImGuiHelpers.GlobalScale);
        DrawStringCombo("##venue_district", ref selectedDistrict, Districts);

        ImGui.SameLine();
        ImGui.TextUnformatted("Ward:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(58f * ImGuiHelpers.GlobalScale);
        DrawNumberCombo("##venue_ward", ref selectedWard, 1, 30);

        ImGui.SameLine();
        ImGui.TextUnformatted("Plot:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(58f * ImGuiHelpers.GlobalScale);
        DrawNumberCombo("##venue_plot", ref selectedPlot, 1, 60);

        ImGui.SetNextItemWidth(-1f);
        using (ImRaii.PushColor(ImGuiCol.Border, UiColors.WithAlpha(config.AccentColor, 0.62f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 1f * ImGuiHelpers.GlobalScale))
            ImGui.InputTextWithHint("##venue_name", "Venue name", ref venueNameBuffer, 64);

        var address = BuildVenueAddress();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 72f * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(true))
        {
            var preview = address;
            ImGui.InputText("##venue_address_preview", ref preview, 256);
        }

        ImGui.SameLine();
        if (ImGui.Button("Save", new Vector2(62f, 0f) * ImGuiHelpers.GlobalScale))
        {
            var name = string.IsNullOrWhiteSpace(venueNameBuffer) ? address : venueNameBuffer.Trim();
            var existing = config.CloudSavedVenues.FirstOrDefault(v => string.Equals(v.Address, address, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                config.CloudSavedVenues.Add(new PrivateVenueBookmark
                {
                    Name = name,
                    DataCenter = selectedDataCenter,
                    World = selectedWorld,
                    District = selectedDistrict,
                    Ward = selectedWard,
                    Plot = selectedPlot,
                    Address = address,
                });
            }
            else
            {
                existing.Name = name;
                existing.DataCenter = selectedDataCenter;
                existing.World = selectedWorld;
                existing.District = selectedDistrict;
                existing.Ward = selectedWard;
                existing.Plot = selectedPlot;
                existing.Address = address;
            }

            venueNameBuffer = string.Empty;
            config.Save();
        }

        ImGui.TextColored(config.AccentColor, "Saved venues:");
        if (config.CloudSavedVenues.Count == 0)
        {
            ImGui.TextDisabled("No venues saved yet.");
            return;
        }

        foreach (var venue in config.CloudSavedVenues.ToList())
        {
            ImGui.TextUnformatted($"{venue.Name} - {venue.Address}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"X##delete_venue_{venue.Address}"))
            {
                config.CloudSavedVenues.Remove(venue);
                config.Save();
            }
        }
    }

    private void DrawDataCenterCombo()
    {
        ImGui.SetNextItemWidth(118f * ImGuiHelpers.GlobalScale);
        if (!ImGui.BeginCombo("##venue_data_center", selectedDataCenter))
            return;

        foreach (var region in DataCentersByRegion)
        {
            if (ImGui.BeginMenu(region.Key))
            {
                foreach (var dc in region.Value.Keys)
                {
                    if (ImGui.MenuItem(dc))
                    {
                        selectedDataCenter = dc;
                        selectedWorld = region.Value[dc][0];
                    }
                }

                ImGui.EndMenu();
            }
        }

        ImGui.EndCombo();
    }

    private void DrawWorldCombo()
    {
        var worlds = GetWorldsForDataCenter(selectedDataCenter);
        if (!worlds.Contains(selectedWorld, StringComparer.OrdinalIgnoreCase))
            selectedWorld = worlds.FirstOrDefault() ?? string.Empty;

        DrawStringCombo("##venue_world", ref selectedWorld, worlds);
    }

    private static string[] GetWorldsForDataCenter(string dataCenter)
    {
        foreach (var region in DataCentersByRegion.Values)
        {
            if (region.TryGetValue(dataCenter, out var worlds))
                return worlds;
        }

        return [];
    }

    private static void DrawStringCombo(string id, ref string current, IReadOnlyList<string> values)
    {
        if (!ImGui.BeginCombo(id, current))
            return;

        foreach (var value in values)
        {
            if (ImGui.Selectable(value, string.Equals(current, value, StringComparison.Ordinal)))
                current = value;
        }

        ImGui.EndCombo();
    }

    private static void DrawNumberCombo(string id, ref int current, int min, int max)
    {
        if (!ImGui.BeginCombo(id, current.ToString()))
            return;

        for (var i = min; i <= max; i++)
        {
            if (ImGui.Selectable(i.ToString(), current == i))
                current = i;
        }

        ImGui.EndCombo();
    }

    private string BuildVenueAddress()
    {
        var district = selectedDistrict.Equals("Lavender Beds", StringComparison.OrdinalIgnoreCase) ? "Lb" : selectedDistrict;
        return $"{selectedDataCenter} {selectedWorld} {district} w{selectedWard} p{selectedPlot}";
    }

    private void OpenAvatarPicker()
    {
        fileDialog.OpenFileDialog(
            "Choose profile picture",
            "Image files{.png,.jpg,.jpeg,.webp}",
            (success, paths) =>
            {
                if (!success || paths.Count == 0)
                    return;

                if (profileImages.TryImportImage(paths[0], "my-cloud-profile", out var storedPath, out var error))
                {
                    localAvatarPath = storedPath;
                    statusMessage = "Profile picture selected. Click Save Profile to upload it.";
                }
                else
                {
                    statusMessage = error;
                }
            },
            1,
            null,
            true);
    }

    private async Task<string> SaveProfileAsync(string displayName, string displayNameEffect, string bio, string profileStatus, string statusColorHex, string avatarPath, string themeName, string themeColorHex, string bannerUrl, string borderColorHex, string placeholderStyle)
    {
        var avatarUrl = config.CloudProfileAvatarUrl;
        if (!string.IsNullOrWhiteSpace(avatarPath))
        {
            var upload = await cloudService.UploadAvatarAsync(avatarPath).ConfigureAwait(false);
            if (!upload.Success)
                return upload.Error;

            avatarUrl = upload.AvatarUrl;
            config.CloudProfileAvatarLocalPath = avatarPath;
        }

        config.ProfileStatusVisibility = ProfileVisuals.NormalizeVisibility(profileStatusVisibility, "All");
        config.ProfileVenuesVisibility = ProfileVisuals.NormalizeVisibility(profileVenuesVisibility, "All");
        config.ProfileLocationVisibility = ProfileVisuals.NormalizeVisibility(profileLocationVisibility, "All");
        config.ProfileBioVisibility = ProfileVisuals.NormalizeVisibility(profileBioVisibility, "All");
        var result = await cloudService.SaveCloudProfileAsync(displayName, displayNameEffect, bio, profileStatus, statusColorHex, avatarUrl, themeName, themeColorHex, bannerUrl, borderColorHex, placeholderStyle).ConfigureAwait(false);
        fieldsInitialized = false;
        return result;
    }

    private void EnsureProfileFieldsInitialized()
    {
        if (fieldsInitialized)
            return;

        profileDisplayName = string.IsNullOrWhiteSpace(config.CloudProfileDisplayName)
            ? config.CloudDisplayName
            : config.CloudProfileDisplayName;
        profileBio = config.CloudProfileBio;
        profileStatusMessage = config.CloudProfileStatusMessage;
        profileStatusColorHex = NormalizeHex(config.CloudProfileStatusColorHex, "#2BE5B5");
        localAvatarPath = config.CloudProfileAvatarLocalPath;
        profileThemeName = ProfileVisuals.NormalizeThemeName(config.CloudProfileThemeName);
        profileThemeColorHex = NormalizeHex(config.CloudProfileThemeColorHex, "#2BE5B5");
        profileDisplayNameEffect = ProfileNameEffects.Normalize(config.CloudProfileDisplayNameEffect);
        profileBannerUrl = config.CloudProfileBannerUrl;
        profileAvatarBorderColorHex = NormalizeHex(config.CloudProfileAvatarBorderColorHex, "#2BE5B5");
        profileAvatarPlaceholderStyle = string.IsNullOrWhiteSpace(config.CloudProfileAvatarPlaceholderStyle) ? "Question Mark" : config.CloudProfileAvatarPlaceholderStyle;
        profileStatusVisibility = ProfileVisuals.NormalizeVisibility(config.ProfileStatusVisibility, "All");
        profileVenuesVisibility = ProfileVisuals.NormalizeVisibility(config.ProfileVenuesVisibility, "All");
        profileLocationVisibility = ProfileVisuals.NormalizeVisibility(config.ProfileLocationVisibility, "All");
        profileBioVisibility = ProfileVisuals.NormalizeVisibility(config.ProfileBioVisibility, "All");
        fieldsInitialized = true;
    }

    private bool HasUnsavedProfileChanges()
    {
        return !string.Equals(profileDisplayName.Trim(), (config.CloudProfileDisplayName ?? string.Empty).Trim(), StringComparison.Ordinal) ||
               !string.Equals(profileBio.Trim(), (config.CloudProfileBio ?? string.Empty).Trim(), StringComparison.Ordinal) ||
               !string.Equals(profileStatusMessage.Trim(), (config.CloudProfileStatusMessage ?? string.Empty).Trim(), StringComparison.Ordinal) ||
               !string.Equals(NormalizeHex(profileStatusColorHex, "#2BE5B5"), NormalizeHex(config.CloudProfileStatusColorHex, "#2BE5B5"), StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(ProfileVisuals.NormalizeThemeName(profileThemeName), ProfileVisuals.NormalizeThemeName(config.CloudProfileThemeName), StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(NormalizeHex(profileThemeColorHex, "#2BE5B5"), NormalizeHex(config.CloudProfileThemeColorHex, "#2BE5B5"), StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(ProfileNameEffects.Normalize(profileDisplayNameEffect), ProfileNameEffects.Normalize(config.CloudProfileDisplayNameEffect), StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(profileBannerUrl.Trim(), (config.CloudProfileBannerUrl ?? string.Empty).Trim(), StringComparison.Ordinal) ||
               !string.Equals(NormalizeHex(profileAvatarBorderColorHex, "#2BE5B5"), NormalizeHex(config.CloudProfileAvatarBorderColorHex, "#2BE5B5"), StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(profileAvatarPlaceholderStyle, string.IsNullOrWhiteSpace(config.CloudProfileAvatarPlaceholderStyle) ? "Question Mark" : config.CloudProfileAvatarPlaceholderStyle, StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(profileStatusVisibility, ProfileVisuals.NormalizeVisibility(config.ProfileStatusVisibility, "All"), StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(profileVenuesVisibility, ProfileVisuals.NormalizeVisibility(config.ProfileVenuesVisibility, "All"), StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(profileLocationVisibility, ProfileVisuals.NormalizeVisibility(config.ProfileLocationVisibility, "All"), StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(profileBioVisibility, ProfileVisuals.NormalizeVisibility(config.ProfileBioVisibility, "All"), StringComparison.OrdinalIgnoreCase);
    }

    private void RunCloudAction(Func<Task<string>> action)
    {
        if (busy) return;

        busy = true;
        statusMessage = string.Empty;

        try
        {
            var task = action();
            _ = CompleteCloudActionAsync(task);
        }
        catch (Exception ex)
        {
            statusMessage = $"Cloud action failed: {ex.Message}";
            busy = false;
        }
    }

    private async Task CompleteCloudActionAsync(Task<string> task)
    {
        try
        {
            statusMessage = await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            statusMessage = $"Cloud action failed: {ex.Message}";
        }
        finally
        {
            busy = false;
        }
    }


    private string BuildPreviewLocationLine(string currentWorld, string currentDataCenter, out string residentialDetails)
    {
        residentialDetails = string.Empty;

        if (clientState.IsLoggedIn)
        {
            var snapshot = GameLocationResolver.GetCurrent(dataManager, clientState);
            residentialDetails = snapshot.ResidentialDetails;
            var parts = new List<string>();
            AddPreviewLocationPart(parts, currentDataCenter);
            AddPreviewLocationPart(parts, currentWorld);
            AddPreviewLocationPart(parts, snapshot.Zone);
            return parts.Count == 0 ? "Current location unavailable." : string.Join(", ", parts);
        }

        var fallback = new List<string>();
        AddPreviewLocationPart(fallback, currentDataCenter);
        AddPreviewLocationPart(fallback, currentWorld);
        return fallback.Count == 0 ? "Current location appears after login." : string.Join(", ", fallback);
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

    private static string ResolveDataCenterForWorld(string world)
    {
        if (string.IsNullOrWhiteSpace(world))
            return string.Empty;

        foreach (var region in DataCentersByRegion.Values)
        {
            foreach (var pair in region)
            {
                if (pair.Value.Any(w => string.Equals(w, world, StringComparison.OrdinalIgnoreCase)))
                    return pair.Key;
            }
        }

        return string.Empty;
    }

    private static void AddPreviewLocationPart(List<string> parts, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var normalized = value.Trim().Replace("The Lavender Beds", "Lavender Beds", StringComparison.OrdinalIgnoreCase);
        if (parts.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        parts.Add(normalized);
    }

    private static string FormatVenueLocationText(string text)
    {
        var value = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Replace(" / ", ", ", StringComparison.Ordinal);
        value = value.Replace(" - ", ", ", StringComparison.Ordinal);
        while (value.Contains(", ,", StringComparison.Ordinal))
            value = value.Replace(", ,", ",", StringComparison.Ordinal);

        return value;
    }

    private static string NormalizeHex(string? value, string fallback)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.StartsWith('#'))
            text = text[1..];

        if (text.Length != 6 || text.Any(ch => !Uri.IsHexDigit(ch)))
            return fallback;

        return "#" + text.ToUpperInvariant();
    }

    private static string ColorToHex(Vector4 color)
    {
        var r = Math.Clamp((int)MathF.Round(color.X * 255f), 0, 255);
        var g = Math.Clamp((int)MathF.Round(color.Y * 255f), 0, 255);
        var b = Math.Clamp((int)MathF.Round(color.Z * 255f), 0, 255);
        return $"#{r:X2}{g:X2}{b:X2}";
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


    private static string GetStatusDisplayName(ContactStatus status)
        => status switch
        {
            ContactStatus.Afk => "AFK",
            ContactStatus.Busy => "Busy",
            ContactStatus.Content => "Content",
            ContactStatus.Streaming => "Streaming",
            ContactStatus.RolePlaying => "Role-Playing",
            ContactStatus.Online => "Online",
            _ => "Offline",
        };

    private static void DrawDotOverlay(ImDrawListPtr drawList, Vector2 pos, Vector2 size, Vector4 color, float scale)
    {
        var dotColor = ImGui.GetColorU32(color);
        var radius = 1.05f * scale;
        var spacing = 18f * scale;
        var inset = 10f * scale;

        for (var y = pos.Y + inset; y < pos.Y + size.Y - inset; y += spacing)
        {
            for (var x = pos.X + inset; x < pos.X + size.X - inset; x += spacing)
                drawList.AddCircleFilled(new Vector2(x, y), radius, dotColor, 6);
        }
    }

    private static string TrimToWidth(string text, float maxWidth, float fontSize)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (GetScaledTextWidth(text, fontSize) <= maxWidth)
            return text;

        const string suffix = "...";
        var available = MathF.Max(0f, maxWidth - GetScaledTextWidth(suffix, fontSize));
        var end = text.Length;
        while (end > 0 && GetScaledTextWidth(text[..end], fontSize) > available)
            end--;

        return end <= 0 ? suffix : text[..end] + suffix;
    }

    private static float GetScaledTextWidth(string text, float fontSize)
    {
        var baseFontSize = MathF.Max(1f, ImGui.GetFontSize());
        return ImGui.CalcTextSize(text).X * (fontSize / baseFontSize);
    }

    private static void DrawTextWithShadow(ImDrawListPtr drawList, Vector2 pos, uint color, string text, float size)
    {
        var font = ImGui.GetFont();
        var shadow = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.78f));
        drawList.AddText(font, size, pos + new Vector2(1.2f, 1.2f), shadow, text);
        drawList.AddText(font, size, pos, color, text);
    }
}
