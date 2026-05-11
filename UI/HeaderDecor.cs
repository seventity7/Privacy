using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System;
using System.Numerics;

namespace Privacy.UI;

internal static class HeaderDecor
{
    public static void Draw(ImDrawListPtr drawList, Vector2 start, float width, float height, Vector4 backgroundColor, Vector4 accentColor)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var end = start + new Vector2(width, height);
        var top = new Vector4(
            MathF.Min(1f, backgroundColor.X + 0.028f),
            MathF.Min(1f, backgroundColor.Y + 0.028f),
            MathF.Min(1f, backgroundColor.Z + 0.028f),
            MathF.Min(1f, MathF.Max(0.38f, backgroundColor.W)));
        var bottom = new Vector4(backgroundColor.X, backgroundColor.Y, backgroundColor.Z, 0f);

        drawList.AddRectFilledMultiColor(start, end, ImGui.GetColorU32(top), ImGui.GetColorU32(top), ImGui.GetColorU32(bottom), ImGui.GetColorU32(bottom));
        DrawBottomGradient(drawList, start, end, width, backgroundColor, scale);
        DrawParticles(drawList, start, new Vector2(width, height), accentColor, scale);
    }

    private static void DrawParticles(ImDrawListPtr drawList, Vector2 start, Vector2 size, Vector4 accentColor, float scale)
    {
        var color = UiColors.WithAlpha(accentColor, 0.20f);
        var spacing = 17f * scale;
        var radius = 0.9f * scale;
        var end = start + size;
        drawList.PushClipRect(start, end, true);

        for (var y = start.Y + 8f * scale; y < end.Y - 8f * scale; y += spacing)
        {
            for (var x = start.X + 8f * scale; x < end.X - 8f * scale; x += spacing)
                drawList.AddCircleFilled(new Vector2(x, y), radius, ImGui.GetColorU32(color), 8);
        }

        drawList.AddLine(start + new Vector2(size.X * 0.53f, 0f), start + new Vector2(size.X * 0.84f, size.Y), ImGui.GetColorU32(UiColors.WithAlpha(accentColor, 0.18f)), 2f * scale);
        drawList.AddLine(start + new Vector2(size.X * 0.67f, 0f), start + new Vector2(size.X * 0.96f, size.Y), ImGui.GetColorU32(UiColors.WithAlpha(accentColor, 0.07f)), 1f * scale);
        drawList.PopClipRect();
    }

    private static void DrawBottomGradient(ImDrawListPtr drawList, Vector2 start, Vector2 end, float width, Vector4 backgroundColor, float scale)
    {
        const int bands = 8;
        var gradientHeight = 60f * scale;
        var bottom = new Vector4(backgroundColor.X, backgroundColor.Y, backgroundColor.Z, 1f);
        for (var i = 0; i < bands; i++)
        {
            var t0 = (float)i / bands;
            var t1 = (float)(i + 1) / bands;
            var s0 = t0 * t0;
            var s1 = t1 * t1;
            var color0 = new Vector4(bottom.X, bottom.Y, bottom.Z, 1f - s0);
            var color1 = new Vector4(bottom.X, bottom.Y, bottom.Z, 1f - s1);
            var y0 = end.Y - gradientHeight + gradientHeight * t0;
            var y1 = end.Y - gradientHeight + gradientHeight * t1;
            drawList.AddRectFilledMultiColor(new Vector2(start.X, y0), new Vector2(start.X + width, y1), ImGui.GetColorU32(color0), ImGui.GetColorU32(color0), ImGui.GetColorU32(color1), ImGui.GetColorU32(color1));
        }
    }
}
