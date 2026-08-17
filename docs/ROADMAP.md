# Port roadmap

Bottom-up order: simulation core first, UI last. Each phase gets its own plan/design pass when it's picked up — this is just the sequence.

1. **Fleet movement** — implement `IFleetMovementHandler`: fuel, speed by ship type, jump vs. warp movement, arrival/destination handling. `TurnEngine` already calls this every turn against a stub.
2. **Fog-of-war / visibility** — implement `IVisibilityHandler`: scouting radius, `EntityVisibility<T>` population for fleets/planets/starbases/stargates/construction sites.
3. **Economy / annual tick** — implement `IAnnualTickHandler`: population growth, industry output, resource production/consumption, efficiency, revolution index.
4. **Galaxy / new-game setup** — galaxy generation or scenario loading, empire creation, initial fleet/planet placement. Needed before any of the above can run against a real game rather than hand-built test fixtures.
5. **NPE AI** — implement an `ITurnHandler` for computer empires (start with one "classic" implementation; the handler-per-empire design already supports adding an "advanced" variant later).
6. **Combat** — attack resolution, fleet/starbase destruction, capital loss and empire elimination (the turn loop currently assumes empires never leave `Game.Empires` mid-game — this phase removes that assumption).
7. **Save/load** — translation layer to read/write original `.SAV` files; explicitly does not shape the in-memory model (per earlier decision).
8. **Human interactive turn handler + Terminal.Gui UI** — the last `ITurnHandler` implementation, plus the actual windowed interface (map view, fleet orders, construction, etc.) per `TUI_LIBRARY_RECOMMENDATION.md`.
9. **Async/hotseat turn mode** — deferred multiplayer option; sequential mode (already built) is the only mode a solo player sees.

Not scheduled, pull in only if/when needed: v2 gameplay changes and new features from `PASCAL_V1_VS_V2_DIFF.md` (all opt-in, none are baseline).
