using Memoria.Prime;
using System;

namespace Memoria.EchoS
{
    public static class LogEchoS
    {
        public static bool DebugEnable = false;

        public static void Message(String msg)
        {
            Log.Message($"[Echo-S] {msg}");
        }

        public static void Debug(string text)
        {
            if (DebugEnable)
                Log.Message("[D][Echo-S] " + text);
        }

        public static void Warning(String msg)
        {
            Log.Warning($"  [Echo-S] {msg}");
        }
    }
}
