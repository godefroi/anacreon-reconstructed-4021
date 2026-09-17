# Sci-fi scenario concepts (Artifacts / Transactions / Victory Conditions)

This is a planning document, not a description of working functionality. Artifacts, Transactions,
and Victory Conditions are real Pascal subsystems (`CDETYPES.PAS`/`ARTIFACT.PAS`/`TRANSACT.PAS`/
`CODE.PAS`) that were built, then cut from the shipped game at every layer at once, before this port
ever started; see `docs/QUESTIONS_FOR_GEORGE.md`'s "dead code" section for the full archaeology.
Nothing in `Reconstructed4021.Core` implements any of this today. The scenarios below assume someone
finishes the port work first, and are written with enough mechanical detail that a future agent can
build toward them without having to re-derive the Pascal semantics from scratch.

## Prerequisite work (build this first, in roughly this order)

1. **The action-code VM.** Port `CDETYPES.PAS` as a small `Core.Scripting` (or similar) namespace:
   `VariableRecord` becomes a C# union type (bool/scalar/empire/object-ref/coordinate, matching
   `BoolVRT`/`ScalarVRT`/`EmpVRT`/`IDVRT`/`XYVRT`), `ActionTypes` becomes an enum, `ActionRecord` a
   plain record with an `AType`, an immediate `VariableRecord`, and up to 4 byte parameters. Port
   `CODE.PAS`'s `CodeInterpreter`/`ExecuteAction`. Two things need real design work, not transcription:
   - `CreateACT`/`DestructACT`/`WinGameACT` have no bodies anywhere in either Pascal source tree
     (`ExecuteAction`'s own `CASE` statement never mentions them). Their intent is documented inline
     (`{ create artifact of type p1 at p2 }`, `{ destroy artifact p1 }`, `{ p1 player wins the game }`)
     but the actual behavior has to be designed here, not ported.
   - `MenuDisplayACT`/`MenuInitializeACT`/`MenuAddFleetsACT` are Terminal.Gui/DOS-console presentation
     calls in real Pascal (`OpenWindow`/`DisplayMenu`/`GetCharacter`). In `Reconstructed4021.Tui2` these
     need a `Panemonde` overlay (a `SingleSelectOverlay<T>`-shaped picker would cover the "pick one line"
     case) that the interpreter can push and then suspend on, since `CodeInterpreter` is currently a
     synchronous call and a player-facing menu is not.
2. **Artifacts.** Port `ARTIFACT.PAS`'s `DefineArtifact`/`CreateArtifact`/`ActiveSituation`/
   `CloseUpSituation` fairly directly (they're complete, working Pascal). Add an `UpdateSituation` call
   from `AnnualTickHandler`'s yearly loop (real Pascal declares the `UpdateSIT` situation type but,
   consistent with everything else in this feature, nothing in either source tree ever calls it: this
   would be new, not a port).
3. **Transactions.** Port `TRANSACT.PAS`'s `DefineTransaction`/`Transaction`/`InitTransaction`
   (complete, working Pascal) plus a `Reconstructed4021.Tui2` command mirroring `MSCCOMM.PAS`'s
   `TransactionCommand`: pick a world, pick one of the player's own fleets there, run the transaction.
4. **Scenario-file grammar.** Extend `ScenarioLoader.cs`'s token switch (`case "CREATEWORLD": ...` etc.,
   around line 173) with `BEGINARTIFACTS`/`ENDARTIFACTS`, `DEFINEARTIFACT`, `CREATEARTIFACT`,
   `BEGINTRANSACTIONS`/`ENDTRANSACTIONS`, and `DEFINETRANSACTION`, matching `NEWGAME.PAS`'s own
   (commented-out) `Artifacts`/`Transactions` parsers. One gap needs inventing, not copying: real
   Pascal's `DefineNewArtifact` reads a `NoOfAttr` count token and then never loops over it, hardcoding
   `ArtAttr:=[]`/`ArtSits:=[]` unconditionally (dead `i,j: Word` and `SType: SituationTypes` locals in
   that procedure are the tell). The per-item syntax for attribute keywords (`MOBILE`/`UNIQUE`/etc.)
   and situation keywords (`ACTIVE`/`CLOSEUP`/`UPDATE`) was never finished even in the original, so this
   port has to design that token grammar itself. The scenario write-ups below assume a plain
   space-separated keyword list terminated the same way `CREATEARTIFACT`'s 10 register values are
   (fixed count, or an explicit terminator token); treat that as a proposal, not a spec to match.
5. **Victory check.** `WinGameACT` needs to set some `Game`-level "there is a winner" state that
   `TurnLoop.Start` (`src/Reconstructed4021.Tui2/NewGame/TurnLoop.cs:66-75`) checks alongside its
   existing `AnyHumanPlayersRemain`/all-NPEs-eliminated checks, and both `Tui`/`Tui2` need a "Victory by
   scenario condition" screen distinct from the existing "every enemy empire destroyed" one.

## Shared vocabulary (so each scenario below doesn't repeat it)

- **Artifact attributes** (`ARTIFACT.PAS`'s `Attributes`): `MobileATT` (can be carried by a fleet),
  `UsableByOwnATT`/`UsableByOtherATT` (who can trigger its `ACTIVE` situation), `UniqueATT` (only one
  instance), `PieceOfATT` (this is one piece of a set, `ArtPieceOf` says which set), `HiddenToAllATT`/
  `HiddenToOtherATT` (invisible until some trigger reveals it; nothing in either Pascal tree implements
  the reveal side of this either, so "how a hidden artifact becomes visible" is another open design
  question, not just a flag to set).
- **Situations** (`SituationTypes`): `ActiveSIT` (player activates it, `ArtifactCommand`'s own `<A>`
  key), `CloseUpSIT` (player examines the artifact's world), `UpdateSIT` (yearly tick, currently
  unwired even conceptually, see above).
- **Registers**: 10 per-artifact/per-transaction "globals" (`RegisterArray`, indices 0-9, persisted with
  the artifact/transaction) plus 10 scratch "registers" reset at the start of each `CodeInterpreter`
  call. `GetParameter`/`SetParameter`'s encoding: `R0..R9` read/write a scratch register, `V0..V9`
  read/write a global, anything else is a literal (`EvaluateImmediate` parses `TRUE`/`FALSE`, `INDEP`,
  `WORLD<n>`, `FLEET<n>`, `I<objtyp>:<index>`, `X<x>,<y>`, or a bare number).
- **Action syntax used below**: `ASSIGN <dest> <src>` (dest := src), `IF <bool> ... END`,
  `WHILE <bool> ... END`, `ISEQUAL <a> <b> <dest>` (`ISGREATER`/`ISLESSER`/`ISNOTEQUAL` the same shape),
  `DISPLAY <text-id> <y>`, `GETOBJECTPOWER <object> <dest>`, `GETARTIFACTCOORD <artifact> <dest>`,
  `WINGAME <empire>` (needs the new `ExecuteAction` case from step 1).

Every code listing below is illustrative syntax, not literal working `.SCN` text: nothing parses it yet.

---

## 1. Foundation (Asimov): hold the Plan, not the galaxy

Anacreon's own namesake: a barbarian kingdom in the Foundation novels. A fitting scenario is one where
conquest is beside the point.

**Setup.** One world, far from the starting empires, is the Foundation itself: `HiddenToAllATT` until
discovered. A `UniqueATT` artifact ("The Seldon Vault") sits there.

**Artifact.** `DEFINEARTIFACT 1 "SeldonVault" desc=12 attr=[UNIQUE, HIDDENTOALL] size=0 pieceof=0
situations=[CLOSEUP, UPDATE]`, with code:
```
CLOSEUP:
  DISPLAY 13 5              { "you have found something ancient" flavor text }
  ASSIGN V0 TRUE            { V0: "Plan known to at least one empire" }
UPDATE:
  ASSIGN R0 V1              { V1: turns elapsed since discovery }
  ISGREATER R0 100 R1
  IF R1
    WINGAME V2              { V2: which empire currently owns the Foundation's world }
  END
  ASSIGN V1 V1+1            { needs an INCREMENT-shaped action, or two ASSIGNs through a scratch add;
                              real Pascal's action set has no arithmetic op beyond comparisons, so this
                              specific line needs a new ActionTypes member too, not just a new body }
```

**Win condition.** Not "eliminate everyone." Whoever holds the Foundation's world 100 years after its
discovery wins, whether or not any other empire has been conquered, a deliberate mismatch with this
port's existing "every NPE eliminated" victory check (`TurnLoop.cs`) that a scenario-level win
condition is supposed to be able to override.

**What needs inventing beyond the prerequisite list.** Arithmetic (increment/add) isn't in the action
set at all; either add an `AddACT`, or model "elapsed turns" as something `Transaction`'s own per-empire
register bank already tracks per-call rather than needing the artifact to count it itself.

## 2. Star Wars (original trilogy): the plans are the artifact

`ArtPieceOf` exists specifically for this: a complete artifact assembled from pieces scattered across
worlds.

**Setup.** Three worlds each hold one `PieceOfATT` artifact (pieceof group 1). A rebel-aligned empire
must collect all three (`MobileATT`, so a fleet can carry them) and deliver them to a friendly world via
`TransactionCommand`. The imperial empire's own capital has a `UsableByOwnATT` artifact ("Death Star")
whose `ActiveSIT` situation is the one legitimate home for the otherwise-cut `HolocaustCommand`/
`HolocaustWorld` mechanic (`ATTACK.PAS`/`MSCCOMM.PAS`, also disabled, also documented in
`QUESTIONS_FOR_GEORGE.md`); `GETOBJECTPOWER` on the target reads its defenses first so the code can
gate whether the shot even lands.

**Transaction** (on the rebel delivery world):
```
DEFINETRANSACTION WORLD7
  ISEQUAL R0 3 R1              { R0: how many plan-pieces this transacting fleet is carrying, computed
                                  via a new GETCARGOCOUNT-shaped action -- doesn't exist yet either }
  IF R1
    ASSIGN V0 TRUE             { "plans delivered" }
    WINGAME R2                 { R2: the empire running this transaction }
  END
```

**What needs inventing.** A way to read an artifact's presence in a fleet's own cargo (none of
`GETARTIFACTCOORD`/`GETOBJECTPOWER` do this); `MobileATT`'s own "artifact rides in a fleet's hold"
mechanics aren't specified anywhere in `ARTIFACT.PAS` either, so "how does an artifact move with a
fleet" is itself an open design question, not just missing plumbing.

## 3. Dune: the spice must flow, but not forever

**Setup.** One world (Arrakis-equivalent) has an enormous Trillum output but is otherwise a normal,
visible planet, not hidden. Two artifacts: a `UniqueATT`, non-mobile "Worm Sign" that periodically
threatens any fleet sitting on the world too long, and a `UsableByOwnATT` "Prescience" artifact that
lets its owner peek at a rival's numbers before a fight.

**Artifact ("Worm Sign"), `UPDATE` situation:**
```
GETOBJECTPOWER WORLD4 R0        { R0: current defending power at Arrakis }
ISGREATER R0 500 R1             { fleets have been sitting still and stacking up }
IF R1
  DISPLAY 20 5                  { "the sands stir..." }
  { real Pascal's HostileLifeAttackedTroops-shaped news/damage isn't a scriptable action either --
    reusing AnnualTickHandler's own HostileLife mechanic directly, rather than teaching the action-code
    VM to deal damage, is probably the pragmatic answer here }
END
```

**Artifact ("Prescience"), `ACTIVE` situation:**
```
GETOBJECTPOWER <target> R0      { target supplied by whoever activates it, via ArtifactCommand's own
                                   menu flow -- needs a target-picker parameter the current ACTIVE
                                   situation call (ArtID, Activator only) doesn't carry }
DISPLAY R0 10                   { shows the raw number back to the activating player }
```

**Win condition.** Hold Arrakis for N consecutive `UPDATE` ticks without losing it to revolt or an
enemy fleet, the same "hold, don't conquer" shape as the Foundation scenario, reusing whatever counter
mechanism that one settles on.

## 4. 2001 / Star Trek "first contact": the monolith

**Setup.** A single `HiddenToAllATT`, `UniqueATT` artifact sits at a random coordinate, not tied to any
world (`ArtifactRecord.Loc` can be a bare `XYCoord`, not just an `IDNumber`, per `ArtifactLoc`'s own
`SameID(Loc.ID,EmptyQuadrant)` branch, so a "deep space" artifact is already representable).

**Artifact, `CLOSEUP` situation** (fires the moment any fleet's own Close Up examines this sector):
```
ASSIGN V0 TRUE                   { reveal: HIDDENTOALL should stop applying from here on -- another
                                    "reveal" mechanic Pascal never actually implemented }
DISPLAY 30 5                     { the monolith's own flavor text }
```

**Artifact, `ACTIVE` situation:**
```
{ no existing action grants a tech level directly -- EmpireGainedTechLevel is a news headline this
  port's Core already has (Types/NewsType.cs), but nothing in the action-code VM can trigger the
  underlying SetEmpireTechnology-shaped effect. A new action (e.g. GrantTechLevelACT) is the honest
  answer, not a workaround through existing primitives. }
GRANTTECHLEVEL Activator +1
DISPLAY 31 5                     { galaxy-wide "a signal has been detected" broadcast, reusing
                                    AddGlobalNews the same way SelfDestructObject already does }
```

**What needs inventing.** A "reveal a hidden artifact" mechanic (see above), and at least one brand
new action type for the tech grant, since this is the one scenario here that can't be built from the
16 actions Pascal ever defined at all.

## 5. Alien: the derelict is a trap, not a reward

**Setup.** Several `HiddenToOtherATT` artifacts (visible only to the empire that found them, not
broadcast to everyone the way `HiddenToAllATT` implies) are scattered as "ancient wreckage." Most are
inert flavor. One is not.

**Artifact ("Derelict"), `ACTIVE` situation:**
```
{ Real Pascal's own HostileLife mechanic (AnnualTickHandler.Revolution.cs's HostileLife, ported from
  UPDATE.PAS) already models "aliens attack troops"/"aliens kill population" as a yearly world-level
  event, not a one-shot triggerable effect. Reusing it directly (call the same C# method the port
  already has, from a new action, rather than reimplementing damage math in the action-code VM) is the
  pragmatic path -- the action-code layer should stay a thin trigger, not a second combat engine. }
TRIGGERHOSTILELIFE Activator's-current-world
DISPLAY 40 5                      { too late for a warning now }
```

**Win condition.** None needed as its own thing; this is a hazard scenario meant to sit inside a
larger conquest game (the existing "eliminate every NPE" victory already covers it), included here
because it's the cleanest illustration of "an artifact's `ACTIVE` situation can be a punishment, not
just a reward," which none of the other six scenarios show.

## 6. The Expanse: the protomolecule sample

**Setup.** A single `MobileATT`, `UniqueATT` artifact, tradeable/smuggleable between empires by
whichever fleet is carrying it (same open "artifact rides in cargo" question as the Star Wars
scenario above).

**Artifact, `ACTIVE` situation:**
```
GETOBJECTPOWER Activator's-empire R0        { baseline military power before the boost }
{ apply a large, one-time tech/production boost -- same "needs a new action" gap as scenario 4 }
GRANTTECHBOOST Activator
{ then the cost: spike the activating world's own revolution index hard, reusing
  CombatOutcome.ChangeTotalRevIndex the same way SelfDestructObject/Rebellion already do }
SPIKEREVINDEX Activator's-current-world +40
DISPLAY 50 5
```

**Why this one's worth including.** It's the only scenario here where the interesting design question
isn't "what new action do we need" so much as "should a scripted artifact be allowed to call back into
Core's own real game-state mutators (`ChangeTotalRevIndex`, `SetEmpireTechnology`) directly, or should
the action-code VM only ever touch a sandboxed subset of state?" Worth resolving once, as a design
principle, before scenario 4's tech grant and this one's rev-index spike get built as two different
answers to the same question by accident.

## 7. Battlestar Galactica: race to Earth, not fight to the last empire

**Setup.** A chain of `UniqueATT` artifacts (an "arc of the covenant" set, distinct from the Star Wars
scenario's `PieceOfATT` group since these don't combine, they chain), each one's `ACTIVE` situation
revealing the next artifact's coordinates rather than the final destination outright. The last link
reveals Earth's own world, itself `HiddenToAllATT` until then.

**Artifact (a middle link in the chain), `ACTIVE` situation:**
```
GETARTIFACTCOORD <next-artifact-in-chain> R0
DISPLAY 60 5
ASSIGN V0 R0                     { world remembers it's been consulted }
```

**Artifact (Earth itself, once revealed), no code needed beyond `CLOSEUP`:**
```
CLOSEUP:
  WINGAME Player                 { first empire whose fleet actually reaches and holds this world wins }
```

**Win condition.** A pure race, not a fight, and not a hold-for-N-turns endurance check like the
Foundation/Dune scenarios above, the third distinct victory shape in this document (eliminate, endure,
or arrive first) worth keeping distinct in whatever `WinGameACT` design ships, rather than collapsing
all scenario victories into one generic "some flag got set" check.

---

## Cross-cutting design questions these seven raise

Collected here rather than repeated seven times above:

- **Arithmetic.** The action set has comparisons but no add/increment. Scenario 1 and (implicitly)
  scenario 6's cost math both need it. Worth deciding once whether registers gain real arithmetic ops
  or whether "counting" is modeled some other way (e.g. a Transaction's own per-empire register bank
  incrementing itself via repeated `Transaction` calls rather than a single artifact's `UPDATE` tick).
- **Mobile artifacts and fleet cargo.** Scenarios 2 and 6 both need "this artifact currently rides
  inside this fleet's hold," a mechanic `ARTIFACT.PAS` declares a flag for (`MobileATT`) but never
  actually implements (no code anywhere moves an `ArtifactRecord.Loc` when a fleet carrying it moves).
- **Reveal mechanics for `HiddenToAllATT`/`HiddenToOtherATT`.** Scenarios 1, 4, and 7 all need "this was
  hidden, now it isn't," and nothing in the original ever implements the transition, only the two flags.
- **Should scripted code call real Core mutators directly, or only a sandboxed subset of state?**
  Raised explicitly in scenario 6; affects scenarios 1, 4, and 5 too the moment any of them touches
  tech levels, revolution index, or hostile-life combat.
- **Three distinct victory shapes, not one.** Eliminate-everyone (already built), hold-a-place-for-N-
  turns (scenarios 1 and 3), and race-to-a-place (scenario 7) all need to fit through the same
  `WinGameACT`/`TurnLoop` check without the endurance and race variants having to fake themselves as
  elimination.
