// In new file: Source/Client/Saving/MapFinalizeInit_Patch.cs

using HarmonyLib;
using Verse;
using System.Collections.Generic;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Map), nameof(Map.FinalizeInit))]
    class Map_FinalizeInit_Patch
    {
        static void Postfix(Map __instance)
        {
            if (Multiplayer.Client == null) return;

            int mapId = __instance.uniqueID;
            var session = Multiplayer.session;

            if (session.bufferedCommands.TryGetValue(mapId, out var commandsToQueue))
            {
                MpTrace.Info($"Map {mapId} finalized. Processing {commandsToQueue.Count} buffered commands.");
                var mapQueue = __instance.AsyncTime().cmds;
                foreach (var cmd in commandsToQueue)
                {
                    mapQueue.Enqueue(cmd);
                }
                session.bufferedCommands.Remove(mapId);
            }
        }
    }
}
