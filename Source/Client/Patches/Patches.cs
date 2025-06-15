using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Multiplayer.Client.Patches;
using UnityEngine;
using Verse;
using Verse.AI;
using System.Security.Policy;
using Verse.AI.Group;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(GameEnder))]
    [HarmonyPatch(nameof(GameEnder.CheckOrUpdateGameOver))]
    public static class GameEnderPatch
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    static class JobTrackerStartFixFrames
    {
        static FieldInfo FrameCountField = AccessTools.Field(typeof(RealTime), nameof(RealTime.frameCount));
        static MethodInfo FrameCountReplacementMethod = AccessTools.Method(typeof(JobTrackerStartFixFrames), nameof(FrameCountReplacement));

        // Transpilers should be a last resort but there's no better way to patch this
        // and handling this case properly might prevent some crashes and help with debugging
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.operand == FrameCountField)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, FrameCountReplacementMethod);
                }
            }
        }

        static int FrameCountReplacement(int frameCount, Pawn_JobTracker tracker)
        {
            return tracker.pawn.Map.AsyncTime()?.eventCount ?? frameCount;
        }
    }

    [HarmonyPatch(typeof(Dialog_BillConfig), MethodType.Constructor)]
    [HarmonyPatch(new[] { typeof(Bill_Production), typeof(IntVec3) })]
    public static class DialogPatch
    {
        static void Postfix(Dialog_BillConfig __instance)
        {
            __instance.absorbInputAroundWindow = false;
        }
    }

    [HarmonyPatch(typeof(WindowStack))]
    [HarmonyPatch(nameof(WindowStack.WindowsForcePause), MethodType.Getter)]
    public static class WindowsPausePatch
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = false;
        }
    }

    /*
    [HarmonyPatch(typeof(Thing), nameof(Thing.ExposeData))]
    public static class PawnExposeDataFirst
    {
        public static Container<Map>? state;

        // Postfix so Thing's faction is already loaded
        static void Postfix(Thing __instance)
        {
            if (Multiplayer.Client == null) return;
            if (!(__instance is Pawn)) return;
            if (__instance.Faction == null) return;
            if (Find.FactionManager == null) return;
            if (Find.FactionManager.AllFactionsListForReading.Count == 0) return;

            __instance.Map.PushFaction(__instance.Faction);
            ThingContext.Push(__instance);
            state = __instance.Map;
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExposeData))]
    public static class PawnExposeDataLast
    {
        static void Postfix()
        {
            if (PawnExposeDataFirst.state != null)
            {
                ThingContext.Pop();
                PawnExposeDataFirst.state.PopFaction();
                PawnExposeDataFirst.state = null;
            }
        }
    }*/

    // why patch it if it's commented out?
    //[HarmonyPatch(typeof(PawnTweener), nameof(PawnTweener.PreDrawPosCalculation))]
    public static class PreDrawPosCalcPatch
    {
        static void Prefix()
        {
            //if (MapAsyncTimeComp.tickingMap != null)
            //    SimpleProfiler.Pause();
        }

        static void Postfix()
        {
            //if (MapAsyncTimeComp.tickingMap != null)
            //    SimpleProfiler.Start();
        }
    }

    public static class ValueSavePatch
    {
        public static bool DoubleSave_Prefix(string label, ref double value)
        {
            if (Scribe.mode != LoadSaveMode.Saving) return true;
            Scribe.saver.WriteElement(label, value.ToString("G17"));
            return false;
        }

        public static bool FloatSave_Prefix(string label, ref float value)
        {
            if (Scribe.mode != LoadSaveMode.Saving) return true;
            Scribe.saver.WriteElement(label, value.ToString("G9"));
            return false;
        }
    }

    //[HarmonyPatch(typeof(Log), nameof(Log.Warning))]
    public static class CrossRefWarningPatch
    {
        private static Regex regex = new Regex(@"^Could not resolve reference to object with loadID ([\w.-]*) of type ([\w.<>+]*)\. Was it compressed away");
        public static bool ignore;

        // The only non-generic entry point during cross reference resolving
        static bool Prefix(string text)
        {
            if (Multiplayer.Client == null || ignore) return true;

            ignore = true;

            GroupCollection groups = regex.Match(text).Groups;
            if (groups.Count == 3)
            {
                string loadId = groups[1].Value;
                string typeName = groups[2].Value;
                // todo
                return false;
            }

            ignore = false;

            return true;
        }
    }

    [HarmonyPatch(typeof(UI), nameof(UI.MouseCell))]
    public static class MouseCellPatch
    {
        public static IntVec3? result;

        static void Postfix(ref IntVec3 __result)
        {
            if (result.HasValue)
                __result = result.Value;
        }
    }

    [HarmonyPatch(typeof(GenWorld), nameof(GenWorld.MouseTile))]
    public static class MouseTilePatch
    {
        public static PlanetTile? result;

        static void Postfix(ref PlanetTile __result)
        {
            if (result.HasValue)
                __result = result.Value;
        }
    }

    [HarmonyPatch(typeof(KeyBindingDef), nameof(KeyBindingDef.IsDownEvent), MethodType.Getter)]
    public static class KeyIsDownPatch
    {
        public static bool? shouldQueue;

        static bool Prefix(KeyBindingDef __instance) => !(__instance == KeyBindingDefOf.QueueOrder && shouldQueue.HasValue);

        static void Postfix(KeyBindingDef __instance, ref bool __result)
        {
            if (__instance == KeyBindingDefOf.QueueOrder && shouldQueue.HasValue)
                __result = shouldQueue.Value;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    static class PawnSpawnSetupMarker
    {
        public static bool currentlyRespawningAfterLoad;

        static void Prefix(bool respawningAfterLoad)
        {
            currentlyRespawningAfterLoad = respawningAfterLoad;
        }

        static void Finalizer()
        {
            currentlyRespawningAfterLoad = false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.ResetToCurrentPosition))]
    static class PatherResetPatch
    {
        static bool Prefix() => !PawnSpawnSetupMarker.currentlyRespawningAfterLoad;
    }

    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    static class LoadGameMarker
    {
        public static bool loading;

        static void Prefix() => loading = true;
        static void Finalizer() => loading = false;
    }

    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Start))]
    static class RootPlayStartMarker
    {
        public static bool starting;

        static void Prefix() => starting = true;
        static void Finalizer() => starting = false;
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), new[] { typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>), typeof(bool), typeof(bool), typeof(Action) })]
    static class CancelRootPlayStartLongEvents
    {
        public static bool cancel;

        static bool Prefix()
        {
            if (RootPlayStartMarker.starting && cancel) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(ScreenFader), nameof(ScreenFader.SetColor))]
    static class DisableScreenFade1
    {
        static bool Prefix() => LongEventHandler.eventQueue.All(e => e.eventTextKey == "MpLoading");
    }

    [HarmonyPatch(typeof(ScreenFader), nameof(ScreenFader.StartFade), typeof(Color), typeof(float), typeof(float))]
    static class DisableScreenFade2
    {
        static bool Prefix() => LongEventHandler.eventQueue.All(e => e.eventTextKey == "MpLoading");
    }

    [HarmonyPatch(typeof(ThingGrid), nameof(ThingGrid.Register))]
    static class DontEnlistNonSaveableThings
    {
        static bool Prefix(Thing t) => t.def.isSaveable;
    }

    [HarmonyPatch(typeof(IncidentDef), nameof(IncidentDef.TargetAllowed))]
    static class GameConditionIncidentTargetPatch
    {
        static void Postfix(IncidentDef __instance, IIncidentTarget target, ref bool __result)
        {
            if (Multiplayer.Client == null) return;

            if (__instance.workerClass == typeof(IncidentWorker_MakeGameCondition) || __instance.workerClass == typeof(IncidentWorker_Aurora))
                __result = target.IncidentTargetTags().Contains(IncidentTargetTagDefOf.Map_PlayerHome);
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_Aurora), nameof(IncidentWorker_Aurora.AuroraWillEndSoon))]
    static class IncidentWorkerAuroraPatch
    {
        static void Postfix(Map map, ref bool __result)
        {
            if (Multiplayer.Client == null) return;

            if (map != Multiplayer.MapContext)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(NamePlayerFactionAndSettlementUtility), nameof(NamePlayerFactionAndSettlementUtility.CanNameAnythingNow))]
    static class NoNamingInMultiplayer
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(DirectXmlSaver), nameof(DirectXmlSaver.XElementFromObject), typeof(object), typeof(Type), typeof(string), typeof(FieldInfo), typeof(bool))]
    static class ExtendDirectXmlSaver
    {
        public static bool extend;

        static bool Prefix(object obj, Type expectedType, string nodeName, FieldInfo owningField, ref XElement __result)
        {
            if (!extend) return true;
            if (obj == null) return true;

            if (obj is Array arr)
            {
                var elementType = arr.GetType().GetElementType();
                var listType = typeof(List<>).MakeGenericType(elementType);
                __result = DirectXmlSaver.XElementFromObject(Activator.CreateInstance(listType, arr), listType, nodeName, owningField);
                return false;
            }

            string content = null;

            if (obj is Type type)
                content = type.FullName;
            else if (obj is MethodBase method)
                content = method.MethodDesc();
            else if (obj is Delegate del)
                content = del.Method.MethodDesc();

            if (content != null)
            {
                __result = new XElement(nodeName, content);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.Pause))]
    static class TickManagerPausePatch
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(WorldRoutePlanner), nameof(WorldRoutePlanner.ShouldStop), MethodType.Getter)]
    static class RoutePlanner_ShouldStop_Patch
    {
        static void Postfix(WorldRoutePlanner __instance, ref bool __result)
        {
            if (Multiplayer.Client == null) return;

            // Ignore unpausing
            if (__result && __instance.active && WorldRendererUtility.WorldRendered)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), typeof(PawnGenerationRequest))]
    static class CancelSyncDuringPawnGeneration
    {
        static void Prefix() => Multiplayer.dontSync = true;
        static void Finalizer() => Multiplayer.dontSync = false;
    }

    [HarmonyPatch(typeof(DesignationDragger), nameof(DesignationDragger.UpdateDragCellsIfNeeded))]
    static class CancelUpdateDragCellsIfNeeded
    {
        static bool Prefix() => !Multiplayer.ExecutingCmds;
    }

    [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority))]
    static class WorkPrioritySameValue
    {
        [HarmonyPriority(MpPriority.MpFirst + 1)]
        static bool Prefix(Pawn_WorkSettings __instance, WorkTypeDef w, int priority) => __instance.GetPriority(w) != priority;
    }

    [HarmonyPatch(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.AreaRestrictionInPawnCurrentMap), MethodType.Setter)]
    static class AreaRestrictionSameValue
    {
        [HarmonyPriority(MpPriority.MpFirst + 1)]
        static bool Prefix(Pawn_PlayerSettings __instance, Area value) => __instance.AreaRestrictionInPawnCurrentMap != value;
    }

    [HarmonyPatch]
    static class PatchQuestChoices
    {
        // It's the only method with a Choice
        static MethodBase TargetMethod()
        {
            return AccessTools.FirstMethod(
                AccessTools.FirstInner(typeof(MainTabWindow_Quests),
                    t => t.GetFields().Any(f => f.Name == "localChoice")
                ), m => !m.IsConstructor);
        }

        static bool Prefix(QuestPart_Choice.Choice ___localChoice)
        {
            if (Multiplayer.Client == null) return true;

            foreach (var part in Find.QuestManager.QuestsListForReading.SelectMany(q => q.parts).OfType<QuestPart_Choice>())
            {
                int index = part.choices.IndexOf(___localChoice);

                if (index >= 0)
                {
                    Choose(part, index);
                    return false;
                }
            }

            // Should not happen!
            Log.Error("Multiplayer :: Choice without QuestPart_Choice... you're about to desync.");
            return true;
        }

        // Registered in SyncMethods.cs
        internal static void Choose(QuestPart_Choice part, int index)
        {
            part.Choose(part.choices[index]);
        }
    }

    [HarmonyPatch(typeof(MoteMaker), nameof(MoteMaker.MakeStaticMote))]
    [HarmonyPatch(new[] { typeof(Vector3), typeof(Map), typeof(ThingDef), typeof(float), typeof(bool), typeof(float) })]
    static class FixNullMotes
    {
        static Dictionary<Type, Mote> cache = new();

        static void Postfix(ThingDef moteDef, ref Mote __result)
        {
            if (__result != null) return;

            if (moteDef.mote.needsMaintenance) return;

            var thingClass = moteDef.thingClass;

            if (cache.TryGetValue(thingClass, out Mote value))
            {
                __result = value;
            }
            else
            {
                __result = (Mote)Activator.CreateInstance(thingClass);

                cache.Add(thingClass, __result);
            }

            __result.def = moteDef;
        }
    }

    [HarmonyPatch(typeof(DiaOption), nameof(DiaOption.Activate))]
    static class NodeTreeDialogSync
    {
        static bool Prefix(DiaOption __instance)
        {
            if (Multiplayer.session == null || !SyncUtil.isDialogNodeTreeOpen || !(__instance.dialog is Dialog_NodeTree dialog))
            {
                SyncUtil.isDialogNodeTreeOpen = false;
                return true;
            }

            // Get the current node, find the index of the option on it, and call a (synced) method
            var currentNode = dialog.curNode;
            int index = currentNode.options.FindIndex(x => x == __instance);
            if (index >= 0)
                SyncDialogOptionByIndex(index);

            return false;
        }

        [SyncMethod]
        internal static void SyncDialogOptionByIndex(int position)
        {

            // Make sure we have the correct dialog and data
            if (position >= 0)
            {
                var dialog = Find.WindowStack.WindowOfType<Dialog_NodeTree>();

                if (dialog != null && position < dialog.curNode.options.Count)
                {
                    SyncUtil.isDialogNodeTreeOpen = false; // Prevents infinite loop, otherwise PreSyncDialog would call this method over and over again
                    var option = dialog.curNode.options[position]; // Get the correct DiaOption
                    option.Activate(); // Call the Activate method to actually "press" the button

                    if (!option.resolveTree) SyncUtil.isDialogNodeTreeOpen = true; // In case dialog is still open, we mark it as such

                    // Try opening the trading menu if the picked option was supposed to do so (caravan meeting, trading option)
                    if (Multiplayer.Client != null && Multiplayer.WorldComp.trading.Any(t => t.trader is Caravan))
                        Find.WindowStack.Add(new TradingWindow());
                }
                else SyncUtil.isDialogNodeTreeOpen = false;
            }
            else SyncUtil.isDialogNodeTreeOpen = false;
        }
    }


    [HarmonyPatch(typeof(Dialog_NodeTree), nameof(Dialog_NodeTree.PostClose))]
    static class NodeTreeDialogMarkClosed
    {
        // Set the dialog as closed in here as well just in case
        static void Prefix() => SyncUtil.isDialogNodeTreeOpen = false;
    }



    [HarmonyPatch]
    static class SetGodModePatch
    {
        static IEnumerable<MethodInfo> TargetMethods()
        {
            yield return AccessTools.Method(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.DrawButtons));
            yield return AccessTools.Method(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.DevToolStarterOnGUI));
            yield return AccessTools.PropertySetter(typeof(Prefs), nameof(Prefs.DevMode));
        }

        static void Prefix(ref bool __state)
        {
            __state = DebugSettings.godMode;
        }

        static void Postfix(bool __state)
        {
            if (Multiplayer.Client != null && __state != DebugSettings.godMode)
                Multiplayer.GameComp.SetGodMode(Multiplayer.session.playerId, DebugSettings.godMode);
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    static class AllowCurrentMapNullWhenLoading
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var list = insts.ToList();

            var strIndex = list.FirstIndexOf(i =>
                "Current map is null after loading but there are maps available. Setting current map to [0].".Equals(i.operand)
            );

            // Remove Log.Error(str) call and setting value=0
            list.RemoveAt(strIndex);
            list.RemoveAt(strIndex);
            list.RemoveAt(strIndex);
            list.RemoveAt(strIndex);

            return list;
        }
    }

    [HarmonyPatch(typeof(PawnTextureAtlas), MethodType.Constructor)]
    static class PawnTextureAtlasCtorPatch
    {
        static void Postfix(PawnTextureAtlas __instance)
        {
            // Pawn ids can change during deserialization when fixing local (negative) ids in CrossRefHandler_Clear_Patch
            __instance.frameAssignments = new Dictionary<Pawn, PawnTextureAtlasFrameSet>(
                IdentityComparer<Pawn>.Instance
            );
        }
    }

    [HarmonyPatch]
    public static class StoragesKeepsTheirOwners
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CompBiosculpterPod), nameof(CompBiosculpterPod.PostExposeData))]
        static void PostCompBiosculpterPod(CompBiosculpterPod __instance)
            => FixStorage(__instance, __instance.allowedNutritionSettings);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CompChangeableProjectile), nameof(CompChangeableProjectile.PostExposeData))]
        static void PostCompChangeableProjectile(CompChangeableProjectile __instance)
            => FixStorage(__instance, __instance.allowedShellsSettings);

        // Fix syncing of copy/paste due to null StorageSettings.owner by assigning the parent
        // in ExposeData. The patched types omit passing/assigning self as the owner by passing
        // Array.Empty<object>() as the argument to expose data on StorageSetting.
        static void FixStorage(IStoreSettingsParent __instance, StorageSettings ___allowedNutritionSettings)
        {
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                ___allowedNutritionSettings.owner ??= __instance;
        }
    }

    [HarmonyPatch(typeof(MoteAttachLink), nameof(MoteAttachLink.UpdateDrawPos))]
    static class MoteAttachLinkUsesTruePosition
    {
        static void Prefix() => DrawPosPatch.returnTruePosition = true;

        static void Finalizer() => DrawPosPatch.returnTruePosition = false;
    }

    //======================================================================================
    // BEGIN DESYNC FIX: DETERMINISTIC MAP TICKERS
    //======================================================================================

    // COMMENT: The patches below (WildAnimalSpawnerTickTraceIgnore, etc.) are bandages that only hide
    // the desync error message but don't fix the underlying problem. They are being replaced by the
    // DeterministicMapTickers patch, which fixes the desync at its source.
    /*
    [HarmonyPatch(typeof(WildAnimalSpawner), "WildAnimalSpawnerTick")]
    public static class WildAnimalSpawnerTickTraceIgnore
    {
        static bool Prefix() => !Multiplayer.InInterface;
    }

    [HarmonyPatch(typeof(WildPlantSpawner), "WildPlantSpawnerTick")]
    public static class WildPlantSpawnerTickTraceIgnore
    {
        static bool Prefix() => !Multiplayer.InInterface;
    }

    [HarmonyPatch(typeof(SteadyEnvironmentEffects), "SteadyEnvironmentEffectsTick")]
    public static class SteadyEnvironmentEffectsTickTraceIgnore
    {
        static bool Prefix() => !Multiplayer.InInterface;
    }
    */
    /*
    /// <summary>
    /// This patch fixes a common source of desyncs in RimWorld 1.6. Several background
    /// map processes use the random number generator (Rand) every tick. Without a
    /// consistent seed, each player's game generates different random outcomes, leading to a desync.
    ///
    /// This patch targets the main Tick methods for these systems and wraps them in a seeded
    /// random state block. This guarantees that for any given map tick, every player's
    /// game will produce the exact same "random" results for weather, animal spawning, etc.
    /// </summary>
    [HarmonyPatch]
    static class DeterministicMapTickers
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            // These methods all run periodically on a specific map and use Rand.
            yield return AccessTools.Method(typeof(WildPlantSpawner), nameof(WildPlantSpawner.WildPlantSpawnerTick));
            yield return AccessTools.Method(typeof(WildAnimalSpawner), nameof(WildAnimalSpawner.WildAnimalSpawnerTick));
            yield return AccessTools.Method(typeof(SteadyEnvironmentEffects), nameof(SteadyEnvironmentEffects.SteadyEnvironmentEffectsTick));
            yield return AccessTools.Method(typeof(WeatherDecider), nameof(WeatherDecider.WeatherDeciderTick));
            yield return AccessTools.Method(typeof(PassingShipManager), nameof(PassingShipManager.PassingShipManagerTick));
            yield return AccessTools.Method(typeof(UndercaveMapComponent), nameof(UndercaveMapComponent.MapComponentTick));
            yield return AccessTools.Method(typeof(LordManager), nameof(LordManager.LordManagerTick));
            yield return AccessTools.Method(typeof(FireWatcher), nameof(FireWatcher.FireWatcherTick));
        }

        /// <summary>
        /// This transpiler injects PushState at the beginning and PopState at the end (inside a finally block).
        /// </summary>
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase original)
        {
            var label = generator.DefineLabel();
            var tryBegin = generator.BeginExceptionBlock();

            // Push RNG State
            yield return new CodeInstruction(OpCodes.Ldarg_0); // `this`
            yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(original.DeclaringType, "map")); // this.map
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DeterministicMapTickers), nameof(PushRNGState)));

            // Original method instructions
            foreach (var instruction in instructions)
            {
                yield return instruction;
            }

            // Add label for jumping after finally block
            var last = new CodeInstruction(OpCodes.Nop);
            last.labels.Add(label);

            // Finally block to pop RNG state
            generator.BeginFinallyBlock();
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DeterministicMapTickers), nameof(PopRNGState)));
            generator.EndExceptionBlock();

            yield return last;
        }

        public static void PushRNGState(Map map)
        {
            if (Multiplayer.Client != null && map?.AsyncTime() != null)
            {
                Rand.PushState(map.AsyncTime().mapTicks);
            }
        }

        public static void PopRNGState()
        {
            if (Multiplayer.Client != null)
            {
                Rand.PopState();
            }
        }
    }

    //======================================================================================
    // END DESYNC FIX
    //======================================================================================
    */
    //======================================================================================
    // BEGIN CARAVAN NEEDS DESYNC FIX
    //======================================================================================

    /// <summary>
    /// This patch fixes a desync caused by the caravan needs system, specifically joy satisfaction.
    /// The vanilla game uses an unseeded random number generator to pick a joy activity for pawns,
    /// which causes each client to make a different choice and desynchronize.
    /// This patch wraps the entire needs ticking logic in a seeded random state block, ensuring
    /// that all random events within (joy, chemical satisfaction, etc.) are deterministic.
    /// </summary>
    [HarmonyPatch(typeof(Caravan_NeedsTracker), nameof(Caravan_NeedsTracker.NeedsTrackerTickInterval))]
    public static class Caravan_NeedsTracker_Tick_Sync
    {
        /// <summary>
        /// Before the needs logic runs, we push a new state to the random number generator.
        /// The state is seeded with the caravan's unique ID, which is deterministic.
        /// We use a direct parameter reference `___caravan` for efficiency.
        /// </summary>
        static void Prefix(Caravan ___caravan)
        {
            if (Multiplayer.Client != null)
            {
                // Seed with the caravan's unique ID to ensure deterministic results.
                Rand.PushState(___caravan.ID);
            }
        }

        /// <summary>
        /// After the needs logic has finished, we pop the state to restore the RNG.
        /// This prevents our deterministic seed from affecting other parts of the game.
        /// A finalizer is used to guarantee this runs even if the original method has an error.
        /// </summary>
        static void Finalizer()
        {
            if (Multiplayer.Client != null)
            {
                Rand.PopState();
            }
        }
    }

    //======================================================================================
    // END CARAVAN NEEDS DESYNC FIX
    //======================================================================================

    //======================================================================================
    // BEGIN CARAVAN FORAGING DESYNC FIX
    //======================================================================================

    /// <summary>
    /// This patch fixes a desync caused by caravan foraging. The vanilla game uses an unseeded
    /// random number generator to determine the amount of foraged food, causing each client
    /// to calculate a different amount and desynchronize.
    /// This patch wraps the foraging tick logic in a seeded random state block, using the
    /// caravan's unique ID as the seed. This ensures every client gets the exact same
    /// "random" result for foraging on any given tick.
    /// </summary>
    [HarmonyPatch(typeof(Caravan_ForageTracker), nameof(Caravan_ForageTracker.ForageTrackerTickInterval))]
    public static class Caravan_ForageTracker_Tick_Sync
    {
        /// <summary>
        /// Before the foraging logic runs, we push a new state to the random number generator.
        /// The state is seeded with the caravan's unique ID, which is deterministic.
        /// We use a direct parameter reference `___caravan` for efficiency.
        /// </summary>
        static void Prefix(Caravan ___caravan)
        {
            if (Multiplayer.Client != null)
            {
                // Seed with the caravan's unique ID to ensure deterministic foraging results.
                Rand.PushState(___caravan.ID);
            }
        }

        /// <summary>
        /// After the foraging logic has finished, we pop the state to restore the RNG.
        /// This prevents our deterministic seed from affecting other parts of the game.
        /// A finalizer is used to guarantee this runs even if the original method has an error.
        /// </summary>
        static void Finalizer()
        {
            if (Multiplayer.Client != null)
            {
                Rand.PopState();
            }
        }
    }

    //======================================================================================
    // END CARAVAN FORAGING DESYNC FIX
    //======================================================================================

    //======================================================================================
    // BEGIN CARAVAN AND CAMP SYNC FIXES
    //======================================================================================

    /// <summary>
    /// This patch intercepts the caravan's "DEV: Teleport to destination" gizmo.
    /// Instead of letting it execute its logic locally, we replace its action
    /// with one that calls our new `SyncMethods.CaravanTeleport`.
    /// </summary>
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    static class SyncCaravanDevTeleportPatch
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Caravan __instance)
        {
            foreach (var gizmo in __result)
            {
                // We identify the dev gizmo by its unique label.
                if (gizmo is Command_Action cmd && cmd.defaultLabel == "DEV: Teleport to destination")
                {
                    // This is the new, synced action.
                    cmd.action = () =>
                    {
                        // The targeting UI is a local effect and is safe to call.
                        Find.WorldTargeter.BeginTargeting(
                            (GlobalTargetInfo targetInfo) => // This is the callback after a tile is selected.
                            {
                                if (!targetInfo.IsValid) return true; // Abort if invalid target.

                                // Instead of teleporting locally, we call our new SyncMethod.
                                // The server will distribute this command to all players.
                                SyncMethods.CaravanTeleport(__instance, targetInfo.Tile);
                                return true; // Return true to close the targeting interface.
                            },
                            canTargetTiles: true
                        );
                    };

                    // Optional: Add a note to the description for clarity during debugging.
                    cmd.defaultDesc += "\n\n(Multiplayer: Synced for all players)";
                }

                yield return gizmo;
            }
        }
    }

    /// <summary>
    /// This patch intercepts the final step of abandoning a settlement or camp.
    /// Instead of running the local abandonment logic, it calls our new `SyncMethods.SyncedAbandonSettlement`.
    /// This is a robust way to sync the action, as it captures it regardless of the UI flow (e.g., confirmation dialogs).
    /// </summary>
    [HarmonyPatch(typeof(SettlementAbandonUtility), "Abandon", new Type[] { typeof(MapParent) })]
    static class SettlementAbandonUtility_Abandon_Sync
    {
        static bool Prefix(MapParent settlement)
        {
            // If we are in a multiplayer game...
            if (Multiplayer.Client != null)
            {
                // ...call our synced method instead of the original logic.
                SyncMethods.SyncedAbandonSettlement(settlement);

                // ...and prevent the original (unsynced) method from running.
                return false;
            }

            // If not in multiplayer, let the original method run as usual.
            return true;
        }
    }

    //======================================================================================
    // END CARAVAN AND CAMP SYNC FIXES
    //======================================================================================

    //======================================================================================
    // BEGIN CARAVAN DRUG POLICY DESYNC FIX
    //======================================================================================

    /// <summary>
    /// This patch fixes a desync caused by pawns taking scheduled drugs in a caravan.
    /// The check for whether to take a drug involves an unseeded random chance, which
    /// leads to different outcomes on different clients.
    /// This patch seeds the random number generator before the check occurs.
    /// </summary>
    [HarmonyPatch(typeof(CaravanDrugPolicyUtility), nameof(CaravanDrugPolicyUtility.CheckTakeScheduledDrugs))]
    public static class Caravan_DrugPolicyUtility_Tick_Sync
    {
        static void Prefix(Caravan caravan)
        {
            if (Multiplayer.Client != null)
            {
                Rand.PushState(caravan.ID);
            }
        }

        static void Finalizer()
        {
            if (Multiplayer.Client != null)
            {
                Rand.PopState();
            }
        }
    }

    //======================================================================================
    // END CARAVAN DRUG POLICY DESYNC FIX
    //======================================================================================


    //======================================================================================
    // BEGIN CARAVAN TENDING DESYNC FIX
    //======================================================================================

    /// <summary>
    /// This patch fixes a desync caused by medical tending in a caravan.
    /// The quality of a medical tend is random, and without a seed, each client would
    /// calculate a different quality, heal a different amount, and desynchronize.
    /// This patch seeds the random number generator before any tending occurs.
    /// </summary>
    [HarmonyPatch(typeof(CaravanTendUtility), nameof(CaravanTendUtility.CheckTend))]
    public static class Caravan_TendUtility_Tick_Sync
    {
        static void Prefix(Caravan caravan)
        {
            if (Multiplayer.Client != null)
            {
                Rand.PushState(caravan.ID);
            }
        }

        static void Finalizer()
        {
            if (Multiplayer.Client != null)
            {
                Rand.PopState();
            }
        }
    }

    //======================================================================================
    // END CARAVAN TENDING DESYNC FIX
    //======================================================================================

    //======================================================================================
    // BEGIN CARAVAN BABY TRACKER DESYNC FIX
    //======================================================================================

    /// <summary>
    /// This patch fixes a desync caused by the Ideo exposure system for babies in a caravan.
    /// The game uses an unseeded random number generator to determine if a baby gains
    /// Ideo exposure, leading to different outcomes on different clients.
    /// This patch seeds the random number generator before the logic runs.
    /// </summary>
    [HarmonyPatch(typeof(Caravan_BabyTracker), nameof(Caravan_BabyTracker.TickInterval))]
    public static class Caravan_BabyTracker_Tick_Sync
    {
        // The caravan object is private in Caravan_BabyTracker, so we use `___caravan` to access it.
        static void Prefix(Caravan ___caravan)
        {
            if (Multiplayer.Client != null)
            {
                Rand.PushState(___caravan.ID);
            }
        }

        static void Finalizer()
        {
            if (Multiplayer.Client != null)
            {
                Rand.PopState();
            }
        }
    }

    //======================================================================================
    // END CARAVAN BABY TRACKER DESYNC FIX
    //======================================================================================

    //======================================================================================
    // BEGIN WORLD TEMPERATURE DESYNC FIX
    //======================================================================================

    /// <summary>
    /// This patch fixes a desync caused by the global temperature calculation system.
    /// When calculating the temperature for a caravan's tile (which has no map), the game
    /// uses an unseeded random number, causing each client to get a different temperature
    /// and desynchronize. This patch seeds the entire world temperature tick, making it
    /// deterministic.
    /// </summary>
    [HarmonyPatch(typeof(TileTemperaturesComp), nameof(TileTemperaturesComp.WorldComponentTick))]
    public static class TileTemperaturesComp_Tick_Sync
    {
        static void Prefix()
        {
            if (Multiplayer.Client != null)
            {
                // Seed with the world's tick count, as this is a world-level component.
                // This ensures every client gets the same "random" temperature fluctuations.
                Rand.PushState(Multiplayer.AsyncWorldTime.worldTicks);
            }
        }

        static void Finalizer()
        {
            if (Multiplayer.Client != null)
            {
                Rand.PopState();
            }
        }
    }

    //======================================================================================
    // END WORLD TEMPERATURE DESYNC FIX
    //======================================================================================

    //======================================================================================
    // BEGIN STORYTELLER DESYNC FIX
    //======================================================================================

    /// <summary>
    /// This patch fixes a desync caused by the global Storyteller. When the storyteller
    /// ticks in the world context (not tied to a specific map), its random decisions are
    /// not seeded, causing divergence. This patch seeds the storyteller and story watcher
    /// ticks with the multiplayer world time.
    /// </summary>
    [HarmonyPatch]
    public static class Storyteller_Tick_Sync
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            // These are all global, world-level tickers that use randomness.
            // We seed all of them here to ensure deterministic outcomes.
            yield return AccessTools.Method(typeof(Storyteller), nameof(Storyteller.StorytellerTick));
            yield return AccessTools.Method(typeof(StoryWatcher), nameof(StoryWatcher.StoryWatcherTick));
            yield return AccessTools.Method(typeof(QuestManager), nameof(QuestManager.QuestManagerTick));
            yield return AccessTools.Method(typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.WorldObjectsHolderTick));

            yield return AccessTools.Method(typeof(FactionManager), nameof(FactionManager.FactionManagerTick));
            yield return AccessTools.Method(typeof(WorldPawns), nameof(WorldPawns.WorldPawnsTick));
            yield return AccessTools.Method(typeof(GameConditionManager), nameof(GameConditionManager.GameConditionManagerTick));

        }

        /// <summary>
        /// This prefix seeds the RNG with the global multiplayer world tick count.
        /// It runs before any of the targeted methods.
        /// </summary>
        static void Prefix()
        {
            if (Multiplayer.Client != null)
            {
                Rand.PushState(Multiplayer.AsyncWorldTime.worldTicks);
            }
        }

        /// <summary>
        /// The finalizer ensures the RNG state is always restored.
        /// </summary>
        static void Finalizer()
        {
            if (Multiplayer.Client != null)
            {
                Rand.PopState();
            }
        }
    }

    //======================================================================================
    // END STORYTELLER DESYNC FIX
    //======================================================================================
    /*
    //======================================================================================
    // BEGIN GAME CONDITION DESYNC FIX
    //======================================================================================

    /// <summary>
    /// This patch fixes a desync caused by the map-level GameConditionManager.
    /// It uses Rand to determine event durations. This patch seeds its tick with the
    /// map's async time to ensure deterministic results.
    /// It requires a separate patch because its map field is named `ownerMap`, not `map`.
    /// </summary>
    [HarmonyPatch(typeof(GameConditionManager), nameof(GameConditionManager.GameConditionManagerTick))]
    public static class GameConditionManager_Tick_Sync
    {
        /// <summary>
        /// Harmony is instructed to find the field named `ownerMap` and pass it as the `map` parameter.
        /// </summary>
        static void Prefix(Map ___ownerMap)
        {
            if (Multiplayer.Client != null && ___ownerMap?.AsyncTime() != null)
            {
                Rand.PushState(___ownerMap.AsyncTime().mapTicks);
            }
        }

        static void Finalizer()
        {
            if (Multiplayer.Client != null)
            {
                Rand.PopState();
            }
        }
    }

    //======================================================================================
    // END GAME CONDITION DESYNC FIX
    //======================================================================================
    */

    //======================================================================================
    // BEGIN DETERMINISTIC MAP TICKER FIXES (Replaces old DeterministicMapTickers patch)
    //======================================================================================

    /// <summary>
    /// The following patches ensure that all periodic map-level systems that use randomness
    /// are deterministically seeded with the map's async tick count. Each system gets its
    /// own patch for maximum stability and to prevent Push/Pop mismatches.
    /// </summary>

    [HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.WildPlantSpawnerTick))]
    public static class WildPlantSpawner_Tick_Sync
    {
        static void Prefix(Map ___map)
        {
            if (Multiplayer.Client != null && ___map?.AsyncTime() != null)
                Rand.PushState(___map.AsyncTime().mapTicks);
        }
        static void Finalizer()
        {
            if (Multiplayer.Client != null)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(WildAnimalSpawner), nameof(WildAnimalSpawner.WildAnimalSpawnerTick))]
    public static class WildAnimalSpawner_Tick_Sync
    {
        static void Prefix(Map ___map)
        {
            if (Multiplayer.Client != null && ___map?.AsyncTime() != null)
                Rand.PushState(___map.AsyncTime().mapTicks);
        }
        static void Finalizer()
        {
            if (Multiplayer.Client != null)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(SteadyEnvironmentEffects), nameof(SteadyEnvironmentEffects.SteadyEnvironmentEffectsTick))]
    public static class SteadyEnvironmentEffects_Tick_Sync
    {
        static void Prefix(Map ___map)
        {
            if (Multiplayer.Client != null && ___map?.AsyncTime() != null)
                Rand.PushState(___map.AsyncTime().mapTicks);
        }
        static void Finalizer()
        {
            if (Multiplayer.Client != null)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(WeatherDecider), nameof(WeatherDecider.WeatherDeciderTick))]
    public static class WeatherDecider_Tick_Sync
    {
        static void Prefix(Map ___map)
        {
            if (Multiplayer.Client != null && ___map?.AsyncTime() != null)
                Rand.PushState(___map.AsyncTime().mapTicks);
        }
        static void Finalizer()
        {
            if (Multiplayer.Client != null)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(PassingShipManager), nameof(PassingShipManager.PassingShipManagerTick))]
    public static class PassingShipManager_Tick_Sync
    {
        static void Prefix(Map ___map)
        {
            if (Multiplayer.Client != null && ___map?.AsyncTime() != null)
                Rand.PushState(___map.AsyncTime().mapTicks);
        }
        static void Finalizer()
        {
            if (Multiplayer.Client != null)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(UndercaveMapComponent), nameof(UndercaveMapComponent.MapComponentTick))]
    public static class UndercaveMapComponent_Tick_Sync
    {
        static void Prefix(Map ___map)
        {
            if (Multiplayer.Client != null && ___map?.AsyncTime() != null)
                Rand.PushState(___map.AsyncTime().mapTicks);
        }
        static void Finalizer()
        {
            if (Multiplayer.Client != null)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(LordManager), nameof(LordManager.LordManagerTick))]
    public static class LordManager_Tick_Sync
    {
        static void Prefix(Map ___map)
        {
            if (Multiplayer.Client != null && ___map?.AsyncTime() != null)
                Rand.PushState(___map.AsyncTime().mapTicks);
        }
        static void Finalizer()
        {
            if (Multiplayer.Client != null)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(FireWatcher), nameof(FireWatcher.FireWatcherTick))]
    public static class FireWatcher_Tick_Sync
    {
        static void Prefix(Map ___map)
        {
            if (Multiplayer.Client != null && ___map?.AsyncTime() != null)
                Rand.PushState(___map.AsyncTime().mapTicks);
        }
        static void Finalizer()
        {
            if (Multiplayer.Client != null)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(GameConditionManager), nameof(GameConditionManager.GameConditionManagerTick))]
    public static class GameConditionManager_Tick_Sync
    {
        // This patch correctly accesses the `ownerMap` field.
        static void Prefix(Map ___ownerMap)
        {
            if (Multiplayer.Client != null && ___ownerMap?.AsyncTime() != null)
                Rand.PushState(___ownerMap.AsyncTime().mapTicks);
        }
        static void Finalizer()
        {
            if (Multiplayer.Client != null)
                Rand.PopState();
        }
    }

    //======================================================================================
    // END DETERMINISTIC MAP TICKER FIXES
    //======================================================================================
}
