using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Privacy.Models;
using Privacy.Services;
using Privacy.UI;
using System;
using System.Linq;
using System.Numerics;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Privacy.Windows;

internal sealed class OnlineProfileWindow : Window
{
    private readonly Configuration config;
    private readonly ProfileImageCache profileImages;
    private readonly GameIconCache gameIcons;
    private readonly FfxivVenuesService ffxivVenuesService;
    private PrivateContact? contact;
    private int venueCarouselOffset;
    private readonly HashSet<string> pendingVenueImageDownloads = new(StringComparer.OrdinalIgnoreCase);
    private string activeAvatarContactId = string.Empty;
    private string activeAvatarUrl = string.Empty;
    private string activeAvatarVersion = string.Empty;
    private bool avatarDownloadInProgress;
    private readonly HashSet<string> pendingBannerDownloads = new(StringComparer.OrdinalIgnoreCase);
    private int pushedColorCount;
    private int pushedStyleVarCount;

    public OnlineProfileWindow(Configuration config, ProfileImageCache profileImages, GameIconCache gameIcons, FfxivVenuesService ffxivVenuesService)
        : base("Online Profile###PrivacyOnlineProfile")
    {
        this.config = config;
        this.profileImages = profileImages;
        this.gameIcons = gameIcons;
        this.ffxivVenuesService = ffxivVenuesService;
        Size = new Vector2(460f, 430f);
        SizeCondition = ImGuiCond.FirstUseEver;

        WindowBuilder.For(this)
            .AllowPinning(true)
            .AllowClickthrough(false)
            .SetSizeConstraints(new Vector2(420f, 330f), new Vector2(660f, 760f))
            .AddFlags(ImGuiWindowFlags.NoDocking)
            .Apply();
    }

    public void Open(PrivateContact selectedContact)
    {
        contact = selectedContact;
        TryQueueCloudAvatarDownload(selectedContact);
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
        PushColor(ImGuiCol.PopupBg, UiColors.Get("PrivatePopupBg"));
        PushColor(ImGuiCol.Border, UiColors.WithAlpha(config.AccentColor, 0.55f));
        PushColor(ImGuiCol.FrameBg, UiColors.Get("PrivateFrameBg"));
        PushColor(ImGuiCol.FrameBgHovered, UiColors.Get("PrivateFrameBgHovered"));
        PushColor(ImGuiCol.FrameBgActive, UiColors.Get("PrivateFrameBgActive"));
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

        PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4f, 0f) * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.6f * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 4f) * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f, 6f) * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.WindowRounding, 7f * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.ChildRounding, 4f * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.FrameRounding, 5f * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f * ImGuiHelpers.GlobalScale);
    }

    public override void PostDraw()
    {
        if (pushedStyleVarCount > 0) ImGui.PopStyleVar(pushedStyleVarCount);
        if (pushedColorCount > 0) ImGui.PopStyleColor(pushedColorCount);
    }

    public override void Draw()
    {
        var palette = ResolvePalette();
        var headerTop = new Vector4(
            MathF.Min(1f, palette.WindowBackground.X + 0.028f),
            MathF.Min(1f, palette.WindowBackground.Y + 0.028f),
            MathF.Min(1f, palette.WindowBackground.Z + 0.028f),
            MathF.Min(1f, MathF.Max(0.38f, palette.WindowBackground.W)));
        var headerBottom = new Vector4(palette.WindowBackground.X, palette.WindowBackground.Y, palette.WindowBackground.Z, 0f);
        WindowEdgeFade.DrawUnified(palette.WindowBackground, palette.Accent, headerTop, headerBottom, 58f * ImGuiHelpers.GlobalScale, 5f * ImGuiHelpers.GlobalScale);
        var themePos = ImGui.GetCursorScreenPos();
        var themeSize = ImGui.GetContentRegionAvail();
        ProfileVisuals.DrawThemeBackdrop(ImGui.GetWindowDrawList(), themePos, themeSize, contact?.CloudThemeName ?? "Default", palette, ImGuiHelpers.GlobalScale);

        using var body = ImRaii.Child("online-profile-body", new Vector2(-1f, -1f), false);
        if (!body) return;

        if (contact == null)
        {
            ImGui.TextDisabled("No profile selected.");
            return;
        }

        TryQueueCloudAvatarDownload(contact);
        DrawProfile(contact);
    }

    private void DrawHeader()
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = 58f * ImGuiHelpers.GlobalScale;
        var top = new Vector4(
            MathF.Min(1f, config.WindowBackgroundColor.X + 0.028f),
            MathF.Min(1f, config.WindowBackgroundColor.Y + 0.028f),
            MathF.Min(1f, config.WindowBackgroundColor.Z + 0.028f),
            MathF.Min(1f, MathF.Max(0.38f, config.WindowBackgroundColor.W)));
        var bottom = new Vector4(config.WindowBackgroundColor.X, config.WindowBackgroundColor.Y, config.WindowBackgroundColor.Z, 0f);

        drawList.AddRectFilledMultiColor(pos, pos + new Vector2(width, height), ImGui.GetColorU32(top), ImGui.GetColorU32(top), ImGui.GetColorU32(bottom), ImGui.GetColorU32(bottom));
        DrawTextWithShadow(drawList, pos + new Vector2(20f, 13f) * ImGuiHelpers.GlobalScale, ImGui.GetColorU32(config.AccentColor), "Online Profile", 20f * ImGuiHelpers.GlobalScale);
        ImGui.Dummy(new Vector2(width, height));
    }

    private void DrawProfile(PrivateContact profile)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var contentWidth = ImGui.GetContentRegionAvail().X;

        DrawProfileOverlay(drawList, profile, contentWidth, scale);

        DrawLowerContentDecor(drawList, profile, contentWidth, scale);
        ImGui.Spacing();
        if (!IsHiddenByPermission(profile.CloudVenuesVisibility))
            DrawOnlineFavoriteVenues(profile);
        else
            DrawSection("Favorite Venues", "This user is not sharing venues.");

        if (!IsHiddenByPermission(profile.CloudStatusVisibility))
            DrawSection("Status", string.IsNullOrWhiteSpace(profile.CloudStatusMessage) ? "No status message set." : profile.CloudStatusMessage, ResolveStatusMessageColor(profile));
        else
            DrawSection("Status", "This user is not sharing status.");

        if (!IsHiddenByPermission(profile.CloudBioVisibility))
            DrawSection("Bio", string.IsNullOrWhiteSpace(profile.CloudBio) ? "No bio set." : profile.CloudBio);
        else
            DrawSection("Bio", "This user is not sharing bio.");

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(ResolvePalette(profile).Accent, ProfileVisuals.GetSectionIcon(profile.CloudThemeName, "Location"));
        }
        ImGui.SameLine();
        ImGui.TextColored(ResolvePalette(profile).Title, "Location");
        var locationText = profile.Status == ContactStatus.Offline
            ? "Currently offline or out of reach.."
            : (string.IsNullOrWhiteSpace(profile.DisplayLocation) ? "Unknown location." : FormatProfileLocation(profile.DisplayLocation));
        ImGui.TextColored(ResolvePalette(profile).Subtitle, IsHiddenByPermission(profile.CloudLocationVisibility) ? "This user is not sharing location." : locationText);

        ImGui.Spacing();
        ImGui.TextDisabled(profile.CloudLastSyncedAt == DateTimeOffset.MinValue
            ? "Cloud profile has not been synced recently."
            : $"Last synced: {profile.CloudLastSyncedAt.LocalDateTime:g}");
    }

    private static string FormatProfileLocation(string text)
    {
        var value = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        value = value.Replace(" / ", ", ", StringComparison.Ordinal).Replace(" - ", ", ", StringComparison.Ordinal);
        while (value.Contains(", ,", StringComparison.Ordinal))
            value = value.Replace(", ,", ",", StringComparison.Ordinal);
        return value;
    }

    private void DrawProfileOverlay(ImDrawListPtr drawList, PrivateContact profile, float contentWidth, float scale)
    {
        var cursor = ImGui.GetCursorScreenPos();
        var protrude = 2f * scale;
        var pos = cursor + new Vector2(-protrude, 0f);
        var size = new Vector2(contentWidth + protrude * 2f, 126f * scale);
        var rounding = 7f * scale;

        var borderPad = 1f * scale;
        var palette = ResolvePalette(profile);
        var themeAccent = palette.Accent;
        var barFill = UiColors.WithAlpha(palette.Panel, 0.78f);
        var barOverlay = palette.Overlay;
        drawList.AddRectFilled(pos - new Vector2(borderPad, borderPad), pos + size + new Vector2(borderPad, borderPad), ImGui.GetColorU32(barFill), rounding + borderPad);
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(barOverlay), rounding);
        var bannerTexture = ResolveBannerTexture(profile);
        if (bannerTexture != null)
            drawList.AddImageRounded(bannerTexture.Handle, pos, pos + size, Vector2.Zero, Vector2.One, ImGui.GetColorU32(UiColors.WithAlpha(Vector4.One, 0.42f)), rounding);
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(UiColors.WithAlpha(themeAccent, 0.42f)), rounding, ImDrawFlags.None, 1f * scale);
        ProfileVisuals.DrawThemeOverlay(drawList, pos, size, profile.CloudThemeName, palette, scale);

        var avatarSize = new Vector2(88f, 88f) * scale;
        var avatarMin = pos + new Vector2(10f, 19f) * scale;
        var avatarMax = avatarMin + avatarSize;
        var texture = GetCloudProfileTexture(profile);

        drawList.AddRectFilled(avatarMin, avatarMax, ImGui.GetColorU32(UiColors.WithAlpha(palette.PanelAlt, 0.58f)), 7f * scale);
        if (texture != null)
            drawList.AddImageRounded(texture.Handle, avatarMin, avatarMax, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), 7f * scale);
        else
            DrawAvatarPlaceholder(drawList, profile, avatarMin, avatarSize, scale);
        var avatarBorder = UiColors.HexToRgba(NormalizeHex(profile.CloudAvatarBorderColorHex, "#2BE5B5"));
        drawList.AddRect(avatarMin, avatarMax, ImGui.GetColorU32(UiColors.WithAlpha(avatarBorder, 0.72f)), 7f * scale, ImDrawFlags.None, 1.4f * scale);

        var textMin = new Vector2(avatarMax.X + 12f * scale, avatarMin.Y + 4f * scale);
        var textMax = pos + size - new Vector2(10f, 12f) * scale;
        var textWidth = MathF.Max(40f * scale, textMax.X - textMin.X);
        var displayName = profile.CloudDisplayName.Length > 0 ? profile.CloudDisplayName : profile.DisplayName;
        var identity = string.IsNullOrWhiteSpace(profile.World) ? profile.Name : $"{profile.Name}@{profile.World}";
        var statusText = GetStatusDisplayName(profile.Status);
        var statusColor = GetStatusColor(profile.Status);

        drawList.PushClipRect(textMin - new Vector2(1f, 1f) * scale, textMax, true);
        var displayFontSize = 15.2f * scale;
        var displayVisible = TrimToWidth(displayName, textWidth, displayFontSize);
        ProfileNameEffects.DrawEffectText(drawList, textMin, displayVisible, profile.CloudDisplayNameEffect, palette.Title, displayFontSize, scale);
        DrawVenueBadgeBesideName(drawList, profile, textMin, displayVisible, textWidth, palette, scale);
        DrawTextWithShadow(drawList, textMin + new Vector2(0f, 24f) * scale, ImGui.GetColorU32(palette.Subtitle), TrimToWidth(identity, textWidth, 13.3f * scale), 13.3f * scale);
        DrawTextWithShadow(drawList, textMin + new Vector2(0f, 48f) * scale, ImGui.GetColorU32(UiColors.WithAlpha(statusColor, 0.96f)), statusText, 13.3f * scale);
        if (avatarDownloadInProgress && !string.IsNullOrWhiteSpace(profile.CloudAvatarUrl))
            DrawTextWithShadow(drawList, textMin + new Vector2(0f, 72f) * scale, ImGui.GetColorU32(UiColors.TextDim), "Loading profile picture...", 12.6f * scale);
        drawList.PopClipRect();

        ImGui.Dummy(new Vector2(contentWidth, size.Y + 8f * scale));
    }


    private void DrawVenueBadgeBesideName(ImDrawListPtr drawList, PrivateContact profile, Vector2 namePos, string visibleName, float textWidth, ProfileVisuals.ThemePalette palette, float scale)
    {
        if (profile.Status == ContactStatus.Offline)
            return;

        var venueName = ResolveVenueNameForProfile(profile);
        if (string.IsNullOrWhiteSpace(venueName))
            return;

        var fontSize = 12.6f * scale;
        var nameWidth = ImGui.CalcTextSize(visibleName).X * (15.2f * scale / MathF.Max(1f, ImGui.GetFontSize()));
        var badgePos = namePos + new Vector2(nameWidth + 8f * scale, 2.2f * scale);
        var available = MathF.Max(0f, textWidth - nameWidth - 10f * scale);
        if (available < 32f * scale)
            return;

        var text = TrimToWidth($"@At {venueName}", available, fontSize);
        DrawTextWithShadow(drawList, badgePos, ImGui.GetColorU32(UiColors.WithAlpha(palette.Accent, 0.88f)), text, fontSize);
    }

    private string ResolveVenueNameForProfile(PrivateContact profile)
    {
        var address = BuildProfileVenueAddress(profile);
        if (string.IsNullOrWhiteSpace(address))
            return string.Empty;

        var venues = (profile.CloudVenues ?? new List<PrivateVenueBookmark>())
            .Concat(config.CloudSavedVenues ?? new List<PrivateVenueBookmark>())
            .Where(v => !string.IsNullOrWhiteSpace(v.Name));

        var normalizedAddress = NormalizeVenueAddress(address);
        var match = venues.FirstOrDefault(v =>
            string.Equals(NormalizeVenueAddress(v.Address), normalizedAddress, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeVenueAddress(v.BuildAddress()), normalizedAddress, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            return match.Name;

        var dataCenter = string.IsNullOrWhiteSpace(profile.CurrentDataCenter) ? profile.DataCenter : profile.CurrentDataCenter;
        var world = string.IsNullOrWhiteSpace(profile.CurrentWorld) ? profile.World : profile.CurrentWorld;
        var district = ResolveResidentialDistrict(profile);
        var wardText = ExtractHousingNumber(profile.ResidentialDetails, "Ward", "w");
        var plotText = ExtractHousingNumber(profile.ResidentialDetails, "Plot", "p");
        var ward = 0;
        var plot = 0;
        var hasWard = int.TryParse(wardText, out ward);
        var hasPlot = int.TryParse(plotText, out plot);
        if (hasWard && hasPlot)
            return ffxivVenuesService.FindByAddress(dataCenter, world, district, ward, plot)?.Name ?? string.Empty;

        return string.Empty;
    }

    private static string BuildProfileVenueAddress(PrivateContact profile)
    {
        var dataCenter = string.IsNullOrWhiteSpace(profile.CurrentDataCenter) ? profile.DataCenter : profile.CurrentDataCenter;
        var world = string.IsNullOrWhiteSpace(profile.CurrentWorld) ? profile.World : profile.CurrentWorld;
        var district = ResolveResidentialDistrict(profile);
        var ward = ExtractHousingNumber(profile.ResidentialDetails, "Ward", "w");
        var plot = ExtractHousingNumber(profile.ResidentialDetails, "Plot", "p");
        if (string.IsNullOrWhiteSpace(dataCenter) || string.IsNullOrWhiteSpace(world) || string.IsNullOrWhiteSpace(district) || string.IsNullOrWhiteSpace(ward) || string.IsNullOrWhiteSpace(plot))
            return string.Empty;
        return $"{dataCenter} {world} {district} w{ward} p{plot}";
    }

    private static string ResolveResidentialDistrict(PrivateContact profile)
    {
        var text = $"{profile.LastKnownZone} {profile.ResidentialDetails}";
        if (text.Contains("Mist", StringComparison.OrdinalIgnoreCase)) return "Mist";
        if (text.Contains("Lavender", StringComparison.OrdinalIgnoreCase) || text.Contains("Lavander", StringComparison.OrdinalIgnoreCase)) return "Lb";
        if (text.Contains("Goblet", StringComparison.OrdinalIgnoreCase)) return "Goblet";
        if (text.Contains("Shirogane", StringComparison.OrdinalIgnoreCase)) return "Shirogane";
        if (text.Contains("Empyreum", StringComparison.OrdinalIgnoreCase)) return "Empyreum";
        return string.Empty;
    }

    private static string ExtractHousingNumber(string text, string longMarker, string shortMarker)
    {
        var value = ExtractNumberAfter(text, longMarker);
        return string.IsNullOrWhiteSpace(value) ? ExtractNumberAfter(text, shortMarker) : value;
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

    private static string NormalizeVenueAddress(string value)
        => (value ?? string.Empty)
            .Replace("Lavender Beds", "Lb", StringComparison.OrdinalIgnoreCase)
            .Replace("Lavander Beds", "Lb", StringComparison.OrdinalIgnoreCase)
            .Replace("The Lavender Beds", "Lb", StringComparison.OrdinalIgnoreCase)
            .Replace(",", " ", StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();

    private static bool IsHiddenByPermission(string visibility)
        => string.Equals(visibility, "Nobody", StringComparison.OrdinalIgnoreCase);

    private void DrawAvatarPlaceholder(ImDrawListPtr drawList, PrivateContact profile, Vector2 avatarMin, Vector2 avatarSize, float scale)
    {
        var style = string.IsNullOrWhiteSpace(profile.CloudAvatarPlaceholderStyle) ? "Question Mark" : profile.CloudAvatarPlaceholderStyle;
        var text = style switch
        {
            "Initials" => BuildInitials(profile.CloudDisplayName.Length > 0 ? profile.CloudDisplayName : profile.DisplayName),
            "User Icon" => FontAwesomeIcon.User.ToIconString(),
            "Sparkle" => char.ConvertFromUtf32(0xF005),
            _ => "?",
        };

        var color = ImGui.GetColorU32(UiColors.WithAlpha(ProfileVisuals.ResolveAccent(profile.CloudThemeName, config.AccentColor), 0.72f));
        var fontSize = style == "Initials" ? 20f * scale : 23f * scale;
        if (style is "User Icon" or "Sparkle")
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                var textSize = ImGui.CalcTextSize(text) * (fontSize / MathF.Max(1f, ImGui.GetFontSize()));
                drawList.AddText(UiBuilder.IconFont, fontSize, avatarMin + (avatarSize - textSize) * 0.5f, color, text);
            }
            return;
        }

        DrawTextWithShadow(drawList, avatarMin + (avatarSize - ImGui.CalcTextSize(text)) * 0.5f, color, text, fontSize);
    }

    private static string BuildInitials(string name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0].Length > 0 ? parts[0][0].ToString().ToUpperInvariant() : "?";
        return (parts[0][0].ToString() + parts[^1][0]).ToUpperInvariant();
    }

    private void DrawLowerContentDecor(ImDrawListPtr drawList, PrivateContact profile, float contentWidth, float scale)
    {
        var cursor = ImGui.GetCursorScreenPos();
        var protrude = 6f * scale;
        var pos = cursor + new Vector2(-protrude, -2f * scale);
        var bottom = ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y - ImGui.GetStyle().WindowPadding.Y;
        var height = MathF.Max(110f * scale, bottom - pos.Y);
        var size = new Vector2(contentWidth + protrude * 2f, height);

        drawList.PushClipRect(pos, pos + size, true);
        ProfileVisuals.DrawThemeOverlay(drawList, pos, size, profile.CloudThemeName, ResolvePalette(profile), scale);
        drawList.PopClipRect();
    }

    private void DrawOnlineFavoriteVenues(PrivateContact profile)
    {
        ffxivVenuesService.EnsureFreshAsync();

        var venues = (profile.CloudVenues ?? new List<PrivateVenueBookmark>())
            .Where(v => !string.IsNullOrWhiteSpace(v.Name))
            .ToList();
        if (venues.Count == 0)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TextColored(ResolvePalette(profile).Title, "Favorite Venues");
        foreach (var venue in venues)
            EnsureVenueBookmarkMetadata(venue);

        var imageSize = 62f * scale;
        var spacing = 10f * scale;
        var arrowWidth = 24f * scale;
        var available = ImGui.GetContentRegionAvail().X;
        var visible = Math.Max(1, (int)((available - arrowWidth * 2f - spacing * 2f) / (imageSize + spacing)));
        visible = Math.Min(visible, venues.Count);
        var stripWidth = visible * imageSize + Math.Max(0, visible - 1) * spacing;
        var totalWidth = arrowWidth * 2f + spacing * 2f + stripWidth;
        var origin = ImGui.GetCursorScreenPos();
        var startX = origin.X + MathF.Max(0f, (available - totalWidth) * 0.5f);
        var y = origin.Y;
        var drawList = ImGui.GetWindowDrawList();
        venueCarouselOffset = NormalizeCarouselOffset(venueCarouselOffset, venues.Count);

        ImGui.SetCursorScreenPos(new Vector2(startX, y + (imageSize - 18f * scale) * 0.5f));
        DrawCarouselArrow("online_venues_left", FontAwesomeIcon.ChevronLeft.ToIconString(), arrowWidth, () => venueCarouselOffset = NormalizeCarouselOffset(venueCarouselOffset - 1, venues.Count));

        var itemStartX = startX + arrowWidth + spacing;
        for (var i = 0; i < visible; i++)
        {
            var venue = venues[(venueCarouselOffset + i) % venues.Count];
            var pos = new Vector2(itemStartX + i * (imageSize + spacing), y);
            ImGui.SetCursorScreenPos(pos);
            var texture = ResolveVenueTexture(venue);
            drawList.AddRectFilled(pos, pos + new Vector2(imageSize), ImGui.GetColorU32(UiColors.WithAlpha(ResolvePalette(profile).PanelAlt, 0.66f)), 5f * scale);
            if (texture != null)
                drawList.AddImageRounded(texture.Handle, pos, pos + new Vector2(imageSize), Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), 5f * scale);
            else
                DrawTextWithShadow(drawList, pos + new Vector2(22f, 20f) * scale, ImGui.GetColorU32(UiColors.TextDim), "?", 18f * scale);
            drawList.AddRect(pos, pos + new Vector2(imageSize), ImGui.GetColorU32(UiColors.WithAlpha(ResolvePalette(profile).Accent, 0.42f)), 5f * scale, ImDrawFlags.None, 1f * scale);

            ImGui.InvisibleButton($"online_favorite_venue_{venue.Name}_{i}", new Vector2(imageSize));
            if (ImGui.IsItemHovered())
                DrawFavoriteVenueTooltip(venue);
            if (ImGui.BeginPopupContextItem($"online_favorite_venue_context_{i}"))
            {
                if (ImGui.MenuItem("Add venue to your list"))
                    AddVenueToLocalList(venue);
                ImGui.EndPopup();
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(itemStartX + stripWidth + spacing, y + (imageSize - 18f * scale) * 0.5f));
        DrawCarouselArrow("online_venues_right", FontAwesomeIcon.ChevronRight.ToIconString(), arrowWidth, () => venueCarouselOffset = NormalizeCarouselOffset(venueCarouselOffset + 1, venues.Count));

        ImGui.SetCursorScreenPos(new Vector2(origin.X, y + imageSize + 7f * scale));
        ImGui.Dummy(new Vector2(1f, 1f));
        ImGui.Spacing();
    }

    private void DrawCarouselArrow(string id, string icon, float width, Action action)
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
            var color = hovered ? config.AccentColor : UiColors.WithAlpha(config.AccentColor, 0.78f);
            drawList.AddText(UiBuilder.IconFont, fontSize, pos + (size - textSize) * 0.5f, ImGui.GetColorU32(color), icon);
        }
    }

    private void AddVenueToLocalList(PrivateVenueBookmark venue)
    {
        var existing = config.Venues.FirstOrDefault(v => string.Equals(v.Name, venue.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return;

        var contact = new PrivateContact
        {
            Name = venue.Name,
            DataCenter = venue.DataCenter,
            CurrentDataCenter = venue.DataCenter,
            World = venue.World,
            CurrentWorld = venue.World,
            LastKnownZone = venue.BuildAddress(),
            ResidentialDetails = BuildVenueResidentialDetails(venue),
            VenueColorHex = config.CloudProfileStatusColorHex,
            VenueTeleportCommand = venue.BuildTeleportCommand(),
            VenueDiscordUrl = venue.DiscordUrl,
            ProfileImagePath = venue.ImageLocalPath,
            CloudAvatarUrl = venue.ImageUrl,
            CloudManagedProfileImage = !string.IsNullOrWhiteSpace(venue.ImageLocalPath),
            Status = ContactStatus.Online,
            AddedAt = DateTimeOffset.UtcNow,
        };
        config.Venues.Add(contact);
        config.Save();
    }

    private static string BuildVenueResidentialDetails(PrivateVenueBookmark venue)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(venue.District)) parts.Add(venue.District);
        if (venue.Ward > 0) parts.Add($"w{venue.Ward}");
        if (venue.Plot > 0) parts.Add($"p{venue.Plot}");
        return string.Join(" - ", parts);
    }

    private void EnsureVenueBookmarkMetadata(PrivateVenueBookmark venue)
    {
        var catalog = ffxivVenuesService.FindBestMatch(venue.Name, venue.BuildAddress())
            ?? ffxivVenuesService.FindByAddress(venue.DataCenter, venue.World, venue.District, venue.Ward, venue.Plot)
            ?? ffxivVenuesService.FindByName(venue.Name);
        if (catalog == null)
            return;

        var changed = false;
        if (string.IsNullOrWhiteSpace(venue.Name) && !string.IsNullOrWhiteSpace(catalog.Name)) { venue.Name = catalog.Name; changed = true; }
        if (string.IsNullOrWhiteSpace(venue.DataCenter) && !string.IsNullOrWhiteSpace(catalog.DataCenter)) { venue.DataCenter = catalog.DataCenter; changed = true; }
        if (string.IsNullOrWhiteSpace(venue.World) && !string.IsNullOrWhiteSpace(catalog.World)) { venue.World = catalog.World; changed = true; }
        if (string.IsNullOrWhiteSpace(venue.District) && !string.IsNullOrWhiteSpace(catalog.District)) { venue.District = catalog.District; changed = true; }
        if (venue.Ward <= 0 && catalog.Ward > 0) { venue.Ward = catalog.Ward; changed = true; }
        if (venue.Plot <= 0 && catalog.Plot > 0) { venue.Plot = catalog.Plot; changed = true; }
        if (string.IsNullOrWhiteSpace(venue.Address) && !string.IsNullOrWhiteSpace(catalog.BuildFullLocation())) { venue.Address = catalog.BuildFullLocation(); changed = true; }
        if (string.IsNullOrWhiteSpace(venue.ImageUrl) && !string.IsNullOrWhiteSpace(catalog.ImageUrl)) { venue.ImageUrl = catalog.ImageUrl; changed = true; }
        if (string.IsNullOrWhiteSpace(venue.WebsiteUrl) && !string.IsNullOrWhiteSpace(catalog.WebsiteUrl)) { venue.WebsiteUrl = catalog.WebsiteUrl; changed = true; }
        if (string.IsNullOrWhiteSpace(venue.DiscordUrl) && !string.IsNullOrWhiteSpace(catalog.DiscordUrl)) { venue.DiscordUrl = catalog.DiscordUrl; changed = true; }
        if (string.IsNullOrWhiteSpace(venue.TeleportCommand) && !string.IsNullOrWhiteSpace(catalog.TeleportCommand)) { venue.TeleportCommand = catalog.TeleportCommand; changed = true; }
        if (string.IsNullOrWhiteSpace(venue.Source)) { venue.Source = "FFXIVVenues"; changed = true; }

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
        var location = venue.BuildAddress();
        return string.IsNullOrWhiteSpace(location) ? "Unknown location" : location;
    }

    private Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? ResolveVenueTexture(PrivateVenueBookmark venue)
    {
        EnsureVenueBookmarkMetadata(venue);

        if (!string.IsNullOrWhiteSpace(venue.ImageLocalPath))
        {
            var texture = profileImages.GetTexture(venue.ImageLocalPath);
            if (texture != null)
                return texture;

            venue.ImageLocalPath = string.Empty;
            config.Save();
        }

        if (!string.IsNullOrWhiteSpace(venue.ImageUrl))
        {
            var cacheKey = BuildVenueImageCacheKey(venue);
            if (!pendingVenueImageDownloads.Add(cacheKey))
                return null;

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

        return null;
    }

    private static string BuildVenueImageCacheKey(PrivateVenueBookmark venue)
        => string.IsNullOrWhiteSpace(venue.Name) ? venue.ImageUrl : venue.Name + "_" + venue.World + "_" + venue.Ward + "_" + venue.Plot;


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

    private static Vector4 GetStatusColor(ContactStatus status)
        => status switch
        {
            _ => ProfileStatusVisuals.GetColor(status),
        };

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

    private void DrawSection(string title, string text, Vector4? textColor = null)
    {
        var palette = ResolvePalette();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(palette.Accent, ProfileVisuals.GetSectionIcon(contact?.CloudThemeName, title));
        }
        ImGui.SameLine();
        ImGui.TextColored(palette.Title, title);
        if (textColor.HasValue)
            ImGui.TextColored(textColor.Value, text);
        else
            ImGui.TextColored(palette.Body, text);
        ImGui.Spacing();
    }

    private ProfileVisuals.ThemePalette ResolvePalette(PrivateContact? profile = null)
    {
        var selected = profile ?? contact;
        return ProfileVisuals.ResolvePalette(selected?.CloudThemeName ?? "Default", UiColors.HexToRgba(NormalizeHex(selected?.CloudThemeColorHex, "#2BE5B5")), config.WindowBackgroundColor);
    }

    private static void DrawThemeOverlay(ImDrawListPtr drawList, Vector2 pos, Vector2 size, string themeName, ProfileVisuals.ThemePalette palette, float scale)
    {
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(UiColors.WithAlpha(palette.Panel, 0.26f)), 0f);
        var key = (themeName ?? string.Empty).Trim();
        if (string.Equals(key, "Minimal", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < 4; i++)
            {
                var y = pos.Y + (i + 1) * size.Y / 5f;
                drawList.AddLine(new Vector2(pos.X, y), new Vector2(pos.X + size.X, y), ImGui.GetColorU32(UiColors.WithAlpha(palette.Accent, 0.035f)), 1f * scale);
            }
            return;
        }

        if (string.Equals(key, "Glass", StringComparison.OrdinalIgnoreCase))
        {
            drawList.AddRectFilledMultiColor(pos, pos + size, ImGui.GetColorU32(UiColors.WithAlpha(Vector4.One, 0.075f)), ImGui.GetColorU32(UiColors.WithAlpha(Vector4.One, 0.025f)), ImGui.GetColorU32(Vector4.Zero), ImGui.GetColorU32(Vector4.Zero));
            DrawDotOverlay(drawList, pos, size, UiColors.WithAlpha(palette.Accent, 0.045f), scale);
            return;
        }

        if (string.Equals(key, "Pastel", StringComparison.OrdinalIgnoreCase))
        {
            drawList.AddCircleFilled(pos + new Vector2(size.X * 0.78f, size.Y * 0.16f), 58f * scale, ImGui.GetColorU32(palette.Glow), 64);
            drawList.AddCircleFilled(pos + new Vector2(size.X * 0.23f, size.Y * 0.72f), 38f * scale, ImGui.GetColorU32(UiColors.WithAlpha(palette.Accent, 0.10f)), 48);
            return;
        }

        if (string.Equals(key, "Royal", StringComparison.OrdinalIgnoreCase))
        {
            drawList.AddCircleFilled(pos + new Vector2(size.X * 0.82f, size.Y * 0.20f), 62f * scale, ImGui.GetColorU32(palette.Glow), 64);
            DrawDotOverlay(drawList, pos, size, UiColors.WithAlpha(palette.Accent, 0.05f), scale);
            return;
        }

        drawList.AddCircleFilled(pos + new Vector2(size.X * 0.84f, size.Y * 0.18f), 52f * scale, ImGui.GetColorU32(palette.Glow), 58);
        DrawDotOverlay(drawList, pos, size, UiColors.WithAlpha(palette.Accent, 0.065f), scale);
    }

    private Vector4 ResolveStatusMessageColor(PrivateContact profile)
        => UiColors.HexToRgba(NormalizeHex(profile.CloudStatusColorHex, "#2BE5B5"));

    private static string NormalizeHex(string? value, string fallback)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.StartsWith('#'))
            text = text[1..];

        if (text.Length != 6 || text.Any(ch => !Uri.IsHexDigit(ch)))
            return fallback;

        return "#" + text.ToUpperInvariant();
    }

    private Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? ResolveBannerTexture(PrivateContact profile)
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
        var cacheKey = BuildBannerCacheKey(profile.Id + "_banner", bannerUrl);
        if (profileImages.TryGetCurrentRemoteVenueImagePath(bannerUrl, cacheKey, bannerUrl, out var cachedPath))
        {
            profile.CloudBannerLocalPath = cachedPath;
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

    private Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? GetCloudProfileTexture(PrivateContact profile)
    {
        if (!profile.CloudManagedProfileImage)
            return null;

        return profileImages.GetTexture(profile.ProfileImagePath);
    }

    private void TryQueueCloudAvatarDownload(PrivateContact profile)
    {
        if (avatarDownloadInProgress || string.IsNullOrWhiteSpace(profile.CloudAvatarUrl))
            return;

        var contactId = profile.Id;
        var avatarUrl = profile.CloudAvatarUrl.Trim();
        var avatarVersion = BuildAvatarCacheVersion(profile);

        if (profile.CloudManagedProfileImage &&
            !string.IsNullOrWhiteSpace(profile.ProfileImagePath) &&
            profileImages.IsRemoteImageCurrent(contactId, avatarUrl, avatarVersion))
            return;

        if (string.Equals(activeAvatarContactId, contactId, StringComparison.Ordinal) &&
            string.Equals(activeAvatarUrl, avatarUrl, StringComparison.Ordinal) &&
            string.Equals(activeAvatarVersion, avatarVersion, StringComparison.Ordinal))
            return;

        activeAvatarContactId = contactId;
        activeAvatarUrl = avatarUrl;
        activeAvatarVersion = avatarVersion;
        avatarDownloadInProgress = true;

        Task.Run(async () =>
        {
            try
            {
                var storedPath = await profileImages.DownloadRemoteImageAsync(avatarUrl, contactId, CancellationToken.None, avatarVersion).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(storedPath))
                    return;

                profile.ProfileImagePath = storedPath;
                profile.CloudManagedProfileImage = true;
                config.Save();
            }
            finally
            {
                avatarDownloadInProgress = false;
            }
        });
    }

    private static string BuildAvatarCacheVersion(PrivateContact profile)
    {
        if (profile.CloudLastSyncedAt != DateTimeOffset.MinValue)
            return profile.CloudLastSyncedAt.ToUnixTimeMilliseconds().ToString();

        return profile.CloudAvatarUrl;
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
