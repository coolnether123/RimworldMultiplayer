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
        private const string LOG_PREFIX = "[MP QUEST SYNC]";
        private static bool isSyncingLetter = false;

        #region Quest Generation and Sync Logic

        [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.GenerateQuestAndMakeAvailable), new Type[] { typeof(QuestScriptDef), typeof(Slate) })]
        static class GenerateQuestAndMakeAvailable_Patch
        {
            static bool Prefix()
            {
                Log.Message($"{LOG_PREFIX} GenerateQuestAndMakeAvailable_Patch Prefix running.");
                bool canRun = Multiplayer.Client == null || Multiplayer.LocalServer != null;
                Log.Message($"{LOG_PREFIX} Is Host/SP? {canRun}. Letting original method run: {canRun}");
                return canRun;
            }

            static void Postfix(Quest __result, Slate vars)
            {
                Log.Message($"{LOG_PREFIX} GenerateQuestAndMakeAvailable_Patch Postfix running.");

                if (Multiplayer.LocalServer != null)
                {
                    Log.Message($"{LOG_PREFIX} Postfix detected Host.");
                    if (__result == null)
                    {
                        Log.Error($"{LOG_PREFIX} Postfix received a NULL quest (__result). Quest generation failed silently. Aborting sync.");
                        return;
                    }

                    Log.Message($"{LOG_PREFIX} Quest generated successfully on host. ID: {__result.id}, Name: {__result.name}");

                    try
                    {
                        ulong randState = vars.Get<ulong>("randState");
                        Log.Message($"{LOG_PREFIX} Retrieved randState from slate: {randState}");

                        var writer = new ByteWriter();
                        writer.WriteULong(randState);

                        Log.Message($"{LOG_PREFIX} Serializing quest...");
                        byte[] questData = ScribeUtil.WriteExposable(__result, "quest");
                        writer.WritePrefixedBytes(questData);
                        Log.Message($"{LOG_PREFIX} Serialization complete. Data length: {questData.Length}. Total payload length: {writer.Position}");

                        Log.Message($"{LOG_PREFIX} Calling SyncMethod: ReceiveGeneratedQuest...");
                        ReceiveGeneratedQuest(__result.id, writer.ToArray());
                        Log.Message($"{LOG_PREFIX} Postfix finished.");
                    }
                    catch (Exception e)
                    {
                        Log.Error($"{LOG_PREFIX} EXCEPTION in Postfix: {e}");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(QuestGen), nameof(QuestGen.Generate))]
        static class QuestGen_Generate_Patch
        {
            static void Prefix(Slate initialVars)
            {
                if (Multiplayer.LocalServer != null)
                {
                    ulong state = Rand.StateCompressed;
                    initialVars.Set("randState", state, true);
                    Log.Message($"{LOG_PREFIX} QuestGen_Generate_Patch Prefix: Captured and set randState in slate: {state}");
                }
            }
        }

        [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.SendLetterQuestAvailable))]
        static class SendLetterQuestAvailable_Patch
        {
            static bool Prefix()
            {
                Log.Message($"{LOG_PREFIX} SendLetterQuestAvailable_Patch Prefix. isSyncingLetter = {isSyncingLetter}");
                return isSyncingLetter;
            }
        }

        [SyncMethod]
        public static void ReceiveGeneratedQuest(int questId, byte[] data)
        {
            Log.Message($"{LOG_PREFIX} ReceiveGeneratedQuest SyncMethod RUNNING. QuestID: {questId}");

            if (Multiplayer.LocalServer != null)
            {
                Log.Message($"{LOG_PREFIX} ReceiveGeneratedQuest: Host logic branch.");
                var hostQuest = Find.QuestManager.QuestsListForReading.FirstOrDefault(q => q.id == questId);
                if (hostQuest != null)
                {
                    Log.Message($"{LOG_PREFIX} ReceiveGeneratedQuest: Host found quest {hostQuest.id}. Calling SyncShowLetter.");
                    SyncShowLetter(hostQuest.id, hostQuest.name);
                }
                else
                {
                    Log.Error($"{LOG_PREFIX} ReceiveGeneratedQuest: Host could NOT find quest with ID {questId} in its QuestManager.");
                }
                return;
            }

            // === CLIENT-ONLY LOGIC ===
            Log.Message($"{LOG_PREFIX} ReceiveGeneratedQuest: Client logic branch.");
            try
            {
                var reader = new ByteReader(data);

                Log.Message($"{LOG_PREFIX} Client deserializing quest...");
                var newQuest = ReadFullQuest(reader);
                Log.Message($"{LOG_PREFIX} Client deserialization complete.");

                if (newQuest == null)
                {
                    Log.Error($"{LOG_PREFIX} Client received null quest after deserialization.");
                    return;
                }

                if (!Find.QuestManager.QuestsListForReading.Any(q => q.id == newQuest.id))
                {
                    Find.QuestManager.Add(newQuest);
                    Log.Message($"{LOG_PREFIX} Client ADDED quest {newQuest.id} ({newQuest.name}) to QuestManager.");
                }
                else
                {
                    Log.Warning($"{LOG_PREFIX} Client already has quest with ID {newQuest.id}. Not adding again.");
                }
            }
            catch (Exception e)
            {
                Log.Error($"{LOG_PREFIX} EXCEPTION in client-side ReceiveGeneratedQuest: {e}");
            }
        }

        [SyncMethod]
        public static void SyncShowLetter(int questId, string questName)
        {
            Log.Message($"{LOG_PREFIX} SyncShowLetter RUNNING for QuestID: {questId} ({questName})");
            var quest = Find.QuestManager.QuestsListForReading.FirstOrDefault(q => q.id == questId);
            if (quest != null)
            {
                try
                {
                    isSyncingLetter = true;
                    Log.Message($"{LOG_PREFIX} Found quest, calling QuestUtility.SendLetterQuestAvailable...");
                    QuestUtility.SendLetterQuestAvailable(quest);
                    Log.Message($"{LOG_PREFIX} SendLetterQuestAvailable finished.");
                }
                catch (Exception e)
                {
                    Log.Error($"{LOG_PREFIX} EXCEPTION in SyncShowLetter: {e}");
                }
                finally
                {
                    isSyncingLetter = false;
                }
            }
            else
            {
                Log.Warning($"{LOG_PREFIX} SyncShowLetter could not find quest with ID {questId}.");
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
                    Log.Message($"{LOG_PREFIX} Intercepted Quest.Accept for quest {__instance.id}. Calling SyncMethod.");
                    SyncQuestAccept(__instance, by);
                    return false;
                }
                return true;
            }
        }

        [SyncMethod]
        static void SyncQuestAccept(Quest quest, Pawn by)
        {
            Log.Message($"{LOG_PREFIX} SyncQuestAccept RUNNING for quest {quest?.id}.");
            if (quest != null && quest.State == QuestState.NotYetAccepted)
            {
                quest.Accept(by);
                Log.Message($"{LOG_PREFIX} Quest {quest.id} accepted.");
            }
            else
            {
                Log.Warning($"{LOG_PREFIX} SyncQuestAccept: Quest was null or not in correct state to be accepted.");
            }
        }

        #endregion

        #region Full Quest Serialization Logic

        private static Quest ReadFullQuest(ByteReader reader)
        {
            ulong randState = reader.ReadULong();
            Log.Message($"{LOG_PREFIX} ReadFullQuest: Setting Rand.State to {randState}");
            Rand.StateCompressed = randState;

            byte[] data = reader.ReadPrefixedBytes();
            Log.Message($"{LOG_PREFIX} ReadFullQuest: Deserializing from {data.Length} bytes.");
            var quest = ScribeUtil.ReadExposable<Quest>(data);

            if (quest != null)
            {
                Log.Message($"{LOG_PREFIX} ReadFullQuest: Successfully deserialized quest {quest.id}. Re-linking parts...");
                foreach (var part in quest.PartsListForReading)
                {
                    part.quest = quest;
                }
            }
            else
            {
                Log.Error($"{LOG_PREFIX} ReadFullQuest: ScribeUtil.ReadExposable returned NULL.");
            }

            return quest;
        }

        #endregion
    }
}
