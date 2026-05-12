using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;

namespace Privacy.UI;

internal static class ProfileVisuals
{
    public readonly struct ThemePalette
    {
        public ThemePalette(Vector4 windowBackground, Vector4 panel, Vector4 panelAlt, Vector4 accent, Vector4 title, Vector4 subtitle, Vector4 body, Vector4 overlay, Vector4 glow, Vector4 button)
        {
            WindowBackground = windowBackground;
            Panel = panel;
            PanelAlt = panelAlt;
            Accent = accent;
            Title = title;
            Subtitle = subtitle;
            Body = body;
            Overlay = overlay;
            Glow = glow;
            Button = button;
        }

        public Vector4 WindowBackground { get; }
        public Vector4 Panel { get; }
        public Vector4 PanelAlt { get; }
        public Vector4 Accent { get; }
        public Vector4 Title { get; }
        public Vector4 Subtitle { get; }
        public Vector4 Body { get; }
        public Vector4 Overlay { get; }
        public Vector4 Glow { get; }
        public Vector4 Button { get; }
    }


    private static Vector4 Blend(Vector4 a, Vector4 b, float t, float alpha)
        => new(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            alpha);

    private static Vector4 Dark(Vector4 c, float t, float alpha)
        => new(c.X * t, c.Y * t, c.Z * t, alpha);

    private static Vector4 Light(Vector4 c, float t, float alpha)
        => new(MathF.Min(1f, c.X + (1f - c.X) * t), MathF.Min(1f, c.Y + (1f - c.Y) * t), MathF.Min(1f, c.Z + (1f - c.Z) * t), alpha);


    private static ThemePalette ApplyThemeColor(ThemePalette palette, Vector4 themeColor, string themeName)
    {
        if (themeColor.W <= 0f)
            return palette;

        var strong = themeName is "Minimal Ink" ? 0.34f : 0.58f;
        var medium = themeName is "Minimal Ink" ? 0.20f : 0.42f;
        var soft = themeName is "Minimal Ink" ? 0.10f : 0.24f;
        var accent = new Vector4(themeColor.X, themeColor.Y, themeColor.Z, 1f);
        return new ThemePalette(
            Blend(palette.WindowBackground, Dark(accent, 0.22f, palette.WindowBackground.W), soft, palette.WindowBackground.W),
            Blend(palette.Panel, Dark(accent, 0.34f, palette.Panel.W), medium, palette.Panel.W),
            Blend(palette.PanelAlt, Dark(accent, 0.46f, palette.PanelAlt.W), medium, palette.PanelAlt.W),
            accent,
            Light(Blend(palette.Title, accent, strong, 1f), 0.12f, 1f),
            Light(Blend(palette.Subtitle, accent, medium, 1f), 0.08f, 1f),
            Light(Blend(palette.Body, accent, soft, 1f), 0.05f, 1f),
            new Vector4(accent.X, accent.Y, accent.Z, MathF.Max(palette.Overlay.W, 0.13f)),
            new Vector4(accent.X, accent.Y, accent.Z, MathF.Max(palette.Glow.W, 0.24f)),
            Blend(palette.Button, Dark(accent, 0.50f, palette.Button.W), medium, palette.Button.W));
    }

    public static ThemePalette ResolvePalette(string themeName, Vector4 fallbackAccent, Vector4 fallbackBackground)
    {
        var key = NormalizeThemeName(themeName);
        var customAccent = fallbackAccent;
        var palette = key switch
        {
            "Neon Grid" => new ThemePalette(
                new Vector4(0.006f, 0.020f, 0.025f, MathF.Max(fallbackBackground.W, 0.92f)),
                new Vector4(0.012f, 0.070f, 0.065f, 0.76f),
                new Vector4(0.020f, 0.120f, 0.110f, 0.56f),
                new Vector4(0.30f, 1.00f, 0.78f, 1f),
                new Vector4(0.70f, 1.00f, 0.90f, 1f),
                new Vector4(0.48f, 0.90f, 0.82f, 1f),
                new Vector4(0.88f, 1.00f, 0.97f, 1f),
                new Vector4(0.25f, 1.00f, 0.80f, 0.14f),
                new Vector4(0.30f, 1.00f, 0.78f, 0.30f),
                new Vector4(0.10f, 0.45f, 0.38f, 0.68f)),
            "Pastel Bloom" => new ThemePalette(
                new Vector4(0.095f, 0.060f, 0.090f, MathF.Max(fallbackBackground.W, 0.90f)),
                new Vector4(0.180f, 0.105f, 0.155f, 0.74f),
                new Vector4(0.235f, 0.145f, 0.200f, 0.54f),
                new Vector4(1.00f, 0.65f, 0.86f, 1f),
                new Vector4(1.00f, 0.84f, 0.94f, 1f),
                new Vector4(0.96f, 0.70f, 0.86f, 1f),
                new Vector4(1.00f, 0.93f, 0.98f, 1f),
                new Vector4(1.00f, 0.60f, 0.86f, 0.12f),
                new Vector4(1.00f, 0.65f, 0.86f, 0.24f),
                new Vector4(0.45f, 0.22f, 0.36f, 0.65f)),
            "Glass Rain" => new ThemePalette(
                new Vector4(0.030f, 0.045f, 0.060f, MathF.Max(fallbackBackground.W, 0.74f)),
                new Vector4(0.090f, 0.130f, 0.160f, 0.48f),
                new Vector4(0.120f, 0.170f, 0.205f, 0.34f),
                new Vector4(0.70f, 0.90f, 1.00f, 1f),
                new Vector4(0.88f, 0.97f, 1.00f, 1f),
                new Vector4(0.64f, 0.82f, 0.92f, 1f),
                new Vector4(0.92f, 0.97f, 1.00f, 1f),
                new Vector4(0.70f, 0.90f, 1.00f, 0.10f),
                new Vector4(0.70f, 0.90f, 1.00f, 0.22f),
                new Vector4(0.17f, 0.28f, 0.34f, 0.56f)),
            "Minimal Ink" => new ThemePalette(
                new Vector4(0.035f, 0.035f, 0.035f, MathF.Max(fallbackBackground.W, 0.92f)),
                new Vector4(0.080f, 0.080f, 0.080f, 0.70f),
                new Vector4(0.120f, 0.120f, 0.120f, 0.46f),
                new Vector4(0.86f, 0.86f, 0.86f, 1f),
                new Vector4(0.96f, 0.96f, 0.96f, 1f),
                new Vector4(0.70f, 0.70f, 0.70f, 1f),
                new Vector4(0.90f, 0.90f, 0.90f, 1f),
                new Vector4(1f, 1f, 1f, 0.045f),
                new Vector4(1f, 1f, 1f, 0.10f),
                new Vector4(0.20f, 0.20f, 0.20f, 0.62f)),
            "Royal Sigil" => new ThemePalette(
                Dark(Blend(new Vector4(0.30f, 0.16f, 0.45f, 1f), customAccent, 0.30f, 1f), 0.20f, MathF.Max(fallbackBackground.W, 0.92f)),
                Dark(Blend(new Vector4(0.36f, 0.20f, 0.55f, 1f), customAccent, 0.25f, 1f), 0.46f, 0.76f),
                Dark(Blend(new Vector4(0.43f, 0.27f, 0.65f, 1f), customAccent, 0.25f, 1f), 0.54f, 0.54f),
                Blend(new Vector4(0.78f, 0.52f, 1.00f, 1f), customAccent, 0.42f, 1f),
                Light(Blend(new Vector4(0.82f, 0.62f, 1.00f, 1f), customAccent, 0.32f, 1f), 0.32f, 1f),
                Light(Blend(new Vector4(0.62f, 0.45f, 0.92f, 1f), customAccent, 0.28f, 1f), 0.18f, 1f),
                Light(Blend(new Vector4(0.86f, 0.76f, 1.00f, 1f), customAccent, 0.18f, 1f), 0.26f, 1f),
                Blend(new Vector4(0.70f, 0.45f, 1.00f, 0.13f), customAccent, 0.45f, 0.13f),
                Blend(new Vector4(0.70f, 0.45f, 1.00f, 0.30f), customAccent, 0.45f, 0.30f),
                Dark(Blend(new Vector4(0.46f, 0.22f, 0.70f, 1f), customAccent, 0.30f, 1f), 0.60f, 0.64f)),
            "Ember Forge" => new ThemePalette(
                new Vector4(0.070f, 0.026f, 0.010f, MathF.Max(fallbackBackground.W, 0.93f)),
                new Vector4(0.180f, 0.065f, 0.020f, 0.78f),
                new Vector4(0.260f, 0.105f, 0.035f, 0.56f),
                Blend(new Vector4(1.00f, 0.42f, 0.16f, 1f), customAccent, 0.34f, 1f),
                new Vector4(1.00f, 0.79f, 0.48f, 1f),
                new Vector4(1.00f, 0.55f, 0.28f, 1f),
                new Vector4(1.00f, 0.91f, 0.78f, 1f),
                new Vector4(1.00f, 0.32f, 0.08f, 0.15f),
                new Vector4(1.00f, 0.22f, 0.04f, 0.30f),
                new Vector4(0.48f, 0.16f, 0.05f, 0.68f)),
            "Ocean Tide" => new ThemePalette(
                new Vector4(0.005f, 0.032f, 0.070f, MathF.Max(fallbackBackground.W, 0.92f)),
                new Vector4(0.020f, 0.105f, 0.165f, 0.76f),
                new Vector4(0.035f, 0.155f, 0.220f, 0.54f),
                Blend(new Vector4(0.24f, 0.72f, 1.00f, 1f), customAccent, 0.35f, 1f),
                new Vector4(0.78f, 0.94f, 1.00f, 1f),
                new Vector4(0.48f, 0.78f, 0.96f, 1f),
                new Vector4(0.88f, 0.97f, 1.00f, 1f),
                new Vector4(0.16f, 0.55f, 1.00f, 0.13f),
                new Vector4(0.12f, 0.68f, 1.00f, 0.26f),
                new Vector4(0.05f, 0.27f, 0.42f, 0.66f)),
            "Void Stars" => new ThemePalette(
                new Vector4(0.012f, 0.008f, 0.034f, MathF.Max(fallbackBackground.W, 0.94f)),
                new Vector4(0.040f, 0.026f, 0.098f, 0.78f),
                new Vector4(0.070f, 0.040f, 0.145f, 0.56f),
                Blend(new Vector4(0.68f, 0.38f, 1.00f, 1f), customAccent, 0.35f, 1f),
                new Vector4(0.96f, 0.88f, 1.00f, 1f),
                new Vector4(0.72f, 0.56f, 0.98f, 1f),
                new Vector4(0.94f, 0.92f, 1.00f, 1f),
                new Vector4(0.58f, 0.32f, 1.00f, 0.13f),
                new Vector4(0.46f, 0.26f, 1.00f, 0.28f),
                new Vector4(0.20f, 0.11f, 0.46f, 0.66f)),
            "Casino" => new ThemePalette(
                new Vector4(0.055f, 0.010f, 0.012f, MathF.Max(fallbackBackground.W, 0.94f)),
                new Vector4(0.155f, 0.026f, 0.030f, 0.80f),
                new Vector4(0.030f, 0.120f, 0.055f, 0.58f),
                Blend(new Vector4(1.00f, 0.78f, 0.18f, 1f), customAccent, 0.22f, 1f),
                new Vector4(1.00f, 0.92f, 0.55f, 1f),
                new Vector4(0.95f, 0.30f, 0.28f, 1f),
                new Vector4(1.00f, 0.96f, 0.84f, 1f),
                new Vector4(1.00f, 0.70f, 0.12f, 0.17f),
                new Vector4(0.95f, 0.08f, 0.12f, 0.32f),
                new Vector4(0.35f, 0.035f, 0.040f, 0.72f)),
            "Frostbite" => new ThemePalette(
                new Vector4(0.010f, 0.038f, 0.070f, MathF.Max(fallbackBackground.W, 0.94f)),
                new Vector4(0.035f, 0.115f, 0.165f, 0.78f),
                new Vector4(0.065f, 0.175f, 0.235f, 0.56f),
                Blend(new Vector4(0.62f, 0.90f, 1.00f, 1f), customAccent, 0.24f, 1f),
                new Vector4(0.90f, 0.99f, 1.00f, 1f),
                new Vector4(0.58f, 0.82f, 1.00f, 1f),
                new Vector4(0.92f, 0.98f, 1.00f, 1f),
                new Vector4(0.62f, 0.92f, 1.00f, 0.15f),
                new Vector4(0.50f, 0.86f, 1.00f, 0.30f),
                new Vector4(0.030f, 0.210f, 0.300f, 0.66f)),
            "Gold Saucer" => new ThemePalette(
                new Vector4(0.075f, 0.052f, 0.012f, MathF.Max(fallbackBackground.W, 0.92f)),
                new Vector4(0.170f, 0.120f, 0.030f, 0.76f),
                new Vector4(0.230f, 0.170f, 0.050f, 0.56f),
                Blend(new Vector4(1.00f, 0.76f, 0.20f, 1f), customAccent, 0.28f, 1f),
                new Vector4(1.00f, 0.92f, 0.62f, 1f),
                new Vector4(1.00f, 0.76f, 0.36f, 1f),
                new Vector4(1.00f, 0.96f, 0.82f, 1f),
                new Vector4(1.00f, 0.72f, 0.12f, 0.14f),
                new Vector4(1.00f, 0.70f, 0.10f, 0.28f),
                new Vector4(0.45f, 0.30f, 0.06f, 0.66f)),
            _ => new ThemePalette(
                fallbackBackground,
                ResolvePanelTint(themeName ?? string.Empty, fallbackBackground),
                new Vector4(MathF.Min(1f, fallbackBackground.X + 0.03f), MathF.Min(1f, fallbackBackground.Y + 0.03f), MathF.Min(1f, fallbackBackground.Z + 0.03f), 0.46f),
                fallbackAccent,
                new Vector4(0.94f, 0.98f, 0.96f, 1f),
                new Vector4(0.68f, 0.78f, 0.74f, 1f),
                new Vector4(0.90f, 0.94f, 0.92f, 1f),
                new Vector4(fallbackAccent.X, fallbackAccent.Y, fallbackAccent.Z, 0.10f),
                new Vector4(fallbackAccent.X, fallbackAccent.Y, fallbackAccent.Z, 0.22f),
                new Vector4(fallbackAccent.X * 0.50f, fallbackAccent.Y * 0.50f, fallbackAccent.Z * 0.50f, 0.58f)),
        };
            return ApplyThemeColor(palette, customAccent, key);
    }

    public static readonly string[] ThemeNames = ["Default", "Neon Grid", "Pastel Bloom", "Glass Rain", "Minimal Ink", "Royal Sigil", "Ember Forge", "Ocean Tide", "Void Stars", "Gold Saucer", "Casino", "Frostbite"];
    public static readonly string[] PlaceholderStyles = ["Question Mark", "Initials", "User Icon", "Sparkle"];
    public static readonly string[] VisibilityOptions = ["Friends", "All", "Nobody"];

    public static Vector4 ResolveAccent(string themeName, Vector4 fallback)
    {
        return NormalizeThemeName(themeName) switch
        {
            "Neon Grid" => Blend(new Vector4(0.30f, 1.00f, 0.78f, 1f), fallback, 0.45f, 1f),
            "Pastel Bloom" => Blend(new Vector4(1.00f, 0.65f, 0.86f, 1f), fallback, 0.35f, 1f),
            "Glass Rain" => Blend(new Vector4(0.70f, 0.90f, 1.00f, 1f), fallback, 0.35f, 1f),
            "Minimal Ink" => Blend(new Vector4(0.86f, 0.86f, 0.86f, 1f), fallback, 0.28f, 1f),
            "Royal Sigil" => Blend(new Vector4(0.72f, 0.48f, 1.00f, 1f), fallback, 0.38f, 1f),
            "Ember Forge" => Blend(new Vector4(1.00f, 0.42f, 0.16f, 1f), fallback, 0.35f, 1f),
            "Ocean Tide" => Blend(new Vector4(0.24f, 0.72f, 1.00f, 1f), fallback, 0.35f, 1f),
            "Void Stars" => Blend(new Vector4(0.68f, 0.38f, 1.00f, 1f), fallback, 0.35f, 1f),
            "Gold Saucer" => Blend(new Vector4(1.00f, 0.76f, 0.20f, 1f), fallback, 0.28f, 1f),
            "Casino" => Blend(new Vector4(1.00f, 0.78f, 0.18f, 1f), fallback, 0.22f, 1f),
            "Frostbite" => Blend(new Vector4(0.62f, 0.90f, 1.00f, 1f), fallback, 0.24f, 1f),
            _ => fallback,
        };
    }

    public static Vector4 ResolvePanelTint(string themeName, Vector4 windowBackground)
    {
        var accent = ResolveAccent(themeName, new Vector4(0.16f, 0.86f, 0.67f, 1f));
        var strength = (themeName ?? string.Empty).Trim() switch
        {
            "Minimal Ink" => 0.02f,
            "Glass Rain" => 0.08f,
            "Pastel Bloom" => 0.06f,
            "Royal Sigil" => 0.07f,
            "Neon Grid" => 0.09f,
            "Casino" => 0.08f,
            "Frostbite" => 0.08f,
            _ => 0.04f,
        };
        return new Vector4(
            MathF.Min(1f, windowBackground.X + accent.X * strength),
            MathF.Min(1f, windowBackground.Y + accent.Y * strength),
            MathF.Min(1f, windowBackground.Z + accent.Z * strength),
            MathF.Max(windowBackground.W, 0.30f));
    }


    public static void DrawThemeBackdrop(ImDrawListPtr drawList, Vector2 pos, Vector2 size, string? themeName, ThemePalette palette, float scale)
    {
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(palette.WindowBackground), 0f);
        DrawThemeOverlay(drawList, pos, size, themeName, palette, scale);
    }

    public static void DrawThemeOverlay(ImDrawListPtr drawList, Vector2 pos, Vector2 size, string? themeName, ThemePalette palette, float scale)
    {
        var key = NormalizeThemeName(themeName);
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(WithAlpha(palette.Panel, 0.16f)), 0f);

        if (string.Equals(key, "Neon Grid", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < 7; i++)
            {
                var y = pos.Y + size.Y * (i + 1) / 8f;
                drawList.AddLine(new Vector2(pos.X, y), new Vector2(pos.X + size.X, y), ImGui.GetColorU32(WithAlpha(palette.Accent, i % 2 == 0 ? 0.11f : 0.045f)), 1f * scale);
            }
            for (var i = 0; i < 4; i++)
            {
                var center = pos + new Vector2(size.X * (0.18f + i * 0.20f), size.Y * (i % 2 == 0 ? 0.22f : 0.72f));
                drawList.AddCircle(center, 9f * scale, ImGui.GetColorU32(WithAlpha(palette.Accent, 0.28f)), 18, 1.1f * scale);
                drawList.AddCircleFilled(center, 2.2f * scale, ImGui.GetColorU32(WithAlpha(palette.Accent, 0.45f)), 12);
            }
            drawList.AddLine(pos + new Vector2(size.X * 0.03f, size.Y * 0.92f), pos + new Vector2(size.X * 0.96f, size.Y * 0.12f), ImGui.GetColorU32(WithAlpha(palette.Accent, 0.10f)), 1.4f * scale);
            return;
        }

        if (string.Equals(key, "Pastel Bloom", StringComparison.OrdinalIgnoreCase))
        {
            var c1 = WithAlpha(palette.Accent, 0.15f);
            var c2 = WithAlpha(palette.Glow, 0.72f);
            drawList.AddCircleFilled(pos + new Vector2(size.X * 0.82f, size.Y * 0.22f), 56f * scale, ImGui.GetColorU32(c2), 64);
            drawList.AddCircleFilled(pos + new Vector2(size.X * 0.24f, size.Y * 0.72f), 34f * scale, ImGui.GetColorU32(c1), 48);
            drawList.AddCircleFilled(pos + new Vector2(size.X * 0.58f, size.Y * 0.55f), 18f * scale, ImGui.GetColorU32(WithAlpha(Vector4.One, 0.045f)), 36);
            for (var i = 0; i < 5; i++)
            {
                var r = (4f + i * 1.2f) * scale;
                var p = pos + new Vector2(size.X * (0.12f + i * 0.17f), size.Y * (0.22f + (i % 3) * 0.18f));
                drawList.AddCircle(p, r, ImGui.GetColorU32(WithAlpha(palette.Subtitle, 0.16f)), 16, 1f * scale);
            }
            return;
        }

        if (string.Equals(key, "Glass Rain", StringComparison.OrdinalIgnoreCase))
        {
            drawList.AddRectFilledMultiColor(pos, pos + size, ImGui.GetColorU32(WithAlpha(Vector4.One, 0.12f)), ImGui.GetColorU32(WithAlpha(Vector4.One, 0.035f)), ImGui.GetColorU32(Vector4.Zero), ImGui.GetColorU32(Vector4.Zero));
            for (var i = -3; i < 7; i++)
            {
                var x = pos.X + i * 52f * scale;
                drawList.AddLine(new Vector2(x, pos.Y + size.Y), new Vector2(x + 86f * scale, pos.Y), ImGui.GetColorU32(WithAlpha(Vector4.One, 0.055f)), 1.1f * scale);
            }
            DrawDotOverlay(drawList, pos, size, WithAlpha(palette.Accent, 0.045f), scale, 24f);
            return;
        }

        if (string.Equals(key, "Minimal Ink", StringComparison.OrdinalIgnoreCase))
        {
            var line = ImGui.GetColorU32(WithAlpha(palette.Accent, 0.20f));
            drawList.AddLine(pos + new Vector2(0f, 1f * scale), pos + new Vector2(size.X, 1f * scale), line, 1f * scale);
            drawList.AddLine(pos + new Vector2(0f, size.Y - 1f * scale), pos + new Vector2(size.X, size.Y - 1f * scale), line, 1f * scale);
            var corner = 15f * scale;
            drawList.AddLine(pos + new Vector2(10f * scale, 10f * scale), pos + new Vector2(10f * scale + corner, 10f * scale), line, 1f * scale);
            drawList.AddLine(pos + new Vector2(10f * scale, 10f * scale), pos + new Vector2(10f * scale, 10f * scale + corner), line, 1f * scale);
            drawList.AddLine(pos + new Vector2(size.X - 10f * scale - corner, size.Y - 10f * scale), pos + new Vector2(size.X - 10f * scale, size.Y - 10f * scale), line, 1f * scale);
            drawList.AddLine(pos + new Vector2(size.X - 10f * scale, size.Y - 10f * scale - corner), pos + new Vector2(size.X - 10f * scale, size.Y - 10f * scale), line, 1f * scale);
            return;
        }

        if (string.Equals(key, "Royal Sigil", StringComparison.OrdinalIgnoreCase))
        {
            drawList.AddCircleFilled(pos + new Vector2(size.X * 0.82f, size.Y * 0.20f), 64f * scale, ImGui.GetColorU32(palette.Glow), 64);
            var diamond = ImGui.GetColorU32(WithAlpha(palette.Accent, 0.13f));
            for (var i = 0; i < 5; i++)
            {
                var center = pos + new Vector2(size.X * (0.18f + i * 0.15f), size.Y * (0.24f + (i % 2) * 0.28f));
                var r = (8f + i % 2 * 3f) * scale;
                drawList.AddQuad(center + new Vector2(0f, -r), center + new Vector2(r, 0f), center + new Vector2(0f, r), center + new Vector2(-r, 0f), diamond, 1.1f * scale);
            }
            DrawDotOverlay(drawList, pos, size, WithAlpha(palette.Subtitle, 0.05f), scale, 28f);
            return;
        }

        if (string.Equals(key, "Ember Forge", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < 8; i++)
            {
                var p = pos + new Vector2(size.X * (0.10f + i * 0.115f), size.Y * (0.80f - (i % 3) * 0.20f));
                drawList.AddTriangleFilled(p, p + new Vector2(8f, -20f) * scale, p + new Vector2(16f, 0f) * scale, ImGui.GetColorU32(WithAlpha(palette.Accent, 0.07f + 0.018f * (i % 2))));
            }
            drawList.AddLine(pos + new Vector2(0f, size.Y * 0.82f), pos + new Vector2(size.X, size.Y * 0.62f), ImGui.GetColorU32(WithAlpha(palette.Subtitle, 0.10f)), 1.3f * scale);
            return;
        }

        if (string.Equals(key, "Ocean Tide", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < 5; i++)
            {
                var y = pos.Y + size.Y * (0.22f + i * 0.14f);
                var color = ImGui.GetColorU32(WithAlpha(i % 2 == 0 ? palette.Accent : palette.Subtitle, 0.09f));
                drawList.AddBezierCubic(new Vector2(pos.X - 20f * scale, y), new Vector2(pos.X + size.X * 0.26f, y - 38f * scale), new Vector2(pos.X + size.X * 0.68f, y + 38f * scale), new Vector2(pos.X + size.X + 20f * scale, y), color, 1.4f * scale, 24);
            }
            drawList.AddCircle(pos + new Vector2(size.X * 0.82f, size.Y * 0.18f), 28f * scale, ImGui.GetColorU32(WithAlpha(palette.Accent, 0.18f)), 36, 1.2f * scale);
            return;
        }

        if (string.Equals(key, "Void Stars", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < 18; i++)
            {
                var x = pos.X + size.X * ((i * 37 % 100) / 100f);
                var y = pos.Y + size.Y * ((i * 61 % 100) / 100f);
                var r = (i % 4 == 0 ? 2.1f : 1.1f) * scale;
                drawList.AddCircleFilled(new Vector2(x, y), r, ImGui.GetColorU32(WithAlpha(i % 3 == 0 ? palette.Subtitle : palette.Accent, 0.16f)), 8);
            }
            drawList.AddCircle(pos + new Vector2(size.X * 0.30f, size.Y * 0.42f), 58f * scale, ImGui.GetColorU32(WithAlpha(palette.Accent, 0.10f)), 64, 1f * scale);
            return;
        }

        if (string.Equals(key, "Casino", StringComparison.OrdinalIgnoreCase))
        {
            var icons = new[] { 0xF523, 0xF524, 0xF525, 0xF526, 0xF527, 0xF522, 0xF091, 0xF005 };
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                for (var i = 0; i < 16; i++)
                {
                    var icon = char.ConvertFromUtf32(icons[i % icons.Length]);
                    var x = pos.X + size.X * ((i * 23 % 100) / 100f);
                    var y = pos.Y + size.Y * ((i * 41 % 100) / 100f);
                    var color = i % 3 == 0 ? palette.Accent : i % 3 == 1 ? palette.Subtitle : new Vector4(0.96f, 0.12f, 0.18f, 0.15f);
                    drawList.AddText(new Vector2(x, y), ImGui.GetColorU32(WithAlpha(color, 0.13f)), icon);
                }
            }
            for (var i = 0; i < 5; i++)
            {
                var y = pos.Y + size.Y * (0.16f + i * 0.18f);
                drawList.AddLine(new Vector2(pos.X, y), new Vector2(pos.X + size.X, y + (i % 2 == 0 ? 18f : -18f) * scale), ImGui.GetColorU32(WithAlpha(palette.Accent, 0.065f)), 1.2f * scale);
            }
            return;
        }

        if (string.Equals(key, "Frostbite", StringComparison.OrdinalIgnoreCase))
        {
            drawList.AddRectFilledMultiColor(pos, pos + size, ImGui.GetColorU32(WithAlpha(Vector4.One, 0.085f)), ImGui.GetColorU32(WithAlpha(palette.Accent, 0.070f)), ImGui.GetColorU32(Vector4.Zero), ImGui.GetColorU32(WithAlpha(palette.Glow, 0.055f)));
            for (var i = 0; i < 12; i++)
            {
                var c = pos + new Vector2(size.X * ((i * 29 % 100) / 100f), size.Y * ((i * 47 % 100) / 100f));
                var r = (4f + (i % 3) * 2f) * scale;
                var col = ImGui.GetColorU32(WithAlpha(i % 2 == 0 ? palette.Accent : Vector4.One, 0.13f));
                drawList.AddLine(c + new Vector2(-r, 0f), c + new Vector2(r, 0f), col, 1f * scale);
                drawList.AddLine(c + new Vector2(0f, -r), c + new Vector2(0f, r), col, 1f * scale);
                drawList.AddLine(c + new Vector2(-r * 0.7f, -r * 0.7f), c + new Vector2(r * 0.7f, r * 0.7f), col, 1f * scale);
                drawList.AddLine(c + new Vector2(-r * 0.7f, r * 0.7f), c + new Vector2(r * 0.7f, -r * 0.7f), col, 1f * scale);
            }
            return;
        }

        if (string.Equals(key, "Gold Saucer", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < 7; i++)
            {
                var center = pos + new Vector2(size.X * (0.08f + i * 0.14f), size.Y * (0.18f + (i % 2) * 0.50f));
                var r = 7f * scale;
                drawList.AddQuad(center + new Vector2(0, -r), center + new Vector2(r, 0), center + new Vector2(0, r), center + new Vector2(-r, 0), ImGui.GetColorU32(WithAlpha(palette.Accent, 0.14f)), 1.2f * scale);
            }
            DrawDotOverlay(drawList, pos, size, WithAlpha(palette.Subtitle, 0.055f), scale, 22f);
            return;
        }

        drawList.AddCircleFilled(pos + new Vector2(size.X * 0.86f, size.Y * 0.18f), 46f * scale, ImGui.GetColorU32(palette.Glow), 58);
        drawList.AddLine(pos + new Vector2(size.X * 0.10f, size.Y * 0.86f), pos + new Vector2(size.X * 0.92f, size.Y * 0.10f), ImGui.GetColorU32(WithAlpha(palette.Accent, 0.10f)), 1f * scale);
        DrawDotOverlay(drawList, pos, size, WithAlpha(palette.Accent, 0.055f), scale, 26f);
    }

    private static void DrawDotOverlay(ImDrawListPtr drawList, Vector2 pos, Vector2 size, Vector4 color, float scale, float spacing)
    {
        var colorU32 = ImGui.GetColorU32(color);
        for (var y = pos.Y + spacing * scale; y < pos.Y + size.Y; y += spacing * scale)
            for (var x = pos.X + spacing * scale; x < pos.X + size.X; x += spacing * scale)
                drawList.AddCircleFilled(new Vector2(x, y), 1.1f * scale, colorU32, 8);
    }

    private static Vector4 WithAlpha(Vector4 color, float alpha)
        => new(color.X, color.Y, color.Z, alpha);

    public static string NormalizeThemeName(string? themeName)
    {
        var value = (themeName ?? string.Empty).Trim();
        if (string.Equals(value, "Neon", StringComparison.OrdinalIgnoreCase)) return "Neon Grid";
        if (string.Equals(value, "Pastel", StringComparison.OrdinalIgnoreCase)) return "Pastel Bloom";
        if (string.Equals(value, "Glass", StringComparison.OrdinalIgnoreCase)) return "Glass Rain";
        if (string.Equals(value, "Minimal", StringComparison.OrdinalIgnoreCase)) return "Minimal Ink";
        if (string.Equals(value, "Royal", StringComparison.OrdinalIgnoreCase)) return "Royal Sigil";
        if (string.Equals(value, "Frost", StringComparison.OrdinalIgnoreCase)) return "Frostbite";
        foreach (var theme in ThemeNames)
            if (string.Equals(theme, value, StringComparison.OrdinalIgnoreCase)) return theme;
        return "Default";
    }

    public static string GetSectionIcon(string? themeName, string section)
    {
        var key = NormalizeThemeName(themeName);
        var sectionKey = (section ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "Neon Grid" => sectionKey switch { "status" => "\uf1eb", "bio" => "\uf2bd", "location" => "\uf3c5", _ => "\uf111" },
            "Pastel Bloom" => sectionKey switch { "status" => "\uf004", "bio" => "\uf4aa", "location" => "\uf5a0", _ => "\uf111" },
            "Glass Rain" => sectionKey switch { "status" => "\uf0e7", "bio" => "\uf740", "location" => "\uf043", _ => "\uf111" },
            "Minimal Ink" => sectionKey switch { "status" => "\uf111", "bio" => "\uf303", "location" => "\uf041", _ => "\uf111" },
            "Royal Sigil" => sectionKey switch { "status" => "\uf521", "bio" => "\uf56b", "location" => "\uf3c5", _ => "\uf111" },
            "Ember Forge" => sectionKey switch { "status" => "\uf06d", "bio" => "\uf0ad", "location" => "\uf124", _ => "\uf111" },
            "Ocean Tide" => sectionKey switch { "status" => "\uf773", "bio" => "\uf578", "location" => "\uf5c9", _ => "\uf111" },
            "Void Stars" => sectionKey switch { "status" => "\uf005", "bio" => "\uf753", "location" => "\uf57d", _ => "\uf111" },
            "Gold Saucer" => sectionKey switch { "status" => "\uf3a5", "bio" => "\uf559", "location" => "\uf3c5", _ => "\uf111" },
            "Casino" => sectionKey switch { "status" => char.ConvertFromUtf32(0xF522), "bio" => char.ConvertFromUtf32(0xF523), "location" => char.ConvertFromUtf32(0xF3C5), _ => char.ConvertFromUtf32(0xF525) },
            "Frostbite" => sectionKey switch { "status" => "\uf2dc", "bio" => "\uf76b", "location" => "\uf7ad", _ => "\uf111" },
            _ => sectionKey switch { "status" => "\uf111", "bio" => "\uf2bb", "location" => "\uf3c5", _ => "\uf111" },
        };
    }

    public static string NormalizeVisibility(string value, string fallback = "All")
    {
        foreach (var option in VisibilityOptions)
        {
            if (string.Equals(option, value, StringComparison.OrdinalIgnoreCase))
                return option;
        }
        return fallback;
    }
}
