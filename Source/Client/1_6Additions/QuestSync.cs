// FILE: Multiplayer/Client/Sync/QuestSync.cs

using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen; // Added for Slate
using System;
using System.Linq;
using Verse;

namespace Multiplayer.Client
{
    public static class QuestSync
    {
        private static bool isSyncingLetter = false;

        #region Quest Generation and Sync Logic

        // Grouped Prefix and Postfix into a single class for clarity and to avoid redundant patching attributes.
        [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.GenerateQuestAndMakeAvailable), new Type[] { typeof(QuestScriptDef), typeof(Slate) })]
        static class GenerateQuestAndMakeAvailable_Patch
        {
            /// <summary>
            /// STEP 1: Suppress quest generation on clients.
            /// </summary>
            static bool Prefix()
            {
                return Multiplayer.Client == null || Multiplayer.LocalServer != null; // Allows Host and Single-Player
            }

            /// <summary>
            /// STEP 2: After the host generates the quest, broadcast it.
            /// </summary>
            static void Postfix(Quest __result)
            {
                if (Multiplayer.LocalServer != null && __result != null)
                {
                    var writer = new ByteWriter();
                    WriteFullQuest(writer, __result);
                    ReceiveGeneratedQuest(writer.ToArray());
                }
            }
        }

        [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.SendLetterQuestAvailable))]
        static class SendLetterQuestAvailable_Patch
        {
            /// <summary>
            /// STEP 3: Suppress the original letter-sending utility unless we explicitly allow it.
            /// </summary>
            static bool Prefix()
            {
                return isSyncingLetter;
            }
        }

        /// <summary>
        /// STEP 4: This SyncMethod runs on all clients, adding the quest object locally. The host then triggers the letter display.
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

            if (!Find.QuestManager.QuestsListForReading.Any(q => q.id == quest.id))
            {
                Find.QuestManager.Add(quest);
            }

            if (Multiplayer.LocalServer != null)
            {
                SyncShowLetter(quest.id, quest.name);
            }
        }

        /// <summary>
        /// STEP 5: This SyncMethod shows the quest letter on all clients.
        /// </summary>
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

        #region Quest Interaction Sync

        [HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
        static class QuestAccept_Patch
        {
            static bool Prefix(Quest __instance, Pawn by)
            {
                if (Multiplayer.Client != null)
                {
                    SyncQuestAccept(__instance, by);
                    return false; // Suppress original call
                }
                return true;
            }
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
            writer.WriteULong(Rand.StateCompressed);
            byte[] data = ScribeUtil.WriteExposable(quest, "quest");
            writer.WritePrefixedBytes(data);
        }

        private static Quest ReadFullQuest(ByteReader reader)
        {
            Rand.StateCompressed = reader.ReadULong();
            byte[] data = reader.ReadPrefixedBytes();

            var quest = ScribeUtil.ReadExposable<Quest>(data);

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
