using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System;
using System.Numerics;

namespace Privacy.UI;

internal sealed class DotBackdropRenderer
{
    public void Draw(ImDrawListPtr drawList, Vector2 min, Vector2 size, Vector4 dotColor, int columns = 5, float spacing = 8f, float radius = 0.8f, float inset = 6f)
    {
        if (size.X <= 0f || size.Y <= 0f) return;

        var scale = ImGuiHelpers.GlobalScale;
        var scaledInset = inset * scale;
        var scaledSpacing = spacing * scale;
        var scaledRadius = MathF.Max(0.5f, radius * scale);
        var color = ImGui.GetColorU32(dotColor);
        var usableWidth = MathF.Max(1f, size.X - scaledInset * 2f);
        var dynamicSpacing = columns > 1 ? usableWidth / (columns - 1) : scaledSpacing;

        for (var y = min.Y + scaledInset; y <= min.Y + size.Y - scaledInset; y += scaledSpacing)
        {
            for (var c = 0; c < columns; c++)
            {
                var x = min.X + scaledInset + dynamicSpacing * c;
                drawList.AddCircleFilled(new Vector2(x, y), scaledRadius, color, 8);
            }
        }
    }
}
