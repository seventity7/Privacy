using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Privacy.UI;

internal sealed class WindowBuilder
{
    private readonly Window window;
    private readonly List<TitleBarButton> titleButtons = new();

    private WindowBuilder(Window window)
        => this.window = window ?? throw new ArgumentNullException(nameof(window));

    public static WindowBuilder For(Window window)
        => new(window);

    public WindowBuilder AllowPinning(bool allow = true)
    {
        window.AllowPinning = allow;
        return this;
    }

    public WindowBuilder AllowClickthrough(bool allow = true)
    {
        window.AllowClickthrough = allow;
        return this;
    }

    public WindowBuilder SetFixedSize(Vector2 size)
        => SetSizeConstraints(size, size);

    public WindowBuilder SetSizeConstraints(Vector2 min, Vector2 max)
    {
        window.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = min,
            MaximumSize = max,
        };
        return this;
    }

    public WindowBuilder AddFlags(ImGuiWindowFlags flags)
    {
        window.Flags |= flags;
        return this;
    }

    public WindowBuilder AddTitleBarButton(FontAwesomeIcon icon, string tooltip, Action onClick, Vector2? iconOffset = null)
    {
        titleButtons.Add(new TitleBarButton
        {
            Icon = icon,
            IconOffset = iconOffset ?? new Vector2(2f, 1f),
            Click = _ => onClick(),
            ShowTooltip = () => ImGui.SetTooltip(tooltip),
        });
        return this;
    }

    public Window Apply()
    {
        if (titleButtons.Count > 0)
            window.TitleBarButtons = titleButtons;
        return window;
    }
}
