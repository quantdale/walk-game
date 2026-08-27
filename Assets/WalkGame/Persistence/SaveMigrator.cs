using System;
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
        public bool TryMigrateToCurrent(PlayerProfile profile, out string error)
        {
            if (profile == null)
            {
                error = "Profile null.";
                return false;
            }

            if (profile.schemaVersion > SaveSchemaVersions.Current)
            {
                error = $"Save schema {profile.schemaVersion} is newer than supported {SaveSchemaVersions.Current}.";
                return false;
            }

            if (profile.schemaVersion < SaveSchemaVersions.MinimumSupported)
            {
                error = $"Save schema {profile.schemaVersion} is below minimum supported {SaveSchemaVersions.MinimumSupported}.";
                return false;
            }

            while (profile.schemaVersion < SaveSchemaVersions.Current)
            {
                int before = profile.schemaVersion;
                bool handled = false;
                // Add sequential steps here as the schema evolves:
                //   case 1: MigrateV1ToV2(profile); handled = true; break;
                switch (before)
                {
                    default:
                        break;
                }
                if (!handled)
                {
                    error = $"No migration path from schema {before} to {before + 1} (current {SaveSchemaVersions.Current}).";
                    return false;
                }
                if (profile.schemaVersion != before + 1)
                {
                    error = $"Migration from {before} failed to advance to {before + 1} (got {profile.schemaVersion}).";
                    return false;
                }
            }

            if (profile.schemaVersion != SaveSchemaVersions.Current)
            {
                error = $"Migration did not reach Current {SaveSchemaVersions.Current} (got {profile.schemaVersion}).";
                return false;
            }

            error = null;
            return true;
        }
    }
}

