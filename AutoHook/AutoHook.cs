using AutoHook.Spearfishing;
using AutoHook.Ui;
using clib;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Plugin;
using ECommons.EzDTR;
using PunishLib;
using System.Threading;

namespace AutoHook;

/* 
 * TODO: 
 * get rid of all other configs that could be conditions in auto casts et al. Migrate them to conditions.
 * stop movement while fishing
 * auto extract materia
 * move around to reduce fish weary
 */

// porting-note(api13): upstream's entry point is croizat.clib's IAsyncDalamudPlugin pattern
// (LoadAsync/DisposeAsync driven by CLibMain). clib publishes lib/net10.0-windows7.0 ONLY - all 82
// published versions, 1.0.0 through 1.0.81, checked against nuget.org - so a net9 build cannot
// reference it at any version. api13 Dalamud only offers the classic synchronous IDalamudPlugin,
// which is what this build uses. The load/dispose bodies are upstream's, unchanged except that the
// two awaits are resolved synchronously; Configuration.LoadAsync already uses ConfigureAwait(false),
// and a Dalamud plugin constructor carries no synchronisation context, so blocking here is safe.
public class AutoHook : IDalamudPlugin {
    private readonly IDalamudPluginInterface pluginInterface;

    public AutoHook(IDalamudPluginInterface pluginInterface) {
        this.pluginInterface = pluginInterface;
        Load();
    }

    public string Name => UIStrings.AutoHook;

    internal static AutoHook Plugin = null!;

    //todo: - Spearfishing rework
    private const string CmdAhCfg = "/ahcfg";
    private const string CmdAh = "/autohook";
    private const string CmdAhOn = "/ahon";
    private const string CmdAhOff = "/ahoff";
    private const string CmdAhtg = "/ahtg";
    private const string CmdAhPreset = "/ahpreset";
    private const string CmdAhStart = "/ahstart";
    private const string CmdAhBait = "/ahbait";
    private const string CmdBait = "/bait";
    private const string CmdAgPreset = "/agpreset";
    private const string CmdAhReplay = "/ahreplay";

    private static readonly Dictionary<string, string> CommandHelp = new()
    {
        { CmdAhOff, UIStrings.Disables_AutoHook },
        { CmdAhOn, UIStrings.Enables_AutoHook },
        { CmdAhCfg, UIStrings.Opens_Config_Window },
        { CmdAh, UIStrings.Opens_Config_Window },
        { CmdAhtg, UIStrings.Toggles_AutoHook_On_Off },
        { CmdAhPreset, UIStrings.Set_preset_command },
        { CmdAhStart, UIStrings.Starts_AutoHook },
        { CmdAhBait, UIStrings.SwitchFishBait },
        { CmdBait, UIStrings.SwitchFishBait },
        { CmdAgPreset, UIStrings.Set_agpreset_command },
        { CmdAhReplay, UIStrings.Opens_Replay_Window }
    };

    private static PluginUi _pluginUi = null!;
    private static AutoGig _autoGig = null!;
    private static ReplayManagementWindow _replayManagement = null!;

    private void Load() {
        ECommonsMain.Init(pluginInterface, this, Module.DalamudReflector, Module.ObjectFunctions);
        // porting-note(api13): restored now that croizat.clib is vendored in-tree (net9-retargeted)
        // instead of being assumed net10-only and dropped. Only CLibModule.Automation, matching
        // upstream exactly - Svc.Navmesh/Svc.Windows are unconditional inside clib's own Svc.Init.
        CLibMain.Init(pluginInterface, this, CLibModule.Automation);
        PunishLibMain.Init(pluginInterface, "AutoHook", new AboutPlugin() { Developer = "InitialDet & croizat", Sponsor = "https://ko-fi.com/initialdet" });
        Service.InitAsync(pluginInterface).AsTask().GetAwaiter().GetResult();

        Plugin = this;
        _pluginUi = new PluginUi();
        _autoGig = new AutoGig();
        _replayManagement = new ReplayManagementWindow();

        foreach (var (command, help) in CommandHelp) {
            Svc.Commands.AddHandler(command, new CommandInfo(OnCommand) {
                HelpMessage = help
            });
        }

        GameRes.Initialize();

        Svc.PluginInterface.UiBuilder.Draw += DrawUi;
        Svc.PluginInterface.UiBuilder.OpenConfigUi += _pluginUi.Toggle;
        Svc.PluginInterface.UiBuilder.OpenMainUi += _pluginUi.Toggle;

        SetupDtr();

#if DEBUG
        if (Svc.ClientState.IsLoggedIn)
            _pluginUi.Toggle();
#endif
    }

    public void Dispose() {
        _pluginUi.Dispose();
        _autoGig.Dispose();
        _replayManagement.Dispose();
        Svc.PluginInterface.UiBuilder.Draw -= DrawUi;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= _pluginUi.Toggle;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= _pluginUi.Toggle;

        foreach (var (command, _) in CommandHelp)
            Svc.Commands.RemoveHandler(command);

        Service.DisposeAsync().AsTask().GetAwaiter().GetResult();
        CLibMain.Dispose();
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args) {
        switch (command.Trim()) {
            case CmdAhCfg:
            case CmdAh:
                _pluginUi.Toggle();
                break;
            case CmdAhOn:
                Svc.Chat.Print(UIStrings.AutoHook_Enabled);
                Service.Configuration.PluginEnabled = true;
                break;
            case CmdAhOff:
                Svc.Chat.Print(UIStrings.AutoHook_Disabled);
                Service.Configuration.PluginEnabled = false;
                break;
            case CmdAhtg when Service.Configuration.PluginEnabled:
                Svc.Chat.Print(UIStrings.AutoHook_Disabled);
                Service.Configuration.PluginEnabled = false;
                break;
            case CmdAhtg:
                Svc.Chat.Print(UIStrings.AutoHook_Enabled);
                Service.Configuration.PluginEnabled = true;
                break;
            case CmdAhPreset:
                SetPreset(args);
                break;
            case CmdAhStart:
                Service.FishManager.StartFishing();
                break;
            case CmdBait:
            case CmdAhBait:
                SwapBait(args);
                break;
            case CmdAgPreset:
                SetGigPreset(args);
                Service.ReplayManagement.Toggle();
                break;
            case CmdAhReplay:
                _replayManagement.Toggle();
                break;
        }
    }

    private static void SwapBait(string args) {
        var bait = GameRes.Baits.FirstOrDefault(f => f.Name.ToLower() == args.ToLower() || f.Id.ToString() == args);
        FishingManager.ChangeBait((uint)bait?.Id!);
    }

    private static void SetPreset(string presetName) {
        var preset = Service.Configuration.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == presetName);
        if (preset == null) {
            Svc.Chat.Print(UIStrings.Preset_not_found);
            return;
        }

        Service.Configuration.HookPresets.Select(preset, FishingPresets.ReasonManual);
        Svc.Chat.Print(@$"{UIStrings.Preset_set_to_} {preset.PresetName}");
        Configuration.FlushAsync().GetAwaiter().GetResult();
    }

    private static void SetGigPreset(string presetName) {
        try {
            var preset = Service.Configuration.AutoGigConfig.Presets.FirstOrDefault(x => x.PresetName == presetName);
            if (preset == null) {
                Svc.Chat.Print(@$"{UIStrings.Preset_not_found} - {presetName}");
                return;
            }

            Service.Configuration.AutoGigConfig.SelectedPreset = preset;
            Svc.Chat.Print(@$"{UIStrings.Gig_preset_set_to_} {preset.PresetName}");
            Configuration.FlushAsync().GetAwaiter().GetResult();
        }
        catch (Exception e) {
            Svc.Log.Error(e, "[AutoHook] SetGigPreset failed.");
        }
    }

    private static void DrawUi() {
        Service.WindowSystem.Draw();
        Service.FileDialog.Draw();
        OceanFishingSpotOverlay.Draw();
    }

    private void SetupDtr() {
        _ = new EzDtr(() => $"{((SeIconChar)0xE05E).ToIconString()} {(Service.Configuration.PluginEnabled ? UIStrings.Enabled : UIStrings.Disabled)}",
                evt => {
                    if (evt.ClickType is MouseClickType.Left) {
                        Service.Configuration.PluginEnabled ^= true;
                        Service.Save();
                    }
                    else if (evt.ClickType is MouseClickType.Right)
                        _pluginUi.Toggle();
                },
                showCondition: () => Service.Configuration.DtrBarEnabled && Player.Job is ECommons.ExcelServices.Job.FSH
            );

        _ = new EzDtr(() => $"{SeIconChar.Collectible.ToIconString()} {Service.Configuration.HookPresets.SelectedPreset?.PresetName ?? $"{UIStrings.GlobalPreset}"}",
            evt => {
                if (Service.Configuration.HookPresets.SelectedPreset == null) return;
                var presets = Service.Configuration.HookPresets.CustomPresets;
                var index = presets.IndexOf(Service.Configuration.HookPresets.SelectedPreset);
                var direction = evt.ClickType == MouseClickType.Left ? 1 : -1;
                Service.Configuration.HookPresets.SelectedPreset = presets[(index + direction + presets.Count) % presets.Count];
                Service.Save();
            },
            $"{Name}Presets",
            () => Service.Configuration.DtrPresetBarEnabled && Player.Job is ECommons.ExcelServices.Job.FSH && Service.Configuration.HookPresets.SelectedPreset != null
        );
    }
}
