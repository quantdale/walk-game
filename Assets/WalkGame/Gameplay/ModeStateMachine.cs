using System;
using WalkGame.Core;

namespace WalkGame.Gameplay
{
    public enum GameMode
    {
        Boot = 0,
        MainMenu = 1,
        LoadingRegion = 2,
        BuilderMode = 3,
        ExploreMode = 4,
        WorldMap = 5
    }

    public struct ModeChanged
    {
        public GameMode Previous;
        public GameMode Current;
    }

    /// <summary>
    /// Explicit mode state machine (TECHNICAL_ARCHITECTURE 7). Mode is never inferred from
    /// which camera happens to be enabled. Legal transitions:
    /// Boot -> MainMenu -> (LoadingRegion -> Builder <-> Explore) -> WorldMap -> LoadingRegion.
    /// </summary>
    public sealed class ModeStateMachine
    {
        private static readonly (GameMode From, GameMode To)[] AllowedTransitions =
        {
            (GameMode.Boot, GameMode.MainMenu),
            (GameMode.MainMenu, GameMode.LoadingRegion),
            (GameMode.LoadingRegion, GameMode.BuilderMode),
            (GameMode.LoadingRegion, GameMode.ExploreMode),
            (GameMode.BuilderMode, GameMode.ExploreMode),
            (GameMode.ExploreMode, GameMode.BuilderMode),
            (GameMode.BuilderMode, GameMode.WorldMap),
            (GameMode.ExploreMode, GameMode.WorldMap),
            (GameMode.MainMenu, GameMode.WorldMap),
            (GameMode.WorldMap, GameMode.LoadingRegion),
            (GameMode.WorldMap, GameMode.MainMenu),
        };

        private readonly DomainEvents _events;
        private readonly Log _log;

        public GameMode Current { get; private set; } = GameMode.Boot;

        public ModeStateMachine(DomainEvents events, Log log)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _log = log ?? Log.Disabled;
        }

        public bool CanTransition(GameMode target)
        {
            foreach (var rule in AllowedTransitions)
            {
                if (rule.From == Current && rule.To == target)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryTransition(GameMode target)
        {
            if (!CanTransition(target))
            {
                _log.Warning($"Illegal mode transition {Current} -> {target} rejected.");
                return false;
            }

            var previous = Current;
            Current = target;
            _events.Publish(new ModeChanged { Previous = previous, Current = target });
            return true;
        }
    }
}
