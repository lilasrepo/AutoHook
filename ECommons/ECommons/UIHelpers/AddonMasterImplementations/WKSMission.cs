using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;
using Callback = ECommons.Automation.Callback;


namespace ECommons.UIHelpers.AddonMasterImplementations;

public partial class AddonMaster
{
    /// <summary>
    /// Space Exploration Mission Screen <br></br>
    /// Details all the missions that you can pick up/have done
    /// </summary>
    public unsafe partial class WKSMission : AddonMasterBase<AtkUnitBase>
    {
        public WKSMission(nint addon) : base(addon) { }
        public WKSMission(void* addon) : base(addon) { }

        public AtkComponentButton* HelpButton => Addon->GetComponentButtonById(7);
        public AtkComponentButton* MissionSelectionButton => Addon->GetComponentButtonById(8);
        public AtkComponentButton* MissionLogButton => Addon->GetComponentButtonById(9);
        public AtkComponentButton* BasicMissionsButton => Addon->GetComponentButtonById(13);
        public AtkComponentButton* ProvisionalMissionsButton => Addon->GetComponentButtonById(14);
        public AtkComponentButton* CriticalMissionsButton => Addon->GetComponentButtonById(15);

        /// <summary>
        /// Keeps the current number of missions that are displayed. <br></br>
        /// This includes the tabs seperating the missions by type [A, B, C, D]
        /// </summary>
        public uint NumEntries => Addon->AtkValues[31].UInt;

        public uint CurrentTab => Addon->AtkValues[27].UInt;

        public uint SelectedMissionId => Addon->AtkValues[1061].UInt;
        public string SelectedMissionName
        {
            get
            {
                var missionName = Addon->AtkValues[1062];
                if(missionName.Type.EqualsAny(AtkValueType.String, AtkValueType.ManagedString, AtkValueType.String8))
                {
                    return MemoryHelper.ReadSeStringNullTerminated((nint)missionName.String.Value).GetText();
                }
                return "n/a";
            }
        }

        public StellarMissions[] StellerMissions
        {
            get
            {
                var ret = new List<StellarMissions>();
                for(var i = 0; i < NumEntries; i++)
                {
                    var missionName = Addon->AtkValues[802 + (i * 2)];
                    var missionId = Addon->AtkValues[40 + (i * 6)].UInt;

                    // category header?
                    if(missionId == 0)
                        continue;

                    if(missionName.Type.EqualsAny(AtkValueType.String, AtkValueType.ManagedString, AtkValueType.String8))
                    {
                        var mission = new StellarMissions(this, i)
                        {
                            Name = MemoryHelper.ReadSeStringNullTerminated((nint)missionName.String.Value).GetText(),
                            MissionId = missionId
                        };
                        ret.Add(mission);
                    }
                    else
                    {
                        break;
                    }
                }
                return [.. ret];
            }
        }

        public class StellarMissions(WKSMission master, int index)
        {
            public string Name { get; set; } = string.Empty;
            public uint MissionId;

            public void Select()
            {
                Callback.Fire(master.Base, true, 12, (int)MissionId, index);
            }
            public void Initiate()
            {
                Callback.Fire(master.Base, true, 13, (int)MissionId, index);
            }
        }

        public class ClassDropdown(WKSMission master, int index)
        {
            public void Select()
            {
                Callback.Fire(master.Base, true, 11, index);
            }
        }

        // porting-note(TC game 7.20): CurrentTab / NumClasses / SelectClass arrived with the 7.5
        // addon layout, so their AtkValue indices have no verified TC equivalent (the surrounding
        // indices did NOT shift uniformly -- NumEntries moved 31->33 while the per-entry array
        // start moved 40->36, so they cannot be inferred either). ICE only reads SelectClass from
        // a debug tab, so they are left at the upstream indices and merely bounded, rather than
        // guessed at. Treat any value they return on TC as meaningless.
        public uint NumClasses => Addon->AtkValues[1].UInt;

        public ClassDropdown[] SelectClass
        {
            get
            {
                var ret = new List<ClassDropdown>();
                var available = Addon->AtkValuesCount > 13 ? (uint)(Addon->AtkValuesCount - 13) : 0u;
                var count = (int)(NumClasses < available ? NumClasses : available);
                for (int i = 0; i < count; i++)
                {
                    int value = Addon->AtkValues[13 + i].Int;

                    var jobSelect = new ClassDropdown(this, value);
                    ret.Add(jobSelect);
                }

                return [.. ret];
            }
        }

        public override string AddonDescription => "Steller Missions Ui";

        public void Help() => ClickButtonIfEnabled(HelpButton);
        public void MissionSelection() => ClickButtonIfEnabled(MissionSelectionButton);
        public void MissionLog() => ClickButtonIfEnabled(MissionLogButton);
        public void BasicMissions() => ClickButtonIfEnabled(BasicMissionsButton);
        public void ProvisionalMissions() => ClickButtonIfEnabled(ProvisionalMissionsButton);
        public void CriticalMissions() => ClickButtonIfEnabled(CriticalMissionsButton);
    }
}
