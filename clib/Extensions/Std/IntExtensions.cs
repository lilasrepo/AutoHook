using clib.Services;
using System.Globalization;
using System.Text;

namespace clib.Extensions;

public static class IntExtensions {
    extension(int i) {
        public Vector2 Vec2() => new(i);
        public Vector3 Vec3() => new(i);
        public Vector4 Vec4() => new(i);
        public int Hex() => int.Parse(i.ToString("X"), NumberStyles.HexNumber);

        public float Scaled() => i * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale * (Svc.Interface.UiBuilder.DefaultFontSpec.SizePt / 12f);

        public string ToRomanNumeral() {
            var sb = new StringBuilder(15);
            foreach (var (value, symbol) in RomanNumeralTable) {
                while (i >= value) {
                    sb.Append(symbol);
                    i -= value;
                }
            }
            return sb.ToString();
        }
    }

    private static readonly (int Value, string Symbol)[] RomanNumeralTable =
        [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
}
