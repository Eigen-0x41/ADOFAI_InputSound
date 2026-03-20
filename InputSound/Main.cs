using HarmonyLib;
using SkyHook;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using UnityModManagerNet;

namespace InputSound
{
    static class Main
    {
        private static Harmony harmony = null;

        internal static UnityModManager.ModEntry.ModLogger Logger;

        private static bool isModEnabled = false;
        public static bool IsEnabled => !(harmony is null) && isModEnabled;


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
            if (harmony is null)
            {
                harmony = new Harmony(modEntry.Info.Id);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            if (HitSoundQueue.instance is null)
                HitSoundQueue.instance = new HitSoundQueue();

            isModEnabled = true;
        }

        private static void StopMod(UnityModManager.ModEntry modEntry)
        {
            harmony.UnpatchAll(modEntry.Info.Id);
            harmony = null;
            HitSoundQueue.instance = null;
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
            if (HitSoundQueue.instance is null)
                return;

            HitSoundQueue.instance.PlayHitSoundAsync(ev.Type == SkyHook.EventType.KeyPressed,
                Task.Run(() =>
                {
                    if (RDC.auto)
                        return false;

                    var scrCtrlIns = scrController.instance;
                    if (scrCtrlIns is null)
                        return false;
                    if (scrCtrlIns.paused)
                        return false;

                    if (!(scrCtrlIns.currFloor.nextfloor is null))
                        if (scrCtrlIns.currFloor.auto && scrCtrlIns.currFloor.nextfloor.auto)
                            return false;

                    return true;
                }));
        }
    }

    [HarmonyPatch(typeof(scrConductor))]
    internal class scrConductorPatch
    {
        [HarmonyPatch(nameof(scrConductor.PlayHitTimes), new Type[] { }), HarmonyPostfix]
        private static void PlayHitTimesPostfix()
        {
            if (HitSoundQueue.instance is null)
                return;
            HitSoundQueue.instance.Clear();
        }

        [HarmonyPatch("Update", new Type[] { }), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> UpdateTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var assembly = Assembly.GetAssembly(typeof(scrConductor));
            var hitSoundsDataVolume = assembly.GetType($"{nameof(scrConductor)}+HitSoundsData").GetField("volume");

            var InjectonPoints = new CILInjectionPointFinder[]{
                new CILInjectionPointFinder(
                    new List<(OpCode, object)>{
                        ValueTuple.Create<OpCode, object>(OpCodes.Ldfld, hitSoundsDataVolume),
                        ValueTuple.Create<OpCode, object>(OpCodes.Ldc_I4, 128),
                        ValueTuple.Create<OpCode, object>(OpCodes.Call, typeof(AudioManager).Method(nameof(AudioManager.Play))),
                    })
            };

            foreach (var instruction in instructions)
            {
                foreach (var injectionPoint in InjectonPoints)
                {
                    if (injectionPoint.IsInjectionPoint(instruction))
                    {
                        instruction.operand = typeof(HitSoundQueue).Method(nameof(HitSoundQueue.HitSoundEnroller));
                    }
                }

                yield return instruction;
            }


            foreach (var injectionPoint in InjectonPoints)
                if (!injectionPoint.IsInjected)
                    Main.Logger.Error("scrConductor.Update(): Faild to transpiling.");
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
            if (HitSoundQueue.instance is null)
                return;

            bool isAutoTile = __instance.currFloor.auto;

            HitSoundQueue.instance.PlayHitSoundAsync(true,
                 Task.Run(() => RDC.auto || isAutoTile));
        }
    }
}
