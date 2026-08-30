namespace Reconstructed4021.Core.Types;

/// <summary>
/// NewsTypes (NEWS.PAS:21-109), ported in full even though only a subset is wired to a real
/// <c>AddNews</c> call site yet — it's pure data (a name list), not logic, so completeness costs
/// nothing and later phases (Combat, NPE AI) don't need to keep coming back to add cases here. Each
/// member is expanded from Pascal's terse abbreviation into a readable name; the trailing comment on
/// each line is the original Pascal identifier plus (where NEWS.PAS had one) its own inline comment on
/// the headline's (Loc,...) parameter shape, kept for cross-referencing against the Pascal source when
/// a later phase wires a new call site. The last 9 members (GTech..LostF) had no inline comment in
/// NEWS.PAS itself — their expanded names are guesses at best, so they're kept as the bare Pascal
/// identifier rather than a fabricated-sounding name.
/// </summary>
public enum NewsType
{
    NoNews,                            // NoNews
    LacksRawMaterial,                  // Lack: (Loc, material) lacks raw material.
    PeopleStarving,                    // Starv: (Loc, 10s of mil) people starve.
    TechLevelIncreased,                // NTech: (Loc,NewTech) new tech level.
    TechLevelRegressed,                // RTech: (Loc,NewTech) regressed level.
    ConstructionLacksRawMaterial,      // ConsLack: (Loc, material) lacks raw material.
    ConstructionCompleted,             // ConsDone: (Loc, ConsTyp) construction done.
    WorldRebelled,                     // Rebel: (Loc) world rebels.
    RebellionSuppressed,               // URebel: (Loc, MenKilled) unsuccessful rebellion.
    RebellionWarning1,                 // RebelW1: (Loc) rebellion warning 1
    RebellionWarning2,                 // RebelW2: (Loc) rebellion warning 2
    RebellionWarning3,                 // RebelW3: (Loc) rebellion warning 3
    RebellionWarning4,                 // RebelW4: (Loc) rebellion warning 4
    ProbeOk,                           // POk: (Loc) probe ok.
    EmpireGainedTechnology,            // NCapTech: (Loc, NewTech) empire has new technology.
    EmpireGainedTechLevel,             // NCapLvl: (Loc, NewTech) empire has new tech level.
    WorldConqueredByEnemy,             // BattleL: (Loc, Emp) enemy empire conquered.
    EnemyEmpireDestroyed,              // BattleW1: (Loc, Emp) enemy empire destroyed.
    EnemyEmpireRetreated,              // BattleW2: (Loc, Emp) enemy empire retreated.
    WorldAddictedToAmbrosia,           // WAddict: (Loc) world addicted to ambrosia.
    WorldNoLongerAddicted,             // UAddict: (Loc) world no longer addicted.
    AddictsDied,                       // AddictDie: (Loc,Deaths) addicts die.
    RiotDeaths,                        // RiotsDie: (Loc,Deaths) people die from ambrosia riots.
    IndustryDestroyed,                 // IndDs: (Loc,Ind,IndI) industry destroyed.
    WorldDeclaredIndependence,         // DInd: (Loc) world declares independence.
    WorldJoinedOtherEmpire,            // Join: (Loc, Emp) world has joined other empire.
    WorldIsNowCapital,                 // NewCap: (Loc) world is now capital.
    EmpireConquered,                   // EndEmp: the empire has been conquered.
    FleetOutOfFuel,                    // NoFuel: (Loc) fleet is out of fuel.
    EnemyFleetDetected,                // FltDet: (Loc, Emp) world scanned enemy fleet from emp.
    EnemyFleetDamagedInMinefield,      // Mines: (Loc, Emp) enemy fleet took damage in mine field.
    FleetDamagedByMines,               // MinesDm: (Loc, Emp) fleet was damaged by enemy mines.
    FleetDestroyedByMines,             // MinesDs: (Loc, Emp) fleet destroyed by enemy mines.
    ConstructionSiteDestroyed,         // ConDs: (Loc, Emp) construction site destroyed by emp.
    StargateDestroyed,                 // GteDs: (Loc, Emp) stargate destroyed by emp.
    FleetDamagedByLams,                // LAMDm: (Loc, Emp) fleet damaged by enemy LAMs.
    FleetDestroyedByLams,              // LAMDs: (Loc, Emp) fleet destroyed by enemy LAMs.
    ShipsOrCargoTransferredToYou,      // TrnsShp: (Loc, Emp) Emp gave ships/cargo to Loc.
    TransferDetail,                    // Trns2: (Num,Typ) detail line.
    EmpireSoldTechnology,              // NSellTech: (Emp, Tch) Emp sells technology.
    WorldGivenToYou,                   // GInd: (Loc, Emp) Emp has given you Loc.
    NewEmpireDiscovered,               // NewPlEmp: (Loc, Emp) is a new empire.
    DefensesLackResources,             // DefLack: (Loc, material) needs res to build defenses.
    IndustryLacksMetals,               // IndLack: (Loc, material) needs metals to build ind.
    ProbeDestroyedByYou,               // PCap: (Loc, Emp) probe from emp destroyed at.
    ProbeDestroyed,                    // PDest: (Loc) probe destroyed at loc.
    MessageReceived,                   // MessR: (Emp) message received from emp.
    MessageIntercepted,                // MessI: (Loc,Emp) Loc intercepts message from emp.
    ConstructionDestroyedByUnknown,    // ConDsUNK: (Loc) construction destroyed by unknown.
    StargateDestroyedByUnknown,        // GteDsUNK: (Loc) gate destroyed by unknown.
    AttackedByUnknown,                 // BattleW2UNK: (Loc) attacked by unknown.
    FleetDestroyedByUnknown,           // BattleLUNK: (Loc) unknown destroyed fleet.
    HostileLifeKilledPopulation,       // HLPopKill: (Loc,pop) aliens kill population.
    HostileLifeAttackedTroops,         // HLMenKill: (Loc,men,nnj) aliens attack troops.
    HostileLifeJoinedTroops,           // HLJoin: (Loc,nnj) aliens join troops.
    EmpireAttackedWithLams,            // LAMDef: (Loc,emp) empire attacks with LAMs.
    DestructionDetail,                 // DestDetail: (Num,Typ) detail line for destruction.
    StarbaseOutOfFuel,                 // BseFuel: (Loc) is out of fuel.
    StarbaseBlocked,                   // BseBlocked: (Loc) is blocked.
    FleetBlocked,                      // FltBlocked: (Loc) is blocked.
    CannotGateToDenseNebula,           // NebGate: (Loc) can't gate to dense nebula.
    MineFieldCleared,                  // SRMClear: (Loc,emp) SRM cleared by emp.
    StarbaseSelfDestructed,            // BseSD: (Loc,emp) base self-destruct by emp.
    FleetsDestroyedInExplosion,        // FltSD: (Loc) fleets destroyed in exp.
    WorldHolocausted,                  // WHolo: (Loc,emp) Loc holocausted by emp.
    HolocaustDeaths,                   // DthHolo: (Deaths) deaths from holocaust.
    FleetStoppedByDisrupter,           // Disrupt: (Loc,emp) fleet stopped by disrupter.
    OutOfTrillumReserves,              // NoTriRes: (Loc) Loc has no more trillum.
    TrillumReservesVeryLow,            // TriResWarn1: (Loc) Loc is very low on trillum reserves
    TrillumReservesLow,                // TriResWarn2: (Loc) Loc is low on trillum reserves
    TroopsWantOut,                     // MilitRev: (Loc) Loc wants troops out
    RebellionQuietedByMilitary,        // RevControl: (Loc) Military on Loc quiets rebellion
    EnemyAttackedEmpireGlobal,         // GLBDest: (Loc,Enemy,Emp) Enemy attacks Emp at Loc. Dest
    EnemyConqueredWorldGlobal,         // GLBConq: (Loc,Enemy,Emp) Enemy conquers Loc from Emp
    EnemyConqueredCapitalGlobal,       // GLBCapConq: (Loc,Enemy,Emp) Enemy conquers Emp capital.
    EmpireLamStrikeGlobal,             // GLBLAMStrk: (Loc,Emp,Enemy) Emp hits Enemy with LAMs.
    WorldRevoltedGlobal,               // GLBRev: (Loc,Emp) Loc revolts from Emp.
    WorldDiscoveredByOutpost,          // OutProbe: (Loc) Loc discovered by outpost.

    GTech,  // undocumented in NEWS.PAS
    CLost,  // undocumented in NEWS.PAS
    LostP,  // undocumented in NEWS.PAS
    SMnR,   // undocumented in NEWS.PAS

    JumpDm, // undocumented in NEWS.PAS
    JumpDs, // undocumented in NEWS.PAS
    ELost,  // undocumented in NEWS.PAS
    TriAcc, // undocumented in NEWS.PAS
    LostF,  // undocumented in NEWS.PAS
}
