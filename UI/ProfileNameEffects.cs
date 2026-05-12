using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Privacy.UI;

internal static class ProfileNameEffects
{
    private readonly record struct ColourSet(string Name, string Base64);
    public readonly record struct NameEffect(string Id, string Label, string Tab, Vector4 Primary, Vector4 Secondary, Vector4 Third, float Speed, float Spread, int ColourSetIndex = -1);

    private static readonly ColourSet[] HonourificColourSets =
    [
        new("Pride Rainbow", "5AMD6RsC7TMC8ksB92MB/HsA/5EA/6IA/7IA/8MA/9QA/+UA5+MEutAKjr0RYaoYNZYeCIMlAHlFAG9rAGaRAF23AFTdAkv9FkXnKj/RPjm8UjOmZi2QcymCcymCcymCcymCcymCcymCZi2QUjOmPjm8Kj/RFkXnAkv9AFTdAF23AGaRAG9rAHlFCIMlNZYeYaoYjr0RutAK5+ME/+UA/9QA/8MA/7IA/6IA/5EA/HsA92MB8ksB7TMC6RsC5AMD"),
        new("Transgender", "W876b8nygsXplsDhqbvYvbfQ0LLI5K2/9aq59rXC+MDL+cvU+tbd/OHm/ezv/vf4//z9/fH0/Obr+9zi+tHZ+MbQ97vH9rC+7qu72q/Ex7TMs7nUn77djMLleMftZcz2Zcz2eMftjMLln77ds7nUx7TM2q/E7qu79rC+97vH+MbQ+tHZ+9zi/Obr/fH0//z9/vf4/ezv/OHm+tbd+cvU+MDL9rXC9aq55K2/0LLIvbfQqbvYlsDhgsXpb8nyW876"),
        new("Lesbian", "1S0A2lQT33ol46E46MdL7e5d8Opg9Nhe98Zc+rVZ/aNX/6Rm/7eG/8qm/93H//Hn/fj79Nnp67vY4p3G2n+10WKkzGCgxl2cwVuZvFmVtleRskqJrzqBrCp4qBpvpQpmpQpmqBpvrCp4rzqBskqJtleRvFmVwVuZxl2czGCg0WKk2oC1457H67zY9Nrp/fj7//Hn/93H/8qm/7eG/6Rm/aNX+rVZ98dc9Nhe8Opg7exd6MZK46A43nkl2lMT1S0A"),
        new("Bisexual", "1gJwzgx1xxZ6vyB/uCmDsDOIqT2NoUeSm0+Wm0+Wm0+Wm0+WlU6XgUuZbUibWUWeRkKgMj+iHjylCjmnCjmnHjylMj+iRkKgWUWebUibgUuZlU6Xm0+Wm0+Wm0+Wm0+WoUeSqT2NsDOIuCmDvyB/xxZ6zgx11gJw"),
        new("Black & White", "////9/f37+/v5+fn39/f19fXzs7OxsbGvr6+tra2rq6upqamnp6elpaWjo6OhoaGfX19dXV1bW1tZWVlXV1dVVVVTU1NRUVFPT09NTU1LS0tJCQkHBwcFBQUDAwMBAQEBAQEDAwMFBQUHBwcJCQkLS0tNTU1PT09RUVFTU1NVVVVXV1dZWVlbW1tdXV1fX19hoaGjo6OlpaWnp6epqamrq6utra2vr6+xsbGzs7O19fX39/f5+fn7+/v9/f3////"),
        new("Black & Red", "/wAA9QAA6wAA4QAA1wAAzAAAwgAAuAAArgAApAAAmgAAkAAAhgAAewAAcQAAZwAAXQAAUwAASQAAPwAANQAAKwAAIAAAFgAADAAAAgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgAADAAAFgAAIAAAKgAANQAAPwAASQAAUwAAXQAAZwAAcQAAewAAhgAAkAAAmgAApAAArgAAuAAAwgAAzAAA1wAA4QAA6wAA9QAA/wAA"),
        new("Black & Blue", "AAD/AAD1AADrAADhAADXAADMAADCAAC4AACuAACkAACaAACQAACGAAB7AABxAABnAABdAABTAABJAAA/AAA1AAArAAAgAAAWAAAMAAACAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAAAMAAAWAAAgAAAqAAA1AAA/AABJAABTAABdAABnAABxAAB7AACGAACQAACaAACkAACuAAC4AADCAADMAADXAADhAADrAAD1AAD/"),
        new("Black & Yellow", "//8A9fUA6+sA4eEA19cAzMwAwsIAuLgArq4ApKQAmpoAkJAAhoYAe3sAcXEAZ2cAXV0AU1MASUkAPz8ANTUAKysAICAAFhYADAwAAgIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgIADAwAFhYAICAAKioANTUAPz8ASUkAU1MAXV0AZ2cAcXEAe3sAhoYAkJAAmpoApKQArq4AuLgAwsIAzMwA19cA4eEA6+sA9fUA//8A"),
        new("Black & Green", "AP8AAPUAAOsAAOEAANcAAMwAAMIAALgAAK4AAKQAAJoAAJAAAIYAAHsAAHEAAGcAAF0AAFMAAEkAAD8AADUAACsAACAAABYAAAwAAAIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIAAAwAABYAACAAACoAADUAAD8AAEkAAFMAAF0AAGcAAHEAAHsAAIYAAJAAAJoAAKQAAK4AALgAAMIAAMwAANcAAOEAAOsAAPUAAP8A"),
        new("Black & Pink", "/wD/9QD16wDr4QDh1wDXzADMwgDCuAC4rgCupACkmgCakACQhgCGewB7cQBxZwBnXQBdUwBTSQBJPwA/NQA1KwArIAAgFgAWDAAMAgACAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgACDAAMFgAWIAAgKgAqNQA1PwA/SQBJUwBTXQBdZwBncQBxewB7hgCGkACQmgCapACkrgCuuAC4wgDCzADM1wDX4QDh6wDr9QD1/wD/"),
        new("Black & Cyan", "AP//APX1AOvrAOHhANfXAMzMAMLCALi4AK6uAKSkAJqaAJCQAIaGAHt7AHFxAGdnAF1dAFNTAElJAD8/ADU1ACsrACAgABYWAAwMAAICAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAICAAwMABYWACAgACoqADU1AD8/AElJAFNTAF1dAGdnAHFxAHt7AIaGAJCQAJqaAKSkAK6uALi4AMLCAMzMANfXAOHhAOvrAPX1AP//"),
        new("Cherry Blossom", "7s7/7Mj36sPv573n5bje47LW4azO3qbG3KC+2pq32pW13pG84YzD5YjK6ITR7H/Y8Hvg83bm93Lt+m30/Wn5/Wf3+Wbt9GXj72Ta6mPQ5mLH4mK+3mG02mCq1l+h0l6Yzl2Oyl2GymCEzmaI0WyM1HOP13mT2n6X34Sb4oqf5ZCi6Jam65up76Gt8qex9a60+bS4/Lq8/r/A/cHF+8LK+sPQ+cXV98bb9sfg9Mjm88rs8svy8Mz378387s7/7s7/"),
        new("Golden", "/5IA/5QE/5YI/5kL/5sP/50T/58X/6Eb/6Mf/6Yj/6gn/6or/6wv/68z/7E2/7M6/7Y+/7hC/7pG/71J/79N/8FR/8NV/8VZ/8dd/8ph/8xl/85p/9Jz/9mJ/+Cl/+a1/+uu/+2c/++L/+2D/+p+/+Z5/+N0/+Bw/9xr/9lm/9Vh/9Jc/89X/8tS/8hN/8VI/8FE/74//7s6/7c1/7Qx/7As/60n/6oi/6Yd/6MY/58T/5wO/5kK/5UF/5IA/5IA"),
        new("Pastel Rainbow", "/7y8/8K8/8i8/868/9S8/9q8/+G8/+e8/+28//O8//m8/v68+f+88/+87f+86P+84f+82/+81f+8z/+8yf+8w/+8vf+8vP/BvP/HvP/NvP/TvP/avP/gvP/mvP/svP/yvP/4vP//vPn/vPP/vOz/vOX/vN//vNj/vNL/vMz/vMX/vL//v7z/xrz/zLz/0rz/2rz/4Lz/5rz/7bz/87z/+rz//7z+/7z4/7zx/7zr/7zk/7ze/7zX/7zR/7zK/7y8"),
        new("Dark Rainbow", "MgAAMgUAMgkAMg4AMhIAMhcAMhsAMiAAMiUAMioAMi4AMTIALTIAKDIAJDIAHzIAGjIAFTIAETIADDIABzIAAzIAADICADIGADILADIQADIUADIZADIeADIiADInADIrADIwAC8yACsyACYyACEyABwyABgyABMyAA0yAAkyAAQyAQEyBQAyCgAyDwAyEwAyGQAyHgAyIgAyJwAyLAAyMQAyMgAvMgAqMgAlMgAgMgAbMgAWMgASMgANMgAAMgAA"),
        new("Non-binary", "//Qz//VK//Zg//h3//mO//qk//u7//3S//7o////9O366dr13sjv07Xqx6PlvJDgsX7apmvVm1nQik+5eUWiZzuLVjF0RShcNB5FIhQuEQoXAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEQoXIhQuNB5FRShcVjF0ZzuLeUWiik+5m1nQpmvVsX7avJDgx6Pl07Xq3sjv6dr19O36//////7o//3S//u7//qk//mO//h3//Zg//VK//Qz")
    ];

    private static readonly NameEffect[] CustomEffects =
    [
        new("None", "None", "Static", new Vector4(0.94f, 0.98f, 0.96f, 1f), new Vector4(0.94f, 0.98f, 0.96f, 1f), new Vector4(0.94f, 0.98f, 0.96f, 1f), 0f, 0f),
        new("Wave.Aurora", "Aurora Wave", "Wave", new Vector4(0.28f, 1.00f, 0.76f, 1f), new Vector4(0.32f, 0.62f, 1.00f, 1f), new Vector4(0.88f, 0.42f, 1.00f, 1f), 2.1f, 0.46f),
        new("Wave.Casino", "Casino Chase", "Wave", new Vector4(1.00f, 0.84f, 0.24f, 1f), new Vector4(0.95f, 0.12f, 0.16f, 1f), new Vector4(0.18f, 0.84f, 0.36f, 1f), 2.4f, 0.54f),
        new("Wave.Frost", "Frost Wave", "Wave", new Vector4(0.78f, 0.98f, 1.00f, 1f), new Vector4(0.32f, 0.70f, 1.00f, 1f), new Vector4(0.94f, 1.00f, 1.00f, 1f), 1.8f, 0.42f),
        new("Wave.Ember", "Ember Flow", "Wave", new Vector4(1.00f, 0.36f, 0.10f, 1f), new Vector4(1.00f, 0.82f, 0.28f, 1f), new Vector4(0.92f, 0.12f, 0.06f, 1f), 2.0f, 0.48f),
        new("Pulse.Heart", "Soft Pulse", "Pulse", new Vector4(1.00f, 0.54f, 0.82f, 1f), new Vector4(1.00f, 0.86f, 0.94f, 1f), new Vector4(1.00f, 0.54f, 0.82f, 1f), 3.0f, 0.22f),
        new("Pulse.Void", "Void Pulse", "Pulse", new Vector4(0.62f, 0.34f, 1.00f, 1f), new Vector4(0.95f, 0.86f, 1.00f, 1f), new Vector4(0.26f, 0.10f, 0.62f, 1f), 2.2f, 0.28f),
        new("Pulse.Gold", "Gold Pulse", "Pulse", new Vector4(1.00f, 0.74f, 0.20f, 1f), new Vector4(1.00f, 0.96f, 0.60f, 1f), new Vector4(0.92f, 0.48f, 0.08f, 1f), 2.7f, 0.24f),
        new("Static.Crimson", "Crimson Glow", "Static", new Vector4(1.00f, 0.28f, 0.28f, 1f), new Vector4(1.00f, 0.66f, 0.50f, 1f), new Vector4(1.00f, 0.28f, 0.28f, 1f), 0f, 0f),
        new("Static.Sapphire", "Sapphire Glow", "Static", new Vector4(0.34f, 0.68f, 1.00f, 1f), new Vector4(0.74f, 0.90f, 1.00f, 1f), new Vector4(0.34f, 0.68f, 1.00f, 1f), 0f, 0f),
        new("Static.Emerald", "Emerald Glow", "Static", new Vector4(0.28f, 1.00f, 0.62f, 1f), new Vector4(0.78f, 1.00f, 0.84f, 1f), new Vector4(0.28f, 1.00f, 0.62f, 1f), 0f, 0f),
    ];

    private static readonly Dictionary<int, Vector4[]> ColourCache = new();
    private static readonly DateTime StartTime = DateTime.UtcNow;

    public static string Normalize(string? effect)
    {
        var value = (effect ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(value)) return "None";
        foreach (var e in CustomEffects)
            if (string.Equals(e.Id, value, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Label, value, StringComparison.OrdinalIgnoreCase))
                return e.Id;
        if (TryParseHonorificId(value, out var tab, out var index) && index >= 0 && index < HonourificColourSets.Length)
            return MakeHonorificId(tab, index);
        for (var i = 0; i < HonourificColourSets.Length; i++)
            if (value.Equals(HonourificColourSets[i].Name, StringComparison.OrdinalIgnoreCase))
                return MakeHonorificId("Static", i);
        return "None";
    }

    public static NameEffect Resolve(string? effect)
    {
        var id = Normalize(effect);
        foreach (var e in CustomEffects)
            if (string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
                return e;
        if (TryParseHonorificId(id, out var tab, out var index) && index >= 0 && index < HonourificColourSets.Length)
        {
            var preview = GetGradientColor(index, tab, 0, 8);
            return new NameEffect(id, HonourificColourSets[index].Name, tab, preview, preview, preview, 1f, 1f, index);
        }
        return CustomEffects[0];
    }

    public static Vector4 CurrentColor(string? effect, Vector4 fallback, int index = 0)
    {
        var e = Resolve(effect);
        if (e.Id == "None") return fallback;
        if (e.ColourSetIndex >= 0) return GetGradientColor(e.ColourSetIndex, e.Tab, index, 5);
        if (e.Tab == "Static") return e.Primary;
        var t = (float)ImGui.GetTime();
        if (e.Tab == "Pulse")
        {
            var pulse = (MathF.Sin(t * e.Speed) + 1f) * 0.5f;
            return Lerp(e.Primary, e.Secondary, pulse);
        }

        var phase = index * MathF.Max(0.38f, e.Spread * 1.8f) - t * e.Speed;
        var wave = (MathF.Sin(phase) + 1f) * 0.5f;
        if (wave < 0.5f) return Lerp(e.Primary, e.Secondary, wave * 2f);
        return Lerp(e.Secondary, e.Third, (wave - 0.5f) * 2f);
    }

    public static void DrawEffectText(ImDrawListPtr drawList, Vector2 pos, string text, string? effect, Vector4 fallbackColor, float fontSize, float scale)
    {
        if (string.IsNullOrEmpty(text)) return;
        var id = Normalize(effect);
        if (id == "None")
        {
            DrawTextWithShadow(drawList, pos, ImGui.GetColorU32(fallbackColor), text, fontSize, scale);
            return;
        }
        var cursor = pos;
        var baseFont = MathF.Max(1f, ImGui.GetFontSize());
        var shadow = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.68f));
        var glow = CurrentColor(id, fallbackColor, 2);
        var glowU32 = ImGui.GetColorU32(new Vector4(glow.X, glow.Y, glow.Z, 0.18f));
        var font = ImGui.GetFont();
        drawList.AddText(font, fontSize, cursor + new Vector2(0f, 1.5f * scale), glowU32, text);
        drawList.AddText(font, fontSize, cursor + new Vector2(1.2f * scale, 0f), glowU32, text);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i].ToString();
            var color = CurrentColor(id, fallbackColor, i);
            var u32 = ImGui.GetColorU32(color);
            drawList.AddText(font, fontSize, cursor + new Vector2(1f, 1f) * scale, shadow, ch);
            drawList.AddText(font, fontSize, cursor, u32, ch);
            cursor.X += ImGui.CalcTextSize(ch).X * (fontSize / baseFont);
        }
    }

    public static bool DrawPickerPopup(string popupId, ref string selectedEffect)
    {
        var changed = false;
        if (!ImGui.BeginPopup(popupId)) return false;
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.030f, 0.034f, 0.044f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.12f, 0.20f, 0.24f, 0.70f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.18f, 0.35f, 0.34f, 0.82f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.20f, 0.55f, 0.46f, 0.90f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.20f, 0.90f, 0.72f, 0.20f));
        ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.055f, 0.090f, 0.110f, 0.94f));
        ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.160f, 0.360f, 0.330f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.105f, 0.270f, 0.245f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.020f, 0.024f, 0.032f, 0.78f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 10f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(7f, 5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.SetNextWindowSize(new Vector2(310f, 330f), ImGuiCond.Appearing);
        if (ImGui.BeginTabBar($"{popupId}_tabs"))
        {
            changed |= DrawTab("Wave", ref selectedEffect);
            changed |= DrawTab("Pulse", ref selectedEffect);
            changed |= DrawTab("Static", ref selectedEffect);
            ImGui.EndTabBar();
        }
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(9);
        ImGui.EndPopup();
        return changed;
    }

    private static bool DrawTab(string tab, ref string selectedEffect)
    {
        var changed = false;
        if (!ImGui.BeginTabItem(tab)) return false;
        var width = MathF.Max(260f, ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginChild($"effect_scroll_{tab}", new Vector2(width, 260f), true))
        {
            foreach (var effect in CustomEffects)
            {
                if (!string.Equals(effect.Tab, tab, StringComparison.OrdinalIgnoreCase)) continue;
                changed |= DrawEffectOption(effect.Id, effect.Label, tab, ref selectedEffect, width);
            }
            for (var i = 0; i < HonourificColourSets.Length; i++)
            {
                var id = MakeHonorificId(tab, i);
                changed |= DrawEffectOption(id, HonourificColourSets[i].Name, tab, ref selectedEffect, width);
            }
        }
        ImGui.EndChild();
        ImGui.EndTabItem();
        return changed;
    }

    private static bool DrawEffectOption(string id, string label, string tab, ref string selectedEffect, float width)
    {
        var selected = string.Equals(Normalize(selectedEffect), id, StringComparison.OrdinalIgnoreCase);
        var start = ImGui.GetCursorScreenPos();
        var height = 24f;
        if (ImGui.Selectable($"##effect_{id}", selected, ImGuiSelectableFlags.None, new Vector2(width - 8f, height)))
        {
            selectedEffect = id;
            return true;
        }
        var drawList = ImGui.GetWindowDrawList();
        DrawEffectText(drawList, start + new Vector2(8f, 4f), label, id, new Vector4(0.94f, 0.98f, 0.96f, 1f), ImGui.GetFontSize(), 1f);
        return false;
    }

    private static Vector4 GetGradientColor(int setIndex, string tab, int chrIndex, int throttle)
    {
        var colours = GetColours(setIndex);
        if (colours.Length == 0) return Vector4.One;
        if (throttle < 1) throttle = 1;
        var elapsed = (long)(DateTime.UtcNow - StartTime).TotalMilliseconds;
        var offset = tab.Equals("Static", StringComparison.OrdinalIgnoreCase) ? 0 : elapsed / 15;
        var multiplier = tab.Equals("Pulse", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        var i = (int)((offset / throttle + chrIndex * multiplier) % colours.Length);
        if (i < 0) i += colours.Length;
        return colours[i];
    }

    private static Vector4[] GetColours(int setIndex)
    {
        if (ColourCache.TryGetValue(setIndex, out var cached)) return cached;
        try
        {
            var data = Convert.FromBase64String(HonourificColourSets[setIndex].Base64);
            var count = data.Length / 3;
            var colours = new Vector4[count];
            for (var i = 0; i < count; i++)
                colours[i] = new Vector4(data[i * 3] / 255f, data[i * 3 + 1] / 255f, data[i * 3 + 2] / 255f, 1f);
            ColourCache[setIndex] = colours;
            return colours;
        }
        catch
        {
            ColourCache[setIndex] = [];
            return [];
        }
    }

    private static string MakeHonorificId(string tab, int index) => $"Honorific.{tab}.{index}";

    private static bool TryParseHonorificId(string value, out string tab, out int index)
    {
        tab = "Static";
        index = -1;
        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 3 && parts[0].Equals("Honorific", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[2], out index))
        {
            tab = NormalizeTab(parts[1]);
            return true;
        }
        return false;
    }

    private static string NormalizeTab(string tab)
        => tab.Equals("Wave", StringComparison.OrdinalIgnoreCase) ? "Wave" : tab.Equals("Pulse", StringComparison.OrdinalIgnoreCase) ? "Pulse" : "Static";

    private static void DrawTextWithShadow(ImDrawListPtr drawList, Vector2 pos, uint color, string text, float fontSize, float scale)
    {
        var shadow = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.62f));
        drawList.AddText(pos + new Vector2(1f, 1f) * scale, shadow, text);
        drawList.AddText(pos, color, text);
    }

    private static Vector4 Lerp(Vector4 a, Vector4 b, float t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, 1f);
}
