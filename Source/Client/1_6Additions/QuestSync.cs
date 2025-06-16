// FILE: Multiplayer/Client/Sync/QuestSync.cs

using HarmonyLib;
using LudeonTK;
using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using System.IO; // Required for MemoryStream
using System.Xml; // Required for XmlDocument

namespace Multiplayer.Client
{
    [HarmonyPatch]
    public static class QuestSync
    {
        // We no longer need the isExecutingSyncedQuestGeneration flag with this approach.

        [HarmonyPatch(typeof(DebugActionNode), nameof(DebugActionNode.Enter))]
        static class DebugAction_GenerateQuest_Patch
        {
            static bool Prefix(DebugActionNode __instance)
            {
                if (Multiplayer.Client == null) return true;
                var questDef = DefDatabase<QuestScriptDef>.GetNamed(__instance.label, false);
                if (questDef == null) return true;

                // Host generates the quest LOCALLY first.
                if (Multiplayer.LocalServer != null)
                {
                    try
                    {
                        var slate = new Slate();
                        slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(Find.CurrentMap));

                        // Generate the quest object on the host.
                        Quest quest = QuestGen.Generate(questDef, slate);

                        if (quest != null)
                        {
                            // Now serialize the *result* into a byte array.
                            byte[] questData = ScribeUtil.WriteExposable(quest, "quest", true);

                            // Add a debug log to show the host's quest data before sending
                            Log.Message($"MP (Host): Generated and serialized quest '{quest.name}' (ID: {quest.id}). Data length: {questData.Length}. Broadcasting...");

                            // Broadcast this data to all players (including the host).
                            ReceiveQuestDataSynced(questData);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error($"MP: Exception during host-side quest generation: {e}");
                    }
                }

                Find.WindowStack.TryRemove(typeof(Dialog_Debug));
                return false;
            }
        }

        // This is the SyncMethod that delivers the final quest data to everyone.
        [SyncMethod]
        public static void ReceiveQuestDataSynced(byte[] questData)
        {
            string clientName = Multiplayer.Client == null ? "SP" : (Multiplayer.LocalServer != null ? "Host" : "Client");
            Quest quest = null;

            try
            {
                // =========================================================================
                // CRITICAL CHANGE: Everyone deserializes from the same data blob.
                // No generation logic is run on clients.
                // ScribeUtil handles loading in an isolated context.
                // =========================================================================
                quest = ScribeUtil.ReadExposable<Quest>(questData);

                if (quest != null)
                {
                    // Relink parts to their parent quest after loading.
                    foreach (var part in quest.PartsListForReading)
                    {
                        part.quest = quest;
                    }

                    // Add a debug log to dump the received quest for comparison.
                    // This custom logger will work without a Scribe context.
                    LogQuestDetails(quest, clientName);

                    // Add the quest to the manager. It's safe to call on the host,
                    // as it will already have the quest from its local generation.
                    // The Add method handles duplicates.
                    Find.QuestManager.Add(quest);

                    // Host triggers the letter for everyone.
                    if (Multiplayer.LocalServer != null)
                    {
                        ShowLetterSynced(quest.id);
                    }
                }
                else
                {
                    Log.Error($"MP ({clientName}): Failed to deserialize quest from received data.");
                }
            }
            catch (Exception e)
            {
                Log.Error($"MP ({clientName}): Exception during quest deserialization: {e}");
            }
        }

        [SyncMethod]
        public static void ShowLetterSynced(int questId)
        {
            Quest quest = Find.QuestManager.QuestsListForReading.FirstOrDefault(q => q.id == questId);
            if (quest != null)
            {
                QuestUtility.SendLetterQuestAvailable(quest);
            }
        }

        // Custom debug logger to print quest details without using Scribe.
        public static void LogQuestDetails(Quest quest, string clientName)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"--- QUEST DUMP ({clientName}) ---");
            sb.AppendLine($"ID: {quest.id}, Name: '{quest.name}'");
            sb.AppendLine($"Root Def: {quest.root.defName}");
            sb.AppendLine($"Description: {quest.description.Resolve()}");
            sb.AppendLine($"Challenge Rating: {quest.challengeRating}");
            sb.AppendLine("Parts:");
            foreach (var part in quest.PartsListForReading)
            {
                sb.AppendLine($"  - {part.GetType().Name} (Index: {part.Index})");
                // Log a key detail for some common parts to help identify differences.
                if (part is QuestPart_WorldObjectTimeout timeout)
                {
                    sb.AppendLine($"    Timeout Ticks: {timeout.delayTicks}");
                    sb.AppendLine($"    WorldObject: {timeout.worldObject?.GetUniqueLoadID() ?? "NULL"}");
                }
                if (part is QuestPart_Choice choice)
                {
                    var reward = choice.choices.FirstOrDefault()?.rewards.FirstOrDefault();
                    if (reward is Reward_Items itemReward)
                    {
                        sb.AppendLine($"    Reward Items: {string.Join(", ", itemReward.items.Select(i => i.LabelCap))}");
                    }
                }
            }
            sb.AppendLine("--------------------------");
            Log.Message(sb.ToString());
        }

        #region Quest Interaction Sync

        // This part remains the same as it correctly syncs player actions.
        [HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
        static class QuestAccept_Patch
        {
            static bool Prefix(Quest __instance, Pawn by)
            {
                if (Multiplayer.Client != null)
                {
                    SyncQuestAccept(__instance.id, by);
                    return false;
                }
                return true;
            }
        }

        [SyncMethod]
        static void SyncQuestAccept(int questId, Pawn by)
        {
            Quest quest = Find.QuestManager.QuestsListForReading.FirstOrDefault(q => q.id == questId);
            if (quest != null && quest.State == QuestState.NotYetAccepted)
            {
                quest.Accept(by);
            }
        }
        #endregion
    }
}
