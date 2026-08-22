using UnityEngine;
using WalkGame.Core;

namespace WalkGame.World
{
    /// <summary>
    /// Composition-root-owned registry so presentation components can resolve content
    /// without static singletons. Only GameHost (the composition root) may WRITE these
    /// during boot; everything else reads. Enforced by convention + code review rather
    /// than visibility, because App and World are separate assemblies by design.
    /// </summary>
    public static class WorldRegistry
    {
        /// <summary>Write from GameHost.Boot only.</summary>
        public static IContentCatalog CurrentCatalog { get; set; }

        /// <summary>Write from GameHost.Boot / region loading only.</summary>
        public static RegionDefinition CurrentRegion { get; set; }
    }
}
