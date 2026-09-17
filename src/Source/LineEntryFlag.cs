using System;

namespace Memoria.EchoS
{
    [Flags]
    public enum LineEntryFlag : UInt32
    {
        None = 0,

        PlayerSolo = 1 << 0,
        PlayerTeam = 1 << 1,

        EnemySolo = 1 << 2,
        EnemyTeam = 1 << 3,

        Self = 1 << 4,
        Enemy = 1 << 5,
        Ally = 1 << 6,
        Single = 1 << 7,
        Multi = 1 << 8,

        Hit = 1 << 9,
        Miss = 1 << 10,
        Dodge = 1 << 11,
        Crit = 1 << 12,

        Hp = 1 << 13,
        Mp = 1 << 14,

        FrontAttack = 1 << 15,
        Preemptive = 1 << 16,
        BackAttack = 1 << 17,

        FriendlyBattle = 1 << 18,
        NonFriendlyBattle = 1 << 19,

        Serious = 1 << 20,
        Boss = 1 << 21
    }
}
