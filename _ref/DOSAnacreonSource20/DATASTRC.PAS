(* DATASTRC.PAS ----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT DataStrc;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES
   Strg,
   TextStrc,
   Types,
   Galaxy;

TYPE
   { PLANETS
      This is a record of all the information stored for each planet. }

   PlanetRecord = RECORD
      XY: XYCoord;                     { location }
      Emp: Empire;                     { empire to which it belongs }
      ScoutedBy: ScoutSet;             { set of all empires that have scouted }
      KnownBy: ScoutSet;               { set of all empires that know of it }

      Cls: WorldClass;                 { class of planet }
      Typ: WorldTypes;                 { type of planet }
      ImpExp: SelfSuffConditions;      { for each RawMType, import or export. }
      Tech: TechLevel;                 { tech level }
      Eff: Index;                      { efficiency 0-100% }
      RevIndex: Index;                 { revolution index }
      Special: SetOfSpecialConditions;
      TerraformTarget: WorldClass;     { class it's terraforming to }

      Pop: Population;                 { population }
      Ships: ShipArray;                { ships on the planet }
      Cargo: CargoArray;               { cargo on the planet }
      Defns: DefnsArray;               { defenses on the planet }
      Indus: IndusArray;               { industry on the planet }

      TriReserve: Word;                { trillum reserves }

      Reserved: ARRAY [1..16] OF Byte;

      NextID: IDNumber;                { link }
   END;  { PlanetRecord }

   PlanetArray = ARRAY [1..MaxNoOfPlanets] OF PlanetRecord;

   { STARBASES }

   StarbaseRecord = RECORD
      XY: XYCoord;                     { location }
      Emp: Empire;                     { empire to which it belongs }
      ScoutedBy: ScoutSet;             { set of empires that have scouted it }
      KnownBy: ScoutSet;               { set of all empires that know of it }

      STyp: StarbaseTypes;             { type of starbase }
      Typ: WorldTypes;                 { type of planet }
      Tech: TechLevel;                 { tech }
      Eff: Index;                      { efficiency }
      RevIndex: Index;                 { revolution index }
      Special: SetOfSpecialConditions;

      Pop: Population;                 { population }
      Ships: ShipArray;                { ships on base }
      Cargo: CargoArray;               { cargo on base }
      Defns: DefnsArray;               { defenses on base }
      Indus: IndusArray;               { industry on base }

      Move: Byte;                      { Move=No. of years til starbase moves }
      Dest: XYCoord;                   { destination }
      Status: FleetStatus;             { status (at dest, in transit, etc.) }

      Reserved: ARRAY [1..18] OF Byte;
      NextID: IDNumber;                { link }
   END;  { StarbaseRecord }

   StarbaseArray = ARRAY [1..MaxNoOfStarbases] OF StarbaseRecord;

   { FLEETS }

   FleetRecordPtr = ^FleetRecord;
   FleetRecord = RECORD
      XY: XYCoord;                     { current location }
      Emp: Empire;                     { empire to which it belongs }
      ScoutedBy: ScoutSet;             { set of empires that have scouted }

      Ships: ShipArray;                { ships in fleet }
      Cargo: CargoArray;               { cargo in fleet }

      Dest: XYCoord;                   { destination }
      Status: FleetStatus;             { InTrans, lost, etc.. }

      { The fuel that a fleet carries is given as FuelHigh*MaxInt + Fuel }
      FuelHigh: Byte;                  { high byte of fuel }
      Fuel: Integer;                   { low part of fuel }

      KnownBy: ScoutSet;

      { orders }
      NextOrder: Byte;
      OrderData: ARRAY [1..6] OF Byte;

      NPEDataIndex: Byte;

      Reserved: ARRAY [1..8] OF Byte;
      NextID: IDNumber;                { link }
   END;  { FleetRecord }

   FleetArray = ARRAY [1..MaxNoOfFleets] OF FleetRecordPtr;

   { STARGATES }

   StargateRecord = RECORD
      XY: XYCoord;                     { location }
      Emp: Empire;
      ScoutedBy: ScoutSet;             { set of empires that have scouted }
      KnownBy: ScoutSet;               { set of all empires that know of it }

      GTyp: StargateTypes;             { type of gate }
      Dest: XYCoord;                   { destination }
      WLF: ARRAY [Empire1..Indep] OF Freq; { Warp Link Frequency }

      NextID: IDNumber;                { link }
   END;  { GateRecord }

   StargateArray = ARRAY [1..MaxNoOfStargates] OF StargateRecord;

   { CONSTRUCTION SITES }

   ConstrRecord = RECORD
      XY: XYCoord;                     { location }
      Emp: Empire;                     { empire constructing }
      ScoutedBy: ScoutSet;             { set of empires that have scouted }
      KnownBy: ScoutSet;               { set of all empires that know of it }

      CTyp: ConstrTypes;               { type of construction }
                                       { (starbase, gate, etc) }
      TimeToCompletion: Byte;          { years to complete project }

      NextID: IDNumber;                { link }
   END;  { ConstructionRecord }

   ConstrArray = ARRAY [1..MaxNoOfConstrSites] of ConstrRecord;

   { Name List }

   NameRecordPtr = ^NameRecord;

   NameRecord = RECORD
      Name: String8;                   { name }
      Coord: Location;                 { coord associated }

      Next: NameRecordPtr;
   END;  { NameRecord }

   { Empire Data }

   { Defense distribution is the per cent of the total number of ships that will be
     placed in a give orbital shell.  By ship type. }
   DefenseDistributionArray = ARRAY [ShellPos,ShipTypes] of Index;

   DefenseRecord = RECORD
      ShellDefDist: DefenseDistributionArray;
      StarbaseDefDist: DefenseDistributionArray;
   END;

   ProbeRecord = RECORD
      Dest: XYCoord;                   { destination of probe }
      Status: ProbeStatus;             { status of probe }
   END;  { ProbeRecord }

   ProbeArray = ARRAY [1..NoOfProbesPerEmpire] OF ProbeRecord;

   EmpireDataRecord = RECORD
      InUse: Boolean;
      IsAPlayer: Boolean;              { player empire }
      EmpireName: String32;            { name of the empire }
      Pass: String8;                   { password }

      TimeLeft: Integer;
      Capital: IDNumber;               { capital }

      DefenseSettings: DefenseRecord;  { current defense settings }

      Probe: ProbeArray;               { probes }

      Names: NameRecordPtr;            { beginning of names }
      LastName: NameRecordPtr;         { pointer to last name }

      TotalRevIndex: Integer;          { adj to individual rev index }

      TechnologyLevel: TechLevel;
      Technology: TechnologySet;

      IsAnEmpress: Boolean;            { Is an empress }
      RevFactor: Integer;              { Permanent adj to TotalRevIndex }
      Founding: Word;                  { year of founding }
      Modifiers: SetOfEmpireModifiers; { special flags }

      Reserved: ARRAY [1..14] OF Byte;
   END;  { EmpireDataRecord }

   EmpireDataArray = ARRAY [Empire] OF EmpireDataRecord;

   { SETS }

   GlobalSetsRecord = RECORD
      SetOfActiveFleets: SET OF 1..MaxNoOfFleets;
      SetOfFleetsOf: ARRAY [Empire] OF SET OF 1..MaxNoOfFleets;
      SetOfActivePlanets: PlanetSet;
      SetOfPlanetsOf: ARRAY [Empire] OF PlanetSet;
      SetOfActiveStarbases: SET OF 1..MaxNoOfStarbases;
      SetOfStarbasesOf: ARRAY [Empire] OF SET OF 1..MaxNoOfStarbases;
      SetOfActiveGates: SET OF 1..MaxNoOfStargates;
      SetOfActiveConstructionSites: SET OF 1..MaxNoOfConstrSites;
      SetOfConstructionSitesOf: ARRAY [Empire] OF SET OF 1..MaxNoOfConstrSites;
   END;

   { THE UNIVERSE }

   UniverseRecord = RECORD
      Planet: PlanetArray;             { planet data }
      Starbase: StarbaseArray;         { starbase data }
      Fleet: FleetArray;               { fleet data }
      Stargate: StargateArray;         { gate data }
      Constr: ConstrArray;             { construction data }
      EmpireData: EmpireDataArray;     { empire data }
   END;  { UniverseRecord }

VAR
   Universe: ^UniverseRecord;
   GlobalSets: GlobalSetsRecord ABSOLUTE SetOfActiveFleets;

IMPLEMENTATION

BEGIN
New(Universe);
END.
