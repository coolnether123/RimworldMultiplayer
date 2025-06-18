using HarmonyLib;
using Verse;
using System.Collections.Generic;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Map), nameof(Map.FinalizeInit))]
    class MapFinalizeInit_Patch
    {
        static void Postfix(Map __instance)
        {
            if (Multiplayer.Client == null) return;

            int mapId = __instance.uniqueID;
            var session = Multiplayer.session;

            if (session.bufferedCommands.TryGetValue(mapId, out var commandsToQueue))
            {
                MpTrace.Info($"Map {mapId} finalized. Processing {commandsToQueue.Count} buffered commands.");

                var asyncTime = __instance.AsyncTime();
                if (asyncTime == null)
                {
                    MpTrace.Error($"Map {mapId} finalized but has no AsyncTimeComp. Cannot process buffered commands.");
                    return;
                }

                var mapQueue = asyncTime.cmds;
                foreach (var cmd in commandsToQueue)
                {
                    mapQueue.Enqueue(cmd);
                }

                session.bufferedCommands.Remove(mapId);
            }
        }
    }
}
