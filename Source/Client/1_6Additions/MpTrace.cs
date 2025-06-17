// In new file: MpTrace.cs

using Microsoft.SqlServer.Server;
using System;
using Verse;

namespace Multiplayer.Client
{

    [StaticConstructorOnStartup]
    public static class MpTrace
    {
        private static bool IsHost => Multiplayer.LocalServer != null;
        private static string Side => IsHost ? "[HOST]" : "[CLIENT]";

        /// <summary>Logs a message only if Sync Tracing is enabled. Use this for high-frequency events.</summary>
        public static void Verbose(string message)
        {
            if (Multiplayer.settings.syncTracing)
            {
                Log.Message($"[MP-TRACE]{Side} {message}");
            }
        }

        /// <summary>Always logs an informational message, regardless of settings. Use for key lifecycle events.</summary>
        public static void Info(string message)
        {
            Log.Message($"[MP-INFO]{Side} {message}");
        }

        /// <summary>Always logs a warning message.</summary>
        public static void Warning(string message)
        {
            Log.Warning($"[MP-WARN]{Side} {message}");
        }

        /// <summary>Always logs an error message.</summary>
        public static void Error(string message)
        {
            Log.Error($"[MP-ERROR]{Side} {message}");
        }
    }
}
