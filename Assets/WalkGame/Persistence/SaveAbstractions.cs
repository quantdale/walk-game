using System;
using WalkGame.Core;

namespace WalkGame.Persistence
{
    public enum SaveLoadResult
    {
        Success = 0,
        Empty = 1,
        RecoveredFromBackup = 2,
        Failed = 3,
        IncompatibleSchema = 4
    }

    /// <summary>Serializes canonical profile state (DATA_MODEL.md 23).</summary>
    public interface ISaveSerializer
    {
        string Serialize(PlayerProfile profile);
        PlayerProfile Deserialize(string payload);
    }

    /// <summary>
    /// Local save repository. Implementations must write atomically and keep one
    /// last-known-good backup (TECHNICAL_ARCHITECTURE 15).
    /// </summary>
    public interface ISaveRepository
    {
        SaveLoadResult Save(PlayerProfile profile);
        /// <summary>Returns false when no save exists; never throws on corruption.</summary>
        bool TryLoad(out PlayerProfile profile, out SaveLoadResult result);

        /// <summary>Diagnostics for tests/debug tooling.</summary>
        bool MainSaveExists();
        bool BackupExists();
        void DeleteAll();
    }
}
