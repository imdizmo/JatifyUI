using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Jatify.UI
{
    // A mod built for an older Valheim fails in ways that look like everyone else's bug: a field or method the game no longer
    // has, a type that can't start. One of them (Backpacks 1.3.8) broke every save and three other mods' features. When the
    // game logs such an error from a plugin's own code, say once which mod it is, in the log and on screen (user).
    internal static class DeadMods
    {
        private static readonly string[] Signs = { "MissingFieldException", "MissingMethodException", "TypeLoadException", "TypeInitializationException", "EntryPointNotFoundException" };
        private static readonly object _lock = new object();
        private static readonly Queue<string> _found = new Queue<string>();
        private static readonly HashSet<string> _said = new HashSet<string>();
        private static Dictionary<Assembly, BepInEx.PluginInfo> _byAsm;
        private static bool _on;

        internal static void Start() { if (_on) return; _on = true; Application.logMessageReceivedThreaded += OnLog; }
        internal static void Stop() { if (!_on) return; _on = false; Application.logMessageReceivedThreaded -= OnLog; }

        private static void OnLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (!Signs.Any(s => condition.Contains(s) || (stack != null && stack.Contains(s)))) return;
            lock (_lock) { if (_found.Count < 64) _found.Enqueue(condition + "\n" + stack); }   // ponytail: worked out on the main thread (Tick); capped so a flood can't grow it
        }

        // main thread: which plugin's code is in the trace; one warning per mod per session
        internal static void Tick()
        {
            if (!_on) return;
            string trace;
            lock (_lock) { if (_found.Count == 0) return; trace = _found.Dequeue(); }
            var info = Owner(trace); if (info == null) return;
            var name = info.Metadata.Name; if (!_said.Add(name)) return;
            var say = $"{name} {info.Metadata.Version} is failing on this Valheim version (it was made for an older one). It can break saving and other mods: update it (it may have moved to another site: Hexium or Nexus Mods) or remove it.";
            Log.Warn(say + " First error: " + trace.Split('\n')[0]);
            if (MessageHud.instance != null && Player.m_localPlayer != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, say);
            else _late.Add(say);
        }
        private static readonly List<string> _late = new List<string>();
        internal static void TickLate()
        {   // found at the title screen: said once the world is up
            if (_late.Count == 0 || MessageHud.instance == null || Player.m_localPlayer == null) return;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, _late[0]); _late.RemoveAt(0);
        }

        // the first frame "at Namespace.Type.Method (" whose type lives in a plugin's assembly (not Jatify's, not the game's)
        private static BepInEx.PluginInfo Owner(string trace)
        {
            if (_byAsm == null)
            {
                _byAsm = new Dictionary<Assembly, BepInEx.PluginInfo>();
                foreach (var pi in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                    if (pi?.Instance != null) _byAsm[pi.Instance.GetType().Assembly] = pi;
            }
            foreach (var line in trace.Split('\n'))
            {
                var l = line.Trim(); if (!l.StartsWith("at ")) continue;
                int paren = l.IndexOf(" ("); if (paren < 0) paren = l.IndexOf('('); if (paren < 4) continue;
                var full = l.Substring(3, paren - 3).Trim(); int dot = full.LastIndexOf('.'); if (dot <= 0) continue;
                var typeName = full.Substring(0, dot); int gen = typeName.IndexOf('['); if (gen > 0) typeName = typeName.Substring(0, gen);   // "CustomSyncedValue`1[T]" if (typeName.EndsWith(".")) typeName = typeName.TrimEnd('.');   // ".cctor" / ".ctor"
                if (typeName.StartsWith("(wrapper") || typeName.StartsWith("System.") || typeName.StartsWith("UnityEngine.") || typeName.StartsWith("HarmonyLib.")) continue;
                foreach (var kv in _byAsm)
                {
                    if (kv.Key == typeof(DeadMods).Assembly) continue;
                    Type t = null; try { t = kv.Key.GetType(typeName, false); } catch { }
                    if (t != null) return kv.Value;
                }
            }
            return null;
        }
    }
}
