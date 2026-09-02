namespace clib;

[Flags]
public enum CLibModule {
    None = 0,
    Items = 1 << 0,
    Automation = 1 << 1,
    SheetManager = 1 << 2,
    All = Items | Automation | SheetManager,
}
