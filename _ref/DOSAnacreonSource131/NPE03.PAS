(* NPE03.PAS -------------------------------------------------------------------

   Guardian Empires.

----------------------------------------------------------------------------- *)

UNIT NPE03;

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

PROCEDURE CleanUpGuardianNPE(VAR DataPtr: Pointer);
PROCEDURE InitializeGuardianNPE(Emp: Empire; VAR DataPtr: Pointer);
PROCEDURE ImplementGuardianNPE(Emp: Empire; DataPtr: Pointer);
PROCEDURE LoadGuardianNPE(VAR GameFile: FILE; Emp: Empire; VAR DataPtr: Pointer; Version: Word; VAR Error: Word);
PROCEDURE SaveGuardianNPE(VAR GameFile: FILE; VAR DataPtr: Pointer; VAR Error: Word);

IMPLEMENTATION

PROCEDURE GRDNLAMAttack (Emp: Empire; Data: GuardianDataPtr; BaseID: IDNumber);
	VAR
		Defns: DefnsArray;
		LAMsToUse, NoOfLAMs: Resources;
		FltID: IDNumber;
		Priority: ARRAY [1..MaxNoOfFleets] OF Word;
		FltSh, DestSh: ShipArray;
		FltDf, DestDf: DefnsArray;
		FltXY, BaseXY: XYCoord;
		NoMoreTargets: Boolean;
		BestTarget, BestPriority, i: Word;

	BEGIN
	GetDefns (BaseID, Defns);
	NoOfLAMs := Defns[LAM];
	GetCoord (BaseID, BaseXY);

	{ Fill priority array with targets }

	FillChar (Priority, SizeOf(Priority), 0);
	FillChar (FltDf, SizeOf(FltDf), 0);
	FltID.ObjTyp := Flt;
	FOR i:=1 TO MaxNoOfFleets DO
		IF i IN SetOfActiveFleets THEN
			BEGIN
			FltID.Index := i;
			GetCoord (FltID, FltXY);
			IF Known (Emp, FltID) 
				AND (Distance (FltXY, BaseXY) <=5) 
				AND (GetStatus (FltID) <> Emp) THEN
				BEGIN
				GetShips (FltID, FltSh);
				Priority[i] := LesserInt (MaxInt, GreaterInt (1, MilitaryPower (FltSh, FltDf) DIV 1000));
				END;
			END;

	{ Attack all fleets in range in order of priority }

	REPEAT
		{ Find best fleet to attack }
		BestPriority := 0;
		FOR i:=1 TO MaxNoOfFleets DO
			IF (Priority[i] > BestPriority) THEN
				BEGIN
				BestPriority := Priority[i];
				BestTarget := i;
				END;

		{ Attack fleet }
		IF (BestPriority>0) THEN
			BEGIN
			FltID.Index := BestTarget;
			LAMsToUse := LesserInt (NoOfLAMs, LongInt(Rnd(75,150))*BestPriority);
			LAMAttack (Emp, LAMsToUse, FltID, DestSh, DestDf);
			Priority[BestTarget] := 0;
			Dec (NoOfLAMs, LAMsToUse);
			END;
	UNTIL (BestPriority=0) OR (NoOfLAMs = 0);

	Defns[LAM] := NoOfLAMs;
	PutDefns (BaseID, Defns);
	END;  { GRDNLAMAttack }

PROCEDURE GRDNUpdateBases (Emp: Empire; Data: GuardianDataPtr);
	VAR
		BaseID: IDNumber;
		i: Word;
		Defns: DefnsArray;

	BEGIN
	BaseID.ObjTyp:=Pln;
	FOR i:=1 TO NoOfPlanets DO
		IF i IN SetOfPlanetsOf[Emp] THEN
			BEGIN
			BaseID.Index := i;

			GetDefns (BaseID, Defns);
			IF Defns[LAM]>0 THEN
				GRDNLAMAttack (Emp, Data, BaseID);
			END;

	BaseID.ObjTyp:=Base;
	FOR i:=1 TO MaxNoOfStarbases DO
		IF i IN SetOfStarbasesOf[Emp] THEN
			BEGIN
			BaseID.Index := i;

			GetDefns (BaseID, Defns);
			IF Defns[LAM]>0 THEN
				GRDNLAMAttack (Emp, Data, BaseID);
			END;
	END;  { GRDNUpdateBases }

PROCEDURE SaveGuardianNPE(VAR GameFile: FILE; VAR DataPtr: Pointer; VAR Error: Word);
   VAR
      Data: GuardianDataPtr;

   BEGIN
   Data:=GuardianDataPtr(DataPtr);
   {$I-}
   BlockWrite(GameFile,Data^,SizeOf(Data^));
   {$I+}
   Error:=IOResult;
   END;  { SaveGuardianNPE }

PROCEDURE LoadGuardianNPE(VAR GameFile: FILE; Emp: Empire; VAR DataPtr: Pointer; Version: Word; VAR Error: Word);
   VAR
      Data: GuardianDataPtr;

   BEGIN
   New(Data);
   DataPtr:=Data;

   {$I-}
   BlockRead(GameFile,Data^,SizeOf(Data^));
   {$I+}

   Error:=IOResult;

   IF Error<>0 THEN
      Dispose(Data);
   END;  { LoadGuardianNPE }

PROCEDURE InitializeGuardianNPE(Emp: Empire; VAR DataPtr: Pointer);
   VAR
      Data: GuardianDataPtr;
      EmpI: Empire;
		BaseID: IDNumber;
		i: Word;
		STyp: StarbaseTypes;

   BEGIN
   New(Data);
   DataPtr:=Data;

	SetEmpireDefenses(Emp);
   END;  { InitializeGuardianNPE }

PROCEDURE ImplementGuardianNPE(Emp: Empire; DataPtr: Pointer);
   VAR
      Data: GuardianDataPtr;

   BEGIN
   Data:=GuardianDataPtr(DataPtr);
   WITH Data^ DO
      BEGIN
		GRDNUpdateBases (Emp, Data);
      END;
   END;  { ImplementGuardianNPE }

PROCEDURE CleanUpGuardianNPE(VAR DataPtr: Pointer);
   VAR
      Data: GuardianDataPtr;

   BEGIN
   Data:=GuardianDataPtr(DataPtr);
   Dispose(Data);
   END;  { CleanUpGuardianNPE }

END.

