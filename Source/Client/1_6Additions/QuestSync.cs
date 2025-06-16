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

namespace Multiplayer.Client
{
    [HarmonyPatch]
    public static class QuestSync
    {
        private static bool isExecutingSyncedQuestGeneration;
        private static int? nextQuestIdOverride;

        [HarmonyPatch(typeof(DebugActionNode), nameof(DebugActionNode.Enter))]
        static class DebugAction_GenerateQuest_Patch
        {
            static bool Prefix(DebugActionNode __instance)
            {
                if (Multiplayer.Client == null) return true;
                var questDef = DefDatabase<QuestScriptDef>.GetNamed(__instance.label, false);
                if (questDef == null) return true;

                if (Multiplayer.LocalServer != null)
                {
                    // =========================================================================
                    // CRITICAL FIX: Create a NEW, CLEAN slate. Do not use any pre-existing
                    // slate from the debug action context. This prevents unsynced variables
                    // from leaking into the generation process.
                    // The 'points' variable is the only one required for most quest scripts.
                    // =========================================================================
                    var cleanSlate = new Slate();
                    cleanSlate.Set("points", StorytellerUtility.DefaultThreatPointsNow(Find.CurrentMap));

                    ulong randState = Rand.StateCompressed;
                    int questId = Find.UniqueIDsManager.GetNextQuestID();

                    byte[] slateData = WriteSlate(cleanSlate);

                    GenerateQuestSynced(questDef, slateData, randState, questId);
                }

                Find.WindowStack.TryRemove(typeof(Dialog_Debug));
                return false;
            }
        }

        [HarmonyPatch(typeof(UniqueIDsManager), nameof(UniqueIDsManager.GetNextQuestID))]
        static class GetNextQuestID_Patch
        {
            static bool Prefix(ref int __result)
            {
                if (isExecutingSyncedQuestGeneration && nextQuestIdOverride.HasValue)
                {
                    __result = nextQuestIdOverride.Value;
                    return false;
                }
                return true;
            }
        }

        [SyncMethod]
        public static void GenerateQuestSynced(QuestScriptDef root, byte[] slateData, ulong randState, int questId)
        {
            Quest generatedQuest = null;
            string clientName = Multiplayer.Client == null ? "SP" : (Multiplayer.LocalServer != null ? "Host" : "Client");

            try
            {
                isExecutingSyncedQuestGeneration = true;
                nextQuestIdOverride = questId;

                Rand.PushState();
                Rand.StateCompressed = randState;

                Slate slate = ReadSlate(slateData);
                if (slate == null)
                {
                    Log.Error("MP: Failed to deserialize slate for synced quest generation.");
                    return;
                }

                generatedQuest = QuestGen.Generate(root, slate);

                if (generatedQuest != null)
                {
                    // Add a debug log to dump the generated quest XML for comparison
                    string questXml = Scribe.saver.DebugOutputFor(generatedQuest);
                    Log.Message($"MP ({clientName}): Generated quest '{generatedQuest.name}' with ID {generatedQuest.id}.\nXML Dump:\n{questXml}");

                    Find.QuestManager.Add(generatedQuest);

                    if (Multiplayer.LocalServer != null)
                    {
                        ShowLetterSynced(generatedQuest.id);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"MP: Exception during synced quest generation on {clientName}: {e}");
            }
            finally
            {
                Rand.PopState();
                nextQuestIdOverride = null;
                isExecutingSyncedQuestGeneration = false;
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

        #region Slate Serialization

        private static byte[] WriteSlate(Slate slate)
        {
            var varsField = typeof(Slate).GetField("vars", BindingFlags.NonPublic | BindingFlags.Instance);
            var vars = (IDictionary<string, object>)varsField.GetValue(slate);

            List<string> keys = vars.Keys.ToList();
            List<object> values = keys.Select(k => vars[k]).ToList();

            return ScribeUtil.WriteExposable(new SlateVars(keys, values), "slateVars");
        }

        private static Slate ReadSlate(byte[] data)
        {
            var slateVars = ScribeUtil.ReadExposable<SlateVars>(data);
            var slate = new Slate();
            if (slateVars?.keys != null && slateVars.values != null)
            {
                for (int i = 0; i < slateVars.keys.Count; i++)
                {
                    slate.Set(slateVars.keys[i], slateVars.values[i]);
                }
            }
            return slate;
        }

        private class SlateVars : IExposable
        {
            public List<string> keys;
            public List<object> values;

            public SlateVars() { }
            public SlateVars(List<string> keys, List<object> values)
            {
                this.keys = keys;
                this.values = values;
            }

            public void ExposeData()
            {
                Scribe_Collections.Look(ref keys, "keys", LookMode.Value);
                Scribe_Collections.Look(ref values, "values", LookMode.Deep);
            }
        }
        #endregion

        #region Quest Interaction Sync

        [HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
        static class QuestAccept_Patch
        {
            static bool Prefix(Quest __instance, Pawn by)
            {
                if (Multiplayer.Client != null && !isExecutingSyncedQuestGeneration)
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
