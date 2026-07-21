using System.Reflection;
using Memoria.Data;

namespace Memoria.EchoS
{
    public static class BattleMomentEx
    {
        public static BattleVoice.BattleMoment Chain => (BattleVoice.BattleMoment)1000;
        public static BattleVoice.BattleMoment Custom => (BattleVoice.BattleMoment)1001;
        public static BattleVoice.BattleMoment KillEffect => (BattleVoice.BattleMoment)1002;
        public static BattleVoice.BattleMoment MissEffect => (BattleVoice.BattleMoment)1003;
        public static BattleVoice.BattleMoment DodgeEffect => (BattleVoice.BattleMoment)1004;
        public static BattleVoice.BattleMoment StealSuccess => (BattleVoice.BattleMoment)1005;
        public static BattleVoice.BattleMoment StealFail => (BattleVoice.BattleMoment)1006;
        public static BattleVoice.BattleMoment StealEmpty => (BattleVoice.BattleMoment)1007;
        public static BattleVoice.BattleMoment EatSuccess => (BattleVoice.BattleMoment)1008;
        public static BattleVoice.BattleMoment EatBad => (BattleVoice.BattleMoment)1009;
        public static BattleVoice.BattleMoment EatFail => (BattleVoice.BattleMoment)1010;
        public static BattleVoice.BattleMoment EatCannot => (BattleVoice.BattleMoment)1011;
        public static BattleVoice.BattleMoment VictoryPoseSurvivor => (BattleVoice.BattleMoment)1012;
        public static BattleVoice.BattleMoment TranceEnter => (BattleVoice.BattleMoment)1013;
        public static BattleVoice.BattleMoment TranceLeave => (BattleVoice.BattleMoment)1014;
        public static BattleVoice.BattleMoment ChangeFront => (BattleVoice.BattleMoment)1015;
        public static BattleVoice.BattleMoment ChangeBack => (BattleVoice.BattleMoment)1016;

        public static readonly PropertyInfo[] Properties = typeof(BattleMomentEx).GetProperties();

        public static string ToString(BattleVoice.BattleMoment battleMoment)
        {
            if (battleMoment < Chain)
            {
                return battleMoment.ToString();
            }

            foreach (PropertyInfo propertyInfo in Properties)
            {
                if (battleMoment == (BattleVoice.BattleMoment)propertyInfo.GetValue(null, null))
                {
                    return propertyInfo.Name;
                }
            }

            return "Invalid Moment";
        }
    }
}
