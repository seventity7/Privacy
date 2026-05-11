
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Privacy.Services;
using Privacy.UI;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace Privacy.Windows;

internal sealed class LoginWindow : Window
{
    private readonly Configuration config;
    private readonly PrivacyCloudService cloudService;

    private string username = string.Empty;
    private string password = string.Empty;
    private string discordCode = string.Empty;
    private string statusMessage = string.Empty;
    private bool busy;
    private bool showPassword;
    private int pushedColorCount;
    private int pushedStyleVarCount;

    public LoginWindow(Configuration config, PrivacyCloudService cloudService)
        : base("Privacy Log In###PrivacyLogin")
    {
        this.config = config;
        this.cloudService = cloudService;
        SizeCondition = ImGuiCond.Always;

        WindowBuilder.For(this)
            .AllowPinning(true)
            .AllowClickthrough(false)
            .SetSizeConstraints(new Vector2(420f, 190f), new Vector2(560f, 520f))
            .AddFlags(ImGuiWindowFlags.NoDocking)
            .Apply();
    }

    public override void PreDraw()
    {
        Size = cloudService.IsLoggedIn ? new Vector2(430f, 210f) : new Vector2(455f, 390f);
        pushedColorCount = 0;
        pushedStyleVarCount = 0;

        PushColor(ImGuiCol.Text, UiColors.Text);
        PushColor(ImGuiCol.TextDisabled, UiColors.TextDim);
        PushColor(ImGuiCol.WindowBg, Vector4.Zero);
        PushColor(ImGuiCol.ChildBg, Vector4.Zero);
        PushColor(ImGuiCol.Border, Vector4.Zero);
        PushColor(ImGuiCol.FrameBg, UiColors.Get("PrivateFrameBg"));
        PushColor(ImGuiCol.FrameBgHovered, UiColors.Get("PrivateFrameBgHovered"));
        PushColor(ImGuiCol.FrameBgActive, UiColors.Get("PrivateFrameBgActive"));
        PushColor(ImGuiCol.TitleBg, UiColors.Get("PrivateTitleBg"));
        PushColor(ImGuiCol.TitleBgActive, UiColors.Get("PrivateTitleBgActive"));
        PushColor(ImGuiCol.TitleBgCollapsed, UiColors.Get("PrivateTitleBgCollapsed"));
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
DrawHeader();

        using var body = ImRaii.Child("login-body", new Vector2(-1f, -1f), false);
        if (!body) return;

        if (cloudService.IsLoggedIn)
        {
            var character = string.IsNullOrWhiteSpace(config.CloudLinkedCharacterName)
                ? config.CloudDisplayName
                : config.CloudLinkedCharacterName;

            ImGui.TextWrapped($"You are logged in as: {character}");
            ImGui.Spacing();

            if (ImGui.Button("Log Off", new Vector2(120f, 0f) * ImGuiHelpers.GlobalScale))
            {
                cloudService.Logout();
                statusMessage = "Logged out.";
            }

            if (!string.IsNullOrWhiteSpace(statusMessage))
                ImGui.TextWrapped(statusMessage);

            return;
        }

        using (ImRaii.Disabled(busy || !cloudService.HasApiBaseUrl))
        {
            DrawEmailPassword();
            ImGui.Separator();
            DrawDiscordLogin();
        }

        if (busy)
            ImGui.TextDisabled("Working...");
    }

        private void DrawHeader()
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var scale = ImGuiHelpers.GlobalScale;
        var headerHeight = 58f * scale;
        var top = new Vector4(
            MathF.Min(1f, config.WindowBackgroundColor.X + 0.028f),
            MathF.Min(1f, config.WindowBackgroundColor.Y + 0.028f),
            MathF.Min(1f, config.WindowBackgroundColor.Z + 0.028f),
            MathF.Min(1f, MathF.Max(0.38f, config.WindowBackgroundColor.W)));
        var bottom = new Vector4(config.WindowBackgroundColor.X, config.WindowBackgroundColor.Y, config.WindowBackgroundColor.Z, 0f);

        drawList.AddRectFilledMultiColor(
            pos,
            pos + new Vector2(width, headerHeight),
            ImGui.GetColorU32(top),
            ImGui.GetColorU32(top),
            ImGui.GetColorU32(bottom),
            ImGui.GetColorU32(bottom));

        DrawTextWithShadow(drawList, pos + new Vector2(20f, 13f) * scale, ImGui.GetColorU32(config.AccentColor), "Log In / Log Off", 20f * scale);
        ImGui.Dummy(new Vector2(width, headerHeight));
    }

    private void DrawEmailPassword()
    {
        ImGui.TextColored(config.AccentColor, "Username / Password");
        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.74f, 0.42f, 1f), statusMessage);
        }

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##cloud_username", "Username", ref username, 32);

        ImGui.SetNextItemWidth(-1f);
        var flags = showPassword ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
        ImGui.InputTextWithHint("##cloud_password", "Password", ref password, 128, flags);
        ImGui.Checkbox("Show password", ref showPassword);

        if (ImGui.Button("Login", new Vector2(92f, 0f) * ImGuiHelpers.GlobalScale))
            RunCloudAction(() => cloudService.LoginWithUsernameAsync(username, password));

        ImGui.SameLine();
        if (ImGui.Button("Create account", new Vector2(138f, 0f) * ImGuiHelpers.GlobalScale))
            RunCloudAction(() => cloudService.RegisterWithUsernameAsync(username, password));
    }

    private void DrawDiscordLogin()
    {
        ImGui.TextColored(config.AccentColor, "Discord");
        if (ImGui.Button("Open Discord login", new Vector2(170f, 0f) * ImGuiHelpers.GlobalScale))
            statusMessage = cloudService.OpenDiscordLogin();

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##discord_code", "Paste Discord login code here", ref discordCode, 256);

        if (ImGui.Button("Complete Discord login", new Vector2(190f, 0f) * ImGuiHelpers.GlobalScale))
            RunCloudAction(() => cloudService.CompleteDiscordLoginAsync(discordCode));
    }

    private void RunCloudAction(Func<Task<string>> action)
    {
        if (busy) return;

        busy = true;
        statusMessage = string.Empty;

        try
        {
            var task = action();
            _ = CompleteCloudActionAsync(task);
        }
        catch (Exception ex)
        {
            statusMessage = $"Cloud action failed: {ex.Message}";
            busy = false;
        }
    }

    private async Task CompleteCloudActionAsync(Task<string> task)
    {
        try
        {
            statusMessage = await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            statusMessage = $"Cloud action failed: {ex.Message}";
        }
        finally
        {
            busy = false;
        }
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
