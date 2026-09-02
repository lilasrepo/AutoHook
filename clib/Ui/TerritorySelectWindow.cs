using clib.Services;
using clib.Ui.Table;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using TerritoryIntendedUseEnum = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace clib.Ui;

[Flags]
public enum TerritoryUseCategory {
    World = 1 << 0,
    Housing = 1 << 1,
    Inn = 1 << 2,
    Dungeon = 1 << 3,
    Raid = 1 << 4,
    Trial = 1 << 5,
    DeepDungeon = 1 << 6,
    Other = 1 << 7,
    All = World | Housing | Inn | Dungeon | Raid | Trial | DeepDungeon | Other,
}

public sealed record TerritoryRow(uint Id, string PlaceName, string Duty, string Zone, string Region, string IntendedUse, TerritoryUseCategory Category);

[Flags]
public enum TerritorySelectColumn {
    None = 0,
    Id = 1 << 0,
    PlaceName = 1 << 1,
    Duty = 1 << 2,
    Zone = 1 << 3,
    Region = 1 << 4,
    IntendedUse = 1 << 5,
    All = Id | PlaceName | Duty | Zone | Region | IntendedUse,
}

public sealed record TerritorySelectOptions {
    public Action<HashSet<uint>>? OnChanged { get; init; }
    public Func<TerritoryType, bool>? Filter { get; init; }
    public string Title { get; init; } = "Select zones";
    public bool SingleSelect { get; init; }
    public TerritorySelectColumn Columns { get; init; } = TerritorySelectColumn.All;
}

public sealed class TerritorySelectWindow : Window {
    private readonly HashSet<uint> _selection;
    private readonly Action<HashSet<uint>>? _onChanged;
    private readonly bool _singleSelect;
    private readonly IReadOnlyList<TerritoryRow> _rows;
    private readonly Table<TerritoryRow> _table;

    private bool _onlySelected;
    private uint _singleSelection;

    public static TerritorySelectWindow Show(HashSet<uint> selection, TerritorySelectOptions? options = null) {
        Svc.Windows.RemoveWindow<TerritorySelectWindow>();
        return new TerritorySelectWindow(selection, options ?? new TerritorySelectOptions());
    }

    private TerritorySelectWindow(HashSet<uint> selection, TerritorySelectOptions options) : base($"{options.Title}###TerritorySelectWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings) {
        _selection = selection;
        _onChanged = options.OnChanged;
        _singleSelect = options.SingleSelect;
        _singleSelection = selection.FirstOrDefault();

        Size = new(900, 600);
        SizeCondition = ImGuiCond.FirstUseEver;

        _rows = BuildRows(options.Filter);
        var categoryColumn = new ColumnFlags<TerritoryUseCategory, TerritoryRow> {
            Label = "Intended Use",
            AllFlags = TerritoryUseCategory.All,
            FilterValue = TerritoryUseCategory.All,
            GetFlags = row => row.Category,
            FormatName = FormatCategory,
            DisplayText = row => row.IntendedUse,
            Width = 150f,
            Flags = ImGuiTableColumnFlags.WidthFixed,
        };

        _table = new Table<TerritoryRow>("TerritorySelect", _rows, BuildColumns(options.Columns, categoryColumn)) {
            DrawPrefix = DrawSelect,
            PrefixLabel = string.Empty,
            PrefixWidth = 28f,
            ExtraFilter = row => !_onlySelected || _selection.Contains(row.Id),
        };

        Svc.Windows.AddWindow(this);
        IsOpen = true;
    }

    public override void OnClose() => Svc.Windows.RemoveWindow(this);

    public override void Draw() {
        DrawToolbar();

        var rowHeight = ImGui.GetTextLineHeightWithSpacing();
        var footerHeight = rowHeight + ImGui.GetStyle().ItemSpacing.Y;
        using (ImRaii.Child("TerritoryTable", new Vector2(0, ImGui.GetContentRegionAvail().Y - footerHeight))) {
            _table.Draw(rowHeight);
        }

        ImGui.TextDisabled($"{_table.VisibleItems} / {_table.TotalItems} visible");
    }

    private void DrawToolbar() {
        if (ImGui.Checkbox("Only selected", ref _onlySelected))
            _table.InvalidateFilter();

        if (IObjectTable.Get().LocalPlayer is not { Available: true })
            return;

        var territoryId = IClientState.Get().TerritoryType;

        // don't let current be selectable if it's not part of the _rows
        if (_rows.None(r => r.Id == territoryId))
            return;

        ImGui.SameLine();
        if (_singleSelect) {
            if (ImGui.RadioButton($"Current: {IPlayerState.Get().Territory.ValueNullable?.PlaceName.ValueNullable?.Name ?? $"#{territoryId}"}", _singleSelection == territoryId)) {
                _singleSelection = territoryId;
                _selection.Clear();
                _selection.Add(territoryId);
                NotifySelectionChanged();
            }
        }
        else if (ImGui.CollectionCheckbox($"Current: {IPlayerState.Get().Territory.ValueNullable?.PlaceName.ValueNullable?.Name ?? $"#{territoryId}"}", territoryId, _selection)) {
            NotifySelectionChanged();
        }
    }

    private void DrawSelect(TerritoryRow row) {
        var isCurrent = IClientState.Get().TerritoryType == row.Id;
        using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange, isCurrent);

        if (_singleSelect) {
            if (ImGui.RadioButton($"##sel{row.Id}", _singleSelection == row.Id)) {
                _singleSelection = row.Id;
                _selection.Clear();
                _selection.Add(row.Id);
                NotifySelectionChanged();
            }
        }
        else if (ImGui.CollectionCheckbox($"##sel{row.Id}", row.Id, _selection)) {
            NotifySelectionChanged();
        }
    }

    private void NotifySelectionChanged() {
        try {
            _onChanged?.Invoke(_selection);
        }
        catch (Exception e) {
            IPluginLog.Get().Error(e, "TerritorySelectWindow callback failed");
        }
    }

    private static Column<TerritoryRow>[] BuildColumns(TerritorySelectColumn visible, ColumnFlags<TerritoryUseCategory, TerritoryRow> categoryColumn) {
        var columns = new List<Column<TerritoryRow>>();
        if (visible.HasFlag(TerritorySelectColumn.Id)) {
            columns.Add(new ColumnString<TerritoryRow>(row => row.Id.ToString()) {
                Label = "ID",
                Width = 72f,
                Flags = ImGuiTableColumnFlags.WidthFixed,
            });
        }

        if (visible.HasFlag(TerritorySelectColumn.PlaceName)) {
            columns.Add(new ColumnString<TerritoryRow>(row => row.PlaceName) {
                Label = "Place Name...",
                Width = 2f,
                Flags = ImGuiTableColumnFlags.WidthStretch,
            });
        }

        if (visible.HasFlag(TerritorySelectColumn.Duty)) {
            columns.Add(new ColumnString<TerritoryRow>(row => row.Duty) {
                Label = "Duty...",
                Width = 1.5f,
                Flags = ImGuiTableColumnFlags.WidthStretch,
            });
        }

        if (visible.HasFlag(TerritorySelectColumn.Zone)) {
            columns.Add(new ColumnString<TerritoryRow>(row => row.Zone) {
                Label = "Zone...",
                Width = 1f,
                Flags = ImGuiTableColumnFlags.WidthStretch,
            });
        }

        if (visible.HasFlag(TerritorySelectColumn.Region)) {
            columns.Add(new ColumnString<TerritoryRow>(row => row.Region) {
                Label = "Region...",
                Width = 1f,
                Flags = ImGuiTableColumnFlags.WidthStretch,
            });
        }

        if (visible.HasFlag(TerritorySelectColumn.IntendedUse))
            columns.Add(categoryColumn);

        return columns.Count > 0 ? [.. columns] : [
            new ColumnString<TerritoryRow>(row => row.PlaceName) {
                Label = "Place Name...",
                Width = 1f,
                Flags = ImGuiTableColumnFlags.WidthStretch,
            },
        ];
    }

    private static List<TerritoryRow> BuildRows(Func<TerritoryType, bool>? filter) {
        var rows = new List<TerritoryRow>();
        foreach (var row in TerritoryType.Rows) {
            if (filter is not null && !filter(row))
                continue;
            if (!TryCreateRow(row, out var territoryRow))
                continue;
            rows.Add(territoryRow);
        }

        return rows;
    }

    internal static bool TryCreateRow(TerritoryType row, out TerritoryRow territoryRow) {
        var duty = string.Empty;
        if (ContentFinderCondition.TryGetRow(row.ContentFinderCondition.RowId, out var cfc))
            duty = cfc.Name.ToString();
        else if (QuestBattle.TryGetRow(row.QuestBattle.RowId, out var qb) && Quest.TryGetRow(qb.Quest.RowId, out var quest))
            duty = quest.Name.ToString();

        var placeName = row.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty;

        if (placeName.IsEmpty && duty.IsEmpty) {
            territoryRow = default!;
            return false;
        }

        var zone = row.PlaceNameZone.ValueNullable?.Name.ToString() ?? string.Empty;
        var region = row.PlaceNameRegion.ValueNullable?.Name.ToString() ?? string.Empty;
        var intendedUseEnum = row.TerritoryIntendedUse.Value.StructsEnum;
        var intendedUse = intendedUseEnum.ToString().Replace("_", " ", StringComparison.Ordinal);
        var category = GetCategory(intendedUseEnum);

        territoryRow = new TerritoryRow(row.RowId, placeName, duty, zone, region, intendedUse, category);
        return true;
    }

    internal static TerritoryUseCategory GetCategory(TerritoryIntendedUseEnum use) => use switch {
        TerritoryIntendedUseEnum.Town or TerritoryIntendedUseEnum.Overworld or TerritoryIntendedUseEnum.OpeningArea => TerritoryUseCategory.World,
        TerritoryIntendedUseEnum.HousingOutdoor or TerritoryIntendedUseEnum.HousingIndoor => TerritoryUseCategory.Housing,
        TerritoryIntendedUseEnum.Inn => TerritoryUseCategory.Inn,
        TerritoryIntendedUseEnum.Dungeon or TerritoryIntendedUseEnum.VariantDungeon or TerritoryIntendedUseEnum.CriterionDungeon
            or TerritoryIntendedUseEnum.CriterionDungeonSavage => TerritoryUseCategory.Dungeon,
        TerritoryIntendedUseEnum.AllianceRaid or TerritoryIntendedUseEnum.Raid1 or TerritoryIntendedUseEnum.Raid2
            or TerritoryIntendedUseEnum.ChaoticRaid or TerritoryIntendedUseEnum.DelubrumReginae or TerritoryIntendedUseEnum.DelubrumReginaeSavage => TerritoryUseCategory.Raid,
        TerritoryIntendedUseEnum.Trial => TerritoryUseCategory.Trial,
        TerritoryIntendedUseEnum.DeepDungeon => TerritoryUseCategory.DeepDungeon,
        _ => TerritoryUseCategory.Other,
    };

    private static string FormatCategory(TerritoryUseCategory category) => category switch {
        TerritoryUseCategory.World => "World",
        TerritoryUseCategory.Housing => "Housing",
        TerritoryUseCategory.Inn => "Inn",
        TerritoryUseCategory.Dungeon => "Dungeon",
        TerritoryUseCategory.Raid => "Raid",
        TerritoryUseCategory.Trial => "Trial",
        TerritoryUseCategory.DeepDungeon => "Deep Dungeon",
        TerritoryUseCategory.Other => "Other",
        _ => category.ToString(),
    };
}
