(* NPE04.PAS -------------------------------------------------------------------

   Bersker Empires.

----------------------------------------------------------------------------- *)

UNIT NPE04;

{$IFDEF Overlay}
{$F+,O+}
{$ENDIF}

INTERFACE

USES CRT,
     Int,
     Types,
	  Galaxy,
     DataCnst,
     NPETypes,
     NPEIntr,
     Misc,
     Primintr,
     News,
     Fleet,
     Attack;

PROCEDURE CleanUpBerserkerNPE(VAR DataPtr: Pointer);
PROCEDURE InitializeBerserkerNPE(Emp: Empire; VAR DataPtr: Pointer);
PROCEDURE ImplementBerserkerNPE(Emp: Empire; DataPtr: Pointer);
PROCEDURE LoadBerserkerNPE(VAR GameFile: FILE; Emp: Empire; VAR DataPtr: Pointer; Version: Word; VAR Error: Word);
PROCEDURE SaveBerserkerNPE(VAR GameFile: FILE; VAR DataPtr: Pointer; VAR Error: Word);

IMPLEMENTATION

CONST
	MinBasePower = 100000;			{ If base power is below this, need to resupply }
	MaxDistanceToHome = 10;			{ Do not go home if not at least this close }

PROCEDURE GetNearestBSRKBase (Emp: Empire; FltID: IDNumber; VAR BaseID: IDNumber);
	VAR
		i: Word;
		TestID: IDNumber;
		STyp: StarbaseTypes;
		BestDistance: Word;
		FltXY, TestXY: XYCoord;

	BEGIN
	TestID.ObjTyp := Base;
	GetCoord (FltID, FltXY);
	BestDistance := 1000;

	FOR i:=1 TO MaxNoOfStarbases DO
		IF (i IN SetOfStarbasesOf[Emp]) THEN
			BEGIN
			TestID.Index := i;
			STyp := GetBaseType (TestID);

			IF (STyp = cmm) OR (STyp = frt) THEN
				BEGIN
				GetCoord (TestID, TestXY);
				IF Distance (FltXY, TestXY) < BestDistance THEN
					BEGIN
					BestDistance := Distance (FltXY, TestXY);
					BaseID := TestID;
					END;
				END;
			END;

	IF BestDistance = 1000 THEN
		GetCapital (Emp, BaseID);
	END;  { GetNearestBSRKBase }

PROCEDURE NewBSRKBaseTarget (Emp: Empire; BaseID: IDNumber; VAR BaseData: BaseDataArray);
	VAR
		i, BestDistance: Word;
		BestTargetID,TestID: IDNumber;
		TestXY, BestTargetXY, BaseXY: XYCoord;
		TestCr: CargoArray;
		FltSh, TestSh: ShipArray;
		FltDf, TestDf: DefnsArray;
		BasePower: LongInt;
		BaseLAMs: Word;

	BEGIN
	TestID.ObjTyp:=Pln;
	BestDistance:=1000;
	GetCoord (BaseID, BaseXY);
	GetShips (BaseID, FltSh);
	GetDefns (BaseID, FltDf);
	BaseLAMs := FltDf[LAM];
	FillChar (FltDf, SizeOf(FltDf), 0);
	BasePower := MilitaryPower (FltSh, FltDf);

	IF (BasePower < MinBasePower) THEN
		BEGIN
		BaseData[BaseID.Index].Mission := FindHomeBMS;
		END
	ELSE
		BEGIN
		FOR i:=1 TO NoOfPlanets DO
			BEGIN
			TestID.Index:=i;
			IF (GetStatus (TestID) <> Emp) THEN
				BEGIN
				GetCargo (TestID, TestCr);
				IF (LongInt(TestCr[met])+LongInt(TestCr[che]) > 2000)
						OR (TestCr[tri] > 1000) THEN
					BEGIN
					GetShips (TestID, TestSh);
					IF (BaseLAMs < 4000) THEN
						GetDefns (TestID, TestDf)
					ELSE
						FillChar (TestDf, SizeOf(TestDf), 0);

					IF (BasePower + Round(BasePower * Random))
						> MilitaryPower (TestSh, TestDf) THEN
						BEGIN
						GetCoord (TestID, TestXY);
						IF Distance (TestXY, BaseXY) < BestDistance THEN
							BEGIN
							BestDistance := Distance (TestXY, BaseXY);
							BestTargetID := TestID;
							BestTargetXY := TestXY;
							END;
						END;
					END;
				END;
			END;

		IF BestDistance<1000 THEN
			BEGIN
			BaseData[BaseID.Index].Mission:=AttackBMS;
			BaseData[BaseID.Index].TargetID:=BestTargetID;
			SetFleetDestination (BaseID, BestTargetXY);
			END
		ELSE
			BEGIN
			{ If no targets found, wander around randomly }
			BaseData[BaseID.Index].Mission:=WanderAroundBMS;
			BaseData[BaseID.Index].Count:=0;

			TestXY.x := Rnd (1, SizeOfGalaxy);
			TestXY.y := Rnd (1, SizeOfGalaxy);
			SetFleetDestination (BaseID, TestXY);
			END;
		END;
	END;  { NewBSRKBaseTarget }

PROCEDURE BSRKDestroyWorld (Emp: Empire; TargetID: IDNumber);
	VAR
		Pop: Population;
		Deaths: Word;
		Loc: Location;
		Indus: IndusArray;
		Ind: IndusTypes;
		IndLost: Word;
		NewTech: TechLevel;
		Owner: Empire;

	BEGIN
	Owner := GetStatus (TargetID);
	Loc.ID := TargetID;
	Loc.XY := Limbo;
	AddNews (Owner, WHolo, Loc, 0, 0, 0);

	{	Kill population }
	Pop := GetPopulation (TargetID);
	Deaths := LesserInt (Pop - 10, GreaterInt ((Pop DIV 2), 100+Rnd(1,100)));
	SetPopulation (TargetID, Pop - Deaths);
	AddNews (Owner, DthHolo, Loc, Deaths, 0, 0);

	{	Destroy industry }
	GetIndus (TargetID, Indus);
	FOR Ind:=BioInd TO TriInd DO
		BEGIN
		IF (Rnd(1,2) = 1) THEN
			IndLost := GreaterInt (Indus[Ind], Indus[Ind] - Rnd(10,50))
		ELSE
			IndLost := Indus[Ind] DIV 2;

		IF (IndLost > 0) THEN
			BEGIN
			Indus[Ind] := Indus[Ind] - IndLost;
			AddNews (Owner, IndDs, Loc, IndLost, Integer(Ind), 0);
			END;
		END;
	PutIndus (TargetID, Indus);

	{	Revert technology }
	NewTech := TechLevel (Rnd (0, 2));
	SetTech (TargetID, NewTech);
	AddNews (Owner, RTech, Loc, Integer(NewTech), 0, 0);
	END;  { BSRKDestroyWorld }

PROCEDURE BSRKCourseCorrection (Emp: Empire; FltID: IDNumber; VAR FleetData: FleetDataArray);
	VAR
		FltXY, BaseXY: XYCoord;

	BEGIN
	WITH FleetData[NPEDataIndex(FltID)] DO
		BEGIN
		IF (Mission=BSRKReturnMSN) THEN
			BEGIN
			GetCoord (TargetID, BaseXY);
			SetFleetDestination (FltID, BaseXY);
			END;
		END;
	END;  { BSRKCourseCorrection }

PROCEDURE GetPowerToUse(TargetID: IDNumber; VAR FleetPower,FleetGAT: LongInt);
	VAR
		Ships: ShipArray;
		Cargo: CargoArray;
		Defns: DefnsArray;
		TargetDefense, TargetMen: LongInt;

   BEGIN
	GetShips (TargetID, Ships);
	GetCargo (TargetID, Cargo);
	GetDefns (TargetID, Defns);

	TargetDefense := MilitaryPower (Ships, Defns);
	TargetMen := LongInt(Cargo[men]) + 4*Cargo[nnj]+10;

   FleetPower := LongInt(50000) 
			+ (LongInt(Rnd(2,5)) * TargetDefense)
			+ (TargetDefense DIV Rnd (2,25));
   FleetGAT := LongInt(2)*TargetMen;
   END;  { GetPowerToUse }

PROCEDURE DeployBSRKAttackMSN (Emp: Empire; BaseID, TargetID: IDNumber; VAR FleetData: FleetDataArray);
	VAR
		FleetPower, FleetGAT: LongInt;

	BEGIN
	GetPowerToUse (TargetID, FleetPower, FleetGAT);
	DeployBattleFleet (Emp, FleetData, BaseID, FleetPower, FleetGAT, BSRKAttackMSN, TargetID);
	END;  { DeployBSRKAttackMSN }

PROCEDURE ImplementBSRKReturnMSN (Emp: Empire; FltID, BaseID: IDNumber; Data: BerserkerDataPtr);
	BEGIN
	IF (Emp = GetStatus (BaseID)) THEN
		BEGIN
		AbortFleet (FltID, BaseID,True);
		DestroyFleet (FltID);

		NewBSRKBaseTarget (Emp, BaseID, Data^.BaseData);
		END
	ELSE
		DestroyFleet (FltID);
	END;  { ImplementBSRKReturnMSN }

PROCEDURE ImplementBSRKAttackMSN (Emp: Empire; FltID, BaseID, TargetID: IDNumber;
											 Data: BerserkerDataPtr);
	VAR
      Result: AttackResultTypes;
		BaseXY: XYCoord;

	BEGIN
	ImplementJumpAttackMSN (Emp, FltID, TargetID, BaseID, Data^.FleetData, Result);

	IF Result=DefConqueredART THEN
		BEGIN
		PlunderWorld (Emp, FltID, TargetID);
		IF Rnd(1,3) = 1 THEN
			BSRKDestroyWorld (Emp, TargetID);
		END;

	IF Result=AttDestroyedART THEN
		BEGIN
		NewBSRKBaseTarget (Emp, BaseID, Data^.BaseData);
		END
	ELSE
		BEGIN
		GetCoord (BaseID, BaseXY);
		WITH Data^.FleetData[NPEDataIndex(FltID)] DO
			BEGIN
      	Mission:=BSRKReturnMSN;
  	   	TargetID:=BaseID;
     		SetFleetDestination(FltID,BaseXY);
			END;
		END;
	END;  { ImplementBSRKAttackMSN }

PROCEDURE ImplementAttackBMS(Emp: Empire; BaseID: IDNumber; RCap: RegionCapitalArray; Data: BerserkerDataPtr);
	VAR
		STyp: StarbaseTypes;
		BaseXY, TargetXY: XYCoord;
		Dist: Word;

	BEGIN
	WITH Data^.BaseData[BaseID.Index] DO
		BEGIN
		STyp:=GetBaseType (BaseID);
		GetCoord (BaseID, BaseXY);
		GetCoord (TargetID, TargetXY);
		Dist := Distance (BaseXY, TargetXY);

		IF ((STyp=frt) AND (Dist <= 5)) OR (Dist = 1) THEN
			BEGIN
			DeployBSRKAttackMSN (Emp, BaseID, TargetID, Data^.FleetData);
			Mission := WaitForAttackBMS;
			Count := 0;
			END;
		END;
	END;  { ImplementAttackBMS }

PROCEDURE ImplementDefendBMS(Emp: Empire; BaseID: IDNumber; RCap: RegionCapitalArray; VAR BaseData: BaseDataArray);
	BEGIN
	END;  { ImplementDefendBMS }

PROCEDURE ImplementFindHomeBMS (Emp: Empire; BaseID: IDNumber; 
										  VAR RCap: RegionCapitalArray; VAR BaseData: BaseDataArray);
	VAR
		i, BestDistance: Word;
		TestXY, BaseXY, HomeXY: XYCoord;
		BestHomeID: IDNumber;

	BEGIN
	BestDistance := 1000;
	GetCoord (BaseID, BaseXY);

	FOR i:=1 TO MaxNoOfRegions DO
		IF NOT SameID (RCap[i],EmptyQuadrant) THEN
			BEGIN
			GetCoord (RCap[i], HomeXY);
			IF Distance (HomeXY, BaseXY) < BestDistance THEN
				BEGIN
				BestDistance := Distance (HomeXY, BaseXY);
				BestHomeID := RCap[i];
				END;
			END;

	IF (BestDistance <= MaxDistanceToHome) THEN
		BEGIN
		BaseData[BaseID.Index].Mission:=RefuelBMS;
		BaseData[BaseID.Index].TargetID:=BestHomeID;
		GetCoord (BestHomeID, TestXY);
		SetFleetDestination (BaseID, TestXY);
		END
	ELSE
		BEGIN
		BaseData[BaseID.Index].Mission:=WanderAroundBMS;
		BaseData[BaseID.Index].Count:=0;

		TestXY.x := Rnd (1, SizeOfGalaxy);
		TestXY.y := Rnd (1, SizeOfGalaxy);
		SetFleetDestination (BaseID, TestXY);
		END;
	END;  { ImplementFindHomeBMS }

PROCEDURE ImplementRefuelBMS(Emp: Empire; BaseID: IDNumber; 
									  VAR RCap: RegionCapitalArray; VAR Data: BerserkerDataPtr);
	VAR
		BaseXY, TargetXY: XYCoord;

	BEGIN
	WITH Data^.BaseData[BaseID.Index] DO
		BEGIN
		GetCoord (BaseID, BaseXY);
		GetCoord (TargetID, TargetXY);

		IF (Distance (BaseXY, TargetXY) < 5) 
			AND (GetStatus (TargetID) = Emp) THEN
			BEGIN
			DeployBattleFleet (Emp, Data^.FleetData, TargetID, 9000000, 9000000, BSRKReturnMSN, BaseID);
			Mission := WaitForAttackBMS;
			Count := 0;
			END;
		END;
	END;  { ImplementRefuelBMS }

PROCEDURE ImplementWaitForAttackBMS(Emp: Empire; BaseID: IDNumber;
												RCap: RegionCapitalArray; VAR BaseData: BaseDataArray);
	VAR
		BaseXY: XYCoord;

	BEGIN
	WITH BaseData[BaseID.Index] DO
		BEGIN
		IF (Count > 10) THEN
			BEGIN
			NewBSRKBaseTarget (Emp, BaseID, BaseData);
			END
		ELSE IF (Count = 3) THEN
			BEGIN
			GetCoord (BaseID, BaseXY);
			SetFleetDestination (BaseID, BaseXY);
			Inc(Count);
			END
		ELSE
			Inc(Count);
		END;
	END;  { ImplementWaitForAttackBMS }

PROCEDURE ImplementWanderAroundBMS (Emp: Empire; BaseID: IDNumber; VAR BaseData: BaseDataArray);
	BEGIN
	WITH BaseData[BaseID.Index] DO
		BEGIN
		IF (Count > 10) THEN
			BEGIN
			NewBSRKBaseTarget (Emp, BaseID, BaseData);
			END
		ELSE
			Inc (Count);
		END;
	END;  { ImplementWanderAroundBMS }

PROCEDURE BSRKReviewNews(Emp: Empire; Data: BerserkerDataPtr);
	VAR
		News: NewsRecordPtr;

	PROCEDURE BaseIsBlocked (BaseID: IDNumber);
		VAR
			BaseXY, TargetXY, TestXY: XYCoord;

		BEGIN
		WITH Data^.BaseData[BaseID.Index] DO
			BEGIN
			GetCoord (BaseID, BaseXY);
			GetCoord (TargetID, TargetXY);

			CASE Mission OF
				AttackBMS: BEGIN
					IF Distance (BaseXY, TargetXY) < 8 THEN
						BEGIN
						DeployBSRKAttackMSN (Emp, BaseID, TargetID, Data^.FleetData);
						Mission := WaitForAttackBMS;
						Count := 0;
						END
					ELSE
						BEGIN
						Mission := WanderAroundBMS;
						Count := 0;

						TestXY.x := Rnd (1, SizeOfGalaxy);
						TestXY.y := Rnd (1, SizeOfGalaxy);
						SetFleetDestination (BaseID, TestXY);
						END;
					END;

				DefendBMS: BEGIN
					END;

				RefuelBMS: BEGIN
					Mission := WanderAroundBMS;
					Count := 0;

					TestXY.x := Rnd (1, SizeOfGalaxy);
					TestXY.y := Rnd (1, SizeOfGalaxy);
					SetFleetDestination (BaseID, TestXY);
					END;

				WaitForAttackBMS: BEGIN
					SetFleetDestination (BaseID, BaseXY);
					END;

				WanderAroundBMS: BEGIN
					TestXY.x := Rnd (1, SizeOfGalaxy);
					TestXY.y := Rnd (1, SizeOfGalaxy);
					SetFleetDestination (BaseID, TestXY);
					END;
			END;  { case }
			END;
		END;  { BaseIsBlocked }

	BEGIN
   GetNewsList (Emp, News);
	WHILE News<>Nil DO
		WITH News^ DO
			BEGIN
			CASE Headline OF
				BseBlocked: BaseIsBlocked (Loc1.ID);
			END;  { case }

	      News := Next;
			END;
	END;  { BSRKReviewNews }

PROCEDURE UpdateBases(Emp: Empire; VAR RCap: RegionCapitalArray; Data: BerserkerDataPtr);
	VAR
		i: Word;
		BaseID: IDNumber;
		STyp: StarbaseTypes;

	BEGIN
	BaseID.ObjTyp:=Base;
	FOR i:=1 TO MaxNoOfStarbases DO
		IF i IN SetOfStarbasesOf[Emp] THEN
			WITH Data^.BaseData[i] DO
				BEGIN
				BaseID.Index:=i;
				STyp:=GetBaseType(BaseID);
				IF (STyp=cmm) OR (STyp=frt) THEN
					BEGIN
					CASE Mission OF
						AttackBMS: ImplementAttackBMS(Emp,BaseID,RCap,Data);
						DefendBMS: ImplementDefendBMS(Emp,BaseID,RCap,Data^.BaseData);
						FindHomeBMS: ImplementFindHomeBMS (Emp, BaseID, RCap, Data^.BaseData);
						RefuelBMS: ImplementRefuelBMS(Emp,BaseID,RCap,Data);
						WaitForAttackBMS: ImplementWaitForAttackBMS (Emp, BaseID, RCap, Data^.BaseData);
						WanderAroundBMS: ImplementWanderAroundBMS (Emp, BaseID, Data^.BaseData);
					ELSE
						NewBSRKBaseTarget (Emp, BaseID, Data^.BaseData);
					END;  { case }
					END;
				END;
	END;  { UpdateBases }

PROCEDURE UpdateFleets(Emp: Empire; VAR RCap: RegionCapitalArray; Data: BerserkerDataPtr);
   VAR
      FirstFleet,i: Word;
      FltID, BaseID: IDNumber;

   BEGIN
   FltID.ObjTyp:=Flt;
   FOR i:=1 TO NoOfFleetsPerEmpire DO
		BEGIN
		FltID.Index:=Data^.FleetData[i].Index;
      IF (FltID.Index>0) AND (FltID.Index IN SetOfActiveFleets) THEN
         BEGIN
			BSRKCourseCorrection (Emp, FltID, Data^.FleetData);
         IF GetFleetStatus(FltID)=FReady THEN
            WITH Data^.FleetData[i] DO
               BEGIN
               CASE Mission OF
                  BSRKReturnMSN: ImplementBSRKReturnMSN (Emp, FltID, TargetID, Data);
						BSRKAttackMSN: ImplementBSRKAttackMSN (Emp, FltID, HomeBaseID, TargetID, Data);
               ELSE
                  DestroyFleet(FltID);
                  END;  { case }
               END;
         END;
		END;
   END;  { UpdateFleets }

PROCEDURE SaveBerserkerNPE(VAR GameFile: FILE; VAR DataPtr: Pointer; VAR Error: Word);
   VAR
      Data: BerserkerDataPtr;

   BEGIN
   Data:=BerserkerDataPtr(DataPtr);
   {$I-}
   BlockWrite(GameFile,Data^,SizeOf(Data^));
   {$I+}
   Error:=IOResult;
   END;  { SaveBerserkerNPE }

PROCEDURE LoadBerserkerNPE(VAR GameFile: FILE; Emp: Empire; VAR DataPtr: Pointer; Version: Word; VAR Error: Word);
   VAR
      Data: BerserkerDataPtr;

   BEGIN
   New(Data);
   DataPtr:=Data;

   {$I-}
   BlockRead(GameFile,Data^,SizeOf(Data^));
   {$I+}

   Error:=IOResult;

   IF Error<>0 THEN
      Dispose(Data);
   END;  { LoadBerserkerNPE }

PROCEDURE InitializeBerserkerNPE(Emp: Empire; VAR DataPtr: Pointer);
   VAR
      Data: BerserkerDataPtr;
      EmpI: Empire;
		BaseID: IDNumber;
		i: Word;
		STyp: StarbaseTypes;

   BEGIN
   New(Data);
   DataPtr:=Data;
   WITH Data^ DO
      BEGIN
      FillChar(FleetData,SizeOf(FleetData),0);
		FillChar(BaseData,SizeOf(BaseData),0);

		BaseID.ObjTyp:=Base;
		FOR i:=1 TO MaxNoOfStarbases DO
			IF i IN SetOfStarbasesOf[Emp] THEN
				BEGIN
				BaseID.Index:=i;
				STyp:=GetBaseType(BaseID);

				IF (STyp=cmm) OR (STyp=frt) THEN
					NewBSRKBaseTarget(Emp, BaseID, BaseData);
				END;
      END;

	SetEmpireDefenses(Emp);
   END;  { InitializeBerserkerNPE }

PROCEDURE ImplementBerserkerNPE(Emp: Empire; DataPtr: Pointer);
   VAR
      Data: BerserkerDataPtr;
      RCap: RegionCapitalArray;

   BEGIN
   Data:=BerserkerDataPtr(DataPtr);
   WITH Data^ DO
      BEGIN
		EnforceNPEDataLinks(Emp,FleetData);
		CreateRegionArray(Emp,RCap);

		BSRKReviewNews(Emp,Data);
		UpdateFleets(Emp,RCap,Data);
		UpdateBases(Emp,RCap,Data);
      END;
   END;  { ImplementBerserkerNPE }

PROCEDURE CleanUpBerserkerNPE(VAR DataPtr: Pointer);
   VAR
      Data: BerserkerDataPtr;

   BEGIN
   Data:=BerserkerDataPtr(DataPtr);
   Dispose(Data);
   END;  { CleanUpBerserkerNPE }

END.

