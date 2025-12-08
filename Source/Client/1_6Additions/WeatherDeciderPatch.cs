using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Verse;


namespace Multiplayer.Client._1_6Additions
{
    [HarmonyPatch(typeof(WeatherDecider), nameof(WeatherDecider.StartInitialWeather))]
    static class WeatherDeciderPatch
    {
        static bool Prefix(WeatherDecider __instance)
        {
            foreach (var w in __instance.map.Biome.baseWeatherCommonalities)
                Log.Message(w.weather);
            if (!__instance.map.Biome.baseWeatherCommonalities.Any())
                Log.Message("NO WEATHERS IN THE MAP BIOME WEATHER COMMONS");

            return true;
        }
    }
}
