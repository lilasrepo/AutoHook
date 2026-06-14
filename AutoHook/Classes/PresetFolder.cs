using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AutoHook.Classes;

public class PresetFolder
{
    public Guid UniqueId { get; set; } = Guid.NewGuid();
    public string FolderName { get; set; }
    public bool IsExpanded { get; set; } = true;
    public List<Guid> PresetIds { get; set; } = new();

    public PresetFolder(string folderName)
    {
        FolderName = folderName;
    }

    public void AddPreset(Guid presetId)
    {
        if (!PresetIds.Contains(presetId))
            PresetIds.Add(presetId);
    }

    public void RemovePreset(Guid presetId)
    {
        if (PresetIds.Contains(presetId))
            PresetIds = PresetIds.Where(p => p != presetId).ToList();
    }

    public bool ContainsPreset(Guid presetId)
    {
        return PresetIds.Contains(presetId);
    }
}