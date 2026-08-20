(* SBASE.PAS -------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT SBase;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Int,
     Strg,
     Types,
     Galaxy,
     DataCnst,
     DataStrc,
     Misc,
     Primintr,
     News,
     Fleet;

PROCEDURE SelfDestructObject(ObjID: IDNumber);
PROCEDURE MovePlayerStarbases(Emp: Empire);

IMPLEMENTATION

PROCEDURE SelfDestructObject(ObjID: IDNumber);
   VAR
      XY: XYCoord;
      SetOfFleets: FleetSet;
      i: Word;
      FltID: IDNumber;
      Loc: Location;
      Emp,OtherEmp: Empire;
      ShpI: ResourceTypes;
      Ships: ShipArray;

   BEGIN
   GetCoord(ObjID,XY);
   GetFleets(XY,SetOfFleets);
   Emp:=GetStatus(ObjID);

   FOR OtherEmp:=Empire1 TO Empire8 DO
      IF EmpireActive(OtherEmp) AND (OtherEmp<>Emp) AND (Scouted(OtherEmp,ObjID)) THEN
         BEGIN
         Loc.ID:=ObjID;  Loc.XY:=Limbo;
         AddNews(OtherEmp,BseSD,Loc,Integer(Emp),0,0);
         END;

   FltID.ObjTyp:=Flt;
   FOR i:=1 TO MaxNoOfFleets DO
      IF i IN SetOfFleets THEN
         BEGIN
         FltID.Index:=i;
         OtherEmp:=GetStatus(FltID);
         GetShips(FltID,Ships);

         IF OtherEmp<>Emp THEN
            BEGIN
            FleetNameDestruction(OtherEmp,FltID,Loc);
            AddNews(OtherEmp,FltSD,Loc,0,0,0);
            FOR ShpI:=fgt TO trn DO
               IF Ships[ShpI]>0 THEN
                  BEGIN
                  AddNews(OtherEmp,DestDetail,Loc,Ships[ShpI],Integer(ShpI),0);
                  END;
            END;

         DestroyFleet(FltID);
         END;

   WITH Universe^,ObjID DO
      BEGIN
      IF ObjTyp=Base THEN
         BEGIN
         SetOfStarbasesOf[Emp]:=SetOfStarbasesOf[Emp]-[Index];
         SetOfActiveStarbases:=SetOfActiveStarbases-[Index];
         END
      ELSE
         SetOfActiveGates:=SetOfActiveGates-[Index];

      Sector[XY.x]^[XY.y].Obj:=EmptyQuadrant;
      END;
   END;  { SelfDestructObject }

FUNCTION XY2Dir(Pos,Dest: XYCoord): Directions;
   VAR
      Dir: Directions;
      dx,dy: Integer;

   BEGIN
   dx:=Sgn(Dest.x-Pos.x);
   dy:=Sgn(Dest.y-Pos.y);
   Dir:=NW;
   WHILE (Dir<>NoDir) AND ((dx<>DirX[Dir]) OR (dy<>DirY[Dir])) DO
      Dec(Dir);

   XY2Dir:=Dir;
   END;  { XY2Dir }

PROCEDURE GetNewBasePos(VAR Pos: XYCoord; Dest: XYCoord);
   VAR
      PosTest,PosTest2: XYCoord;
      MoveX,MoveY: Integer;
      Obj2,Obj: IDNumber;
      DirTest,Dir: Directions;

   PROCEDURE TentativeMove(VAR NewPos: XYCoord; Dest: XYCoord);
      VAR
         ObjID: IDNumber;

      BEGIN
      GetNewPos(NewPos,Dest);
      IF NOT SameXY(NewPos,Limbo) THEN
         BEGIN
         GetObject(NewPos,ObjID);
         IF ObjID.ObjTyp<>Void THEN
            NewPos:=Limbo;
         END;
      END;  { TentativeMove }

   BEGIN
   PosTest:=Pos;
   Dir:=XY2Dir(Pos,Dest);
   TentativeMove(PosTest,Dest);

   IF NOT SameXY(PosTest,Limbo) THEN
      { ASSERT: Initial move not blocked }
      BEGIN
      Pos:=PosTest;
      END
   ELSE
      { ASSERT: Initial move blocked }
      BEGIN
      IF Dir=NW THEN
         DirTest:=No
      ELSE
         DirTest:=Succ(Dir);

      PosTest.x:=Pos.x+DirX[DirTest];
      PosTest.y:=Pos.y+DirY[DirTest];
      PosTest2:=PosTest;
      TentativeMove(PosTest2,Dest);
      IF GetNebula(PosTest)=DenseNebula THEN
         PosTest:=Limbo
      ELSE
         BEGIN
         GetObject(PosTest,Obj);
         IF Obj.ObjTyp<>Void THEN
            PosTest:=Limbo;
         END;

      IF (NOT SameXY(PosTest,Limbo)) AND (NOT SameXY(PosTest2,Limbo)) THEN
         { Move to move not blocked }
         BEGIN
         Pos:=PosTest;
         END
      ELSE
         BEGIN
         IF Dir=No THEN
            DirTest:=NW
         ELSE
            DirTest:=Pred(Dir);

         PosTest.x:=Pos.x+DirX[DirTest];
         PosTest.y:=Pos.y+DirY[DirTest];
         PosTest2:=PosTest;
         TentativeMove(PosTest2,Dest);
         IF GetNebula(PosTest)=DenseNebula THEN
            PosTest:=Limbo
         ELSE
            BEGIN
            GetObject(PosTest,Obj);
            IF Obj.ObjTyp<>Void THEN
               PosTest:=Limbo;
            END;

         IF (NOT SameXY(PosTest,Limbo)) AND (NOT SameXY(PosTest2,Limbo)) THEN
            BEGIN
            Pos:=PosTest;
            END
         ELSE
            { both sides blocked }
            BEGIN
            Pos:=Limbo;
            END;
         END;
      END;
   END;  { GetNewBasePos }

PROCEDURE MoveBase(BaseID: IDNumber; NewPos: XYCoord);
   BEGIN
   WITH Universe^,Starbase[BaseID.Index] DO
      BEGIN
      Sector[XY.x]^[XY.y].Obj:=EmptyQuadrant;
      Sector[NewPos.x]^[NewPos.y].Obj:=BaseID;
      XY:=NewPos;
      END;  { with scope }
   END;  { MoveBase }

PROCEDURE MovePlayerStarbases(Emp: Empire);
{ MovePlayerStarbases: }

   VAR
      i: Byte;
      BaseID: IDNumber;
      Dest,NewPos,OldPos: XYCoord;
      FuelCon,TriLeft: Resources;
      Move: Boolean;
      STyp: StarbaseTypes;
      Loc: Location;

   BEGIN
   BaseID.ObjTyp:=Base;

   FOR i:=1 TO MaxNoOfStarbases DO
      IF i IN SetOfStarbasesOf[Emp] THEN
         BEGIN
         BaseID.Index:=i;
         Loc.ID:=BaseID;
         Loc.XY:=Limbo;
         STyp:=GetBaseType(BaseID);

         IF (STyp=cmm) OR (STyp=frt) THEN
            BEGIN
            GetCoord(BaseID,OldPos);
            GetFleetDestination(BaseID,Dest);

            IF NOT SameXY(Dest,OldPos) THEN
               BEGIN
               TriLeft:=GetTrillum(BaseID);
               FuelCon:=100;
               IF TriLeft>=FuelCon THEN
                  BEGIN
                  NewPos:=OldPos;

                  GetNewBasePos(NewPos,Dest);
                  IF SameXY(NewPos,Limbo) THEN
                     BEGIN
                     NewPos:=OldPos;
                     AddNews(Emp,BseBlocked,Loc,0,0,0);
                     END;
                  { check for black holes, gates, etc. }

                  MoveBase(BaseID,NewPos);

                  TriLeft:=TriLeft-FuelCon;
                  PutTrillum(BaseID,TriLeft);

                  IF SameXY(Dest,NewPos) THEN
                     SetFleetStatus(BaseID,FReady);
                  END
               ELSE
                  { base is out of fuel }
                  BEGIN
                  AddNews(Emp,BseFuel,Loc,0,0,0);
                  END;
               END;
            END;
         END;  { if and loop }
   END;  { MovePlayerStarbases }

END.
