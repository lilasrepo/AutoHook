using System;
using AutoHook.Resources.Localization;
using Lumina.Excel.Sheets;
using Lumina.Text;
using Lumina.Text.ReadOnly;
using Action = Lumina.Excel.Sheets.Action;

namespace AutoHook.Utils;

public readonly struct MultiString
{
    public static string ParseSeString(ReadOnlySeString? luminaString)
        => luminaString?.ExtractText() ?? string.Empty;

    public static string GetStatusName(uint statusId)
    {
        return ParseSeString(Service.DataManager.GetExcelSheet<Status>()!.GetRowOrDefault(statusId)?.Name);
    }
    
    public static string GetActionName(uint id)
    {
        return ParseSeString(Service.DataManager.GetExcelSheet<Action>().GetRowOrDefault(id)?.Name); 
    } 
    
    public static string GetItemName(uint id)
    {
        string itemName = string.Empty;
        try
        {
            itemName = ParseSeString(Service.DataManager.GetExcelSheet<Item>()!.GetRowOrDefault(id)?.Name);
        }
        catch (Exception e)
        {
           Service.PluginLog.Error(e.Message);
        }
        if (id == 0)
            return UIStrings.None;
       
        
        
        return itemName == string.Empty ? UIStrings.None : itemName;
    } 
    
    public static string GetItemName(int id)
    {
        return GetItemName((uint)id);
    } 
}
