(* FLEET.PAS -------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT Fleet;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Strg,
     Int,
     Types,
     Galaxy,
     DataStrc,
     DataCnst,
     Misc,
     Primintr,
     Orders,
     News;

PROCEDURE AbortFleet(Flt1: IDNumber; GroundID: IDNumber; Report: Boolean);
PROCEDURE ChangeCompositionOfFleet(FltID: IDNumber; GroundID: IDNumber;
                                   NewFSh: ShipArray; NewFCr: CargoArray;
                                   NewGSh: ShipArray; NewGCr: CargoArray);
PROCEDURE DeployFleet(Emp: Empire; LaunchID: IDNumber; 
                      VAR Sh: ShipArray; VAR Cr: CargoArray;
                      DestXY: XYCoord; VAR FltID: IDNumber);
PROCEDURE DestroyFleet(FltID: IDNumber);
PROCEDURE FleetNameDestruction(Emp: Empire; FltID: IDNumber; VAR NewLoc: Location);
PROCEDURE GetFleetDestination(FltID: IDNumber; VAR D: XYCoord);
PROCEDURE GetNewPos(VAR Pos: XYCoord;  Dest: XYCoord);
PROCEDURE MoveFleet(FltID: IDNumber; NewCoord: XYCoord);
PROCEDURE RefuelFleet(FltID,GroundID: IDNumber; Trillum: Resources);
PROCEDURE SetFleetDestination(FltID: IDNumber; NewDest: XYcoord);
PROCEDURE UpdateAllFleets(Player,NextPlayer: Empire);
PROCEDURE UpdateFleet(FltID: IDNumber);
FUNCTION InRangeOfMyDisrupter(Emp: Empire; Pos: XYCoord): Boolean;

IMPLEMENTATION

uses
	Intrface;

PROCEDURE FleetNameDestruction(Emp: Empire; FltID: IDNumber; VAR NewLoc: Location);
   VAR
      NamePtr: NameRecordPtr;
		NewsPtr: NewsRecordPtr;
      FltName: String16;
      Error: Boolean;

   BEGIN
   NewLoc.XY:=Limbo;
   NewLoc.ID:=FltID;
   IF FltID.ObjTyp=Flt THEN
      BEGIN
      Location2Index(Emp,NewLoc,NamePtr);
      IF NamePtr<>Nil THEN
         BEGIN
         GetDefinedName(Emp,NamePtr,FltName,NewLoc);
         DeleteName(Emp,FltName);
         NewLoc.ID.ObjTyp:=DestFlt;
         AddName(Emp,NewLoc,FltName,Error);

			{ change all previous news references }
			GetNewsList(Emp,NewsPtr);
			WHILE NewsPtr<>Nil DO
				BEGIN
				IF SameID(NewsPtr^.Loc1.ID,FltID) THEN
					NewsPtr^.Loc1:=NewLoc;

				NewsPtr:=NewsPtr^.Next;
				END;
         END;
      END;
   END;  { FleetNameDestruction }

PROCEDURE GetFleetDestination(FltID: IDNumber; VAR D: XYCoord);
   BEGIN
   WITH Universe^,FltID DO
      CASE ObjTyp OF
         Flt: D:=Fleet[Index]^.Dest;
         Base: D:=Starbase[Index].Dest;
      ELSE
         D:=Limbo;
      END;  { case }
   END;  { GetFleetDestination }

PROCEDURE SetFleetDestination(FltID: IDNumber; NewDest: XYcoord);
   VAR
      FltXY: XYCoord;

   BEGIN
   WITH Universe^,FltID DO
      CASE ObjTyp OF
         Flt: Fleet[Index]^.Dest:=NewDest;
         Base: Starbase[Index].Dest:=NewDest;
      END;  { case }

   GetCoord(FltID,FltXY);
   IF SameXY(FltXY,NewDest) THEN
      SetFleetStatus(FltID,FReady)
   ELSE
      SetFleetStatus(FltID,FInTrans);
   END;  { SetFleetDestination }

FUNCTION NoOtherFleetsInSect(FltID: IDNumber; Emp: Empire; XY: XYCoord): Boolean;
{ NoOtherFleetsInSect:
   Returns true if there are no fleet of 'Emp' in 'XY'. }
   VAR
      SetOfFleetsOfEmpAtXY: FleetSet;

   BEGIN
   GetFleets(XY,SetOfFleetsOfEmpAtXY);
   SetOfFleetsOfEmpAtXY:=SetOfFleetsOfEmpAtXY * SetOfFleetsOf[Emp];
   SetOfFleetsOfEmpAtXY:=SetOfFleetsOfEmpAtXY - [FltID.Index];

   IF SetOfFleetsOfEmpAtXY=[] THEN
      NoOtherFleetsInSect:=True
   ELSE
      NoOtherFleetsInSect:=False;
   END;  { NoOtherFleetInSect }

PROCEDURE MoveFleet(FltID: IDNumber; NewCoord: XYCoord);
{ MoveFleet:
   Moves a fleet to the given new co-ordinate. }

   VAR
      OldCoord: XYCoord;

   { MoveFleet: MAIN PROCEDURE }
   BEGIN
   WITH Universe^,Fleet[FltID.Index]^ DO
      BEGIN
      OldCoord:=XY;
      XY:=NewCoord;

      IF NoOtherFleetsInSect(FltID,Emp,OldCoord) THEN
         { set fleet flag }
         WITH Sector[OldCoord.x]^[OldCoord.y] DO
            Flts:=Flts-[Emp];

      { set fleet flag in new quad }
      WITH Sector[NewCoord.x]^[NewCoord.y] DO
         Flts:=Flts+[Emp];
      END;
   END;  { MoveFleet }

PROCEDURE AbortFleet(Flt1: IDNumber; GroundID: IDNumber; Report: Boolean);
{ AbortFleet:
   This procedure transfers all ships and cargo from the fleet to the
   given GroundID.  Fuel left is converted into trillum.  NOTE:  This
   procedure must be followed by DestroyFleet(Flt1) to completely abort the
   fleet. }

   VAR
      Sh,Sh2: ShipArray;
      Cr,Cr2: CargoArray;
      FltPos: XYCoord;
      FuelLeft,FuelOnFlt: Real;
      OtherEmp,Emp: Empire;
      ResI: ResourceTypes;
      CargoTransfered: Boolean;
      Loc: Location;

   { AbortFleet: MAIN PROCEDURE }
   BEGIN
   FuelLeft:=GetFleetFuel(Flt1);
   WITH Universe^.Fleet[Flt1.Index]^ DO
      BEGIN
      Sh2:=Ships;
      Cr2:=Cargo;
      FltPos:=XY;

      IF GroundID.ObjTyp<>Flt THEN
         Cr2[Tri]:=ThgLmt(Cr2[Tri]+ThgLmt(FuelLeft/FuelPerTon))
      ELSE
         BEGIN
         FuelOnFlt:=GetFleetFuel(GroundID);
         FuelOnFlt:=FuelOnFlt+FuelLeft;
         SetFleetFuel(GroundID,FuelOnFlt);
         END;
      END;  { with scope }

   { if ground does not belong to empire, report it. }
   OtherEmp:=GetStatus(GroundID);
   Emp:=GetStatus(Flt1);
   Loc.XY:=Limbo;  Loc.ID:=GroundID;
   IF (OtherEmp<>Emp) AND Report THEN
      BEGIN
      AddNews(OtherEmp,TrnsShp,Loc,Ord(Emp),0,0);
      FOR ResI:=fgt TO trn DO
         IF Sh2[ResI]<>0 THEN
            AddNews(OtherEmp,Trns2,Loc,Sh2[ResI],Ord(ResI),0);

      FOR ResI:=men TO tri DO
         IF Cr2[ResI]<>0 THEN
            AddNews(OtherEmp,Trns2,Loc,Cr2[ResI],Ord(ResI),0);
      END;

   GetShips(GroundID,Sh);
   GetCargo(GroundID,Cr);
   AddThings(Sh,Cr,Sh2,Cr2);
   PutShips(GroundID,Sh);
   PutCargo(GroundID,Cr);

   DeleteName(Emp,ObjectName(Emp,Flt1,ShortFormat));
   END;  { AbortFleet }

PROCEDURE DestroyFleet(FltID: IDNumber);
{ DestroyFleet: }
   VAR
      FltPos: XYCoord;
      FltSta: Empire;
      Code: OrderStructure;

   BEGIN
	IF FltID.Index IN SetOfActiveFleets THEN
		BEGIN
   	WITH Universe^.Fleet[FltID.Index]^ DO
      	BEGIN
      	FltPos:=XY;
      	FltSta:=Emp;
      	END;

   	IF FleetNextStatement(FltID)>0 THEN
      	BEGIN
      	GetFleetCode(FltID,Code);
      	DisposeOrders(Code);
      	END;

   	Dispose(Universe^.Fleet[FltID.Index]);

   	SetOfActiveFleets:=SetOfActiveFleets-[FltID.Index];
   	SetOfFleetsOf[FltSta]:=SetOfFleetsOf[FltSta]-[FltID.Index];

   	IF NoOtherFleetsInSect(FltID,FltSta,FltPos) THEN
      	WITH Sector[FltPos.x]^[FltPos.y] DO
         	BEGIN
         	Flts:=Flts-[FltSta];
         	END;  { with scope }
		END;
   END;  { DestroyFleet }

PROCEDURE GetNextFleet(NewEmp: Empire; VAR FltID: IDNumber);
   VAR
      NextSlot,i,NoOfFleets: Word;
      UnActiveFleets: FleetSet;

   BEGIN
   NoOfFleets:=0;
   NextSlot:=0;
   i:=MaxNoOfFleets;
   WHILE (i>0) AND (NextSlot=0) DO
      BEGIN
      IF NOT (i IN SetOfActiveFleets) THEN
         NextSlot:=i;
      Dec(i);
      END;

   IF NextSlot=0 THEN
      FltID:=EmptyQuadrant
   ELSE
      BEGIN
      FltID.ObjTyp:=Flt;

      FltID.Index:=NextSlot;
      New(Universe^.Fleet[NextSlot]);
      FillChar(Universe^.Fleet[NextSlot]^,SizeOf(FleetRecord),0);

      WITH Universe^.Fleet[NextSlot]^ DO
         BEGIN
         Emp:=NewEmp;
         NextOrder:=0;
         END;
      SetOfFleetsOf[NewEmp]:=SetOfFleetsOf[NewEmp]+[NextSlot];
      SetOfActiveFleets:=SetOfActiveFleets+[NextSlot];
      END;
   END;  { GetNextFleet }

PROCEDURE ChangeCompositionOfFleet(FltID: IDNumber; GroundID: IDNumber;
                                   NewFSh: ShipArray; NewFCr: CargoArray;
                                   NewGSh: ShipArray; NewGCr: CargoArray);
{ ChangeCompositionOfFleet:
   This procedure transfers things from a fleet to the ground.  Empty
   fleets are aborted and all fuel is transfered in proportion to the
   number of ships transfered. }

   VAR
      FltFuel,GrdFuel: Real;
      OldFuCap,NewFuCap,OldGFuCap,NewGFuCap: Real;
      FuelChange: Real;

      TonsOnGround: Resources;
      TonsNeeded: Integer;
      OldFSh,OldGSh: ShipArray;

   BEGIN
   GetShips(FltID,OldFSh);
   GetShips(GroundID,OldGSh);

   IF NoShips(NewFSh) THEN
      BEGIN
      AbortFleet(FltID,GroundID,True);
      DestroyFleet(FltID);
      END
   ELSE IF NoShips(NewGSh) AND (GroundID.ObjTyp=Flt) THEN
      BEGIN
      AbortFleet(GroundID,FltID,True);
      DestroyFleet(GroundID);
      END
   ELSE
      BEGIN
      IF GroundID.ObjTyp=Flt THEN

         { ASSERT: ground is a fleet }
         BEGIN
         FltFuel:=GetFleetFuel(FltID);
         GrdFuel:=GetFleetFuel(GroundID);

         { GET FLEET FUEL CAPACITY }
         OldFuCap:=FuelCapacity(OldFSh);
         NewFuCap:=FuelCapacity(NewFSh);

         { GET GROUND FUEL CAPACITY }
         OldGFuCap:=FuelCapacity(OldGSh);
         NewGFuCap:=FuelCapacity(NewGSh);

         FuelChange:=(FltFuel*NewFuCap/OldFuCap)-FltFuel;
         IF FuelChange>GrdFuel THEN
            FuelChange:=GrdFuel;

         GrdFuel:=GrdFuel-FuelChange;
         FltFuel:=FltFuel+FuelChange;

         SetFleetFuel(FltID,FltFuel);
         SetFleetFuel(GroundID,GrdFuel);

         { SET GROUND AND FLEET SHIPS AND CARGO }
         PutShips(FltID,NewFSh);  PutCargo(FltID,NewFCr);
         PutShips(GroundID,NewGSh);  PutCargo(GroundID,NewGCr);
         END

      ELSE

         { ASSERT: ground is not a fleet }
         BEGIN
         FltFuel:=GetFleetFuel(FltID);

         { GET FLEET FUEL CAPACITY }
         OldFuCap:=FuelCapacity(OldFSh);
         NewFuCap:=FuelCapacity(NewFSh);

         { SET NEW GROUND SHIPS AND CARGO }
         PutShips(GroundID,NewGSh);
         PutCargo(GroundID,NewGCr);

         FuelChange:=NewFuCap-FltFuel;

         TonsNeeded:=Round(FuelChange/FuelPerTon);
         { ASSERT: if fuel moves from ground to fleet then TonsNeeded is
           positive, else TonsNeeded is negative. }

         TonsOnGround:=GetTrillum(GroundID);
         IF TonsOnGround>=TonsNeeded THEN
            BEGIN
            TonsOnGround:=ThgLmt(TonsOnGround-TonsNeeded);
            PutTrillum(GroundID,TonsOnGround);
            END
         ELSE
            BEGIN
            PutTrillum(GroundID,0);
            FuelChange:=1.0*TonsOnGround*FuelPerTon;
            END;  { if }

         FltFuel:=FltFuel+FuelChange;
         { ASSERT: FltFuel = new fuel for fleet. }

         SetFleetFuel(FltID,FltFuel);

         { SET FLEET SHIPS AND CARGO }
         PutShips(FltID,NewFSh);
         PutCargo(FltID,NewFCr);
         END;  { if }

      END;  { if }

   END;  { ChangeCompositionOfFleet }

PROCEDURE DeployFleet(Emp: Empire; LaunchID: IDNumber;
                      VAR Sh: ShipArray; VAR Cr: CargoArray;
                      DestXY: XYCoord; VAR FltID: IDNumber);
{ DeployFleet: -----------------------------------------------------------------

------------------------------------------------------------------------------ }
   VAR
      LaunchXY: XYCoord;
      LauSh: ShipArray;
      LauCr: CargoArray;

   BEGIN
   GetNextFleet(Emp,FltID);
   IF FltID.ObjTyp<>Void THEN
      BEGIN
      GetCoord(LaunchID,LaunchXY);
      WITH Universe^.Fleet[FltID.Index]^ DO
         BEGIN
         XY:=Limbo;
         ScoutedBy:=[Emp];
			KnownBy:=[Emp];
         FillChar(Ships,SizeOf(Sh),0);
         FillChar(Cargo,SizeOf(Cr),0);
         END;
      SetFleetFuel(FltID,0);
      SetOfActiveFleets:=SetOfActiveFleets+[FltID.Index];
      MoveFleet(FltID,LaunchXY);
      SetFleetDestination(FltID,DestXY);
 
      GetShips(LaunchID,LauSh);
      GetCargo(LaunchID,LauCr);
      SubThings(LauSh,LauCr,Sh,Cr);

      { The reverse order for ChangeCompositionOfFleet when the ground is
        a fleet is necessary so that the routines that transfer fuel
        work correctly. }
      IF LaunchID.ObjTyp=Flt THEN
         ChangeCompositionOfFleet(LaunchID,FltID,LauSh,LauCr,Sh,Cr)
      ELSE
         ChangeCompositionOfFleet(FltID,LaunchID,Sh,Cr,LauSh,LauCr);

      IF GetFleetFuel(FltID)=0 THEN
         SetFleetFuel(FltID,10);
      END;
   END;  { DeployFleet }

PROCEDURE GetNewPos(VAR Pos: XYCoord;  Dest: XYCoord);
   VAR
      NewPos: XYCoord;

   BEGIN
   NewPos:=Pos;
   NewPos.x:=NewPos.x+Sgn(Dest.x-Pos.x);
   NewPos.y:=NewPos.y+Sgn(Dest.y-Pos.y);

   IF GetNebula(NewPos)=DenseNebula THEN
      Pos:=Limbo
   ELSE
      Pos:=NewPos;
   END;  { GetNewPos }

procedure RefuelFleet(FltID,GroundID: IDNumber; Trillum: Resources);
{ RefuelFleet:
  This procedure will take the specified number of tons of trillum to 
  refuel the given fleet.  It assumes that there is enough. } 

   var
      FltSh: ShipArray;
      FltCr: CargoArray;
      FuelCons,FltFuel,MaxFuel: Real;
      TonsOnGround: Resources;
      FPos,FDes: XYCoord;

   BEGIN
   GetShips(FltID,FltSh);
   GetCargo(FltID,FltCr);
   FltFuel:=GetFleetFuel(FltID);
   MaxFuel:=FuelCapacity(FltSh);
   TonsOnGround:=GetTrillum(GroundID);

   TonsOnGround:=TonsOnGround-Trillum;
   FltFuel:=FltFuel+1.0*Trillum*FuelPerTon;
   IF FltFuel>MaxFuel THEN
      FltFuel:=MaxFuel;

   PutTrillum(GroundID,TonsOnGround);
   SetFleetFuel(FltID,FltFuel);

   FuelCons:=FuelConsumption(FltSh,FltCr);
   IF FltFuel>FuelCons THEN
      { set fleet status }
      BEGIN
      GetCoord(FltID,FPos);
      GetFleetDestination(FltID,FDes);
      IF SameXY(FPos,FDes) THEN
         SetFleetStatus(FltID,FReady)
      ELSE
         SetFleetStatus(FltID,FInTrans);
      END;
   END;  { RefuelFleet }

PROCEDURE ExecuteDestCOM(FltID: IDNumber; Loc: Location);
   BEGIN
   IF SameXY(Loc.XY,Limbo) THEN
      GetCoord(Loc.ID,Loc.XY);
   SetFleetDestination(FltID,Loc.XY);
   END;  { ExecuteDestCOM }

PROCEDURE ExecuteSweepCOM(FltID: IDNumber);
   VAR
      XY: XYCoord;
      SRMLoc,FleetLoc: Location;
      Player,SRMOwner: Empire;
      ShipsInFleet: ShipArray;

   BEGIN
   GetCoord(FltID,XY);
   
   SRMOwner:=EnemyMine(XY);
   Player:=GetStatus(FltID);
   SRMLoc.ID:=EmptyQuadrant;
   SRMLoc.XY:=XY;
   FleetLoc.ID:=FltID;
   FleetLoc.XY:=Limbo;
   GetShips(FltID,ShipsInFleet);
   IF ShipsInFleet[ssp] < 100 THEN
      AddNews(Player,OrdersNoSsp,FleetLoc,0,0,0)
   ELSE 
      BEGIN
      IF SRMOwner=Indep THEN
         AddNews(Player,OrdersNoSRMs,FleetLoc,0,0,0);
      IF SRMOwner<>Indep THEN
         BEGIN
         IF SRMOwner<>Player THEN
         AddNews(SRMOwner,SRMClear,SRMLoc,Integer(Player),0,0);
         PutMine(XY,Indep);
         ClrMineScout(XY);
         AddNews(Player,OrdersSRMClear,FleetLoc,0,0,0);
         END;
      END;
   END;  { ExecuteSweepCom }
	

FUNCTION GetResource(VAR Sh: ShipArray; VAR Cr: CargoArray; Res: ResourceTypes): Word;
   BEGIN
   IF Res>trn THEN
      GetResource:=Cr[Res]
   ELSE
      GetResource:=Sh[Res];
   END;  { GetResource }

PROCEDURE PutResource(VAR Sh: ShipArray; VAR Cr: CargoArray; Res: ResourceTypes; Val: Word);
   BEGIN
   IF Res>trn THEN
      Cr[Res]:=Val
   ELSE
      Sh[Res]:=Val;
   END;  { GetResource }

PROCEDURE ExecuteTransCOM(FltID: IDNumber; Res: ResourceTypes; Trans: Integer);
   VAR
      GroundID: IDNumber;
      FltXY: XYCoord;
      FltRes,GrnRes: Word;
      GrnSh,FltSh: ShipArray;
      GrnCr,FltCr: CargoArray;

   BEGIN
   GetCoord(FltID,FltXY);
   GetObject(FltXY,GroundID);
   IF (NOT SameID(GroundID,EmptyQuadrant)) AND (GetStatus(GroundID)=GetStatus(FltID)) AND 
      (GroundID.ObjTyp IN [Pln,Base]) THEN
     { ASSERT: legal ground exists }
      BEGIN
      GetShips(GroundID,GrnSh);
      GetCargo(GroundID,GrnCr);
      GetShips(FltID,FltSh);
      GetCargo(FltID,FltCr);

      FltRes:=GetResource(FltSh,FltCr,Res);
      GrnRes:=GetResource(GrnSh,GrnCr,Res);
      
      IF Trans>0 THEN
         { ASSERT: fleet is picking up stuff }
         BEGIN
         Trans:=LesserInt(Trans,GrnRes);
         Trans:=LesserInt(Trans,MaxResources-FltRes);
         IF Res IN [men..tri] THEN
            Trans:=LesserInt(FleetCargoSpace(FltSh,FltCr)*LongInt(CargoSpace[Res]),Trans);
         END
      ELSE
         { ASSERT: fleet is dropping stuff off }
         BEGIN
         Trans:=-Trans;
         Trans:=LesserInt(Trans,FltRes);
         Trans:=LesserInt(Trans,MaxResources-GrnRes);
         Trans:=-Trans;
         END;

      PutResource(FltSh,FltCr,Res,FltRes+Trans);
      PutResource(GrnSh,GrnCr,Res,GrnRes-Trans);

      BalanceFleet(FltSh,FltCr);
      ChangeCompositionOfFleet(FltID,GroundID,FltSh,FltCr,GrnSh,GrnCr);
      END;
   END;  { ExecuteTransCOM }

PROCEDURE ExecuteFleetOrders(Emp: Empire; FltID: IDNumber);
{ ExecuteFleetOrders: ----------------------------------------------------------
   This procedure should be called after a fleet has reached its destination
   but before a player takes his/her turn.
------------------------------------------------------------------------------ }
   VAR
      Code: OrderStructure;
      FleetDestroyed,IgnoreRepeat: Boolean;
      LastCommand,Com: Word;
      Command: CommandRecord;

   BEGIN
   Com:=FleetNextStatement(FltID);
   IgnoreRepeat:=False;
	FleetDestroyed:=False;
   IF Com<>0 THEN
      { ASSERT: Fleet has orders }
      BEGIN
      GetFleetCode(FltID,Code);
      LastCommand:=NumberOfCommands(Code);
      REPEAT
         GetCommandRecord(Code,Com,Command);
         CASE Command.Typ OF
            DestCOM: ExecuteDestCOM(FltID,Command.Loc);
            TransCOM: ExecuteTransCOM(FltID,Command.Res,Command.Trns);
	    SweepCom: ExecuteSweepCOM(FltID);
            END; { case }

         IF FltID.Index IN SetOfActiveFleets THEN
            { ASSERT: Fleet still exists. }
            BEGIN
            IF (Command.Typ=RepeatCOM) AND (NOT IgnoreRepeat) THEN
               BEGIN
               Com:=1;
               IgnoreRepeat:=True;
               END
            ELSE
               BEGIN
               IF Com<LastCommand THEN
                  Inc(Com)
               ELSE
                  BEGIN
                  Com:=0;
                  DisposeOrders(Code);
                  SetFleetCode(FltID,Code);
                  END;
               END;
            END
         ELSE
            FleetDestroyed:=True;

      UNTIL (Command.Typ IN [DestCOM,WaitCOM]) OR (Com=0) OR (FleetDestroyed);

      IF NOT FleetDestroyed THEN
         SetFleetNextStatement(FltID,Com);
      END;
   END;  { ExecuteFleetOrders }

FUNCTION InRangeOfDisrupter(Emp: Empire; Pos: XYCoord; VAR DisruptBy: Empire): Boolean;
   VAR
      i: Word;
      GteID: IDNumber;
      XY: XYCoord;

   BEGIN
   GteID.ObjTyp:=Gate;
   FOR i:=1 TO MaxNoOfStargates DO
      IF i IN SetOfActiveGates THEN
         BEGIN
         GteID.Index:=i;
         IF (GetGateType(GteID)=dis) 
          AND (GetWarpLinkFreq(Emp,GteID)<>GetWarpLinkFreq(GetStatus(GteID),GteID)) THEN
            BEGIN
            GetCoord(GteID,XY);
            IF Distance(XY,Pos)<=3 THEN
               BEGIN
               DisruptBy:=GetStatus(GteID);
               InRangeOfDisrupter:=True;
               Exit;
               END;
            END;
         END;

   InRangeOfDisrupter:=False;
   END;  { InRangeOfDisrupter }

FUNCTION InRangeOfMyDisrupter(Emp: Empire; Pos: XYCoord): Boolean;
   VAR
      i: Word;
      GteID: IDNumber;
      XY: XYCoord;

   BEGIN
   GteID.ObjTyp:=Gate;
   FOR i:=1 TO MaxNoOfStargates DO
      IF i IN SetOfActiveGates THEN
         BEGIN
         GteID.Index:=i;
         IF (GetGateType(GteID)=dis) 
           AND (GetWarpLinkFreq(Emp,GteID)=GetWarpLinkFreq(GetStatus(GteID),GteID)) THEN
            BEGIN
            GetCoord(GteID,XY);
            IF Distance(XY,Pos)<=2 THEN
               BEGIN
               InRangeOfMyDisrupter:=True;
               Exit;
               END;
            END;
         END;

   InRangeOfMyDisrupter:=False;
   END;  { InRangeOfMyDisrupter }

PROCEDURE UpdateFleet(FltID: IDNumber);
   LABEL
      ExitMoveLoop;

   VAR
      FltTyp: FleetTypes;
      GateTyp: StargateTypes;
      OldPos,NewPos,FltXY,Dest: XYCoord;
      Emp,DisruptBy,MinedBy: Empire;
      Loc: Location;
      ObjID,ObjID2: IDNumber;
      EnoughFuel,Teleport,FltDestroyed: Boolean;
      j,Disruptercounter: Byte;

   PROCEDURE UseUpFuel(FltID: IDNumber; VAR EnoughFuel: Boolean);
      VAR
         FuelCon,FuelLeft: Real;
         Sh: ShipArray;
         Cr: CargoArray;
         Loc: Location;
         Emp: Empire;
         TriToUse: Word;

      { UseUpFuel: MAIN PROCEDURE }
      BEGIN
      GetShips(FltID,Sh);
      GetCargo(FltID,Cr);
      Emp:=GetStatus(FltID);

      FuelCon:=FuelConsumption(Sh,Cr);
      FuelLeft:=GetFleetFuel(FltID);

      IF FuelLeft>=FuelCon THEN
         BEGIN
         FuelLeft:=FuelLeft-FuelCon;
         SetFleetFuel(FltID,FuelLeft);
         EnoughFuel:=True;
         END
      ELSE
         BEGIN
         IF Cr[tri]=0 THEN
            BEGIN
            EnoughFuel:=False;
            Loc.ID:=FltID;  Loc.XY:=Limbo;
            AddNews(Emp,NoFuel,Loc,0,0,0);
            SetFleetStatus(FltID,FInactive);
            END
         ELSE
            { ASSERT: Refuel from cargo }
            BEGIN
            TriToUse:=GreaterInt(1,LesserInt(Cr[tri],Round(FuelCon/FuelPerTon)));
            RefuelFleet(FltID,FltID,TriToUse);

            { try again }
            UseUpFuel(FltID,EnoughFuel);
            END;
         END;
      END;  { UseUpFuel }

   PROCEDURE MineFieldDamage(FltID: IDNumber; MinedBy: Empire;
                             VAR FltDestroyed: Boolean);
      VAR
         Sh: ShipArray;
         Cr: CargoArray;
         Emp: Empire;
         ResI: ResourceTypes;
         Loc: Location;
         ShipsDest: ShipArray;

      { MineFieldDamage: MAIN PROCEDURE }
      BEGIN
      GetShips(FltID,Sh);
      GetCargo(FltID,Cr);
      Emp:=GetStatus(FltID);

      FOR ResI:=hkr TO jtn DO
         BEGIN
         ShipsDest[ResI]:=LesserInt(Sh[ResI],Rnd(1,100)+Round(Sh[ResI]*((ProtecNeeded[ResI]+20)/100)));
         Sh[ResI]:=Sh[ResI]-ShipsDest[ResI];
         END;

      Loc.XY:=Limbo;  Loc.ID:=FltID;
      IF NoShips(Sh) THEN
         BEGIN
         FleetNameDestruction(Emp,FltID,Loc);
         AddNews(Emp,MinesDs,Loc,Integer(MinedBy),0,0);
         DestroyFleet(FltID);
         FltDestroyed:=True;
         END
      ELSE
         BEGIN
         AddNews(Emp,MinesDm,Loc,Integer(MinedBy),0,0);
         BalanceFleet(Sh,Cr);
         PutShips(FltID,Sh);
         PutCargo(FltID,Cr);
         END;

      FOR ResI:=hkr TO jtn DO
         IF ShipsDest[ResI]<>0 THEN
            AddNews(Emp,DestDetail,Loc,ShipsDest[ResI],Integer(ResI),0);
      END;  { MineFieldDamage }

   { MAIN PROCEDURE: UpdateFleet }
   BEGIN
   GetCoord(FltID,FltXY);
   GetObject(FltXY,ObjID);
   GetFleetDestination(FltID,Dest);
   FltTyp:=TypeOfFleet(FltID);
   Emp:=GetStatus(FltID);
   FltDestroyed:=False;
   
   IF NOT SameXY(Dest,FltXY) THEN
      { ASSERT: Fleet not at destination }
      BEGIN
      Teleport:=False;
      IF PassingThroughGate(FltID,FltXY,Dest) THEN
         Teleport:=True
      ELSE IF PassingThroughFortress(FltXY) THEN
         BEGIN
         IF (Distance(FltXY,Dest)<=5) THEN
            Teleport:=True
         ELSE
            BEGIN
            j:=5;
            NewPos:=FltXY;
            WHILE (j>0) AND (NOT SameXY(NewPos,Limbo)) DO
               BEGIN
               FltXY:=NewPos;
               GetNewPos(NewPos,Dest);
               Dec(j);
               END;

            MoveFleet(FltID,FltXY);
            END;
         END;

      UseUpFuel(FltID,EnoughFuel);
      IF EnoughFuel THEN
         BEGIN
         IF Teleport THEN
            BEGIN
            IF GetNebula(Dest)=DenseNebula THEN
               BEGIN
               Loc.ID:=FltID;  Loc.XY:=Limbo;
               AddNews(Emp,NebGate,Loc,0,0,0);
               END
            ELSE
               BEGIN
               MoveFleet(FltID,Dest);
               SetFleetStatus(FltID,FReady);
               END;
            END
         ELSE
            { ASSERT: Not passing through a gate }
            BEGIN
            NewPos:=FltXY;

            FOR j:=1 TO FltMovementRate[FltTyp] DO
               BEGIN
               OldPos:=NewPos;
               GetNewPos(NewPos,Dest);

               { check for mines and disrupters }
               IF FltTyp IN [JumpFleet,HKFleet] THEN
                  BEGIN
                  MinedBy:=EnemyMine(NewPos);
                  IF (MinedBy<>Indep) AND (MinedBy<>Emp) THEN
                     BEGIN
                     MineFieldDamage(FltID,MinedBy,FltDestroyed);
                     Loc.XY:=NewPos;  Loc.ID:=EmptyQuadrant;
                     AddNews(MinedBy,Mines,Loc,Integer(Emp),0,0);
		     SetMineScout(Emp,NewPos);
                     GOTO ExitMoveLoop;
                     END;

                  IF InRangeOfDisrupter(Emp,NewPos,DisruptBy) THEN
                     BEGIN
                     Loc.XY:=Limbo;  Loc.ID:=FltID;
                     AddNews(Emp,Disrupt,Loc,Integer(DisruptBy),0,0);
                     GOTO ExitMoveLoop;
                     END;
                  END;  { if }

               { Check for disrupter for warp fleets }
               { This section will let these fleets travel at }
               { jump speed while in range of the owner's disruptor }
	       IF FltTyp IN [Standard, Penetrator, AdvWrpFleet] THEN
		  IF InRangeOfMyDisrupter(Emp,NewPos) THEN
                     BEGIN
                     Disruptercounter:=1;
                     { Now we keep going until either we reach our }
                     { destination or we are out of range of a     }
                     { disruptor OR we've gone 10 sectors.         }
		     WHILE InRangeOfMyDisrupter(Emp,NewPos) AND 
                           (Disruptercounter <= 10) AND  { Keeps it at jump speed }
                           NOT SameXY(NewPos,Dest) AND   { Stops at destination } 
                           NOT SameXY(NewPos,Limbo) DO         { Blocked by dense nebula }
                       BEGIN
                         OldPos:=NewPos;
                         GetNewPos(NewPos,Dest);
                         Inc(Disruptercounter);
                       END;
                     END;   
               
                     

               IF SameXY(NewPos,Limbo) THEN
                  BEGIN
                  FltDestroyed:=False;
                  NewPos:=OldPos;
                  Loc.ID:=FltID;  Loc.XY:=Limbo;
                  AddNews(Emp,FltBlocked,Loc,0,0,0);
                  GOTO ExitMoveLoop;
                  END;
               END;  { loop }

            ExitMoveLoop:
            { The move loop is exitted here if the fleet runs into a 
              problem such as a mine or dense nebula. }

            IF NOT FltDestroyed THEN
               BEGIN
               MoveFleet(FltID,NewPos);

               IF SameXY(Dest,NewPos) THEN
                  SetFleetStatus(FltID,FReady);
               END;  { if }
            END;
         END;
      END;

   IF (NOT FltDestroyed) AND (GetFleetStatus(FltID)=FReady) THEN
      BEGIN
      ExecuteFleetOrders(Emp,FltID);
      END;
   END;  { UpdateFleet }

PROCEDURE UpdateAllFleets(Player,NextPlayer: Empire);
{ UpdateFleets:
   This procedure will update the following fleets:

      - player jumpfleets not passing through a gate
      - next player warp fleets
      - next player fleets passing through a gate
      - next player penetrator and hk fleets
      - next player starbases

   All other fleets are not updated }

   VAR
      FltID,ObjID: IDNumber;
      FltXY: XYCoord;
      FltTyp: FleetTypes;
      Emp: Empire;
      i: Byte;

   { MAIN PROCEDURE: UpdateAllFleets }
   BEGIN
   FltID.ObjTyp:=Flt;
   FOR i:=1 TO MaxNoOfFleets DO
      IF i IN SetOfActiveFleets THEN
         BEGIN
         FltID.Index:=i;
         FltTyp:=TypeOfFleet(FltID);
         GetCoord(FltID,FltXY);
         GetObject(FltXY,ObjID);
         Emp:=GetStatus(FltID);

         IF (Emp=NextPlayer) AND 
            ((FltTyp IN [Standard,Penetrator,AdvWrpFleet]) OR
            (ObjID.ObjTyp=Gate)) THEN
            BEGIN
            UpdateFleet(FltID);
            END
         ELSE IF (Emp=Player) AND
            (FltTyp IN [HKFleet,JumpFleet]) AND
            (ObjID.ObjTyp<>Gate) THEN
            BEGIN
            UpdateFleet(FltID);
            END;
         END;  { loop }
   END;  { UpdateAllFleets }

END.

