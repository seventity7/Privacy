using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Privacy.Models;
using Privacy.Services;
using System;
using System.Numerics;

namespace Privacy.UI;

internal static class ProfileStatusVisuals
{
    public static uint GetIconId(ContactStatus status)
        => status switch
        {
            ContactStatus.Busy => 61509,
            ContactStatus.Afk => 61511,
            ContactStatus.Content => 61506,
            ContactStatus.Streaming => 61546,
            ContactStatus.RolePlaying => 61545,
            ContactStatus.Online => 61505,
            ContactStatus.Offline => 61504,
            _ => 61505,
        };

    public static string GetDisplayName(ContactStatus status)
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

    public static Vector4 GetColor(ContactStatus status)
        => status switch
        {
            ContactStatus.Busy => UiColors.Busy,
            ContactStatus.Afk => new Vector4(1.00f, 0.34f, 0.34f, 1f),
            ContactStatus.Content => new Vector4(0.18f, 0.88f, 1.00f, 1f),
            ContactStatus.Streaming => new Vector4(0.78f, 0.56f, 1.00f, 1f),
            ContactStatus.RolePlaying => new Vector4(1.00f, 0.55f, 0.86f, 1f),
            ContactStatus.Online => UiColors.Online,
            _ => UiColors.Offline,
        };

    public static bool TryDrawGameIcon(ImDrawListPtr drawList, GameIconCache? gameIcons, ContactStatus status, Vector2 center, float size, float scale)
    {
        if (gameIcons == null)
            return false;

        var icon = gameIcons.GetIcon(GetIconId(status));
        if (icon == null)
            return false;

        var min = center - new Vector2(size * 0.5f);
        var max = center + new Vector2(size * 0.5f);
        var tint = status == ContactStatus.Online ? GetColor(status) : Vector4.One;
        drawList.AddImage(icon.Handle, min, max, Vector2.Zero, Vector2.One, ImGui.GetColorU32(tint));
        if (status == ContactStatus.Online)
            drawList.AddCircleFilled(center, size * 0.42f, ImGui.GetColorU32(new Vector4(GetColor(status).X, GetColor(status).Y, GetColor(status).Z, 0.26f)), 24);
        return true;
    }
}
