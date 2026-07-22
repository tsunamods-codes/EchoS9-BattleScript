using Assets.Sources.Scripts.UI.Common;
using Global.Sound.SaXAudio;
using Memoria.Assets;
using Memoria.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Memoria.Data.BattleVoice;
using Line = System.Collections.Generic.KeyValuePair<System.Int32, Memoria.Data.BattleVoice.BattleMoment>;

namespace Memoria.EchoS
{
    public static class BattleSystem
    {
        public static uint Flags = 0U;

        public static LineEntry[] Lines = null;
        public static int CustomLineStart;

        public static Queue<Line> LinesQueue = new Queue<Line>();

        public static int[] PlayedLines = new int[512];
        public static int PlayedLinesPos;
        public static int PlayedLinesCount;

        public static BattleCalculator OnDeathCalc;
        public static BattleCalculator PerformingCalc;
        public static Dictionary<BattleCommand, List<StatusEventData>> StatusEvents = new Dictionary<BattleCommand, List<StatusEventData>>();

        public static int CurrentPlayingDialog = -1;
        public static int CurrentPlayingChain = -1;

        public static bool HasFirstActHappened = false;

        public static HashSet<BTL_DATA> InTranceCharacters = new HashSet<BTL_DATA>();

        public static bool IsPreemptive => FF9StateSystem.Battle.FF9Battle.btl_scene.Info.StartType == battle_start_type_tags.BTL_START_FIRST_ATTACK;

        public static bool IsBackAttack => FF9StateSystem.Battle.FF9Battle.btl_scene.Info.StartType == battle_start_type_tags.BTL_START_BACK_ATTACK;

        public static bool CanPlayMoreLines => CurrentPlayingDialog < 0;

        public delegate bool LineEntryPredicate(int lineId, BattleVoice.BattleMoment when);
        public static Dictionary<int, string> MonsterNameWithoutTag = new Dictionary<int, string>();

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

        public static Boolean CommonChecks(Int32 i, BattleMoment when, UInt32 flags, BattleUnit speaker, BattleStatusId statusException = BattleStatusId.None)
        {
            // [SPECIAL] Black Waltz 3: Vivi lines not tagged with the right BattleId are ignored
            //if (FF9StateSystem.Battle.battleMapIndex == 296 && Lines[i].Speaker.playerId == CharacterId.Vivi && !Lines[i].BattleIds.Contains(296))
                //return false;

            if (!Lines[i].When.Contains(when))
                return false;

            if ((~Lines[i].Flags & (LineEntryFlag)flags) != 0)
                return false;

            if (Lines[i].BattleIds != null && Lines[i].BattleIds.Length > 0)
            {
                if (Lines[i].BattleIdIsBlacklist == Lines[i].BattleIds.Contains(FF9StateSystem.Battle.battleMapIndex))
                    return false;
            }

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
                //if (FF9StateSystem.Battle.battleMapIndex == 296 && Lines[chainId].Speaker.playerId == CharacterId.Vivi && !Lines[chainId].BattleIds.Contains(296))
                    //return false;

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
                        Int32 p = (PlayedLinesPos - 1 - j + PlayedLines.Length) % PlayedLines.Length;
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

            LogEchoS.Message($"Selected lines ({weights.Count}): {selectedLines}RNG: {rng}/{totalWeight}");

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
                LogEchoS.Debug($"Line '{Lines[i].Path}' ignored. Queue full");
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
                LogEchoS.Debug($"Queueing '{Lines[i].Path}'");
        }

        private static void PlayNextLine()
        {
            LinesQueue.Dequeue();
            if (LinesQueue.Count == 0)
            {
                LogEchoS.Debug($"Chain ended");
                return;
            }

            Line line = LinesQueue.Peek();
            LogEchoS.Debug($"Playing next line: {Lines[line.Key].Path}");
            PlayLine(line.Key, line.Value, PlayNextLine);
        }

        private static void PlayLine(int i, BattleVoice.BattleMoment when, Action onFinishedPlaying = null)
        {
            if (Lines[i].IsVerbal && (CurrentPlayingDialog >= 0 || BattleVoice.GetPlayingVoicesCount() > 0))
            {
                if (PersistenSingleton<BattleSubtitles>.Instance != null)
                {
                    PersistenSingleton<BattleSubtitles>.Instance.StartCoroutine(WaitForVoiceRoutine(i, when, onFinishedPlaying));
                }
                else
                {
                    onFinishedPlaying?.Invoke();
                }
                return;
            }
            PlayLineNow(i, when, onFinishedPlaying);
        }

        private static IEnumerator WaitForVoiceRoutine(int i, BattleVoice.BattleMoment when, Action onFinishedPlaying)
        {
            yield return new WaitForSeconds(0.1f);
            float timeout = 3.0f;

            while (timeout > 0)
            {
                yield return new WaitForSeconds(0.05f);
                timeout -= 0.05f;

                if (CurrentPlayingDialog >= 0) break;

                if (BattleVoice.GetPlayingVoicesCount() == 0)
                {
                    PlayLineNow(i, when, onFinishedPlaying);
                    yield break;
                }
            }

            LogEchoS.Message(string.Format("Cancelled queued line '{0}' ({1})", Lines[i].Path, (timeout <= 0) ? "timeout" : "dialog"));
            onFinishedPlaying?.Invoke();
        }

        private static IEnumerator WaitAndHideRoutine(int speakerId, string text, float seconds, Action onFinished)
        {
            yield return new WaitForSeconds(seconds);
            if (PersistenSingleton<BattleSubtitles>.Instance != null)
            {
                string displayText = text != null ? "“" + text + "”" : "";
                PersistenSingleton<BattleSubtitles>.Instance.Hide((ushort)speakerId, displayText);
            }
            onFinished?.Invoke();
        }

        private static void PlayLineNow(int i, BattleVoice.BattleMoment when, Action onFinishedPlaying)
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
            {
                speaker = Lines[i].Speaker.FindBattleUnit();
            }

            if (speaker != null)
            {
                string path = Lines[i].Path;
                bool isTrance = InTranceCharacters.Contains(speaker.Data);

                if (isTrance && when != (BattleVoice.BattleMoment)BattleMomentEx.TranceEnter && when != (BattleVoice.BattleMoment)BattleMomentEx.TranceLeave)
                {
                    string trancePath = path.Replace("/", "(Trance)/");
                    string fullPath = "Voices/" + Localization.CurrentSymbol + "/Battle/" + trancePath;
                    if (AssetManager.HasAssetOnDisc("Sounds/" + fullPath + ".akb", true, true) || AssetManager.HasAssetOnDisc("Sounds/" + fullPath + ".ogg", true, false))
                        path = trancePath;
                }

                LogEchoS.Message("Starting '" + path + "'" + ((onFinishedPlaying != null) ? " with a chain" : ""));
                AddToPlayedLines(i);

                bool soundStarted = false;
                string displayText = Lines[i].Text != null ? "“" + Lines[i].Text + "”" : "";

                int soundId = PlayVoice(speaker, "Battle/" + path, AdjustPriority(when, Lines[i].Priority), delegate ()
                {
                    if (soundStarted)
                    {
                        onFinishedPlaying?.Invoke();
                        if (PersistenSingleton<BattleSubtitles>.Instance != null)
                            PersistenSingleton<BattleSubtitles>.Instance.Hide(speaker.Id, displayText);
                    }
                });

                if (soundId != -1)
                {
                    soundStarted = true;

                    foreach (BattleStatusId currentStatuses in speaker.CurrentStatus.ToStatusList())
                    {
                        string speakerName = "Unknown";
                        if (speaker.IsPlayer)
                            speakerName = FF9TextTool.CharacterDefaultName(speaker.PlayerIndex);
                        else if (!MonsterNameWithoutTag.TryGetValue(speaker.Id, out speakerName))
                            speakerName = "Unknown";

                        EffectPreset preset = AudioEffectManager.GetUnlistedPreset($"{currentStatuses}{speakerName}");

                        if (preset == null) continue;

                        AudioEffectManager.ApplyPresetOnSound(preset, soundId, path, 0f);
                        break;
                    }

                    // Apply Mini speed effect => [TODO] Maybe add parameters for the preset file ? Or the .ini file ?
                    if (speaker.IsUnderStatus(BattleStatus.Mini))
                        ISdLibAPIProxy.Instance.SdSoundSystem_SoundCtrl_SetPitch(soundId, 1.25f, 0);

                    if (PersistenSingleton<BattleSubtitles>.Instance != null && !string.IsNullOrEmpty(Lines[i].Text))
                        PersistenSingleton<BattleSubtitles>.Instance.Show(speaker, displayText);
                }
                else
                {
                    LogEchoS.Message("Audio file missing for {speaker.Name} ! => Battle/{path}");

                    if (PersistenSingleton<BattleSubtitles>.Instance != null && !string.IsNullOrEmpty(Lines[i].Text))
                        PersistenSingleton<BattleSubtitles>.Instance.Show(speaker, displayText);

                    float textLength = Lines[i].Text != null ? Lines[i].Text.Length : 0;
                    float waitTime = 2.0f + (textLength * 0.05f);

                    if (PersistenSingleton<BattleSubtitles>.Instance != null)
                    {
                        PersistenSingleton<BattleSubtitles>.Instance.StartCoroutine(WaitAndHideRoutine(speaker.Id, Lines[i].Text, waitTime, onFinishedPlaying));
                    }
                    else
                    {
                        onFinishedPlaying?.Invoke();
                    }
                }
                return;
            }

            LogEchoS.Message(string.Format("Couldn't find battle unit '{0}'", Lines[i].Speaker));
            onFinishedPlaying?.Invoke();
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
}
