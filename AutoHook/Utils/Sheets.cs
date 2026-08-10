using Lumina.Excel;

namespace AutoHook.Utils;

// porting-note(api13): upstream's Lumina exposes GetRow/TryGetRow as statics on the generated row
// structs themselves ("Item dot GetRow(id)"). The Lumina shipped with the
// api13 runtime only exposes them on the sheet, so route through Svc.Data instead of rewriting every
// call site into a full GetExcelSheet<T>() chain.
public static class Sheets
{
    public static T GetRow<T>(uint id) where T : struct, IExcelRow<T>
        => Svc.Data.GetExcelSheet<T>().GetRow(id);

    public static bool TryGetRow<T>(uint id, out T row) where T : struct, IExcelRow<T>
        => Svc.Data.GetExcelSheet<T>().TryGetRow(id, out row);
}
