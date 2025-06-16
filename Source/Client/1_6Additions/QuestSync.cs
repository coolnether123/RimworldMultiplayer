// FILE: Multiplayer/Client/Sync/QuestSync.cs

using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common; // This using is critical to find ByteWriter/Reader
using RimWorld;
using RimWorld.Planet;
using System;
using System.Linq;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch]
    public static class QuestSync
    {
        // A temporary flag to ensure our synchronized letter-sending method can run,
        // while the original, unsynchronized call is suppressed.
        private static bool isSyncingLetter = false;

        #region Quest Generation and Sync Logic

        /// <summary>
        /// STEP 1: Suppress quest generation on clients. Only the host (LocalServer != null) will execute the original method.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.GenerateQuestAndMakeAvailable))]
        static bool GenerateQuestAndMakeAvailable_Prefix()
        {
            return Multiplayer.Client == null || Multiplayer.LocalServer != null; // Allows Host and Single-Player to run
        }

        /// <summary>
        /// STEP 2: After the host generates the quest, this postfix captures the result and starts the sync process.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.GenerateQuestAndMakeAvailable))]
        static void GenerateQuestAndMakeAvailable_Postfix(Quest __result)
        {
            if (Multiplayer.LocalServer != null && __result != null)
            {
                var writer = new ByteWriter();
                WriteFullQuest(writer, __result);
                ReceiveGeneratedQuest(writer.ToArray()); // Broadcast the serialized quest data
            }
        }

        /// <summary>
        /// STEP 3: The original letter-sending utility is suppressed. It will only be called through our specific sync method.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.SendLetterQuestAvailable))]
        static bool SendLetterQuestAvailable_Prefix()
        {
            // Only allow this method to run if we are explicitly calling it via SyncShowLetter.
            return isSyncingLetter;
        }

        /// <summary>
        /// STEP 4: This SyncMethod is called by the host. It runs on ALL clients (including the host).
        /// Clients deserialize the quest and add it. The host then triggers the letter sync.
        /// </summary>
        [SyncMethod]
        public static void ReceiveGeneratedQuest(byte[] questData)
        {
            var reader = new ByteReader(questData);
            var quest = ReadFullQuest(reader);

            if (quest == null)
            {
                Log.Error("MP: Received null quest after deserialization.");
                return;
            }

            // Only add the quest if it doesn't already exist.
            // This prevents the host from adding a duplicate.
            if (!Find.QuestManager.QuestsListForReading.Any(q => q.id == quest.id))
            {
                Find.QuestManager.Add(quest);
            }

            // Now that the quest object is guaranteed to exist everywhere,
            // the host tells all clients to display the associated letter.
            if (Multiplayer.LocalServer != null)
            {
                SyncShowLetter(quest.id, quest.name); // Pass basic info for the letter
            }
        }

        /// <summary>
        /// STEP 5: This SyncMethod runs on all clients to create and show the quest letter, ensuring a consistent UI experience.
        /// </summary>
        [SyncMethod]
        public static void SyncShowLetter(int questId, string questName)
        {
            var quest = Find.QuestManager.QuestsListForReading.FirstOrDefault(q => q.id == questId);
            if (quest != null)
            {
                // We have to temporarily re-enable the original method to let it do the work for us.
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

        #region Quest Interaction Sync

        /// <summary>
        /// Intercepts the "Accept" action and redirects it through a SyncMethod.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
        static bool QuestAccept_Prefix(Quest __instance, Pawn by)
        {
            if (Multiplayer.Client != null)
            {
                SyncQuestAccept(__instance, by);
                return false; // Suppress original call
            }
            return true;
        }

        [SyncMethod]
        static void SyncQuestAccept(Quest quest, Pawn by)
        {
            if (quest != null && quest.State == QuestState.NotYetAccepted)
            {
                quest.Accept(by);
            }
        }

        #endregion

        #region Full Quest Serialization Logic

        private static void WriteFullQuest(ByteWriter writer, Quest quest)
        {
            writer.WriteULong(Rand.StateCompressed); // Ensure deterministic RNG
            byte[] data = ScribeUtil.WriteExposable(quest, "quest");
            writer.WritePrefixedBytes(data);
        }

        private static Quest ReadFullQuest(ByteReader reader)
        {
            Rand.StateCompressed = reader.ReadULong(); // Restore host's RNG state
            byte[] data = reader.ReadPrefixedBytes();

            var quest = ScribeUtil.ReadExposable<Quest>(data);

            // Re-link parent quest reference in all parts after deserialization to be safe.
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
