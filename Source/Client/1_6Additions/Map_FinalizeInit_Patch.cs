// In new file: Source/Client/Map_FinalizeInit_Patch.cs

using HarmonyLib;
using Verse;
using System.Collections.Generic;
using Multiplayer.Common;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Map), nameof(Map.FinalizeInit))]
    class Map_FinalizeInit_Patch
    {
        static void Postfix(Map __instance)
        {
            // If not in a multiplayer game, do nothing.
            if (Multiplayer.Client == null) return;

            int mapId = __instance.uniqueID;
            var session = Multiplayer.session;

            // Check if there are any buffered commands waiting for this map.
            if (session.bufferedCommands.TryGetValue(mapId, out List<ScheduledCommand> commandsToQueue))
            {
                MpTrace.Info($"Map {mapId} has finished loading. Processing {commandsToQueue.Count} buffered commands.");

                var mapQueue = __instance.AsyncTime().cmds;
                foreach (var cmd in commandsToQueue)
                {
                    mapQueue.Enqueue(cmd);
                }

                // Clear the buffer for this mapId.
                session.bufferedCommands.Remove(mapId);
            }
        }
    }
}
