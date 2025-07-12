using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(ListerThings), nameof(ListerThings.Add))]
    static class AllGroupsPatch
    {
        static FieldInfo AllGroups = AccessTools.Field(typeof(ThingListGroupHelper), nameof(ThingListGroupHelper.AllGroups));

        static Dictionary<ThingDef, ThingRequestGroup[]> cache = new();

       static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var method = AccessTools.Method(typeof(AllGroupsPatch), nameof(GroupsForThing));
            foreach (var inst in insts)
            {
                // look for the static field load you want to replace
                if (inst.opcode == OpCodes.Ldsfld && inst.operand == AllGroups)
                {
                    // 1) clone and steal its labels
                    var first = new CodeInstruction(OpCodes.Ldarg_1);
                    first.labels.AddRange(inst.labels);
                    yield return first;

                    // 2) call your helper next
                    yield return new CodeInstruction(OpCodes.Call, method);

                    // 3) skip the original ldsfld entirely
                }
                else
                {
                    yield return inst;
                }
            }
        }

        static ThingRequestGroup[] GroupsForThing(Thing t)
        {
            if (!cache.TryGetValue(t.def, out ThingRequestGroup[] value))
            {
                List<ThingRequestGroup> list = new List<ThingRequestGroup>();
                ThingRequestGroup[] allGroups = ThingListGroupHelper.AllGroups;
                for (int i = 0; i < allGroups.Length; i++)
                {
                    if (allGroups[i].Includes(t.def))
                    {
                        list.Add(allGroups[i]);
                    }
                }
                value = cache[t.def] = list.ToArray();
            }
            return value;
        }
    }
}
