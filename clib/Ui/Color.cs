using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using Lumina.Excel.Sheets;
using System.Buffers.Binary;

namespace clib.Ui;

// https://github.com/Haselnussbomber/HaselCommon/blob/79fb95330bed8e1c4ce47cb8f7130eeb6cdd01e3/HaselCommon/Graphics/Color.cs
// https://github.com/NightmareXIV/ECommons/blob/master/ECommons/ImGuiMethods/EzColor.cs
public struct Color(float r, float g, float b, float a = 1) {
    public float R = r;
    public float G = g;
    public float B = b;
    public float A = a;

    public readonly Vector4 Vector4 => this;

    public readonly Color LerpTo(Color other, float fraction) => Lerp(this, other, fraction);
    public static Color Lerp(Color a, Color b, float fraction) => new(
        a.R.LerpTo(b.R, fraction),
        a.G.LerpTo(b.G, fraction),
        a.B.LerpTo(b.B, fraction),
        a.A.LerpTo(b.A, fraction));

    public readonly Color GetGradient(Color end, int milliseconds = 1000) => GetGradient(this, end, milliseconds);
    public static Color GetGradient(Color start, Color end, int milliseconds = 1000) {
        var period = milliseconds * 2L;
        var elapsed = Environment.TickCount64 % period;
        var fraction = elapsed < milliseconds ? (float)elapsed / milliseconds : 1f - (float)(elapsed - milliseconds) / milliseconds;
        return start.LerpTo(end, fraction);
    }

    public static Color From(float r, float g, float b, float a = 1) => new(r, g, b, a);
    public static Color From(Vector3 colour, float alpha = 1) => new(colour.X, colour.Y, colour.Z, alpha);
    public static Color From(Vector4 colour) => new(colour.X, colour.Y, colour.Z, colour.W);
    public static Color From(ImGuiCol col) => FromRgba(ImGui.GetColorU32(col));
    public static Color From(ByteColor c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
    public static unsafe Color From(ByteColor* c) => From(*c);
    public static Color From(UIForegroundPayload p) => FromAbgr(p.RGBA);
    public static Color From(UIGlowPayload p) => FromAbgr(p.RGBA);
    public static Color From(UIColor row, bool glow = false) => glow ? FromAbgr(row.Light) : FromAbgr(row.Dark);

    public static Color FromVector4(Vector4 vec, ColorFormat format = ColorFormat.Rgba) => format switch {
        ColorFormat.Rgba => new(vec.X, vec.Y, vec.Z, vec.W),
        ColorFormat.Bgra => new(vec.Z, vec.Y, vec.X, vec.W),
        ColorFormat.Argb => new(vec.Y, vec.Z, vec.W, vec.X),
        ColorFormat.Abgr => new(vec.W, vec.Z, vec.Y, vec.X),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static Color FromUInt(uint value, ColorFormat format = ColorFormat.Rgba) => format switch {
        ColorFormat.Rgba => FromRgba(value),
        ColorFormat.Bgra => FromBgra(value),
        ColorFormat.Argb => FromArgb(value),
        ColorFormat.Abgr => FromAbgr(value),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static Color FromUiForeground(uint id) => IDataManager.Get().GetRow<UIColor>(id) is { } row ? FromAbgr(row.Dark) : Black;
    public static Color FromUiGlow(uint id) => IDataManager.Get().GetRow<UIColor>(id) is { } row ? FromAbgr(row.Light) : Black;
    public static Color FromStain(uint id) => IDataManager.Get().GetRow<Stain>(id) is { } row ? FromAbgr(BinaryPrimitives.ReverseEndianness(row.Color) >> 8) with { A = 1 } : Black;

    public readonly ImRaii.Color Push(ImGuiCol idx, bool condition = true)
        => ImRaii.PushColor(idx, ToUInt(), condition);

    public readonly uint ToUInt(ColorFormat format = ColorFormat.Rgba) {
        var vec = ToVector4(format);
        return PackByte(vec.X) | ((uint)PackByte(vec.Y) << 8) | ((uint)PackByte(vec.Z) << 16) | ((uint)PackByte(vec.W) << 24);
    }

    public readonly Vector4 ToVector4(ColorFormat format = ColorFormat.Rgba) => format switch {
        ColorFormat.Rgba => new(R, G, B, A),
        ColorFormat.Bgra => new(B, G, R, A),
        ColorFormat.Argb => new(A, R, G, B),
        ColorFormat.Abgr => new(A, B, G, R),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public readonly ByteColor ToByteColor() => new() {
        R = PackByte(R),
        G = PackByte(G),
        B = PackByte(B),
        A = PackByte(A),
    };

    public static implicit operator Vector4(Color colour) => new(colour.R, colour.G, colour.B, colour.A);
    public static implicit operator ByteColor(Color colour) => colour.ToByteColor();
    public static implicit operator uint(Color colour) => colour.ToUInt();

    public override readonly string ToString()
        => $"RGBA [{R:0.###}, {G:0.###}, {B:0.###}, {A:0.###}] ImGui {ToUInt():X8}";

    public static Color Transparent { get; } = new();
    public static Color White { get; } = new(1, 1, 1);
    public static Color Black { get; } = new(0, 0, 0);

    public static Color Red { get; } = new(1, 0, 0);
    public static Color RedDark { get; } = new(68 / 255, 0, 0);
    public static Color Green { get; } = new(0, 1, 0);
    public static Color GreenDark { get; } = new(0, 68 / 255, 0);
    public static Color Blue { get; } = new(0, 0, 1);
    public static Color BlueDark { get; } = new(0, 0, 68 / 255);
    public static Color Cyan { get; } = new(0, 1, 1);
    public static Color Magenta { get; } = new(1, 0, 1);
    public static Color MagentaDark { get; } = new(68 / 255, 0, 68 / 255);
    public static Color Yellow { get; } = new(1, 1, 0);
    public static Color YellowDark { get; } = new(68 / 255, 68 / 255, 0);
    public static Color Orange { get; } = new(1, 0.6f, 0);
    public static Color OrangeBright { get; } = new(1, 127 / 255, 0);
    public static Color Gold { get; } = new(0.847f, 0.733f, 0.49f);

    public static Color Text => From(ImGuiCol.Text);
    public static Color TextA90 => From(ImGuiCol.Text) with { A = 0.9f };
    public static Color TextA80 => From(ImGuiCol.Text) with { A = 0.8f };
    public static Color TextA70 => From(ImGuiCol.Text) with { A = 0.7f };
    public static Color TextA60 => From(ImGuiCol.Text) with { A = 0.6f };
    public static Color TextA50 => From(ImGuiCol.Text) with { A = 0.5f };
    public static Color TextA40 => From(ImGuiCol.Text) with { A = 0.4f };
    public static Color TextA30 => From(ImGuiCol.Text) with { A = 0.3f };
    public static Color TextA20 => From(ImGuiCol.Text) with { A = 0.2f };
    public static Color TextA10 => From(ImGuiCol.Text) with { A = 0.1f };

    private static byte PackByte(float value) => (byte)(value.Clamp01() * 255f + 0.5f);
    private static Color FromRgba(uint rgba) => FromVector4(ImGui.ColorConvertU32ToFloat4(rgba));
    private static Color FromBgra(uint bgra) => FromRgba((bgra & 0xFF00FF00) | ((bgra & 0x000000FF) << 16) | ((bgra & 0x00FF0000) >> 16));
    private static Color FromAbgr(uint abgr) => FromRgba(BinaryPrimitives.ReverseEndianness(abgr));
    private static Color FromArgb(uint argb) => FromRgba(BinaryPrimitives.ReverseEndianness((argb & 0xFF00FF00) | ((argb & 0x000000FF) << 16) | ((argb & 0x00FF0000) >> 16)));
}
