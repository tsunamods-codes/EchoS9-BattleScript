using Global.Sound.SaXAudio;
using Memoria.Assets;
using Memoria.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;
using static Memoria.Data.BattleVoice;
using Line = System.Collections.Generic.KeyValuePair<System.Int32, Memoria.Data.BattleVoice.BattleMoment>;

namespace Memoria.EchoS
{
    public static class BattleSystem
    {
        public static UInt32 Flags = 0;

        public static LineEntry[] Lines = null;
        public static Int32 CustomLineStart;

        public static Queue<Line> LinesQueue = new Queue<Line>();

        public static Int32[] PlayedLines = new Int32[512];
        public static Int32 PlayedLinesPos;
        public static Int32 PlayedLinesCount;

        public static BattleCalculator OnDeathCalc;
        public static BattleCalculator PerformingCalc;
        public static Dictionary<BattleCommand, List<StatusEventData>> StatusEvents = new Dictionary<BattleCommand, List<StatusEventData>>();

        public static Int32 CurrentPlayingDialog = -1;
        public static Int32 CurrentPlayingChain = -1;

        public static Boolean HasFirstActHappened = false;

        public static HashSet<BTL_DATA> InTranceCharacters = new HashSet<BTL_DATA>();

        public static Boolean IsPreemptive => FF9StateSystem.Battle.FF9Battle.btl_scene.Info.StartType == battle_start_type_tags.BTL_START_FIRST_ATTACK;
        public static Boolean IsBackAttack => FF9StateSystem.Battle.FF9Battle.btl_scene.Info.StartType == battle_start_type_tags.BTL_START_BACK_ATTACK;
        public static Boolean CanPlayMoreLines => CurrentPlayingDialog < 0;

        public delegate Boolean LineEntryPredicate(Int32 lineId, BattleMoment when);

        public static UInt32 GetFlags(BattleCalculator calc)
        {
            UInt32 flags = Flags;
            if (calc.Command.IsManyTarget)
                flags |= (UInt32)LineEntryFlag.Multi;
            else
            {
                flags |= (UInt32)LineEntryFlag.Single;

                if (calc.Target.Id == calc.Caster.Id)
                    flags |= (UInt32)LineEntryFlag.Self;
            }

            if ((calc.Target.Flags & CalcFlag.Critical) != 0)
                flags |= (UInt32)LineEntryFlag.Crit;
            if ((calc.Target.Flags & CalcFlag.HpAlteration) != 0)
                flags |= (UInt32)LineEntryFlag.Hp;
            if ((calc.Target.Flags & CalcFlag.MpAlteration) != 0)
                flags |= (UInt32)LineEntryFlag.Mp;

            if ((calc.Context.Flags & BattleCalcFlags.Dodge) != 0)
                flags |= (UInt32)LineEntryFlag.Dodge;
            else if ((calc.Context.Flags & BattleCalcFlags.Miss) != 0)
                flags |= (UInt32)LineEntryFlag.Miss;
            else
                flags |= (UInt32)LineEntryFlag.Hit;

            return flags;
        }

        private static Boolean IsBattleIdMatched(Int32[] battleIds, Int32 currentBattleId)
        {
            // If BattleId array is null or empty, it matches any battle (backward compatibility for -1)
            if (battleIds == null || battleIds.Length == 0)
                return true;

            // If any element is -1, it matches any battle
            for (Int32 i = 0; i < battleIds.Length; i++)
            {
                if (battleIds[i] == -1)
                    return true;
            }

            const Int32 excludedBattleIdOffset = 1000000;
            Boolean hasInclude = false;
            Boolean includeMatched = false;

            for (Int32 i = 0; i < battleIds.Length; i++)
            {
                Int32 battleId = battleIds[i];

                // Excluded BattleId values are encoded as -(id + excludedBattleIdOffset)
                if (battleId <= -excludedBattleIdOffset)
                {
                    Int32 excludedBattleId = -battleId - excludedBattleIdOffset;
                    if (excludedBattleId == currentBattleId)
                        return false;
                    continue;
                }

                if (battleId > 0)
                {
                    hasInclude = true;
                    if (battleId == currentBattleId)
                        includeMatched = true;
                }
            }

            if (hasInclude)
                return includeMatched;

            // Backward compatibility: no includes means match-all unless excluded above
            return true;
        }

        public static Boolean CommonChecks(Int32 i, BattleMoment when, UInt32 flags, BattleUnit speaker, BattleStatusId statusException = BattleStatusId.None)
        {
            // [SPECIAL] Black Waltz 3: Vivi lines not tagged with the right BattleId are ignored
            if (FF9StateSystem.Battle.battleMapIndex == 296 && Lines[i].Speaker.playerId == CharacterId.Vivi && !IsBattleIdMatched(Lines[i].BattleId, 296))
                return false;

            if (!Lines[i].When.Contains(when))
                return false;

            if ((~Lines[i].Flags & flags) != 0)
                return false;

            if (!IsBattleIdMatched(Lines[i].BattleId, FF9StateSystem.Battle.battleMapIndex))
                return false;

            if (Lines[i].ScenarioMin > 0 && GameState.ScenarioCounter < Lines[i].ScenarioMin)
                return false;

            if (Lines[i].ScenarioMax > 0 && GameState.ScenarioCounter > Lines[i].ScenarioMax)
                return false;

            if (speaker != null)
            {
                if (Lines[i].Speaker.CheckIsPlayer)
                {
                    if (!Lines[i].Speaker.CheckIsCharacter(speaker))
                        return false;
                }
                // TODO check all With
                else if (Lines[i].With != null && !Lines[i].With[0].CheckIsCharacter(speaker))
                    return false;
            }

            // Check if speaker can talk
            if (statusException == BattleStatusId.None)
                statusException = BattleStatusId.Jump;

            if (!CanTalk(Lines[i].Speaker, AdjustPriority(when, Lines[i].Priority), statusException))
                return false;

            // Check if all speakers in the chain can talk
            Int32 chainId = Lines[i].ChainId;
            while (chainId >= 0)
            {
                // [SPECIAL] Black Waltz 3: Vivi lines not tagged with the right BattleId are ignored
                if (FF9StateSystem.Battle.battleMapIndex == 296 && Lines[chainId].Speaker.playerId == CharacterId.Vivi && !IsBattleIdMatched(Lines[chainId].BattleId, 296))
                    return false;

                if (!CanTalk(Lines[chainId].Speaker, AdjustPriority(when, Lines[i].Priority), statusException))
                    return false;
                chainId = Lines[chainId].ChainId;
            }

            // Check for required player
            if (Lines[i].With != null)
            {
                Boolean found = false;
                foreach (BattleSpeakerEx with in Lines[i].With)
                {
                    BattleUnit unit = with.FindBattleUnit();

                    // Check for without
                    if (with.Without && unit != null)
                        continue;

                    // Note: should I not check for Death here?
                    if (unit == null || unit.IsUnderStatus(BattleStatus.Death))
                        continue;
                    if (with.Status != BattleStatusId.None && !unit.IsUnderStatus(with.Status))
                        continue;
                    if (with.CheckCanTalk && !BattleSpeaker.CheckCanSpeak(unit, AdjustPriority(when, Lines[i].Priority), statusException))
                        continue;

                    found = true;
                    break;
                }

                if (!found)
                    return false;
            }

            return true;
        }

        public static Int32 GetRandomLine(BattleMoment moment, LineEntryPredicate filter)
        {
            Int32 currentPriority = Int32.MinValue;
            Dictionary<Int32, Single> weights = new Dictionary<Int32, Single>();
            Single totalWeight = 0;
            String selectedLines = "";
            for (Int32 i = 0; i < CustomLineStart; i++)
            {
                // Chains cannot be interrupted
                if (CurrentPlayingChain >= 0 && Lines[CurrentPlayingChain].Speaker.Equals(Lines[i].Speaker))
                    continue;

                Int32 priority = AdjustPriority(moment, Lines[i].Priority);
                if (priority < currentPriority)
                    continue;
                if (!filter(i, moment))
                    continue;

                // Reduce weights of already played lines
                Single weight = Lines[i].Weight;
                if (weight >= 0)
                {
                    for (Int32 j = 0; j < PlayedLinesCount; j++)
                    {
                        // Get the position in the circular buffer
                        Int32 p = (PlayedLinesPos - 1 - j) % PlayedLines.Length;
                        if (PlayedLines[p] == i)
                        {
                            Single f = Mathf.Min(0.5f, j / 20f);
                            weight *= f;
                        }
                    }
                }
                else
                    weight = -weight; // negative weight mean fixed weight

                if (priority > currentPriority)
                {
                    currentPriority = priority;
                    weights.Clear();
                    totalWeight = 0;
                    selectedLines = "";
                }
                weights[i] = weight;
                totalWeight += weight;
                selectedLines += $"['{Lines[i].Path}'({weight})] ";
            }
            if (totalWeight == 0)
                return -1;

            // To avoid repeating a single line too much do a coin toss
            // TODO: remove when enough lines?
            if (weights.Count == 1 && totalWeight < 1f)
                totalWeight *= 3f;

            Single rng = UnityEngine.Random.Range(0, totalWeight);

            Log.Message($"Selected lines ({weights.Count}): {selectedLines}RNG: {rng}/{totalWeight}");

            totalWeight = 0;
            foreach (Int32 i in weights.Keys)
            {
                totalWeight += weights[i];
                if (rng <= totalWeight)
                    return i;
            }
            return -1;
        }

        public static void QueueLine(Int32 i, BattleMoment when)
        {
            if (i < 0)
                return;

            Boolean queueEmpty = LinesQueue.Count == 0;
            Int32 chainId = -1;
            if (!Lines[i].IsVerbal)
            {
                chainId = Lines[i].ChainId;
                while (chainId >= 0)
                {
                    LinesQueue.Enqueue(new Line(chainId, BattleMomentEx.Chain));
                    chainId = Lines[chainId].ChainId;
                }
                if (queueEmpty)
                {
                    // If queue is empty we need to start a chain
                    LinesQueue.Enqueue(new Line(i, when));
                    PlayLine(i, when, PlayNextLine);
                }
                else
                    // A chain is already going, we play the line on top of it
                    PlayLine(i, when);
                return;
            }

            // TODO: priority?

            if (LinesQueue.Count > 2)
            {
                Log.Debug($"Line '{Lines[i].Path}' ignored. Queue full");
                return;
            }

            // We queue the line and its chain
            LinesQueue.Enqueue(new Line(i, when));
            chainId = Lines[i].ChainId;
            while (chainId >= 0)
            {
                LinesQueue.Enqueue(new Line(chainId, BattleMomentEx.Chain));
                chainId = Lines[chainId].ChainId;
            }

            if (queueEmpty)
                // We start a chain going
                PlayLine(i, when, PlayNextLine);
            else
                // No need to start a chain
                Log.Debug($"Queueing '{Lines[i].Path}'");
        }

        private static void PlayNextLine()
        {
            LinesQueue.Dequeue();
            if (LinesQueue.Count == 0)
            {
                Log.Debug($"Chain ended");
                return;
            }

            Line line = LinesQueue.Peek();
            Log.Debug($"Playing next line: {Lines[line.Key].Path}");
            PlayLine(line.Key, line.Value, PlayNextLine);
        }

        private static void PlayLine(Int32 i, BattleMoment when, Action onFinishedPlaying = null)
        {
            // Verbal lines must wait for all lines to be finished playing
            if (Lines[i].IsVerbal && (CurrentPlayingDialog >= 0 || GetPlayingVoicesCount() > 0))
            {
                new Thread(() =>
                {
                    Thread.Sleep(100);
                    Int32 timeout = 3000;
                    while (true)
                    {
                        Thread.Sleep(50);
                        timeout -= 50;

                        if (timeout < 0 || CurrentPlayingDialog >= 0)
                        {
                            Log.Debug($"Cancelled queued line '{Lines[i].Path}' ({(timeout < 0 ? "timeout" : "dialog")})");
                            onFinishedPlaying?.Invoke();
                            return;
                        }

                        if (GetPlayingVoicesCount() == 0)
                        {
                            PlayLineNow(i, when, onFinishedPlaying);
                            return;
                        }
                    }
                }).Start();
                return;
            }

            PlayLineNow(i, when, onFinishedPlaying);
        }

        private static void PlayLineNow(Int32 i, BattleMoment when, Action onFinishedPlaying)
        {
            BattleUnit speaker = null;
            // Note: 'With' becomes the speaker when 'Speaker' is not checked for speech
            if (!Lines[i].Speaker.CheckCanTalk && Lines[i].With != null)
            {
                BattleUnit unit = null;
                foreach (BattleSpeakerEx with in Lines[i].With)
                {
                    if (with.CheckCanTalk)
                    {
                        unit = with.FindBattleUnit();
                        if (unit != null)
                        {
                            speaker = unit;
                            break;
                        }
                    }
                }
            }
            else
                speaker = Lines[i].Speaker.FindBattleUnit();

            if (speaker == null)
            {
                Log.Debug($"Couldn't find battle unit '{Lines[i].Speaker}'");
                onFinishedPlaying?.Invoke();
                return;
            }

            String path = Lines[i].Path;
            Boolean applyTranceEffect = false;
            if (InTranceCharacters.Contains(speaker) && when != BattleMomentEx.TranceEnter && when != BattleMomentEx.TranceLeave)
            {
                string trancePath = path.Replace("/", "(Trance)/");
                string text = "Voices/" + Localization.CurrentSymbol + "/Battle/" + trancePath;
                if (AssetManager.HasAssetOnDisc("Sounds/" + text + ".akb", includeAssetPath: true, includeAssetExtension: true)
                 || AssetManager.HasAssetOnDisc("Sounds/" + text + ".ogg", includeAssetPath: true, includeAssetExtension: false))
                {
                    path = trancePath;
                }
                else
                    applyTranceEffect = true;
            }

            Log.Debug($"Starting '{path}'{(onFinishedPlaying == PlayNextLine ? " with a chain" : "")}");

            AddToPlayedLines(i);
            Int32 soundID = PlayVoice(speaker, $"Battle/{path}", AdjustPriority(when, Lines[i].Priority), () =>
            {
                onFinishedPlaying?.Invoke();
                BattleSubtitles.Instance.Hide(speaker.Id, $"“{Lines[i].Text}”");
            });

            if (soundID <= 0)
            {
                onFinishedPlaying?.Invoke();
                return;
            }

            if (speaker.IsPlayer && applyTranceEffect)
            {
                EffectPreset preset = AudioEffectManager.GetUnlistedPreset($"Trance{speaker.PlayerIndex}");
                if (preset != null)
                    AudioEffectManager.ApplyPresetOnSound(preset, soundID, path);
            }

            // Apply Mini speed effect
            if (speaker.IsPlayer && speaker.IsUnderStatus(BattleStatus.Mini))
                ISdLibAPIProxy.Instance.SdSoundSystem_SoundCtrl_SetPitch(soundID, 1.25f, 0);

            BattleSubtitles.Instance.Show(speaker, $"“{Lines[i].Text}”");
        }

        private static Boolean CanTalk(BattleSpeakerEx speaker, Int32 priority, BattleStatusId statusException)
        {
            BattleUnit unit = speaker.FindBattleUnit();
            if (unit == null)
                return false;
            if (speaker.Status != BattleStatusId.None && !unit.IsUnderStatus(speaker.Status))
                return false;
            if (speaker.CheckCanTalk && !BattleSpeaker.CheckCanSpeak(unit, priority, statusException))
                return false;
            return true;
        }

        private static Int32 AdjustPriority(BattleMoment moment, Int32 priority)
        {
            // Adjust priorities
            if (moment == BattleMoment.VictoryPose)
                return priority + 2000;
            if ((moment >= BattleMoment.BattleStart && moment <= BattleMoment.EnemyEscape)
                || moment == BattleMomentEx.VictoryPoseSurvivor
                || moment == BattleMomentEx.TranceEnter
                || moment == BattleMomentEx.TranceLeave)
                return priority + 1000;
            if (moment == BattleMomentEx.KillEffect || moment == BattleMomentEx.MissEffect || moment == BattleMomentEx.DodgeEffect)
                return priority + 100;
            if (moment == BattleMoment.Added || moment == BattleMoment.Removed)
                return priority + 100;
            if (moment >= BattleMoment.Damaged && moment <= BattleMoment.Missed)
                return priority - 100;
            return priority;
        }

        private static void AddToPlayedLines(Int32 i)
        {
            PlayedLines[PlayedLinesPos] = i;
            PlayedLinesPos = (PlayedLinesPos + 1) % PlayedLines.Length;
            if (PlayedLinesCount < PlayedLines.Length) PlayedLinesCount++;
        }
    }

    public class BattleSpeakerEx : BattleSpeaker
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
                Log.Debug($"[CheckIsCharacter] {Status} {isCharacter}");
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

    public static class BattleMomentEx
    {
        public static BattleMoment Chain => (BattleMoment)1000;
        public static BattleMoment Custom => (BattleMoment)1001;

        public static BattleMoment KillEffect => (BattleMoment)1002;
        public static BattleMoment MissEffect => (BattleMoment)1003;
        public static BattleMoment DodgeEffect => (BattleMoment)1004;

        public static BattleMoment StealSuccess => (BattleMoment)1005;
        public static BattleMoment StealFail => (BattleMoment)1006;
        public static BattleMoment StealEmpty => (BattleMoment)1007;

        public static BattleMoment EatSuccess => (BattleMoment)1008;
        public static BattleMoment EatBad => (BattleMoment)1009;
        public static BattleMoment EatFail => (BattleMoment)1010;
        public static BattleMoment EatCannot => (BattleMoment)1011;

        public static BattleMoment VictoryPoseSurvivor => (BattleMoment)1012;

        public static BattleMoment TranceEnter => (BattleMoment)1013;
        public static BattleMoment TranceLeave => (BattleMoment)1014;

        public static BattleMoment ChangeFront => (BattleMoment)1015;
        public static BattleMoment ChangeBack => (BattleMoment)1016;

        public static PropertyInfo[] Properties = typeof(BattleMomentEx).GetProperties();

        public static String ToString(BattleMoment battleMoment)
        {
            if (battleMoment < Chain)
                return battleMoment.ToString();

            foreach (PropertyInfo property in Properties)
            {
                if (battleMoment == (BattleMoment)property.GetValue(null, null))
                    return property.Name;

            }
            return "Invalid Moment";
        }
    }

    public struct StatusEventData
    {
        public BattleUnit statusedChar;
        public BattleCalculator calc;
        public BattleStatusId status;
        public BattleMoment when;
    }

    public struct LineEntry
    {
        public String Path;
        public Int32 ChainId;

        public BattleMoment[] When;
        public BattleSpeakerEx Speaker;
        public BattleSpeakerEx[] With;
        public BattleSpeakerEx Target;
        public Int32 Priority;
        public Single Weight;

        public Int32[] BattleId;

        public Int32 ScenarioMin;
        public Int32 ScenarioMax;

        public BattleCommandId[] CommandId;
        public BattleAbilityId[] Abilities;
        public BattleStatusId[] Statuses;

        public BattleCalcFlags ContextFlags;
        public UInt32 Flags;

        public RegularItem[] Items;

        public String Text;
        public Boolean IsVerbal => Text != null;
    }

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
