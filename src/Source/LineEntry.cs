using System;
using Memoria.Data;

namespace Memoria.EchoS
{
    public struct LineEntry
    {
        public bool IsVerbal => Text != null;

        public string Path;

        public int ChainId;

        public BattleVoice.BattleMoment[] When;

        public BattleSpeakerEx Speaker;

        public BattleSpeakerEx[] With;

        public BattleSpeakerEx Target;

        public int Priority;

        public float Weight;

        public int[] BattleIds;

        public bool BattleIdIsBlacklist;

        public int ScenarioMin;

        public int ScenarioMax;

        public BattleCommandId[] CommandId;

        public BattleAbilityId[] Abilities;

        public bool CommandIdIsBlacklist;
        public bool AbilitiesIsBlacklist;

        public BattleStatusId[] Statuses;

        public BattleCalcFlags ContextFlags;

        public LineEntryFlag Flags;

        public RegularItem[] Items;

        public string Text;
    }
}
