using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System;
using System.Numerics;

namespace Privacy.UI;

internal static class WindowEdgeFade
{
    private const float BorderThickness = 18f;

    public static void DrawUnified(Vector4 color)
    {
        var headerTop = new Vector4(
            MathF.Min(1f, color.X + 0.028f),
            MathF.Min(1f, color.Y + 0.028f),
            MathF.Min(1f, color.Z + 0.028f),
            MathF.Min(1f, MathF.Max(0.38f, color.W)));
        var headerBottom = new Vector4(color.X, color.Y, color.Z, 0f);
        DrawUnified(color, UiColors.Get("PrivateBorder"), headerTop, headerBottom, 58f * ImGuiHelpers.GlobalScale);
    }

    public static void DrawUnified(Vector4 color, Vector4 borderColor)
    {
        var headerTop = new Vector4(
            MathF.Min(1f, color.X + 0.028f),
            MathF.Min(1f, color.Y + 0.028f),
            MathF.Min(1f, color.Z + 0.028f),
            MathF.Min(1f, MathF.Max(0.38f, color.W)));
        var headerBottom = new Vector4(color.X, color.Y, color.Z, 0f);
        DrawUnified(color, borderColor, headerTop, headerBottom, 58f * ImGuiHelpers.GlobalScale);
    }

    public static void DrawUnified(Vector4 color, Vector4 borderColor, Vector4 headerTop, Vector4 headerBottom, float headerHeight)
    {
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var cursor = ImGui.GetCursorScreenPos();
        var scale = ImGuiHelpers.GlobalScale;

        if (windowSize.X <= 1f || windowSize.Y <= 1f)
            return;

        var titleBottomY = windowPos.Y + ImGui.GetFrameHeight();
        var outerMin = new Vector2(windowPos.X, titleBottomY);
        var outerMax = new Vector2(windowPos.X + windowSize.X, windowPos.Y + windowSize.Y);
        var thickness = BorderThickness * scale;

        var panelMin = new Vector2(windowPos.X + thickness, titleBottomY);
        var panelMax = new Vector2(windowPos.X + windowSize.X - thickness, outerMax.Y);
        if (panelMax.Y <= panelMin.Y + 1f || panelMax.X <= panelMin.X + 1f)
            return;

        var bgU = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 1f));
        var headerTopU = ImGui.GetColorU32(headerTop);
        var headerBottomU = ImGui.GetColorU32(headerBottom);
        var headerBottomY = MathF.Min(outerMax.Y, outerMin.Y + MathF.Max(0f, headerHeight));

        drawList.PushClipRect(windowPos, windowPos + windowSize, false);

        drawList.AddRectFilled(panelMin, panelMax, bgU);

        drawList.AddRectFilled(
            outerMin,
            new Vector2(panelMin.X, outerMax.Y),
            bgU);

        drawList.AddRectFilled(
            new Vector2(panelMax.X, outerMin.Y),
            outerMax,
            bgU);

        if (headerBottomY > outerMin.Y)
        {
            drawList.AddRectFilledMultiColor(
                outerMin,
                new Vector2(outerMax.X, headerBottomY),
                headerTopU,
                headerTopU,
                headerBottomU,
                headerBottomU);
        }

        drawList.PopClipRect();

        if (cursor.X < panelMin.X)
            ImGui.SetCursorScreenPos(new Vector2(panelMin.X, cursor.Y));
    }

    public static void Draw(Vector4 color)
        => DrawUnified(color);
}
