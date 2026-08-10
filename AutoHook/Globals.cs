global using AutoHook.Classes;
global using AutoHook.Classes.AutoCasts;
global using AutoHook.Configurations;
global using AutoHook.Data;
global using AutoHook.Enums;
global using AutoHook.Fishing;
global using AutoHook.Resources.Localization;
global using AutoHook.Utils;
global using ECommons;
// porting-note(api13): upstream drops this because croizat.clib re-exports its own Svc (an ECommons
// superset that adds Svc.Automation). clib is net10-only, so this build uses ECommons' Svc directly;
// the only member that superset added is Automation, which is already degraded out - see the B1
// notes in FishingManager.
global using ECommons.DalamudServices;
global using ECommons.GameHelpers;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Text;
global using System.Threading.Tasks;
global using static ECommons.GenericHelpers;
