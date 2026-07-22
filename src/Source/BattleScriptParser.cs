using Memoria.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Memoria.Data.BattleVoice;

namespace Memoria.EchoS
{
    public static class BattleScriptParser
    {
        public static FileSystemWatcher watcher;
        public static bool Loading;
        public static String StuffListedPath = "[Tsunamods] Echo-S 9/BattleLines.tsv";

        public static IEnumerable<LineEntry> LoadLines()
        {
            Loading = true;
            List<LineEntry> lines = new List<LineEntry>();
            List<LineEntry> customLines = new List<LineEntry>();
            Dictionary<int, string> chainLinks = new Dictionary<int, string>();
            Dictionary<int, string> customChainLinks = new Dictionary<int, string>();

            Stream stream = null;
            string fullPath = null;

            string assetPath = AssetManager.SearchAssetOnDisc(StuffListedPath, false, false);

            if (!string.IsNullOrEmpty(assetPath))
            {
                fullPath = Path.Combine(Environment.CurrentDirectory, assetPath);
            }
            else
            {
                string directPath = Path.Combine(Environment.CurrentDirectory, StuffListedPath);
                if (File.Exists(directPath))
                {
                    fullPath = directPath;
                }
            }

            if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
            {
                LogEchoS.Message($"Loading external database '{fullPath}'");
                try
                {
                    stream = File.OpenRead(fullPath);

                    if (watcher == null)
                    {
                        watcher = new FileSystemWatcher(Path.GetDirectoryName(fullPath), Path.GetFileName(fullPath))
                        {
                            NotifyFilter = NotifyFilters.LastWrite,
                            IncludeSubdirectories = false
                        };
                        watcher.Changed += (sender, e) =>
                        {
                            if (e.ChangeType != WatcherChangeTypes.Changed || Loading) return;
                            BattleSystem.Lines = LoadLines().ToArray();
                        };
                        watcher.EnableRaisingEvents = true;
                    }
                }
                catch (Exception ex)
                {
                    LogEchoS.Message($"Failed to open file '{fullPath}': {ex.Message}");
                    stream = null;
                }
            }
            else
            {
                LogEchoS.Message($"[BattleScriptParser] File not found: '{StuffListedPath}'. Please check the path or mod installation.");
            }

            if (stream == null)
            {
                Loading = false;
                yield break;
            }

            using (StreamReader reader = new StreamReader(stream))
            {
                reader.ReadLine();
                int lineNumber = 1;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    if (string.IsNullOrEmpty(line) || line.Trim().Length == 0) continue;

                    string[] columns = line.Split('\t');
                    if (columns.Length < 36) continue;

                    BattleSpeakerEx speaker = ParseSpeaker(columns[0]);
                    if (speaker == null)
                    {
                        LogEchoS.Message($"Speaker missing or invalid at line {lineNumber}");
                        continue;
                    }

                    if (string.IsNullOrEmpty(columns[2]))
                    {
                        LogEchoS.Message($"Path missing {lineNumber}");
                        continue;
                    }

                    string path = columns[2];
                    if (!path.Contains("/") && speaker.playerId != CharacterId.NONE)
                    {
                        path = $"{speaker.playerId}/{columns[2]}";
                    }

                    ParseBattleIds(columns[13], out int[] bIds, out bool bBlacklist);

                    LineEntry entry = new LineEntry
                    {
                        Path = path,
                        ChainId = -1,
                        When = ParseMoments(columns[5]),
                        Speaker = speaker,
                        Target = ParseSpeaker(columns[9]),
                        Priority = ParseInt32(columns[6], 0),
                        Weight = ParseInt32(columns[7], 100) / 100f,
                        BattleIds = bIds,
                        BattleIdIsBlacklist = bBlacklist,
                        ScenarioMin = ParseInt32(columns[14], 0),
                        ScenarioMax = ParseInt32(columns[15], 0),
                        Statuses = ParseEnumMulti<BattleStatusId>(columns[10])
                    };

                    string commandIdRaw = columns[8].Trim();
                    if (!string.IsNullOrEmpty(commandIdRaw))
                    {
                        if (commandIdRaw.StartsWith("!"))
                        {
                            entry.CommandIdIsBlacklist = true;
                            commandIdRaw = commandIdRaw.Substring(1).Trim();
                        }
                        entry.CommandId = ParseEnumMulti<BattleCommandId>(commandIdRaw);
                    }

                    string abilitiesRaw = columns[12].Trim();
                    if (!string.IsNullOrEmpty(abilitiesRaw))
                    {
                        if (abilitiesRaw.StartsWith("!"))
                        {
                            entry.AbilitiesIsBlacklist = true;
                            abilitiesRaw = abilitiesRaw.Substring(1).Trim();
                        }
                        entry.Abilities = ParseAbilities(abilitiesRaw);
                    }

                    if (entry.When == null)
                    {
                        LogEchoS.Message($"Moment missing or invalid at line {lineNumber}");
                        continue;
                    }

                    if (columns[1].Length > 0)
                    {
                        List<BattleSpeakerEx> withList = new List<BattleSpeakerEx>();
                        string[] withParts = columns[1].Split(',');
                        foreach (string part in withParts)
                        {
                            BattleSpeakerEx withSpeaker = ParseSpeaker(part.Trim());
                            if (withSpeaker != null) withList.Add(withSpeaker);
                        }
                        if (withList.Count > 0) entry.With = withList.ToArray();
                    }

                    if (columns[16].Length > 0)
                    {
                        BattleCalcFlags[] ctxFlags = ParseEnumMulti<BattleCalcFlags>(columns[16]);
                        entry.ContextFlags = 0;
                        if (ctxFlags != null)
                        {
                            foreach (BattleCalcFlags f in ctxFlags) entry.ContextFlags |= f;
                        }
                        else
                        {
                            LogEchoS.Message($"Couldn't parse Context Flags '{columns[16]}' at line {lineNumber}");
                        }
                    }

                    for (ushort i = 0; i < 22; i++)
                    {
                        if (columns[17 + i].Length == 0)
                        {
                            entry.Flags |= (LineEntryFlag)(1U << i);
                        }
                    }

                    if (columns[11].Length > 0)
                    {
                        entry.Items = ParseEnumMulti<RegularItem>(columns[11]);
                        if (entry.Items == null) LogEchoS.Message($"Couldn't parse items '{columns[11]}' at line {lineNumber}");
                    }

                    string textVal = columns[3].Trim();
                    if (textVal.Length > 0) entry.Text = textVal;

                    if (entry.When.Contains((BattleVoice.BattleMoment)BattleMomentEx.Custom))
                    {
                        if (columns[4].Length > 0) customChainLinks.Add(customLines.Count, columns[4]);
                        customLines.Add(entry);
                    }
                    else
                    {
                        if (columns[4].Length > 0) chainLinks.Add(lines.Count, columns[4]);
                        lines.Add(entry);
                    }
                }
            }

            foreach (var link in chainLinks)
            {
                string targetPath = link.Value;
                for (int k = 0; k < lines.Count; k++)
                {
                    if (lines[k].Path == targetPath)
                    {
                        LineEntry update = lines[link.Key];
                        update.ChainId = k;
                        lines[link.Key] = update;
                        break;
                    }
                }
                if (lines[link.Key].ChainId < 0) LogEchoS.Message($"Couldn't find next line in the chain '{targetPath}'");
            }

            foreach (var link in customChainLinks)
            {
                string targetPath = link.Value;
                for (int l = 0; l < customLines.Count; l++)
                {
                    if (customLines[l].Path == targetPath)
                    {
                        LineEntry update = customLines[link.Key];
                        update.ChainId = l;
                        customLines[link.Key] = update;
                        break;
                    }
                }
                if (customLines[link.Key].ChainId < 0) LogEchoS.Message($"Couldn't find next line in the chain '{targetPath}'");
            }

            LogEchoS.Message($"Total lines successfully loaded '{lines.Count + customLines.Count}'");
            BattleSystem.CustomLineStart = lines.Count;

            foreach (var lineItem in lines) yield return lineItem;
            foreach (var customItem in customLines) yield return customItem;

            Loading = false;
        }

        public static void CountCharacterLines(LineEntry[] lines)
        {
            Dictionary<CharacterId, HashSet<String>> linesPerChar = new Dictionary<CharacterId, HashSet<String>>();

            for (Int32 i = 0; i < lines.Length; i++)
            {
                if (!lines[i].IsVerbal) continue;

                BattleSpeakerEx speaker = lines[i].Speaker;
                CharacterId id = speaker.playerId;
                if (id == CharacterId.NONE && speaker.enemyModelId == 298)
                    id = CharacterId.Steiner;
                if (id == CharacterId.NONE && speaker.CheckCanTalk)
                    continue;


                if (!speaker.CheckCanTalk)
                {
                    if (lines[i].With == null || lines[i].With.Count() == 0) continue;
                    if (lines[i].With[0].playerId == CharacterId.NONE || !lines[i].With[0].CheckCanTalk) continue;
                    id = lines[i].With[0].playerId;
                }

                if (!linesPerChar.ContainsKey(id))
                {
                    linesPerChar[id] = new HashSet<String>();
                }
                linesPerChar[id].Add(lines[i].Text);
            }

            foreach (var player in linesPerChar)
            {
                LogEchoS.Message($"{player.Key}: {player.Value.Count} unique lines");
            }
        }

        private static Int32 ParseInt32(String value, Int32 defaultValue)
        {
            if (value.Length == 0)
                return defaultValue;

            if (!Int32.TryParse(value, out defaultValue))
            {
                LogEchoS.Warning($"Couldn't parse '{value}'");
            }
            return defaultValue;
        }

        private static T ParseEnum<T>(String value, T defaultValue) where T : Enum
        {
            if (value.Length == 0)
                return defaultValue;

            try
            {
                return (T)Enum.Parse(typeof(T), value);
            }
            catch
            {
                LogEchoS.Warning($"Couldn't parse {typeof(T)} '{value}'");
            }
            return defaultValue;
        }

        private static T[] ParseEnumMulti<T>(String value) where T : Enum
        {
            if (value.Length == 0)
                return null;

            String[] values = value.Split(',');
            List<T> result = new List<T>();
            try
            {
                foreach (String val in values)
                    result.Add((T)Enum.Parse(typeof(T), val.Trim()));

                return result.ToArray();
            }
            catch
            {
                LogEchoS.Warning($"Couldn't parse {typeof(T)} '{value}'");
            }
            return null;
        }

        private static void ParseBattleIds(string value, out int[] ids, out bool isBlacklist)
        {
            ids = null;
            isBlacklist = false;

            if (string.IsNullOrEmpty(value) || value.Trim().Length == 0) return;

            string content = value.Trim();

            if (content.StartsWith("!"))
            {
                isBlacklist = true;
                content = content.Substring(1);
            }

            string[] parts = content.Split(',');
            List<int> list = new List<int>();

            foreach (string s in parts)
            {
                if (int.TryParse(s.Trim(), out int id))
                {
                    list.Add(id);
                }
            }

            if (list.Count > 0) ids = list.ToArray();
        }

        private static BattleAbilityId[] ParseAbilities(String value)
        {
            if (value.Length == 0)
                return null;

            String[] values = value.Split(',');
            List<BattleAbilityId> result = new List<BattleAbilityId>();
            try
            {
                foreach (String val in values)
                {
                    String trimmed = val.Trim();
                    if (Int32.TryParse(trimmed, out Int32 id))
                        result.Add((BattleAbilityId)id);
                    else
                        result.Add((BattleAbilityId)Enum.Parse(typeof(BattleAbilityId), trimmed));
                }
                return result.ToArray();
            }
            catch
            {
                LogEchoS.Warning($"Couldn't parse {typeof(BattleAbilityId)} '{value}'");
            }
            return null;
        }

        private static BattleMoment[] ParseMoments(String value)
        {
            if (value.Length == 0)
                return null;

            List<BattleMoment> result = new List<BattleMoment>();

            String[] values = value.Split(',');
            for (int i = 0; i < values.Length; i++)
            {
                BattleMoment moment = ParseMoment(values[i].Trim());
                if (moment == BattleMoment.Unknown)
                {
                    LogEchoS.Warning($"Couldn't parse BattleMoment '{values[i].Trim()}'");
                    return null;
                }
                else
                    result.Add(moment);
            }

            if (result.Count == 0)
                return null;

            return result.ToArray();
        }

        private static BattleMoment ParseMoment(String value)
        {
            BattleMoment result = BattleMoment.Unknown;
            try
            {
                result = (BattleMoment)Enum.Parse(typeof(BattleMoment), value);
            }
            catch
            {
                // Look in BattleMomentEx
                value = value.ToLower();
                PropertyInfo[] properties = typeof(BattleMomentEx).GetProperties();
                foreach (PropertyInfo property in properties)
                {
                    if (property.Name.ToLower() == value)
                        return (BattleMoment)property.GetValue(null, null);
                }
            }
            return result;
        }

        private static BattleSpeakerEx ParseSpeaker(String value)
        {
            if (value.Length == 0)
                return null;

            BattleSpeakerEx speaker = new BattleSpeakerEx();

            if (value[0] == '$')
            {
                speaker.CheckCanTalk = false;
                value = value.Substring(1);
            }

            if (value[0] == '!')
            {
                speaker.CheckIsPlayer = false;
                value = value.Substring(1);
            }

            if (value[0] == '\\')
            {
                speaker.Without = true;
                value = value.Substring(1);
            }

            String[] tokens = value.Trim().Split(':');
            if (tokens.Length == 1)
            {
                // CharacterId
                speaker.playerId = ParseEnum(tokens[0], CharacterId.NONE);
                if (speaker.playerId == CharacterId.NONE)
                    return null;
            }
            else if (tokens.Length > 1)
            {
                String status = null;
                // The speaker is an enemy identified by its battle ID and/or its model name
                if (tokens[0].Length > 0)
                {
                    // BattleId:
                    if (!Int32.TryParse(tokens[0], out speaker.enemyBattleId))
                    {
                        // CharacterId:StatusId
                        speaker.playerId = ParseEnum(tokens[0], CharacterId.NONE);
                        if (speaker.playerId == CharacterId.NONE)
                            return null;
                        status = tokens[1];
                    }
                }
                // Note: Empty string is valid, allowing to target any enemy. Useful for debugging.
                if (status == null && tokens[1].Length > 0)
                {
                    // BattleId:ModelId
                    if (Int32.TryParse(tokens[1], out int modelId))
                    {
                        // Verify the number is a valid ModelId
                        if (!FF9BattleDB.GEO.ContainsKey(modelId))
                        {
                            LogEchoS.Warning($"Invalid model id '{modelId}'");
                            return null;
                        }
                        speaker.enemyModelId = modelId;
                    }
                    // BattleId:ModelName
                    else if (!FF9BattleDB.GEO.TryGetKey(tokens[1], out speaker.enemyModelId))
                    {
                        LogEchoS.Warning($"Invalid model name '{tokens[1]}'");
                        return null;
                    }
                }

                // BattleId:ModelId:StatusId
                if (tokens.Length > 2)
                    status = tokens[2];

                if (status != null)
                    speaker.Status = ParseEnum(status, BattleStatusId.None);
            }
            return speaker;
        }
    }
}
