using Memoria.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using static Memoria.Data.BattleVoice;
using static Memoria.EchoS.BattleSystem;
using Line = System.Collections.Generic.KeyValuePair<System.Int32, Memoria.Data.BattleVoice.BattleMoment>;

namespace Memoria.EchoS
{
    public class BattleScript : IOverloadVABattleScript
    {
        public void Initialize()
        {
            Log.Debug($"Initialize");
            BattleVoice.OnBattleInOut += OnBattleInOut;
            BattleVoice.OnAct += OnAct;
            BattleVoice.OnHit += OnHit;
            BattleVoice.OnStatusChange += OnStatusChange;
            BattleVoice.OnDialogAudioStart += OnDialogAudioStart;
            BattleVoice.OnDialogAudioEnd += OnDialogAudioEnd;
            Lines = BattleScriptParser.LoadLines().ToArray();
            BattleScriptParser.CountCharacterLines(Lines);

            BattleSubtitles.Instance.Enabled = true;
        }

        public Boolean OnBattleInOut(BattleMoment when)
        {
            HasFirstActHappened = false;

            // Clear the queue but 1
            if (LinesQueue.Count > 1)
            {
                Line line = LinesQueue.Dequeue();
                LinesQueue.Clear();
                LinesQueue.Enqueue(line);
                Log.Debug($"Cleared all lines but '{Lines[line.Key].Path}'");
            }

            // Custom moments
            CharacterId focusChar = VictoryFocusIndex;
            if (when == BattleMoment.VictoryPose)
            {
                if (focusChar != CharacterId.NONE && BattleState.BattleUnitCount(true) > 1)
                {
                    Boolean isSolo = true;
                    for (Int32 i = 0; i < 4; i++)
                    {
                        BattleUnit unit = new BattleUnit(FF9StateSystem.Battle.FF9Battle.btl_data[i]);
                        CharacterId charId = unit.PlayerIndex;

                        if (charId != CharacterId.NONE && charId != focusChar && unit.CurrentHp > 0)
                        {
                            isSolo = false;
                            break;
                        }
                    }
                    if (isSolo)
                        when = BattleMomentEx.VictoryPoseSurvivor;
                }
            }

            if (focusChar == CharacterId.NONE)
            {
                // Select a random player
                Int32 count = BattleState.BattleUnitCount(true);
                if (FF9StateSystem.Battle.battleMapIndex == 303)
                    count--;
                Int32 rng, i;
                rng = i = UnityEngine.Random.Range(0, count);
                for (BTL_DATA next = FF9StateSystem.Battle.FF9Battle.btl_list.next; next != null; next = next.next)
                {
                    // Prevent Blank from saying an intro
                    if (FF9StateSystem.Battle.battleMapIndex == 303 && (CharacterId)next.bi.slot_no == CharacterId.Blank) continue;
                    if (next.bi.player != 0 && i-- == 0)
                    {
                        focusChar = (CharacterId)next.bi.slot_no;
                        break;
                    }
                }
                Log.Debug($"OnBattleInOut {BattleMomentEx.ToString(when)} {focusChar} RNG: {rng}/{count}");
            }
            else
                Log.Debug($"OnBattleInOut {BattleMomentEx.ToString(when)} {focusChar}");

            if (!CanPlayMoreLines)
                return true;

            if (when == BattleMoment.BattleStart)
            {
                BattleSubtitles.Instance.ClearAll();

                String players = "";
                String enemies = "";
                for (BTL_DATA next = FF9StateSystem.Battle.FF9Battle.btl_list.next; next != null; next = next.next)
                {
                    BattleUnit unit = new BattleUnit(next);
                    if (unit.IsPlayer)
                    {
                        players += $"[{unit.Name}] ";
                    }
                    else
                    {
                        enemies += $"[{unit.Name.RemoveTags()}({unit.Data.dms_geo_id})] ";
                    }
                }
                Log.Debug($"BattleId: {FF9StateSystem.Battle.battleMapIndex} Players: {players}Enemies: {enemies}");

                InTranceCharacters.Clear();

                Flags = 0;
                if (IsPreemptive)
                    Flags |= (UInt32)LineEntryFlag.Preemptive;
                else if (IsBackAttack)
                    Flags |= (UInt32)LineEntryFlag.BackAttack;
                else
                    Flags |= (UInt32)LineEntryFlag.FrontAttack;

                Flags |= BattleState.BattleUnitCount(true) > 1 ? (UInt32)LineEntryFlag.PlayerTeam : (UInt32)LineEntryFlag.PlayerSolo;
                Flags |= BattleState.BattleUnitCount(false) > 1 ? (UInt32)LineEntryFlag.EnemyTeam : (UInt32)LineEntryFlag.EnemySolo;
                Flags |= BattleState.IsFriendlyBattle || BattleState.IsRagtimeBattle ? (UInt32)LineEntryFlag.FriendlyBattle : (UInt32)LineEntryFlag.NonFriendlyBattle;

                // TODO: better define Boss and Serious
                // Right now both mean scripted battles
                if (!BattleState.IsRandomBattle)
                    Flags |= (UInt32)LineEntryFlag.Boss | (UInt32)LineEntryFlag.Serious;
            }

            // Prepping flags
            UInt32 flags = Flags;

            // Play a random line
            Int32 lineId = GetRandomLine(when, (i, moment) =>
            {
                if (!CommonChecks(i, moment, flags, null, when == BattleMoment.GameOver ? BattleStatusId.Death : BattleStatusId.None))
                    return false;

                // Check for focused character
                if (Lines[i].Speaker.CheckIsPlayer
                    && focusChar != CharacterId.NONE
                    && focusChar != Lines[i].Speaker.playerId)
                {
                    return false;
                }

                return true;
            });
            QueueLine(lineId, when);

            return true;
        }

        public Boolean OnAct(BattleUnit actingChar, BattleCalculator calc, BattleMoment when)
        {
            HasFirstActHappened = true;
            Boolean processStatuses = false;

            // Custom moments
            if (when == BattleMoment.HitEffect)
            {
                if (calc.Command.Id == BattleCommandId.Steal)
                {
                    if (calc.Context.ItemSteal == RegularItem.NoItem)
                    {
                        if (calc.Target.Enemy.Data.steal_item.Count(p => p != RegularItem.NoItem) == 0)
                            when = BattleMomentEx.StealEmpty;
                        else
                            when = BattleMomentEx.StealFail;
                    }
                    else
                        when = BattleMomentEx.StealSuccess;
                }
                else if (calc.Command.Id == BattleCommandId.Eat || calc.Command.Id == BattleCommandId.Cook)
                {
                    switch (calc.Context.EatResult)
                    {
                        case EatResult.Yummy:
                            when = BattleMomentEx.EatSuccess;
                            break;
                        case EatResult.TasteBad:
                            when = BattleMomentEx.EatBad;
                            break;
                        case EatResult.Failed:
                            when = BattleMomentEx.EatFail;
                            break;
                        case EatResult.CannotEat:
                            when = BattleMomentEx.EatCannot;
                            break;
                    }
                }
                processStatuses = true;
                PerformingCalc = null;
            }
            if (when == BattleMoment.CommandPerform)
            {
                if (calc.Command.Id == BattleCommandId.SysTrans)
                {
                    if (actingChar.InTrance)
                    {
                        when = BattleMomentEx.TranceEnter;
                        InTranceCharacters.Add(actingChar.Data);
                    }
                    else
                    {
                        when = BattleMomentEx.TranceLeave;
                        InTranceCharacters.Remove(actingChar.Data);
                    }
                }
                else if (calc.Command.Id == BattleCommandId.Change)
                {
                    when = (actingChar.Row == 0) ? BattleMomentEx.ChangeFront : BattleMomentEx.ChangeBack;
                }
                else
                {
                    PerformingCalc = calc;
                }
            }

            BattleAbilityId ability = actingChar.IsPlayer ? calc.Command.AbilityId : (BattleAbilityId)calc.Command.RawIndex;
            String abilityName = calc.Command.AbilityCastingName.RemoveTags();
            if (String.IsNullOrEmpty(abilityName) && actingChar.IsPlayer) abilityName = calc.Command.AbilityId.ToString();
            Log.Debug($"OnBattleAct {BattleMomentEx.ToString(when)} [{actingChar.Name.RemoveTags()}({actingChar.Id})] {calc.Command.Id} [{abilityName}({(Int32)ability})] {calc.Command.TargetType} {((calc.Target != null) ? $"[{calc.Target.Name.RemoveTags()}({calc.Target.Id})]" : "")}");

            if (!CanPlayMoreLines)
            {
                if (processStatuses)
                    ProcessStatuses(calc);
                return true;
            }

            // Prepping flags
            UInt32 flags = Flags;
            flags |= GetFlags(calc);
            if (calc.Caster.Data != calc.Target.Data)
                flags |= calc.Target.IsPlayer ? (UInt32)LineEntryFlag.Ally : (UInt32)LineEntryFlag.Enemy;

            // Play a random line
            Boolean filter(int i, BattleMoment moment)
            {
                if (!CommonChecks(i, moment, flags, actingChar))
                    return false;

                // Check target
                if (Lines[i].Target != null && !Lines[i].Target.CheckIsCharacter(calc.Target))
                    return false;

                // Check ability
                if (Lines[i].Abilities != null && !Lines[i].Abilities.Contains(ability))
                    return false;

                // Check command
                if (Lines[i].CommandId != null)
                {
                    BattleCommandId command = calc.Command.Id;
                    if (!Lines[i].CommandId.Contains(command))
                        return false;

                    // Check for item used
                    if ((command == BattleCommandId.Item || command == BattleCommandId.Throw) && Lines[i].Items != null)
                    {
                        if (!Lines[i].Items.Contains(calc.Command.ItemId))
                            return false;
                    }
                }

                // Check for item stolen
                if (actingChar.IsPlayer && ability == BattleAbilityId.Steal && Lines[i].Items != null)
                {
                    if (!Lines[i].Items.Contains(calc.Context.ItemSteal))
                        return false;
                }

                return true;
            }

            Int32 lineId = GetRandomLine(when, filter);
            QueueLine(lineId, when);

            BattleMoment additionalWhen = BattleMoment.Unknown;
            if (when == BattleMoment.HitEffect)
            {
                if (calc.Target?.CurrentHp <= 0)
                    additionalWhen = BattleMomentEx.KillEffect;
                else if ((calc.Context.Flags & BattleCalcFlags.Dodge) != 0)
                    additionalWhen = BattleMomentEx.DodgeEffect;
                else if ((calc.Context.Flags & BattleCalcFlags.Miss) != 0)
                    additionalWhen = BattleMomentEx.MissEffect;
            }

            if (additionalWhen != BattleMoment.Unknown)
            {
                Log.Debug($"OnBattleAct additional When: {additionalWhen}");
                Int32 nextId = GetRandomLine(additionalWhen, filter);
                if (nextId >= 0)
                    QueueLine(nextId, additionalWhen);
            }

            if (processStatuses)
                ProcessStatuses(calc);

            return true;
        }

        public Boolean OnHit(BattleUnit hitChar, BattleCalculator calc, BattleMoment when)
        {
            OnDeathCalc = null;

            // We don't want the event when hit character dies
            // TODO: or do we? only non verbal?
            if (hitChar.CurrentHp == 0)
            {
                Log.Debug($"OnHit {BattleMomentEx.ToString(when)} [{hitChar.Name.RemoveTags()}({hitChar.Id})] died");
                CheckDeathLowHP(calc);
                return true;
            }

            BattleAbilityId ability = calc.Caster.IsPlayer ? calc.Command.AbilityId : (BattleAbilityId)calc.Command.RawIndex;
            Log.Debug($"OnHit {BattleMomentEx.ToString(when)} [{hitChar.Name.RemoveTags()}({hitChar.Id})] {calc.Command.Id} [{calc.Command.AbilityCastingName.RemoveTags()}({(Int32)ability})]");

            // Prepping flags
            UInt32 flags = Flags;
            flags |= GetFlags(calc);
            if (calc.Caster.Data != calc.Target.Data)
                flags |= calc.Caster.IsPlayer ? (UInt32)LineEntryFlag.Ally : (UInt32)LineEntryFlag.Enemy;
            else
                flags |= (UInt32)LineEntryFlag.Self;

            // Play a random line
            Int32 lineId = GetRandomLine(when, (i, moment) =>
            {
                if (!CommonChecks(i, moment, flags, hitChar))
                    return false;

                // Check context flags
                if (Lines[i].ContextFlags != 0 && (Lines[i].ContextFlags & calc.Context.Flags) == 0)
                    return false;

                // Only non verbal lines can play while a dialog plays
                if (!CanPlayMoreLines && Lines[i].IsVerbal)
                    return false;

                // Check caster
                if (Lines[i].Target != null && !Lines[i].Target.CheckIsCharacter(calc.Caster))
                    return false;

                // Check ability
                if (Lines[i].Abilities != null && !Lines[i].Abilities.Contains(ability))
                    return false;

                // Check command
                if (Lines[i].CommandId != null)
                {
                    BattleCommandId command = calc.Command.Id;
                    if (!Lines[i].CommandId.Contains(command))
                        return false;

                    // Check for item used
                    if ((command == BattleCommandId.Item || command == BattleCommandId.Throw) && Lines[i].Items != null)
                    {
                        if (!Lines[i].Items.Contains(calc.Command.ItemId))
                            return false;
                    }
                }

                // Check for item stolen
                if (calc.Caster.IsPlayer && ability == BattleAbilityId.Steal && Lines[i].Items != null)
                {
                    if (!Lines[i].Items.Contains(calc.Context.ItemSteal))
                        return false;
                }

                return true;
            });

            QueueLine(lineId, when);

            // Special handling of Death and LowHp status
            CheckDeathLowHP(calc);

            return true;
        }

        private void CheckDeathLowHP(BattleCalculator calc)
        {
            BattleUnit hitChar = calc.Target;
            if (hitChar.HpDamage == 0) return;

            Boolean hasDeath = hitChar.IsUnderStatus(BattleStatus.Death);

            // Note: we don't want Zidane to say a death line after sacrifice
            if (!hasDeath && hitChar.CurrentHp == 0 && calc.Command.AbilityId != BattleAbilityId.Sacrifice)
            {
                Log.Debug($"Death added [{hitChar.Name.RemoveTags()}({hitChar.Id})]");
                OnStatusChangeEx(hitChar, calc, BattleStatusId.Death, BattleMoment.Added);
                return;
            }
            else if (hasDeath && hitChar.CurrentHp > 0)
            {
                Log.Debug($"Death removed [{hitChar.Name.RemoveTags()}({hitChar.Id})]");
                OnStatusChangeEx(hitChar, calc, BattleStatusId.Death, BattleMoment.Removed);
                return;
            }

            Single ratio = hitChar.IsPlayer ? 6f : 4f;
            Boolean wasLowHP = ((hitChar.CurrentHp + hitChar.HpDamage) * ratio <= hitChar.MaximumHp);
            Boolean isLowHP = (hitChar.CurrentHp * ratio <= hitChar.MaximumHp);
            Boolean cantEat = !hitChar.IsPlayer && calc.Target.CheckUnsafetyOrMiss() && calc.Target.CanBeAttacked() && !calc.Target.HasCategory(EnemyCategory.Humanoid);

            if (!wasLowHP && isLowHP)
            {
                OnStatusChangeEx(hitChar, calc, BattleStatusId.LowHP, BattleMoment.Added);
                // Apply a slight glow to indicate the enemy is low hp (eat range)
                if (cantEat) btl_stat.AddCustomGlowEffect(hitChar.Data, 0, 1, new int[] { -5, -15, -20 });
            }
            else if (wasLowHP && !isLowHP)
            {
                OnStatusChangeEx(hitChar, calc, BattleStatusId.LowHP, BattleMoment.Removed);
                // Remove the glow
                if (cantEat) btl_stat.ClearAllGlowEffect(hitChar.Data);
            }
        }

        private void ProcessStatuses(BattleCalculator calc)
        {
            // Delay ever so slightly so it happens after OnHit
            if (StatusEvents.TryGetValue(calc.Command, out List<StatusEventData> statusEvents))
            {
                StatusEvents.Remove(calc.Command);
                new Thread(() =>
                {
                    Thread.Sleep(1);
                    foreach (StatusEventData data in statusEvents)
                        OnStatusChangeEx(data.statusedChar, data.calc, data.status, data.when);
                }).Start();
            }
        }

        public Boolean OnStatusChange(BattleUnit statusedChar, BattleCalculator calc, BattleStatusId status, BattleMoment when)
        {
            // We handle Death and LowHP in OnHit
            if (status == BattleStatusId.Death || status == BattleStatusId.LowHP) return true;

            if (PerformingCalc != null && calc != null)
            {
                Log.Debug($"Enqueued OnStatusChange {BattleMomentEx.ToString(when)} [{statusedChar.Name.RemoveTags()}({statusedChar.Id})] {status} [{calc.Caster?.Name.RemoveTags()}({calc.Caster?.Id})]");
                if (!StatusEvents.ContainsKey(calc.Command))
                    StatusEvents[calc.Command] = new List<StatusEventData>();
                StatusEvents[calc.Command].Add(new StatusEventData() { statusedChar = statusedChar, calc = calc, status = status, when = when });
                return true;
            }
            OnStatusChangeEx(statusedChar, calc, status, when);
            return true;
        }

        public Boolean OnStatusChangeEx(BattleUnit statusedChar, BattleCalculator calc, BattleStatusId status, BattleMoment when)
        {
            if (!HasFirstActHappened)
                return true;

            if (calc != null)
                Log.Debug($"OnStatusChange {BattleMomentEx.ToString(when)} [{statusedChar.Name.RemoveTags()}({statusedChar.Id})] {status} [{calc.Caster?.Name.RemoveTags()}({calc.Caster?.Id})]");
            else
                Log.Debug($"OnStatusChange {BattleMomentEx.ToString(when)} [{statusedChar.Name.RemoveTags()}({statusedChar.Id})] {status}");

            if (!CanPlayMoreLines)
                return true;

            BattleAbilityId ability = BattleAbilityId.Void;

            // Prepping flags
            UInt32 flags = Flags;
            if (calc != null)
            {
                flags |= GetFlags(calc);
                if (calc.Caster.Data != calc.Target.Data)
                    flags |= calc.Caster.IsPlayer ? (UInt32)LineEntryFlag.Ally : (UInt32)LineEntryFlag.Enemy;
                else
                    flags |= (UInt32)LineEntryFlag.Self;

                // Prevent LowHP after Revive
                // TODO: what about auto-life?
                if (status == BattleStatusId.Death && when == BattleMoment.Removed)
                    OnDeathCalc = calc;

                if (status == BattleStatusId.LowHP && when == BattleMoment.Added && OnDeathCalc == calc)
                {
                    Log.Debug($"OnStatusChange LowHP after revive prevented");
                    OnDeathCalc = null;
                    return true;
                }

                ability = calc.Caster.IsPlayer ? calc.Command.AbilityId : (BattleAbilityId)calc.Command.RawIndex;
            }
            else
            {
                // Allows disabling some lines when triggered by poison or regen
                flags |= (UInt32)LineEntryFlag.Self;
            }

            // Play a random line
            Int32 lineId = GetRandomLine(when, (i, moment) =>
            {
                if (Lines[i].Statuses == null || !Lines[i].Statuses.Contains(status))
                    return false;

                if (!CommonChecks(i, moment, flags, statusedChar, status))
                    return false;

                if (calc == null)
                    // We don't have further info to filter
                    return true;

                // Check context flags
                if (Lines[i].ContextFlags != 0 && (Lines[i].ContextFlags & calc.Context.Flags) == 0)
                    return false;

                // Check caster
                if (Lines[i].Target != null && !Lines[i].Target.CheckIsCharacter(calc.Caster))
                    return false;

                // Check ability
                if (Lines[i].Abilities != null && !Lines[i].Abilities.Contains(ability))
                    return false;

                // Check command
                if (Lines[i].CommandId != null)
                {
                    BattleCommandId command = calc.Command.Id;
                    if (!Lines[i].CommandId.Contains(command))
                        return false;

                    // Check for item used
                    if ((command == BattleCommandId.Item || command == BattleCommandId.Throw) && Lines[i].Items != null)
                    {
                        if (!Lines[i].Items.Contains(calc.Command.ItemId))
                            return false;
                    }
                }
                return true;
            });

            QueueLine(lineId, when);
            return true;
        }

        public void OnDialogAudioStart(Int32 voiceId, String text)
        {
            Log.Debug($"OnBattleDialogAudioStart {voiceId} '{text}'");
            CurrentPlayingDialog = voiceId;
            LinesQueue.Clear();
            StopAllVoices();
            BattleSubtitles.Instance.ClearAll();
        }

        public void OnDialogAudioEnd(Int32 voiceId, String text)
        {
            Log.Debug($"OnBattleDialogAudioEnd {voiceId} '{text}'");
            if (CurrentPlayingDialog == voiceId)
                CurrentPlayingDialog = -1;
        }
    }

    public static class Log
    {
        public static void Message(String msg)
        {
            Prime.Log.Message($"[Echo-S] {msg}");
        }
        public static void Debug(String msg)
        {
#if DEBUG
            Prime.Log.Message($" [Echo-S] {msg}");
#endif
        }
        public static void Warning(String msg)
        {
            Prime.Log.Warning($"  [Echo-S] {msg}");
        }
    }

    public static class StringExtension
    {
        public static String RemoveTags(this string s)
        {
            return Regex.Replace(s, @"\[[^]]*\]", "");
        }
    }
}