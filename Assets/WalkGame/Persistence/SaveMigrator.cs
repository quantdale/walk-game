using System;
using System.Collections.Generic;
using WalkGame.Core;

namespace WalkGame.Persistence
{
    /// <summary>
    /// Sequential schema migrations (DATA_MODEL.md 21). Each step is deterministic and
    /// unit-tested. v1 is the initial schema; add MigrateV1ToV2-style steps when the
    /// schema breaks and bump SaveSchemaVersions.Current.
    /// Never rely on default deserializer behavior for destructive changes.
    /// </summary>
    public sealed class SaveMigrator
    {
        private readonly int _currentSchema;
        private readonly int _minimumSupported;
        private readonly IDictionary<int, Action<PlayerProfile>> _migrationSteps;

        /// <summary>Uses the canonical production schema policy.</summary>
        public SaveMigrator()
            : this(SaveSchemaVersions.Current, SaveSchemaVersions.MinimumSupported, null)
        {
        }

        /// <summary>
        /// Injectable migration table used by contract tests to exercise future-step
        /// invariants without pretending that an unimplemented production migration exists.
        /// The default constructor is the only production policy and targets the canonical
        /// SaveSchemaVersions values.
        /// </summary>
        public SaveMigrator(
            int currentSchema,
            int minimumSupported,
            IDictionary<int, Action<PlayerProfile>> migrationSteps)
        {
            if (minimumSupported < 0 || currentSchema < minimumSupported)
            {
                throw new ArgumentOutOfRangeException(nameof(currentSchema), "Schema bounds are invalid.");
            }

            _currentSchema = currentSchema;
            _minimumSupported = minimumSupported;
            _migrationSteps = migrationSteps ?? new Dictionary<int, Action<PlayerProfile>>();
        }

        public bool TryMigrateToCurrent(PlayerProfile profile, out string error)
        {
            if (profile == null)
            {
                error = "Profile null.";
                return false;
            }

            if (profile.schemaVersion > _currentSchema)
            {
                error = $"Save schema {profile.schemaVersion} is newer than supported {_currentSchema}.";
                return false;
            }

            if (profile.schemaVersion < _minimumSupported)
            {
                error = $"Save schema {profile.schemaVersion} is below minimum supported {_minimumSupported}.";
                return false;
            }

            while (profile.schemaVersion < _currentSchema)
            {
                int before = profile.schemaVersion;
                if (!_migrationSteps.TryGetValue(before, out var migration) || migration == null)
                {
                    error = $"No migration path from schema {before} to {before + 1} (current {_currentSchema}).";
                    return false;
                }

                try
                {
                    migration(profile);
                }
                catch (Exception ex)
                {
                    error = $"Migration from {before} threw {ex.GetType().Name}.";
                    return false;
                }

                if (profile.schemaVersion != before + 1)
                {
                    error = $"Migration from {before} failed to advance to {before + 1} (got {profile.schemaVersion}).";
                    return false;
                }
            }

            if (profile.schemaVersion != _currentSchema)
            {
                error = $"Migration did not reach Current {_currentSchema} (got {profile.schemaVersion}).";
                return false;
            }

            error = null;
            return true;
        }
    }
}

