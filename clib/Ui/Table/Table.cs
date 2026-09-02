using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace clib.Ui.Table;

// https://github.com/Ottermandias/OtterGui/blob/79771ee5f3d463f02c63bebbedaa0aff49e59718/Table/Table.cs#L14
public class Table<T>(string id, IReadOnlyList<T> items, params Column<T>[] columns) {
    public const float ArrowWidth = 10f;

    private bool _filterDirty = true;
    private readonly List<T> _filteredItems = [];
    public ImGuiTableFlags Flags = ImGuiTableFlags.RowBg
        | ImGuiTableFlags.BordersOuter
        | ImGuiTableFlags.ScrollY
        | ImGuiTableFlags.PreciseWidths
        | ImGuiTableFlags.SizingFixedFit
        | ImGuiTableFlags.BordersInnerV
        | ImGuiTableFlags.NoBordersInBodyUntilResize
        | ImGuiTableFlags.NoSavedSettings;

    public Func<T, bool>? ExtraFilter { get; set; }
    public Action<T>? DrawPrefix { get; set; }
    public string PrefixLabel { get; set; } = string.Empty;
    public float PrefixWidth { get; set; } = 28f;

    public int TotalItems => items.Count;
    public int VisibleItems => _filteredItems.Count;

    public void Draw(float rowHeight) {
        using var scope = ImRaii.PushId(id);
        UpdateFilter();

        var columnCount = columns.Length + (DrawPrefix is null ? 0 : 1);
        using var table = ImRaii.Table("Table", columnCount, Flags, ImGui.GetContentRegionAvail());
        if (!table)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        if (DrawPrefix is not null)
            ImGui.TableSetupColumn(PrefixLabel, ImGuiTableColumnFlags.WidthFixed, PrefixWidth);

        foreach (var column in columns)
            ImGui.TableSetupColumn(column.Label, column.Flags, column.Width);

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        if (DrawPrefix is not null) {
            ImGui.TableSetColumnIndex(0);
            ImGui.TableHeader(PrefixLabel);
        }

        for (var i = 0; i < columns.Length; i++) {
            var col = DrawPrefix is null ? i : i + 1;
            using var id = ImRaii.PushId(i);
            if (!ImGui.TableSetColumnIndex(col))
                continue;

            using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            ImGui.TableHeader(string.Empty);
            ImGui.SameLine();
            style.Pop();
            if (columns[i].DrawFilter())
                _filterDirty = true;
        }

        var clipper = ImGui.ImGuiListClipper();
        clipper.Begin(_filteredItems.Count, rowHeight);
        while (clipper.Step()) {
            for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++) {
                var item = _filteredItems[row];
                using var rowId = ImRaii.PushId(row);
                ImGui.TableNextRow();

                if (DrawPrefix is not null) {
                    ImGui.TableNextColumn();
                    DrawPrefix(item);
                }

                foreach (var column in columns) {
                    ImGui.TableNextColumn();
                    column.DrawColumn(item);
                }
            }
        }
        clipper.End();
    }

    private void UpdateFilter() {
        if (!_filterDirty)
            return;

        _filteredItems.Clear();
        foreach (var item in items) {
            if (ExtraFilter is { } extra && !extra(item))
                continue;
            if (columns.All(column => column.FilterFunc(item)))
                _filteredItems.Add(item);
        }

        _filterDirty = false;
    }

    public void InvalidateFilter() => _filterDirty = true;
}
