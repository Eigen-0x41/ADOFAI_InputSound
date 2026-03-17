using HarmonyLib;
using SkyHook;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;
using UnityModManagerNet;

namespace InputSound
{
    static class Main
    {
        public static UnityModManager.ModEntry.ModLogger Logger;

        [Conditional("DEBUG")]
        static public void DebugPrinting(string str)
        {
            Logger.Log(str);
        }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            Logger = modEntry.Logger;

            DebugPrinting("Enable Debug Log Enable.");

            return true;
        }
    }

    [HarmonyPatch(typeof(SkyHookManager))]
    internal class SkyHookManagerPatch
    {
        public static HitSoundQueue hitSoundQueue = new HitSoundQueue();
        private static void PlayHitSound(double dspTime, bool isKeyPressed)
        {
            AudioSource audSrc = null;
            if (!hitSoundQueue.TryGetHitSound(dspTime, isKeyPressed, out audSrc))
                return;

            audSrc.Play();
        }

        [HarmonyPatch("HookCallback", new Type[] { typeof(SkyHookEvent) }), HarmonyPrefix]
        private static void HookCallbackPrefix(SkyHookManager __instance, SkyHookEvent ev)
        {
            var srcControllerInstance = scrController.instance;
            if (srcControllerInstance == null)
                return;
            if (srcControllerInstance.paused)
                return;

            _ = Task.Run(() => PlayHitSound(scrConductor.instance.dspTime, ev.Type == SkyHook.EventType.KeyPressed));
        }
    }

    [HarmonyPatch(typeof(AudioManager))]
    internal class AudioManagerPatch
    {
        [HarmonyPatch(nameof(AudioManager.Play), new Type[] { typeof(string), typeof(double), typeof(AudioMixerGroup), typeof(float), typeof(int) }), HarmonyPrefix]
        private static bool PlayPrefix(string snd, double time, AudioMixerGroup group, float volume, int priority)
        {
            // ノーツ音ではないヒットサウンドの検出用。
            // ADOFAI本体にハードコードされています。
            if (priority == 10)
                return true;

            SkyHookManagerPatch.hitSoundQueue.EnrollHitSound(snd, time, group, volume, priority);

            return false;
        }
    }

    [HarmonyPatch(typeof(scrConductor))]
    internal class scrConductorPatch
    {
        [HarmonyPatch(nameof(scrConductor.PlayWithEndTime), new Type[] { typeof(string), typeof(double), typeof(double), typeof(float), typeof(int) }), HarmonyPrefix]
        private static void PlayWithEndTimePrefix(string snd, double time, double endTime, float volume = 1f, int priority = 128)
        {
            SkyHookManagerPatch.hitSoundQueue.EnrollReleaseHitSound(endTime);
        }
    }
}
