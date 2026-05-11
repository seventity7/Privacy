using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Privacy.Models;
using Privacy.Services;
using Privacy.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
    private readonly FileDialogManager fileDialog = new();

    private string profileDisplayName = string.Empty;
    private string profileBio = string.Empty;
    private string profileStatusMessage = string.Empty;
    private string localAvatarPath = string.Empty;
    private string statusMessage = string.Empty;
    private string venueNameBuffer = string.Empty;
    private string selectedDataCenter = "Dynamis";
    private string selectedWorld = "Golem";
    private string selectedDistrict = "Goblet";
    private int selectedWard = 1;
    private int selectedPlot = 1;
    private bool fieldsInitialized;
    private bool busy;
    private int pushedColorCount;
    private int pushedStyleVarCount;

    public MyProfileWindow(Configuration config, PrivacyCloudService cloudService, ProfileImageCache profileImages)
        : base("My Profile###PrivacyMyProfile")
    {
        this.config = config;
        this.cloudService = cloudService;
        this.profileImages = profileImages;
        SizeCondition = ImGuiCond.Always;

        WindowBuilder.For(this)
            .AllowPinning(true)
            .AllowClickthrough(false)
            .SetSizeConstraints(new Vector2(430f, 420f), new Vector2(640f, 840f))
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
        Size = cloudService.IsLoggedIn ? new Vector2(505f, 735f) : new Vector2(430f, 120f);
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
            DrawProfileFields();
            DrawAvatarPicker();

            if (ImGui.Button("Save Profile", new Vector2(135f, 0f) * ImGuiHelpers.GlobalScale))
            {
                var displayName = profileDisplayName;
                var bio = profileBio;
                var profileStatus = profileStatusMessage;
                var avatarPath = localAvatarPath;
                RunCloudAction(() => SaveProfileAsync(displayName, bio, profileStatus, avatarPath));
            }

            ImGui.SameLine();
            if (ImGui.Button("Reset fields", new Vector2(110f, 0f) * ImGuiHelpers.GlobalScale))
            {
                fieldsInitialized = false;
                EnsureProfileFieldsInitialized();
            }
        }

        if (busy)
            ImGui.TextDisabled("Working...");

        if (!string.IsNullOrWhiteSpace(statusMessage))
            ImGui.TextWrapped(statusMessage);

        ImGui.Separator();
        ImGui.TextColored(config.AccentColor, "Profile preview:");
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
        var top = new Vector4(
            MathF.Min(1f, config.WindowBackgroundColor.X + 0.028f),
            MathF.Min(1f, config.WindowBackgroundColor.Y + 0.028f),
            MathF.Min(1f, config.WindowBackgroundColor.Z + 0.028f),
            MathF.Min(1f, MathF.Max(0.38f, config.WindowBackgroundColor.W)));
        var bottom = new Vector4(config.WindowBackgroundColor.X, config.WindowBackgroundColor.Y, config.WindowBackgroundColor.Z, 0f);

        drawList.AddRectFilledMultiColor(
            pos,
            pos + new Vector2(width, headerHeight),
            ImGui.GetColorU32(top),
            ImGui.GetColorU32(top),
            ImGui.GetColorU32(bottom),
            ImGui.GetColorU32(bottom));

        DrawTextWithShadow(drawList, pos + new Vector2(20f, 13f) * scale, ImGui.GetColorU32(config.AccentColor), "My Profile", 20f * scale);
        ImGui.Dummy(new Vector2(width, headerHeight));
    }

    private void DrawProfileFields()
    {
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##my_profile_display", "Display name", ref profileDisplayName, 48);

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##my_profile_status", "Status message", ref profileStatusMessage, 60);

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextMultiline("##my_profile_bio", ref profileBio, 120, new Vector2(-1f, 74f * ImGuiHelpers.GlobalScale));
    }

    private void DrawAvatarPicker()
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TextColored(config.AccentColor, "Profile picture");
        var size = new Vector2(68f, 68f) * scale;
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

        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(config.AccentColor.X, config.AccentColor.Y, config.AccentColor.Z, 0.45f)), 5f * scale, ImDrawFlags.None, 1.2f * scale);
        ImGui.InvisibleButton("my-profile-avatar-picker", size);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Click to choose a profile picture. Max 512x512 and 2 MB.");

        if (ImGui.IsItemClicked() && cloudService.IsLoggedIn && !busy)
            OpenAvatarPicker();

        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextWrapped("This picture is uploaded to your Privacy profile and shown to other users.");
        if (!string.IsNullOrWhiteSpace(config.CloudProfileAvatarUrl))
            ImGui.TextDisabled("Cloud avatar is configured.");
        ImGui.EndGroup();
    }

    private void DrawProfilePreview()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var rowMin = ImGui.GetCursorScreenPos();
        var rowSize = new Vector2(ImGui.GetContentRegionAvail().X, 86f * scale);
        var rowMax = rowMin + rowSize;
        drawList.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(new Vector4(0.055f, 0.095f, 0.085f, 0.58f)), 4f * scale);
        drawList.AddRect(rowMin, rowMax, ImGui.GetColorU32(new Vector4(config.AccentColor.X, config.AccentColor.Y, config.AccentColor.Z, 0.22f)), 4f * scale);

        var avatarSize = new Vector2(48f, 48f) * scale;
        var avatarMin = rowMin + new Vector2(10f, 11f) * scale;
        var avatarMax = avatarMin + avatarSize;
        var texture = profileImages.GetTexture(localAvatarPath);
        drawList.AddRectFilled(avatarMin, avatarMax, ImGui.GetColorU32(new Vector4(0.06f, 0.09f, 0.08f, 0.92f)), 4f * scale);
        if (texture != null)
            drawList.AddImageRounded(texture.Handle, avatarMin, avatarMax, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), 4f * scale);
        else
            drawList.AddText(avatarMin + new Vector2(15f, 15f) * scale, ImGui.GetColorU32(UiColors.TextDim), "?");

        var textPos = rowMin + new Vector2(68f, 10f) * scale;
        DrawTextWithShadow(drawList, textPos, ImGui.GetColorU32(UiColors.Text), string.IsNullOrWhiteSpace(profileDisplayName) ? config.CloudLinkedCharacterName : profileDisplayName, 17f * scale);
        DrawTextWithShadow(drawList, textPos + new Vector2(0f, 23f * scale), ImGui.GetColorU32(UiColors.TextDim), string.IsNullOrWhiteSpace(profileStatusMessage) ? "No status message set." : profileStatusMessage, 13f * scale);
        DrawTextWithShadow(drawList, textPos + new Vector2(0f, 43f * scale), ImGui.GetColorU32(new Vector4(0.68f, 0.72f, 0.70f, 1f)), string.IsNullOrWhiteSpace(profileBio) ? "No bio set." : profileBio, 12f * scale);

        ImGui.Dummy(rowSize);
    }

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

    private async Task<string> SaveProfileAsync(string displayName, string bio, string profileStatus, string avatarPath)
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

        var result = await cloudService.SaveCloudProfileAsync(displayName, bio, profileStatus, avatarUrl).ConfigureAwait(false);
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
        localAvatarPath = config.CloudProfileAvatarLocalPath;
        fieldsInitialized = true;
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
