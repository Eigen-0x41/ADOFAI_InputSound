using HarmonyLib;
using SkyHook;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using UnityModManagerNet;

namespace InputSound
{
    static class Main
    {
        private static Harmony harmony = null;
        public static Settings settings = null;

        internal static UnityModManager.ModEntry.ModLogger Logger;

        public static bool IsEnabled => !(harmony is null);

        private static void StartMod(UnityModManager.ModEntry modEntry)
        {
            if (harmony is null)
            {
                harmony = new Harmony(modEntry.Info.Id);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            if (HitSoundQueue.instance is null)
                HitSoundQueue.instance = new HitSoundQueue();
        }

        private static void StopMod(UnityModManager.ModEntry modEntry)
        {
            harmony.UnpatchAll(modEntry.Info.Id);
            harmony = null;
            HitSoundQueue.instance = null;
        }
        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Draw(modEntry);
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
        }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            modEntry.OnToggle = OnToggle;

            settings = Settings.Load<Settings>(modEntry);
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            return true;
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
    }

    [HarmonyPatch(typeof(SkyHookManager))]
    internal static class SkyHookManagerPatch
    {
        [HarmonyPatch("HookCallback", new Type[] { typeof(SkyHookEvent) }), HarmonyPrefix]
        private static void HookCallbackPrefix(SkyHookEvent ev)
        {
            if (!Main.IsEnabled)
                return;
            if (HitSoundQueue.instance is null)
                return;

            HitSoundQueue.instance.PlayHitSoundAsync(ev.Type == SkyHook.EventType.KeyReleased,
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

        public bool ReplaceTryGetValueForDictionary(HitSound key, out double value)
        {
            value = 0.0;
            if (Main.settings.IsUseHitSoundOffset)
                return ADOBase.gc.hitSoundOffsets.TryGetValue(key, out value);
            return true;
        }

        //callvirt instance bool class [mscorlib]System.Collections.Generic.Dictionary`2<valuetype HitSound, float64>::TryGetValue(!0, !1&)
        [HarmonyPatch(nameof(scrConductor.PlayHitTimes), new Type[] { }), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> PlayHitTimesTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var InjectonPoints = new CILInjectionPointFinder[]{
                new CILInjectionPointFinder(
                    new List<(OpCode, object)>{
                        ValueTuple.Create<OpCode, object>(OpCodes.Callvirt, typeof(ADOBase).GetProperty("gc").PropertyType.GetField("hitSoundOffsets").FieldType.GetMethod("TryGetValue")),
                    },
                    (instruction) => instruction.operand = typeof(scrConductorPatch).GetMethod(nameof(ReplaceTryGetValueForDictionary))
                    ),
            };

            foreach (var instruction in instructions)
            {
                foreach (var injectionPoint in InjectonPoints)
                {
                    if (injectionPoint.IsInjectionPoint(instruction))
                    {
                        injectionPoint.Injection(instruction);
                    }
                }
                yield return instruction;
            }

            foreach (var injectionPoint in InjectonPoints)
                if (!injectionPoint.IsInjected)
                    Main.Logger.Error("scrConductor.PlayHitTimes(): Faild to transpiling.");
        }

        [HarmonyPatch("Update", new Type[] { }), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> UpdateTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var assembly = Assembly.GetAssembly(typeof(scrConductor));
            var holdSoundsDataVolume = assembly.GetType($"{nameof(scrConductor)}+HoldSoundsData").GetField("volume");
            var hitSoundsDataVolume = assembly.GetType($"{nameof(scrConductor)}+HitSoundsData").GetField("volume");

            var InjectonPoints = new CILInjectionPointFinder[]{
                new CILInjectionPointFinder(
                    new List<(OpCode, object)>{
                        ValueTuple.Create<OpCode, object>(OpCodes.Ldfld, holdSoundsDataVolume),
                        ValueTuple.Create<OpCode, object>(OpCodes.Ldc_I4, 128),
                        ValueTuple.Create<OpCode, object>(OpCodes.Call, typeof(AudioManager).Method(nameof(AudioManager.Play))),
                    },
                    (instruction) => instruction.operand = typeof(HitSoundQueue).Method(nameof(HitSoundQueue.HoldSoundEnroller))
                    ),
                new CILInjectionPointFinder(
                    new List<(OpCode, object)>{
                        ValueTuple.Create<OpCode, object>(OpCodes.Ldfld, hitSoundsDataVolume),
                        ValueTuple.Create<OpCode, object>(OpCodes.Ldc_I4, 128),
                        ValueTuple.Create<OpCode, object>(OpCodes.Call, typeof(AudioManager).Method(nameof(AudioManager.Play))),
                    },
                    (instruction) => instruction.operand = typeof(HitSoundQueue).Method(nameof(HitSoundQueue.HitSoundEnroller))
                    ),
            };

            foreach (var instruction in instructions)
            {
                foreach (var injectionPoint in InjectonPoints)
                {
                    if (injectionPoint.IsInjectionPoint(instruction))
                    {
                        injectionPoint.Injection(instruction);
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
    internal static class scrControllerPatch
    {
        [HarmonyPatch(nameof(scrController.Hit), new Type[] { typeof(bool) }), HarmonyPostfix]
        private static void HitPostfix(scrController __instance)
        {
            if (!Main.IsEnabled)
                return;
            if (HitSoundQueue.instance is null)
                return;

            bool isAutoTile = __instance.currFloor.auto;

            HitSoundQueue.instance.PlayHitSoundAsync(false,
                 Task.Run(() => RDC.auto || isAutoTile));
        }
    }
}
