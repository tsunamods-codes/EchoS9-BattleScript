using Assets.Sources.Scripts.UI.Common;
using FF9;
using Memoria.Assets;
using Memoria.Data;
using Memoria.Database;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Memoria.EchoS
{
    public class DialogHelpVA : MonoBehaviour
    {
        private GameObject _lastSelectedTarget;
        private Boolean _wasHelpShown;
        private SoundProfile _lastPlayedSound;
        private readonly Dictionary<String, String> _audioPathCache = new Dictionary<String, String>();

        private void Update()
        {
            Boolean isHelpShown = Singleton<HelpDialog>.Instance != null && Singleton<HelpDialog>.Instance.IsShown;
            GameObject currentSelected = UICamera.selectedObject;

            if (isHelpShown && (!_wasHelpShown || currentSelected != _lastSelectedTarget))
            {
                OnHelpTargetChanged(currentSelected);
            }
            else if (!isHelpShown && _wasHelpShown)
            {
                StopAudio();
            }

            _wasHelpShown = isHelpShown;
            _lastSelectedTarget = currentSelected;
        }

        private void OnHelpTargetChanged(GameObject target)
        {
            StopAudio();
            if (target == null) return;

            ButtonGroupState bgs = target.GetComponent<ButtonGroupState>();
            if (bgs == null || !bgs.Help.Enable) return;

            String groupName = bgs.GroupName;

            // ==========================================
            // 1. ABILITY MENU (OUT OF BATTLE)
            // ==========================================
            if (groupName == "Ability.ActionAbility" || groupName == "Ability.SupportAbility")
            {
                RecycleListItem listItem = target.GetComponent<RecycleListItem>();
                if (listItem != null)
                {
                    Int32 dataIndex = listItem.ItemDataIndex;
                    AbilityUI abilityUI = PersistenSingleton<UIManager>.Instance.AbilityScene;

                    if (abilityUI != null)
                    {
                        CharacterId currentCharacter = CharacterId.NONE;
                        FieldInfo partyIndexField = typeof(AbilityUI).GetField("currentPartyIndex", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (partyIndexField != null)
                        {
                            Int32 partyIndex = (Int32)partyIndexField.GetValue(abilityUI);
                            PLAYER player = FF9StateSystem.Common.FF9.party.member[partyIndex];
                            if (player != null)
                                currentCharacter = player.Index;
                        }

                        String fieldName = (groupName == "Ability.ActionAbility") ? "aaIdList" : "saIdList";
                        FieldInfo listField = typeof(AbilityUI).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

                        if (listField != null)
                        {
                            List<Int32> idList = (List<Int32>)listField.GetValue(abilityUI);

                            if (idList != null && dataIndex >= 0 && dataIndex < idList.Count)
                            {
                                Int32 rawItemId = idList[dataIndex];
                                String type;
                                String name;
                                Int32 numericId;

                                if (groupName == "Ability.ActionAbility")
                                {
                                    type = "ActiveAbility";
                                    BattleAbilityId aaId = ff9abil.GetActiveAbilityFromAbilityId(rawItemId);
                                    numericId = (Int32)aaId;
                                    name = aaId.ToString();
                                }
                                else
                                {
                                    type = "SupportAbility";
                                    SupportAbility saId = ff9abil.GetSupportAbilityFromAbilityId(rawItemId);
                                    numericId = (Int32)saId;
                                    name = saId.ToString();
                                }

                                LogEchoS.Debug($"[DialogHelpVA] Menu Ability identified: Type={type}, ID={numericId}, Name={name}, Char={currentCharacter}");
                                PlayHelpVoice(type, numericId, name, currentCharacter);
                            }
                        }
                    }
                }
            }
            // ==========================================
            // 2. ITEM MENU (OUT OF BATTLE)
            // ==========================================
            else if (groupName == "Item.Item" || groupName == "Item.KeyItem")
            {
                RecycleListItem listItem = target.GetComponent<RecycleListItem>();
                if (listItem != null)
                {
                    Int32 dataIndex = listItem.ItemDataIndex;
                    ItemUI itemUI = PersistenSingleton<UIManager>.Instance.ItemScene;

                    if (itemUI != null)
                    {
                        String fieldName = (groupName == "Item.Item") ? "_itemIdList" : "_keyItemIdList";
                        FieldInfo listField = typeof(ItemUI).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

                        if (listField != null)
                        {
                            if (groupName == "Item.Item")
                            {
                                List<RegularItem> idList = (List<RegularItem>)listField.GetValue(itemUI);
                                if (idList != null && dataIndex >= 0 && dataIndex < idList.Count)
                                {
                                    RegularItem itemId = idList[dataIndex];
                                    String type = "MenuItem";
                                    Int32 numericId = (Int32)itemId;
                                    String name = itemId.ToString();

                                    LogEchoS.Debug($"[DialogHelpVA] Menu Item identified: Type={type}, ID={numericId}, Name={name}");
                                    PlayHelpVoice(type, numericId, name, CharacterId.NONE);
                                }
                            }
                            else if (groupName == "Item.KeyItem")
                            {
                                List<Int32> idList = (List<Int32>)listField.GetValue(itemUI);
                                if (idList != null && dataIndex >= 0 && dataIndex < idList.Count)
                                {
                                    Int32 numericId = idList[dataIndex];
                                    if (numericId != 255)
                                    {
                                        String type = "KeyItem";
                                        String name = FF9TextTool.ImportantItemName(numericId);

                                        LogEchoS.Debug($"[DialogHelpVA] Menu KeyItem identified: Type={type}, ID={numericId}, Name={name}");
                                        PlayHelpVoice(type, numericId, name, CharacterId.NONE);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            // ==========================================
            // 3. EQUIP INVENTORY MENU
            // ==========================================
            else if (groupName == "Equip.Inventory")
            {
                RecycleListItem listItem = target.GetComponent<RecycleListItem>();
                if (listItem != null)
                {
                    Int32 dataIndex = listItem.ItemDataIndex;
                    EquipUI equipUI = PersistenSingleton<UIManager>.Instance.EquipScene;

                    if (equipUI != null)
                    {
                        CharacterId currentCharacter = CharacterId.NONE;
                        FieldInfo partyIndexField = typeof(EquipUI).GetField("currentPartyIndex", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (partyIndexField != null)
                        {
                            Int32 partyIndex = (Int32)partyIndexField.GetValue(equipUI);
                            PLAYER player = FF9StateSystem.Common.FF9.party.member[partyIndex];
                            if (player != null)
                                currentCharacter = player.Index;
                        }

                        FieldInfo equipPartField = typeof(EquipUI).GetField("currentEquipPart", BindingFlags.NonPublic | BindingFlags.Instance);
                        FieldInfo listField = typeof(EquipUI).GetField("itemIdList", BindingFlags.NonPublic | BindingFlags.Instance);

                        if (equipPartField != null && listField != null)
                        {
                            Int32 currentEquipPart = (Int32)equipPartField.GetValue(equipUI);
                            List<List<FF9ITEM>> itemIdList = (List<List<FF9ITEM>>)listField.GetValue(equipUI);

                            if (itemIdList != null && currentEquipPart >= 0 && currentEquipPart < itemIdList.Count)
                            {
                                List<FF9ITEM> currentList = itemIdList[currentEquipPart];
                                if (currentList != null && dataIndex >= 0 && dataIndex < currentList.Count)
                                {
                                    RegularItem itemId = currentList[dataIndex].id;

                                    if (itemId != RegularItem.NoItem)
                                    {
                                        String type = "MenuItem";
                                        Int32 numericId = (Int32)itemId;
                                        String name = itemId.ToString();

                                        LogEchoS.Debug($"[DialogHelpVA] Equip Inventory identified: Type={type}, ID={numericId}, Name={name}, Char={currentCharacter}");
                                        PlayHelpVoice(type, numericId, name, currentCharacter);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            // ==========================================
            // 4. BATTLE HUD (IN BATTLE)
            // ==========================================
            else if (groupName == "Battle.Item" || groupName == "Battle.Ability")
            {
                RecycleListItem listItem = target.GetComponent<RecycleListItem>();
                BattleHUD battleHUD = UIManager.Battle;

                if (battleHUD != null && listItem != null)
                {
                    Int32 dataIndex = listItem.ItemDataIndex;
                    CharacterId currentCharacter = CharacterId.NONE;

                    Int32 playerIndex = battleHUD.CurrentPlayerIndex;
                    if (playerIndex >= 0)
                    {
                        BattleUnit unit = FF9StateSystem.Battle.FF9Battle.GetUnit(playerIndex);
                        if (unit != null && unit.IsPlayer)
                            currentCharacter = unit.PlayerIndex;
                    }

                    if (groupName == "Battle.Item")
                    {
                        FieldInfo listField = typeof(BattleHUD).GetField("_itemIdList", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (listField != null)
                        {
                            List<RegularItem> idList = (List<RegularItem>)listField.GetValue(battleHUD);
                            if (idList != null && dataIndex >= 0 && dataIndex < idList.Count)
                            {
                                RegularItem itemId = idList[dataIndex];
                                String type = "BattleItem";
                                Int32 numericId = (Int32)itemId;
                                String name = itemId.ToString();

                                LogEchoS.Debug($"[DialogHelpVA] Battle Item identified: Type={type}, ID={numericId}, Name={name}, Char={currentCharacter}");
                                PlayHelpVoice(type, numericId, name, currentCharacter);
                            }
                        }
                    }
                    else if (groupName == "Battle.Ability")
                    {
                        FieldInfo cmdField = typeof(BattleHUD).GetField("_currentCommandId", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (cmdField != null)
                        {
                            BattleCommandId currentCmd = (BattleCommandId)cmdField.GetValue(battleHUD);
                            CharacterCommand ff9Command = CharacterCommands.Commands[currentCmd];

                            BattleAbilityId aaId = ff9Command.GetAbilityId(dataIndex);

                            if (aaId != BattleAbilityId.Void)
                            {
                                String type = "ActiveAbility";
                                Int32 numericId = (Int32)aaId;
                                String name = aaId.ToString();

                                LogEchoS.Debug($"[DialogHelpVA] Battle Ability identified: Type={type}, ID={numericId}, Name={name}, Char={currentCharacter}");
                                PlayHelpVoice(type, numericId, name, currentCharacter);
                            }
                        }
                    }
                }
            }
            // ==========================================
            // 5. BATTLE COMMAND MENU
            // ==========================================
            else if (groupName == "Battle.Command")
            {
                BattleHUD battleHUD = UIManager.Battle;

                if (battleHUD != null)
                {
                    Int32 playerIndex = battleHUD.CurrentPlayerIndex;
                    if (playerIndex >= 0)
                    {
                        BattleUnit btl = FF9StateSystem.Battle.FF9Battle.GetUnit(playerIndex);
                        if (btl != null && btl.IsPlayer)
                        {
                            CharacterId currentCharacter = btl.PlayerIndex;
                            CharacterPresetId presetId = btl.Player.PresetId;
                            Boolean isTrance = btl.IsUnderAnyStatus(BattleStatus.Trance);
                            BattleCommandMenu menu = BattleCommandMenu.None;

                            String targetName = target.name.Replace(" ", "");

                            if (targetName == "Attack") menu = BattleCommandMenu.Attack;
                            else if (targetName == "Defend") menu = BattleCommandMenu.Defend;
                            else if (targetName == "Skill1") menu = BattleCommandMenu.Ability1;
                            else if (targetName == "Skill2") menu = BattleCommandMenu.Ability2;
                            else if (targetName == "Item") menu = BattleCommandMenu.Item;
                            else if (targetName == "Change") menu = BattleCommandMenu.Change;

                            if (menu != BattleCommandMenu.None)
                            {
                                BattleCommandId cmdId = CharacterCommands.CommandSets[presetId].Get(isTrance, menu);
                                cmdId = BattleCommandHelper.Patch(cmdId, menu, btl.Player, btl);

                                String type = "BattleCommand";
                                Int32 numericId = (Int32)cmdId;
                                String name = cmdId.ToString();

                                LogEchoS.Debug($"[DialogHelpVA] Battle Command identified: Type={type}, ID={numericId}, Name={name}, Char={currentCharacter}");
                                PlayHelpVoice(type, numericId, name, currentCharacter);
                            }
                        }
                    }
                }
            }
            // ==========================================
            // 6. MAIN MENU, CARD & EQUIP STATIC MENUS
            // ==========================================
            else if (groupName.StartsWith("MainMenu.") || groupName.StartsWith("Card.") || groupName.StartsWith("Equip."))
            {
                String[] parts = groupName.Split('.');
                if (parts.Length == 2)
                {
                    String category = parts[0];
                    String subCategory = parts[1];
                    String targetName = target.name;

                    Int32 cloneSuffixIndex = targetName.IndexOf(" (");
                    if (cloneSuffixIndex > 0)
                    {
                        targetName = targetName.Substring(0, cloneSuffixIndex);
                    }

                    String cacheKey = $"Static_{category}_{subCategory}_{targetName}";
                    if (_audioPathCache.TryGetValue(cacheKey, out String cachedPath))
                    {
                        if (cachedPath != null)
                        {
                            LogEchoS.Debug($"[DialogHelpVA] [LOADED FROM CACHE] Audio playing: {cachedPath}");
                            PlayResolvedPath(cachedPath);
                        }
                        return;
                    }

                    LogEchoS.Debug($"[DialogHelpVA] {category} Static Menu identified: Group={groupName}, Target={targetName}");

                    String lang = Localization.CurrentSymbol;
                    String exactPath = $"Voices/{lang}/HelpText/{category}/{subCategory}/{targetName}";
                    String finalPath = null;

                    if (CheckFileExists(exactPath))
                    {
                        finalPath = exactPath;
                    }
                    else
                    {
                        String fallbackPath = $"Voices/{lang}/HelpText/{category}/{subCategory}/va_{targetName.Replace(" ", "")}";
                        if (CheckFileExists(fallbackPath))
                        {
                            finalPath = fallbackPath;
                        }
                        else
                        {
                            LogEchoS.Debug($"[DialogHelpVA] [NOT FOUND] No audio found for {category} (tried Sounds/{exactPath} and Sounds/{fallbackPath})");
                        }
                    }

                    _audioPathCache[cacheKey] = finalPath;
                    if (finalPath != null)
                    {
                        PlayResolvedPath(finalPath);
                    }
                }
            }
            // ==========================================
            // X. UNHANDLED GROUPS FALLBACK (Debug)
            // ==========================================
            else
            {
                LogEchoS.Debug($"[DialogHelpVA] [UNHANDLED GROUP] groupName = '{groupName}', target = '{target.name}'");
            }
        }

        private void PlayHelpVoice(String type, Int32 id, String name, CharacterId character = CharacterId.NONE, String subtype = null)
        {
            String cacheKey = $"{type}_{id}_{name}_{character}_{subtype}";

            if (_audioPathCache.TryGetValue(cacheKey, out String cachedPath))
            {
                if (cachedPath != null)
                {
                    LogEchoS.Debug($"[DialogHelpVA] [CACHE HIT] Audio playing: {cachedPath}");
                    PlayResolvedPath(cachedPath);
                }
                return;
            }

            LogEchoS.Debug($"[DialogHelpVA] --- Starting audio search for {name ?? id.ToString()} ---");
            String foundPath = ResolveAudioPath(type, id, name, character, subtype);

            _audioPathCache[cacheKey] = foundPath;

            if (foundPath != null)
            {
                PlayResolvedPath(foundPath);
            }
            else
            {
                LogEchoS.Debug($"[DialogHelpVA] [NOT FOUND] No audio found for (ID:{id}, Name:{name}). Cached as missing.");
            }
        }

        private String ResolveAudioPath(String type, Int32 id, String name, CharacterId character, String subtype)
        {
            String lang = Localization.CurrentSymbol;
            String basePath = $"Voices/{lang}/HelpText";
            String pathToCheck;

            if (type == "MainMenu")
            {
                pathToCheck = $"{basePath}/MainMenu/va_{subtype}_{id}";
                if (CheckFileExists(pathToCheck)) return pathToCheck;
                return null;
            }

            if (character != CharacterId.NONE)
            {
                pathToCheck = $"{basePath}/{character}/{type}/va_{id}";
                if (CheckFileExists(pathToCheck)) return pathToCheck;

                if (!String.IsNullOrEmpty(name))
                {
                    pathToCheck = $"{basePath}/{character}/{type}/va_{name}";
                    if (CheckFileExists(pathToCheck)) return pathToCheck;
                }
            }

            pathToCheck = $"{basePath}/{type}/va_{id}";
            if (CheckFileExists(pathToCheck)) return pathToCheck;

            if (!String.IsNullOrEmpty(name))
            {
                pathToCheck = $"{basePath}/{type}/va_{name}";
                if (CheckFileExists(pathToCheck)) return pathToCheck;
            }

            return null;
        }

        private Boolean CheckFileExists(String path)
        {
            LogEchoS.Debug($"[DialogHelpVA] [TEST AUDIO] -> Sounds/{path}");
            return AssetManager.HasAssetOnDisc($"Sounds/{path}.akb", true, true) ||
                   AssetManager.HasAssetOnDisc($"Sounds/{path}.ogg", true, false);
        }

        private void PlayResolvedPath(String path)
        {
            int customID = path.GetHashCode();
            LogEchoS.Debug($"[DialogHelpVA] [SUCCESS] Audio found and playing: {path}");
            _lastPlayedSound = VoicePlayer.CreateLoadThenPlayVoice(customID, path);
        }

        private void StopAudio()
        {
            if (_lastPlayedSound != null)
            {
                ISdLibAPIProxy.Instance.SdSoundSystem_SoundCtrl_Stop(_lastPlayedSound.SoundID, 0);
                _lastPlayedSound = null;
            }
        }
    }
}
