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
                    var slate = new Slate();
                    slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(Find.CurrentMap));

                    // =========================================================================
                    // CRITICAL FIX: The authoritative random state is now captured here,
                    // right at the moment of user input, before anything else can run.
                    // This is the seed that will be broadcast and used by everyone.
                    // =========================================================================
                    ulong randState = Rand.StateCompressed;

                    byte[] slateData = WriteSlate(slate);

                    GenerateQuestSynced(questDef, slateData, randState);
                }

                Find.WindowStack.TryRemove(typeof(Dialog_Debug));
                return false;
            }
        }

        [SyncMethod]
        public static void GenerateQuestSynced(QuestScriptDef root, byte[] slateData, ulong randState)
        {
            Quest generatedQuest = null;
            try
            {
                isExecutingSyncedQuestGeneration = true;

                // =========================================================================
                // CRITICAL FIX: The entire generation block is wrapped in a Pushed/Popped
                // random state. This ensures that the quest generation is perfectly
                // deterministic and isolated from the rest of the game's RNG.
                // =========================================================================
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
                    Find.QuestManager.Add(generatedQuest);

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
                // CRITICAL: Always pop the state, even if an error occurred.
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
