using Dalamud.Interface.Utility;

namespace AutoHook.Utils;

// porting-note(api13): newer ECommons exposes these as numeric extensions; the ECommons revision
// this tree pins only has the ButtonScaled/IconButtonScaled helpers. Same arithmetic, kept local
// so the vendored condition UI compiles without advancing the ECommons pin.
public static class ScaleExtensions
{
    public static float Scaled(this int value) => value * ImGuiHelpers.GlobalScale;

    public static float Scaled(this float value) => value * ImGuiHelpers.GlobalScale;
}
