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

            while (profile.schemaVersion < SaveSchemaVersions.Current)
            {
                // Add sequential steps here as the schema evolves:
                //   case 1: MigrateV1ToV2(profile); break;
                // Each step mutates the profile then sets profile.schemaVersion to the
                // next version. The loop re-runs until the save reaches Current.
                break;
            }

            error = null;
            return true;
        }
    }
}

