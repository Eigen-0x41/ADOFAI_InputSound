using HarmonyLib;
using SkyHook;
using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityModManagerNet;

namespace InputSound
{
    static class Main
    {
        public static UnityModManager.ModEntry ModEntry;


        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            scrConductorPatch.HitSoundsDataPatch.LoadMannual(modEntry, harmony);

            ModEntry = modEntry;

            return true;
        }
    }

    [HarmonyPatch(typeof(SkyHookManager))]
    public class SkyHookManagerPatch
    {

        [HarmonyPatch("HookCallback", new Type[] { typeof(SkyHookEvent) }), HarmonyPrefix]
        public static async void HookCallbackPrefix(SkyHookManager __instance, SkyHookEvent ev)
        {
            await Task.Run(() =>
            {
                if (ev.Type != SkyHook.EventType.KeyPressed)
                    return;

                var srcCondIns = scrConductor.instance;
                AudioManager.Play("snd" + srcCondIns.hitSound, 0, srcCondIns.hitSoundGroup, srcCondIns.hitSoundVolume, 10);
            });
        }
    }

    [HarmonyPatch(typeof(scrConductor))]
    public class scrConductorPatch
    {
        public class HitSoundsDataPatch
        {
            public static void LoadMannual(in UnityModManager.ModEntry modEntry, Harmony harmony)
            {
                var Asembly = Assembly.GetAssembly(typeof(scrConductor));
                var mOriginal = AccessTools.Constructor(Asembly.GetType($"{nameof(scrConductor)}+HitSoundsData"), new Type[] { typeof(HitSound), typeof(double), typeof(float) });
                modEntry.Logger.Log($"Original: {mOriginal}");
                var mPrefix = AccessTools.Method(typeof(HitSoundsDataPatch), "HitSoundsDataPrefix");
                modEntry.Logger.Log($"Prefix  : {mPrefix}");
                harmony.Patch(mOriginal, new HarmonyMethod(mPrefix));
            }

            public static void HitSoundsDataPrefix(HitSound hitSound, double time, ref float volume)
            {
                volume = 0.0f;
            }
        }
    }
}
