using Memoria.Data;

namespace Memoria.EchoS
{
    public struct StatusEventData
    {
        public BattleUnit statusedChar;

        public BattleCalculator calc;

        public BattleStatusId status;

        public BattleVoice.BattleMoment when;
    }
}
