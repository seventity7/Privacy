using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace Privacy.UI;

internal static class UiColors
{
    private static readonly Dictionary<string, string> DefaultHexColors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "LightlessPurple", "#2BE5B5" },
        { "LightlessPurpleActive", "#43F8CC" },
        { "LightlessPurpleDefault", "#18A986" },
        { "ButtonDefault", "#303331" },
        { "FullBlack", "#000000" },
        { "LightlessBlue", "#88FFF8" },
        { "LightlessYellow", "#FFD56A" },
        { "LightlessYellow2", "#CFBD63" },
        { "LightlessGreen", "#95DD39" },
        { "LightlessGreenDefault", "#468A50" },
        { "LightlessOrange", "#FFB366" },
        { "LightlessGrey", "#9BA2A3" },
        { "PairBlue", "#A6C2FF" },
        { "DimRed", "#FF431D" },
        { "PrivateWindowBg", "#171717F8" },
        { "PrivateChildBg", "#17171742" },
        { "PrivatePopupBg", "#171717F8" },
        { "PrivateBorder", "#414141FF" },
        { "PrivateFrameBg", "#282828FF" },
        { "PrivateFrameBgHovered", "#32323264" },
        { "PrivateFrameBgActive", "#1E1E1EFF" },
        { "PrivateTitleBg", "#181818E8" },
        { "PrivateTitleBgActive", "#1E1E1EFF" },
        { "PrivateTitleBgCollapsed", "#1B1B1BFF" },
        { "HeaderGradientTop", "#082018FF" },
        { "HeaderGradientBottom", "#0C211B00" },
        { "HeaderStaticStar", "#FFFFFFFF" },
        { "HeaderShootingStar", "#2BE5B5FF" },
    };

    private static readonly Dictionary<string, Vector4> ParsedColors = new(StringComparer.OrdinalIgnoreCase);

    public static readonly Vector4 Accent = HexToRgba("#2BE5B5");
    public static readonly Vector4 AccentDim = HexToRgba("#0D7B63");
    public static readonly Vector4 WindowBg = HexToRgba("#171717F8");
    public static readonly Vector4 PanelBg = HexToRgba("#171717A8");
    public static readonly Vector4 RowBg = HexToRgba("#111C19A8");
    public static readonly Vector4 RowBorder = HexToRgba("#2BE5B540");
    public static readonly Vector4 Text = HexToRgba("#E9F5F0");
    public static readonly Vector4 TextDim = HexToRgba("#AAB8B3");
    public static readonly Vector4 Online = HexToRgba("#95DD39");
    public static readonly Vector4 Offline = HexToRgba("#9BA2A3");
    public static readonly Vector4 Busy = HexToRgba("#FF431D");
    public static readonly Vector4 Favorite = HexToRgba("#F8EC33");
    public static readonly Vector4 PurpleHover = HexToRgba("#C9A9FFFF");
    public static readonly Vector4 CyanHover = HexToRgba("#88FFF8FF");
    public static readonly Vector4 GoldHover = HexToRgba("#FFD56AFF");
    public static readonly Vector4 BrownHover = HexToRgba("#C89D77FF");

    public static Vector4 Get(string name)
    {
        if (ParsedColors.TryGetValue(name, out var parsed))
            return parsed;

        if (!DefaultHexColors.TryGetValue(name, out var hex))
            throw new ArgumentException($"Color '{name}' not found in UIColors.", nameof(name));

        parsed = HexToRgba(hex);
        ParsedColors[name] = parsed;
        return parsed;
    }

    public static Vector4 HexToRgba(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6)
            hex += "FF";
        if (hex.Length != 8)
            return Vector4.One;

        var r = int.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
        var g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
        var b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
        var a = int.Parse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
        return new Vector4(r, g, b, a);
    }

    public static Vector4 WithAlpha(Vector4 color, float alpha)
        => new(color.X, color.Y, color.Z, alpha);
}
