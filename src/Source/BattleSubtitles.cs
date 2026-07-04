using Assets.Sources.Scripts.UI.Common;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Memoria.EchoS
{
    public class BattleSubtitles : PersistenSingleton<BattleSubtitles>
    {
        private readonly Dictionary<UInt16, HUDMessageChild> activeSubtitles = new Dictionary<UInt16, HUDMessageChild>();
        private readonly Dictionary<BattleUnit, String> createQueue = new Dictionary<BattleUnit, String>();
        private readonly HashSet<HUDMessageChild> deleteQueue = new HashSet<HUDMessageChild>();

        public Boolean Enabled = false;

        public void Update()
        {
            foreach(var entry in createQueue)
            {
                try
                {
                    if (activeSubtitles.TryGetValue(entry.Key.Id, out HUDMessageChild message))
                    {
                        Hide(entry.Key.Id, message.Label);
                    }

                    btl2d.GetIconPosition(entry.Key.Data, btl2d.ICON_POS_NUMBER, out Transform attach, out Vector3 offset);
                    message = HUDMessage.Instance.Show(attach, entry.Value, HUDMessage.MessageStyle.NONE, offset, 0);
                    message.GetComponent<UIWidget>().color = FF9TextTool.White;
                    message.GetComponent<TweenPosition>().enabled = false;
                    message.GetComponent<TweenAlpha>().enabled = false;
                    activeSubtitles[entry.Key.Id] = message;
                }
                catch { }
            }
            createQueue.Clear();

            foreach(HUDMessageChild message in deleteQueue)
            {
                try
                {
                    HUDMessage.Instance.ReleaseObject(message);
                }
                catch { }
            }
            deleteQueue.Clear();

        }

        public void Show(BattleUnit speaker, String text)
        {
            if (!Enabled || speaker == null || text.Length < 3 || text.StartsWith("“$")) return;

            createQueue[speaker] = text;
        }

        public void Hide(UInt16 speakerID, String text)
        {
            if (!Enabled) return;
            if (activeSubtitles.TryGetValue(speakerID, out HUDMessageChild message) && message.Label == text)
            {
                deleteQueue.Add(message);
                activeSubtitles.Remove(speakerID);
            }
        }

        public void ClearAll()
        {
            foreach (HUDMessageChild message in activeSubtitles.Values)
            {
                deleteQueue.Add(message);
            }
            activeSubtitles.Clear();
        }

        private static void ListComponents(GameObject go, int indent = 0)
        {
            Log.Message($"[DEBUG] {new string(' ', indent * 4)}> {go.name} position: {go.transform.localPosition}");
            var comps = go.GetComponents<Component>();
            if (comps != null && comps.Length > 0)
            {
                foreach (Component c in comps)
                {
                    if (c is Transform || ((c as MonoBehaviour)?.isActiveAndEnabled ?? false)) continue;
                    if (c is UIWidget)
                    {
                        var w = c as UIWidget;
                        Log.Message($"[DEBUG]   {new string(' ', indent * 4)}{c} {c.GetType()} w: {w.width} h: {w.height} localScale: {c.transform.localScale} localPosition; {c.transform.localPosition}");
                    }
                    else
                    {
                        Log.Message($"[DEBUG]   {new string(' ', indent * 4)}{c} {c.GetType()}");
                    }
                }
                foreach (Transform child in go.transform)
                {
                    if (child.gameObject != go) ListComponents(child.gameObject, indent + 1);
                }
            }
        }
    }
}
