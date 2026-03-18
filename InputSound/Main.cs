using HarmonyLib;
using SkyHook;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;
using UnityModManagerNet;

namespace InputSound
{
    static class Main
    {
        private static Harmony harmony = null;

        internal static HitSoundQueue hitSoundQueue = new HitSoundQueue();
        internal static UnityModManager.ModEntry.ModLogger Logger;

        private static bool isModEnabled = false;
        public static bool IsEnabled => (harmony != null) && isModEnabled;


        [Conditional("DEBUG")]
        static public void DebugPrinting(string str)
        {
            Logger.Log(str);
        }

        public static void Load(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            modEntry.OnToggle = OnToggle;
        }

        public static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            if (value)
            {
                StartMod(modEntry);
            }
            else
            {
                StopMod(modEntry);
            }
            return true;
        }

        private static void StartMod(UnityModManager.ModEntry modEntry)
        {
            if (harmony == null)
            {
                harmony = new Harmony(modEntry.Info.Id);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            isModEnabled = true;
        }

        private static void StopMod(UnityModManager.ModEntry modEntry)
        {
            //harmony.UnpatchAll(modEntry.Info.Id);
            //harmony = null;
            isModEnabled = false;
        }
    }

    [HarmonyPatch(typeof(SkyHookManager))]
    internal class SkyHookManagerPatch
    {
        [HarmonyPatch("HookCallback", new Type[] { typeof(SkyHookEvent) }), HarmonyPrefix]
        private static void HookCallbackPrefix(SkyHookEvent ev)
        {
            if (!Main.IsEnabled)
                return;

            Main.hitSoundQueue.PlayHitSoundAsync(ev.Type == SkyHook.EventType.KeyPressed,
                Task.Run(() =>
                {
                    if (RDC.auto)
                        return false;

                    var scrCtrlIns = scrController.instance;
                    if (scrCtrlIns == null)
                        return false;
                    if (scrCtrlIns.paused)
                        return false;

                    if (scrCtrlIns.currFloor.nextfloor != null)
                        if (scrCtrlIns.currFloor.auto && scrCtrlIns.currFloor.nextfloor.auto)
                            return false;

                    return true;
                }));
        }
    }

    [HarmonyPatch(typeof(AudioManager))]
    internal class AudioManagerPatch
    {
        [HarmonyPatch(nameof(AudioManager.Play), new Type[] { typeof(string), typeof(double), typeof(AudioMixerGroup), typeof(float), typeof(int) }), HarmonyPrefix]
        private static bool PlayPrefix(ref AudioSource __result, string snd, double time, AudioMixerGroup group, float volume, int priority)
        {
            if (!Main.IsEnabled)
                return true;

            if (priority != 128)
                return true;

            __result = Main.hitSoundQueue.EnrollHitSound(snd, time, group, volume, priority);

            return false;
        }
    }

    [HarmonyPatch(typeof(scrConductor))]
    internal class scrConductorPatch
    {
        [HarmonyPatch(nameof(scrConductor.PlayWithEndTime), new Type[] { typeof(string), typeof(double), typeof(double), typeof(float), typeof(int) }), HarmonyPrefix]
        private static void PlayWithEndTimePrefix(double endTime)
        {
            if (!Main.IsEnabled)
                return;

            Main.hitSoundQueue.EnrollReleaseHitSound(endTime);
        }

        [HarmonyPatch(nameof(scrConductor.PlayHitTimes), new Type[] { }), HarmonyPostfix]
        private static void PlayHitTimesPostfix()
        {
            Main.hitSoundQueue.Clear();
        }
    }

    [HarmonyPatch(typeof(scrController))]
    internal class scrControllerPatch
    {
        [HarmonyPatch(nameof(scrController.Hit), new Type[] { typeof(bool) }), HarmonyPostfix]
        private static void HitPostfix(scrController __instance)
        {
            if (!Main.IsEnabled)
                return;

            bool isAutoTile = __instance.currFloor.auto;
            Main.hitSoundQueue.PlayHitSoundAsync(true,
                 Task.Run(() => RDC.auto || isAutoTile));
        }
    }
}
