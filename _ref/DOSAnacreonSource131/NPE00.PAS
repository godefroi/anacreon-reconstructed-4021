(* NPE00.PAS -------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT NPE00;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES
   Int,
   Types,
   Galaxy,
   DataCnst,
   Misc,
   PrimIntr,
   Intrface,
   News,
   Fleet,
   Attack,
   AttNPE,
   NPETypes,
   NPEIntr;

PROCEDURE CargoSupplyFleet(Emp: Empire; WorldID: IDNumber;
                           VAR Cr: CargoArray;
                           VAR RCap: RegionCapitalArray;
                           VAR FleetData: FleetDataArray);
PROCEDURE DefendEmpire(Emp: Empire;
                       VAR RCap: RegionCapitalArray;
                       VAR FleetData: FleetDataArray;
                       VAR Persona: NPECharacterRecord);
PROCEDURE ExplorationAndProbing(Emp: Empire;
                                VAR RCap: RegionCapitalArray; 
                                VAR Persona: NPECharacterRecord);
PROCEDURE ImperialExpansion(Emp: Empire;
                            VAR RCap: RegionCapitalArray;
                            VAR FleetData: FleetDataArray;
                            VAR Persona: NPECharacterRecord);
PROCEDURE NPEConquest(Emp: Empire; WorldID: IDNumber; Result: AttackResultTypes;
                      VAR RCap: RegionCapitalArray;
                      VAR Persona: NPECharacterRecord);
PROCEDURE ReviewNews(Emp: Empire; 
                     VAR FleetData: FleetDataArray;
                     VAR RCap: RegionCapitalArray;
                     VAR Persona: NPECharacterRecord;
                     VAR State: StateDeptArray);
PROCEDURE WarCabinet(Emp: Empire;
                     VAR RCap: RegionCapitalArray;
                     VAR FleetData: FleetDataArray;
                     VAR Persona: NPECharacterRecord;
                     VAR State: StateDeptArray);

IMPLEMENTATION

PROCEDURE RNIndustryLack(Emp: Empire; ID: IDNumber; 
                         VAR RCap: RegionCapitalArray;
                         VAR FleetData: FleetDataArray);
   VAR
      Cr: CargoArray;

   BEGIN
   FillChar(Cr,SizeOf(Cr),0);
   Cr[met]:=1000;
   CargoSupplyFleet(Emp,ID,Cr,RCap,FleetData);
   END;  { RNIndustryLack }

PROCEDURE ReviewNews(Emp: Empire; 
                     VAR FleetData: FleetDataArray;
                     VAR RCap: RegionCapitalArray;
                     VAR Persona: NPECharacterRecord;
                     VAR State: StateDeptArray);
   VAR
      News: NewsRecordPtr;
      BasePower: LongInt;
      Severity: Index;

   PROCEDURE RespondToEnemyAttack(Emp,AttEmp: Empire; Severity: Index;
                                  VAR Persona: NPECharacterRecord;
                                  VAR State: StateDeptArray);
   { EnemyAttack: --------------------------------------------------------------
      If an enemy has attacked, then increase the level of aggression.
   --------------------------------------------------------------------------- }
      VAR
         AggInc: Word;

      BEGIN
      WITH State[AttEmp] DO
         BEGIN
         CASE Policy OF
             NeutralPLT: IF (Severity>50) AND (Rnd(1,100)<Persona.Provoke) THEN
                            Policy:=PreemptPLT
                         ELSE
                            Policy:=HarassPLT;
              HarassPLT: IF (Severity>50) AND (Rnd(1,100)<Persona.Provoke) THEN 
                            Policy:=ConflictPLT
                         ELSE IF (Rnd(1,100)<Persona.Provoke) THEN
                            Policy:=PreemptPLT;
             PreemptPLT: IF (Severity>35) AND (Rnd(1,100)<=Persona.Provoke) THEN
                            Policy:=ConflictPLT;
            ConflictPLT: IF (Severity>75) AND (Rnd(1,100)<=(Persona.Provoke DIV 2)) THEN
                            Policy:=WarPLT;
            END;  { case }

         AggInc:=10+Round(Severity/5);
         IF (Aggressiveness+AggInc)>100 THEN
            Aggressiveness:=100
         ELSE IF (AggInc>0) AND ((Aggressiveness+AggInc)<35) THEN
            Aggressiveness:=35
         ELSE
            Inc(Aggressiveness,AggInc);
         END;  { with scope }
      END;  { EnemyAttack }

   FUNCTION AttackSeverity(News: NewsRecordPtr; BasePower: LongInt): Index;
      VAR
         Total: LongInt;

      BEGIN
      News:=News^.Next;
      Total:=1;
      WHILE (News<>Nil) AND (News^.Headline=DestDetail) DO
         WITH News^ DO
            BEGIN
            Total:=Total+Parm1*MPower[AttackTypes(Parm2)];
            News:=Next;
            END;

      IF BasePower=0 THEN
         BasePower:=1;
      AttackSeverity:=LesserInt(100,10+Round(50*Total/BasePower));
      END;  { AttackSeverity }

   PROCEDURE SendRescueFleet(Emp: Empire;
                             FltID: IDNumber;
                             VAR RCap: RegionCapitalArray;
                             VAR FleetData: FleetDataArray);
      VAR
         BaseID: IDNumber;
         FuelNeeded,JtnNeeded: Word;
         Sh: ShipArray;
         Cr: CargoArray;

      BEGIN
      GetRegionalCapital(FltID,RCap,BaseID);
      GetShips(FltID,Sh);
      FuelNeeded:=1+Round(FuelCapacity(Sh)/FuelPerTon);
      JtnNeeded:=1+Round(FuelNeeded/10);

      GetShips(BaseID,Sh);
      GetCargo(BaseID,Cr);
      IF (Sh[jtn]>JtnNeeded) AND (Cr[tri]>FuelNeeded) THEN
         BEGIN
         FillChar(Cr,SizeOf(Cr),0);
         Cr[tri]:=FuelNeeded;
         DeployCargoFleet(Emp,FleetData,BaseID,Cr,True,RefuelMSN,FltID);
         END;
      END;  { SendRescueFleet }

   { ReviewNews: MAIN PROCEDURE }
   BEGIN
   {$IFDEF Debug}
   WriteLn('   Review News.');
   {$ENDIF}
   BasePower:=AverageMilitaryPower(RCap);
   GetNewsList(Emp,News);
   WHILE News<>Nil DO
      WITH News^ DO
         BEGIN
         CASE Headline OF
            BattleL,
            BattleW1,
            BattleW2,
            ConDs,
            GteDs,
            LAMDm,
            LAMDs,
            LAMDef: BEGIN
					IF (Headline=BattleL) AND (Loc1.ID.ObjTyp IN [Pln,Base,Gate]) THEN
						Dec(State[Empire(Parm1)].Balance)
					ELSE IF Rnd(1,100)<25 THEN
						Dec(State[Empire(Parm1)].Balance);

               Severity:=AttackSeverity(News,BasePower);
               RespondToEnemyAttack(Emp,Empire(Parm1),Severity,Persona,State);
               END;
            NoFuel: SendRescueFleet(Emp,Loc1.ID,RCap,FleetData);
            IndLack: RNIndustryLack(Emp,Loc1.ID,RCap,FleetData);
         END;  { case }

         News:=Next;
         END;  { while loop }
   END;  { ReviewNews }

PROCEDURE DefendEmpire(Emp: Empire;
                       VAR RCap: RegionCapitalArray;
                       VAR FleetData: FleetDataArray;
                       VAR Persona: NPECharacterRecord);
{ DefendEmpire: ----------------------------------------------------------------
   This procedure makes sure that all worlds are adequately defended given
   the current character of the empire. Immediate threat is not assessed, only
   general defense. Fleets are deployed to worlds that are not well defended.
   Worlds that have enough defenses send ships to the regional capital.

   Any enemy attack fleets over worlds are attacked.

   fleet missions: GuardMSN, ReturnMSN, StackMSN
------------------------------------------------------------------------------ }
   VAR
      ID,WorldID,BaseID: IDNumber;
      Ships: ShipArray;
      Cargo,BaseCargo: CargoArray;
      Defns: DefnsArray;
      OptimumGAT,GATPower,
      MaxBaseGAT,
      ShipPower,PowerDiff,GATDiff,
      PowerAvail,OptimumPower: LongInt;

   PROCEDURE AttackEnemyFleets(Emp: Empire; ID,BaseID: IDNumber);
      VAR
         FltXY,XY,BaseXY: XYCoord;
         FltID,BattleID: IDNumber;
         EnemyFleets: FleetSet;
         Sh,FltSh: ShipArray;
         Cr,FltCr: CargoArray;
         Df,NullDf: DefnsArray;
         MPower: LongInt;
         Result: AttackResultTypes;

      BEGIN
      GetCoord(ID,XY);
      GetCoord(BaseID,BaseXY);
      EnemyFleets:=SetOfActiveFleets-SetOfFleetsOf[Emp];

      FltID.ObjTyp:=Flt;
      FOR FltID.Index:=1 TO MaxNoOfFleets DO
         IF (FltID.Index IN EnemyFleets) AND Scouted(Emp,FltID) THEN
            BEGIN
            GetCoord(FltID,FltXY);
            IF SameXY(FltXY,XY) THEN
               BEGIN
               FillChar(NullDf,SizeOf(NullDf),0);
               GetShips(FltID,FltSh);
               MPower:=MilitaryPower(FltSh,NullDf);

               { Launch LAMs against fleet }
               IF (MPower>30000) AND (Distance(BaseXY,XY)<=5) THEN
                  BEGIN
                  GetDefns(BaseID,Df);
                  IF (Df[LAM]>500) AND (Rnd(1,100)<50) THEN
                     BEGIN
                     LAMAttack(Emp,Df[LAM],FltID,FltSh,NullDf);
                     GetDefns(BaseID,Df);
                     Df[LAM]:=0;
                     PutDefns(BaseID,Df);
                     END;
                  END;

               IF FltID.Index IN SetOfActiveFleets THEN
                  BEGIN
                  { ASSERT: Enemy fleet not destroyed by LAMs }
                  GetShips(BaseID,Sh);
                  GetDefns(BaseID,Df);
                  FillChar(Cr,SizeOf(Cr),0);
                  FillChar(NullDf,SizeOf(NullDf),0);

                  GetFleetComposition(ID,MPower*2,0,JumpAttackMSN,Sh,Cr);

                  IF (NOT NoShips(Sh)) 
                     AND ((MilitaryPower(Sh,Df)<MPower) OR (MilitaryPower(Sh,NullDf)>MPower)) THEN
                     BEGIN
                     { ASSERT: There are ships on the planet and good idea to attack. }
                     DeployFleet(Emp,ID,Sh,Cr,XY,BattleID);
                     IF NOT SameID(BattleID,EmptyQuadrant) THEN
                        BEGIN
                        NPEAttack(BattleID,FltID,CaptTrnAIT,0,Result);
                        IF Result<>AttDestroyedART THEN
                           BEGIN
                           AbortFleet(BattleID,ID,True);
                           DestroyFleet(BattleID);
                           END;
                        END;
                     END;
                  END;
               END;
            END;
      END;  { AttackEnemyFleets }

   FUNCTION NoOfGuardsAtBase(Emp: Empire; 
                             BaseID: IDNumber; VAR FleetData: FleetDataArray): Word;
      VAR
         NoOfGuards,i: Word;
         FltXY,BaseXY: XYCoord;
         FltID: IDNumber;

      BEGIN
      NoOfGuards:=0;
      GetCoord(BaseID,BaseXY);

      FltID.ObjTyp:=Flt;
      FOR i:=1 TO NoOfFleetsPerEmpire DO
         WITH FleetData[i] DO
            IF (Index IN SetOfActiveFleets) AND (Mission=GuardMSN) THEN
               BEGIN
               FltID.Index:=Index;
               GetCoord(FltID,FltXY);
               IF SameXY(FltXY,BaseXY) THEN
                  Inc(NoOfGuards);
               END;

      NoOfGuardsAtBase:=NoOfGuards;
      END;  { NoOfGuardsAtBase }

   PROCEDURE GetBestBaseToProtect(Emp: Empire;
                                  VAR RCap: RegionCapitalArray;
                                  VAR FleetData: FleetDataArray;
                                  FromID: IDNumber; VAR BestBaseID: IDNumber);
      VAR
         i,Guards,LowestNo: Word;

      BEGIN
      LowestNo:=MaxNoOfGuards;
      BestBaseID:=EmptyQuadrant;
      FOR i:=1 TO MaxNoOfRegions DO
         IF (NOT SameID(RCap[i],EmptyQuadrant))
            AND (NOT SameID(RCap[i],FromID)) THEN
            BEGIN
            Guards:=NoOfGuardsAtBase(Emp,RCap[i],FleetData);
            IF (Guards<LowestNo) THEN
               BEGIN
               LowestNo:=Guards;
               BestBaseID:=RCap[i];
               END;
            END;
      END;  { GetBestBaseToProtect }

   { DefendEmpire: MAIN PROCEDURE }
   BEGIN
   ID.ObjTyp:=Pln;
   FOR ID.Index:=1 TO NoOfPlanets DO
      IF (ID.Index IN SetOfPlanetsOf[Emp]) THEN
			BEGIN
         IF (NOT (GetType(ID) IN [BseTyp,CapTyp])) THEN
            { ASSERT: World is not a base }
            BEGIN
            GetPotentialRes(ID,Ships,Cargo,FleetData);
            GetDefns(ID,Defns);
            PowerAvail:=MilitaryPower(Ships,Defns);
            FillChar(Defns,SizeOf(Defns),0);
            ShipPower:=MilitaryPower(Ships,Defns);
            OptimumPower:=MinimumDefense(ID,Persona);
            OptimumGAT:=OptimumPower DIV 20;
            GATPower:=LongInt(Cargo[men])+3*Cargo[nnj];
            GetRegionalCapital(ID,RCap,BaseID);

            AttackEnemyFleets(Emp,ID,BaseID);

            PowerDiff:=OptimumPower-PowerAvail;
            GATDiff:=OptimumGAT-GATPower;
            IF LesserInt(ShipPower,Abs(PowerDiff))>10000 THEN
               BEGIN
               IF PowerAvail<OptimumPower THEN
                  { ASSERT: World needs more defenses }
                  BEGIN
                  GetCargo(BaseID,BaseCargo);
                  MaxBaseGAT:=GreaterInt(0,(BaseCargo[men]+3*BaseCargo[nnj])-3000);
                  GATDiff:=LesserInt(MaxBaseGAT,GreaterInt(0,GATDiff));
                  DeployBattleFleet(Emp,FleetData,BaseID,PowerDiff,GATDiff,ReturnMSN,ID);
                  END
               ELSE IF NoOfGuardsAtBase(Emp,BaseID,FleetData)<MaxNoOfGuards THEN
                  { ASSERT: World has more than enough defenses 
                            and room for more guards. }
                  BEGIN
                  GATDiff:=GreaterInt(0,-GATDiff);
                  DeployBattleFleet(Emp,FleetData,ID,-PowerDiff,GATDiff,StackMSN,BaseID);
                  END;
               END;

            END
         ELSE
            { ASSERT: World is a base }
            BEGIN
            AttackEnemyFleets(Emp,ID,ID);
            IF NoOfGuardsAtBase(Emp,ID,FleetData)=MaxNoOfGuards THEN
               { ASSERT: Base has an excess of ships }
               BEGIN
               GetBestPlanetToProtect(ID,WorldID);
               IF NOT SameID(WorldID,EmptyQuadrant) THEN
                  BEGIN
                  DeployBattleFleet(Emp,FleetData,ID,9000*MPower[fgt],0,ReturnMSN,WorldID);
                  END;

               GetBestBaseToProtect(Emp,RCap,FleetData,ID,BaseID);
               IF NOT SameID(BaseID,EmptyQuadrant) THEN
                  BEGIN
                  DeployBattleFleet(Emp,FleetData,ID,150000,0,StackMSN,BaseID);
                  END;
               END;
            END;
			END;
   END;  { DefendEmpire }

PROCEDURE ImperialExpansion(Emp: Empire;
                            VAR RCap: RegionCapitalArray;
                            VAR FleetData: FleetDataArray;
                            VAR Persona: NPECharacterRecord);
{ ImperialExpansion: -----------------------------------------------------------
   This procedure rolls a chance (depending on Persona) that the empire will
   attempt to conquer an independent world. If it does decide to conquer a
   world, it evaluates the best target (again based on Persona) and sends out
   a single battle fleet. Other empire worlds and worlds that would require 
   more than one fleet are not considered.

   fleet missions: ConquerMSN
------------------------------------------------------------------------------ }
   VAR
      TargetID,BaseID: IDNumber;
      BasePower: LongInt;                 { Average military power of bases }
      FleetPower,FleetGAT: LongInt;       { fleet power }
      TargetDefense,TargetMen: LongInt;   { target power }
      Possibilities: PlanetSet;
      EmpI: Empire;
      ShipsAvail: ShipArray;
      Df: DefnsArray;

   PROCEDURE GetPowerToUse(VAR Persona: NPECharacterRecord;
                           TargetDefense,TargetMen: LongInt;
                           VAR FleetPower,FleetGAT: LongInt);
      BEGIN
      FleetPower:=Round((1.5+Random)*TargetDefense);
      FleetGAT:=Round(1.5*TargetMen);
      END;  { GetPowerToUse }

   PROCEDURE ModifyPersona(VAR Persona: NPECharacterRecord);
      VAR
         Delta,Temp: Integer;

      BEGIN
      WITH Persona DO
         BEGIN
         IF Rnd(1,100)<=RandomGene THEN
            BEGIN
            Delta:=RndVar(ImpGene,FactorGene)-RndVar(Imperialist,FactorGene);
            Temp:=Imperialist+Round((Delta/12)*FactorGene);

            IF Temp>100 THEN
               Imperialist:=100
            ELSE IF Temp<0 THEN
               Imperialist:=0
            ELSE
               Imperialist:=Temp;
            END;
         END;  { with scope }
      END;  { ModifyPersona }

   PROCEDURE GetPossibilities(Emp: Empire; VAR RCap: RegionCapitalArray;
                              VAR Persona: NPECharacterRecord;
                              VAR Possibilities: PlanetSet);
      VAR
         i,j,Threshold: Word;
         WorldID,BaseID: IDNumber;
         WorldXY,BaseXY: XYCoord;

      BEGIN
      Threshold:=2+10-(Persona.SphereX DIV 10);
      Possibilities:=[];
      WorldID.ObjTyp:=Pln;
      FOR i:=1 TO NoOfPlanets DO
         BEGIN
         WorldID.Index:=i;
         IF GetStatus(WorldID)=Indep THEN
            BEGIN
            GetCoord(WorldID,WorldXY);
            GetRegionalCapital(WorldID,RCap,BaseID);
            GetCoord(BaseID,BaseXY);
            IF Distance(WorldXY,BaseXY)<=Threshold THEN
               Possibilities:=Possibilities+[i];
            END;
         END;
      END;  { GetPossibilities }

   { ImperialExpansion }
   BEGIN
   {$IFDEF Debug}
   WriteLn('   Expansion.');
   {$ENDIF}

   IF Rnd(1,100)<=Persona.Imperialist THEN
      { ASSERT: Empire will expand }
      BEGIN
      BasePower:=AverageMilitaryPower(RCap);
      GetPossibilities(Emp,RCap,Persona,Possibilities);
      GetBestTarget(Emp,Possibilities,BasePower,Persona,
                    FleetData,TargetID,TargetDefense,TargetMen);
      IF NOT SameID(TargetID,EmptyQuadrant) THEN
         BEGIN
         GetPowerToUse(Persona,TargetDefense,TargetMen,FleetPower,FleetGAT);
         GetRegionalCapital(TargetID,RCap,BaseID);
         GetShips(BaseID,ShipsAvail);
         FillChar(Df,SizeOf(Df),0);
         IF MilitaryPower(ShipsAvail,Df)>TargetDefense THEN
            DeployBattleFleet(Emp,FleetData,BaseID,FleetPower,FleetGAT,ConquerMSN,TargetID);
         END;
      END;
   ModifyPersona(Persona);
   END;  { ImperialExpansion }

PROCEDURE WarCabinet(Emp: Empire;
                     VAR RCap: RegionCapitalArray;
                     VAR FleetData: FleetDataArray;
                     VAR Persona: NPECharacterRecord;
                     VAR State: StateDeptArray);
{ WarCabinet: ------------------------------------------------------------------
   This procedure carries out basic war maneuvers against EnemyEmp. Depending 
   on the persona and the empire's policy towards each empire, this procedure 
   will deploy battle fleets to harass and conquer enemy worlds. 
------------------------------------------------------------------------------ }
   VAR
      EnemyEmp: Empire;
      CapID: IDNumber;
      CapXY,XY: XYCoord;
      x,y: Integer;
      PNum,i,NoOfRaidersOut: Word;

	FUNCTION GetNoOfRaidersOut: Word;
		VAR
			i,Count: Word;

		BEGIN
		Count:=0;
		FOR i:=1 TO NoOfFleetsPerEmpire DO
			WITH FleetData[i] DO
				IF (Index>0) AND (Mission=RaidTrnMSN) THEN
					Inc(Count);

		GetNoOfRaidersOut:=Count;
		END;  { GetNoOfRaidersOut }

   BEGIN
   {$IFDEF Debug}
   WriteLn('   War Maneuvers.');
   {$ENDIF}

   FOR EnemyEmp:=Empire1 TO Empire8 DO
      IF EmpireActive(EnemyEmp) THEN
         WITH State[EnemyEmp] DO
            BEGIN
				NoOfRaidersOut := GetNoOfRaidersOut;
            { Deploy hk raiding fleets }
            IF (NoOfRaidersOut < MaxNoOfRaiders)
					AND ((Balance<0)
						OR ((Policy>=HarassPLT) AND (Rnd(1,100)<=AttackChance))) THEN
               BEGIN
               DeployHKRaiders(Emp,EnemyEmp,RCap,FleetData);
               END;

            { Deploy main battle fleets }
            IF ((Balance<0) AND (Rnd(1,2)=1)) OR (Rnd(1,100)<=AttackChance) THEN
               BEGIN
               CASE Policy OF
                  HarassPLT: IF NoOfRaidersOut < MaxNoOfRaiders THEN
									     DeployHKRaiders(Emp,EnemyEmp,RCap,FleetData);
                  PreemptPLT: CASE Rnd(1,100) OF
                       1..50: IF NoOfRaidersOut < MaxNoOfRaiders THEN
							  			   DeployHKRaiders(Emp,EnemyEmp,RCap,FleetData);
                     51..100: DeployJumpAttack(Emp,EnemyEmp,RCap,Persona,FleetData);
                     END;  { case }
                  ConflictPLT: CASE Rnd(1,100) OF
                       1..75: DeployJumpAttack(Emp,EnemyEmp,RCap,Persona,FleetData);
                     76..100: DeploySlowAttack(Emp,EnemyEmp,RCap,Persona,FleetData);
                     END;  { case }
                  WarPLT: CASE Rnd(1,100) OF
                       1..50: DeployJumpAttack(Emp,EnemyEmp,RCap,Persona,FleetData);
                     51..100: DeploySlowAttack(Emp,EnemyEmp,RCap,Persona,FleetData);
                     END;  { case }
                  END;  { case }
               END;

            { probe enemy capital }
            IF (Rnd(1,100)<=Aggressiveness) THEN
               BEGIN
               GetCapital(EnemyEmp,CapID);
               GetCoord(CapID,CapXY);
               FOR i:=1 TO Rnd(1,4) DO
                  BEGIN
                  x:=Rnd(Integer(CapXY.x)-4,Integer(CapXY.x)+4);
                  y:=Rnd(Integer(CapXY.y)-4,Integer(CapXY.y)+4);
                  IF InGalaxy(x,y) THEN
                     BEGIN
                     XY.x:=x; XY.y:=y;
                     GetProbe(Emp,PNum);
                     IF PNum<>0 THEN
                        LaunchProbe(Emp,PNum,XY);
                     END;
                  END;
               END;
            END;
   END;  { WarCabinet }

PROCEDURE GetClosestCargoWorld(Emp: Empire; WorldID: IDNumber; VAR Cr: CargoArray;
                               VAR CargoID: IDNumber);
{ GetClosestCargoWorld: --------------------------------------------------------
   Returns the closest world to WorldID with at least the required Cr.
------------------------------------------------------------------------------ }
   VAR
      i,ClosestDist: Word;
      ObjID,ClosestID: IDNumber;
      ObjCr: CargoArray;
      ObjXY,XY: XYCoord;
      ResI: ResourceTypes;

   BEGIN
   GetCoord(WorldID,XY);
   ClosestDist:=99;
   ClosestID:=EmptyQuadrant;

   ObjID.ObjTyp:=Pln;
   FOR i:=1 TO NoOfPlanets DO
      IF i IN SetOfPlanetsOf[Emp] THEN
         BEGIN
         ObjID.Index:=i;
         GetCoord(ObjID,ObjXY);
         IF Distance(ObjXY,XY)<ClosestDist THEN
            BEGIN
            GetCargo(ObjID,ObjCr);
            ResI:=men;
            WHILE (ResI<Succ(tri)) AND (ObjCr[ResI]>=Cr[ResI]) DO
               Inc(ResI);

            IF ResI=Succ(tri) THEN
               BEGIN
               ClosestID:=ObjID;
               ClosestDist:=Distance(ObjXY,XY);
               END;
            END;
         END;

   CargoID:=ClosestID;
   END;  { GetClosestCargoWorld }

PROCEDURE CargoSupplyFleet(Emp: Empire; WorldID: IDNumber;
                           VAR Cr: CargoArray;
                           VAR RCap: RegionCapitalArray;
                           VAR FleetData: FleetDataArray);
   VAR
      CargoID,BaseID: IDNumber;
      Sh: ShipArray;

   BEGIN
   GetClosestCargoWorld(Emp,WorldID,Cr,CargoID);
   IF NOT SameID(CargoID,EmptyQuadrant) THEN
      BEGIN
      GetShips(CargoID,Sh);
      IF FleetCargoSpace(Sh,Cr)>0 THEN
         { ASSERT: Enough transports on cargo world. }
         BEGIN
         IF NOT AlreadyTargetted(Emp,WorldID,SupplyMSN,FleetData) THEN
            DeployCargoFleet(Emp,FleetData,CargoID,Cr,True,SupplyMSN,WorldID);
         END
      ELSE
         BEGIN
         IF NOT AlreadyTargetted(Emp,CargoID,SupplyTrnMSN,FleetData) THEN
            BEGIN
            GetRegionalCapital(CargoID,RCap,BaseID);
            DeployCargoFleet(Emp,FleetData,BaseID,Cr,False,SupplyTrnMSN,CargoID);
            END;
         END;
      END;
   END;  { CargoSupplyFleet }

PROCEDURE NPEConquest(Emp: Empire; WorldID: IDNumber; Result: AttackResultTypes;
                      VAR RCap: RegionCapitalArray;
                      VAR Persona: NPECharacterRecord);
   VAR
      NewTyp: WorldTypes;
      BaseID: IDNumber;
      WorldXY,BaseXY: XYCoord;

   BEGIN
   IF (Result=DefConqueredART) AND (Emp=GetStatus(WorldID)) THEN
      BEGIN
      GetNewDesignation(WorldID,Persona,RCap,NewTyp);
      IF NewTyp<>GetType(WorldID) THEN
         DesignateWorld(WorldID,NewTyp);

      GetCoord(WorldID,WorldXY);
      GetRegionalCapital(WorldID,RCap,BaseID);
      GetCoord(BaseID,BaseXY);
      END;
   END;  { NPEConquest }

PROCEDURE ExplorationAndProbing(Emp: Empire;
                                VAR RCap: RegionCapitalArray; 
                                VAR Persona: NPECharacterRecord);
{ ExplorationAndProbing: -------------------------------------------------------
   This procedure will launch a probe from each regional capital to a random
   sector within a certain distance. The distance is proportional to the
   SphereX factor in Persona. The procedure will launch all available probes,
   so the routine should be called after other probes are launched.
------------------------------------------------------------------------------ }
   VAR
      i,MaxProbeDist,PNum: Word;
      x,y: Integer;
      BaseXY,XY: XYCoord;
      NoMoreProbes: Boolean;

   BEGIN
   {$IFDEF Debug}
   WriteLn('   Probing.');
   {$ENDIF}

   MaxProbeDist:=24-(2*ISqrt(Persona.SphereX));

   NoMoreProbes:=False;
   REPEAT
      FOR i:=1 TO MaxNoOfRegions DO
         IF NOT SameID(RCap[i],EmptyQuadrant) THEN
            BEGIN
            GetCoord(RCap[i],BaseXY);
            x:=Rnd(Integer(BaseXY.x)-MaxProbeDist,Integer(BaseXY.x)+MaxProbeDist);
            y:=Rnd(Integer(BaseXY.y)-MaxProbeDist,Integer(BaseXY.y)+MaxProbeDist);
            IF InGalaxy(x,y) THEN
               BEGIN
               XY.x:=x;
               XY.y:=y;
               GetProbe(Emp,PNum);
               IF PNum<>0 THEN
                  LaunchProbe(Emp,PNum,XY)
               ELSE
                  NoMoreProbes:=True;
               END;
            END;
   UNTIL NoMoreProbes;
   END;  { ExplorationAndProbing }

END.
