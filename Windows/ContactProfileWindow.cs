using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Privacy.Models;
using Privacy.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Privacy.Windows;

internal sealed class ContactProfileWindow : Window
{
    private static readonly string[] SupportedSymbols = BuildSupportedSymbols();

    private static readonly string[] RelationshipModes =
    {
        "", "Trusted", "Important", "Avoid"
    };

    private readonly Configuration config;
    private readonly IPluginLog log;
    private PrivateContact? contact;
    private string nickname = string.Empty;
    private string symbol = string.Empty;
    private string symbolColorHex = "#2BE5B5";
    private Vector4 symbolPickerColor = UiColors.HexToRgba("#2BE5B5");
    private int symbolIndex;
    private string mainJob = string.Empty;
    private string role = string.Empty;
    private string nameday = string.Empty;
    private string preferredContent = string.Empty;
    private string profileBio = string.Empty;
    private int relationshipIndex;
    private int pushedColorCount;
    private int pushedStyleVarCount;

    public ContactProfileWindow(Configuration config, IPluginLog log)
        : base("Privacy Profile###PrivacyProfile")
    {
        this.config = config;
        this.log = log;

        Size = new Vector2(473f, 500f);
        SizeCondition = ImGuiCond.FirstUseEver;

        WindowBuilder.For(this)
            .AllowPinning(true)
            .AllowClickthrough(false)
            .SetSizeConstraints(new Vector2(420f, 360f), new Vector2(760f, 820f))
            .AddFlags(ImGuiWindowFlags.NoDocking)
            .Apply();
    }

    public void Open(PrivateContact selected)
    {
        contact = selected;
        nickname = selected.Nickname ?? string.Empty;
        symbol = selected.ContactSymbol ?? string.Empty;
        symbolColorHex = NormalizeHex(selected.ContactSymbolColorHex, "#2BE5B5");
        symbolPickerColor = UiColors.HexToRgba(symbolColorHex);
        symbolIndex = Math.Max(0, Array.FindIndex(SupportedSymbols, s => s == symbol));
        if (symbolIndex < 0) symbolIndex = 0;
        mainJob = selected.MainJob ?? string.Empty;
        role = selected.Role ?? string.Empty;
        nameday = selected.Nameday ?? string.Empty;
        preferredContent = selected.PreferredContent ?? string.Empty;
        profileBio = selected.ProfileBio ?? string.Empty;
        relationshipIndex = Math.Max(0, Array.FindIndex(RelationshipModes, s => string.Equals(s, selected.RelationshipStatus ?? string.Empty, StringComparison.OrdinalIgnoreCase)));
        if (relationshipIndex < 0) relationshipIndex = 0;
        WindowName = $"Profile - {selected.DisplayName}###PrivacyProfile";
        IsOpen = true;
        log.Information("Privacy: opening profile window for {Name}@{World}.", selected.Name, selected.World);
    }

    public override void PreDraw()
    {
        pushedColorCount = 0;
        pushedStyleVarCount = 0;

        PushColor(ImGuiCol.Text, UiColors.Text);
        PushColor(ImGuiCol.TextDisabled, UiColors.TextDim);
        PushColor(ImGuiCol.WindowBg, Vector4.Zero);
        PushColor(ImGuiCol.ChildBg, Vector4.Zero);
        PushColor(ImGuiCol.PopupBg, UiColors.Get("PrivatePopupBg"));
        PushColor(ImGuiCol.Border, Vector4.Zero);
        PushColor(ImGuiCol.FrameBg, UiColors.Get("PrivateFrameBg"));
        PushColor(ImGuiCol.FrameBgHovered, UiColors.Get("PrivateFrameBgHovered"));
        PushColor(ImGuiCol.FrameBgActive, UiColors.Get("PrivateFrameBgActive"));
        PushColor(ImGuiCol.TitleBg, UiColors.Get("PrivateTitleBg"));
        PushColor(ImGuiCol.TitleBgActive, UiColors.Get("PrivateTitleBgActive"));
        PushColor(ImGuiCol.Button, UiColors.Get("ButtonDefault"));
        PushColor(ImGuiCol.ButtonHovered, config.AccentColor);
        PushColor(ImGuiCol.ButtonActive, UiColors.Get("LightlessPurpleActive"));
        PushColor(ImGuiCol.CheckMark, config.AccentColor);
        PushColor(ImGuiCol.Separator, new Vector4(1f, 1f, 1f, 0.12f));
        PushColor(ImGuiCol.ScrollbarBg, UiColors.WithAlpha(config.WindowBackgroundColor, 0.16f));
        PushColor(ImGuiCol.ScrollbarGrab, UiColors.WithAlpha(config.AccentColor, 0.55f));
        PushColor(ImGuiCol.ScrollbarGrabHovered, UiColors.WithAlpha(config.AccentColor, 0.72f));
        PushColor(ImGuiCol.ScrollbarGrabActive, UiColors.WithAlpha(config.AccentColor, 0.92f));

        PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 0f) * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 4f) * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f, 6f) * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.WindowRounding, 7f * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.ChildRounding, 4f * ImGuiHelpers.GlobalScale);
        PushStyleVar(ImGuiStyleVar.FrameRounding, 4f * ImGuiHelpers.GlobalScale);
    }

    public override void PostDraw()
    {
        if (pushedStyleVarCount > 0) ImGui.PopStyleVar(pushedStyleVarCount);
        if (pushedColorCount > 0) ImGui.PopStyleColor(pushedColorCount);
    }

    public override void Draw()
    {
        
        WindowEdgeFade.DrawUnified(config.WindowBackgroundColor, config.AccentColor);
var drawList = ImGui.GetWindowDrawList();
        var headerPos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var headerHeight = 58f * ImGuiHelpers.GlobalScale;

        var headerTop = new Vector4(
            MathF.Min(1f, config.WindowBackgroundColor.X + 0.028f),
            MathF.Min(1f, config.WindowBackgroundColor.Y + 0.028f),
            MathF.Min(1f, config.WindowBackgroundColor.Z + 0.028f),
            MathF.Min(1f, MathF.Max(0.38f, config.WindowBackgroundColor.W)));
        var headerBottom = new Vector4(config.WindowBackgroundColor.X, config.WindowBackgroundColor.Y, config.WindowBackgroundColor.Z, 0f);

        drawList.AddRectFilledMultiColor(
            headerPos,
            headerPos + new Vector2(width, headerHeight),
            ImGui.GetColorU32(headerTop),
            ImGui.GetColorU32(headerTop),
            ImGui.GetColorU32(headerBottom),
            ImGui.GetColorU32(headerBottom));

        var title = contact == null ? "Contact Profile" : $"Profile - {contact.DisplayName}";
        DrawTextWithShadow(drawList, headerPos + new Vector2(20f, 13f) * ImGuiHelpers.GlobalScale, ImGui.GetColorU32(config.AccentColor), title, 20f * ImGuiHelpers.GlobalScale);
        ImGui.Dummy(new Vector2(width, headerHeight));

        using var child = ImRaii.Child("profile-body", new Vector2(-1f, -1f), false);
        if (!child) return;

        if (contact == null)
        {
            ImGui.TextUnformatted("No contact selected.");
            return;
        }

        ImGui.TextColored(config.AccentColor, $"{contact.Name}@{contact.World}");
        ImGui.TextDisabled("Private profile fields only affect the plugin display.");
        ImGui.Separator();

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##nickname", "Nickname shown in the list", ref nickname, 64);

        DrawSymbolSelector();
        ImGui.SameLine();
        DrawSymbolColorPicker();
        ImGui.TextDisabled("Symbols are limited to game-supported characters.");

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##mainjob", "Main Job", ref mainJob, 48);
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##role", "Role / relationship note", ref role, 48);
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##nameday", "Birthday / Nameday", ref nameday, 48);
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##content", "Preferred content", ref preferredContent, 96);

        ImGui.SetNextItemWidth(190f * ImGuiHelpers.GlobalScale);
        ImGui.Combo("Private status", ref relationshipIndex, RelationshipModes, RelationshipModes.Length);
        if (RelationshipModes[Math.Clamp(relationshipIndex, 0, RelationshipModes.Length - 1)] is "Avoid")
            ImGui.TextColored(new Vector4(1f, 0.48f, 0.42f, 1f), "Avoid contacts are sorted to the bottom and action buttons are hidden.");

        ImGui.TextColored(config.AccentColor, "Mini bio");
        ImGui.InputTextMultiline("##bio", ref profileBio, 2048, new Vector2(-1f, 82f * ImGuiHelpers.GlobalScale));

        if (ImGui.Button("Save", new Vector2(100f, 0f) * ImGuiHelpers.GlobalScale))
        {
            Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear Profile", new Vector2(120f, 0f) * ImGuiHelpers.GlobalScale))
        {
            nickname = symbol = mainJob = role = nameday = preferredContent = profileBio = string.Empty;
            symbolColorHex = "#2BE5B5";
            symbolPickerColor = UiColors.HexToRgba(symbolColorHex);
            symbolIndex = 0;
            relationshipIndex = 0;
            Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Close", new Vector2(100f, 0f) * ImGuiHelpers.GlobalScale))
            IsOpen = false;

        ImGui.Separator();
        DrawRecentHistory();
    }

    private void DrawRecentHistory()
    {
        if (contact == null) return;

        ImGui.TextColored(config.AccentColor, "Status history");
        var events = config.History
            .Where(e => string.Equals(e.ContactId, contact.Id, StringComparison.Ordinal))
            .OrderByDescending(e => e.Timestamp)
            .Take(8)
            .ToList();

        if (events.Count == 0)
        {
            ImGui.TextDisabled("No status history yet.");
            return;
        }

        foreach (var item in events)
        {
            var time = item.Timestamp.ToLocalTime().ToString("MM/dd HH:mm");
            ImGui.TextDisabled($"{time}  {item.Message}");
        }

        if (ImGui.Button("Clear History", new Vector2(120f, 0f) * ImGuiHelpers.GlobalScale))
        {
            config.History.RemoveAll(e => string.Equals(e.ContactId, contact.Id, StringComparison.Ordinal));
            config.Save();
            log.Information("Privacy: cleared status history for {Name}@{World}.", contact.Name, contact.World);
        }
    }

    private void Save()
    {
        if (contact == null) return;

        contact.Nickname = nickname.Trim();
        contact.ContactSymbol = symbol.Trim();
        contact.ContactSymbolColorHex = NormalizeHex(symbolColorHex, "#2BE5B5");
        contact.MainJob = mainJob.Trim();
        contact.Role = role.Trim();
        contact.Nameday = nameday.Trim();
        contact.PreferredContent = preferredContent.Trim();
        contact.ProfileBio = profileBio.Trim();
        contact.RelationshipStatus = RelationshipModes[Math.Clamp(relationshipIndex, 0, RelationshipModes.Length - 1)];
        config.Save();
        log.Information("Privacy: saved profile for {Name}@{World}; nickname={Nickname}; symbol={Symbol}; status={Status}.", contact.Name, contact.World, contact.Nickname, contact.ContactSymbol, contact.RelationshipStatus);
    }


    private void DrawSymbolSelector()
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetNextItemWidth(192f * scale);
        ImGui.SetNextWindowSize(new Vector2(232f, 220f) * scale, ImGuiCond.Always);

        var preview = string.IsNullOrWhiteSpace(symbol) ? "None" : symbol;
        if (!ImGui.BeginCombo("Symbol", preview, ImGuiComboFlags.HeightLargest))
            return;

        var buttonSize = new Vector2(43f, 25f) * scale;
        var buttonSpacing = new Vector2(4f, 4f) * scale;

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, buttonSpacing))
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(3f, 2f) * scale))
        {
            if (ImGui.BeginTable("symbol-grid-table", 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings))
            {
                for (var i = 0; i < SupportedSymbols.Length; i++)
                {
                    ImGui.TableNextColumn();
                    using var id = ImRaii.PushId(i);

                    var value = SupportedSymbols[i];
                    var label = string.IsNullOrWhiteSpace(value) ? "None" : value;
                    var selected = i == symbolIndex;
                    using var selectedColor = ImRaii.PushColor(ImGuiCol.Button, selected ? UiColors.WithAlpha(config.AccentColor, 0.30f) : UiColors.Get("ButtonDefault"));
                    using var hoveredColor = ImRaii.PushColor(ImGuiCol.ButtonHovered, UiColors.WithAlpha(config.AccentColor, 0.24f));
                    using var activeColor = ImRaii.PushColor(ImGuiCol.ButtonActive, UiColors.WithAlpha(config.AccentColor, 0.36f));

                    if (ImGui.Button(label, buttonSize))
                    {
                        symbolIndex = i;
                        symbol = value;
                        ImGui.CloseCurrentPopup();
                    }
                }

                ImGui.EndTable();
            }
        }

        ImGui.EndCombo();
    }

    private void DrawSymbolColorPicker()
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Color");
        ImGui.SameLine();
        DrawColorSquare("symbol_color", ref symbolPickerColor, ref symbolColorHex);
    }

    private bool DrawColorSquare(string id, ref Vector4 color, ref string hexBuffer)
    {
        var changed = false;
        var size = new Vector2(24f, 24f) * ImGuiHelpers.GlobalScale;
        var flags = ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop;
        if (ImGui.ColorButton($"##{id}_button", color, flags, size))
            ImGui.OpenPopup($"{id}_picker");

        if (ImGui.BeginPopup($"{id}_picker"))
        {
            var picker = new Vector3(color.X, color.Y, color.Z);
            if (ImGui.ColorPicker3($"##{id}_picker_value", ref picker, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoLabel))
            {
                color = new Vector4(picker.X, picker.Y, picker.Z, 1f);
                hexBuffer = HexFromColor(color);
                changed = true;
            }
            ImGui.EndPopup();
        }

        return changed;
    }

    private static string[] BuildSupportedSymbols()
    {
        var symbols = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string symbol)
        {
            if ((symbol.Length == 0 || !string.IsNullOrWhiteSpace(symbol)) && seen.Add(symbol))
                symbols.Add(symbol);
        }

        void AddTextSymbols(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text, i))
                    continue;

                var codepoint = char.ConvertToUtf32(text, i);
                if (char.IsHighSurrogate(text[i]))
                    i++;

                Add(char.ConvertFromUtf32(codepoint));
            }
        }

        Add("");
        AddTextSymbols("★☆♠♡♢♣♤♥♦♧♪♭♯°。・○◎●□■△▼◆◇☀☁☂☃←↑→↓⇔⇒©®™§¶$€¥£¢∀∂∃⊇⊂≠≡≦∽∫∥∙∋+-=〒⊥∟⓪①②③④⑤⑥⑦⑧⑨⑩✓♀†");

        return symbols.ToArray();
    }

    private static string HexFromColor(Vector4 color)
    {
        var r = Math.Clamp((int)MathF.Round(color.X * 255f), 0, 255);
        var g = Math.Clamp((int)MathF.Round(color.Y * 255f), 0, 255);
        var b = Math.Clamp((int)MathF.Round(color.Z * 255f), 0, 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static string NormalizeHex(string? value, string fallback)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.StartsWith('#'))
            text = text[1..];

        if (text.Length != 6 || text.Any(ch => !Uri.IsHexDigit(ch)))
            return fallback;

        return "#" + text.ToUpperInvariant();
    }

    private void PushColor(ImGuiCol target, Vector4 color)
    {
        ImGui.PushStyleColor(target, color);
        pushedColorCount++;
    }

    private void PushStyleVar(ImGuiStyleVar target, Vector2 value)
    {
        ImGui.PushStyleVar(target, value);
        pushedStyleVarCount++;
    }

    private void PushStyleVar(ImGuiStyleVar target, float value)
    {
        ImGui.PushStyleVar(target, value);
        pushedStyleVarCount++;
    }

    private static void DrawTextWithShadow(ImDrawListPtr drawList, Vector2 pos, uint color, string text, float size)
    {
        var font = ImGui.GetFont();
        var shadow = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.78f));
        drawList.AddText(font, size, pos + new Vector2(1.2f, 1.2f), shadow, text);
        drawList.AddText(font, size, pos, color, text);
    }
}
