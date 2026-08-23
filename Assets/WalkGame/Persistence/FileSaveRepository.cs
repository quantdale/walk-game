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
        private readonly IClock _clock;
        private readonly ISaveFileSystem _fileSystem;

        public FileSaveRepository(
            string directory,
            string fileName,
            ISaveSerializer serializer,
            SaveMigrator migrator,
            Log log,
            IClock clock = null,
            ISaveFileSystem fileSystem = null)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Save directory required.", nameof(directory));
            }

            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _migrator = migrator ?? throw new ArgumentNullException(nameof(migrator));
            _log = log ?? Log.Disabled;
            _clock = clock ?? SystemClock.Instance;
            _fileSystem = fileSystem ?? new LocalSaveFileSystem();

            _fileSystem.EnsureDirectory(directory);
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
                // Save metadata is still not an economic input, but using the trusted
                // clock keeps persistence deterministic when the device clock changes.
                profile.lastSavedAtUtc = _clock.UtcNow;
                string payload = _serializer.Serialize(profile);

                // Validate before touching the live file: a payload that cannot
                // round-trip must never become the authoritative save.
                var validation = _serializer.Deserialize(payload);
                if (validation == null)
                {
                    _log.Error("Save aborted: serialization round-trip produced null.");
                    return SaveLoadResult.Failed;
                }

                _fileSystem.WriteAllText(_tempPath, payload);

                if (_fileSystem.Exists(_mainPath))
                {
                    _fileSystem.Copy(_mainPath, _backupPath, overwrite: true);
                    // Backup already holds last-known-good; safe to remove before move.
                    _fileSystem.Delete(_mainPath);
                }
                else
                {
                    // First-ever save: the validated payload is itself the last known
                    // good state, so seed the backup from it.
                    _fileSystem.Copy(_tempPath, _backupPath, overwrite: true);
                }

                _fileSystem.Move(_tempPath, _mainPath);
                return SaveLoadResult.Success;
            }
            catch (Exception ex)
            {
                _log.Error($"Save failed while rotating save files ({ex.GetType().Name}).");
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
            return _fileSystem.Exists(_mainPath);
        }

        public bool BackupExists()
        {
            return _fileSystem.Exists(_backupPath);
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

            if (!_fileSystem.Exists(path))
            {
                failure = SaveLoadResult.Empty;
                return false;
            }

            try
            {
                string payload = _fileSystem.ReadAllText(path);
                var parsed = _serializer.Deserialize(payload);
                if (parsed == null)
                {
                    _log.Warning("Unreadable save slot.");
                    failure = SaveLoadResult.Failed;
                    return false;
                }

                if (!_migrator.TryMigrateToCurrent(parsed, out string migrationError))
                {
                    _log.Error($"Incompatible save schema: {migrationError}");
                    failure = SaveLoadResult.IncompatibleSchema;
                    return false;
                }

                SaveValidator.RepairAndValidate(parsed, _clock, _log);
                profile = parsed;
                return true;
            }
            catch (Exception ex)
            {
                _log.Warning($"Corrupt save slot ({ex.GetType().Name}).");
                failure = SaveLoadResult.Failed;
                return false;
            }
        }

        private void TryCleanupTemp()
        {
            try
            {
                if (_fileSystem.Exists(_tempPath))
                {
                    _fileSystem.Delete(_tempPath);
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
                if (_fileSystem.Exists(path))
                {
                    _fileSystem.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Could not delete save slot ({ex.GetType().Name}).");
            }
        }
    }

    /// <summary>Production implementation of the repository file-operation seam.</summary>
    internal sealed class LocalSaveFileSystem : ISaveFileSystem
    {
        public void EnsureDirectory(string directory)
        {
            Directory.CreateDirectory(directory);
        }

        public bool Exists(string path)
        {
            return File.Exists(path);
        }

        public string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }

        public void WriteAllText(string path, string contents)
        {
            File.WriteAllText(path, contents);
        }

        public void Copy(string sourceFileName, string destFileName, bool overwrite)
        {
            File.Copy(sourceFileName, destFileName, overwrite);
        }

        public void Delete(string path)
        {
            File.Delete(path);
        }

        public void Move(string sourceFileName, string destFileName)
        {
            File.Move(sourceFileName, destFileName);
        }
    }
}
