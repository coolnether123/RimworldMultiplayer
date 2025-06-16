// FILE: Multiplayer/Client/Sync/QuestSync.cs

using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System;
using System.Linq;
using Verse;

namespace Multiplayer.Client
{
    public static class QuestSync
    {
        private static bool isSyncingLetter = false;

        #region Quest Generation and Sync Logic

        [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.GenerateQuestAndMakeAvailable), new Type[] { typeof(QuestScriptDef), typeof(Slate) })]
        static class GenerateQuestAndMakeAvailable_Patch
        {
            // STEP 1: Suppress quest generation on clients. Correctly implemented.
            static bool Prefix()
            {
                return Multiplayer.Client == null || Multiplayer.LocalServer != null; // Host and SP only
            }

            // STEP 2: After host generates, serialize and broadcast.
            static void Postfix(Quest __result, Slate vars)
            {
                if (Multiplayer.LocalServer != null && __result != null)
                {
                    // Capture the random state used for generation
                    // This should be done more robustly by pushing a seed before generation,
                    // but for now, we'll send the state after.
                    ulong randState = vars.Get<ulong>("randState"); // Assuming we inject this

                    var writer = new ByteWriter();
                    writer.WriteULong(randState);
                    byte[] questData = ScribeUtil.WriteExposable(__result, "quest");
                    writer.WritePrefixedBytes(questData);

                    // Call the SyncMethod to handle distribution
                    ReceiveGeneratedQuest(__result.id, writer.ToArray());
                }
            }
        }

        // This is a new patch needed to capture the RNG state *before* generation.
        [HarmonyPatch(typeof(QuestGen), nameof(QuestGen.Generate))]
        static class QuestGen_Generate_Patch
        {
            static void Prefix(Slate slate)
            {
                // Capture the RNG state and store it in the slate so the Postfix can access it.
                if (Multiplayer.LocalServer != null)
                {
                    slate.Set("randState", Rand.StateCompressed, true);
                }
            }
        }

        // STEP 3: Suppress the original letter utility unless we explicitly allow it. Correctly implemented.
        [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.SendLetterQuestAvailable))]
        static class SendLetterQuestAvailable_Patch
        {
            static bool Prefix()
            {
                // isSyncingLetter will only be true inside of our synced letter call
                return isSyncingLetter;
            }
        }

        // STEP 4: This method is now much cleaner. Host broadcasts, clients process.
        [SyncMethod]
        public static void ReceiveGeneratedQuest(int questId, byte[] data)
        {
            // The host calls this method to broadcast, but its job is done.
            // It will proceed to call SyncShowLetter from its own context.
            if (Multiplayer.LocalServer != null)
            {
                // The host already has the quest, so now it just tells everyone (including itself) to show the letter.
                var hostQuest = Find.QuestManager.QuestsListForReading.FirstOrDefault(q => q.id == questId);
                if (hostQuest != null)
                {
                    SyncShowLetter(hostQuest.id, hostQuest.name);
                }
                return;
            }

            // === CLIENT-ONLY LOGIC ===
            var reader = new ByteReader(data);

            // FIX: Renamed variable to avoid conflict with the one in the host's scope.
            var newQuest = ReadFullQuest(reader);

            if (newQuest == null)
            {
                Log.Error("MP: Client received null quest after deserialization.");
                return;
            }

            // Ensure client doesn't already have this quest from a late packet
            if (!Find.QuestManager.QuestsListForReading.Any(q => q.id == newQuest.id))
            {
                Find.QuestManager.Add(newQuest);
                Log.Message($"MP Client: Added quest {newQuest.id} ({newQuest.name})");
            }
        }

        // STEP 5: This shows the quest letter on all clients (and the host).
        [SyncMethod]
        public static void SyncShowLetter(int questId, string questName)
        {
            var quest = Find.QuestManager.QuestsListForReading.FirstOrDefault(q => q.id == questId);
            if (quest != null)
            {
                try
                {
                    isSyncingLetter = true;
                    QuestUtility.SendLetterQuestAvailable(quest);
                }
                finally
                {
                    isSyncingLetter = false;
                }
            }
            else
            {
                Log.Warning($"MP: SyncShowLetter called for quest ID {questId} ({questName}), but the quest was not found.");
            }
        }

        #endregion

        #region Quest Interaction Sync (Accept/Decline etc.)

        // This patch intercepts the local "Accept" action.
        [HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
        static class QuestAccept_Patch
        {
            static bool Prefix(Quest __instance, Pawn by)
            {
                if (Multiplayer.Client != null)
                {
                    // Instead of running locally, send a command to the host to accept it everywhere.
                    SyncQuestAccept(__instance, by);
                    return false; // Suppress original call
                }
                return true;
            }
        }

        // This SyncMethod ensures the "Accept" action is performed deterministically on all clients.
        [SyncMethod]
        static void SyncQuestAccept(Quest quest, Pawn by)
        {
            if (quest != null && quest.State == QuestState.NotYetAccepted)
            {
                // The original method is now called here, inside the sync lockstep.
                quest.Accept(by);
            }
        }

        #endregion

        #region Full Quest Serialization Logic

        // We only need the Read logic now, as write is handled inline.
        private static Quest ReadFullQuest(ByteReader reader)
        {
            // Set the RNG state to match the host's before deserializing.
            Rand.StateCompressed = reader.ReadULong();

            byte[] data = reader.ReadPrefixedBytes();
            var quest = ScribeUtil.ReadExposable<Quest>(data);

            // Re-link parts to their parent quest after loading.
            if (quest != null)
            {
                foreach (var part in quest.PartsListForReading)
                {
                    part.quest = quest;
                }
            }

            return quest;
        }

        #endregion
    }
}
