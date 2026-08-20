(* NPEINTR.PAS -----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT NPEIntr;

{$IFDEF Overlay}
{$F+,O+}
{$ENDIF}

INTERFACE

USES Int,
     Types,
     Galaxy,
     DataCnst,
	  DataStrc,
     NPETypes,
     Misc,
     Primintr,
     Intrface,
     News,
     Fleet,
     Attack,
     AttNPE;

CONST
   MaxNoOfRegions = 20;

   { MaxNoOfGuards: ------------------------------------------------------------
     This is the maximum number of fleets that will stay over a base planet.
     This number can be decreased to 0 or increased to MaxNoOfFleets.
     In general, the more guards over a base planet, the more ships an empire
     will have to attack with. Past a certain point, however, the number of
     guards will reach the MaxNoOfFleets, and the empire will stagnate
     and decay. -------------------------------------------------------------- }
   MaxNoOfGuards = 3;
	MaxNoOfRaiders = 3;

CONST
	Defense1: DefenseRecord =
                        { fgt hkr jmp jtn pen ssp trn }
      ( ShellDefDist: ( (   0,  0,  0,  0,  0,  0,  0 ),        { deep space }
                        (   0,  0,  0,  0,100,100,  0 ),        { high orbit }
                        (  50,100,100,  0,  0,  0,  0 ),        { orbit }
                        (   0,  0,  0,  0,  0,  0,  0 ),        { sub-orbit }
                        (  50,  0,  0,100,  0,  0,100 ) ) );    { ground }

	Defense2: DefenseRecord =
                        { fgt hkr jmp jtn pen ssp trn }
      ( ShellDefDist: ( (   0,  0,  0,  0,  0,  0,  0 ),        { deep space }
                        (   0,  0,  0,  0,  0,  0,  0 ),        { high orbit }
                        ( 100,100,100,  0,100,100,  0 ),        { orbit }
                        (   0,  0,  0,  0,  0,  0,  0 ),        { sub-orbit }
                        (   0,  0,  0,100,  0,  0,100 ) ) );    { ground }

	Defense3: DefenseRecord =
                        { fgt hkr jmp jtn pen ssp trn }
      ( ShellDefDist: ( (   0,  0,  0,  0,  0,  0,  0 ),        { deep space }
                        (   0, 25, 50,  0, 75, 75,  0 ),        { high orbit }
                        (   0, 75, 50,  0, 25, 25,  0 ),        { orbit }
                        (   0,  0,  0,  0,  0,  0,  0 ),        { sub-orbit }
                        ( 100,  0,  0,100,  0,  0,100 ) ) );    { ground }

TYPE
   RegionCapitalArray = ARRAY [1..MaxNoOfRegions] OF IDNumber;

FUNCTION AlreadyTargetted(Emp: Empire; ID: IDNumber; M: MissionTypes;
                          VAR FleetData: FleetDataArray): Boolean;
FUNCTION AverageMilitaryPower(VAR RCap: RegionCapitalArray): LongInt;
PROCEDURE CreateRegionArray(Emp: Empire; VAR Region: RegionCapitalArray);
PROCEDURE DeployBattleFleet(Emp: Empire;
                            VAR FleetData: FleetDataArray;
                            FromID: IDNumber;
                            Power: LongInt;
                            GAT: LongInt;
                            NewMission: MissionTypes;
                            ToID: IDNumber);
PROCEDURE DeployCargoFleet(Emp: Empire;
                           VAR FleetData: FleetDataArray;
                           FromID: IDNumber;
                           VAR Cr: CargoArray;
                           CarryCargo: Boolean;
                           NewMission: MissionTypes;
                           ToID: IDNumber);
PROCEDURE DeployHarassFleet(Emp,EnemyEmp: Empire;
                            VAR RCap: RegionCapitalArray;
                            VAR Persona: NPECharacterRecord;
                            VAR FleetData: FleetDataArray);
PROCEDURE DeployHKRaiders(Emp,EnemyEmp: Empire;
                          VAR RCap: RegionCapitalArray;
                          VAR FleetData: FleetDataArray);
PROCEDURE DeployJumpAttack(Emp,EnemyEmp: Empire;
                           VAR RCap: RegionCapitalArray;
                           VAR Persona: NPECharacterRecord;
                           VAR FleetData: FleetDataArray);
PROCEDURE DeploySlowAttack(Emp,EnemyEmp: Empire;
                           VAR RCap: RegionCapitalArray;
                           VAR Persona: NPECharacterRecord;
                           VAR FleetData: FleetDataArray);
PROCEDURE EnforceNPEDataLinks(Emp: Empire; VAR FleetData: FleetDataArray);
PROCEDURE GetBestPlanetToProtect(BaseID: IDNumber; VAR BestWorldID: IDNumber);
PROCEDURE GetBestTarget(Emp: Empire;
                        VAR SetOfPossibilities: PlanetSet;
                        BasePower: LongInt;
                        VAR Persona: NPECharacterRecord;
                        VAR FleetData: FleetDataArray;
                        VAR TargetID: IDNumber;
                        VAR TargetDefense: LongInt;
                        VAR TargetMen: LongInt);
PROCEDURE GetFleetComposition(ObjID: IDNumber; Power,GAT: LongInt; Mission: MissionTypes;
                              VAR Ships: ShipArray; VAR Cargo: CargoArray);
PROCEDURE GetNewDesignation(WorldID: IDNumber; VAR Persona: NPECharacterRecord;
                            VAR RCap: RegionCapitalArray;
                            VAR NewType: WorldTypes);
PROCEDURE GetPotentialRes(ID: IDNumber; VAR Ships: ShipArray; VAR Cargo: CargoArray;
                          VAR FleetData: FleetDataArray);
PROCEDURE GetRegionalCapital(ID: IDNumber; VAR RCap: RegionCapitalArray;
                             VAR BaseID: IDNumber);
PROCEDURE ImplementConquerMSN(Emp: Empire; FltID,TargetID: IDNumber;
                              BaseID: IDNumber;
                              VAR FleetData: FleetDataArray;
                              VAR Result: AttackResultTypes);
PROCEDURE ImplementGuardMSN(FltID,BaseID: IDNumber);
PROCEDURE ImplementJumpAttackMSN(Emp: Empire; FltID,TargetID: IDNumber;
                                 BaseID: IDNumber;
                                 VAR FleetData: FleetDataArray;
                                 VAR Result: AttackResultTypes);
PROCEDURE ImplementRaidTrnMSN(Emp: Empire; FltID,TargetID,BaseID: IDNumber;
                              VAR FleetData: FleetDataArray);
PROCEDURE ImplementRefuelMSN(FltID,TargetID: IDNumber);
PROCEDURE ImplementReturnMSN(FltID,TargetID: IDNumber);
PROCEDURE ImplementStackMSN(FltID: IDNumber; VAR FleetData: FleetDataArray);
PROCEDURE ImplementSupplyMSN(Emp: Empire; FltID,TargetID,BaseID: IDNumber; 
                             VAR FleetData: FleetDataArray);
PROCEDURE MidCourseCorrection(Emp: Empire; FltID: IDNumber;
                              VAR RCap: RegionCapitalArray;
                              VAR FleetData: FleetDataArray);
FUNCTION MinimumDefense(ID: IDNumber; VAR Persona: NPECharacterRecord): LongInt;
FUNCTION NextFleetDataSlot(VAR FleetData: FleetDataArray): Word;
PROCEDURE PlunderWorld (Emp: Empire; FltID, TargetID: IDNumber);
PROCEDURE ReDesignateEmpire(Emp: Empire; VAR RCap: RegionCapitalArray;
                            VAR Persona: NPECharacterRecord);
PROCEDURE SetEmpireDefenses(Emp: Empire);
PROCEDURE SetFleetReturn(Emp: Empire; FltID,BaseID: IDNumber;
                         VAR FleetData: FleetDataArray);
PROCEDURE SetRaidingFleetNewTarget(Emp: Empire; FltID,TargetID,BaseID: IDNumber;
                                   VAR FleetData: FleetDataArray;
                                   VAR Persona: NPECharacterRecord);
PROCEDURE StateDepartment(Emp: Empire; VAR Persona: NPECharacterRecord;
                          VAR State: StateDeptArray);
PROCEDURE StateDeptReport(Emp: Empire; VAR State: StateDeptArray);

IMPLEMENTATION

CONST
   NPETypeDefault: ARRAY [WorldTypes] OF Index =
      { ArgTyp } (  10,
      { AmbTyp }     0,
      { BseTyp }    75,
                     0,
      { CapTyp }     0,
      { CheTyp }    30,
      { IndTyp }     0,
      { JmpTyp }   100,
                     0,
      { MinTyp }    50,
      { NnjTyp }     0,
      { OutTyp }     0,
      { RawTyp }    50,
                     0,
      { StrTyp }   100,
                     0,
      { TrnTyp }    20,
                     0,
      { RsrTyp }    50,
                     0,
      { TriTyp }    40 );

   NPETypeValue: ARRAY [WorldTypes] OF Index =
      { ArgTyp } (  30,
      { AmbTyp }   100,
      { BseTyp }    10,
                     0,
      { CapTyp }   100,
      { CheTyp }    50,
      { IndTyp }    25,
      { JmpTyp }    25,
                     0,
      { MinTyp }    50,
      { NnjTyp }   100,
      { OutTyp }   100,
      { RawTyp }    50,
                     0,
      { StrTyp }    25,
                     0,
      { TrnTyp }    60,
                     0,
      { RsrTyp }    50,
                     0,
      { TriTyp }    50 );

   NPEClassValue: ARRAY [WorldClass] OF Index =
      { AmbCls } ( 100,
      { ArdCls }    35,
      { ArtCls }   100,
      { BarCls }    50,
      { ClsJ   }    65,
      { ClsK   }    65,
      { ClsL   }    65,
      { ClsM   }    65,
      { DrtCls }    50,
      { EthCls }    65,
      { FstCls }    75,
      { GsGCls }    60,
      { HLfCls }    80,
      { IceCls }    25,
      { JngCls }    75,
      { OcnCls }    50,
      { ParCls }   100,
      { PsnCls }    50,
      { RnsCls }   100,
      { UndCls }    60,
      { TerCls }    90,
      { VlcCls }    75 );

FUNCTION NextFleetDataSlot(VAR FleetData: FleetDataArray): Word;
   VAR
      i: Word;

   BEGIN
   i:=NoOfFleetsPerEmpire;
   WHILE (i>0) AND (FleetData[i].Index IN SetOfActiveFleets) DO
      Dec(i);
   NextFleetDataSlot:=i;
   END;  { NextFleetDataSlot }

PROCEDURE GetFleetComposition(ObjID: IDNumber; Power,GAT: LongInt; Mission: MissionTypes;
                              VAR Ships: ShipArray; VAR Cargo: CargoArray);
   TYPE
      SequenceArray = ARRAY [1..5] OF ShipTypes;

   CONST
      JSeq: SequenceArray =
         ( jmp,hkr,pen,ssp,fgt );
      ASeq: SequenceArray =
         ( pen,hkr,ssp,fgt,jmp );
      SSeq: SequenceArray =
         ( hkr,pen,jmp,ssp,fgt );
      ESeq: SequenceArray =
         ( jmp,hkr,hkr,hkr,hkr );
      RSeq: SequenceArray =
         ( hkr,hkr,hkr,hkr,hkr );
      LSeq: SequenceArray =
         ( fgt,jmp,pen,hkr,ssp );

   VAR
      Sequence: SequenceArray;
      i,TotalCargoSpace,CargoSpaceNeeded: Word;
      ShpAtBase: ShipArray;
      CarAtBase: CargoArray;
      MenToTake,NnjToTake: Resources;
      ShI: ResourceTypes;

   BEGIN
   CASE Mission OF
          ReturnMSN: Sequence:=LSeq;
         ConquerMSN: Sequence:=JSeq;
           StackMSN: Sequence:=ASeq;
      JumpAttackMSN: Sequence:=ESeq;
         RaidTrnMSN: Sequence:=RSeq;
   ELSE
      Sequence:=JSeq;
      END;  { case }

   FillChar(Ships,SizeOf(Ships),0);
   FillChar(Cargo,SizeOf(Cargo),0);
   GetShips(ObjID,ShpAtBase);
   GetCargo(ObjID,CarAtBase);
   i:=1;
   WHILE (Power>0) AND (i<=5) DO
      BEGIN
      ShI:=Sequence[i];
      Ships[ShI]:=LesserInt(ShpAtBase[ShI],Round(Power/MPower[ShI])+1);
      Dec(Power,LongInt(Ships[ShI])*MPower[ShI]);
      Inc(i);
      END;

   IF GAT>0 THEN
      BEGIN
      { Calculate how many men and nnj needed for given troop strength }
      NnjToTake:=LesserInt(GAT DIV 3, CarAtBase[nnj]);
      GAT:=GAT-(3*NnjToTake);
      MenToTake:=LesserInt(GAT, GreaterInt(0, (CarAtBase[men] - 500)));
      GAT:=GAT-MenToTake;

      { Calculate ships needed to carry troops }
      CargoSpaceNeeded:=Round((MenToTake+NnjToTake)/CargoSpace[men]);
      Ships[jtn]:=LesserInt(ShpAtBase[jtn],Round(CargoSpaceNeeded/TrnAdj[jtn]));
      CargoSpaceNeeded:=CargoSpaceNeeded-Round(Ships[jtn]*TrnAdj[jtn]);
      Ships[trn]:=LesserInt(ShpAtBase[trn],Round(CargoSpaceNeeded/TrnAdj[trn]));
      CargoSpaceNeeded:=CargoSpaceNeeded-Round(Ships[trn]*TrnAdj[trn]);
      TotalCargoSpace:=Round(Ships[jtn]*TrnAdj[jtn]+Ships[trn]*TrnAdj[trn]);

      { Bring troops, taking into account number of ships available }
      Cargo[nnj]:=LesserInt(NnjToTake,Round(TotalCargoSpace*CargoSpace[nnj]));
      TotalCargoSpace:=TotalCargoSpace-Round(Cargo[nnj]*CargoSpace[nnj]);
      Cargo[men]:=LesserInt(MenToTake,Round(TotalCargoSpace*CargoSpace[men]));
      END;

	{ If mission calls for extra transports, take all of them }
	IF (Mission = BSRKAttackMSN) THEN
		BEGIN
		Ships[trn]:=ShpAtBase[trn];
		Ships[jtn]:=ShpAtBase[jtn];
		END;

   IF (Ships[hkr]+Ships[jmp]+Ships[jtn]+Ships[pen]+Ships[ssp]+Ships[trn])=0 THEN
      { ASSERT: Only fighters in the fleet }
      BEGIN
      Cargo[tri]:=10;
      END;
   END;  { GetFleetComposition }

FUNCTION AlreadyTargetted(Emp: Empire; ID: IDNumber; M: MissionTypes;
                          VAR FleetData: FleetDataArray): Boolean;
{ AlreadyTargetted: ------------------------------------------------------------
   Returns True if a fleet of the given mission has already been deployed to ID.
------------------------------------------------------------------------------ }
   VAR
      i: Word;

   BEGIN
   i:=1;
   WHILE (i<=NoOfFleetsPerEmpire) AND
         ( (NOT ((FleetData[i].Index) IN SetOfActiveFleets)) OR
            (M<>FleetData[i].Mission) OR
            (NOT SameID(FleetData[i].TargetID,ID)) ) DO
      Inc(i);

   IF i>NoOfFleetsPerEmpire THEN
      AlreadyTargetted:=False
   ELSE
      AlreadyTargetted:=True;
   END;  { AlreadyTargetted }

PROCEDURE GetPotentialRes(ID: IDNumber; VAR Ships: ShipArray; VAR Cargo: CargoArray;
                          VAR FleetData: FleetDataArray);
{ GetPotentialRes: -------------------------------------------------------------
   Returns the number of ships and cargo that are on the planet plus the ships
   and cargo on any fleets bound for the planet.
------------------------------------------------------------------------------ }
   VAR
      i: Word;
      Sh: ShipArray;
      Cr: CargoArray;
      FltID: IDNumber;

   BEGIN
   GetShips(ID,Ships);
   GetCargo(ID,Cargo);

   FltID.ObjTyp:=Flt;
   FltID.Index:=NoOfFleetsPerEmpire*Ord(GetStatus(ID))+1;
   FOR i:=1 TO NoOfFleetsPerEmpire DO
      WITH FleetData[i] DO
         BEGIN
         IF (FltID.Index IN SetOfActiveFleets)
            AND (Mission=ReturnMSN) AND SameID(ID,TargetID) THEN
            BEGIN
            GetShips(FltID,Sh);
            GetCargo(FltID,Cr);
            AddThings(Ships,Cargo,Sh,Cr);
            END;
         Inc(FltID.Index);
         END;
   END;  { GetPotentialRes }

FUNCTION MinimumDefense(ID: IDNumber; VAR Persona: NPECharacterRecord): LongInt;
{ MinimumDefense: --------------------------------------------------------------
   Returns the MilitaryPower value that this empire thinks is sufficient to
   defend ID. No threat estimate is made.
------------------------------------------------------------------------------ }
   VAR
      BaseValue: LongInt;
      ObjID: IDNumber;
      XY,WorldXY: XYCoord;
      i: Word;
      Emp: Empire;

   BEGIN
   BaseValue:=NPETypeValue[GetType(ID)];

   { Adjust for population }
   BaseValue:=Round(BaseValue*(0.5+(GetPopulation(ID)/2500)));

   { Adjust for empire character }
   WITH Persona DO
      BEGIN
      BaseValue:=BaseValue*Defensive*50;
      END;  { with scope }

   { Adjust if enemy worlds within 5 sectors }
   GetCoord(ID,WorldXY);
   ObjID.ObjTyp:=Pln;
   Emp:=GetStatus(ID);
   FOR i:=1 TO NoOfPlanets DO
      IF NOT (i IN SetOfPlanetsOf[Emp]) THEN
         BEGIN
         ObjID.Index:=i;
         IF GetStatus(ObjID)<>Indep THEN
            BEGIN
            GetCoord(ObjID,XY);
            IF Distance(XY,WorldXY)<=5 THEN
               BEGIN
               BaseValue:=BaseValue*3;
               IF GetType(ObjID)=BseTyp THEN
                  BaseValue:=BaseValue*2
               ELSE IF GetType(ObjID)=CapTyp THEN
                  BaseValue:=BaseValue*2;
               END;
            END;
         END;

   MinimumDefense:=BaseValue;
   END;  { MinimumDefense }

FUNCTION AverageMilitaryPower(VAR RCap: RegionCapitalArray): LongInt;
   VAR
      i,NoOfBases: Word;
      TotalPower: Real;
      Sh: ShipArray;
      Df: DefnsArray;

   BEGIN
   NoOfBases:=0;
   TotalPower:=0;
   FillChar(Df,SizeOf(Df),0);

   FOR i:=1 TO MaxNoOfRegions DO
      IF NOT SameID(RCap[i],EmptyQuadrant) THEN
         BEGIN
         Inc(NoOfBases);
         GetShips(RCap[i],Sh);
         TotalPower:=TotalPower+MilitaryPower(Sh,Df);
         END;

   IF NoOfBases>0 THEN
      AverageMilitaryPower:=Round(TotalPower/NoOfBases)
   ELSE
      AverageMilitaryPower:=0;
   END;  { AverageMilitaryPower }

PROCEDURE GetBestBase(Emp: Empire; TargetID: IDNumber; FleetPower, FleetGAT: LongInt;
							 VAR WorldID: IDNumber);
{ GetBestBase: ---------------------------------------------------------------
	Returns the nearest world to TargetID that has enough ships and men
	to deploy a fleet of the given size. If a world is not available, it
	returns EmptyQuadrant.
---------------------------------------------------------------------------- }
	VAR
		i: Word;
		BestWorldID,TestID: IDNumber;
		BestDistance: Word;
		TargetXY,TestXY: XYCoord;

	FUNCTION Qualifies(TestID: IDNumber): Boolean;
		VAR
			Ships: ShipArray;
			Cargo: CargoArray;
			Defns: DefnsArray;

		BEGIN
		GetShips(TestID,Ships);
		GetCargo(TestID,Cargo);
		FillChar(Defns,SizeOf(Defns),0);

		Qualifies := (MilitaryPower (Ships, Defns) > (FleetPower DIV 2))
					  AND ((Cargo[men] + LongInt(5) * Cargo[nnj]) > FleetGAT);
		END;  { Qualifies }

	{ GetBestBase }
	BEGIN
	BestWorldID:=EmptyQuadrant;
	BestDistance:=15;

	GetCoord(TargetID,TargetXY);

	TestID.ObjTyp:=Pln;
	FOR i:=1 TO NoOfPlanets DO
		IF i IN SetOfPlanetsOf[Emp] THEN
			BEGIN
			TestID.Index:=i;

			GetCoord(TestID,TestXY);
			IF (Distance(TestXY,TargetXY)<BestDistance) AND Qualifies(TestID) THEN
				BEGIN
				BestWorldID:=TestID;
				BestDistance:=Distance(TestXY,TargetXY);
				END;
			END;

	WorldID:=BestWorldID;
	END;  { GetBestBase }

PROCEDURE DeployBattleFleet(Emp: Empire;
                            VAR FleetData: FleetDataArray;
                            FromID: IDNumber;
                            Power: LongInt;
                            GAT: LongInt;
                            NewMission: MissionTypes;
                            ToID: IDNumber);
{ DeployBattleFleet: -----------------------------------------------------------
   Deploys a battle fleet of the given power and mission.
------------------------------------------------------------------------------ }
   VAR
      Ships: ShipArray;
      Cargo: CargoArray;
      DestXY: XYCoord;
      FltID: IDNumber;
      Slot,i,PNum: Word;

   BEGIN
   GetFleetComposition(FromID,Power,GAT,NewMission,Ships,Cargo);
   GetCoord(ToID,DestXY);
   DeployFleet(Emp,FromID,Ships,Cargo,DestXY,FltID);

   IF NOT SameID(FltID,EmptyQuadrant) THEN
      BEGIN
      Slot:=NextFleetDataSlot(FleetData);
      IF (Slot=0) OR (EstimatedDateOfArrival(FltID)>EstimatedRange(FltID)) THEN
         BEGIN
         AbortFleet(FltID,FromID,True);
         DestroyFleet(FltID);
         END
      ELSE
         BEGIN
         SetNPEDataIndex(FltID,Slot);
         WITH FleetData[Slot] DO
            BEGIN
            Mission:=NewMission;
				HomeBaseID:=FromID;
            TargetID:=ToID;
            Waiting:=0;
            Index:=FltID.Index;
            END;

         { launch probes to area }
         IF (GetStatus(ToID)<>Emp) AND (GetStatus(ToID)<>Indep) THEN
            BEGIN
            FOR i:=1 TO Rnd(1,4) DO
               BEGIN
               GetProbe(Emp,PNum);
               IF PNum<>0 THEN
                  LaunchProbe(Emp,PNum,DestXY);
               END;
            END;
         END;
      END;
   END;  { DeployBattleFleet }

PROCEDURE DeployCargoFleet(Emp: Empire;
                           VAR FleetData: FleetDataArray;
                           FromID: IDNumber;
                           VAR Cr: CargoArray;
                           CarryCargo: Boolean;
                           NewMission: MissionTypes;
                           ToID: IDNumber);
{ DeployCargoFleet: ------------------------------------------------------------
------------------------------------------------------------------------------ }
   VAR
      Ships,Sh: ShipArray;
      GroundCr: CargoArray;
      DestXY: XYCoord;
      FltID: IDNumber;
      Slot,CargoSpace,JtnCargo: Word;
      CarI: CargoTypes;

   BEGIN
   FillChar(Sh,SizeOf(Sh),0);
   GetShips(FromID,Ships);

   CargoSpace:=-FleetCargoSpace(Sh,Cr);
   Sh[jtn]:=LesserInt(Ships[jtn],1+Round(CargoSpace/TrnAdj[jtn]));
   Sh[trn]:=LesserInt(Ships[trn],1+CargoSpace);
   JtnCargo:=Round(Sh[jtn]*TrnAdj[jtn]);
   IF (JtnCargo<CargoSpace) AND (JtnCargo<Sh[trn]) THEN
      { ASSERT: Not enought jtn to carry all, more cargo space in trn }
      Sh[jtn]:=0
   ELSE
      { ASSERT: Jtns can carry all }
      Sh[trn]:=0;

   IF CarryCargo THEN
      BEGIN
      GetCargo(FromID,GroundCr);
      FOR CarI:=men TO tri DO
         Cr[CarI]:=LesserInt(Cr[CarI],GroundCr[CarI]);
      END
   ELSE
      FillChar(Cr,SizeOf(Cr),0);

   BalanceFleet(Sh,Cr);

   GetCoord(ToID,DestXY);
   DeployFleet(Emp,FromID,Sh,Cr,DestXY,FltID);

   IF NOT SameID(FltID,EmptyQuadrant) THEN
      BEGIN
      Slot:=NextFleetDataSlot(FleetData);
      IF (Slot=0) OR (EstimatedDateOfArrival(FltID)>EstimatedRange(FltID)) THEN
         BEGIN
         AbortFleet(FltID,FromID,True);
         DestroyFleet(FltID);
         END
      ELSE
         BEGIN
         SetNPEDataIndex(FltID,Slot);
         WITH FleetData[Slot] DO
            BEGIN
            Mission:=NewMission;
				HomeBaseID:=FromID;
            TargetID:=ToID;
            Waiting:=0;
            Index:=FltID.Index;
            END;
         END;
      END;
   END;  { DeployCargoFleet }

PROCEDURE GetBestPlanetToProtect(BaseID: IDNumber; VAR BestWorldID: IDNumber);
   VAR
      BaseXY,XY: XYCoord;
      ObjID: IDNumber;
      LowestDefenses,Power: LongInt;
      Sh: ShipArray;
      Df: DefnsArray;
      i: Word;
      Emp: Empire;

   BEGIN
   Emp:=GetStatus(BaseID);
   GetCoord(BaseID,BaseXY);
   LowestDefenses:=MaxLongInt;
   { Default planet = capital }
   GetCapital(Emp,BestWorldID);

   ObjID.ObjTyp:=Pln;
   FOR i:=1 TO NoOfPlanets DO
      IF i IN SetOfPlanetsOf[Emp] THEN
         BEGIN
         ObjID.Index:=i;
         GetCoord(ObjID,XY);
         IF (Distance(XY,BaseXY)<=5) AND (NOT (GetType(ObjID) IN [BseTyp,CapTyp])) THEN
            { ASSERT: Consider non-base planets within 5 sectors. }
            BEGIN
            GetShips(ObjID,Sh);
            GetDefns(ObjID,Df);
            Power:=MilitaryPower(Sh,Df);
            IF (Power<LowestDefenses) THEN
               BEGIN
               BestWorldID:=ObjID;
               LowestDefenses:=Power;
               END;
            END;
         END;
   END;  { GetBestPlanetToProtect }

PROCEDURE GetBestRaiderTarget(Emp,EnemyEmp: Empire; VAR TargetID: IDNumber);
   VAR
      ObjID,BestTarget: IDNumber;
      i: Word;

   BEGIN
   BestTarget:=EmptyQuadrant;

   { ASSERT: Look for gate targets. }
   ObjID.ObjTyp:=Gate;
   FOR i:=1 TO MaxNoOfStargates DO
      IF i IN SetOfActiveGates THEN
         BEGIN
         ObjID.Index:=i;
         IF (Rnd(1,100)<50) AND Known(Emp,ObjID) AND (GetStatus(ObjID)=EnemyEmp) THEN
            BestTarget:=ObjID;
         END;

   { ASSERT: Look for construction targets }
   IF SameID(BestTarget,EmptyQuadrant) THEN
      BEGIN
      ObjID.ObjTyp:=Con;
      FOR i:=1 TO MaxNoOfConstrSites DO
         IF i IN SetOfConstructionSitesOf[EnemyEmp] THEN
            BEGIN
            ObjID.Index:=i;
            IF (Rnd(1,100)<50) AND Known(Emp,ObjID) THEN
               BestTarget:=ObjID;
            END;
      END;

   { ASSERT: Look for planet targets }
   IF SameID(BestTarget,EmptyQuadrant) THEN
      BEGIN
      ObjID.ObjTyp:=Pln;
      FOR i:=1 TO NoOfPlanets DO
         IF i IN SetOfPlanetsOf[EnemyEmp] THEN
            BEGIN
            ObjID.Index:=i;
            IF (Rnd(1,100)<25) AND Known(Emp,ObjID) THEN
               BestTarget:=ObjID;
            END;
      END;

   TargetID:=BestTarget;
   END;  { GetBestRaiderTarget }

PROCEDURE GetBestTarget(Emp: Empire;
                        VAR SetOfPossibilities: PlanetSet;
                        BasePower: LongInt;
                        VAR Persona: NPECharacterRecord;
                        VAR FleetData: FleetDataArray;
                        VAR TargetID: IDNumber;
                        VAR TargetDefense: LongInt;
                        VAR TargetMen: LongInt);
   VAR
      Defense,Troops: LongInt;

      i: Word;
      ID: IDNumber;
      TargetValue,CalcValue,DefValue,Factor: Real;

      Sh: ShipArray;
      Cr: CargoArray;
      Df: DefnsArray;

   BEGIN
   TargetDefense:=0;
   TargetMen:=0;
   TargetValue:=0;
   TargetID:=EmptyQuadrant;

   Factor:=(1.5+Random)*(Persona.WorldPower/20);

   ID.ObjTyp:=Pln;
   FOR i:=1 TO NoOfPlanets DO
      BEGIN
      ID.Index:=i;
      IF (i IN SetOfPossibilities) AND
         (Known(Emp,ID)) AND
         (NOT AlreadyTargetted(Emp,ID,ConquerMSN,FleetData)) THEN
         BEGIN
         GetShips(ID,Sh);
         GetCargo(ID,Cr);
         GetDefns(ID,Df);

         { value of world dependent on tech and class }
         CalcValue:=Factor*(Ord(GetTech(ID))+1)*(NPEClassValue[GetClass(ID)]);

         CalcValue:=CalcValue*(GetPopulation(ID)/1000);

         { adjust for defenses on world }
         Defense:=MilitaryPower(Sh,Df);
         Troops:=LongInt(Cr[men])+4*Cr[nnj]+10;
         DefValue:=100+Round((Defense+Troops)/1000);
         CalcValue:=CalcValue/DefValue;

         IF (CalcValue>TargetValue) AND (Defense<BasePower) THEN
            BEGIN
            TargetValue:=CalcValue;
            TargetDefense:=Defense;
            TargetMen:=Troops;
            TargetID:=ID;
            END;
         END;
      END;

   END;  { GetBestTarget }

PROCEDURE DeployHarassFleet(Emp,EnemyEmp: Empire;
                            VAR RCap: RegionCapitalArray;
                            VAR Persona: NPECharacterRecord;
                            VAR FleetData: FleetDataArray);
   BEGIN
   END;  { DeployHarassFleet }

PROCEDURE DeployJumpAttack(Emp,EnemyEmp: Empire;
                           VAR RCap: RegionCapitalArray;
                           VAR Persona: NPECharacterRecord;
                           VAR FleetData: FleetDataArray);
{ DeployJumpAttack: ------------------------------------------------------------
   Finds the best target to attack with jumpfleets and deploys a battle fleet
   from the nearest base planet.
------------------------------------------------------------------------------ }
   VAR
      BasePower,TargetDefense,TargetMen,FleetPower,FleetGAT: LongInt;
      TargetID,BaseID: IDNumber;
      Df: DefnsArray;
      ShipsAvail: ShipArray;

   PROCEDURE GetPowerToUse(TargetDefense,TargetMen: LongInt;
                           VAR FleetPower,FleetGAT: LongInt);
      BEGIN
      FleetPower:=LongInt(30000)+Round((Random+Rnd(2,5))*TargetDefense);
      FleetGAT:=Round(2.0*TargetMen);
      END;  { GetPowerToUse }

   { DeployJumpAttack: MAIN PROCEDURE }
   BEGIN
   BasePower:=AverageMilitaryPower(RCap);
   GetBestTarget(Emp,SetOfPlanetsOf[EnemyEmp],BasePower,Persona,
                 FleetData,TargetID,TargetDefense,TargetMen);
   IF NOT SameID(TargetID,EmptyQuadrant) THEN
      BEGIN
      GetPowerToUse(TargetDefense,TargetMen,FleetPower,FleetGAT);

      GetRegionalCapital(TargetID,RCap,BaseID);
      GetShips(BaseID,ShipsAvail);
      FillChar(Df,SizeOf(Df),0);
      IF MilitaryPower(ShipsAvail,Df)>(FleetPower DIV 2) THEN
         DeployBattleFleet(Emp,FleetData,BaseID,FleetPower,FleetGAT,JumpAttackMSN,TargetID)
		ELSE
			{ ASSERT: Nearest base does not have enough ships }
			{         Look at other worlds }
			BEGIN
			GetBestBase(Emp,TargetID,FleetPower,FleetGAT,BaseID);
			IF NOT SameID(BaseID,EmptyQuadrant) THEN
	         DeployBattleFleet(Emp,FleetData,BaseID,FleetPower,FleetGAT,JumpAttackMSN,TargetID)
			END
      END;
   END;  { DeployJumpAttack }

PROCEDURE DeployHKRaiders(Emp,EnemyEmp: Empire;
                          VAR RCap: RegionCapitalArray;
                          VAR FleetData: FleetDataArray);
   VAR
      BaseID,TargetID: IDNumber;
      FleetPower: LongInt;

   { DeployHKRaiders: MAIN PROCEDURE }
   BEGIN
   GetBestRaiderTarget(Emp,EnemyEmp,TargetID);
   IF NOT SameID(TargetID,EmptyQuadrant) THEN
      BEGIN
      GetRegionalCapital(TargetID,RCap,BaseID);
      FleetPower:=LongInt(MPower[hkr])*Rnd(100,5000);
      DeployBattleFleet(Emp,FleetData,BaseID,FleetPower,0,RaidTrnMSN,TargetID);
      END;
   END;  { DeployHKRaiders }

PROCEDURE DeploySlowAttack(Emp,EnemyEmp: Empire;
                           VAR RCap: RegionCapitalArray;
                           VAR Persona: NPECharacterRecord;
                           VAR FleetData: FleetDataArray);
   VAR
      BasePower,TargetDefense,TargetMen,FleetPower,FleetGAT: LongInt;
      TargetID,BaseID: IDNumber;
      Df: DefnsArray;
      ShipsAvail: ShipArray;

   PROCEDURE GetPowerToUse(TargetDefense,TargetMen: LongInt;
                           VAR FleetPower,FleetGAT: LongInt);
      BEGIN
      FleetPower:=LongInt(50000)+Round((Random+Rnd(2,5))*TargetDefense);
      FleetGAT:=Round(2.0*TargetMen);
      END;  { GetPowerToUse }

   BEGIN
   BasePower:=AverageMilitaryPower(RCap);
   GetBestTarget(Emp,SetOfPlanetsOf[EnemyEmp],BasePower,Persona,
                 FleetData,TargetID,TargetDefense,TargetMen);
   IF NOT SameID(TargetID,EmptyQuadrant) THEN
      BEGIN
      GetPowerToUse(TargetDefense,TargetMen,FleetPower,FleetGAT);
      GetRegionalCapital(TargetID,RCap,BaseID);
      GetShips(BaseID,ShipsAvail);
      FillChar(Df,SizeOf(Df),0);
      IF MilitaryPower(ShipsAvail,Df)>(FleetPower DIV 2) THEN
         DeployBattleFleet(Emp,FleetData,BaseID,FleetPower,FleetGAT,SlowAttackMSN,TargetID)
		ELSE
			{ ASSERT: Nearest base does not have enough ships, }
			{         look at other worlds }
			BEGIN
			GetBestBase(Emp,TargetID,FleetPower,FleetGAT,BaseID);
			IF NOT SameID(BaseID,EmptyQuadrant) THEN
	         DeployBattleFleet(Emp,FleetData,BaseID,FleetPower,FleetGAT,JumpAttackMSN,TargetID)
			END
      END;
   END;  { DeploySlowAttack }

PROCEDURE GetRegionalCapital(ID: IDNumber; VAR RCap: RegionCapitalArray;
                             VAR BaseID: IDNumber);
{ GetRegionalCapital: ----------------------------------------------------------
   Returns the nearest regional capital to ID.
------------------------------------------------------------------------------ }
   VAR
      XY,BaseXY: XYCoord;
      ClosestID: IDNumber;
      i,BestDist,Dist: Word;

   BEGIN
   GetCoord(ID,XY);
   BestDist:=100;
   i:=1;
   WHILE (i<=MaxNoOfRegions) AND (NOT SameID(RCap[i],EmptyQuadrant)) DO
      BEGIN
      GetCoord(RCap[i],BaseXY);
      Dist:=Distance(XY,BaseXY);
      IF Dist<BestDist THEN
         BEGIN
         BestDist:=Dist;
         ClosestID:=RCap[i];
         END;
      Inc(i);
      END;

   BaseID:=ClosestID;
   END;  { GetRegionalCapital }

PROCEDURE GetNewDesignation(WorldID: IDNumber; VAR Persona: NPECharacterRecord;
                            VAR RCap: RegionCapitalArray;
                            VAR NewType: WorldTypes);
   VAR
      Chance: ARRAY [WorldTypes] OF Real;
      ClssAdj: ARRAY [IndusTypes] OF Real;
      TypI: WorldTypes;
      Tech: TechLevel;
      IndI: IndusTypes;
      Pop: Population;
      Roll,Total: LongInt;
      BaseID,CapID: IDNumber;
      CapXY,WorldXY,BaseXY: XYCoord;
      Emp,EmpI: Empire;
      CloseToACapital: Boolean;

   BEGIN
   Tech:=GetTech(WorldID);
   Pop:=GetPopulation(WorldID);

   FOR IndI:=BioInd TO TriInd DO
      ClssAdj[IndI]:=Sqr(ClassIndAdj[GetClass(WorldID),IndI]/100);

   { Set default chance }
   FOR TypI:=AgrTyp TO TriTyp DO
      BEGIN
      IF Tech<MinTechForType[TypI] THEN
         Chance[TypI]:=0
      ELSE
         Chance[TypI]:=NPETypeDefault[TypI];
      END;

   Chance[GetType(WorldID)]:=Chance[GetType(WorldID)]*20;

   { Adjust for class }
   Chance[RawTyp]:=Chance[RawTyp]*ClssAdj[MinInd]*ClssAdj[CheInd]*ClssAdj[TriInd];
   Chance[MinTyp]:=Chance[MinTyp]*ClssAdj[MinInd];
   Chance[CheTyp]:=Chance[CheTyp]*ClssAdj[CheInd];
   Chance[TriTyp]:=Chance[TriTyp]*ClssAdj[TriInd];
   Chance[AgrTyp]:=Chance[AgrTyp]*ClssAdj[SupInd];
   Chance[JmpTyp]:=Chance[JmpTyp]*ClssAdj[CheInd]*ClssAdj[MinInd]*ClssAdj[SupInd];
   Chance[StrTyp]:=Chance[StrTyp]*ClssAdj[MinInd]*ClssAdj[SupInd];
   Chance[BseTyp]:=Chance[BseTyp]*ClssAdj[MinInd]*ClssAdj[SupInd];

   { adjust for population }
   IF Pop>2000 THEN
      BEGIN
      Chance[BseTyp]:=Chance[BseTyp]*25;
      Chance[StrTyp]:=Chance[StrTyp]*28;
      Chance[JmpTyp]:=Chance[JmpTyp]*30;
      END
   ELSE IF Pop>1000 THEN
      BEGIN
      Chance[BseTyp]:=Chance[BseTyp]*12;
      Chance[StrTyp]:=Chance[StrTyp]*6;
      Chance[JmpTyp]:=Chance[JmpTyp]*12;
      END;

   { adjust for base planet }
   GetRegionalCapital(WorldID,RCap,BaseID);
   GetCoord(BaseID,BaseXY);
   GetCoord(WorldID,WorldXY);
   IF (Distance(BaseXY,WorldXY)<5) AND (Distance(BaseXY,WorldXY)>0) THEN
      Chance[BseTyp]:=Chance[BseTyp]*0.01;

   { adjust for distance to enemy }
   Emp:=GetStatus(WorldID);
   EmpI:=Empire1;
   CloseToACapital:=False;
   WHILE (EmpI<>Indep) AND (NOT CloseToACapital) DO
      BEGIN
      IF EmpireActive(EmpI) THEN
         BEGIN
         GetCapital(EmpI,CapID);
         IF Known(Emp,CapID) THEN
            BEGIN
            GetCoord(CapID,CapXY);
            IF Distance(WorldXY,CapXY)<=5 THEN
               BEGIN
               Chance[BseTyp]:=Chance[BseTyp]*30;
               CloseToACapital:=True;
               END;
            END;
         END;
      Inc(EmpI);
      END;

   { adjust for ambrosia world }
   IF (GetClass(WorldID) IN [AmbCls,ParCls]) AND (Tech>=BioTchLvl) THEN
      BEGIN
      NewType:=AmbTyp;
      END
   ELSE
      { roll chances }
      BEGIN
      Total:=0;
      FOR TypI:=AgrTyp TO TriTyp DO
         BEGIN
         Chance[TypI]:=Round(Chance[TypI]);
         Total:=Total+Round(Chance[TypI]);
         END;

      IF Total<100 THEN
         NewType:=IndTyp
      ELSE
         BEGIN
         Roll:=Trunc(Random*Total)+1;
         NewType:=AgrTyp;
         WHILE ((Roll>Chance[NewType]) OR (Round(Chance[NewType])=0))
            AND (NewType<>TriTyp) DO
            BEGIN
            Roll:=Roll-Round(Chance[NewType]);
            Inc(NewType);
            END;

         IF (NewType=TriTyp) AND (Chance[TriTyp]=0) THEN
            NewType:=IndTyp;
         END;
      END;
   END;  { GetNewDesignation }

PROCEDURE ReDesignateEmpire(Emp: Empire; VAR RCap: RegionCapitalArray;
                            VAR Persona: NPECharacterRecord);
   VAR
      NewType: WorldTypes;
      WorldID,CapID: IDNumber;
      i: Word;

   BEGIN
   GetCapital(Emp,CapID);
   WorldID.ObjTyp:=Pln;
   FOR WorldID.Index:=1 TO NoOfPlanets DO
      IF (WorldID.Index IN SetOfPlanetsOf[Emp]) 
         AND (NOT ((GetType(WorldID) IN [JmpTyp,StrTyp,BseTyp])
            OR (Rnd(1,100)<10)))
         AND (NOT SameID(CapID,WorldID)) THEN
         BEGIN
         GetNewDesignation(WorldID,Persona,RCap,NewType);
         IF NewType<>GetType(WorldID) THEN
            DesignateWorld(WorldID,NewType);
         END;
   END;  { ReDesignateEmpire }

{ Fleet Mission Implementations ---------------------------------------------- }

PROCEDURE SetFleetReturn(Emp: Empire; FltID,BaseID: IDNumber;
                         VAR FleetData: FleetDataArray);
{ ----------------------------------------------------------------------------
	PRE-CONDITION: FltID is an active fleet belonging to the empire.
		BaseID is a valid object.
---------------------------------------------------------------------------- }
   VAR
      BaseXY: XYCoord;

   BEGIN
   GetCoord(BaseID,BaseXY);
   WITH FleetData[NPEDataIndex(FltID)] DO
      BEGIN
      Mission:=ReturnMSN;
      TargetID:=BaseID;
      SetFleetDestination(FltID,BaseXY);
      END;
   END;  { SetFleetReturn }

PROCEDURE SetRaidingFleetNewTarget(Emp: Empire; FltID,TargetID,BaseID: IDNumber;
                                   VAR FleetData: FleetDataArray;
                                   VAR Persona: NPECharacterRecord);
   VAR
      EnemyEmp: Empire;
      Sh: ShipArray;
      Df: DefnsArray;
      FleetPower,TargDef,TargMen: LongInt;
      NewTargetID: IDNumber;
      TargetXY,BaseXY: XYCoord;

   BEGIN
   EnemyEmp:=GetStatus(TargetID);
   GetShips(FltID,Sh);
   FillChar(Df,SizeOf(Df),0);
   FleetPower:=MilitaryPower(Sh,Df);

   IF (Rnd(1,100)<=Persona.Offensive) AND (EnemyEmp<>Emp) THEN
      { ASSERT: Choose a new target }
      BEGIN
      GetBestTarget(Emp,SetOfPlanetsOf[EnemyEmp],FleetPower,Persona,
                    FleetData,NewTargetID,TargDef,TargMen);
      IF NOT SameID(NewTargetID,EmptyQuadrant) THEN
         { ASSERT: New target found }
         BEGIN
         GetCoord(NewTargetID,TargetXY);
         SetFleetDestination(FltID,TargetXY);
         WITH FleetData[NPEDataIndex(FltID)] DO
            BEGIN
            Mission:=JumpAttackMSN;
            TargetID:=NewTargetID;
            END;
         END
      ELSE
         { ASSERT: No new targets found; returning to base }
         BEGIN
         SetFleetReturn(Emp,FltID,BaseID,FleetData);
         END;
      END
   ELSE
      BEGIN
      SetFleetReturn(Emp,FltID,BaseID,FleetData);
      END;
   END;  { SetRaidingFleetNewTarget }

PROCEDURE DestroyAllFleetsInSector(Emp: Empire; FltID: IDNumber;
                                   FleetPower: LongInt;
                                   VAR AllFleetsDestroyed: Boolean);
   VAR
      EnemyID: IDNumber;
      i: Word;
      EnemySh: ShipArray;
      EnemyDf: DefnsArray;
      EnemyXY,FltXY: XYCoord;
      Result: AttackResultTypes;

   BEGIN
   FillChar(EnemyDf,SizeOf(EnemyDf),0);
   GetCoord(FltID,FltXY);

   EnemyID.ObjTyp:=Flt;
   AllFleetsDestroyed:=True;
   FOR i:=1 TO MaxNoOfFleets DO
      IF i IN SetOfActiveFleets THEN
         { INVARIANT: AllFleetsDestroyed=True if all fleets in sector <i destroyed. }
         BEGIN
         EnemyID.Index:=i;
         GetCoord(EnemyID,EnemyXY);
         IF SameXY(FltXY,EnemyXY) AND (GetStatus(EnemyID)<>Emp) AND (Scouted(Emp,EnemyID)) THEN
            BEGIN
            GetShips(EnemyID,EnemySh);
            IF FleetPower > (MilitaryPower(EnemySh,EnemyDf) DIV 2) THEN
               BEGIN
               NPEAttack(FltID,EnemyID,CaptTrnAIT,0,Result);
               IF Result<>DefConqueredART THEN
                  AllFleetsDestroyed:=False;
               END
            ELSE
               AllFleetsDestroyed:=False;
            END;
         END;
   END;  { DestroyAllFleetsInSector }

PROCEDURE ImplementReturnMSN(FltID,TargetID: IDNumber);
   BEGIN
   AbortFleet(FltID,TargetID,True);
   DestroyFleet(FltID);
   END;  { ImplementReturnMSN }

PROCEDURE ImplementSupplyMSN(Emp: Empire; FltID,TargetID,BaseID: IDNumber; 
                             VAR FleetData: FleetDataArray);
   VAR
      FltSh,TargSh: ShipArray;
      FltCr,TargCr: CargoArray;
      CarI: CargoTypes;

   BEGIN
   GetShips(FltID,FltSh);
   GetCargo(FltID,FltCr);
   GetShips(TargetID,TargSh);
   GetCargo(TargetID,TargCr);

   FOR CarI:=men TO tri DO
      MoveThings(FltCr[CarI],FltCr[CarI],TargCr[CarI]);

   ChangeCompositionOfFleet(FltID,TargetID,FltSh,FltCr,TargSh,TargCr);

   SetFleetReturn(Emp,FltID,BaseID,FleetData);
   END;  { ImplementSupplyMSN }

PROCEDURE ImplementRefuelMSN(FltID,TargetID: IDNumber);
   VAR
      MaxFuel,FltFuel: Real;
      TonsNeeded,MaxTri,TonsOnGround: Resources;
      Sh: ShipArray;

   BEGIN
   AbortFleet(FltID,TargetID,True);
   DestroyFleet(FltID);

   TonsOnGround:=GetTrillum(TargetID);
   GetShips(TargetID,Sh);
   FltFuel:=GetFleetFuel(TargetID);
   MaxFuel:=FuelCapacity(Sh);
   TonsNeeded:=Trunc((MaxFuel-FltFuel)/FuelPerTon)+1;
   MaxTri:=LesserInt(TonsNeeded,TonsOnGround);

   RefuelFleet(TargetID,TargetID,MaxTri);
   END;  { ImplementReturnMSN }

PROCEDURE ImplementConquerMSN(Emp: Empire; FltID,TargetID: IDNumber;
                              BaseID: IDNumber;
                              VAR FleetData: FleetDataArray;
                              VAR Result: AttackResultTypes);
   VAR
      TargetXY: XYCoord;
      FleetsAtTarget: FleetSet;
      Return: Boolean;

   BEGIN
   GetCoord(TargetID,TargetXY);
   GetFleets(TargetXY,FleetsAtTarget);
   FleetsAtTarget:=FleetsAtTarget-SetOfFleetsOf[GetStatus(FltID)];

   Return:=False;
   IF (FleetsAtTarget=[]) AND (GetStatus(TargetID)=Indep) THEN
      BEGIN
      NPEAttack(FltID,TargetID,ConquerAIT,0,Result);
      Return:=True;
      END
   ELSE
      BEGIN
      IF Rnd(1,4)=1 THEN
         Return:=True;
		Result:=AttRetreatsART;
      END;

   IF Return THEN
      SetFleetReturn(Emp,FltID,BaseID,FleetData);
   END;  { ImplementConquerMSN }

PROCEDURE ImplementRaidTrnMSN(Emp: Empire; FltID,TargetID,BaseID: IDNumber;
                              VAR FleetData: FleetDataArray);
   VAR
      FleetPower: LongInt;
      AllFleetsDestroyed: Boolean;
      Result: AttackResultTypes;
      TargetXY: XYCoord;
      Sh: ShipArray;
      Cr: CargoArray;
      Df: DefnsArray;

   BEGIN
   GetShips(FltID,Sh);
   FillChar(Df,SizeOf(Df),0);
   FleetPower:=MilitaryPower(Sh,Df);
   
   DestroyAllFleetsInSector(Emp,FltID,FleetPower,AllFleetsDestroyed);

   GetCoord(FltID,TargetXY);
   GetObject(TargetXY,TargetID);
   IF AllFleetsDestroyed AND (TargetID.ObjTyp IN [Con,Gate]) THEN
      BEGIN
      NPEAttack(FltID,TargetID,DestTrnAIT,0,Result);
      END;

   WITH FleetData[NPEDataIndex(FltID)] DO
      BEGIN
      IF Waiting=5 THEN
         SetFleetReturn(Emp,FltID,BaseID,FleetData)
      ELSE
         Inc(Waiting);
      END;

   { destroy booty }
   GetShips(FltID,Sh);
   Sh[fgt]:=0; Sh[jmp]:=0; Sh[jtn]:=0; Sh[pen]:=0; Sh[ssp]:=0; Sh[trn]:=0;
   PutShips(FltID,Sh);
   GetCargo(FltID,Cr);
   FillChar(Cr,SizeOf(Cr),0);
   PutCargo(FltID,Cr);
   END;  { ImplementRaidTrnMSN }

PROCEDURE ImplementJumpAttackMSN(Emp: Empire; FltID,TargetID: IDNumber;
                                 BaseID: IDNumber;
                                 VAR FleetData: FleetDataArray;
                                 VAR Result: AttackResultTypes);
   VAR
      TargetXY,BaseXY: XYCoord;
      Df,EnemyDf: DefnsArray;
      Sh,EnemySh: ShipArray;
      FleetPower: LongInt;
      AllFleetsDestroyed: Boolean;
		LAMsToUse: Word;

   BEGIN
	Result:=AttRetreatsART;
   FillChar(Df,SizeOf(Df),0);
   GetShips(FltID,Sh);
   FleetPower:=MilitaryPower(Sh,Df);

   DestroyAllFleetsInSector(Emp,FltID,FleetPower,AllFleetsDestroyed);

   IF (AllFleetsDestroyed) AND (GetStatus(TargetID)<>Emp) THEN
      { ASSERT: All defensive fleets destroyed; ready to attack world. }
      BEGIN
      GetCoord(TargetID,TargetXY);
      GetCoord(BaseID,BaseXY);
      GetShips(TargetID,EnemySh);
      GetDefns(TargetID,EnemyDf);
      GetShips(FltID,Sh);
      GetDefns(BaseID,Df);

      { launch LAMs }
      IF (Df[LAM]>500) AND (Distance(TargetXY,BaseXY)<=5) THEN
         BEGIN
			LAMsToUse := LesserInt (Df[LAM], 2 * EnemyDf[def] + EnemyDf[ion] + (EnemyDf[GDM] DIV 2));
         LAMAttack(Emp,LAMsToUse,TargetID,EnemySh,EnemyDf);
         Dec(Df[LAM], LAMsToUse);
         PutDefns(BaseID,Df);
         END;

      GetDefns(TargetID,EnemyDf);
      FillChar(Df,SizeOf(Df),0);
      IF MilitaryPower(Sh,Df)>(MilitaryPower(EnemySh,EnemyDf) DIV 2) THEN
         { ASSERT: Attack world }
         BEGIN
         NPEAttack(FltID,TargetID,ConquerAIT,0,Result);
         END
      ELSE
         { ASSERT: World too powerful; chose new target }
			Result := NoART;
      END
   ELSE
      { ASSERT: Defensive fleets too powerful; chose new target }
		Result := NoART;
   END;  { ImplementJumpAttackMSN }

PROCEDURE ImplementStackMSN(FltID: IDNumber; VAR FleetData: FleetDataArray);
   VAR
      Emp: Empire;
      ObjID,NewWorldID: IDNumber;
      XY: XYCoord;
      NoOfGuards,i: Word;
      ActiveFleets: FleetSet;

   PROCEDURE DumpStuff(FltID,GuardID: IDNumber);
      VAR
         FltSh,GrdSh: ShipArray;
         FltCr,GrdCr: CargoArray;
         ShpI: ShipTypes;
         GATTrn,Trans: Word;

      BEGIN
      GetShips(FltID,FltSh);
      GetShips(GuardID,GrdSh);
      GetCargo(FltID,FltCr);
      GetCargo(GuardID,GrdCr);

      FOR ShpI:=fgt TO trn DO
         BEGIN
         IF FltSh[ShpI]+GrdSh[ShpI]<9999 THEN
            Trans:=FltSh[ShpI]
         ELSE
            Trans:=9999-GrdSh[ShpI];

         Dec(FltSh[ShpI],Trans);
         Inc(GrdSh[ShpI],Trans);

         IF ShpI=jtn THEN
            BEGIN
            GATTrn:=LesserInt(Trans,FltCr[men]);
            Dec(FltCr[men],GATTrn);
            GrdCr[men]:=LesserInt(9999,GrdCr[men]+GATTrn);
            Trans:=Trans-GATTrn;

            GATTrn:=LesserInt(Trans,FltCr[nnj]);
            Dec(FltCr[nnj],GATTrn);
            GrdCr[nnj]:=LesserInt(9999,GrdCr[nnj]+GATTrn);
            END
         ELSE IF ShpI=trn THEN
            BEGIN
            GATTrn:=LesserInt(Trans,CargoSpace[men]*FltCr[men]);
            Dec(FltCr[men],GATTrn);
            GrdCr[men]:=LesserInt(9999,GrdCr[men]+GATTrn);
            Trans:=GreaterInt(0,Integer(Trans)-(GATTrn DIV CargoSpace[men]));

            GATTrn:=LesserInt(Trans,FltCr[nnj]);
            Dec(FltCr[nnj],GATTrn);
            GrdCr[nnj]:=LesserInt(9999,GrdCr[nnj]+GATTrn);
            END;
         END;

      ChangeCompositionOfFleet(FltID,GuardID,FltSh,FltCr,GrdSh,GrdCr);
      END;  { DumpStuff }

   BEGIN
   Emp:=GetStatus(FltID);
   GetCoord(FltID,XY);
   GetFleets(XY,ActiveFleets);
   ActiveFleets:=(ActiveFleets * SetOfFleetsOf[Emp]) - [FltID.Index];

   NoOfGuards:=0;
   ObjID.ObjTyp:=Flt;
   FOR i:=1 TO NoOfFleetsPerEmpire DO
      WITH FleetData[i] DO
         IF (Index IN ActiveFleets) AND (Mission=GuardMSN) THEN
            BEGIN
            ObjID.Index:=Index;
            Inc(NoOfGuards);
            DumpStuff(FltID,ObjID);
            END;

   IF (FltID.Index IN SetOfActiveFleets) AND (NoOfGuards<MaxNoOfGuards) THEN
      FleetData[NPEDataIndex(FltID)].Mission:=GuardMSN
   ELSE
      { ASSERT: Too many guards, land on some poor unprotected planet }
      BEGIN
      GetBestPlanetToProtect(FltID,NewWorldID);
      SetFleetReturn(Emp,FltID,NewWorldID,FleetData)
      END;
   END;  { ImplementStackMSN }

PROCEDURE ImplementGuardMSN(FltID,BaseID: IDNumber);
   VAR
      Emp: Empire;
      i: Word;
      Trans: Integer;
      FltSh,BaseSh: ShipArray;
      FltCr,BaseCr: CargoArray;
      ShpI: ShipTypes;

   BEGIN
   Emp:=GetStatus(FltID);

   GetShips(FltID,FltSh);
   GetShips(BaseID,BaseSh);
   GetCargo(FltID,FltCr);
   GetCargo(BaseID,BaseCr);

   FOR ShpI:=fgt TO trn DO
      BEGIN
      IF BaseSh[ShpI]+FltSh[ShpI]<=9000  THEN
         Trans:=FltSh[ShpI]
      ELSE
         Trans:=9000-BaseSh[ShpI];

      IF (FltSh[ShpI]-Trans)>9999 THEN
         Trans:=FltSh[ShpI]-9999;

      BaseSh[ShpI]:=ThgLmt(BaseSh[ShpI]+Trans);
      FltSh[ShpI]:=ThgLmt(FltSh[ShpI]-Trans);
      END;

   ChangeCompositionOfFleet(FltID,BaseID,FltSh,FltCr,BaseSh,BaseCr);
   END;  { ImplementGuardMSN }

{ ---------------------------------------------------------------------------- }

PROCEDURE StateDepartment(Emp: Empire; VAR Persona: NPECharacterRecord;
                          VAR State: StateDeptArray);
{ StateDepartment: -------------------------------------------------------------
   This procedure starts and ends wars with other empires.
------------------------------------------------------------------------------ }
   VAR
      EnemyEmp: Empire;
      UsefulPower,EnemyPower: Real;
      NewPolicy: PolicyTypes;

   BEGIN
   IF State[Emp].Worlds=0 THEN
      UsefulPower:=State[Emp].TotalMilitary
   ELSE
      UsefulPower:=State[Emp].TotalMilitary/State[Emp].Worlds;

   FOR EnemyEmp:=Empire1 TO Empire8 DO
      IF EmpireActive(EnemyEmp) AND (EnemyEmp<>Emp) THEN
         WITH State[EnemyEmp] DO
            BEGIN
            IF Worlds=0 THEN
               EnemyPower:=TotalMilitary
            ELSE
               EnemyPower:=TotalMilitary/Worlds;
            NewPolicy:=Policy;

				IF Balance<-1 THEN
					NewPolicy:=ConflictPLT
				ELSE IF Balance<0 THEN
					NewPolicy:=PreemptPLT
				ELSE
					BEGIN
            	CASE Policy OF
  	            	NeutralPLT: BEGIN
                  	IF (UsefulPower>EnemyPower) AND (UsefulPower>10)
                     	AND (ThreatAssess>50) AND (Rnd(1,100)<Persona.Offensive) THEN
                     	NewPolicy:=HarassPLT
                  	ELSE IF (UsefulPower>(3*EnemyPower)) AND (UsefulPower>30)
                     	AND (ThreatAssess>75) 
                     	AND (Rnd(1,100)<(Persona.Offensive DIV 2)) THEN
                     	NewPolicy:=PreemptPLT;
                  	END;
               	HarassPLT: BEGIN
                  	IF (UsefulPower>EnemyPower) AND (UsefulPower>30)
                     	AND (ThreatAssess>50) AND (Rnd(1,100)<Persona.Offensive) THEN
                     	NewPolicy:=PreemptPLT
                  	ELSE IF (UsefulPower>(3*EnemyPower)) AND (UsefulPower>30)
                     	AND (ThreatAssess>75) AND (Rnd(1,100)<Persona.Offensive) THEN
                     	NewPolicy:=PreemptPLT
                  	ELSE IF (Aggressiveness<10) AND (Rnd(1,100)>Persona.Offensive) THEN
                     	NewPolicy:=NeutralPLT
                  	ELSE IF (UsefulPower<5) THEN
                     	NewPolicy:=NeutralPLT;
                  	END;
               	PreemptPLT: BEGIN
                  	IF (UsefulPower>EnemyPower)
                     	AND (ThreatAssess>50) AND (Rnd(1,100)<Persona.Offensive) THEN
                     	NewPolicy:=ConflictPLT
                  	ELSE IF (UsefulPower>(4*EnemyPower))
                     	AND (ThreatAssess>50) AND (Rnd(1,100)<Persona.Offensive) THEN
                     	NewPolicy:=ConflictPLT
                  	ELSE IF (Aggressiveness<15) AND (ThreatAssess<50)
                     	AND (Rnd(1,100)>Persona.Offensive) THEN
                     	NewPolicy:=NeutralPLT
                  	ELSE IF (UsefulPower<10) THEN
                     	NewPolicy:=HarassPLT;
                  	END;
               	ConflictPLT: BEGIN
                  	IF (Aggressiveness<20) AND (ThreatAssess<50)
                     	AND (Rnd(1,100)>Persona.Offensive) THEN
                     	NewPolicy:=NeutralPLT
                  	ELSE IF (Aggressiveness>80) AND (EnemyPower>(2*UsefulPower)) THEN
                     	NewPolicy:=NeutralPLT;
                  	END;
               	END;  { case }
					END;

            Policy:=NewPolicy;
            Dec(Aggressiveness,Rnd(1,2));
            END;
   END;  { StateDepartment }

PROCEDURE MidCourseCorrection(Emp: Empire; FltID: IDNumber;
                              VAR RCap: RegionCapitalArray;
                              VAR FleetData: FleetDataArray);
   VAR
      BaseID: IDNumber;
      BaseXY: XYCoord;

   BEGIN
   WITH FleetData[NPEDataIndex(FltID)] DO
      BEGIN
      IF (Mission=ReturnMSN) AND (GetStatus(TargetID)<>Emp) THEN
         { ASSERT: TargetID taken over by hostile forces. Retreat to nearest base. }
         BEGIN
         GetRegionalCapital(FltID,RCap,BaseID);
         GetCoord(BaseID,BaseXY);
         SetFleetDestination(FltID,BaseXY);
         TargetID:=BaseID;
         END
      END;
   END;  { MidCourseCorrection }

PROCEDURE StateDeptReport(Emp: Empire; VAR State: StateDeptArray);
{ StateDeptReport: -------------------------------------------------------------
   Updates TotalMilitary and ThreatAssess for each enemy empire.

   TotalMilitary:    Sum of MilitaryPower of empire divided by 1000
   ThreatAssess:     <50 lower threat. >50 higher threat.
   Worlds:           Number of worlds in the empire.
------------------------------------------------------------------------------ }
   VAR
      Planets,SInd: Word;
      TotalPop: LongInt;
      TotalShips: TotalShipArray;
      ShpI: ShipTypes;
      CapID: IDNumber;
      Tech: TechLevel;
      Threat: Real;
      EnemyEmp: Empire;

      EmpireMilitary: LongInt;
      EmpireSInd: Word;
      EmpireTech: TechLevel;

   BEGIN
   { Calculate empire military strength }
   GetEmpireStatus(Emp,Planets,TotalPop,EmpireSInd,TotalShips);
   EmpireMilitary:=1;
   FOR ShpI:=fgt TO trn DO
      Inc(EmpireMilitary,Round(TotalShips[ShpI]/1000)*LongInt(MPower[ShpI]));

   State[Emp].TotalMilitary:=EmpireMilitary;
   State[Emp].Worlds:=Planets;

   { Get tech of empire }
   GetCapital(Emp,CapID);
   EmpireTech:=GetTech(CapID);

   { calculate TotalMilitary and ThreatAssess for all other empires }
   FOR EnemyEmp:=Empire1 TO Empire8 DO
      IF (EmpireActive(EnemyEmp)) AND (EnemyEmp<>Emp) THEN
         WITH State[EnemyEmp] DO
            BEGIN
            GetEmpireStatus(EnemyEmp,Worlds,TotalPop,SInd,TotalShips);
            TotalMilitary:=1;
            FOR ShpI:=fgt TO trn DO
               Inc(TotalMilitary,Round(TotalShips[ShpI]/1000)*LongInt(MPower[ShpI]));

            GetCapital(Emp,CapID);
            Tech:=GetTech(CapID);

            { Threat assessment }
            Threat:=50;

            { empire of higher tech are a threat }
            IF Tech>EmpireTech THEN
               Threat:=Threat*1.5
            ELSE IF Tech<EmpireTech THEN
               Threat:=Threat*0.75;

            { empires with higher SInd are a threat }
            Threat:=Threat*(1+((SInd-EmpireSInd)/5));

            { empires with greater military are a threat }
            Threat:=Threat*(TotalMilitary/EmpireMilitary);

            IF Threat>100 THEN
               ThreatAssess:=100
            ELSE
               ThreatAssess:=Round(Threat);
            END;
   END;  { StateDeptReport }

PROCEDURE CreateRegionArray(Emp: Empire; VAR Region: RegionCapitalArray);
{ CreateRegionArray: -----------------------------------------------------------
   This procedure will initialize the RegionCapitalArray so that it contains
   a list of all regional capitals (base planets and capital) for the given
   empire. The region array includes base planets.
------------------------------------------------------------------------------ }
   VAR
      ID: IDNumber;
      i,NextRegion: Word;

   BEGIN
   FillChar(Region,SizeOf(Region),0);
   NextRegion:=1;

   ID.ObjTyp:=Pln;
   ID.Index:=1;
   WHILE (ID.Index<=NoOfPlanets) AND (NextRegion<=MaxNoOfRegions) DO
      BEGIN
      IF (ID.Index IN SetOfPlanetsOf[Emp]) AND (GetType(ID) IN [BseTyp,CapTyp]) THEN
         BEGIN
         Region[NextRegion]:=ID;
         Inc(NextRegion);
         END;
      Inc(ID.Index);
      END;
   END;  { CreateRegionArray }

PROCEDURE EnforceNPEDataLinks(Emp: Empire; VAR FleetData: FleetDataArray);
   VAR
      ActiveDataRecords: SET OF 1..NoOfFleetsPerEmpire;
      ActiveFleets: FleetSet;
      FltID: IDNumber;
      Rec,i: Word;

   BEGIN
   ActiveFleets:=SetOfActiveFleets * SetOfFleetsOf[Emp];
   ActiveDataRecords:=[];
   FltID.ObjTyp:=Flt;
   FOR i:=1 TO MaxNoOfFleets DO
      BEGIN
      FltID.Index:=i;
      Rec:=NPEDataIndex(FltID);
      IF (i IN ActiveFleets) AND (Rec<>0) THEN
         BEGIN
         ActiveDataRecords:=ActiveDataRecords+[Rec];
         END;
      END;

   FOR i:=1 TO NoOfFleetsPerEmpire DO
      BEGIN
      IF NOT (i IN ActiveDataRecords) THEN
         BEGIN
         FleetData[i].Index:=0;
         END;
      END;
   END;  { EnforceNPEDataLinks }

PROCEDURE SetEmpireDefenses(Emp: Empire);
	BEGIN
	CASE Rnd(1,100) OF
		  1..25: SetDefenseSettings(Emp,InitDefenseRecord);
		 26..75: SetDefenseSettings(Emp,Defense1);
		 76..85: SetDefenseSettings(Emp,Defense3);
		86..100: SetDefenseSettings(Emp,Defense2);
	END;  { case }
	END;  { SetEmpireDefenses }

PROCEDURE PlunderWorld (Emp: Empire; FltID, TargetID: IDNumber);
	VAR
		FltSh, TargetSh: ShipArray;
		FltCr, TargetCr: CargoArray;

	BEGIN
	IF (Emp = GetStatus (TargetID)) THEN
		BEGIN
		{ Steal all resources from conquered world }
		GetShips (FltID, FltSh);
		GetShips (TargetID, TargetSh);
		GetCargo (FltID, FltCr);
		GetCargo (TargetID, TargetCr);
		AddThings (FltSh, FltCr, TargetSh, TargetCr);
		BalanceFleet (FltSh, FltCr);
		PutShips (FltID, FltSh);
		PutCargo (FltID, FltCr);

		{ Leave nothing behind }
		FillChar (TargetSh, SizeOf(TargetSh), 0);
		FillChar (TargetCr, SizeOf(TargetCr), 0);
		PutShips (TargetID, TargetSh);
		PutCargo (TargetID, TargetCr);

		{ Set world to independent }
		SetType (TargetID, IndTyp);
		SetStatus (TargetID, Indep);
		END;
	END;	{ PlunderWorld }

END.

