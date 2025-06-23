namespace Multiplayer.Common;

public enum CommandType : byte
{
    // Global scope
    GlobalTimeSpeed,
    TimeSpeedVote,
    PauseAll,
    CreateJoinPoint,
    InitPlayerData,

    // Mixed scope
    Sync,
    DebugTools,

    // Map scope
    MapTimeSpeed,
    Designator,
    SyncPawnPath,   // For path updates from PatherTick
    SyncPawnJob     // For AI-driven job starts from JobTracker
}
