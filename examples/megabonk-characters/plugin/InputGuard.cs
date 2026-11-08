using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UmMegabonkStudio
{
    // While a prompt box is focused, legacy UnityEngine.Input reports no keys.
    // The panel reads Event.current itself and does not use the stripped text widget.
    // A hidden panel does not install this block. Mouse buttons are left alone.
    static class InputGuard
    {
        static bool _loggedMiss;

        public static void Install(Harmony harmony)
        {
            var input = typeof(UnityEngine.Input);
            PatchBool(harmony, AccessTools.Method(input, "GetKeyDown", new[] { typeof(KeyCode) }));
            PatchBool(harmony, AccessTools.Method(input, "GetKey", new[] { typeof(KeyCode) }));
            PatchBool(harmony, AccessTools.Method(input, "GetKeyUp", new[] { typeof(KeyCode) }));
            PatchBool(harmony, AccessTools.Method(input, "GetKeyDown", new[] { typeof(string) }));
            PatchBool(harmony, AccessTools.Method(input, "GetKey", new[] { typeof(string) }));
            PatchBool(harmony, AccessTools.Method(input, "GetKeyUp", new[] { typeof(string) }));
            PatchBool(harmony, AccessTools.Method(input, "GetButton", new[] { typeof(string) }));
            PatchBool(harmony, AccessTools.Method(input, "GetButtonDown", new[] { typeof(string) }));
            PatchBool(harmony, AccessTools.Method(input, "GetButtonUp", new[] { typeof(string) }));
            PatchBool(harmony, AccessTools.PropertyGetter(input, "anyKey"));
            PatchBool(harmony, AccessTools.PropertyGetter(input, "anyKeyDown"));
            PatchString(harmony, AccessTools.PropertyGetter(input, "inputString"));
        }

        static void PatchBool(Harmony harmony, MethodInfo method)
        {
            if (method == null)
            {
                Miss();
                return;
            }
            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(InputGuard), nameof(BlockBool)));
            }
            catch (Exception ex)
            {
                Plugin.Warn("Could not block " + method.Name + " while typing: " + ex.GetType().Name);
            }
        }

        static void PatchString(Harmony harmony, MethodInfo method)
        {
            if (method == null)
            {
                Miss();
                return;
            }
            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(InputGuard), nameof(BlockString)));
            }
            catch (Exception ex)
            {
                Plugin.Warn("Could not block " + method.Name + " while typing: " + ex.GetType().Name);
            }
        }

        static void Miss()
        {
            if (_loggedMiss) return;
            _loggedMiss = true;
            Plugin.Warn("A UnityEngine.Input method was missing. Typing may still reach the game. Probe InputLegacyModule for GetKeyDown if that happens. Do not send the DLL.");
        }

        public static bool BlockBool(ref bool __result)
        {
            if (!StudioWindow.BlockGameplayKeys) return true;
            __result = false;
            return false;
        }

        public static bool BlockString(ref string __result)
        {
            if (!StudioWindow.BlockGameplayKeys) return true;
            __result = "";
            return false;
        }
    }
}
