# Port gaps found during the comment/xmldoc sweep (working draft)

Not a real doc yet. Every real port limitation (as opposed to a Pascal-source finding) turned up
while sweeping "Phase N"/"commit N" references and completeness-status language out of code
comments gets logged here, so none of them get lost before the docs pass decides where they
actually belong (a `ROADMAP.md` section, or a separate to-do doc) and folds them in for real. Delete
this file once that happens.

## Entities/Combat/Galaxy pass

- **`CombatOutcome.AbortFleet` doesn't convert leftover fuel to trillum.** A real Fuel/FuelCapacity
  model exists (`Entities/FleetLogistics.cs`) that this method doesn't use; a fleet that dissolves
  via `AbortFleet` silently loses whatever fuel it was carrying instead of it landing as trillum on
  the ground. Already had a matching, more detailed entry in `docs/PASCAL_ARCHITECTURE_NOTES.md`
  ("`CombatOutcome.AbortFleet` still doesn't convert leftover fuel to trillum") that's misfiled
  there for the same reason: it's a port gap, not a Pascal-source finding.
- **`CombatOutcome.RestoreCombatant` doesn't port the Fleet branch's FleetCargoSpace/BalanceFleet/
  FuelCapacity clamp.** This port has no fleet cargo-space/fuel-capacity system. Noted in the code
  as low-severity: structurally unreachable from this method regardless, since it only ever
  subtracts casualties, which can only free up space, never exceed it. Already loosely
  cross-referenced to `docs/ROADMAP.md`'s Phase 6a movement-fidelity gaps, which is a real tracker
  location, unlike the architecture notes.
- **`CombatOutcome.DestroyEmpire` doesn't port `CleanUpNPE`.** Doesn't clean up NPE-AI-decision
  state when an empire dies. A Kingdom's own diplomacy dictionary can still reference a destroyed
  empire, which is the actual mechanism behind the SaveFormat layer's "orphan empire" handling
  (`EntityIndex` in `GameJson.cs`/`SavGameWriter.cs`). Worth a real tracked entry since it has a
  concrete, already-observed consequence elsewhere in the port.
