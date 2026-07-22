using Memoria.Data;
using System;
using static Memoria.Data.BattleVoice;

namespace Memoria.EchoS
{
    public class BattleSpeakerEx : BattleVoice.BattleSpeaker
    {
        public BattleStatusId Status = BattleStatusId.None;
        public Boolean CheckCanTalk = true;
        public Boolean CheckIsPlayer = true;
        public Boolean Without = false;

        public new bool Equals(BattleSpeaker speaker)
        {
            if (speaker.playerId >= 0 || playerId >= 0)
                return speaker.playerId == playerId;

            if (speaker.enemyModelId != enemyModelId)
                return false;

            return speaker.enemyBattleId == -1 || enemyBattleId == -1 || speaker.enemyBattleId == enemyBattleId;
        }

        public Boolean CheckIsCharacter(BattleUnit unit)
        {
            if (!CheckIsPlayer) return true;

            Boolean isCharacter = (playerId == CharacterId.NONE && enemyModelId == -1 && enemyBattleId == -1) ? true : base.CheckIsCharacter(unit.Data);
            if (isCharacter && Status != BattleStatusId.None)
            {
                isCharacter = unit.IsUnderAnyStatus(Status);
                LogEchoS.Debug($"[CheckIsCharacter] {Status} {isCharacter}");
            }

            if (Without) isCharacter = !isCharacter;

            return isCharacter;
        }

        public override string ToString()
        {
            String status = Status != BattleStatusId.None ? $":{Status}" : "";
            String pre = $"{(!CheckCanTalk ? "$" : "")}{(!CheckIsPlayer ? "!" : "")}{(Without ? "\\" : "")}";
            if (playerId != CharacterId.NONE)
                return $"{pre}{playerId}{status}";
            return $"{pre}{(enemyBattleId >= 0 ? enemyBattleId.ToString() : "")}:{(enemyModelId >= 0 ? enemyModelId.ToString() : "")}{status}";
        }
    }
}
