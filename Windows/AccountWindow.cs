using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Privacy.UI;
using System.Numerics;

namespace Privacy.Windows;

internal sealed class AccountWindow : Window
{
    private readonly LoginWindow loginWindow;

    public AccountWindow(LoginWindow loginWindow)
        : base("Privacy Account###PrivacyAccount")
    {
        this.loginWindow = loginWindow;
        Size = new Vector2(360f, 120f);
        SizeCondition = ImGuiCond.Always;

        WindowBuilder.For(this)
            .AllowPinning(true)
            .AllowClickthrough(false)
            .SetSizeConstraints(new Vector2(320f, 110f), new Vector2(420f, 180f))
            .AddFlags(ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.AlwaysAutoResize)
            .Apply();
    }

    public override void Draw()
    {
        ImGui.TextWrapped("Account login was moved to the Log In/Log Off window.");
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, UiColors.Get("LightlessPurpleHover")))
        {
            if (ThemedWidgets.Button("Open Log In/Log Off", new Vector2(160f, 0f) * ImGuiHelpers.GlobalScale, UiColors.Accent))
            {
                loginWindow.IsOpen = true;
                IsOpen = false;
            }
        }
    }
}
