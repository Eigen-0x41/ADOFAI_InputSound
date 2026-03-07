using HarmonyLib;
using SkyHook;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
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
            scrConductorPatch.HitSoundsDataPatch.LoadMannual(modEntry, harmony);

            Logger = modEntry.Logger;

            DebugPrinting("Enable Debug Log Enable.");

            return true;
        }
    }

    [HarmonyPatch(typeof(SkyHookManager))]
    public class SkyHookManagerPatch
    {
        private static void PlayHitSound()
        {
            try
            {
                AudioManagerPatch.UpdateToLatest();

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
            if (ev.Type != SkyHook.EventType.KeyPressed)
                return;

            var srcControllerInstance = scrController.instance;
            if (srcControllerInstance == null)
                return;
            if (srcControllerInstance.paused)
                return;

            Task.Run(() => PlayHitSound());
        }
    }

    [HarmonyPatch(typeof(AudioManager))]
    public class AudioManagerPatch
    {
        public static AudioSource CurrentHitSound => (currentDelay != double.MaxValue) ? hitSoundBuffer.First().Value : null;
        private static double currentDelay => (hitSoundBuffer.Count > 0) ? hitSoundBuffer.First().Key : double.MaxValue;

        private static SortedList<double, AudioSource> hitSoundBuffer = new SortedList<double, AudioSource>();

        private static readonly DateTime GameDateTime = DateTime.Now;

        [HarmonyPatch(nameof(AudioManager.Play), new Type[] { typeof(string), typeof(double), typeof(AudioMixerGroup), typeof(float), typeof(int) }), HarmonyPrefix]
        private static bool HookCallbackPrefix(string snd, double time, AudioMixerGroup group, float volume, int priority)
        {
            var __instance = AudioManager.Instance;

            AudioSource audioSource = __instance.MakeSource(snd, 0);
            audioSource.pitch = 1f;
            if (group != null)
            {
                audioSource.outputAudioMixerGroup = group;
            }
            else
            {
                audioSource.outputAudioMixerGroup = __instance.fallbackMixerGroup;
            }

            audioSource.volume = volume;
            audioSource.priority = priority;
            float num = (audioSource.clip ? audioSource.clip.length : float.PositiveInfinity);

            if (!hitSoundBuffer.ContainsKey(time))
                hitSoundBuffer.Add(time, audioSource);

            return false;
        }

        public static void UpdateToLatest()
        {
            double dspTime = scrConductor.instance.dspTime - 0.03125;
            while (currentDelay < dspTime)
            {
                Main.DebugPrinting($"HookCallback: [Operation: delete], {currentDelay}, {dspTime}");
                hitSoundBuffer.Remove(currentDelay);
            }
        }
    }

    [HarmonyPatch(typeof(scrConductor))]
    public class scrConductorPatch
    {
        public class HitSoundsDataPatch
        {
            public static void LoadMannual(in UnityModManager.ModEntry modEntry, Harmony harmony)
            {
                // var Asembly = Assembly.GetAssembly(typeof(scrConductor));
                // var mOriginal = AccessTools.Constructor(Asembly.GetType($"{nameof(scrConductor)}+HitSoundsData"), new Type[] { typeof(HitSound), typeof(double), typeof(float) });
                // modEntry.Logger.Log($"Original: {mOriginal}");
                // var mPrefix = AccessTools.Method(typeof(HitSoundsDataPatch), "HitSoundsDataPrefix");
                // modEntry.Logger.Log($"Prefix  : {mPrefix}");
                // harmony.Patch(mOriginal, new HarmonyMethod(mPrefix));
            }

            private static void HitSoundsDataPrefix(HitSound hitSound, double time, ref float volume)
            {
                volume = 0.0f;
            }
        }
    }
}
