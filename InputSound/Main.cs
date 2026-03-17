using HarmonyLib;
using SkyHook;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
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
        private static bool isAuxEnabled = true;
        public static bool IsEnabled
        {
            get => (harmony != null) && isModEnabled && isAuxEnabled;
            set => isAuxEnabled = value;
        }

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
            Main.hitSoundQueue.PlayHitSound(ev.Type == SkyHook.EventType.KeyPressed,
                Task.Run(() =>
                {
                    if (!Main.IsEnabled)
                        return false;

                    if (RDC.auto)
                        return false;

                    var srcControllerInstance = scrController.instance;
                    if (srcControllerInstance == null)
                        return false;
                    if (srcControllerInstance.paused)
                        return false;
                    if (srcControllerInstance.currFloor != null)
                        if (srcControllerInstance.currFloor.auto)
                            return false;
                    return true;
                }));
        }
    }

    [HarmonyPatch(typeof(AudioManager))]
    internal class AudioManagerPatch
    {
        [HarmonyPatch(nameof(AudioManager.Play), new Type[] { typeof(string), typeof(double), typeof(AudioMixerGroup), typeof(float), typeof(int) }), HarmonyPrefix]
        private static bool PlayPrefix(string snd, double time, AudioMixerGroup group, float volume, int priority)
        {
            if (!Main.IsEnabled)
                return true;

            // 拍子の検出。
            if ((snd == "sndHat") && (priority == 10))
                return true;

            Main.hitSoundQueue.EnrollHitSound(snd, time, group, volume, priority);

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
    }

    [HarmonyPatch(typeof(scrController))]
    internal class scrControllerPatch
    {
        [HarmonyPatch(nameof(scrController.Hit), new Type[] { typeof(bool) }), HarmonyPostfix]
        private static void HitPostfix()
        {
            if (!Main.IsEnabled)
                return;

            Main.hitSoundQueue.PlayHitSound(true,
                 Task.Run(() => RDC.auto || scrController.instance.currFloor.auto));
        }
    }
}
