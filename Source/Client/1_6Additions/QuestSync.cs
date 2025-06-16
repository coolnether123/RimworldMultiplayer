// FILE: Multiplayer/Client/Sync/QuestSync.cs

using HarmonyLib;
using LudeonTK; // Needed for DebugActionNode
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

        // ====================================================================================
        // NEW, DIRECT PATCH ON THE DEBUG ACTION ITSELF
        // This is our new entry point, which is more robust.
        // ====================================================================================
        [HarmonyPatch(typeof(DebugActionNode), nameof(DebugActionNode.Enter))]
        static class DebugAction_GenerateQuest_Patch
        {
            static bool Prefix(DebugActionNode __instance)
            {
                // We only care about multiplayer sessions.
                if (Multiplayer.Client == null) return true;

                // Check if the debug action is one of the quest generation ones.
                // The label will be the defName of the QuestScriptDef.
                var questDef = DefDatabase<QuestScriptDef>.GetNamed(__instance.label, false);
                if (questDef == null)
                {
                    // Not a quest generation button we care about, let the original logic run.
                    return true;
                }

                // We have identified a quest generation button. Take over completely.
                Log.Message($"MP: Intercepted debug quest generation for '{questDef.defName}'.");

                try
                {
                    // The host is responsible for initiating the sync.
                    if (Multiplayer.LocalServer != null)
                    {
                        // Create the slate with the 'points' variable, which is what the quest generator expects.
                        var slate = new Slate();
                        slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(Find.CurrentMap));

                        // Capture the current random state.
                        ulong randState = Rand.StateCompressed;

                        // Serialize the slate for transport.
                        byte[] slateData = WriteSlate(slate);

                        // Call the SyncMethod to have everyone generate the quest.
                        GenerateQuestSynced(questDef, slateData, randState);
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"MP: Exception during debug quest interception: {e}");
                }

                // CRITICAL: Return false to prevent the original, broken debug action logic from running.
                // We also need to manually close the debug window.
                Find.WindowStack.TryRemove(typeof(Dialog_Debug));
                return false;
            }
        }

        // This method remains the same. All players (including host) execute it.
        [SyncMethod]
        public static void GenerateQuestSynced(QuestScriptDef root, byte[] slateData, ulong randState)
        {
            Quest generatedQuest = null;
            try
            {
                isExecutingSyncedQuestGeneration = true;

                Rand.PushState();
                Rand.StateCompressed = randState;

                Slate slate = ReadSlate(slateData);
                if (slate == null)
                {
                    Log.Error("MP: Failed to deserialize slate for synced quest generation.");
                    return;
                }

                // We now call the lower-level QuestGen.Generate directly, which is safer.
                generatedQuest = QuestGen.Generate(root, slate);

                if (generatedQuest != null)
                {
                    // Add the generated quest to the manager.
                    Find.QuestManager.Add(generatedQuest);

                    // The host then triggers the letter for everyone.
                    if (Multiplayer.LocalServer != null)
                    {
                        ShowLetterSynced(generatedQuest.id);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"MP: Exception during synced quest generation: {e}");
            }
            finally
            {
                Rand.PopState();
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
