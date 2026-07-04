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
        public static FileSystemWatcher watcher = null;
        public static Boolean Loading = false;
        public static IEnumerable<LineEntry> LoadLines()
        {
            Loading = true;
            try
            {
                List<LineEntry> lines = new List<LineEntry>();
                List<LineEntry> customLines = new List<LineEntry>();
                Dictionary<Int32, String> chains = new Dictionary<Int32, String>();
                Dictionary<Int32, String> customChains = new Dictionary<Int32, String>();
                List<String> dbLocations = FindExternalDatabases();

                Log.Message("[Echo-S] BattleLines merge order source: AssetManager.FolderHighToLow (high -> low).");
                if (dbLocations.Count > 0)
                {
                    for (Int32 i = 0; i < dbLocations.Count; i++)
                    {
                        Log.Message($"[Echo-S] BattleLines source[{i}] relative='{ToRelativeGamePath(dbLocations[i])}' full='{dbLocations[i]}'");
                    }
                }
                else
                {
                    Log.Message("[Echo-S] No external BattleLines.tsv found in active folders.");
                }

                ConfigureWatcher(dbLocations);

                if (dbLocations.Count > 0)
                {
                    for (Int32 i = 0; i < dbLocations.Count; i++)
                    {
                        String dbLocation = dbLocations[i];
                        Log.Message($"Loading external database '{dbLocation}'");
                        using (Stream stream = File.OpenRead(dbLocation))
                        using (StreamReader reader = new StreamReader(stream))
                            ParseRows(reader, dbLocation, lines, customLines, chains, customChains);
                    }
                }
                else
                {
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Memoria.EchoS.BattleLines.tsv"))
                    {
                        if (stream == null)
                        {
                            Log.Warning("[Echo-S] No external BattleLines.tsv found and embedded resource is missing. Loading empty battle lines.");
                            BattleSystem.CustomLineStart = 0;
                            return new List<LineEntry>();
                        }

                        Log.Message("[Echo-S] Fallback: loading embedded resource 'Memoria.EchoS.BattleLines.tsv'.");
                        using (StreamReader reader = new StreamReader(stream))
                            ParseRows(reader, "Memoria.EchoS.BattleLines.tsv", lines, customLines, chains, customChains);
                    }
                }

                // Check for chains
                foreach (Int32 key in chains.Keys)
                {
                    String path = chains[key];
                    for (Int32 i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].Path == path)
                        {
                            LineEntry entry = lines[key];
                            entry.ChainId = i;
                            lines[key] = entry;
                            break;
                        }
                    }

                    if (lines[key].ChainId < 0)
                        Log.Warning($"Couldn't find next line in the chain '{chains[key]}'");
                }

                foreach (Int32 key in customChains.Keys)
                {
                    String path = customChains[key];
                    for (Int32 i = 0; i < customLines.Count; i++)
                    {
                        if (customLines[i].Path == path)
                        {
                            LineEntry entry = customLines[key];
                            entry.ChainId = i;
                            customLines[key] = entry;
                            break;
                        }
                    }

                    if (customLines[key].ChainId < 0)
                        Log.Warning($"Couldn't find next line in the chain '{customChains[key]}'");
                }

                Log.Message($"[Echo-S] BattleLines merged counts: regular={lines.Count}, custom={customLines.Count}, total={lines.Count + customLines.Count}");

                BattleSystem.CustomLineStart = lines.Count;

                List<LineEntry> merged = new List<LineEntry>(lines.Count + customLines.Count);
                merged.AddRange(lines);
                merged.AddRange(customLines);
                return merged;
            }
            finally
            {
                Loading = false;
            }
        }

        private static List<String> FindExternalDatabases()
        {
            List<String> result = new List<String>();
            HashSet<String> unique = new HashSet<String>(StringComparer.OrdinalIgnoreCase);

            if (AssetManager.FolderHighToLow != null)
            {
                foreach (var folder in AssetManager.FolderHighToLow)
                {
                    if (folder == null || String.IsNullOrEmpty(folder.FolderPath))
                        continue;

                    String relativePath = folder.FolderPath + "BattleLines.tsv";
                    String fullPath = Path.Combine(Environment.CurrentDirectory, relativePath);
                    if (File.Exists(fullPath) && unique.Add(fullPath))
                        result.Add(fullPath);
                }
            }

            // Backward-compatible fallback for setups where folder listing doesn't expose all roots.
            String fallbackPath = AssetManager.SearchAssetOnDisc("BattleLines.tsv", false, false);
            if (!String.IsNullOrEmpty(fallbackPath))
            {
                String fullPath = Path.Combine(Environment.CurrentDirectory, fallbackPath);
                if (File.Exists(fullPath) && unique.Add(fullPath))
                    result.Add(fullPath);
            }

            return result;
        }

        private static String ToRelativeGamePath(String fullPath)
        {
            String gameRoot = Environment.CurrentDirectory;
            String normalizedRoot = gameRoot.EndsWith(Path.DirectorySeparatorChar.ToString()) ? gameRoot : gameRoot + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(normalizedRoot.Length).Replace('\\', '/');
            return fullPath.Replace('\\', '/');
        }

        private static void ConfigureWatcher(List<String> dbLocations)
        {
            if (dbLocations.Count != 1)
            {
                if (watcher != null)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                    watcher = null;
                }

                if (dbLocations.Count > 1)
                    Log.Message("[Echo-S] Disabled BattleLines file watcher because multiple databases are active.");

                return;
            }

            String dbLocation = dbLocations[0];
            String watcherPath = Path.GetDirectoryName(dbLocation);
            String watcherFilter = Path.GetFileName(dbLocation);

            if (watcher != null)
            {
                Boolean sameTarget =
                    String.Equals(watcher.Path, watcherPath, StringComparison.OrdinalIgnoreCase)
                    && String.Equals(watcher.Filter, watcherFilter, StringComparison.OrdinalIgnoreCase);
                if (sameTarget)
                {
                    watcher.EnableRaisingEvents = true;
                    return;
                }

                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                watcher = null;
            }

            watcher = new FileSystemWatcher(watcherPath, watcherFilter)
            {
                NotifyFilter = NotifyFilters.LastWrite,
                IncludeSubdirectories = false
            };
            watcher.Changed += OnDatabaseChanged;
            watcher.EnableRaisingEvents = true;
        }

        private static void OnDatabaseChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType != WatcherChangeTypes.Changed || Loading)
                return;
            BattleSystem.Lines = LoadLines().ToArray();
        }

        private static void ParseRows(StreamReader reader, String sourceName, List<LineEntry> lines, List<LineEntry> customLines, Dictionary<Int32, String> chains, Dictionary<Int32, String> customChains)
        {
            String line = reader.ReadLine(); // Skips first line (header)
            Int32 lineNumber = 1;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;

                String[] val = line.Split('\t');
                if (val.Length < 36) continue;

                // Speaker
                BattleSpeakerEx speaker = ParseSpeaker(val[0]);
                if (speaker == null)
                {
                    Log.Warning($"Speaker missing or invalid at line {lineNumber} in '{sourceName}'");
                    continue;
                }

                // Path
                if (String.IsNullOrEmpty(val[2]))
                {
                    Log.Warning($"Path missing at line {lineNumber} in '{sourceName}'");
                    continue;
                }
                String path = val[2];
                if (!path.Contains("/"))
                {
                    if (speaker.playerId != CharacterId.NONE)
                        path = $"{speaker.playerId}/{val[2]}";
                }

                LineEntry entry = new LineEntry()
                {
                    Path = path,
                    ChainId = -1,

                    When = ParseMoments(val[5]),
                    Speaker = speaker,
                    Target = ParseSpeaker(val[9]),
                    Priority = ParseInt32(val[6], 0),
                    Weight = ParseInt32(val[7], 100) / 100f,

                    BattleId = ParseInt32Array(val[13], -1),

                    ScenarioMin = ParseInt32(val[14], 0),
                    ScenarioMax = ParseInt32(val[15], 0),

                    CommandId = ParseEnumMulti<BattleCommandId>(val[8]),
                    Abilities = ParseAbilities(val[12]),
                    Statuses = ParseEnumMulti<BattleStatusId>(val[10])
                };

                if (entry.When == null)
                {
                    Log.Warning($"Moment missing or invalid at line {lineNumber} in '{sourceName}'");
                    continue;
                }

                // Parsing With
                if (val[1].Length > 0)
                {
                    List<BattleSpeakerEx> list = new List<BattleSpeakerEx>();
                    String[] tokens = val[1].Split(',');
                    foreach (String token in tokens)
                    {
                        BattleSpeakerEx with = ParseSpeaker(token.Trim());
                        if (with != null)
                            list.Add(with);
                    }
                    if (list.Count > 0)
                        entry.With = list.ToArray();
                }

                // Parsing Context Flags
                if (val[16].Length > 0)
                {
                    BattleCalcFlags[] flags = ParseEnumMulti<BattleCalcFlags>(val[16]);
                    entry.ContextFlags = 0;
                    if (flags != null)
                    {
                        foreach (BattleCalcFlags flag in flags)
                            entry.ContextFlags |= flag;
                    }
                    else
                        Log.Warning($"Couldn't parse Context Flags '{val[16]}' at line {lineNumber} in '{sourceName}'");
                }

                // Parsing Flags
                for (UInt16 i = 0; i < 22; i++)
                {
                    if (val[17 + i].Length == 0)
                        entry.Flags |= (UInt32)(1 << i);
                }

                // Parsing Items
                if (val[11].Length > 0)
                {
                    entry.Items = ParseEnumMulti<RegularItem>(val[11]);
                    if (entry.Items == null)
                        Log.Warning($"Couldn't parse items '{val[11]}' at line {lineNumber} in '{sourceName}'");
                }

                // Parse Text
                String text = val[3].Trim();
                if (text.Length > 0)
                    entry.Text = text;

                // Check if file exists
                if (true)
                {
                    String filePath = entry.Path;
                    if (!AssetManager.HasAssetOnDisc($"Sounds/Voices/US/Battle/{filePath}.akb", true, true) && !AssetManager.HasAssetOnDisc($"Sounds/Voices/US/Battle/{filePath}.ogg", true, false))
                        Log.Warning($"'{filePath}' is missing");
                }

                // Add line to the list
                if (entry.When.Contains(BattleMomentEx.Custom))
                {
                    if (val[4].Length > 0)
                        customChains.Add(customLines.Count, val[4]);
                    customLines.Add(entry);
                }
                else
                {
                    // Add chain value to look up
                    if (val[4].Length > 0)
                        chains.Add(lines.Count, val[4]);
                    lines.Add(entry);
                }
            }
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
                Log.Message($"{player.Key}: {player.Value.Count} unique lines");
            }
        }

        private static Int32 ParseInt32(String value, Int32 defaultValue)
        {
            if (value.Length == 0)
                return defaultValue;

            if (!Int32.TryParse(value, out defaultValue))
            {
                Log.Warning($"Couldn't parse '{value}'");
            }
            return defaultValue;
        }

        private static Int32[] ParseInt32Array(String value, Int32 defaultValue)
        {
            if (value.Length == 0)
                return new Int32[] { defaultValue };

            const Int32 excludedBattleIdOffset = 1000000;

            String[] values = value.Split(',');
            List<Int32> result = new List<Int32>();
            foreach (String val in values)
            {
                String trimmed = val.Trim();
                if (trimmed.StartsWith("!") && trimmed.Length > 1)
                {
                    String excluded = trimmed.Substring(1).Trim();
                    if (Int32.TryParse(excluded, out Int32 excludedId) && excludedId >= 0)
                        result.Add(-(excludedId + excludedBattleIdOffset));
                    else
                        Log.Warning($"Couldn't parse '{trimmed}' as excluded Int32");
                }
                else if (Int32.TryParse(trimmed, out Int32 id))
                {
                    result.Add(id);
                }
                else
                {
                    Log.Warning($"Couldn't parse '{trimmed}' as Int32");
                }
            }

            return result.Count > 0 ? result.ToArray() : new Int32[] { defaultValue };
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
                Log.Warning($"Couldn't parse {typeof(T)} '{value}'");
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
                Log.Warning($"Couldn't parse {typeof(T)} '{value}'");
            }
            return null;
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
                Log.Warning($"Couldn't parse {typeof(BattleAbilityId)} '{value}'");
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
                    Log.Warning($"Couldn't parse BattleMoment '{values[i].Trim()}'");
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
                            Log.Warning($"Invalid model id '{modelId}'");
                            return null;
                        }
                        speaker.enemyModelId = modelId;
                    }
                    // BattleId:ModelName
                    else if (!FF9BattleDB.GEO.TryGetKey(tokens[1], out speaker.enemyModelId))
                    {
                        Log.Warning($"Invalid model name '{tokens[1]}'");
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
