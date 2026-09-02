namespace clib.Ui;

// https://github.com/Haselnussbomber/HaselCommon/blob/79fb95330bed8e1c4ce47cb8f7130eeb6cdd01e3/HaselCommon/Graphics/ColorFormat.cs
public enum ColorFormat {
    /// <summary>RGBA | 0xAABBGGRR</summary>
    /// <remarks>Used by ImGui</remarks>
    Rgba,
    /// <summary>BGRA | 0xAARRGGBB</summary>
    /// <remarks>Used by SeStrings</remarks>
    Bgra,
    /// <summary>ARGB | 0xBBGGRRAA</summary>
    Argb,
    /// <summary>ABGR | 0xRRGGBBAA</summary>
    Abgr,
}
