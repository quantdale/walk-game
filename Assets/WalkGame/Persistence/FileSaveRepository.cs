using System;
using System.IO;
using WalkGame.Core;

namespace WalkGame.Persistence
{
    /// <summary>
    /// JSON file repository implementing the atomic write strategy:
    /// serialize -> validate round-trip -> write temp -> rotate backup -> replace main.
    /// Corruption of the main file falls back to the backup; a missing or dead backup
    /// is reported instead of silently wiping player progress (TECHNICAL_ARCHITECTURE 15).
    /// </summary>
    public sealed class FileSaveRepository : ISaveRepository
    {
        private readonly string _mainPath;
        private readonly string _backupPath;
        private readonly string _tempPath;
        private readonly ISaveSerializer _serializer;
        private readonly SaveMigrator _migrator;
        private readonly Log _log;

        public FileSaveRepository(string directory, string fileName, ISaveSerializer serializer, SaveMigrator migrator, Log log)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Save directory required.", nameof(directory));
            }

            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _migrator = migrator ?? throw new ArgumentNullException(nameof(migrator));
            _log = log ?? Log.Disabled;

            Directory.CreateDirectory(directory);
            _mainPath = Path.Combine(directory, fileName);
            _backupPath = _mainPath + ".bak";
            _tempPath = _mainPath + ".tmp";
        }

        public SaveLoadResult Save(PlayerProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            try
            {
                // Intentional wall clock: lastSavedAtUtc is save-file metadata for
                // debugging, not economic time (campaign S9); injecting a clock here
                // would not change any gameplay decision.
                profile.lastSavedAtUtc = DateTime.UtcNow;
                string payload = _serializer.Serialize(profile);

                // Validate before touching the live file: a payload that cannot
                // round-trip must never become the authoritative save.
                var validation = _serializer.Deserialize(payload);
                if (validation == null)
                {
                    _log.Error("Save aborted: serialization round-trip produced null.");
                    return SaveLoadResult.Failed;
                }

                File.WriteAllText(_tempPath, payload);

                if (File.Exists(_mainPath))
                {
                    File.Copy(_mainPath, _backupPath, overwrite: true);
                    // Backup already holds last-known-good; safe to remove before move.
                    File.Delete(_mainPath);
                }
                else
                {
                    // First-ever save: the validated payload is itself the last known
                    // good state, so seed the backup from it.
                    File.Copy(_tempPath, _backupPath, overwrite: true);
                }

                File.Move(_tempPath, _mainPath);
                return SaveLoadResult.Success;
            }
            catch (Exception ex)
            {
                _log.Error($"Save failed: {ex.Message}");
                TryCleanupTemp();
                return SaveLoadResult.Failed;
            }
        }

        public bool TryLoad(out PlayerProfile profile, out SaveLoadResult result)
        {
            profile = null;
            result = SaveLoadResult.Empty;

            if (TryReadProfile(_mainPath, out var loaded, out var migrated))
            {
                profile = loaded;
                result = SaveLoadResult.Success;
                return true;
            }

            if (migrated == SaveLoadResult.IncompatibleSchema)
            {
                // Never fall back to an older backup across schema boundaries blindly:
                // migrations are sequential, so a backup at the same version is safe.
                if (TryReadProfile(_backupPath, out loaded, out migrated))
                {
                    profile = loaded;
                    result = SaveLoadResult.RecoveredFromBackup;
                    return true;
                }

                result = SaveLoadResult.IncompatibleSchema;
                return false;
            }

            if (TryReadProfile(_backupPath, out loaded, out migrated))
            {
                profile = loaded;
                result = SaveLoadResult.RecoveredFromBackup;
                return true;
            }

            result = MainSaveExists() || BackupExists() ? SaveLoadResult.Failed : SaveLoadResult.Empty;
            return false;
        }

        public bool MainSaveExists()
        {
            return File.Exists(_mainPath);
        }

        public bool BackupExists()
        {
            return File.Exists(_backupPath);
        }

        public void DeleteAll()
        {
            SafeDelete(_mainPath);
            SafeDelete(_backupPath);
            SafeDelete(_tempPath);
        }

        private bool TryReadProfile(string path, out PlayerProfile profile, out SaveLoadResult failure)
        {
            profile = null;
            failure = SaveLoadResult.Success;

            if (!File.Exists(path))
            {
                failure = SaveLoadResult.Empty;
                return false;
            }

            try
            {
                string payload = File.ReadAllText(path);
                var parsed = _serializer.Deserialize(payload);
                if (parsed == null)
                {
                    _log.Warning($"Unreadable save at '{path}'.");
                    failure = SaveLoadResult.Failed;
                    return false;
                }

                if (!_migrator.TryMigrateToCurrent(parsed, out string migrationError))
                {
                    _log.Error($"Incompatible save at '{path}': {migrationError}");
                    failure = SaveLoadResult.IncompatibleSchema;
                    return false;
                }

                SaveValidator.RepairAndValidate(parsed, _log);
                profile = parsed;
                return true;
            }
            catch (Exception ex)
            {
                _log.Warning($"Corrupt save at '{path}': {ex.Message}");
                failure = SaveLoadResult.Failed;
                return false;
            }
        }

        private void TryCleanupTemp()
        {
            try
            {
                if (File.Exists(_tempPath))
                {
                    File.Delete(_tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        private void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Could not delete '{path}': {ex.Message}");
            }
        }
    }
}
