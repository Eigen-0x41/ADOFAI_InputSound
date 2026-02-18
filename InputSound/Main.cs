using HarmonyLib;
using SkyHook;
using System;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using UnityModManagerNet;
using static UnityModManagerNet.UnityModManager.Param;

namespace InputSound
{
    static class Main
    {
        public const string HitSoundName = "HitSound.wav";

        public static AudioClip HitSound;
        public static UnityModManager.ModEntry ModEntry;
        public static bool IsActive;

        private static bool CheckLoadingHitSound(UnityModManager.ModEntry modEntry)
        {
            if (HitSound is null) return false;
            while (true)
            {
                if (HitSound.loadState == AudioDataLoadState.Loading)
                    continue;
                if (HitSound.loadState != AudioDataLoadState.Loaded)
                    break;

                modEntry.Logger.Log($"Done!");
                return true;
            }

            modEntry.Logger.Log($"Could not load. Because {HitSound.loadState}");
            return false;
        }
        private static bool LoadHitSound(UnityModManager.ModEntry modEntry)
        {
            string HitSoundPath = Path.Combine(modEntry.Path, HitSoundName);
            if (!File.Exists(HitSoundPath)) return false;
            var uwm = UnityWebRequestMultimedia.GetAudioClip($"file://{HitSoundPath}", AudioType.WAV);
            uwm.SendWebRequest();

            while (!uwm.isDone)
            {
                // MOD リソースの読み込みをAsyncにするほどではないと判断。
            }

            while (uwm.result != UnityWebRequest.Result.Success)
            {
                if (uwm.result == UnityWebRequest.Result.InProgress)
                    continue;
                modEntry.Logger.Log($"Faild to resouce load. Because {uwm.result.ToString()}: {HitSoundPath}.");
            }

            HitSound = DownloadHandlerAudioClip.GetContent(uwm);
            //if (HitSound is null)
            //{
            //    modEntry.Logger.Log($"Faild to resouce load. Because Null: {HitSoundPath}.");
            //    return false;
            //}
            modEntry.Logger.Log($"Loading Hit Sound: {HitSoundPath}...");
            IsActive = CheckLoadingHitSound(modEntry);
            return IsActive;
        }

        public static bool OnToggle(UnityModManager.ModEntry modEntry, bool isRequestActive)
        {
            IsActive
                = (isRequestActive) ? LoadHitSound(modEntry)
                : false;
            return IsActive;
        }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            //SkyHookManagerPatch.LoadMannual(modEntry, harmony);

            ModEntry = modEntry;
            modEntry.OnToggle = OnToggle;
            return true;
        }
    }

    [HarmonyPatch(typeof(SkyHookManager))] //nameof(SkyHookManager.HookCallBack)
    public class SkyHookManagerPatch
    {
        //public static void LoadMannual(in UnityModManager.ModEntry modEntry, Harmony harmony)
        //{
        //    var mOriginal = AccessTools.Method(typeof(SkyHookManager), "HookCallback", new Type[] { typeof(SkyHookEvent) });
        //    modEntry.Logger.Log($"Original: {mOriginal}");
        //    var mPrefix = AccessTools.Method(typeof(SkyHookManagerPatch), "HookCallbackPrefix", new Type[] { typeof(SkyHookManager), typeof(SkyHookEvent) });
        //    modEntry.Logger.Log($"Prefix  : {mPrefix}");
        //    //var mPostfix = typeof(SkyHookManagerPatch).GetMethod(nameof(SkyHookManagerPatch.HookCallBackPostfix));
        //    harmony.Patch(mOriginal, new HarmonyMethod(mPrefix));
        //}
        private static bool HookCallBackPrefixMain(SkyHookManager __instance, SkyHookEvent ev)
        {
            if (!__instance.requireFocus || SkyHookManager.IsFocused || ev.Type != SkyHook.EventType.KeyPressed)
            {
                SkyHookManager.KeyUpdated.Invoke(ev);
                if (!Main.IsActive) return false;
                AudioManager.Instance.audioSourcePrefab.PlayOneShot(Main.HitSound, 0.125f);
                //AudioManagerPatch.AudSource.PlayOneShot(Main.HitSound, 0.125f);
            }
            return false;
        }

        [HarmonyPatch("HookCallback", new Type[] { typeof(SkyHookEvent) }), HarmonyPrefix]
        public static bool HookCallbackPrefix(SkyHookManager __instance, SkyHookEvent ev)
        {
            //{
            return HookCallBackPrefixMain(__instance, ev);
            //}
            //catch (Exception e)
            //{
            //    Main.ModEntry.Logger.Error(e.ToString());
            //}
            //return true;
        }
    }

    //[HarmonyPatch(typeof(AudioManager))]
    //public class AudioManagerPatch
    //{
    //    public static AudioSource AudSource;

    //    [HarmonyPatch(nameof(AudioManager.Awake)), HarmonyPostfix]
    //    public static void AwakePostfix(AudioManager __instance)
    //    {
    //        AudSource = GetComponent<AudioSource>()
    //        if (AudSource == null)
    //        {
    //            // AudSource = Resources.Load<AudioSource>("Audio Source");
    //        }
    //    }
    //}
}
