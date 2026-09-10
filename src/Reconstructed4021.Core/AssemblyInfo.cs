using System.Runtime.CompilerServices;

// Reconstructed4021.LegacyNpe is this port's Pascal-derived NPE AI implementation (KingdomTurnHandler
// and friends) -- a friend assembly Core was already designed around before the split (see e.g.
// FleetLogistics.CargoSpacePerUnit/CombatOutcome.AbortFleet/DestroyFleet/FleetLifecycle.NoShips'
// own doc comments: each is internal, not public, specifically because NpeToolkit -- and only
// NpeToolkit -- needs it). This grants that one assembly the same access "same assembly" gave it
// before the split, without widening Core's actual public API for every other consumer.
[assembly: InternalsVisibleTo("Reconstructed4021.LegacyNpe")]
