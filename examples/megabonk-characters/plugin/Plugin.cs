using System;
using System.Collections.Generic;
using System.IO;
using Assets.Scripts.Saves___Serialization.Progression.Achievements;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace UmMegabonkStudio
{
    [BepInPlugin(Plugin.Guid, "Megabonk Studio", "0.3.6")]
    public class Plugin : BasePlugin
    {
        public const string Guid = "um.megabonk.studio";
        internal static ManualLogSource LogSource;
        internal static string JsonPath;
        internal static bool UiStopped;
        static bool _uiDead;

        public override void Load()
        {
            LogSource = Log;
            var dir = Path.Combine(Paths.PluginPath, "UmMegabonkStudio");
            Directory.CreateDirectory(dir);
            JsonPath = Path.Combine(dir, "characters.json");
            Info("Megabonk Studio 0.3.6. Drafts: " + JsonPath);
            Info("F7 is the in-game editor key. OnGUI has not run yet. Leaderboard uploads should stay Off. This plugin does not change that switch and does not write the save folder.");
            var harmony = new Harmony(Guid);
            InputGuard.Install(harmony);
            var load = AccessTools.Method(typeof(DataManager), nameof(DataManager.Load));
            if (load == null)
            {
                Log.LogError("DISABLED: DataManager.Load was not found. No characters will be added.");
            }
            else
            {
                harmony.Patch(load, postfix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.AfterLoad)));
            }
            PatchText(harmony, nameof(UnlockableBase.GetName), nameof(Hooks.NamePrefix));
            PatchText(harmony, nameof(UnlockableBase.GetDescription), nameof(Hooks.DescriptionPrefix));
            var saveLoad = AccessTools.Method(typeof(SaveManager), "Load", new System.Type[] { typeof(bool) });
            if (saveLoad != null)
                harmony.Patch(saveLoad, postfix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.AfterSaveLoad)));
            else
                Warn("SaveManager.Load(bool) was not found. Skin indexes are recorded only at character registration.");
            var tryInit = AccessTools.Method(typeof(CharacterMenu), "TryInit");
            if (tryInit != null)
                harmony.Patch(tryInit, postfix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.AfterMenuInit)));
            else
                Warn("CharacterMenu.TryInit was not found. The grid still reads characterData if the game builds it from there.");
            try
            {
                StudioWindow.Install();
                Info("Studio UI behaviour created. Waiting for OnGUI.");
            }
            catch (Exception ex)
            {
                FailUi("Install", ex);
            }
        }

        static void PatchText(Harmony harmony, string method, string prefix)
        {
            var found = AccessTools.Method(typeof(UnlockableBase), method);
            if (found == null)
            {
                Warn("UnlockableBase." + method + " was not found. Custom names may throw when the menu opens.");
                return;
            }
            harmony.Patch(found, prefix: new HarmonyMethod(typeof(Hooks), prefix));
        }

        internal static void Info(string message) { LogSource.LogInfo(message); }
        internal static void Warn(string message) { LogSource.LogWarning(message); }

        static readonly HashSet<string> _once = new HashSet<string>();

        internal static void WarnOnce(string phase, Exception ex)
        {
            if (LogSource == null || !_once.Add(phase)) return;
            LogSource.LogWarning("Preview stopped during phase=" + phase);
            var guard = 0;
            while (ex != null && guard < 6)
            {
                LogSource.LogWarning(SafeLog(ex.GetType().FullName + ": " + ex.Message));
                if (!string.IsNullOrEmpty(ex.StackTrace))
                    LogSource.LogWarning(SafeLog(ex.StackTrace));
                ex = ex.InnerException;
                guard++;
            }
        }

        internal static void FailUi(string where, Exception ex)
        {
            if (_uiDead) return;
            _uiDead = true;
            UiStopped = true;
            LogSource.LogError("Studio UI stopped during " + where);
            var guard = 0;
            while (ex != null && guard < 6)
            {
                LogSource.LogError(SafeLog(ex.GetType().FullName + ": " + ex.Message));
                if (!string.IsNullOrEmpty(ex.StackTrace))
                    LogSource.LogError(SafeLog(ex.StackTrace));
                ex = ex.InnerException;
                guard++;
            }
            LogSource.LogError("OnGUI will not run again this session, so it cannot keep capturing input. Edit characters.json with `um megabonk studio` and restart.");
        }

        static string SafeLog(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var bearer = text.IndexOf("Bearer ", StringComparison.OrdinalIgnoreCase);
            if (bearer >= 0) text = text.Substring(0, bearer) + "Bearer [redacted]";
            if (text.Length > 1800) text = text.Substring(0, 1800);
            return text;
        }
    }

    static class Hooks
    {
        public static void AfterLoad(DataManager __instance)
        {
            try
            {
                int n = Registrar.RegisterAll(__instance, Plugin.JsonPath);
                Plugin.Info("Studio registered " + n + " character(s).");
            }
            catch (Exception ex)
            {
                Plugin.Warn("Studio registration failed: " + ex);
            }
        }

        public static void AfterSaveLoad(SaveManager __instance, bool loadBackup)
        {
            Registrar.EnsureSaveSkinIndexes();
        }

        public static void AfterMenuInit()
        {
            Plugin.Info("CharacterMenu.TryInit finished. Studio characters are in characterData if registration already ran.");
        }

        public static bool NamePrefix(UnlockableBase __instance, ref string __result)
        {
            if (!Registrar.TryText(__instance, false, out var text)) return true;
            __result = text;
            return false;
        }

        public static bool DescriptionPrefix(UnlockableBase __instance, ref string __result)
        {
            if (!Registrar.TryText(__instance, true, out var text)) return true;
            __result = text;
            return false;
        }
    }
}
