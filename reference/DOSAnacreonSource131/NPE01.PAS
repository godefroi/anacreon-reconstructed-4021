(* NPE01.PAS -------------------------------------------------------------------

   Pirates.

----------------------------------------------------------------------------- *)

UNIT NPE01;

{$IFDEF Overlay}
{$F+,O+}
{$ENDIF}

INTERFACE

USES CRT,
     Int,
     Types,
     Galaxy,
     NPETypes,
     Misc,
     Primintr,
     Intrface,
     NPEIntr,
     News,
     Fleet,
     Attack,
     AttNPE;

PROCEDURE CleanUpPirateNPE(VAR DataPtr: Pointer);
PROCEDURE InitializePirateNPE(Emp: Empire; VAR DataPtr: Pointer);
PROCEDURE ImplementPirateNPE(Emp: Empire; DataPtr: Pointer);
PROCEDURE LoadPirateNPE(VAR GameFile: FILE; Emp: Empire; VAR DataPtr: Pointer; Version: Word; VAR Error: Word);
PROCEDURE SavePirateNPE(VAR GameFile: FILE; VAR DataPtr: Pointer; VAR Error: Word);

IMPLEMENTATION

PROCEDURE FindTarget(Emp: Empire; FltID: IDNumber; VAR TargetID: IDNumber);
   VAR
      TargetXY,FltXY: XYCoord;
      SetOfTargetFleets: FleetSet;
      i: Word;
      FltSh,Sh: ShipArray;

   BEGIN
   SetOfTargetFleets:=SetOfActiveFleets - SetOfFleetsOf[Emp];
   GetCoord(FltID,FltXY);
   GetShips(FltID,FltSh);
   TargetID.ObjTyp:=Flt;
   FOR i:=1 TO MaxNoOfFleets DO
      IF i IN SetOfTargetFleets THEN
         BEGIN
         TargetID.Index:=i;
         GetCoord(TargetID,TargetXY);
         IF (Distance(TargetXY,FltXY)<=5) THEN
            BEGIN
            GetShips(TargetID,Sh);
            IF (Sh[jtn]+Sh[trn]>0) AND ((Sh[jmp]+Sh[hkr])<=(FltSh[jmp]+FltSh[hkr])) 
               AND ((Sh[pen]+Sh[ssp])<=(FltSh[hkr] DIV 2)) THEN
               BEGIN
               Exit;
               END;
            END;
         END;

   { No target found }
   TargetID:=EmptyQuadrant;
   END;  { FindTarget }

PROCEDURE FindNearestBase(Emp: Empire; XY: XYCoord; VAR BaseXY: XYCoord);
   VAR
      NearBase: IDPtr;

   BEGIN
   NearBase:=Nil;
   GetNearestWorlds(XY,1,SetOfPlanetsOf[Emp],NearBase);
   GetCoord(NearBase^.ID,BaseXY);
   DisposeIDList(NearBase);
   END;  { FindNearestBase }

PROCEDURE UpdateFleets(Emp: Empire; Data: PirateDataPtr);
   VAR
      i,j: Word;
      FltID,GroundID,SecTargID: IDNumber;
      FltXY,TargetDestXY,TargetXY,BaseXY: XYCoord;
      Result: AttackResultTypes;
      Sh,Sh2: ShipArray;
      Cr,Cr2: CargoArray;
      FleetsAtTarget: FleetSet;
      AbortMission: Boolean;

   BEGIN
   FltID.ObjTyp:=Flt;
   FOR i:=1 TO NoOfFleetsPerEmpire DO
      WITH Data^,FleetData[i] DO
         IF Index IN SetOfActiveFleets THEN
            BEGIN
            FltID.Index:=Index;
            IF GetFleetStatus(FltID)=FReady THEN
               { ASSERT: Fleet is at destination }
               BEGIN
               GetCoord(FltID,FltXY);
               CASE Mission OF
                  ReturnMSN: BEGIN
                     GetObject(FltXY,GroundID);
                     ImplementReturnMSN(FltID,GroundID);
                     END;
                  WaitForTrnMSN: BEGIN
                     FindTarget(Emp,FltID,TargetID);
                     IF TargetID.ObjTyp=Void THEN
                        { ASSERT: No target found }
                        BEGIN
                        IF Waiting=0 THEN
                           BEGIN
                           FindNearestBase(Emp,FltXY,BaseXY);
                           SetFleetDestination(FltID,BaseXY);
                           Mission:=ReturnMSN;
                           Dec(HuntingGround[BlockX,BlockY],5);
                           END
                        ELSE
                           BEGIN
                           Dec(Waiting);
                           END;
                        END
                     ELSE
                        { ASSERT: TargetID is a transport fleet in range. }
                        BEGIN
                        GetCoord(TargetID,TargetXY);
                        GetFleetDestination(TargetID,TargetDestXY);
                        GetNewPos(TargetXY,TargetDestXY);
                        SetFleetDestination(FltID,TargetXY);
                        Waiting:=Rnd(1,2);
                        Mission:=AttackTrnMSN;
                        Inc(HuntingGround[BlockX,BlockY],15);
                        END;
                     END;
                  AttackTrnMSN: BEGIN
                     {$IFDEF DebugNPE}
                     WriteLn('Attack transports');
                     {$ENDIF}
                     GetCoord(TargetID,TargetXY);
                     IF SameXY(TargetXY,FltXY)
                        AND (TargetID.Index IN SetOfActiveFleets) THEN
                        BEGIN
                        Mission:=ReturnMSN;
                        Waiting:=0;
                        FindNearestBase(Emp,FltXY,BaseXY);
                        SetFleetDestination(FltID,BaseXY);
                        NPEAttack(FltID,TargetID,CaptTrnAIT,0,Result);
                        GetShips(FltID,Sh);
                        GetCargo(FltID,Cr);
                        Sh[fgt]:=0;
                        Sh[trn]:=0;
                        BalanceFleet(Sh,Cr);
                        PutShips(FltID,Sh);
                        PutCargo(FltID,Cr);
                        END
                     ELSE
                        BEGIN
                        IF Waiting=0 THEN
                           BEGIN
                           Mission:=ReturnMSN;
                           FindNearestBase(Emp,FltXY,BaseXY);
                           SetFleetDestination(FltID,BaseXY);
                           END
                        ELSE
                           Dec(Waiting);
                        END;
                     END;
                  AttackWrldMSN: BEGIN
                     GetCoord(TargetID,TargetXY);
                     GetFleets(TargetXY,FleetsAtTarget);
                     FleetsAtTarget:=FleetsAtTarget-SetOfFleetsOf[Emp];
                     AbortMission:=False;
                     IF FleetsAtTarget<>[] THEN
                        BEGIN
                        SecTargID.ObjTyp:=Flt;
                        FOR j:=1 TO MaxNoOfFleets DO
                           IF (j IN FleetsAtTarget) AND (NOT AbortMission) THEN
                              BEGIN
                              SecTargID.Index:=j;
                              NPEAttack(FltID,SecTargID,CaptTrnAIT,0,Result);
                              IF Result<>DefConqueredART THEN
                                 AbortMission:=True;
                              END;
                        END;

                     Mission:=ReturnMSN;
                     Waiting:=0;
                     FindNearestBase(Emp,FltXY,BaseXY);
                     SetFleetDestination(FltID,BaseXY);

                     IF NOT AbortMission THEN
                        BEGIN
                        NPEAttack(FltID,TargetID,CaptTrnAIT,0,Result);
                        IF Result=DefConqueredART THEN
									PlunderWorld (Emp, FltID, TargetID);
                        END;
                     END;
               END;  { case }
               END;
            END;
   END;  { UpdateFleets }

PROCEDURE GetPatrolDestination(VAR HuntingGround: HuntingGroundArray;
                               VAR BX,BY: Word; VAR XY: XYCoord);
   VAR
      x,y,MaxBX,MaxBY: Word;
      RN,Total: Word;

   BEGIN
   MaxBX:=SizeOfGalaxy DIV 5;
   MaxBY:=SizeOfGalaxy DIV 5;
   Total:=0;

   FOR x:=1 TO MaxBX DO
      FOR y:=1 TO MaxBY DO
         Inc(Total,HuntingGround[x,y]);

   RN:=Rnd(1,Total);
   FOR x:=1 TO MaxBX DO
      FOR y:=1 TO MaxBY DO
         IF RN<=HuntingGround[x,y] THEN
            BEGIN
            XY.x:=Rnd(1+(x-1)*5,x*5);
            XY.y:=Rnd(1+(y-1)*5,y*5);
            BX:=x;
            BY:=y;
            Exit;
            END
         ELSE
            Dec(RN,HuntingGround[x,y]);
   END;  { GetPatrolDestination }

PROCEDURE DeployRaiders(Emp: Empire; Data: PirateDataPtr);
   VAR
      i,Slot: Word;
      BaseID,FltID,TargID: IDNumber;
      FltSh,Ships: ShipArray;
      FltCr,Cargo: CargoArray;
      DestXY: XYCoord;
      BX,BY: Word;

   PROCEDURE GetTarget(VAR TargetID: IDNumber; VAR FltSh: ShipArray; GAT: Word);
      VAR
         i,BestTarget,BestValue: Word;
         GoodTargets: PlanetSet;
         TestID: IDNumber;
         Sh: ShipArray;
         Cr: CargoArray;
         Df: DefnsArray;
         Possible: ARRAY [1..MaxNoOfPlanets] OF Integer;
         Weight: Real;
         Gain,Protect: LongInt;
         FltPower: LongInt;

      BEGIN
      TargetID:=EmptyQuadrant;
      GoodTargets:=[1..NoOfPlanets]-SetOfPlanetsOf[Emp];
      FillChar(Possible,SizeOf(Possible),0);
      FltPower:=GreaterInt(1,FltSh[jmp]+2*FltSh[hkr]);

      {$IFDEF DebugNPE}
      ClrScr;
      WriteLn('Get Target.  Fleet power: ',FltPower);
      WriteLn;
      {$ENDIF}

      TestID.ObjTyp:=Pln;
      FOR i:=1 TO NoOfPlanets DO
         IF i IN GoodTargets THEN
            BEGIN
            TestID.Index:=i;
            IF GetTech(TestID)>=AtomicLvl THEN
               BEGIN
               GetShips(TestID,Sh);
               GetCargo(TestID,Cr);
               GetDefns(TestID,Df);
               Gain:=LongInt(Cr[che])+Cr[met]+5*(Cr[tri])+(Cr[sup] DIV 2);
               Protect:=10*LongInt(Df[def])+Df[GDM]+5*Df[ion]+2*Sh[hkr]+Sh[jmp]+4*Sh[pen]+10*Sh[ssp];
               IF (Protect<FltPower) AND (GAT>(Cr[men]+2*Cr[nnj])) THEN
                  Possible[i]:=Round((1-(Protect/FltPower))*(Gain DIV 10));

               {$IFDEF DebugNPE }
               WriteLn('World: ',i,' Def: ',Protect,' Cargo: ',Gain,' Rating: ',Possible[i]);
               {$ENDIF}
               END;
            END;

      BestTarget:=0;
      BestValue:=0;
      FOR i:=1 TO NoOfPlanets DO
         IF RndVar(Possible[i],25)>BestValue THEN
            BEGIN
            BestTarget:=i;
            BestValue:=Possible[i];
            END;

      IF BestTarget<>0 THEN
         BEGIN
         TargetID.ObjTyp:=Pln;
         TargetID.Index:=BestTarget;
         {$IFDEF DebugNPE}
         WriteLn;
         WriteLn('Best Target: ',BestTarget,'  Rating: ',BestValue);
         {$ENDIF}
         END;
      END;  { GetTarget }

   BEGIN
   {$IFDEF DebugNPE}
   WriteLn('Deploy Raiders');
   {$ENDIF}
   BaseID.ObjTyp:=Pln;
   FOR i:=1 TO NoOfPlanets DO
      IF i IN SetOfPlanetsOf[Emp] THEN
         BEGIN
         BaseID.Index:=i;
         GetShips(BaseID,Ships);
         GetCargo(BaseID,Cargo);
         FillChar(FltSh,SizeOf(FltSh),0);
         FillChar(FltCr,SizeOf(FltCr),0);
         WITH Data^ DO
            BEGIN
            IF (Ships[hkr]>1500) AND (Ships[jmp]>2500) AND (Ships[jtn]>4000) THEN
               BEGIN
               FltSh[hkr]:=LesserInt(Rnd(1500,5000),Ships[hkr]);
               FltSh[jmp]:=LesserInt(Rnd(2500,9500),Ships[jmp]);
               FltSh[jtn]:=LesserInt(Rnd(4000,9500),Ships[jtn]);
               FltCr[men]:=LesserInt(Ships[jtn] DIV 2,Cargo[men]);

               GetTarget(TargID,FltSh,FltCr[men]);
               IF TargID.ObjTyp<>Void THEN
                  BEGIN
                  GetCoord(TargID,DestXY);
                  Slot:=NextFleetDataSlot(FleetData);
						IF Slot<>0 THEN
							BEGIN
	                  DeployFleet(Emp,BaseID,FltSh,FltCr,DestXY,FltID);
   	               SetNPEDataIndex(FltID,Slot);
      	            WITH FleetData[Slot] DO
         	            BEGIN
            	         Mission:=AttackWrldMSN;
               	      Waiting:=1;
                  	   TargetID:=TargID;
                     	Index:=FltID.Index;
	                     END;
							END;
                  END;
               END;
            END;  { with scope }
         END;
   END;  { DeployRaiders }

PROCEDURE DeployNewFleets(Emp: Empire; Data: PirateDataPtr);
   VAR
      Slot,i: Word;
      PirateBaseID,FltID: IDNumber;
      FltSh,Ships: ShipArray;
      FltCr: CargoArray;
      DestXY: XYCoord;
      BX,BY: Word;

   PROCEDURE GetFleetComposition(VAR FltSh,Ships: ShipArray);
      BEGIN
      IF (Ships[jtn]>4000) AND (Ships[jmp]>2000) THEN
         BEGIN
         FltSh[hkr]:=LesserInt(Rnd(1000,5000),Ships[hkr]);
         FltSh[jmp]:=LesserInt(Rnd(1900,9200),Ships[jmp]);
         FltSh[jtn]:=LesserInt(Rnd(3900,9200),Ships[jtn]);
         END
      ELSE IF (Ships[jtn]>1000) AND (Ships[jmp]>1000) AND (Rnd(1,100)<=75) THEN
         BEGIN
         FltSh[jmp]:=LesserInt(Rnd(900,3100),Ships[jmp]);
         FltSh[jtn]:=LesserInt(Rnd(1900,2100),Ships[jtn]);
         END
      ELSE IF (Ships[hkr]>250) THEN
         BEGIN
         FltSh[hkr]:=LesserInt(Rnd(400,2500),Ships[hkr]);
         END
      END;  { GetFleetComposition }

   BEGIN
   {$IFDEF DebugNPE}
   WriteLn('Deploy Fleets');
   {$ENDIF}
   PirateBaseID.ObjTyp:=Pln;
   FOR i:=1 TO NoOfPlanets DO
      IF i IN SetOfPlanetsOf[Emp] THEN
         BEGIN
         PirateBaseID.Index:=i;
         GetShips(PirateBaseID,Ships);
         FillChar(FltSh,SizeOf(FltSh),0);
         FillChar(FltCr,SizeOf(FltCr),0);
         WITH Data^ DO
            BEGIN
            GetFleetComposition(FltSh,Ships);
            IF NOT NoShips(FltSh) THEN
               BEGIN
               GetPatrolDestination(HuntingGround,BX,BY,DestXY);
               Slot:=NextFleetDataSlot(FleetData);
					IF Slot<>0 THEN
						BEGIN
	               DeployFleet(Emp,PirateBaseID,FltSh,FltCr,DestXY,FltID);
   	            SetNPEDataIndex(FltID,Slot);
      	         WITH FleetData[Slot] DO
         	         BEGIN
            	      Mission:=WaitForTrnMSN;
               	   Waiting:=Rnd(2,5);
                  	BlockX:=BX;
	                  BlockY:=BY;
   	               Index:=FltID.Index;
      	            END;
						END;
               END;
            END;  { with scope }
         END;
   END;  { DeployNewFleets }

PROCEDURE ReviewNews(Emp: Empire; Data: PirateDataPtr);
   VAR
      News: NewsRecordPtr;
      FltID: IDNumber;
      NewXY: XYCoord;
      BX,BY: Word;

   BEGIN
   {$IFDEF DebugNPE}
   WriteLn('Review News');
   {$ENDIF}
   GetNewsList(Emp,News);
   WHILE News<>Nil DO
      WITH News^ DO
         BEGIN
         CASE Headline OF
            FltBlocked: BEGIN
               FltID:=Loc1.ID;
               GetPatrolDestination(Data^.HuntingGround,BX,BY,NewXY);
               SetFleetDestination(FltID,NewXY);
               WITH Data^.FleetData[NPEDataIndex(FltID)] DO
                  BEGIN
                  BlockX:=BX;
                  BlockY:=BY;
                  END;
               END;
            NoFuel: DestroyFleet(Loc1.ID);
         END;  { case }

         News:=Next;
         END;  { with scope }
   END;  { ReviewNews }

PROCEDURE ImplementPirateNPE(Emp: Empire; DataPtr: Pointer);
   VAR
      Data: PirateDataPtr;

   BEGIN
   Data:=PirateDataPtr(DataPtr);
   EnforceNPEDataLinks(Emp,Data^.FleetData);
   ReviewNews(Emp,Data);
   DeployRaiders(Emp,Data);
   DeployNewFleets(Emp,Data);
   UpdateFleets(Emp,Data);
   END;  { ImplementPirateNPE }

PROCEDURE InitializePirateNPE(Emp: Empire; VAR DataPtr: Pointer);
   VAR
      Data: PirateDataPtr;

   BEGIN
   New(Data);
   DataPtr:=Data;
   WITH Data^ DO
      BEGIN
      FillChar(FleetData,SizeOf(FleetData),0);
      FillChar(HuntingGround,SizeOf(HuntingGround),25);
      END;

	SetEmpireDefenses(Emp);
   END;  { InitializePirateNPE }

PROCEDURE SavePirateNPE(VAR GameFile: FILE; VAR DataPtr: Pointer; VAR Error: Word);
   VAR
      Data: PirateDataPtr;

   BEGIN
   Data:=PirateDataPtr(DataPtr);
   {$I-}
   BlockWrite(GameFile,Data^,SizeOf(Data^));
   {$I+}
   Error:=IOResult;
   END;  { SavePirateNPE }

PROCEDURE LoadPirateNPE(VAR GameFile: FILE; Emp: Empire; VAR DataPtr: Pointer; Version: Word; VAR Error: Word);
   VAR
      Data: PirateDataPtr;
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

      BlockRead(GameFile,Data^.HuntingGround,SizeOf(Data^.HuntingGround));
      BlockRead(GameFile,Data^.Sheep,SizeOf(Data^.Sheep));
      {$I+}
      END
   ELSE
      BEGIN
      {$I-}
      BlockRead(GameFile,Data^,SizeOf(Data^));
      {$I+}
      Error:=IOResult;
      END;

   IF Error<>0 THEN
      Dispose(Data);
   END;  { SavePirateNPE }

PROCEDURE CleanUpPirateNPE(VAR DataPtr: Pointer);
   VAR
      Data: PirateDataPtr;

   BEGIN
   Data:=PirateDataPtr(DataPtr);
   Dispose(Data);
   END;  { CleanUpPirateNPE }

END.
