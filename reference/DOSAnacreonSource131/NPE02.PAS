(* NPE02.PAS -------------------------------------------------------------------

   Kingdom

----------------------------------------------------------------------------- *)

UNIT NPE02;

{DEFINE Debug}

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
     Intrface,
     News,
     Fleet,
     NPE00,
     Attack;

PROCEDURE CleanUpKingdomNPE(VAR DataPtr: Pointer);
PROCEDURE InitializeKingdom1NPE(Emp: Empire; VAR DataPtr: Pointer);
PROCEDURE InitializeKingdom2NPE(Emp: Empire; VAR DataPtr: Pointer);
PROCEDURE ImplementKingdom1NPE(Emp: Empire; DataPtr: Pointer);
PROCEDURE LoadKingdom1NPE(VAR GameFile: FILE; Emp: Empire; VAR DataPtr: Pointer; Version: Word; VAR Error: Word);
PROCEDURE SaveKingdom1NPE(VAR GameFile: FILE; VAR DataPtr: Pointer; VAR Error: Word);

IMPLEMENTATION

{$IFDEF Debug}
USES
   Wnd;

VAR
   DebugWind: WindowHandle;
{$ENDIF}

PROCEDURE SaveKingdom1NPE(VAR GameFile: FILE; VAR DataPtr: Pointer; VAR Error: Word);
   VAR
      Data: Kingdom1DataPtr;

   BEGIN
   Data:=Kingdom1DataPtr(DataPtr);
   {$I-}
   BlockWrite(GameFile,Data^,SizeOf(Data^));
   {$I+}
   Error:=IOResult;
   END;  { SaveKingdom1NPE }

PROCEDURE LoadKingdom1NPE(VAR GameFile: FILE; Emp: Empire; VAR DataPtr: Pointer; Version: Word; VAR Error: Word);
   VAR
      Data: Kingdom1DataPtr;
      i,FirstFleet: Word;

   BEGIN
   New(Data);
   DataPtr:=Data;

   IF Version<12 THEN
      BEGIN
      {$I-}
      FirstFleet:=25*(Ord(Emp)-Ord(Empire1));
      FOR i:=1 TO 25 DO
         BEGIN
         BlockRead(GameFile,Data^.FleetData[i],8);
         Data^.FleetData[i].Index:=i+FirstFleet;
         END;

      BlockRead(GameFile,Data^.State,SizeOf(Data^.State));
      BlockRead(GameFile,Data^.Persona,SizeOf(Data^.Persona));
      {$I+}
      END
   ELSE
      BEGIN
      {$I-}
      BlockRead(GameFile,Data^,SizeOf(Data^));
      {$I+}
      END;

   Error:=IOResult;

   IF Error<>0 THEN
      Dispose(Data);
   END;  { SaveKingdom1NPE }

PROCEDURE InitializeKingdom1NPE(Emp: Empire; VAR DataPtr: Pointer);
   VAR
      Data: Kingdom1DataPtr;
      EmpI: Empire;

   BEGIN
   New(Data);
   DataPtr:=Data;
   WITH Data^ DO
      BEGIN
      FillChar(FleetData,SizeOf(FleetData),0);
      FOR EmpI:=Empire1 TO Empire8 DO
         WITH State[EmpI] DO
            BEGIN
            Policy:=NeutralPLT;
            AttackChance:=50;
            Aggressiveness:=0;
				Balance:=0;
            END;

      WITH Persona DO
         BEGIN
         ImpGene:=Rnd(1,5);
         DefGene:=Rnd(50,75);
         OffGene:=Rnd(1,2);
         FactorGene:=15;
         RandomGene:=50;

         Defensive:=DefGene;
         Offensive:=OffGene;
         Techno:=50;
         Provoke:=75;
         Imperialist:=ImpGene;
         WorldPower:=Rnd(25,75);
         Honorable:=50;
         SphereX:=Rnd(25,75);

         Clock:=0;
         Offset:=Rnd(1,10);
         END;
      END;

	SetEmpireDefenses(Emp);
   END;  { InitializeKingdom1NPE }

PROCEDURE InitializeKingdom2NPE(Emp: Empire; VAR DataPtr: Pointer);
   VAR
      Data: Kingdom1DataPtr;
      EmpI: Empire;

   BEGIN
   New(Data);
   DataPtr:=Data;
   WITH Data^ DO
      BEGIN
      FillChar(FleetData,SizeOf(FleetData),0);
      FOR EmpI:=Empire1 TO Empire8 DO
         WITH State[EmpI] DO
            BEGIN
            Policy:=HarassPLT;
            AttackChance:=50;
            Aggressiveness:=0;
				Balance:=0;
            END;

      WITH Persona DO
         BEGIN
         ImpGene:=Rnd(50,100);
         DefGene:=Rnd(5,10);
         OffGene:=Rnd(50,100);
         FactorGene:=25;
         RandomGene:=50;

         Defensive:=DefGene;
         Offensive:=OffGene;
         Techno:=50;
         Provoke:=Rnd(50,100);
         Imperialist:=ImpGene;
         WorldPower:=Rnd(25,75);
         Honorable:=50;
         SphereX:=Rnd(25,100);

         Clock:=0;
         Offset:=Rnd(1,10);
         END;
      END;

	SetEmpireDefenses(Emp);
   END;  { InitializeKingdom2NPE }

PROCEDURE UpdateFleets(Emp: Empire; VAR RCap: RegionCapitalArray;
                       VAR Persona: NPECharacterRecord;
                       Data: Kingdom1DataPtr);
   VAR
      i: Word;
      FltID: IDNumber;
      Result: AttackResultTypes;
      TargID,BaseID: IDNumber;
      CurrentMission: MissionTypes;
      NewTyp: WorldTypes;
		EnemyEmp: Empire;

   BEGIN
   FltID.ObjTyp:=Flt;
   FOR i:=1 TO NoOfFleetsPerEmpire DO
      WITH Data^ DO
         BEGIN
         FltID.Index:=FleetData[i].Index;
         IF (FltID.Index>0) AND (FltID.Index IN SetOfActiveFleets) THEN
            BEGIN
            MidCourseCorrection(Emp,FltID,RCap,FleetData);

            IF GetFleetStatus(FltID)=FReady THEN
               BEGIN
               GetRegionalCapital(FltID,RCap,BaseID);

               WITH FleetData[i] DO
                  BEGIN
                  CurrentMission:=Mission;
                  TargID:=TargetID;
                  END;

               CASE CurrentMission OF
                      ReturnMSN: ImplementReturnMSN(FltID,TargID);
                      RefuelMSN: ImplementRefuelMSN(FltID,TargID);
                       GuardMSN: ImplementGuardMSN(FltID,TargID);
                       StackMSN: ImplementStackMSN(FltID,FleetData);
                     RaidTrnMSN: ImplementRaidTrnMSN(Emp,FltID,TargID,BaseID,FleetData);
                      SupplyMSN: ImplementSupplyMSN(Emp,FltID,TargID,BaseID,FleetData);
                   SupplyTrnMSN: ImplementReturnMSN(FltID,TargID);
                  ConquerMSN: BEGIN
						   EnemyEmp:=GetStatus(TargID);
                     ImplementConquerMSN(Emp,FltID,TargID,BaseID,FleetData,Result);
							IF Result=DefConqueredART THEN
								BEGIN
	                     NPEConquest(Emp,TargID,Result,RCap,Persona);
								Inc(State[EnemyEmp].Balance);
								END;
                     END;
						SlowAttackMSN,
                  JumpAttackMSN: BEGIN
						   EnemyEmp:=GetStatus(TargID);
                     ImplementJumpAttackMSN(Emp,FltID,TargID,BaseID,FleetData,Result);
							IF Result=DefConqueredART THEN
								BEGIN
	                     NPEConquest(Emp,TargID,Result,RCap,Persona);
								Inc(State[EnemyEmp].Balance);

								{ leave fleet as garrison }
								AbortFleet(FltID,TargID,True);
								DestroyFleet(FltID);
								END
							ELSE IF Result=NoART THEN
								BEGIN
						      SetRaidingFleetNewTarget(Emp,FltID,TargID,BaseID,FleetData,Persona);
								END
							ELSE
								BEGIN
								SetFleetReturn (Emp, FltID, BaseID, FleetData);
								END;
                     END;
                  ELSE
                     DestroyFleet(FltID);
                  END;  { case }
               END;
            END;
         END;
   END;  { UpdateFleets }

PROCEDURE ImplementKingdom1NPE(Emp: Empire; DataPtr: Pointer);
   VAR
      Data: Kingdom1DataPtr;
      RCap: RegionCapitalArray;

		{$IFDEF Debug}
		EmpI: Empire;
		{$ENDIF}

   BEGIN
   Data:=Kingdom1DataPtr(DataPtr);
   WITH Data^ DO
      BEGIN
      EnforceNPEDataLinks(Emp,FleetData);
      IF Persona.Clock=0 THEN
         { Initialize things first year }
         BEGIN
         StateDeptReport(Emp,State);
         END;

      CreateRegionArray(Emp,RCap);

      UpdateFleets(Emp,RCap,Persona,Data);
      ReviewNews(Emp,FleetData,RCap,Persona,State);

      { Wars and foreign affairs }
      StateDepartment(Emp,Persona,State);
      WarCabinet(Emp,RCap,FleetData,Persona,State);

      { Internal Affairs }
      DefendEmpire(Emp,RCap,FleetData,Persona);
      ImperialExpansion(Emp,RCap,FleetData,Persona);
      IF ((Persona.Clock+Persona.Offset) MOD 7)=0 THEN
         BEGIN
         StateDeptReport(Emp,State);
         ReDesignateEmpire(Emp,RCap,Persona);
         END;
      ExplorationAndProbing(Emp,RCap,Persona);

      Inc(Persona.Clock);

		{$IFDEF Debug}
	   OpenWindow(40,1,40,15,ThinBRD,'Debug',7,7,DebugWind);
		WriteLn('Balance for each empire.');
		WriteLn;
		FOR EmpI:=Empire1 TO Empire8 DO
			IF EmpireActive(EmpI) THEN
				WriteLn(EmpireName(EmpI):16,': ',State[EmpI].Balance);

		ReadLn;
		CloseWindow;
   	{$ENDIF}
      END;
   END;  { ImplementKingdom1NPE }

PROCEDURE CleanUpKingdomNPE(VAR DataPtr: Pointer);
   VAR
      Data: Kingdom1DataPtr;

   BEGIN
   Data:=Kingdom1DataPtr(DataPtr);
   Dispose(Data);
   END;  { CleanUpPirateNPE }

END.
