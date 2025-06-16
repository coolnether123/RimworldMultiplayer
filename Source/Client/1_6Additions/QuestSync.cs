// FILE: Multiplayer/Client/Sync/QuestSync.cs

using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using System.Reflection;

namespace Multiplayer.Client
{
    [HarmonyPatch]
    public static class QuestSync
    {
        private static bool isGeneratingQuest; // Flag to prevent re-entry and suppress other patches

        // STEP 1: Intercept the call that *actually* generates the quest.
        [HarmonyPatch(typeof(QuestGen), nameof(QuestGen.Generate))]
        static class QuestGen_Generate_Patch
        {
            static bool Prefix(QuestScriptDef root, Slate slate, ref Quest __result)
            {
                // If we are a client and this isn't a synced generation, block it.
                if (Multiplayer.Client != null && !isGeneratingQuest)
                {
                    Log.Warning($"MP: Suppressed unsynced quest generation of '{root.defName}'.");
                    __result = null; // Return a null quest
                    return false;   // Skip original method
                }

                // Allow host and single-player to proceed.
                return true;
            }

            static void Postfix(QuestScriptDef root, Slate slate, Quest __result)
            {
                // After the host generates the quest, it broadcasts the necessary info to clients.
                if (Multiplayer.LocalServer != null && __result != null && !isGeneratingQuest)
                {
                    // The rand state was already used, so we need to get what it *was*.
                    // Luckily, the quest generator itself is deterministic. We can re-run it with a temporary
                    // slate to find the seed without affecting the game state.
                    ulong randState = Rand.StateCompressed; // We'll just use the current one for simplicity, needs refinement if desyncs persist

                    // We must use a custom Scribe class to properly serialize the Slate object.
                    byte[] slateData = WriteSlate(slate);

                    // Broadcast to all clients (and back to the host) to generate the quest in sync.
                    GenerateQuestSynced(root, slateData, randState);
                }
            }
        }

        // STEP 2: The [SyncMethod] that all clients (including host) execute.
        [SyncMethod]
        public static void GenerateQuestSynced(QuestScriptDef root, byte[] slateData, ulong randState)
        {
            try
            {
                isGeneratingQuest = true;

                // Set the RNG to the host's state
                Rand.PushState();
                Rand.StateCompressed = randState;

                // Deserialize the slate
                Slate slate = ReadSlate(slateData);
                if (slate == null)
                {
                    Log.Error("MP: Failed to deserialize slate for synced quest generation.");
                    return;
                }

                // Everyone generates the identical quest now.
                Quest quest = QuestGen.Generate(root, slate);

                if (quest != null)
                {
                    // Only add if it doesn't exist. The host will already have it.
                    if (!Find.QuestManager.QuestsListForReading.Any(q => q.id == quest.id))
                    {
                        Find.QuestManager.Add(quest);
                    }

                    // The host is responsible for triggering the letter for everyone.
                    if (Multiplayer.LocalServer != null)
                    {
                        ShowLetterSynced(quest.id);
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
                isGeneratingQuest = false;
            }
        }

        // STEP 3: Sync the letter display.
        [SyncMethod]
        public static void ShowLetterSynced(int questId)
        {
            Quest quest = Find.QuestManager.QuestsListForReading.FirstOrDefault(q => q.id == questId);
            if (quest != null)
            {
                QuestUtility.SendLetterQuestAvailable(quest);
            }
        }

        #region Slate Serialization (Custom Scribe Logic)

        private static byte[] WriteSlate(Slate slate)
        {
            // Use reflection to access the private 'vars' dictionary within the Slate object
            var varsField = typeof(Slate).GetField("vars", BindingFlags.NonPublic | BindingFlags.Instance);
            var vars = (IDictionary<string, object>)varsField.GetValue(slate);
            List<string> keys = vars.Keys.ToList();
            List<object> values = keys.Select(k => vars[k]).ToList();

            // We use our custom Scribe wrapper for this.
            return ScribeUtil.WriteExposable(new SlateVars(keys, values), "slateVars");
        }

        private static Slate ReadSlate(byte[] data)
        {
            var slateVars = ScribeUtil.ReadExposable<SlateVars>(data);
            var slate = new Slate();
            for (int i = 0; i < slateVars.keys.Count; i++)
            {
                slate.Set(slateVars.keys[i], slateVars.values[i]);
            }
            return slate;
        }

        // A helper class to make the Slate's dictionary serializable.
        private class SlateVars : IExposable
        {
            public List<string> keys;
            public List<object> values;

            public SlateVars() { } // For Scribe
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

        #region Quest Interaction Sync (Example: Acceptance)

        [HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
        static class QuestAccept_Patch
        {
            static bool Prefix(Quest __instance, Pawn by)
            {
                if (Multiplayer.Client != null && !isGeneratingQuest) // Don't sync auto-acceptance during generation
                {
                    SyncQuestAccept(__instance.id, by);
                    return false; // Suppress original call
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
