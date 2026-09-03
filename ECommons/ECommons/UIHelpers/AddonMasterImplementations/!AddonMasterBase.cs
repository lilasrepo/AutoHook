using ECommons.Automation.UIInput;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using AtkEvent = FFXIVClientStructs.FFXIV.Component.GUI.AtkEvent;

namespace ECommons.UIHelpers.AddonMasterImplementations;
public abstract unsafe class AddonMasterBase<T> : IAddonMasterBase where T : unmanaged
{
    protected AddonMasterBase(nint addon)
    {
        Addon = (T*)addon;
    }
    protected AddonMasterBase(void* addon)
    {
        Addon = (T*)addon;
    }

    /// <summary>
    /// User-friendly description, for use in plugin settings, etc.
    /// </summary>
    public abstract string AddonDescription { get; }
    public T* Addon { get; }
    public AtkUnitBase* Base => (AtkUnitBase*)Addon;
    public bool IsVisible => Base->IsVisible;
    public bool IsAddonReady => GenericHelpers.IsAddonReady(Base);

    public bool HasFocus
    {
        get
        {
            var focus = AtkStage.Instance()->GetFocus();
            if(focus == null) return false;
            for(var i = 0; i < RaptureAtkUnitManager.Instance()->FocusedUnitsList.Count; i++)
            {
                var atk = RaptureAtkUnitManager.Instance()->FocusedUnitsList.Entries[i].Value;
                if(atk != null && atk->RootNode == GenericHelpers.GetRootNode(focus))
                    return true;
            }
            return false;
        }
    }

    public bool IsAddonInFocusList
    {
        get
        {
            for(var i = 0; i < RaptureAtkUnitManager.Instance()->FocusedUnitsList.Count; i++)
            {
                var atk = RaptureAtkUnitManager.Instance()->FocusedUnitsList.Entries[i].Value;
                if(atk != null && atk == Base) return true;
            }
            return false;
        }
    }

    [Obsolete("For the intended functionality please use HasFocus. For the same functionality please use IsAddonInFocusList.")]
    public bool IsAddonFocused => IsAddonInFocusList;
    public bool IsAddonOnlyFocusListEntry => RaptureAtkUnitManager.Instance()->FocusedUnitsList.Count == 1 && RaptureAtkUnitManager.Instance()->FocusedUnitsList.Entries[0].Value == Base;

    // porting-note(TC game 7.20): every button property in this file's siblings is
    // `Addon->GetComponentButtonById(<literal>)`, and those literals track the CURRENT
    // international UI layout. On the older TC layout a node id frequently does not exist, so the
    // getter hands back null and the dereference below throws. Because these calls sit inside
    // per-frame TaskManager work, one missing node turns into an exception every tick and the
    // automation wedges -- which is exactly how the 2026-08-04 ICE regression presented
    // (WKSMission node 17 vs TC's 13). A null button means "nothing to click", not "crash".
    protected bool ClickButtonIfEnabled(AtkComponentButton* button, bool respectHoldButtons = false)
    {
        if(button == null) return false;
        if(button->IsEnabled && button->AtkResNode->IsVisible()
            && (!respectHoldButtons || button->GetComponentType() != ComponentType.HoldButton))
        {
            button->ClickAddonButton(Base);
            return true;
        }
        return false;
    }

    protected bool ClickButtonIfEnabled(AtkComponentRadioButton* button)
    {
        if(button == null) return false;
        if(button->IsEnabled && button->AtkResNode->IsVisible())
        {
            button->ClickRadioButton(Base);
            return true;
        }
        return false;
    }

    protected bool ClickCheckboxIfEnabled(AtkComponentCheckBox* checkbox)
    {
        if(checkbox == null) return false;
        if(checkbox->IsEnabled && checkbox->AtkResNode->IsVisible())
        {
            checkbox->ClickCheckBox(Base);
            checkbox->SetChecked(true);
            return true;
        }
        return false;
    }

    protected AtkEvent CreateAtkEvent(byte flags = 0)
    {
        var ret = stackalloc AtkEvent[]
        {
            new()
            {
                Listener = (AtkEventListener*)Base,
                Target = &AtkStage.Instance()->AtkEventTarget,
                State = new()
                {
                    StateFlags = (AtkEventStateFlags)flags
                }
            }
        };
        return *ret;
    }

    protected AtkEventDataBuilder CreateAtkEventData()
    {
        return new();
    }
}

public abstract unsafe class AddonMasterBase : AddonMasterBase<AtkUnitBase>
{
    protected AddonMasterBase(nint addon) : base(addon)
    {
    }

    protected AddonMasterBase(void* addon) : base(addon)
    {
    }
}

public unsafe interface IAddonMasterBase
{
    string AddonDescription { get; }
    unsafe AtkUnitBase* Base { get; }
    bool HasFocus { get; }
    bool IsAddonInFocusList { get; }
    bool IsAddonOnlyFocusListEntry { get; }
    bool IsAddonReady { get; }
    bool IsVisible { get; }
}
