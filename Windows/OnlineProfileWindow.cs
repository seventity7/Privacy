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
    private readonly FfxivVenuesService ffxivVenuesService;
    private PrivateContact? contact;
    private int venueCarouselOffset;
    private readonly HashSet<string> pendingVenueImageDownloads = new(StringComparer.OrdinalIgnoreCase);
    private string activeAvatarContactId = string.Empty;
    private string activeAvatarUrl = string.Empty;
    private string activeAvatarVersion = string.Empty;
    private bool avatarDownloadInProgress;
    private int pushedColorCount;
    private int pushedStyleVarCount;

    public OnlineProfileWindow(Configuration config, ProfileImageCache profileImages, FfxivVenuesService ffxivVenuesService)
        : base("Online Profile###PrivacyOnlineProfile")
    {
        this.config = config;
        this.profileImages = profileImages;
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
        PushColor(ImGuiCol.Border, Vector4.Zero);
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

        DrawLowerContentDecor(drawList, contentWidth, scale);
        ImGui.Spacing();
        DrawOnlineFavoriteVenues(profile);
        DrawSection("Status", string.IsNullOrWhiteSpace(profile.CloudStatusMessage) ? "No status message set." : profile.CloudStatusMessage, ResolveStatusMessageColor(profile));
        DrawSection("Bio", string.IsNullOrWhiteSpace(profile.CloudBio) ? "No bio set." : profile.CloudBio);

        ImGui.TextColored(config.AccentColor, "Location");
        ImGui.TextDisabled(string.IsNullOrWhiteSpace(profile.DisplayLocation) ? "Unknown location." : FormatProfileLocation(profile.DisplayLocation));

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
        var protrude = 6f * scale;
        var pos = cursor + new Vector2(-protrude, 0f);
        var size = new Vector2(contentWidth + protrude * 2f, 126f * scale);
        var rounding = 7f * scale;

        var borderPad = 1f * scale;
        var barFill = UiColors.WithAlpha(config.WindowBackgroundColor, 0.24f);
        var barOverlay = UiColors.WithAlpha(config.BottomBarBackgroundColor, 0.18f);
        drawList.AddRectFilled(pos - new Vector2(borderPad, borderPad), pos + size + new Vector2(borderPad, borderPad), ImGui.GetColorU32(barFill), rounding + borderPad);
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(barOverlay), rounding);
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(UiColors.WithAlpha(config.AccentColor, 0.42f)), rounding, ImDrawFlags.None, 1f * scale);
        DrawDotOverlay(drawList, pos, size, UiColors.WithAlpha(config.AccentColor, 0.08f), scale);

        var avatarSize = new Vector2(88f, 88f) * scale;
        var avatarMin = pos + new Vector2(14f, 19f) * scale;
        var avatarMax = avatarMin + avatarSize;
        var texture = GetCloudProfileTexture(profile);

        drawList.AddRectFilled(avatarMin, avatarMax, ImGui.GetColorU32(UiColors.WithAlpha(config.WindowBackgroundColor, 0.34f)), 7f * scale);
        if (texture != null)
            drawList.AddImageRounded(texture.Handle, avatarMin, avatarMax, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), 7f * scale);
        else
            DrawTextWithShadow(drawList, avatarMin + new Vector2(35f, 31f) * scale, ImGui.GetColorU32(UiColors.TextDim), "?", 24f * scale);
        drawList.AddRect(avatarMin, avatarMax, ImGui.GetColorU32(UiColors.WithAlpha(config.AccentColor, 0.58f)), 7f * scale, ImDrawFlags.None, 1f * scale);

        var textMin = new Vector2(avatarMax.X + 16f * scale, avatarMin.Y + 4f * scale);
        var textMax = pos + size - new Vector2(14f, 12f) * scale;
        var textWidth = MathF.Max(40f * scale, textMax.X - textMin.X);
        var displayName = profile.CloudDisplayName.Length > 0 ? profile.CloudDisplayName : profile.DisplayName;
        var identity = string.IsNullOrWhiteSpace(profile.World) ? profile.Name : $"{profile.Name}@{profile.World}";
        var statusText = GetStatusDisplayName(profile.Status);
        var statusColor = GetStatusColor(profile.Status);

        drawList.PushClipRect(textMin - new Vector2(1f, 1f) * scale, textMax, true);
        DrawTextWithShadow(drawList, textMin, ImGui.GetColorU32(UiColors.Text), TrimToWidth(displayName, textWidth, 15.2f * scale), 15.2f * scale);
        DrawTextWithShadow(drawList, textMin + new Vector2(0f, 24f) * scale, ImGui.GetColorU32(UiColors.TextDim), TrimToWidth(identity, textWidth, 13.3f * scale), 13.3f * scale);
        DrawTextWithShadow(drawList, textMin + new Vector2(0f, 48f) * scale, ImGui.GetColorU32(statusColor), statusText, 13.3f * scale);
        if (avatarDownloadInProgress && !string.IsNullOrWhiteSpace(profile.CloudAvatarUrl))
            DrawTextWithShadow(drawList, textMin + new Vector2(0f, 72f) * scale, ImGui.GetColorU32(UiColors.TextDim), "Loading profile picture...", 12.6f * scale);
        drawList.PopClipRect();

        ImGui.Dummy(new Vector2(contentWidth, size.Y + 8f * scale));
    }

    private void DrawLowerContentDecor(ImDrawListPtr drawList, float contentWidth, float scale)
    {
        var cursor = ImGui.GetCursorScreenPos();
        var protrude = 6f * scale;
        var pos = cursor + new Vector2(-protrude, -2f * scale);
        var bottom = ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y - ImGui.GetStyle().WindowPadding.Y;
        var height = MathF.Max(110f * scale, bottom - pos.Y);
        var size = new Vector2(contentWidth + protrude * 2f, height);

        drawList.PushClipRect(pos, pos + size, true);
        HeaderDecor.Draw(drawList, pos, size.X, size.Y, config.WindowBackgroundColor, config.AccentColor);
        drawList.PopClipRect();
    }

    private void DrawOnlineFavoriteVenues(PrivateContact profile)
    {
        var venues = (profile.CloudVenues ?? new List<PrivateVenueBookmark>())
            .Where(v => v.Favorite && !string.IsNullOrWhiteSpace(v.Name))
            .ToList();
        if (venues.Count == 0)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TextColored(config.AccentColor, "Favorite Venues");
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
        var startX = ImGui.GetCursorScreenPos().X + MathF.Max(0f, (available - totalWidth) * 0.5f);
        var y = ImGui.GetCursorScreenPos().Y;
        var drawList = ImGui.GetWindowDrawList();
        venueCarouselOffset = NormalizeCarouselOffset(venueCarouselOffset, venues.Count);

        ImGui.SetCursorScreenPos(new Vector2(startX, y + (imageSize - 18f * scale) * 0.5f));
        DrawCarouselArrow("online_venues_left", FontAwesomeIcon.ChevronLeft.ToIconString(), arrowWidth, () => venueCarouselOffset = NormalizeCarouselOffset(venueCarouselOffset - 1, venues.Count));

        ImGui.SameLine(0f, spacing);
        ImGui.SetCursorScreenPos(new Vector2(startX + arrowWidth + spacing, y));
        for (var i = 0; i < visible; i++)
        {
            var venue = venues[(venueCarouselOffset + i) % venues.Count];
            var pos = ImGui.GetCursorScreenPos();
            var texture = ResolveVenueTexture(venue);
            drawList.AddRectFilled(pos, pos + new Vector2(imageSize), ImGui.GetColorU32(UiColors.WithAlpha(config.WindowBackgroundColor, 0.48f)), 5f * scale);
            if (texture != null)
                drawList.AddImageRounded(texture.Handle, pos, pos + new Vector2(imageSize), Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), 5f * scale);
            else
                DrawTextWithShadow(drawList, pos + new Vector2(22f, 20f) * scale, ImGui.GetColorU32(UiColors.TextDim), "?", 18f * scale);
            drawList.AddRect(pos, pos + new Vector2(imageSize), ImGui.GetColorU32(UiColors.WithAlpha(config.AccentColor, 0.36f)), 5f * scale, ImDrawFlags.None, 1f * scale);

            ImGui.InvisibleButton($"online_favorite_venue_{venue.Name}_{i}", new Vector2(imageSize));
            if (ImGui.IsItemHovered())
                DrawFavoriteVenueTooltip(venue);
            if (ImGui.BeginPopupContextItem($"online_favorite_venue_context_{i}"))
            {
                if (ImGui.MenuItem("Add venue to your list"))
                    AddVenueToLocalList(venue);
                ImGui.EndPopup();
            }

            if (i < visible - 1)
                ImGui.SameLine(0f, spacing);
        }

        ImGui.SameLine(0f, spacing);
        ImGui.SetCursorScreenPos(new Vector2(startX + arrowWidth + spacing + stripWidth + spacing, y + (imageSize - 18f * scale) * 0.5f));
        DrawCarouselArrow("online_venues_right", FontAwesomeIcon.ChevronRight.ToIconString(), arrowWidth, () => venueCarouselOffset = NormalizeCarouselOffset(venueCarouselOffset + 1, venues.Count));

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, y + imageSize + 7f * scale));
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
        var catalog = ffxivVenuesService.FindByName(venue.Name);
        if (catalog == null)
            return;

        var changed = false;
        if (string.IsNullOrWhiteSpace(venue.ImageUrl) && !string.IsNullOrWhiteSpace(catalog.ImageUrl)) { venue.ImageUrl = catalog.ImageUrl; changed = true; }
        if (string.IsNullOrWhiteSpace(venue.TeleportCommand) && !string.IsNullOrWhiteSpace(catalog.TeleportCommand)) { venue.TeleportCommand = catalog.TeleportCommand; changed = true; }
        if (string.IsNullOrWhiteSpace(venue.Address)) { venue.Address = catalog.BuildFullLocation(); changed = true; }
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
            ContactStatus.Busy => UiColors.Busy,
            ContactStatus.Idle => new Vector4(1.00f, 0.80f, 0.23f, 1f),
            ContactStatus.Afk => new Vector4(1.00f, 0.34f, 0.34f, 1f),
            ContactStatus.Content => new Vector4(0.18f, 0.88f, 1.00f, 1f),
            ContactStatus.Streaming => new Vector4(0.78f, 0.56f, 1.00f, 1f),
            ContactStatus.Online => UiColors.Online,
            _ => UiColors.Offline,
        };

    private static string GetStatusDisplayName(ContactStatus status)
        => status switch
        {
            ContactStatus.Afk => "AFK",
            ContactStatus.Idle => "Idle",
            ContactStatus.Busy => "Busy",
            ContactStatus.Content => "Content",
            ContactStatus.Streaming => "Streaming",
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
        ImGui.TextColored(config.AccentColor, title);
        if (textColor.HasValue)
            ImGui.TextColored(textColor.Value, text);
        else
            ImGui.TextWrapped(text);
        ImGui.Spacing();
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
