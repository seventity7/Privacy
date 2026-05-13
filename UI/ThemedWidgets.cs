using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System;
using System.Numerics;

namespace Privacy.UI;

internal static class ThemedWidgets
{
    public static bool Button(string label, Vector2 size, Vector4 accent, bool enabled = true)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var visibleLabel = VisibleLabel(label);
        var textSize = ImGui.CalcTextSize(visibleLabel);
        if (size.X <= 0f)
            size.X = textSize.X + 28f * scale;
        if (size.Y <= 0f)
            size.Y = MathF.Max(ImGui.GetFrameHeight(), textSize.Y + 12f * scale);

        var pos = ImGui.GetCursorScreenPos();
        if (!enabled)
            ImGui.BeginDisabled();

        var clicked = ImGui.InvisibleButton(label, size);
        var hovered = enabled && ImGui.IsItemHovered();
        var active = enabled && ImGui.IsItemActive();
        if (!enabled)
            ImGui.EndDisabled();

        DrawButtonSurface(ImGui.GetWindowDrawList(), pos, size, accent, hovered, active, enabled);

        var textColor = enabled ? UiColors.Text : UiColors.WithAlpha(UiColors.TextDim, 0.70f);
        var textPos = pos + (size - textSize) * 0.5f;
        ImGui.GetWindowDrawList().AddText(textPos + new Vector2(1f * scale, 1.2f * scale), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.66f)), visibleLabel);
        ImGui.GetWindowDrawList().AddText(textPos, ImGui.GetColorU32(textColor), visibleLabel);
        return clicked && enabled;
    }

    public static bool BeginCombo(string id, string preview, Vector4 accent, ImGuiComboFlags flags = ImGuiComboFlags.None)
    {
        PushComboStyle(accent);
        var opened = ImGui.BeginCombo(id, preview, flags);
        DrawComboOverlayOnLastItem(accent);
        PopComboStyle();
        return opened;
    }

    public static bool Combo(string label, ref int currentItem, string[] items, int itemsCount, Vector4 accent)
    {
        PushComboStyle(accent);
        var changed = ImGui.Combo(label, ref currentItem, items, itemsCount);
        DrawComboOverlayOnLastItem(accent);
        PopComboStyle();
        return changed;
    }

    public static bool Combo(string label, ref int currentItem, string[] items, int itemsCount, int popupMaxHeightInItems, Vector4 accent)
    {
        PushComboStyle(accent);
        var preview = currentItem >= 0 && currentItem < items.Length ? items[currentItem] : string.Empty;
        var changed = false;
        var opened = ImGui.BeginCombo(label, preview);
        DrawComboOverlayOnLastItem(accent);
        PopComboStyle();

        if (!opened)
            return false;

        var visibleCount = Math.Min(itemsCount, items.Length);
        var maxItems = Math.Max(1, popupMaxHeightInItems);
        var itemHeight = ImGui.GetTextLineHeightWithSpacing();
        var childHeight = Math.Min(visibleCount, maxItems) * itemHeight + ImGui.GetStyle().WindowPadding.Y * 2f;
        var useChild = visibleCount > maxItems;

        if (useChild)
            ImGui.BeginChild($"{label}_scroll", new Vector2(0f, childHeight), false);

        for (var i = 0; i < visibleCount; i++)
        {
            var selected = i == currentItem;
            if (ImGui.Selectable(items[i], selected))
            {
                currentItem = i;
                changed = true;
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        if (useChild)
            ImGui.EndChild();

        ImGui.EndCombo();
        return changed;
    }

    private static void DrawButtonSurface(ImDrawListPtr drawList, Vector2 pos, Vector2 size, Vector4 accent, bool hovered, bool active, bool enabled)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var rounding = 8f * scale;
        var top = pos;
        var bottom = pos + size;
        var bottomLip = MathF.Min(6f * scale, size.Y * 0.26f);
        var lipTop = new Vector2(top.X, bottom.Y - bottomLip);

        var alpha = enabled ? 1f : 0.42f;
        var baseColor = active ? Darken(accent, 0.66f, 0.98f * alpha) : Darken(accent, hovered ? 0.86f : 0.78f, 0.96f * alpha);
        var topColor = active ? Darken(accent, 0.72f, 0.98f * alpha) : new Vector4(accent.X, accent.Y, accent.Z, (hovered ? 0.98f : 0.92f) * alpha);
        var bottomColor = Darken(accent, active ? 0.42f : 0.54f, 0.96f * alpha);
        var lipColor = Darken(accent, active ? 0.30f : 0.38f, 1f * alpha);
        var borderColor = new Vector4(accent.X, accent.Y, accent.Z, (hovered ? 0.92f : 0.72f) * alpha);
        var innerBorder = new Vector4(1f, 1f, 1f, enabled ? hovered ? 0.18f : 0.10f : 0.04f);
        var shadow = new Vector4(0f, 0f, 0f, enabled ? 0.42f : 0.20f);

        drawList.AddRectFilled(top + new Vector2(0f, 3f * scale), bottom + new Vector2(0f, 3f * scale), ImGui.GetColorU32(shadow), rounding);
        drawList.AddRectFilled(top, bottom, ImGui.GetColorU32(baseColor), rounding);

        var bodyInset = 1.5f * scale;
        var upperMin = top + new Vector2(bodyInset, bodyInset);
        var upperMax = new Vector2(bottom.X - bodyInset, lipTop.Y + 1f * scale);
        if (upperMax.X > upperMin.X && upperMax.Y > upperMin.Y)
        {
            drawList.AddRectFilled(
                upperMin,
                upperMax,
                ImGui.GetColorU32(topColor),
                MathF.Max(1f, rounding - bodyInset),
                ImDrawFlags.RoundCornersTop);
        }

        var lowerMin = new Vector2(top.X + bodyInset, lipTop.Y - 1f * scale);
        var lowerMax = bottom - new Vector2(bodyInset, bodyInset);
        if (lowerMax.X > lowerMin.X && lowerMax.Y > lowerMin.Y)
        {
            drawList.AddRectFilled(
                lowerMin,
                lowerMax,
                ImGui.GetColorU32(lipColor),
                MathF.Max(1f, rounding - bodyInset),
                ImDrawFlags.RoundCornersBottom);
        }

        var shineTop = top + new Vector2(4f * scale, 3f * scale);
        var shineBottom = new Vector2(bottom.X - 4f * scale, top.Y + MathF.Max(7f * scale, size.Y * 0.30f));
        if (shineBottom.X > shineTop.X && shineBottom.Y > shineTop.Y)
        {
            drawList.AddRectFilledMultiColor(
                shineTop,
                shineBottom,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, enabled ? hovered ? 0.22f : 0.14f : 0.04f)),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, enabled ? hovered ? 0.14f : 0.08f : 0.03f)),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)));
        }

        drawList.AddRect(top, bottom, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.None, 1.3f * scale);
        drawList.AddRect(top + new Vector2(1f * scale), bottom - new Vector2(1f * scale), ImGui.GetColorU32(innerBorder), MathF.Max(1f, rounding - 1f * scale), ImDrawFlags.None, 1f * scale);

        if (active)
            drawList.AddRectFilled(top, bottom, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.14f)), rounding);
    }

    private static void DrawComboOverlayOnLastItem(Vector4 accent)
    {
        if (ImGui.GetItemRectSize().X <= 0f || ImGui.GetItemRectSize().Y <= 0f)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var scale = ImGuiHelpers.GlobalScale;
        var rounding = 7f * scale;
        var borderColor = new Vector4(accent.X, accent.Y, accent.Z, active ? 0.90f : hovered ? 0.78f : 0.58f);
        var innerColor = new Vector4(1f, 1f, 1f, hovered ? 0.11f : 0.05f);

        drawList.AddRect(min, max, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.None, 1.2f * scale);
        drawList.AddRect(min + new Vector2(1f * scale), max - new Vector2(1f * scale), ImGui.GetColorU32(innerColor), MathF.Max(1f, rounding - scale), ImDrawFlags.None, 1f * scale);
    }

    private static void PushComboStyle(Vector4 accent)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 7f * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleColor(ImGuiCol.Border, UiColors.WithAlpha(accent, 0.60f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Darken(accent, 0.38f, 0.94f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Darken(accent, 0.50f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Darken(accent, 0.58f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.Button, Darken(accent, 0.42f, 0.94f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Darken(accent, 0.54f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Darken(accent, 0.62f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Header, UiColors.WithAlpha(accent, 0.28f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, UiColors.WithAlpha(accent, 0.38f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, UiColors.WithAlpha(accent, 0.48f));
    }

    private static void PopComboStyle()
    {
        ImGui.PopStyleColor(10);
        ImGui.PopStyleVar(2);
    }

    private static Vector4 Darken(Vector4 color, float factor, float alpha)
        => new(MathF.Max(0f, color.X * factor), MathF.Max(0f, color.Y * factor), MathF.Max(0f, color.Z * factor), alpha);

    private static Vector4 Lighten(Vector4 color, float amount, float alpha)
        => new(MathF.Min(1f, color.X + (1f - color.X) * amount), MathF.Min(1f, color.Y + (1f - color.Y) * amount), MathF.Min(1f, color.Z + (1f - color.Z) * amount), alpha);

    public static void FadeSeparator(Vector4 accent, float thickness = 1f, float verticalPadding = 6f)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var y = pos.Y + verticalPadding * scale;
        var start = pos.X;
        var end = pos.X + width;
        var midStart = start + width * 0.12f;
        var midEnd = end - width * 0.12f;
        var strong = new Vector4(accent.X, accent.Y, accent.Z, 0.32f);
        var clear = new Vector4(accent.X, accent.Y, accent.Z, 0f);
        var t = MathF.Max(1f, thickness * scale);
        drawList.AddRectFilledMultiColor(new Vector2(start, y), new Vector2(midStart, y + t), ImGui.GetColorU32(clear), ImGui.GetColorU32(strong), ImGui.GetColorU32(strong), ImGui.GetColorU32(clear));
        drawList.AddRectFilled(new Vector2(midStart, y), new Vector2(midEnd, y + t), ImGui.GetColorU32(strong));
        drawList.AddRectFilledMultiColor(new Vector2(midEnd, y), new Vector2(end, y + t), ImGui.GetColorU32(strong), ImGui.GetColorU32(clear), ImGui.GetColorU32(clear), ImGui.GetColorU32(strong));
        ImGui.Dummy(new Vector2(width, (verticalPadding * 2f + thickness) * scale));
    }

    public static void PushThemedInputs(Vector4 accent)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleColor(ImGuiCol.Border, UiColors.WithAlpha(accent, 0.58f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.WithAlpha(UiColors.Get("PrivateFrameBg"), 0.96f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, UiColors.WithAlpha(accent, 0.16f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, UiColors.WithAlpha(accent, 0.22f));
        ImGui.PushStyleColor(ImGuiCol.Button, Darken(accent, 0.30f, 0.94f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Darken(accent, 0.46f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Darken(accent, 0.56f, 1f));
    }

    public static void PopThemedInputs()
    {
        ImGui.PopStyleColor(7);
        ImGui.PopStyleVar(2);
    }

    private static string VisibleLabel(string label)
    {
        var index = label.IndexOf("##", StringComparison.Ordinal);
        return index >= 0 ? label[..index] : label;
    }
}
