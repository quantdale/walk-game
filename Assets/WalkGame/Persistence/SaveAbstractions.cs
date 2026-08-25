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
        IncompatibleSchema = 4,

        /// <summary>
        /// The backup loaded, but the main file holds a NEWER schema than this build
        /// can migrate (ADR 0007). Distinct from <see cref="RecoveredFromBackup"/> so the
        /// application can fail closed: playing would autosave an older-schema world
        /// over forward evidence, so the session must not durably mutate.
        /// </summary>
        RecoveredFromBackupForwardSchema = 5
    }

    /// <summary>Serializes canonical profile state (DATA_MODEL.md 23).</summary>
    public interface ISaveSerializer
    {
        string Serialize(PlayerProfile profile);
        PlayerProfile Deserialize(string payload);
    }

    /// <summary>
    /// File operations used by the save repository. Keeping this seam outside the
    /// repository lets fault-injection tests model an interrupted write or backup
    /// rotation without changing the production algorithm.
    /// </summary>
    public interface ISaveFileSystem
    {
        void EnsureDirectory(string directory);
        bool Exists(string path);
        string ReadAllText(string path);
        void WriteAllText(string path, string contents);
        void Copy(string sourceFileName, string destFileName, bool overwrite);
        void Delete(string path);
        void Move(string sourceFileName, string destFileName);
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

        /// <summary>
        /// Explicit destructive-recovery support (ADR 0007): move every save slot's
        /// bytes to deterministic quarantine locations without destroying them, so a
        /// player-initiated "start over" preserves forensic evidence instead of wiping it.
        /// </summary>
        void QuarantineAll();
    }
}
