using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using System.Collections.Generic;
using Unity.Collections;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    [StaticConstructorOnStartup]
    static class PathingDebugSetup
    {
        static PathingDebugSetup()
        {
            if (!MP.enabled) return;
            var harmony = new Harmony("coolnether123.pathingdebug.v1");

            // Trace host-side job broadcast
            harmony.Patch(
                AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob)),
                postfix: new HarmonyMethod(typeof(PathingDebugPatches), nameof(PathingDebugPatches.Postfix_StartJobDebug))
            );

            // Trace client-side PatherTick
            harmony.Patch(
                AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick)),
                postfix: new HarmonyMethod(typeof(PathingDebugPatches), nameof(PathingDebugPatches.Postfix_PatherTickDebug))
            );

            // Intercept path application on client
            MP.RegisterSyncWorker<PawnPathSurrogate>(SyncWorkers.ReadWritePawnPathSurrogate);

            Log.Message("[Multiplayer-PathingDebug] Debug patches applied.");
        }
    }

    public static class PathingDebugPatches
    {
        // 1) Host broadcasts AI job
        public static void Postfix_StartJobDebug(Pawn_JobTracker __instance, Job newJob)
        {
            if (Multiplayer.LocalServer != null && newJob?.jobGiver != null && __instance.pawn.IsColonist)
            {
                MpTrace.Info($"[Debug] Host SEND StartJobAI pawn={__instance.pawn} job={newJob.def.defName}");
            }
        }

        // 2) Client receives and executes the SyncMethod
        [SyncMethod(context = SyncContext.CurrentMap)]
        public static void StartJobAI_Debug(Pawn pawn, JobParams prms)
        {
            if (Multiplayer.LocalServer != null || pawn == null || !pawn.IsColonist) return;
            MpTrace.Info($"[Debug] Client RECV StartJobAI pawn={pawn} job={prms.def.defName}");
            try { pawn.jobs.StartJob(prms.ToJob(), JobCondition.InterruptForced); }
            catch { /* swallow */ }
        }

        // 3) Client PatherTick probe
        public static void Postfix_PatherTickDebug(Pawn_PathFollower __instance)
        {
            if (Multiplayer.LocalServer == null && __instance.pawn.IsColonist && __instance.pawn.Spawned && !__instance.pawn.Drafted)
            {
                MpTrace.Verbose($"[Debug] Client PatherTick pawn={__instance.pawn} dest={__instance.Destination}");
            }
        }

        // 4) Client applies the new path surrogate
        [SyncMethod(context = SyncContext.CurrentMap)]
        public static void SetPawnPath_Debug(Pawn pawn, PawnPathSurrogate surr)
        {
            if (Multiplayer.LocalServer != null || pawn == null || !pawn.IsColonist) return;
            MpTrace.Info($"[Debug] Client RECV SetPawnPath pawn={pawn} valid={surr.isValid}");
            var pf = pawn.pather;
            pf.curPath?.ReleaseToPool();
            pf.curPath = surr.ToPawnPath(pawn);
            if (pf.curPath.Found) pf.ResetToCurrentPosition();
        }
    }
}
