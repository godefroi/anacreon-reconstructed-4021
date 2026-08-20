(* PRIMINTR.PAS ----------------------------------------------------------------

   These procedures serve as an interface between the ANACREON Data Base and the
   rest of the program.  All access to the Data Base should be done through
   these procedures.

----------------------------------------------------------------------------- *)

UNIT PrimIntr;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Strg,                                      { String primitives }
     Int,
     Types,
     Galaxy,
     DataStrc,                                  { Universe data structure. }
     DataCnst,
     Misc;                                      { Miscellaneous procedures. }

CONST
   { Use these identifiers when calling GetName }
   ShortFormat = False;
   LongFormat = True;

{ QUAD Procedures }
FUNCTION GetNebula(Pos: XYCoord): NebulaTypes;
PROCEDURE PutNebula(Pos: XYCoord; Neb: NebulaTypes);
PROCEDURE PutMine(Pos: XYCoord; Emp: Empire);
FUNCTION EnemyMine(Pos: XYCoord): Empire;
PROCEDURE GetObject(Pos: XYCoord; VAR ID: IDNumber);
PROCEDURE GetFleets(Pos: XYCoord; VAR Flts: FleetSet);

{ GET INFO Procedures }
procedure GetResources (ObjID: IDNumber; var Resources: ResourceArray);
procedure PutResources (ObjID: IDNumber; var Resources: ResourceArray);
PROCEDURE GetCoord(Obj: IDNumber; var XY: XYCoord);
PROCEDURE GetShips(Obj: IDNumber; var Ships: ShipArray);
PROCEDURE GetShipsKnown(Player: Empire; Obj: IDNumber; VAR Ships: ShipArray);
PROCEDURE GetCargo(Obj: IDNumber; var Cargo: CargoArray);
PROCEDURE GetDefns(Obj: IDNumber; var Defns: DefnsArray);
PROCEDURE GetIndus(Obj: IDNumber; var Indus: IndusArray);
FUNCTION GetTrillum(Obj: IDNumber): Resources;
FUNCTION TroopStrength(Obj: IDNumber): Word;
FUNCTION GetClass(Obj: IDNumber): WorldClass;
FUNCTION GetTech(Obj: IDNumber): TechLevel;
FUNCTION GetPopulation(Obj: IDNumber): Population;
FUNCTION GetEfficiency(Obj: IDNumber): Index;
FUNCTION GetStatus(Obj: IDNumber): Empire;
FUNCTION GetType(Obj: IDNumber): WorldTypes;
FUNCTION GetTerraformTarget(Obj: IDNumber): WorldClass;
FUNCTION GetISSP(Obj: IDNumber; Ind: IndusTypes): Byte;
FUNCTION GetRevIndex(Obj: IDNumber): Index;
PROCEDURE GetSpecial(Obj: IDNumber; VAR Setting: SetOfSpecialConditions);
FUNCTION Scouted(Emp: Empire; Obj: IDNumber): Boolean;
FUNCTION Known(Emp: Empire; Obj: IDNumber): Boolean;
FUNCTION GetConstrType(ConID: IDNumber): ConstrTypes;
FUNCTION GetConstrTimeLeft(ConID: IDNumber): Word;

{ SET INFO Procedures }
PROCEDURE PutShips(Obj: IDNumber; VAR Ships: ShipArray);
PROCEDURE PutCargo(Obj: IDNumber; VAR Cargo: CargoArray);
PROCEDURE PutDefns(Obj: IDNumber; VAR Defns: DefnsArray);
PROCEDURE PutIndus(Obj: IDNumber; VAR Indus: IndusArray);
PROCEDURE InitializeISSP(Obj: IDNumber);
PROCEDURE SetISSP(Obj: IDNumber; Ind: IndusTypes; ISSPInd: Byte);
PROCEDURE PutTrillum(Obj: IDNumber; T: Resources);
PROCEDURE PutTrillumReserves(Obj: IDNumber; NewRes: Word);
FUNCTION TrillumReserves(Obj: IDNumber): Word;
PROCEDURE SetClass(Obj: IDNumber; Class: WorldClass);
PROCEDURE SetTech(Obj: IDNumber; Tech: TechLevel);
PROCEDURE SetPopulation(Obj: IDNumber; Pop: Population);
PROCEDURE SetEfficiency(Obj: IDNumber; Eff: Index);
PROCEDURE SetStatus(Obj: IDNumber; Emp: Empire);
PROCEDURE SetType(Obj: IDNumber; Typ: WorldTypes);
PROCEDURE ChangeRevIndex(Obj: IDNumber; Chg: Integer);
PROCEDURE SetSpecial(Obj: IDNumber; NewSetting: SetOfSpecialConditions);
PROCEDURE ScoutObject(Emp: Empire; Obj: IDNumber);
PROCEDURE SetTerraformTarget(Obj: IDNumber; TargetClass: WorldClass);

{ FLEET Procedures }
PROCEDURE SetNPEDataIndex(FltID: IDNumber; Index: Word);
FUNCTION NPEDataIndex(FltID: IDNumber): Word;
FUNCTION TypeOfFleet(FltID: IDNumber): FleetTypes;
PROCEDURE SetFleetStatus(FltID: IDNumber;  NewStatus: FleetStatus);
FUNCTION GetFleetStatus(FltID: IDNumber): FleetStatus;
FUNCTION GetFleetFuel(FltID: IDNumber): Real;
PROCEDURE SetFleetFuel(FltID: IDNumber; FuelLeft: Real);

{ STARBASE Procedures }
FUNCTION GetBaseType(BaseID: IDNumber): StarbaseTypes;

{ STARGATE Procedures }
FUNCTION GetGateType(GateID: IDNumber): StargateTypes;
FUNCTION GetWarpLinkFreq(Emp: Empire; Obj: IDNumber): Freq;
PROCEDURE SetWarpLinkFreq(Emp: Empire; Obj: IDNumber; Frequency: Freq);

{ EMPIRE Procedures }
PROCEDURE GetProbe(Player: Empire; VAR PNum: Word);
PROCEDURE LaunchProbe(Player: Empire; PNum: Byte; Loc: XYCoord);
FUNCTION GetTimeLeft(Player: Empire): Integer;
PROCEDURE SetTimeLeft(Player: Empire; Time: Integer);
FUNCTION CentralizedCapital(Emp: Empire): Boolean;
PROCEDURE CreateEmpire(Emp: Empire;  Player,Empress: Boolean;
                       Name: String32;
                       Password: String8;  CapID: IDNumber;
                       Tech: TechLevel; TechSet: TechnologySet;
                       Restlessness: Integer;
                       NewModifiers: SetOfEmpireModifiers;
                       YearFounded: Word);
FUNCTION EmpireActive(Emp: Empire): Boolean;
FUNCTION EmpireAge(Emp: Empire): Word;
FUNCTION EmpirePlayer(Emp: Empire): Boolean;
FUNCTION Empress(Emp: Empire): Boolean;
FUNCTION EmpireName(Emp: Empire): String32;
FUNCTION MyLord(Emp: Empire): String32;
PROCEDURE GetDefenseSettings(Emp: Empire; VAR Def: DefenseRecord);
PROCEDURE SetDefenseSettings(Emp: Empire; VAR Def: DefenseRecord);
PROCEDURE GetEmpireTechnology(Emp: Empire; VAR Tech: TechLevel;
                              VAR TechSet: TechnologySet);
PROCEDURE SetEmpireTechnology(Emp: Empire; Tech: TechLevel;
                              TechSet: TechnologySet);
PROCEDURE ChangeTotalRevIndex(Emp: Empire; Inc: Integer);
PROCEDURE SetTotalRevIndex(Emp: Empire; NewIndex: Integer);
FUNCTION TotalRevIndex(Emp: Empire): Integer;
PROCEDURE GetCapital(Emp: Empire; VAR CapID: IDNumber);
PROCEDURE SetCapital(Emp: Empire; CapID: IDNumber);
FUNCTION AbsoluteX(X: Integer): Integer;
FUNCTION AbsoluteY(Y: Integer): Integer;
FUNCTION RelativeX(X: Coordinate): Integer;
FUNCTION RelativeY(Y: Coordinate): Integer;

{ NAME Procedures }
FUNCTION ObjectName(Emp: Empire; ObjID: IDNumber; LongFormat: Boolean): String32;
PROCEDURE GetNewName(Player: Empire; VAR NewSlot: NameRecordPtr);
PROCEDURE GetDefinedName(Emp: Empire; NamePtr: NameRecordPtr;
                         VAR DefName: String16; VAR DefCoord: Location);
PROCEDURE DefineName(Emp: Empire; NamePtr: NameRecordPtr; 
                     DefName: String16; DefCoord: Location);
PROCEDURE AddName(Player: Empire; VAR Loc: Location;
                  NameVar: String8; VAR Error: Boolean);
PROCEDURE DeleteName(Player: Empire; NameToDelete: String32);
PROCEDURE DeleteAllFleetDestNames(Emp: Empire);
PROCEDURE DeleteAllNames(Emp: Empire);
PROCEDURE Location2Index(Emp: Empire; Loc: Location; 
                         VAR NamePtr: NameRecordPtr);
PROCEDURE Name2Index(Emp: Empire; NameToFind: String16;
                     VAR NamePtr: NameRecordPtr);
PROCEDURE GetFleetName(Emp: Empire; FltID: IDNumber; VAR Strg: String32);
PROCEDURE GetCoordName(Coord: XYCoord; var Strg: String32);
PROCEDURE Name2Fleet(Emp: Empire; Strg: String32;
                     VAR ID: IDNumber);
PROCEDURE Name2Coord(Strg: String32; var Coord: XYCoord);
PROCEDURE GetName(Emp: Empire; Loc: Location; LongFormat: Boolean;
                  VAR Strg: String32);
PROCEDURE GetLocation(Emp: Empire; Strg: String32; var Loc: Location);

PROCEDURE AddIDCell(VAR Root: IDPtr; NewID: IDNumber);
PROCEDURE DisposeIDList(VAR Root: IDPtr);

FUNCTION NextEmpire(Player: Empire): Empire;

IMPLEMENTATION

USES
   Environ;

(* QUAD Procedures ---------------------------------------------------------- *)

FUNCTION GetNebula(Pos: XYCoord): NebulaTypes;
   BEGIN
   IF InGalaxy(Pos.x,Pos.y) THEN
      BEGIN
      WITH Sector[Pos.x]^[Pos.y] DO
         GetNebula:=NebulaTypes(Special mod 16);
      END
   ELSE
      GetNebula:=NoNeb;
   END;  { GetNebula }

PROCEDURE PutNebula(Pos: XYCoord; Neb: NebulaTypes);
   BEGIN
   WITH Sector[Pos.x]^[Pos.y] DO
      Special:=(Special AND $F0) OR Integer(Neb);
   END;  { PutNebula }

PROCEDURE PutMine(Pos: XYCoord; Emp: Empire);
   BEGIN
   WITH Sector[Pos.x]^[Pos.y] DO
      Special:=(Special AND $0F) OR (16*Integer(Emp));
   END;  { PutMine }

FUNCTION EnemyMine(Pos: XYCoord): Empire;
{ EnemyMine:
   Returns the empire that has mined 'Pos' or Indep if no mines. }

   BEGIN
   WITH Sector[Pos.x]^[Pos.y] DO
      EnemyMine:=Empire(Special div 16);
   END;  { EnemyMine }

PROCEDURE GetObject(Pos: XYCoord; VAR ID: IDNumber);
   BEGIN
   WITH Sector[Pos.x]^[Pos.y] DO
      ID:=Obj;
   END;  { GetObject }

PROCEDURE GetFleets(Pos: XYCoord; VAR Flts: FleetSet);
{ GetFleets:
   This procedure returns a set containing all the fleets that are currently in
   the quadrant. }

   VAR
      i: Integer;

   BEGIN
   Flts:=[];
   WITH Universe^ DO      
      FOR i:=1 TO MaxNoOfFleets DO
         IF (i IN SetOfActiveFleets) AND (SameXY(Pos,Fleet[i]^.XY)) THEN
            Flts:=Flts+[i];
   END;  { GetFleets }

(* GET INFO Procedures ------------------------------------------------------ *)

procedure GetResources (ObjID: IDNumber; var Resources: ResourceArray);
{ ----------------------------------------------------------------------------
	Returns the resources of an object.
---------------------------------------------------------------------------- }
	begin
	case ObjID.ObjTyp of
		Pln: begin
			with Universe^.Planet[ObjID.Index] do
				begin
				Move (Defns, Resources[LAM], SizeOf (Defns));
				Move (Ships, Resources[fgt], SizeOf (Ships));
				Move (Cargo, Resources[men], SizeOf (Cargo));
				end;
			end;

		Base: begin
			with Universe^.Starbase[ObjID.Index] do
				begin
				System.Move (Defns, Resources[LAM], SizeOf (Defns));
				System.Move (Ships, Resources[fgt], SizeOf (Ships));
				System.Move (Cargo, Resources[men], SizeOf (Cargo));
				end;
			end;

		Flt: begin
			with Universe^.Fleet[ObjID.Index]^ do
				begin
				FillChar (Resources[LAM], SizeOf (DefnsArray), 0);
				Move (Ships, Resources[fgt], SizeOf (Ships));
				Move (Cargo, Resources[men], SizeOf (Cargo));
				end;
			end;

	else
		FillChar (Resources, SizeOf(Resources), 0);
	end;  { case }
	end;  { GetResources }

procedure PutResources (ObjID: IDNumber; var Resources: ResourceArray);
{ ----------------------------------------------------------------------------
	Sets the resources of an object to the given levels.
---------------------------------------------------------------------------- }
	begin
	case ObjID.ObjTyp of
		Pln: begin
			with Universe^.Planet[ObjID.Index] do
				begin
				Move (Resources[LAM], Defns, SizeOf (Defns));
				Move (Resources[fgt], Ships, SizeOf (Ships));
				Move (Resources[men], Cargo, SizeOf (Cargo));
				end;
			end;

		Base: begin
			with Universe^.Starbase[ObjID.Index] do
				begin
				System.Move (Resources[LAM], Defns, SizeOf (Defns));
				System.Move (Resources[fgt], Ships, SizeOf (Ships));
				System.Move (Resources[men], Cargo, SizeOf (Cargo));
				end;
			end;

		Flt: begin
			with Universe^.Fleet[ObjID.Index]^ do
				begin
				Move (Resources[fgt], Ships, SizeOf (Ships));
				Move (Resources[men], Cargo, SizeOf (Cargo));
				end;
			end;

	else
		FillChar (Resources, SizeOf(Resources), 0);
	end;  { case }
	end;  { PutResources }

PROCEDURE GetCoord(Obj: IDNumber; VAR XY: XYCoord);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
          Pln: XY:=Planet[Index].XY;
         Base: XY:=Starbase[Index].XY;
          Flt: XY:=Fleet[Index]^.XY;
         Gate: XY:=Stargate[Index].XY;
          Con: XY:=Constr[Index].XY;
      ELSE
          XY:=Limbo;
      END;  { case }
   END;  { GetCoord }

PROCEDURE GetShips(Obj: IDNumber; VAR Ships: ShipArray);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
          Pln: Ships:=Planet[Index].Ships;
         Base: Ships:=Starbase[Index].Ships;
          Flt: Ships:=Fleet[Index]^.Ships;
      ELSE
         FillChar(Ships,SizeOf(Ships),0);
      END;  { case }
   END;  { GetShips }

PROCEDURE GetShipsKnown(Player: Empire; Obj: IDNumber; VAR Ships: ShipArray);
   VAR
      ShI: ShipTypes;
      Tech: TechLevel;
      Emp: Empire;

   BEGIN
   FillChar(Ships,SizeOf(Ships),0);
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: BEGIN
            Tech:=GetTech(Obj);
            Emp:=GetStatus(Obj);
            IF (Emp<>Indep) THEN
               Ships:=Planet[Index].Ships
            ELSE
               BEGIN
               FOR ShI:=fgt TO trn DO
                  IF (Planet[Index].Ships[ShI]>0) AND (ShI IN TechDev[Tech]) THEN
                     Ships[ShI]:=Planet[Index].Ships[ShI];
               END;
            END;
         Base: Ships:=Starbase[Index].Ships;
         Flt: Ships:=Fleet[Index]^.Ships;
      END;  { case }
   END;  { GetShipsKnown }

PROCEDURE GetCargo(Obj: IDNumber; VAR Cargo: CargoArray);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Cargo:=Planet[Index].Cargo;
         Base: Cargo:=Starbase[Index].Cargo;
         Flt: Cargo:=Fleet[Index]^.Cargo;
      ELSE
         FillChar(Cargo,SizeOf(Cargo),0);
      END;  { case }
   END;  { GetCargo }

PROCEDURE GetDefns(Obj: IDNumber; VAR Defns: DefnsArray);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Defns:=Planet[Index].Defns;
         Base: Defns:=Starbase[Index].Defns;
      ELSE
         FillChar(Defns,SizeOf(Defns),0);
      END;  { case }
   END;  { GetDefns }

PROCEDURE GetIndus(Obj: IDNumber; VAR Indus: IndusArray);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Indus:=Planet[Index].Indus;
         Base: Indus:=Starbase[Index].Indus;
      ELSE
         FillChar(Indus,SizeOf(Indus),0);
      END;  { case }
   END;  { GetIndus }

FUNCTION GetTrillum(Obj: IDNumber): Resources;
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: GetTrillum:=Planet[Index].Cargo[tri];
         Base: GetTrillum:=Starbase[Index].Cargo[tri];
         Flt: GetTrillum:=Fleet[Index]^.Cargo[tri];
      ELSE
         GetTrillum:=0;
      END;  { case }
   END;  { GetTrillum }

FUNCTION TroopStrength(Obj: IDNumber): Word;
{ TroopStrength: ---------------------------------------------------------------
   Returns the strength of troops on the object. Strength is equal to:
                 (no. of men)+2 (no. of ninja)
------------------------------------------------------------------------------ }
   VAR
      Cr: CargoArray;

   BEGIN
   GetCargo(Obj,Cr);
   TroopStrength:=Cr[men]+2*Cr[nnj];
   END;  { GetTroopSize }

FUNCTION GetClass(Obj: IDNumber): WorldClass;
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: GetClass:=Planet[Index].Cls;
         Base: GetClass:=ArtCls;
      ELSE
         GetClass:=ArtCls;
      END;  { case }
   END;  { GetClass }

FUNCTION GetTech(Obj: IDNumber): TechLevel;
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: GetTech:=Planet[Index].Tech;
         Base: GetTech:=Starbase[Index].Tech;
      ELSE
         GetTech:=PrimitLvl;
      END;  { case }
   END;  { GetTech }

FUNCTION GetPopulation(Obj: IDNumber): Population;
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: GetPopulation:=Planet[Index].Pop;
         Base: GetPopulation:=Starbase[Index].Pop;
      ELSE
         GetPopulation:=0;
      END;  { case }
   END;  { GetPopulation }

FUNCTION GetEfficiency(Obj: IDNumber): Index;
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: GetEfficiency:=Planet[Index].Eff;
         Base: GetEfficiency:=Starbase[Index].Eff;
      ELSE
         GetEfficiency:=0;
      END;  { case }
   END;  { GetEfficiency }

FUNCTION GetStatus(Obj: IDNumber): Empire;
   BEGIN
   WITH Universe^,Obj do
      CASE ObjTyp OF
         Pln: GetStatus:=Planet[Index].Emp;
         Base: GetStatus:=Starbase[Index].Emp;
         Flt: GetStatus:=Fleet[Index]^.Emp;
         Gate: GetStatus:=Stargate[Index].Emp;
         Con: GetStatus:=Constr[Index].Emp;
      ELSE
         GetStatus:=Indep;
      END;  { case }
   END;  { GetStatus }

FUNCTION GetType(Obj: IDNumber): WorldTypes;
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln  : GetType:=Planet[Index].Typ;
         Base : GetType:=Starbase[Index].Typ;
      ELSE
         GetType:=AgrTyp;
      END; { case }
   END;  { GetType }

FUNCTION GetTerraformTarget(Obj: IDNumber): WorldClass;
   BEGIN
      WITH Universe^,Obj DO
        GetTerraformTarget:=Planet[Index].TerraformTarget
   END;

FUNCTION TrillumReserves(Obj: IDNumber): Word;
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln  : TrillumReserves:=Planet[Index].TriReserve;
         Base : TrillumReserves:=MaxResources;
      ELSE
         TrillumReserves:=0;
         END;  { case }
   END;  { TrillumReserves }

PROCEDURE PutTrillumReserves(Obj: IDNumber; NewRes: Word);
   BEGIN
   WITH Universe^,Obj DO
      IF ObjTyp=Pln THEN
         Planet[Index].TriReserve:=NewRes;
   END;  { PutTrillumReserves }

FUNCTION GetISSP(Obj: IDNumber; Ind: IndusTypes): Byte;
{ GetISSP: ---------------------------------------------------------------------
   The ISSP (Industrial Self-Sufficiency Percentage) for a given industry is
   percent of self-sufficiency that the industry is set to.  For example, if
   the ISSP of the mining industry is set to .5, that means that the world
   only produces 50% of the metals that it needs.  This returns an index into
   the array ISSP.
------------------------------------------------------------------------------ }

   BEGIN
   IF Ind IN [BioInd,SYGInd..SYTInd] THEN
      GetISSP:=6
   ELSE BEGIN
      WITH Universe^,Obj DO
         BEGIN
         CASE ObjTyp OF
            Pln : BEGIN
               WITH Planet[Index] DO
                  CASE Ind OF
                     CheInd: GetISSP:=Lo(ImpExp) MOD 16;
                     MinInd: GetISSP:=Lo(ImpExp) DIV 16;
                     SupInd: GetISSP:=Hi(ImpExp) MOD 16;
                     TriInd: GetISSP:=Hi(ImpExp) DIV 16;
                  END;  { case }
               END;
            Base : GetISSP:=0;  { Always lowest ISSP }
         END;  { case }
         END;  { with scope }
      END;
   END;  { GetISSP }

FUNCTION GetRevIndex(Obj: IDNumber): Index;
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: GetRevIndex:=Planet[Index].RevIndex;
         Base: GetRevIndex:=Starbase[Index].RevIndex;
      ELSE
         GetRevIndex:=0;
      END;  { case }
   END;  { GetRevIndex }

PROCEDURE GetSpecial(Obj: IDNumber; VAR Setting: SetOfSpecialConditions);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
          Pln: Setting:=Planet[Index].Special;
         Base: Setting:=Starbase[Index].Special;
      ELSE
         Setting:=[];
      END;  { case }
   END;  { GetSpecial }

FUNCTION Scouted(Emp: Empire; Obj: IDNumber): Boolean;
{ Scouted:
   Returns true if ID has been scouted by Emp. }

   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Scouted:=(Emp IN Planet[Index].ScoutedBy);
         Base: Scouted:=(Emp IN Starbase[Index].ScoutedBy);
         Flt: Scouted:=(Emp IN Fleet[Index]^.ScoutedBy);
         Gate: Scouted:=(Emp IN Stargate[Index].ScoutedBy);
         Con: Scouted:=(Emp IN Constr[Index].ScoutedBy);
      ELSE
         Scouted:=False;
      END;  { case }
   END;  { Scouted }

FUNCTION Known(Emp: Empire; Obj: IDNumber): Boolean;
{ Known:
   Returns true if ID is known by Emp. }

   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Known:=(Emp IN Planet[Index].KnownBy);
         Base: Known:=(Emp IN Starbase[Index].KnownBy);
         Gate: Known:=(Emp IN Stargate[Index].KnownBy);
         Con: Known:=(Emp IN Constr[Index].KnownBy);
         Flt: Known:=(Emp IN Fleet[Index]^.KnownBy);
      ELSE
         Known:=False;
      END;  { case }
   END;  { Known }

FUNCTION GetWarpLinkFreq(Emp: Empire; Obj: IDNumber): Freq;

   BEGIN
   WITH Universe^,Obj DO
      GetWarpLinkFreq:=Stargate[Index].WLF[Emp];
   END;  { GetWarpLinkFreq }

PROCEDURE SetWarpLinkFreq(Emp: Empire; Obj: IDNumber; Frequency: Freq);

   BEGIN
   WITH Universe^,Obj DO
      Stargate[Index].WLF[Emp]:=Frequency;
   END;  { SetWarpLinkFreq }


(* SET INFO Procedures ------------------------------------------------------ *)

PROCEDURE PutShips(Obj: IDNumber; VAR Ships: ShipArray);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Planet[Index].Ships:=Ships;
         Base: Starbase[Index].Ships:=Ships;
         Flt: Fleet[Index]^.Ships:=Ships;
      END;  { case }
   END;  { PutShips }

PROCEDURE PutCargo(Obj: IDNumber; VAR Cargo: CargoArray);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Planet[Index].Cargo:=Cargo;
         Base: Starbase[Index].Cargo:=Cargo;
         Flt: Fleet[Index]^.Cargo:=Cargo;
      END;  { case }
   END;  { PutCargo }

PROCEDURE PutDefns(Obj: IDNumber; VAR Defns: DefnsArray);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Planet[Index].Defns:=Defns;
         Base: Starbase[Index].Defns:=Defns;
      END;  { case }
   END;  { PutDefns }

PROCEDURE PutIndus(Obj: IDNumber; VAR Indus: IndusArray);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Planet[Index].Indus:=Indus;
         Base: Starbase[Index].Indus:=Indus;
      END;  { case }
   END;  { PutIndus }

PROCEDURE InitializeISSP(Obj: IDNumber);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Planet[Index].ImpExp:=DefaultISSP;
      END;  { case }
   END;  { InitializeISSP }

PROCEDURE SetISSP(Obj: IDNumber; Ind: IndusTypes; ISSPInd: Byte);
   BEGIN
   IF Ind IN [BioInd,SYGInd..SYTInd] THEN
      BEGIN END
   ELSE BEGIN
      CASE Obj.ObjTyp OF
         Pln : WITH Universe^.Planet[Obj.Index] DO
            BEGIN
            CASE Ind OF
               CheInd: ImpExp:=(ImpExp AND $FFF0) + ISSPInd;
               MinInd: ImpExp:=(ImpExp AND $FF0F) + (ISSPInd * $10);
               SupInd: ImpExp:=(ImpExp AND $F0FF) + (ISSPInd * $100);
               TriInd: ImpExp:=(ImpExp AND $0FFF) + (ISSPInd * $1000);
            END;  { case }
            END;
      END;  { case }
      END;
   END;  { SetISSP }

PROCEDURE PutTrillum(Obj: IDNumber; T: Resources);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln  : Planet[Index].Cargo[tri]:=T;
         Base : Starbase[Index].Cargo[tri]:=T;
         Flt  : Fleet[Index]^.Cargo[tri]:=T;
      END;  { case }
   END;  { PutTrillum }

PROCEDURE SetClass(Obj: IDNumber; Class: WorldClass);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Planet[Index].Cls:=Class;
      END;  { case }
   END;  { SetClass }

PROCEDURE SetTech(Obj: IDNumber; Tech: TechLevel);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Planet[Index].Tech:=Tech;
         Base: Starbase[Index].Tech:=Tech;
      END;  { case }
   END;  { SetTech }

PROCEDURE SetPopulation(Obj: IDNumber; Pop: Population);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Planet[Index].Pop:=Pop;
         Base: Starbase[Index].Pop:=Pop;
      END;  { case }
   END;  { SetPopulation }

PROCEDURE SetEfficiency(Obj: IDNumber; Eff: Index);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Planet[Index].Eff:=Eff;
         Base: Starbase[Index].Eff:=Eff;
      END;  { case }
   END;  { SetEfficiency }

PROCEDURE SetStatus(Obj: IDNumber; Emp: Empire);
   VAR
      OldEmp: Empire;

   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: BEGIN
            OldEmp:=Planet[Index].Emp;
            Planet[Index].Emp:=Emp;
            SetOfPlanetsOf[OldEmp]:=SetOfPlanetsOf[OldEmp]-[Index];
            SetOfPlanetsOf[Emp]:=SetOfPlanetsOf[Emp]+[Index];
            END;
         Base: BEGIN
            OldEmp:=Starbase[Index].Emp;
            Starbase[Index].Emp:=Emp;
            SetOfStarbasesOf[OldEmp]:=SetOfStarbasesOf[OldEmp]-[Index];
            SetOfStarbasesOf[Emp]:=SetOfStarbasesOf[Emp]+[Index];
            END;
         Con: BEGIN
            OldEmp:=Constr[Index].Emp;
            Constr[Index].Emp:=Emp;
            SetOfConstructionSitesOf[OldEmp]:=SetOfConstructionSitesOf[OldEmp]-[Index];
            SetOfConstructionSitesOf[Emp]:=SetOfConstructionSitesOf[Emp]+[Index];
            END;
         END;  { case }
   END;  { SetStatus }

PROCEDURE SetType(Obj: IDNumber; Typ: WorldTypes);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Planet[Index].Typ:=Typ;
         Base: Starbase[Index].Typ:=Typ;
         END;
   END;  { SetType }

PROCEDURE ChangeRevIndex(Obj: IDNumber; Chg: Integer);
   VAR
      Rev: Index;

   BEGIN
   WITH Universe^,Obj DO
      BEGIN
      CASE ObjTyp OF
         Pln: Rev:=Planet[Index].RevIndex;
         Base: Rev:=Starbase[Index].RevIndex
      ELSE
         Rev:=0
      END;  { case }

      IF Rev+Chg>100 THEN
         Rev:=100
      ELSE IF Rev+Chg<0 THEN
         Rev:=0
      ELSE
         Rev:=Rev+Chg;

      CASE ObjTyp of
         Pln: Planet[Index].RevIndex:=Rev;
         Base: Starbase[Index].RevIndex:=Rev;
      END;  { case }
      END;  { with scope }
   END;  { ChangeRevIndex }

PROCEDURE SetSpecial(Obj: IDNumber; NewSetting: SetOfSpecialConditions);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: Planet[Index].Special:=NewSetting;
         Base: Starbase[Index].Special:=NewSetting;
      END;  { case }
   END;  { SetSpecial }

PROCEDURE ScoutObject(Emp: Empire; Obj: IDNumber);
   BEGIN
   WITH Universe^,Obj DO
      CASE ObjTyp OF
         Pln: BEGIN
            Planet[Index].ScoutedBy:=Planet[Index].ScoutedBy+[Emp];
            Planet[Index].KnownBy:=Planet[Index].KnownBy+[Emp];
            END;
         Base: BEGIN
            Starbase[Index].ScoutedBy:=Starbase[Index].ScoutedBy+[Emp];
            Starbase[Index].KnownBy:=Starbase[Index].KnownBy+[Emp];
            END;
         Gate: BEGIN
            Stargate[Index].ScoutedBy:=Stargate[Index].ScoutedBy+[Emp];
            Stargate[Index].KnownBy:=Stargate[Index].KnownBy+[Emp];
            END;
         Con: BEGIN
            Constr[Index].ScoutedBy:=Constr[Index].ScoutedBy+[Emp];
            Constr[Index].KnownBy:=Constr[Index].KnownBy+[Emp];
            END;
      END;  { case }
   END;  { ScoutObject }

PROCEDURE SetTerraformTarget(Obj: IDNumber; TargetClass: WorldClass);
   BEGIN
      WITH Universe^,Obj DO
        Planet[Index].TerraformTarget:=TargetClass;
   END;



(* FLEET Procedures --------------------------------------------------------- *)

PROCEDURE SetNPEDataIndex(FltID: IDNumber; Index: Word);
   BEGIN
   Universe^.Fleet[FltID.Index]^.NPEDataIndex:=Index;
   END;  { SetNPEDataIndex }

FUNCTION NPEDataIndex(FltID: IDNumber): Word;
   BEGIN
   NPEDataIndex:=Universe^.Fleet[FltID.Index]^.NPEDataIndex;
   END;  { NPEDataIndex }

FUNCTION TypeOfFleet(FltID: IDNumber): FleetTypes;
{ TypeOfFleet: -----------------------------------------------------------------
   This function returns the type of fleet.  I.e. if there are only
   jumpships, it returns JumpFleet, etc.
------------------------------------------------------------------------------ }

   BEGIN
   IF FltID.ObjTyp=Flt THEN
      BEGIN
      WITH Universe^.Fleet[FltID.Index]^ DO
         BEGIN
         IF Ships[ssp]+Ships[pen]+Ships[jmp]+Ships[fgt]+Ships[jtn]+Ships[trn] = 0 THEN
            TypeOfFleet:=HKFleet
         ELSE IF Ships[ssp]+Ships[pen]+Ships[fgt]+Ships[trn] = 0 THEN
            TypeOfFleet:=JumpFleet
         ELSE IF Ships[ssp]+Ships[jmp]+Ships[jtn]+Ships[trn]+Ships[fgt] = 0 THEN
            TypeOFFleet:=Penetrator
         ELSE IF Ships[ssp]+Ships[fgt]+Ships[trn] = 0 THEN
            TypeOfFleet:=AdvWrpFleet
         ELSE
            TypeOfFleet:=Standard;
         END { with scope }
      END
   ELSE
      TypeOfFleet:=Standard;
   END;  { TypeOfFleet }

PROCEDURE SetFleetStatus(FltID: IDNumber;  NewStatus: FleetStatus);
   BEGIN
   WITH Universe^,FltID DO
      CASE ObjTyp OF
         Flt: Fleet[Index]^.Status:=NewStatus;
         Base: Starbase[Index].Status:=NewStatus;
      END;  { case }
   END; { SetFleetStatus }

FUNCTION GetFleetStatus(FltID: IDNumber): FleetStatus;
   BEGIN
   WITH Universe^,FltID DO
      CASE ObjTyp OF
         Flt: GetFleetStatus:=Fleet[Index]^.Status;
         Base: GetFleetStatus:=Starbase[Index].Status;
      END;  { case }
   END;  { FleetStatus }

FUNCTION GetFleetFuel(FltID: IDNumber): Real;
{ GetFleetFuel:
   Returns the amount of fuel left in FltID. }

   BEGIN
   IF FltID.ObjTyp=Flt THEN
      BEGIN
      WITH Universe^.Fleet[FltID.Index]^ DO
         GetFleetFuel:=(1.0*FuelHigh*MaxInt) + Fuel;
      END
   ELSE
      GetFleetFuel:=0;
   END;  { GetFleetFuel }

PROCEDURE SetFleetFuel(FltID: IDNumber; FuelLeft: Real);
{ SetFleetFuel:
   Sets the amount of fuel in the fleet }

   BEGIN
   IF FltID.ObjTyp=Flt THEN
      BEGIN
      WITH Universe^.Fleet[FltID.Index]^ DO
         BEGIN
         FuelHigh:=Trunc(FuelLeft/MaxInt);
         Fuel:=Trunc(FuelLeft-(1.0*FuelHigh*MaxInt));
         END;
      END;
   END;  { SetFleetFuel }

(* STARBASE Procedures ------------------------------------------------------ *)

FUNCTION GetBaseType(BaseID: IDNumber): StarbaseTypes;
   BEGIN
   IF BaseID.ObjTyp=Base THEN
      GetBaseType:=Universe^.Starbase[BaseID.Index].STyp
   ELSE
      GetBaseType:=cmm;
   END;  { GetBaseType }

(* STARGATE Procedures ------------------------------------------------------ *)

FUNCTION GetGateType(GateID: IDNumber): StargateTypes;
   BEGIN
   IF GateID.ObjTyp=Gate THEN
      GetGateType:=Universe^.Stargate[GateID.Index].GTyp
   ELSE
      GetGateType:=gte;
   END;  { GetGateType }

(* CONSTRUCTION Procedures -------------------------------------------------- *)

FUNCTION GetConstrType(ConID: IDNumber): ConstrTypes;
   BEGIN
   IF ConID.ObjTyp=Con THEN
      GetConstrType:=Universe^.Constr[ConID.Index].CTyp
   ELSE
      GetConstrType:=SRM;
   END;  { GetConstrType }

FUNCTION GetConstrTimeLeft(ConID: IDNumber): Word;
   BEGIN
   IF ConID.ObjTyp=Con THEN
      GetConstrTimeLeft:=Universe^.Constr[ConID.Index].TimeToCompletion
   ELSE
      GetConstrTimeLeft:=0;
   END;  { GetConstrTimeLeft }

(* EMPIRE Procedures -------------------------------------------------------- *)

PROCEDURE GetProbe(Player: Empire; VAR PNum: Word);
{ GetProbe: --------------------------------------------------------------------
   Returns the number of the next available probe. If there are no more probes
   left, it returns 0. 
------------------------------------------------------------------------------ }
   BEGIN
   PNum:=NoOfProbesPerEmpire;
   WITH Universe^.EmpireData[Player] DO
      WHILE (PNum>0) AND (Probe[PNum].Status<>PReady) DO
         Dec(PNum);
   END;  { GetProbe }

PROCEDURE LaunchProbe(Player: Empire; PNum: Byte; Loc: XYCoord);
{ LaunchProbe: -----------------------------------------------------------------
   Launches probe number PNum to Loc. PNum must be a legal number obtained from
   GetProbe.
------------------------------------------------------------------------------ }
   BEGIN
   WITH Universe^.EmpireData[Player].Probe[PNum] DO
      BEGIN
      Dest:=Loc;
      Status:=PInTrans;
      END;
   END;  { LaunchProbe }

FUNCTION GetTimeLeft(Player: Empire): Integer;
   BEGIN
   GetTimeLeft:=Universe^.EmpireData[Player].TimeLeft;
   END;  { GetTimeLeft }

PROCEDURE SetTimeLeft(Player: Empire; Time: Integer);
   BEGIN
   Universe^.EmpireData[Player].TimeLeft:=Time;
   END;  { SetTimeLeft }

FUNCTION CentralizedCapital(Emp: Empire): Boolean;
   BEGIN
   CentralizedCapital:=(CentralEMD IN Universe^.EmpireData[Emp].Modifiers);
   END;  { CentrailizedCapital }

FUNCTION EmpireActive(Emp: Empire): Boolean;
   BEGIN
   EmpireActive:=Universe^.EmpireData[Emp].InUse;
   END;  { EmpireActive }

FUNCTION EmpirePlayer(Emp: Empire): Boolean;
   BEGIN
   EmpirePlayer:=Universe^.EmpireData[Emp].IsAPlayer;
   END;  { EmpirePlayer }

FUNCTION Empress(Emp: Empire): Boolean;
   BEGIN
   Empress:=Universe^.EmpireData[Emp].IsAnEmpress;
   END;  { Empress }

FUNCTION EmpireAge(Emp: Empire): Word;
   BEGIN
   EmpireAge:=Year-Universe^.EmpireData[Emp].Founding;
   END;  { EmpireAge }

PROCEDURE CreateEmpire(Emp: Empire;  Player,Empress: Boolean;
                       Name: String32;
                       Password: String8;  CapID: IDNumber;
                       Tech: TechLevel; TechSet: TechnologySet;
                       Restlessness: Integer;
                       NewModifiers: SetOfEmpireModifiers;
                       YearFounded: Word);
{ CreateEmpire: 
   Given the necessary information, this procedures sets up the data 
   in EmpireData. }

   BEGIN
   WITH Universe^.EmpireData[Emp] DO
      BEGIN
      InUse:=True;
      IsAPlayer:=Player;
      IsAnEmpress:=Empress;
      EmpireName:=Name;
      Pass:=Password;

      TimeLeft:=1500;
      Capital:=CapID;
      DefenseSettings:=InitDefenseRecord;

      TechnologyLevel:=Tech;
      Technology:=TechSet;

      { Every year the revolution index of all worlds is increased by this
        amount. }
      RevFactor:=Restlessness;

      Modifiers:=NewModifiers;
      Founding:=YearFounded;
      END;  { with scope }
   END;  { CreateEmpire }

FUNCTION EmpireName(Emp: Empire): String32;
   BEGIN
   EmpireName:=Universe^.EmpireData[Emp].EmpireName;
   END;  { EmpireName }
 
FUNCTION MyLord(Emp: Empire): String32;
   BEGIN
   IF Universe^.EmpireData[Emp].IsAnEmpress THEN
      BEGIN
      CASE Rnd(1,4) OF
         1: MyLord:='My Lady';
         2: MyLord:='Your Highness';
         3: MyLord:='Your Excellency';
         4: MyLord:='My Empress';
      END;  { case }
      END
   ELSE
      BEGIN
      CASE Rnd(1,6) OF
         1: MyLord:='My Lord';
         2: MyLord:='Your Highness';
         3: MyLord:='Your Majesty';
         4: MyLord:='My Liege';
         5: MyLord:='Your Excellency';
         6: MyLord:='Sir';
      END;  { case }
      END;
   END;  { MyLord }

PROCEDURE GetDefenseSettings{Emp: Empire; VAR Def: DefenseRecord};
{ GetDefenseSettings: }
   BEGIN
   Def:=Universe^.EmpireData[Emp].DefenseSettings;
   END;  { GetDefenseSettings }

PROCEDURE SetDefenseSettings(Emp: Empire; VAR Def: DefenseRecord);
{ GetDefenseSettings: }
   BEGIN
   Universe^.EmpireData[Emp].DefenseSettings:=Def;
   END;  { GetDefenseSettings }

PROCEDURE GetEmpireTechnology(Emp: Empire; VAR Tech: TechLevel;
                              VAR TechSet: TechnologySet);
{ GetEmpireTechnology: }

   BEGIN
   WITH Universe^.EmpireData[Emp] DO
      BEGIN
      Tech:=TechnologyLevel;
      TechSet:=Technology;
      END;  { with scope }
   END;  { GetEmpireTechnology }

PROCEDURE SetEmpireTechnology(Emp: Empire; Tech: TechLevel;
                              TechSet: TechnologySet);
{ SetEmpireTechnology: 
   This procedure will set the TechLevel and TechnologySet of the given
   empire. }

   BEGIN
   WITH Universe^.EmpireData[Emp] DO
      BEGIN
      TechnologyLevel:=Tech;
      Technology:=TechSet;
      END;  { with scope }
   END;  { SetEmpireTechnology }

PROCEDURE ChangeTotalRevIndex(Emp: Empire; Inc: Integer);
   BEGIN
   WITH Universe^.EmpireData[Emp] DO
      TotalRevIndex:=TotalRevIndex+Inc;
   END;  { ChangeTotalRevIndex }

PROCEDURE SetTotalRevIndex(Emp: Empire; NewIndex: Integer);
   BEGIN
   WITH Universe^.EmpireData[Emp] DO
      TotalRevIndex:=NewIndex;
   END;  { SetTotalRevIndex }

FUNCTION TotalRevIndex(Emp: Empire): Integer;
   BEGIN
   IF Emp<>Indep THEN
      BEGIN
      WITH Universe^ DO
         TotalRevIndex:=EmpireData[Emp].TotalRevIndex+EmpireData[Emp].RevFactor;
      END
   ELSE
      TotalRevIndex:=0;
   END;  { TotalRevIndex }

PROCEDURE GetCapital(Emp: Empire; VAR CapID: IDNumber);
   BEGIN
   CapID:=Universe^.EmpireData[Emp].Capital;
   END;  { GetCapital }

PROCEDURE SetCapital(Emp: Empire; CapID: IDNumber);
   BEGIN
   Universe^.EmpireData[Emp].Capital:=CapID;
   END;  { SetCapital }

FUNCTION AbsoluteX(X: Integer): Integer;
{ AbsoluteX:
   Converts relativeX empire coordinates to absoluteX galaxy coordinates. }
   VAR
      CapXY: XYCoord;

   BEGIN
   GetCoord(Universe^.EmpireData[Player].Capital,CapXY);
   AbsoluteX:=X+CapXY.x;
   END;  { AbsoluteX }

FUNCTION AbsoluteY(Y: Integer): Integer;
{ AbsoluteY:
   Converts relativeY empire coordinates to absoulteY galaxy coordinates. }
   VAR
      CapXY: XYCoord;

   BEGIN
   GetCoord(Universe^.EmpireData[Player].Capital,CapXY);
   AbsoluteY:=CapXY.y-Y;
   END;  { AbsoluteY }

FUNCTION RelativeX{X: Coordinate): Integer};
{ RelativeX:
   Converts absoluteX to relativeX. }
   VAR
      CapXY: XYCoord;

   BEGIN
   GetCoord(Universe^.EmpireData[Player].Capital,CapXY);
   RelativeX:=X-CapXY.x;
   END;  { RelativeX }

FUNCTION RelativeY{Y: Coordinate): Integer};
{ RelativeY:
   Converts absoluteY to relativeY. }
   VAR
      CapXY: XYCoord;

   BEGIN
   GetCoord(Universe^.EmpireData[Player].Capital,CapXY);
   RelativeY:=CapXY.y-Y;
   END;  { RelativeY }

(* NAME Procedures ---------------------------------------------------------- *)

PROCEDURE GetNewName(Player: Empire; VAR NewSlot: NameRecordPtr);
{ GetNewName: ------------------------------------------------------------------
   This procedure will try to add a name record at the end of the list.  If 
   there is no room then it will return Nil, else it will return a pointer
   to the new name.  It will update the list and the pointers in 
   EmpireData.
------------------------------------------------------------------------------ }

   BEGIN
   IF MaxAvail>SizeOf(NewSlot) THEN
      { ASSERT: enough memory }
      BEGIN
      New(NewSlot);

      WITH Universe^.EmpireData[Player] DO
         BEGIN
         IF Names=Nil THEN
            Names:=NewSlot
         ELSE
            LastName^.Next:=NewSlot;

         LastName:=NewSlot;
         END;  { with scope }

      NewSlot^.Next:=Nil;
      END
   ELSE
      NewSlot:=Nil;
   END;  { GetNewName }

PROCEDURE GetDefinedName(Emp: Empire; NamePtr: NameRecordPtr; 
                         VAR DefName: String16; VAR DefCoord: Location);
   BEGIN
   DefName:=NamePtr^.Name;
   DefCoord:=NamePtr^.Coord;
   END;  { GetDefinedName }

PROCEDURE DefineName(Emp: Empire; NamePtr: NameRecordPtr;
                     DefName: String16; DefCoord: Location);
   BEGIN
   NamePtr^.Name:=DefName;
   NamePtr^.Coord:=DefCoord;
   END;  { DefineName }

PROCEDURE AddName(Player: Empire; VAR Loc: Location;
                  NameVar: String8; VAR Error: Boolean);
{ AddName:
   This procedure will attemp to add a new name definition to the Player's
   list of names.  If the location is already named it will simply 
   change its name, else it will try to create a new name. If all is ok, 
   then Error is False. }

   var
      NewSlot: NameRecordPtr;

   { AddName: MAIN PROCEDURE }
   BEGIN
   Error:=False;

   IF Loc.ID.ObjTyp IN [Con,Pln,Gate] THEN
      GetCoord(Loc.ID,Loc.XY);

   Location2Index(Player,Loc,NewSlot);
   IF NewSlot=Nil THEN
      GetNewName(Player,NewSlot);

   IF NewSlot<>Nil THEN
      DefineName(Player,NewSlot,NameVar,Loc)

   ELSE

      { no empty slot found }
      Error:=True;
   END;  { AddName }

PROCEDURE NAMDelete(Emp: Empire; NameToDel,PrevName: NameRecordPtr);
   BEGIN
   WITH Universe^.EmpireData[Emp] DO
      BEGIN
      IF PrevName=Nil THEN
         { ASSERT: delete first name in list }
         Names:=NameToDel^.Next
      ELSE
         PrevName^.Next:=NameToDel^.Next;

      IF NameToDel^.Next=Nil THEN
         { ASSERT: deleting last name in the list }
         LastName:=PrevName;
      END;  { with scope }

   Dispose(NameToDel);
   END;  { NAMDelete }

PROCEDURE DeleteName(Player: Empire; NameToDelete: String32);
   VAR
      NextName,PrevName: NameRecordPtr;
      Found: Boolean;
      NameStr: String16;

   BEGIN
   WITH Universe^.EmpireData[Player] DO
      BEGIN
      NextName:=Names;
      PrevName:=Nil;
      Found:=False;

      AllUpcase(NameToDelete);

      WHILE (NextName<>Nil) AND (Found=False) DO
         BEGIN
         NameStr:=NextName^.Name;
         AllUpcase(NameStr);

         IF NameStr=NameToDelete THEN
            BEGIN
            Found:=True;
            END
         ELSE
            BEGIN
            PrevName:=NextName;
            NextName:=NextName^.Next;
            END;
         END;  { while }
      END;  { with scope }

   IF NextName<>Nil THEN
      { ASSERT: name to delete found }
      NAMDelete(Player,NextName,PrevName);
   END;  { DeleteName }

PROCEDURE DeleteAllFleetDestNames(Emp: Empire);
   VAR
      NameToDel,NextName,PrevName: NameRecordPtr;

   BEGIN
   WITH Universe^.EmpireData[Emp] DO
      BEGIN
      NameToDel:=Names;
      PrevName:=Nil;

      WHILE NameToDel<>Nil DO
         BEGIN
         NextName:=NameToDel^.Next;
         IF NameToDel^.Coord.ID.ObjTyp=DestFlt THEN
            NAMDelete(Emp,NameToDel,PrevName)
         ELSE
            PrevName:=NameToDel;
         NameToDel:=NextName;
         END;
      END;  { with scope }
   END;  { DeleteAllFleetDestNames }

PROCEDURE Location2Index(Emp: Empire; Loc: Location; 
                         VAR NamePtr: NameRecordPtr);
{ Location2Index: --------------------------------------------------------------
   This procedure will search through the list of current names.  If it finds a
   name defined for 'Loc' then it will return a pointer to the name.  Otherwise 
   it will return Nil.
------------------------------------------------------------------------------ }

   BEGIN
   NamePtr:=Universe^.EmpireData[Emp].Names;

   WHILE (NamePtr<>Nil) AND (NOT SameLocation(Loc,NamePtr^.Coord)) DO
      NamePtr:=NamePtr^.Next;
   END;  { Location2Index }

PROCEDURE Name2Index{Emp: Empire; NameToFind: String16;
                     VAR NamePtr: NameRecordPtr};
{ Name2Index: ------------------------------------------------------------------
   This procedure will search through the list of current names.  If it finds an
   entry for 'NameToFind' then it will return a pointer to the name.  Otherwise
   it will return Nil.
------------------------------------------------------------------------------ }

   FUNCTION NameMatches(NameToMatch: String32): Boolean;
      BEGIN
      AllUpcase(NameToMatch);
      IF NameToFind=NameToMatch THEN
         NameMatches:=True
      ELSE
         NameMatches:=False;
      END;

   BEGIN
   AllUpcase(NameToFind);
   NamePtr:=Universe^.EmpireData[Emp].Names;

   WHILE (NamePtr<>Nil) AND (NOT NameMatches(NamePtr^.Name)) DO
      NamePtr:=NamePtr^.Next;
   END;  { Name2Index }

PROCEDURE GetFleetName(Emp: Empire; FltID: IDNumber; VAR Strg: String32);
{ GetFleetName: ----------------------------------------------------------------
   Given the IDNumber of a fleet, this procedure will return its name.  If it is
   a player fleet, the name returned will be 'Fleet#' where # is the number of
   the fleet from 1-15.  If the fleet is an enemy fleet, then the name returned
   will be 'Enemy#' where # is the number from 1-120.
------------------------------------------------------------------------------ }

   VAR
      FirstFleet,FleetNumber: Word;

   BEGIN
   IF (FltID.ObjTyp=DestFlt) OR (FltID.Index IN SetOfFleetsOf[Emp]) THEN
      BEGIN
      Str(FltID.Index,Strg);
      Strg:='Fleet'+Strg;
      END
   ELSE
      BEGIN
      Str(FltID.Index,Strg);
      Strg:='Enemy'+Strg;
      END;
   END;  { GetFleetName }

PROCEDURE GetCoordName(Coord: XYCoord; VAR Strg: String32);
{ GetCoordName: ----------------------------------------------------------------
   Given a location in space, this routine will return the co-ordinates of the
   place in string form adjusted relative to the player.  E.g.  if the location
   of the player capital is the input, the returned string will be '0,0'  No
   extra spaces are added.
------------------------------------------------------------------------------ }

   VAR
      XStr,YStr: String32;

   BEGIN
   WITH Coord DO
      BEGIN      
      Str(RelativeX(x),XStr);
      Str(RelativeY(y),YStr);
      END;  { with scope }
   Strg:=XStr+','+YStr;
   END;  { GetCoordName }

PROCEDURE Name2Fleet{Emp: Empire; Strg: String32;
                     VAR ID: IDNumber};
{ Name2Fleet: ------------------------------------------------------------------
   If Strg is a fleet name, it will return the ID of the fleet in ID.
   If Strg is 'FLEET' then it will return the first fleet that belongs to
   the empire that is not in use.  Otherwise it will return EmptyQuadrant.

   Adam Luker fixed the Enemy240 bug here, by changing "FltIndex<MaxNoOfFleets"
                                                    to "FltIndex<=MaxNoOfFleets"
------------------------------------------------------------------------------ }

   VAR
      Error,FltIndex: Integer;

   BEGIN
   AllUpcase(Strg);
   ID:=EmptyQuadrant;

   IF ((Copy(Strg,1,5)='ENEMY') OR (Copy(Strg,1,5)='FLEET')) 
      AND (Length(Strg)>5) THEN
      BEGIN
      Val(Copy(Strg,6,16),FltIndex,Error);
      IF (Error=0) AND (FltIndex>0) AND (FltIndex<=MaxNoOfFleets) THEN
         BEGIN
         ID.ObjTyp:=Flt;
         ID.Index:=FltIndex;
         END;
      END;
   END;  { Name2Fleet }

PROCEDURE Name2Coord(Strg: String32; VAR Coord: XYCoord);
{ Name2Coord: ------------------------------------------------------------------
   Translates a string into x,y coordinates.  If it is not a coordinate,
   it returns Limbo in Coord.  This procedure should ONLY be called from within 
   PlayerTakesTurn.
------------------------------------------------------------------------------ }

   VAR
      X,Y,Error,Del: Integer;
      XStr,YStr: String32;

   BEGIN
   Coord:=Limbo;

   Del:=Pos(',',Strg);
   IF Del>0 THEN
      BEGIN
      XStr:=Copy(Strg,1,Del-1);
      YStr:=Copy(Strg,Del+1,16);
      Val(XStr,X,Error);
      IF Error=0 THEN
         BEGIN
         Val(YStr,Y,Error);
         IF Error=0 THEN
            BEGIN
            X:=AbsoluteX(X);
            Y:=AbsoluteY(Y);
            IF InGalaxy(X,Y) THEN
               BEGIN
               Coord.x:=X;
               Coord.y:=Y;
               END;  { if }
            END;  { if }
         END;  { if }
      END;  { if }
   END;  { Name2Coord }

PROCEDURE GetName(Emp: Empire; Loc: Location; LongFormat: Boolean;
                  VAR Strg: String32);
{ GetName: ---------------------------------------------------------------------
   This procedure will return the name associated with Loc.  If there is no
   name, it will return the coordinates in a string.  Loc is a fleet, it
   will either return a name (if it has been named) or its fleet ID name.
   if the object is not named and is not a fleet, and LongFormat is true, then
   this procedure will return the name of the object instead of the coordinates
   alone.  I.e.  'world at 1,9' or 'gate at 4,5' intead of '1,9'
------------------------------------------------------------------------------ }

   VAR
      NamePtr: NameRecordPtr;
      Coord: XYCoord;

   FUNCTION IsObject(Loc: Location): Boolean;
      BEGIN
      IF Loc.ID.ObjTyp<>Void THEN
         IsObject:=True
      ELSE
         IsObject:=False;
      END;

   { GetName: MAIN PROCEDURE }
   BEGIN
   IF Loc.ID.ObjTyp IN [Con,Pln,Gate] THEN
      GetCoord(Loc.ID,Loc.XY)
   ELSE IF NOT SameXY(Loc.XY,Limbo) THEN
      BEGIN
      GetObject(Loc.XY,Loc.ID);
      IF Loc.ID.ObjTyp=Base THEN
         Loc.XY:=Limbo;
      END;

   Location2Index(Emp,Loc,NamePtr);
   IF NamePtr=Nil THEN
      BEGIN
      WITH Loc DO
         BEGIN
         IF IsObject(Loc) THEN
            BEGIN
            IF (ID.ObjTyp=Flt) OR (ID.ObjTyp=DestFlt) THEN
               GetFleetName(Emp,ID,Strg)
            ELSE
               BEGIN
               GetCoord(ID,Coord);
               GetCoordName(Coord,Strg);
               IF LongFormat AND Known(Emp,ID) THEN
                  Strg:=ObjName[ID.ObjTyp]+' at '+Strg;
               END;
            END
         ELSE
            BEGIN
            GetCoordName(XY,Strg);
            END;
         END;  { with scope }
      END
   ELSE
      BEGIN
      GetDefinedName(Emp,NamePtr,Strg,Loc);
      END;  { if }
   END;  { GetName }

PROCEDURE GetLocation(Emp: Empire; Strg: String32; VAR Loc: Location);
{ GetLocation:
   Given a string this procedure will convert it to its associated Location }

   var
      TempCoord: XYCoord;
      TempID: IDNumber;
      Dummy: String32;
      NamePtr: NameRecordPtr;

   { GetLocation: MAIN PROCEDURE }
   BEGIN
   Loc.XY:=Limbo;  Loc.ID:=EmptyQuadrant;
   Name2Index(Emp,Strg,NamePtr);
   IF NamePtr=Nil THEN

      { name is not defined }
      BEGIN
      Name2Fleet(Emp,Strg,Loc.ID);
      IF SameID(Loc.ID,EmptyQuadrant) THEN

         { name is not a fleet name }
         BEGIN
         Name2Coord(Strg,TempCoord);
         IF NOT SameXY(TempCoord,Limbo) THEN

            { name is an xy coord string }
            BEGIN
            GetObject(TempCoord,TempID);
            IF NOT SameID(TempID,EmptyQuadrant) THEN
               Loc.ID:=TempID
            ELSE
               Loc.XY:=TempCoord;
            END;
         END;
      END

   else

      { name is defined }
      begin
      GetDefinedName(Emp,NamePtr,Dummy,Loc);
      end;
   end;  { GetLocation }

FUNCTION ObjectName(Emp: Empire; ObjID: IDNumber; LongFormat: Boolean): String32;
   VAR
      Strg: String32;
      Loc: Location;

   BEGIN
   Loc.ID:=ObjID; Loc.XY:=Limbo;
   GetName(Emp,Loc,LongFormat,Strg);
   ObjectName:=Strg;
   END;  { ObjectName }

PROCEDURE DeleteAllNames(Emp: Empire);
   VAR
      NextName,CurName: NameRecordPtr;

   BEGIN
   WITH Universe^.EmpireData[Emp] DO
      BEGIN
      NextName:=Names;
      CurName:=Names;
      END;  { with scope }

   WHILE NextName<>Nil DO
      BEGIN
      NextName:=CurName^.Next;
      Dispose(CurName);
      CurName:=NextName;
      END;
   END;  { DeleteAllNames }

(* MISCELLANEOUS Procedures ------------------------------------------------- *)

PROCEDURE AddIDCell(VAR Root: IDPtr; NewID: IDNumber);
{ AddIDCell:
   This procedure adds an element to a linked list.
   TESTED: 7/1/87. }

   VAR
      NewCell: IDPtr;

   BEGIN
   IF Root=Nil THEN
      BEGIN
      New(Root);
      NewCell:=Root;
      END
   ELSE BEGIN
      NewCell:=Root;
      WHILE NewCell^.Next<>Nil DO
         NewCell:=NewCell^.Next;

      New(NewCell^.Next);
      NewCell:=NewCell^.Next;
      END;

   NewCell^.ID:=NewID;
   NewCell^.Next:=Nil;
   END;  { AddIDCell }

PROCEDURE DisposeIDList{VAR Root: IDPtr};
{ DisposeIDList:
   This procedure disposes of a linked list.
   TESTED: 7/1/87. }

   VAR
      NextCell,CellToDispose: IDPtr;

   BEGIN
   CellToDispose:=Root;
   WHILE CellToDispose<>Nil DO
      BEGIN
      NextCell:=CellToDispose^.Next;
      Dispose(CellToDispose);
      CellToDispose:=NextCell;
      END;
   END;  { DisposeIDList }

FUNCTION NextEmpire(Player: Empire): Empire;
   BEGIN
   REPEAT
      IF Player=Empire8 THEN
         Player:=Empire1
      ELSE
         Inc(Player);
   UNTIL EmpireActive(Player);
   NextEmpire:=Player;
   END;  { NextEmpire }

END.
