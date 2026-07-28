using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutoHook.Classes;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace AutoHook.Utils;

public static class GameRes
{
    public const uint FishingTackleRow = 30;
    public const int AllBaitsId = -99;
    public const int AllMoochesId = -98;

    public static List<BaitFishClass> Baits { get; private set; } = new();
    public static List<BaitFishClass> Fishes { get; private set; } = new();
    public static List<BaitFishClass> LureFishes => Fishes.Where(f => f.LureMessage != "").ToList();

    public static List<ImportedFish> ImportedFishes { get; private set; } = new();

    public static List<BiteTimers> BiteTimers { get; private set; } = new();

    public static void Initialize()
    {
        
        Baits =
            (Service.DataManager.GetExcelSheet<Item>()?
                 .Where(i => i.ItemSearchCategory.RowId == FishingTackleRow)
             ?? [])
            .Concat(
                // DLLSET-RECHECK(api13 official 13.0.0.16 / Lumina.Excel 7.3.1): the named WKSItemSubCategory column
                // may not exist in a future Lumina. Re-judged 2026-07-28: the official set's Lumina.Excel.dll is
                // byte-identical to the preview one and WKSItemInfo.WKSItemSubCategory is present, so this stands.
                // porting-note(api13): this DLL set's Lumina identified two more WKSItemInfo
                // columns and dropped one -- Unknown0 is gone, Unknown3 is now a bool, and
                // Item / WKSItemSubCategory are RowRefs. Upstream HEAD still reads the api12
                // Unknown names, so there is nothing to port forward. Switched to the named
                // columns, which is the defensible reading of the better-identified schema.
                // RUNTIME-VERIFY: confirm the cosmic bait list is still populated.
                Service.DataManager.GetExcelSheet<WKSItemInfo>()?
                    .Where(i => i.WKSItemSubCategory.RowId == 5)
                    .Select(i => Service.DataManager.GetExcelSheet<Item>()?.GetRow(i.Item.RowId))
                    .Where(item => item != null)
                    .Cast<Item>()
                ?? []
            )
            .Select(b => new BaitFishClass(b))
            .ToList();

        Fishes = Service.DataManager.GetExcelSheet<FishParameter>()?
                     .Where(f => f.Item.RowId != 0 && f.Item.RowId < 1000000)
                     .Select(f => new BaitFishClass(f))
                     .GroupBy(f => f.Id)
                     .Select(group => group.First())
                     .ToList()
                 ?? new List<BaitFishClass>();

        try
        {
            var fishList = Path.Combine(Service.PluginInterface.AssemblyLocation.DirectoryName!,
                $"Data\\FishData\\fish_list.json");

            if (File.Exists(fishList))
            {
                var json = File.ReadAllText(fishList);

                ImportedFishes = JsonSerializer.Deserialize<List<ImportedFish>>(json)!;
            }

            var biteTimers = Path.Combine(Service.PluginInterface.AssemblyLocation.DirectoryName!,
                $"Data\\FishData\\bitetimers.json");

            if (File.Exists(biteTimers))
            {
                var json = File.ReadAllText(biteTimers);

                BiteTimers = JsonSerializer.Deserialize<List<BiteTimers>>(json)!;
            }
        }
        catch (Exception e)
        {
            ImGui.SetClipboardText(e.Message);
            Service.PluginLog.Error($"{e.Message}");
        }
    }
}