#nullable disable

#region

using Dalamud.Hooking;
using ECommons.DalamudServices;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using InteropGenerator.Runtime;
using System;

#endregion

namespace ECommons.Hooks;

public static unsafe class StaticVfx
{
    /// <summary>
    ///     The signature your method must match to subscribe to
    ///     <see cref="StaticVfxCreateEvent" />.
    /// </summary>
    /// <param name="vfxPtr">
    ///     Pointer to the newly-created Static VFX instance, this is the
    ///     <see langword="return" /> from calling <c>.Original()</c> inside
    ///     of that Detour, prior to calling your Delegate.<br />
    ///     Due to that, this delegate does NOT match the signature of the
    ///     Detour itself.<br />
    ///     Can be cast to a <see cref="ECommons.GameHelpers.VfxStruct" />.
    /// </param>
    /// <param name="path">The VFX path string.</param>
    /// <param name="systemSource">
    ///     The FFXIV subsystem that is responsible for creating the VFX.
    /// </param>
    /// <remarks>
    ///     WARNING! Do NOT try to call <c>.Original()</c> within your delegate!
    ///     <br />
    ///     These delegates are already called after that is run (to provide
    ///     <paramref name="vfxPtr" />).
    /// </remarks>
    public delegate void StaticVfxCreateCallbackDelegate(nint vfxPtr, string path, string systemSource);

    /// <summary>
    ///     The signature your method must match to subscribe to
    ///     <see cref="StaticVfxDtorEvent" />.
    /// </summary>
    /// <param name="staticVfxAddress">
    ///     Address of the Static VFX instance being destructed.<br />
    ///     Can be cast to a <see cref="ECommons.GameHelpers.VfxStruct" />.
    /// </param>
    /// <remarks>
    ///     WARNING! Do NOT try to call <c>.Original()</c> within your delegate!
    ///     <br />
    ///     These delegates are already called right before that is run.
    /// </remarks>
    public delegate void StaticVfxDtorCallbackDelegate(nint staticVfxAddress);

    /// <summary>
    ///     The signature your method must match to subscribe to
    ///     <see cref="StaticVfxRunEvent" />.
    /// </summary>
    /// <param name="staticVfxAddress">
    ///     Address of the Static VFX instance being run.<br />
    ///     Can be cast to a <see cref="ECommons.GameHelpers.VfxStruct" />.
    /// </param>
    /// <param name="a1">
    ///     Unknown float(?) parameter.<br />
    ///     Possibly related to position/rotation?
    /// </param>
    /// <param name="a2">
    ///     Unknown uint(?) parameter.<br />
    ///     Possibly related to position/rotation?<br />
    ///     Possibly an incorrectly-typed game object id?
    /// </param>
    /// <remarks>
    ///     WARNING! Do NOT try to call <c>.Original()</c> within your delegate!
    ///     <br />
    ///     These delegates are already called right before that is run.
    /// </remarks>
    public delegate void StaticVfxRunCallbackDelegate(nint staticVfxAddress, float a1, uint a2);

    // porting-note(api13): FFXIVClientStructs 6966 (TC_ok/_dalamud_api13) declares VfxObject with
    // no Addresses / MemberFunctionPointers and only a GetObjectType delegate, so
    // VfxObject.Delegates.{Create,Update,CleanupRender} and VfxObject.Addresses.* do not exist.
    // The hook TYPES are re-declared locally so every consumer of this class still compiles
    // (ECommonsMain.Dispose, VfxManager's event subscriptions); the hooks themselves are never
    // installed -- see HookCreate/HookRun/HookDtor below. ICE does not use StaticVfx.
    internal delegate VfxObject* StaticVfxCreateDelegate(CStringPointer a1, CStringPointer a2);

    internal delegate void StaticVfxUpdateDelegate(VfxObject* a1, float a2, int a3);

    internal delegate void StaticVfxCleanupRenderDelegate(VfxObject* a1);

    private static Hook<StaticVfxCreateDelegate>? StaticVfxCreateHook;

    private static Hook<StaticVfxUpdateDelegate>? StaticVfxRunHook;

    private static Hook<StaticVfxCleanupRenderDelegate>? StaticVfxDtorHook;

    private static event StaticVfxCreateCallbackDelegate? _staticVfxCreateEvent;

    private static event StaticVfxRunCallbackDelegate? _staticVfxRunEvent;

    private static event StaticVfxDtorCallbackDelegate? _staticVfxDtorEvent;

    /// <summary>
    ///     Add a <see cref="StaticVfxCreateCallbackDelegate" /> subscriber
    ///     to this event to be called when a static VFX is created.
    /// </summary>
    public static event StaticVfxCreateCallbackDelegate StaticVfxCreateEvent
    {
        add
        {
            HookCreate();
            _staticVfxCreateEvent += value;
        }
        remove => _staticVfxCreateEvent -= value;
    }

    /// <summary>
    ///     Add a <see cref="StaticVfxRunCallbackDelegate" /> subscriber
    ///     to this event to be called when a static VFX is run.
    /// </summary>
    public static event StaticVfxRunCallbackDelegate StaticVfxRunEvent
    {
        add
        {
            HookRun();
            _staticVfxRunEvent += value;
        }
        remove => _staticVfxRunEvent -= value;
    }

    /// <summary>
    ///     Add a <see cref="StaticVfxDtorCallbackDelegate" /> subscriber
    ///     to this event to be called when a static VFX is destructed.
    /// </summary>
    public static event StaticVfxDtorCallbackDelegate StaticVfxDtorEvent
    {
        add
        {
            HookDtor();
            _staticVfxDtorEvent += value;
        }
        remove => _staticVfxDtorEvent -= value;
    }

    internal static VfxObject* StaticVfxCreateDetour(CStringPointer a1, CStringPointer a2)
    {
        var output = StaticVfxCreateHook!.Original(a1, a2);

        try
        {
            var @event = _staticVfxCreateEvent;
            if(@event != null)
            {
                // Iterate individual subscribers so a failing subscriber doesn't stop the rest.
                foreach(var subscriber in @event.GetInvocationList())
                {
                    try
                    {
                        var subscriberMethod = (StaticVfxCreateCallbackDelegate)subscriber;
                        subscriberMethod((nint)output, a1.ToString(), a2.ToString());
                    }
                    catch(Exception e)
                    {
                        e.Log();
                    }
                }
            }
        }
        catch(Exception e)
        {
            e.Log();
        }

        return output;
    }

    internal static void StaticVfxRunDetour(VfxObject* a1, float a2, int a3)
    {
        try
        {
            var @event = _staticVfxRunEvent;
            if(@event != null)
            {
                // Iterate individual subscribers so a failing subscriber doesn't stop the rest.
                foreach(var subscriber in @event.GetInvocationList())
                {
                    try
                    {
                        var subscriberMethod = (StaticVfxRunCallbackDelegate)subscriber;
                        subscriberMethod((nint)a1, a2, (uint)a3);
                    }
                    catch(Exception e)
                    {
                        e.Log();
                    }
                }
            }
        }
        catch(Exception e)
        {
            e.Log();
        }

        StaticVfxRunHook!.Original(a1, a2, a3);
    }

    internal static void StaticVfxDtorDetour(VfxObject* a1)
    {
        try
        {
            var @event = _staticVfxDtorEvent;
            if(@event != null)
            {
                // Iterate individual subscribers so a failing subscriber doesn't stop the rest.
                foreach(var subscriber in @event.GetInvocationList())
                {
                    try
                    {
                        var subscriberMethod = (StaticVfxDtorCallbackDelegate)subscriber;
                        subscriberMethod((nint)a1);
                    }
                    catch(Exception e)
                    {
                        e.Log();
                    }
                }
            }
        }
        catch(Exception e)
        {
            e.Log();
        }

        StaticVfxDtorHook!.Original(a1);
    }

    private static void HookCreate()
    {
        if(StaticVfxCreateHook != null)
        {
            return;
        }

        // porting-note(api13): inert -- see the delegate block above. Left as a no-op rather than
        // throwing so a caller that subscribes still degrades to "never fires".
        PluginLog.Warning("StaticVfx.HookCreate is unavailable at api13 (VfxObject has no member-function table); ignoring.");
    }

    private static void HookRun()
    {
        if(StaticVfxRunHook != null)
        {
            return;
        }

        // porting-note(api13): inert -- see the delegate block above. Left as a no-op rather than
        // throwing so a caller that subscribes still degrades to "never fires".
        PluginLog.Warning("StaticVfx.HookRun is unavailable at api13 (VfxObject has no member-function table); ignoring.");
    }

    private static void HookDtor()
    {
        if(StaticVfxDtorHook != null)
        {
            return;
        }

        // porting-note(api13): inert -- see the delegate block above. Left as a no-op rather than
        // throwing so a caller that subscribes still degrades to "never fires".
        PluginLog.Warning("StaticVfx.HookDtor is unavailable at api13 (VfxObject has no member-function table); ignoring.");
    }

    /// <remarks>
    ///     Already called when you subscribe a delegate to
    ///     <see cref="StaticVfxCreateEvent" />.
    /// </remarks>
    public static void EnableCreate()
    {
        if(StaticVfxCreateHook?.IsEnabled == false)
        {
            StaticVfxCreateHook?.Enable();
        }
    }

    /// <remarks>
    ///     Already called when you subscribe a delegate to
    ///     <see cref="StaticVfxRunEvent" />.
    /// </remarks>
    public static void EnableRun()
    {
        if(StaticVfxRunHook?.IsEnabled == false)
        {
            StaticVfxRunHook?.Enable();
        }
    }

    /// <remarks>
    ///     Already called when you subscribe a delegate to
    ///     <see cref="StaticVfxDtorEvent" />.
    /// </remarks>
    public static void EnableDtor()
    {
        if(StaticVfxDtorHook?.IsEnabled == false)
        {
            StaticVfxDtorHook?.Enable();
        }
    }

    /// <remarks>
    ///     Already called in <see cref="Dispose()" />.
    /// </remarks>
    public static void DisableCreate()
    {
        if(StaticVfxCreateHook?.IsEnabled == true)
        {
            StaticVfxCreateHook?.Disable();
        }
    }

    /// <remarks>
    ///     Already called in <see cref="Dispose()" />.
    /// </remarks>
    public static void DisableRun()
    {
        if(StaticVfxRunHook?.IsEnabled == true)
        {
            StaticVfxRunHook?.Disable();
        }
    }

    /// <remarks>
    ///     Already called in <see cref="Dispose()" />.
    /// </remarks>
    public static void DisableDtor()
    {
        if(StaticVfxDtorHook?.IsEnabled == true)
        {
            StaticVfxDtorHook?.Disable();
        }
    }

    /// <remarks>
    ///     Already called in <see cref="ECommons.ECommonsMain.Dispose()" />.
    /// </remarks>
    public static void Dispose()
    {
        if(StaticVfxCreateHook != null)
        {
            PluginLog.Information("Disposing StaticVfx Create Hook");
            DisableCreate();
            if(!StaticVfxCreateHook.IsDisposed)
            {
                StaticVfxCreateHook?.Dispose();
            }

            StaticVfxCreateHook = null;
        }

        if(StaticVfxRunHook != null)
        {
            PluginLog.Information("Disposing StaticVfx Run Hook");
            DisableRun();
            if(!StaticVfxRunHook.IsDisposed)
            {
                StaticVfxRunHook?.Dispose();
            }

            StaticVfxRunHook = null;
        }

        if(StaticVfxDtorHook != null)
        {
            PluginLog.Information("Disposing StaticVfx Dtor Hook");
            DisableDtor();
            if(!StaticVfxDtorHook.IsDisposed)
            {
                StaticVfxDtorHook?.Dispose();
            }

            StaticVfxDtorHook = null;
        }
    }
}