using HarmonyLib;
using SkyHook;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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
        private static void PlayHitSound(bool isKeyReleased)
        {
            try
            {
                if (isKeyReleased && !AudioManagerPatch.UpdateAndDoReleaseHitSound())
                    return;

                var audSrc = AudioManagerPatch.CurrentHitSound;
                if (audSrc == null)
                    return;

                //audSrc.volume = scrConductor.instance.hitSoundVolume;
                audSrc.Play();
            }
            catch (Exception e)
            {
                Main.Logger.LogException("PlayHitSound()", e);
            }
        }

        [HarmonyPatch("HookCallback", new Type[] { typeof(SkyHookEvent) }), HarmonyPrefix]
        private static void HookCallbackPrefix(SkyHookManager __instance, SkyHookEvent ev)
        {
            var srcControllerInstance = scrController.instance;
            if (srcControllerInstance == null)
                return;
            if (srcControllerInstance.paused)
                return;

            Task.Run(() => PlayHitSound(ev.Type == SkyHook.EventType.KeyReleased));
        }
    }

    [HarmonyPatch(typeof(AudioManager))]
    internal class AudioManagerPatch
    {
        public static AudioSource CurrentHitSound => (hitSoundBuffer.Count > 0) ? hitSoundBuffer.Values[0] : null;
        private static double currentDelay => (hitSoundBuffer.Count > 0) ? hitSoundBuffer.Keys[0] : double.MaxValue;
        private static double previousDelay = double.MaxValue;

        private static SortedList<double, AudioSource> hitSoundBuffer = new SortedList<double, AudioSource>();

        private static readonly DateTime GameDateTime = DateTime.Now;

        [HarmonyPatch(nameof(AudioManager.Play), new Type[] { typeof(string), typeof(double), typeof(AudioMixerGroup), typeof(float), typeof(int) }), HarmonyPrefix]
        private static bool HookCallbackPrefix(string snd, double time, AudioMixerGroup group, float volume, int priority)
        {
            // ノーツ音ではないヒットサウンドの検出用。
            // ADOFAI本体にハードコードされています。
            if (priority == 10)
                return true;

            var __instance = AudioManager.Instance;

            AudioSource audioSource = __instance.MakeSource(snd, 0);
            audioSource.pitch = 1f;

            if (group != null)
                audioSource.outputAudioMixerGroup = group;
            else
                audioSource.outputAudioMixerGroup = __instance.fallbackMixerGroup;

            audioSource.volume = volume;
            audioSource.priority = priority;
            float num = (audioSource.clip ? audioSource.clip.length : float.PositiveInfinity);

            if (!hitSoundBuffer.ContainsKey(time))
                hitSoundBuffer.Add(time, audioSource);

            return false;
        }

        public static bool UpdateAndDoReleaseHitSound()
        {
            var scrCondIns = scrConductor.instance;
            double dspTime = scrCondIns.dspTime;
            while (dspTime > currentDelay)
            {
                Main.DebugPrinting($"HookCallback: [Operation: delete], {currentDelay}, {dspTime}");
                previousDelay = currentDelay;
                hitSoundBuffer.Remove(currentDelay);
            }

            return scrConductorPatch.DoReleaseHitSound(dspTime, previousDelay, currentDelay);
        }
    }

    [HarmonyPatch(typeof(scrConductor))]
    internal class scrConductorPatch
    {
        private static double currentDelay => (keyReleaseSoundSet.Count > 0) ? keyReleaseSoundSet.First() : double.MaxValue;

        private static SortedSet<double> keyReleaseSoundSet = new SortedSet<double>();

        public static bool DoReleaseHitSound(double dspTime, double previousDelay, double currentPlaingDelay)
        {
            while (dspTime > currentDelay)
                keyReleaseSoundSet.Remove(currentDelay);

            return (previousDelay < currentDelay) && (currentDelay < currentPlaingDelay);
        }

        [HarmonyPatch(nameof(scrConductor.PlayWithEndTime), new Type[] { typeof(string), typeof(double), typeof(double), typeof(float), typeof(int) }), HarmonyPrefix]
        private static void PlayWithEndTimePrefix(string snd, double time, double endTime, float volume = 1f, int priority = 128)
        {
            keyReleaseSoundSet.Add(endTime);
        }
    }
}
