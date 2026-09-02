using clib.Services;

namespace clib.Extensions;

public static class FloatExtensions {
    extension(float f) {
        public float Scaled() => f * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale * (Svc.Interface.UiBuilder.DefaultFontSpec.SizePt / 12f);
        public float Clamp01() => Math.Clamp(f, 0f, 1f);
        public float LerpTo(float other, float fraction) => f + (other - f) * fraction;
    }
}
