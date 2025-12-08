using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    public static class FactionContext
    {
        public static Stack<Faction> stack = new();

        public static Faction Push(Faction newFaction, bool force = false)
        {
            if (newFaction == null || !force && Find.FactionManager.ofPlayer == newFaction || !newFaction.def.isPlayer)
            {
                stack.Push(null);
                return null;
            }

            stack.Push(Find.FactionManager.OfPlayer);
            Set(newFaction);

            return newFaction;
        }

        public static Faction Pop()
        {
            Faction f = stack.Pop();
            if (f != null)
                Set(f);
            return f;
        }

        public static void Set(Faction newFaction)
        {
            Find.FactionManager.ofPlayer = newFaction;
        }

        public static void Clear()
        {
            stack.Clear();
        }
        /// <summary>
        /// This class provides a thread-safe way to store faction information for a specific tile
        /// during asynchronous map generation. When a caravan settles, we store its faction here
        /// before the background worker starts. The map generator then retrieves it.
        /// </summary>
        public static class TileFactionContext
        {
            private static readonly Dictionary<int, Faction> tileFactions = new Dictionary<int, Faction>();
            private static readonly object lockObject = new object();

            public static void SetFactionForTile(int tileId, Faction faction)
            {
                lock (lockObject)
                {
                    Log.Message($"MP: Storing faction context '{faction?.Name ?? "null"}' for tile {tileId}");
                    tileFactions[tileId] = faction;
                }
            }

            public static Faction GetFactionForTile(int tileId)
            {
                lock (lockObject)
                {
                    return tileFactions.TryGetValue(tileId, out Faction faction) ? faction : null;
                }
            }

            public static void ClearTile(int tileId)
            {
                lock (lockObject)
                {
                    if (tileFactions.Remove(tileId))
                    {
                        Log.Message($"MP: Cleared faction context for tile {tileId}");
                    }
                }
            }
        }
    }

}

