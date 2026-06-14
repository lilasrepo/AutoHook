using System;
using AutoHook.Configurations;
using System.Linq;
using ECommons.EzIpcManager;

namespace AutoHook.IPC;

public class AutoHookIPC
{
    private Configuration _cfg = Service.Configuration;

    public AutoHookIPC()
    {
        EzIPC.Init(this, "AutoHook");
    }

    [EzIPC]
    public void SetPluginState(bool state)
    {
        
        _cfg.PluginEnabled = state;
        Service.Save();
    }

    [EzIPC]
    public void SetAutoGigState(bool state)
    {
        _cfg.AutoGigConfig.AutoGigEnabled = state;
        Service.Save();
    }

    [EzIPC]
    public void SetPreset(string preset)
    {
        Service.Save();
        _cfg.HookPresets.SelectedPreset =
            _cfg.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == preset);
        Service.Save();
    }

    public void SetPresetAutogig(string preset)
    {
        Service.Save();
        _cfg.AutoGigConfig.SelectedPreset =
            _cfg.AutoGigConfig.Presets.FirstOrDefault(x => x.PresetName == preset);
        Service.Save();
    }

    [EzIPC]
    public void CreateAndSelectAnonymousPreset(string preset)
    {
        var _import = Configuration.ImportPreset(preset);
        if (_import == null) return;
        var name = $"anon_{_import.PresetName}";
        _import.RenamePreset(name);
        Service.Save();
        _cfg.HookPresets.AddNewPreset(_import);
        _cfg.HookPresets.SelectedPreset =
            _cfg.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == name);
        Service.Save();
    }

    [EzIPC]
    public void ImportAndSelectPreset(string preset)
    {
        var _import = Configuration.ImportPreset(preset);
        if (_import == null) return;
        var name = $"{_import.PresetName}";
        _import.RenamePreset(name);
        
        if (_import is CustomPresetConfig customPreset)
            _cfg.HookPresets.AddNewPreset(customPreset);
        else if (_import is AutoGigConfig gigPreset)
            _cfg.AutoGigConfig.AddNewPreset(gigPreset);

        Service.Save();
    }

    [EzIPC]
    public void DeleteSelectedPreset()
    {
        var selected = _cfg.HookPresets.SelectedPreset;
        if (selected == null) return;
        _cfg.HookPresets.RemovePreset(selected.UniqueId);
        _cfg.HookPresets.SelectedPreset = null;
        Service.Save();
    }

    [EzIPC]
    public void DeleteAllAnonymousPresets()
    {
        _cfg.HookPresets.CustomPresets.RemoveAll(p => p.PresetName.StartsWith("anon_"));
        Service.Save();
    }
}