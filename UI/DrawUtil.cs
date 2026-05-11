using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace Privacy.UI;

internal static class DrawUtil
{
    public static void AddTextWithShadow(ImDrawListPtr drawList, Vector2 pos, uint color, string text, float? fontSize = null)
    {
        var font = ImGui.GetFont();
        var size = fontSize ?? ImGui.GetFontSize();
        var shadow = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.78f));
        drawList.AddText(font, size, pos + new Vector2(1.5f, 1.5f), shadow, text);
        drawList.AddText(font, size, pos, color, text);
    }

    public static void DrawGlowRect(ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector4 color, float rounding, float intensity = 0.18f)
    {
        var c1 = ImGui.GetColorU32(UiColors.WithAlpha(color, intensity));
        var c2 = ImGui.GetColorU32(UiColors.WithAlpha(color, intensity * 0.55f));
        drawList.AddRectFilled(min - new Vector2(2f), max + new Vector2(2f), c2, rounding + 2f);
        drawList.AddRect(min, max, c1, rounding, ImDrawFlags.None, 2f);
    }
}
