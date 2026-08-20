(* MAPWIND.PAS -----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT MapWind;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES
   Strg,
   Int,
   CRT,
   System2,
   EIO,
   WND,
   Types,
   Galaxy,
   DataStrc,
   DataCnst,
   WndTypes,
   PrimIntr,
   Intrface;

CONST
   { map scan constants }
   NebulaChar: ARRAY [NebulaTypes] OF Char = ' ±°°';
   MineChar        = #043;    { + }
   PlayerFleetChar = #016;    {  }
   EnemyFleetChar  = #031;    {  }
   ConsChar        = #035;    { # }
   BlankChar       = #032;    {   }
   UnkPlanetChar   = #112;    { p }

   HorzChar        = #250;    { ú }
   VertChar        = #250;    { ú }
   CrossChar1      = #196;    { Ä }
   CrossChar2      = #197;    { Å }
   CrossChar3      = #196;    { Ä }

   MaxSizeOfGalaxy2 = MaxSizeOfGalaxy * 6;

TYPE
   { Scan map declarations:  These two declarations are used for
     the scan window. }
   CellRecord = RECORD
      PlayerFltChar: Char;
      PlayerFltCharColor: Byte;

      WorldChar: Char;
      WorldCharColor: Byte;

      EnemyFltChar: Char;
      EnemyFltCharColor: Byte;
   END;

   MapArray = ARRAY [1..MaxSizeOfGalaxy,1..MaxSizeOfGalaxy] OF CellRecord;
   { the Map array is implemented differently from Sector.
     Sector[x,y] corresponds to Map[y,x] }

   MapStructurePtr = ^MapStructure;
   MapStructure = RECORD
      Buffer: MapArray;

      CurX: Word;
      CurY: Word;
      LocX: Word;
      LocY: Word;

      WNDPosX: Word;
      WNDPosY: Word;
      WNDWidth: Word;
      WNDHeight: Word;
      TopCornerX: Word;
      TopCornerY: Word;
      BotCornerX: Word;
      BotCornerY: Word;

      Handle: WindowHandle;
      Col: Word;
   END;

VAR
   MAP: MapStructurePtr;

PROCEDURE ScanWindow(VAR MAP: MapStructure; Comm: Word);
PROCEDURE InitScanWindowMap(VAR MAP: MapStructure; Player: Empire);
PROCEDURE UMFleets(x,y: Byte; VAR Map: MapStructure);
PROCEDURE UMSector(x,y: Byte; VAR Map: MapStructure);
PROCEDURE UMAddName(VAR Map: MapStructure; Name: String16; XY: XYCoord);

IMPLEMENTATION

USES
	Misc,
	Menu,
   Environ,
   SWindows,
	Display;

CONST
   TLCursor        = #218;
   TRCursor        = #191;
   BLCursor        = #192;
   BRCursor        = #217;

   GridSep = 5;
   CursorJump = 5;

   InitX = 1;
   InitY = 4;
   InitWidth = 80;
   InitHeight = 21;

PROCEDURE ClearMap(VAR Buffer: MapArray);
   BEGIN
   FillWord(Buffer,SizeOf(Buffer) DIV 2,32+256*C.MapBackground);
   END;  { ClearMap }

PROCEDURE DrawGrid(VAR Buffer: MapArray; CapXY: XYCoord);
   VAR
      StartHorz,StartVert,x,y: Integer;

   BEGIN
   StartHorz:=(CapXY.x MOD GridSep);
   StartVert:=(CapXY.y mod GridSep);
   IF StartHorz=0 then
      StartHorz:=GridSep;
   if StartVert=0 then
      StartVert:=GridSep;

   x:=StartHorz;
   repeat
      for y:=1 to SizeOfGalaxy do
         begin
         Buffer[y,x].WorldChar:=VertChar;
         Buffer[y,x].PlayerFltChar:=BlankChar;
         Buffer[y,x].EnemyFltChar:=BlankChar;

         Buffer[y,x].WorldCharColor:=C.GridColor;
         end;

      x:=x+GridSep;
   until x>SizeOfGalaxy;

   y:=StartVert;
   repeat
      for x:=1 to SizeOfGalaxy do
         begin
         Buffer[y,x].WorldCharColor:=C.GridColor;
         Buffer[y,x].PlayerFltCharColor:=C.GridColor;
         Buffer[y,x].EnemyFltCharColor:=C.GridColor;

         if Buffer[y,x].WorldChar=VertChar then
            begin
            Buffer[y,x].EnemyFltChar:=CrossChar1;
            Buffer[y,x].WorldChar:=CrossChar2;
            Buffer[y,x].PlayerFltChar:=CrossChar3;
            end
         else
            begin
            Buffer[y,x].WorldChar:=HorzChar;
            Buffer[y,x].EnemyFltChar:=BlankChar;
            Buffer[y,x].PlayerFltChar:=BlankChar;
            end;
         end;

      y:=y+GridSep;
   until y>SizeOfGalaxy;
   END;  { DrawGrid }

PROCEDURE DrawNebulaAndMines(VAR Buffer: MapArray; Player: Empire);
   VAR
      x,y: Byte;
      Neb: NebulaTypes;
		Mined: Boolean;
		MineColor: Byte;

   BEGIN
   FOR x:=1 TO SizeOfGalaxy DO
      FOR y:=1 TO SizeOfGalaxy DO
         WITH Sector[x]^[y] DO
            BEGIN
            Neb:=NebulaTypes(Special MOD 16);
				Mined:=(Player IN MineScout) OR ((Empire(Special DIV 16))=Player);
				IF Player=(Empire(Special DIV 16)) THEN
					MineColor:=C.PlayerColor
				ELSE
					MineColor:=C.EnemyColor;

            IF Neb<>NoNeb THEN
               BEGIN
               Buffer[y,x].PlayerFltChar:=NebulaChar[Neb];
               Buffer[y,x].PlayerFltCharColor:=C.NebulaColor;
               Buffer[y,x].EnemyFltChar:=NebulaChar[Neb];
               Buffer[y,x].EnemyFltCharColor:=C.NebulaColor;
               IF Mined THEN
                  BEGIN
                  Buffer[y,x].WorldChar:=MineChar;
 						Buffer[y,x].WorldCharColor:=MineColor;
                  END
               ELSE
                  BEGIN
                  Buffer[y,x].WorldChar:=NebulaChar[Neb];
                  Buffer[y,x].WorldCharColor:=C.NebulaColor;
                  END;
               END
            ELSE
               BEGIN
               IF Mined THEN
                  BEGIN
                  Buffer[y,x].WorldChar:=MineChar;
                  Buffer[y,x].WorldCharColor:=MineColor;
                  END;
               END;
            END;
   END;  { DrawNebulaAndMines }

PROCEDURE DrawPlanets(VAR Buffer: MapArray; Player: Empire);
   VAR
      i: Byte;

   BEGIN
   FOR i:=1 TO NoOfPlanets DO
      WITH Universe^,Planet[i] DO
         BEGIN
         IF Player IN KnownBy THEN
            BEGIN
            Buffer[XY.y,XY.x].WorldChar:=TypeStr[Typ];
            IF Emp=Player THEN
               Buffer[XY.y,XY.x].WorldCharColor:=C.PlayerColor
            ELSE
               Buffer[XY.y,XY.x].WorldCharColor:=C.EnemyColor;
            END
         ELSE IF NebulaTypes(Sector[XY.x]^[XY.y].Special MOD 16)=NoNeb THEN
            BEGIN
            Buffer[XY.y,XY.x].WorldChar:=UnkPlanetChar;
            Buffer[XY.y,XY.x].WorldCharColor:=C.UnscoutedColor;
            END;
         END;
   END;  { DrawPlanets }

PROCEDURE DrawFleets(VAR Buffer: MapArray; Player: Empire);
   VAR
      i: Byte;
      ActivePlayerFleets: FleetSet;

   BEGIN
   ActivePlayerFleets:=SetOfActiveFleets * SetOfFleetsOf[Player];
   FOR i:=1 TO MaxNoOfFleets DO
      IF i IN ActivePlayerFleets THEN
         WITH Universe^.Fleet[i]^ DO
            BEGIN
            Buffer[XY.y,XY.x].PlayerFltChar:=PlayerFleetChar;
            Buffer[XY.y,XY.x].PlayerFltCharColor:=C.PlayerFleetColor;
            END;
   END;  { DrawFleets }

PROCEDURE DrawStarbases(VAR Buffer: MapArray; Player: Empire);
   VAR
      i: Byte;

   BEGIN
   FOR i:=1 TO MaxNoOfStarbases DO
      IF i IN SetOfActiveStarbases THEN
         WITH Universe^.Starbase[i] DO
            BEGIN
            IF Player IN KnownBy THEN
               BEGIN
               Buffer[XY.y,XY.x].WorldChar:=BaseTypeData[STyp];
               IF Emp=Player THEN
                  Buffer[XY.y,XY.x].WorldCharColor:=C.PlayerColor
               ELSE
                  Buffer[XY.y,XY.x].WorldCharColor:=C.EnemyColor;
               END;
            END;  { with scope and loop }
   END;  { DrawStarbases }

PROCEDURE DrawStargates(VAR Buffer: MapArray; Player: Empire);
   VAR
      i: Byte;

   BEGIN
   FOR i:=1 TO MaxNoOfStargates DO
      IF i IN SetOfActiveGates THEN
         WITH Universe^.Stargate[i] DO
            BEGIN
            IF Player IN KnownBy THEN
               BEGIN
               Buffer[XY.y,XY.x].WorldChar:=GateTypeData[GTyp];
               IF Emp=Player THEN
                  Buffer[XY.y,XY.x].WorldCharColor:=C.PlayerColor
               ELSE
                  Buffer[XY.y,XY.x].WorldCharColor:=C.EnemyColor;
               END;
            END;  { with scope and loop }
   END;  { DrawStargates }

PROCEDURE DrawConstructions(VAR Buffer: MapArray; Player: Empire);
   VAR
      i: Byte;

   BEGIN
   FOR i:=1 TO MaxNoOfConstrSites DO
      IF i IN SetOfActiveConstructionSites THEN
         WITH Universe^.Constr[i] DO
            BEGIN
            IF Player IN KnownBy THEN
               BEGIN
               Buffer[XY.y,XY.x].WorldChar:=ConsChar;
               IF Emp=Player THEN
                  Buffer[XY.y,XY.x].WorldCharColor:=C.PlayerColor
               ELSE
                  Buffer[XY.y,XY.x].WorldCharColor:=C.EnemyColor;
               END;
            END;  { with scope and loop }
   END;  { DrawConstructions }

PROCEDURE DrawEnemyFleets(VAR Buffer: MapArray; Player: Empire);
{ DrawEnemyFleets:
   This procedure will determine which of the enemy fleets are visible to 
   the player and adjust the Map array to show all the visible fleets. }

   VAR
      i: Integer;
      FleetsToCheck: FleetSet;

   BEGIN
   FleetsToCheck:=SetOfActiveFleets-SetOfFleetsOf[Player];
   FOR i:=1 TO MaxNoOfFleets DO
      IF i IN FleetsToCheck THEN
         WITH Universe^.Fleet[i]^ DO
            IF Player IN KnownBy THEN
               BEGIN
               Buffer[XY.y,XY.x].EnemyFltChar:=EnemyFleetChar;
               Buffer[XY.y,XY.x].EnemyFltCharColor:=C.EnemyColor;
               END;
   END;  { DrawEnemyFleets }

procedure WriteNames(VAR MAP: MapStructure; Player: Empire);
   var
      x,y: Integer;
      NextName: NameRecordPtr;
      XY: XYCoord;

   { WriteNames: MAIN PROCEDURE }
   BEGIN
   NextName:=Universe^.EmpireData[Player].Names;

   WHILE NextName<>Nil DO
      BEGIN
      WITH NextName^ DO
         BEGIN
         IF Coord.ID.ObjTyp=Pln THEN
            UMAddName(MAP,Name,Coord.XY)
         ELSE IF Coord.ID.ObjTyp=Base THEN
            BEGIN
            GetCoord(Coord.ID,XY);
            UMAddName(MAP,Name,XY);
            END;
         END;  { with scope }

      NextName:=NextName^.Next;
      END;  { while }
   END;  { WriteNames }

PROCEDURE InitScanWindowMap(VAR MAP: MapStructure; Player: Empire);
{ InitScanWindowMap:
   This procedure initializes the temporary local map that is used for
   status.  }

   VAR
      CapID: IDNumber;
      CapXY: XYCoord;

   { InitScanWindowMap: MAIN PROCEDURE }
   BEGIN
   GetCapital(Player,CapID);
   GetCoord(CapID,CapXY);

   ClearMap(MAP.Buffer);
   DrawGrid(MAP.Buffer,CapXY);

   DrawNebulaAndMines(MAP.Buffer,Player);
   DrawPlanets(MAP.Buffer,Player);
   DrawFleets(MAP.Buffer,Player);
   DrawStarbases(MAP.Buffer,Player);
   DrawStargates(MAP.Buffer,Player);
   DrawConstructions(MAP.Buffer,Player);

   WriteNames(MAP,Player);
   DrawEnemyFleets(MAP.Buffer,Player);

   MAP.Handle:=0;
   MAP.Col:=C.CommWind;
   END;  { InitScanWindowMap }

PROCEDURE DrawCharacter(VAR MAP: MapStructure; Ch: Char; PosX,PosY,Color: Word);
   BEGIN
   WITH MAP DO
      BEGIN
      IF (PosX>0) AND (PosX<=WNDWidth) AND (PosY>0) AND (PosY<=WNDHeight) THEN
         WriteString(Ch,PosX,PosY,Color);
      END;
   END;

PROCEDURE DrawMapCursor(VAR MAP: MapStructure);
   VAR
      CoordLine: LineStr;
      TmpStr: String16;
      Loc: Location;

   BEGIN
   WITH MAP,Buffer[CurY-1,CurX] DO
      BEGIN
      DrawCharacter(MAP,'Ú',LocX-1,LocY-1,White);
      DrawCharacter(MAP,'¿',LocX+1,LocY-1,White);
      END;

   WITH MAP,Buffer[CurY+1,CurX] DO
      BEGIN
      DrawCharacter(MAP,'À',LocX-1,LocY+1,White);
      DrawCharacter(MAP,'Ù',LocX+1,LocY+1,White);
      END;

   Loc.XY.x:=MAP.CurX;
   Loc.XY.y:=MAP.CurY;
   Loc.ID:=EmptyQuadrant;
   GetName(Player,Loc,ShortFormat,CoordLine);
   IF Pos(',',CoordLine)=0 THEN
      BEGIN
      GetCoordName(Loc.XY,TmpStr);
      CoordLine:=TmpStr+' ('+CoordLine+')';
      END;

   WriteHelpLine(''#27#26':Move <Enter>:Select                          '+CoordLine);
   END;  { DrawMapCursor }

PROCEDURE EraseMapCursor(VAR MAP: MapStructure);
   BEGIN
   WITH MAP,Buffer[CurY-1,CurX] DO
      BEGIN
      DrawCharacter(MAP,PlayerFltChar,LocX-1,LocY-1,PlayerFltCharColor);
      DrawCharacter(MAP,EnemyFltChar,LocX+1,LocY-1,EnemyFltCharColor);
      END;

   WITH MAP,Buffer[CurY+1,CurX] DO
      BEGIN
      DrawCharacter(MAP,PlayerFltChar,LocX-1,LocY+1,PlayerFltCharColor);
      DrawCharacter(MAP,EnemyFltChar,LocX+1,LocY+1,EnemyFltCharColor);
      END;
   WriteHelpLine('');
   END;  { EraseMapCursor }

PROCEDURE DrawMapWindow(VAR MAP: MapStructure);
   VAR
      w1,y1: Word;

   BEGIN
   WITH MAP DO
      BEGIN
      TurnScreenOff;
      w1:=6*LesserInt(WNDWidth DIV 3,SizeOfGalaxy);
      FOR y1:=0 TO LesserInt(WNDHeight-1,SizeOfGalaxy-1) DO
         Move(Buffer[TopCornerY+y1,TopCornerX],VirtualScreen^[WNDPosY+y1][WNDPosX],w1);
      TurnScreenOn;
      END;  { with scope }

   DrawMapCursor(MAP);
   END;  { DrawMapWindow }

PROCEDURE ScrollMapUp(VAR MAP: MapStructure);
   VAR
      x1: Word;

   BEGIN
   WITH MAP DO
      BEGIN
      ActivateWindow(Handle);
      IF BotCornerY<SizeOfGalaxy THEN
         BEGIN
         TurnScreenOff;
         ScrollUp;
         Inc(BotCornerY);
         Inc(TopCornerY);
         Inc(CurY);

         x1:=6*(WNDWidth DIV 3);
         Move(Buffer[BotCornerY,TopCornerX],VirtualScreen^[WNDPosY+WNDHeight-1][WNDPosX],x1);

         TurnScreenOn;
         END;
      END;
   END;  { ScrollMapUp }

PROCEDURE ScrollMapDown(VAR MAP: MapStructure);
   VAR
      x1: Word;

   BEGIN
   WITH MAP DO
      BEGIN
      ActivateWindow(Handle);
      IF TopCornerY>1 THEN
         BEGIN
         TurnScreenOff;
         ScrollDown;
         BotCornerY:=BotCornerY-1;
         TopCornerY:=TopCornerY-1;
         Dec(CurY);

         x1:=6*(WNDWidth DIV 3);
         Move(Buffer[TopCornerY,TopCornerX],VirtualScreen^[WNDPosY][WNDPosX],x1);

         TurnScreenOn;
         END;
      END;
   END;  { ScrollMapDown }

PROCEDURE ScrollMapLeft(VAR MAP: MapStructure);
   VAR
      i,x: Word;

   BEGIN
   WITH MAP DO
      BEGIN
      ActivateWindow(Handle);
      IF BotCornerX<SizeOfGalaxy THEN
         BEGIN
         TurnScreenOff;
         FOR i:=1 TO 3 DO
            ScrollLeft;

         BotCornerX:=BotCornerX+1;
         TopCornerX:=TopCornerX+1;
         Inc(CurX);

         x:=WNDPosX+(3*(WNDWidth DIV 3)-3);
         FOR i:=0 TO WNDHeight-1 DO
            Move(Buffer[TopCornerY+i,BotCornerX],VirtualScreen^[WNDPosY+i][x],6);

         TurnScreenOn;
         END;
      END;
   END;  { ScrollMapLeft }

PROCEDURE ScrollMapRight(VAR MAP: MapStructure);
   VAR
      i,x1,l1: Word;

   BEGIN
   WITH MAP DO
      BEGIN
      ActivateWindow(Handle);
      IF TopCornerX>1 THEN
         BEGIN
         TurnScreenOff;
         FOR i:=1 TO 3 DO
            ScrollRight;

         BotCornerX:=BotCornerX-1;
         TopCornerX:=TopCornerX-1;
         Dec(CurX);

         l1:=WNDWidth-3*(WNDWidth DIV 3);
         x1:=(WNDWidth-l1)+1;
         FOR i:=0 TO WNDHeight-1 DO
            BEGIN
            Move(Buffer[TopCornerY+i,TopCornerX],VirtualScreen^[WNDPosY+i][WNDPosX],6);
            WriteBlanks(l1,x1,i+1,0);
            END;

         TurnScreenOn;
         END;
      END;
   END;  { ScrollMapRight }

PROCEDURE MoveCursorLeft(VAR MAP: MapStructure);
   BEGIN
   WITH MAP DO
      BEGIN
      ActivateWindow(Handle);
      EraseMapCursor(MAP);
      IF LocX>2 THEN
         BEGIN
         Dec(CurX);
         Dec(LocX,3);
         END
      ELSE
         ScrollMapRight(MAP);
      END;
   DrawMapCursor(MAP);
   END;  { MoveCursorLeft }

PROCEDURE MoveCursorRight(VAR MAP: MapStructure);
   BEGIN
   WITH MAP DO
      BEGIN
      ActivateWindow(Handle);
      EraseMapCursor(MAP);
      IF CurX<SizeOfGalaxy THEN
         BEGIN
         IF (LocX<(3*(WNDWidth DIV 3)-1)) THEN
            BEGIN
            Inc(CurX);
            Inc(LocX,3);
            END
         ELSE
            ScrollMapLeft(MAP);
         END;
      END;
   DrawMapCursor(MAP);
   END;  { MoveCursorRight }

PROCEDURE MoveCursorUp(VAR MAP: MapStructure);
   BEGIN
   WITH MAP DO
      BEGIN
      ActivateWindow(Handle);
      EraseMapCursor(MAP);
      IF LocY>1 THEN
         BEGIN
         Dec(CurY);
         Dec(LocY);
         END
      ELSE
         ScrollMapDown(MAP);
      END;
   DrawMapCursor(MAP);
   END;  { MoveCursorUp }

PROCEDURE MoveCursorDown(VAR MAP: MapStructure);
   BEGIN
   WITH MAP DO
      BEGIN
      ActivateWindow(Handle);
      EraseMapCursor(MAP);
      IF CurY<SizeOfGalaxy THEN
         BEGIN
         IF LocY<WNDHeight THEN
            BEGIN
            Inc(CurY);
            Inc(LocY);
            END
         ELSE
            ScrollMapUp(MAP);
         END;
      END;
   DrawMapCursor(MAP);
   END;  { MoveCursorDown }

PROCEDURE FastMoveDown(VAR MAP: MapStructure);
   VAR
      CurDisp,WndDisp: Word;

   BEGIN
   WITH MAP DO
      BEGIN
      ActivateWindow(Handle);
      IF CurY<SizeOfGalaxy THEN
         BEGIN
         CurDisp:=LesserInt(CursorJump,SizeOfGalaxy-CurY);
         IF (CurY+CurDisp)>BotCornerY THEN
            WndDisp:=LesserInt(CurDisp,SizeOfGalaxy-BotCornerY)
         ELSE
            WndDisp:=0;

         Inc(CurY,CurDisp);
         Inc(BotCornerY,WndDisp);
         Inc(TopCornerY,WndDisp);
         LocY:=LocY+(CurDisp-WndDisp);

         DrawMapWindow(MAP);
         END;
      END;
   END;  { FastMoveDown }

PROCEDURE FastMoveUp(VAR MAP: MapStructure);
   VAR
      CurDisp,WndDisp: Word;

   BEGIN
   WITH MAP DO
      BEGIN
      ActivateWindow(Handle);
      IF CurY>1 THEN
         BEGIN
         CurDisp:=LesserInt(CursorJump,CurY-1);
         IF (CurY-CurDisp)<TopCornerY THEN
            WndDisp:=LesserInt(CurDisp,TopCornerY-1)
         ELSE
            WndDisp:=0;

         Dec(CurY,CurDisp);
         Dec(BotCornerY,WndDisp);
         Dec(TopCornerY,WndDisp);
         LocY:=LocY-(CurDisp-WndDisp);

         DrawMapWindow(MAP);
         END;
      END;
   END;  { FastMoveUp }

PROCEDURE FastMoveRight(VAR MAP: MapStructure);
   VAR
      CurDisp,WndDisp: Word;

   BEGIN
   WITH MAP DO
      BEGIN
      ActivateWindow(Handle);
      IF CurX<SizeOfGalaxy THEN
         BEGIN
         CurDisp:=LesserInt(CursorJump,SizeOfGalaxy-CurX);
         IF (CurX+CurDisp)>BotCornerX THEN
            WndDisp:=LesserInt(CurDisp,SizeOfGalaxy-BotCornerX)
         ELSE
            WndDisp:=0;

         Inc(CurX,CurDisp);
         Inc(BotCornerX,WndDisp);
         Inc(TopCornerX,WndDisp);
         LocX:=LocX+3*(CurDisp-WndDisp);

         DrawMapWindow(MAP);
         END;
      END;
   END;  { FastMoveRight }

PROCEDURE FastMoveLeft(VAR MAP: MapStructure);
   VAR
      CurDisp,WndDisp: Word;

   BEGIN
   WITH MAP DO
      BEGIN
      ActivateWindow(Handle);
      IF CurX>1 THEN
         BEGIN
         CurDisp:=LesserInt(CursorJump,CurX-1);
         IF (CurX-CurDisp)<TopCornerX THEN
            WndDisp:=LesserInt(CurDisp,TopCornerX-1)
         ELSE
            WndDisp:=0;

         Dec(CurX,CurDisp);
         Dec(BotCornerX,WndDisp);
         Dec(TopCornerX,WndDisp);
         LocX:=LocX-3*(CurDisp-WndDisp);

         DrawMapWindow(MAP);
         END;
      END;
   END;  { FastMoveLeft }

PROCEDURE InitializeMapPos(VAR MAP: MapStructure; CapXY: XYCoord);
   VAR
      MaxX,MaxY: Word;           { TopCornerX in range 1..MaxX }
      BaseX,BaseY: Word;         { if CapXY <= BaseX,BaseY then TopCorner = 1,1 }

   BEGIN
   WITH MAP DO
      BEGIN
      BaseX:=(WNDWidth DIV 6);
      BaseY:=(WNDHeight DIV 2);

      MaxX:=GreaterInt(0,1+(Integer(SizeOfGalaxy)-(WNDWidth DIV 3)));
      MaxY:=GreaterInt(0,1+(SizeOfGalaxy-WNDHeight));

      TopCornerX:=GreaterInt(1,LesserInt(MaxX,Integer(CapXY.x)-BaseX));
      TopCornerY:=GreaterInt(1,LesserInt(MaxY,Integer(CapXY.y)-BaseY));
      BotCornerX:=LesserInt(SizeOfGalaxy,TopCornerX+(WNDWidth DIV 3)-1);
      BotCornerY:=LesserInt(SizeOfGalaxy,TopCornerY+WNDHeight-1);

      CurX:=CapXY.x;
      CurY:=CapXY.y;
      LocX:=(3*(CurX-TopCornerX))+2;
      LocY:=(CurY-TopCornerY)+1;
      END;  { with scope }
   END;  { InitializeMapPos }

PROCEDURE CenterMap(VAR MAP: MapStructure; CapXY: XYCoord);
   BEGIN
   ActivateWindow(MAP.Handle);
   InitializeMapPos(MAP,CapXY);
   DrawMapWindow(MAP);
   END;  { CenterMap }

PROCEDURE OpenMapWindow(VAR MAP: MapStructure; CapXY: XYCoord);
   BEGIN
   WITH MAP DO
      BEGIN
      IF Handle=0 THEN
         BEGIN
         OpenWindow(InitX,InitY,InitWidth,InitHeight,NoBRD,'',Col,LightGray,Handle);
         WNDPosX:=InitX;
         WNDPosY:=InitY;
         WNDWidth:=InitWidth;
         WNDHeight:=InitHeight;

         InitializeMapPos(MAP,CapXY);
         END
      ELSE
         BEGIN
         ActivateWindow(Handle);
         END;
      DrawMapWindow(MAP);
      END;  { with scope }
   END;  { OpenMapWindow }

PROCEDURE CloseMapWindow(VAR MAP: MapStructure);
   BEGIN
   WITH MAP DO
      BEGIN
      IF Handle<>0 THEN
         BEGIN
         ActivateWindow(Handle);
         CloseWindow;
         Handle:=0;
         END;
      END;
   END;  { CloseMapWindow }

PROCEDURE GetMapObject(XY: XYCoord; VAR ID: IDNumber);
	VAR
		Menu: MenuStructure;
		PotentialID: IDList;
		ListSize: Word;
		Exit: Boolean;
		OldModalInput: Boolean;

   procedure CreateMenu(XY: XYCoord;
                        VAR Menu: MenuStructure;
                        VAR PotentialID: IDList;
                        VAR ListSize: Word);
      VAR
         Fleets: FleetSet;
         Flt2,Obj2: IDNumber;
         Emp: Empire;
         i: Word;

      PROCEDURE AddID(ID: IDNumber;
                      Emp: Empire;
                      VAR Menu: MenuStructure;
                      VAR PotentialID: IDList;
                      VAR ListSize: Word);

         VAR
            Line: LineStr;

         BEGIN
			Inc(ListSize);
         PotentialID[ListSize]:=ID;

         Line:=ObjectName(Player, ID, LongFormat);
         Line:=Line+'  ('+EmpireName(Emp)+')';
         AddMenuLine(Menu, Line);
         END;  { AddID }

      BEGIN
      InitializeMenu(Menu);
      ListSize:=0;

      { add object }
      GetObject(XY,Obj2);
      IF (Obj2.ObjTyp IN [Pln,Base]) AND Known (Player, Obj2) THEN
         BEGIN
         Emp:=GetStatus(Obj2);
  	      AddID (Obj2, Emp, Menu, PotentialID, ListSize);
         END;

      Flt2.ObjTyp:=Flt;
      GetFleets(XY,Fleets);

      { add fleets to menu }
      FOR i:=1 TO MaxNoOfFleets DO
         IF i IN Fleets THEN
            BEGIN
            Flt2.Index:=i;
            IF (Known(Player,Flt2) OR Scouted(Player,Flt2)) THEN
					BEGIN
	            Emp:=GetStatus(Flt2);
               AddID (Flt2, Emp, Menu, PotentialID, ListSize);
					END;
            END;
      END;  { CreateMenu }

	BEGIN
	CreateMenu (XY, Menu, PotentialID, ListSize);
	IF (ListSize > 0) THEN
		BEGIN
		OldModalInput:=ModalInput;
		ModalInput:=True;
		GetIDMenuChoice (Menu, PotentialID, ID, Exit);
		ModalInput:=OldModalInput;
		IF Exit THEN
			ID:=EmptyQuadrant;
		END
	ELSE
		ID:=EmptyQuadrant;
	END;  { GetMapObject }

PROCEDURE SelectPoint(VAR MAP: MapStructure);
   VAR
      XY: XYCoord;
      XYStr: String32;
		ID: IDNumber;

   BEGIN
   WITH MAP DO
      BEGIN
      ActivateWindow(Handle);
      XY.x:=CurX;
      XY.y:=CurY;

		IF InCommand THEN
			BEGIN
			GetCoordName (XY, XYStr);
  	   	AddToInputStream (EscKey+XYStr+ReturnKey);
			END
		ELSE IF NOT InProcess THEN
			BEGIN
			GetMapObject(XY,ID);
			IF (SameID(ID,EmptyQuadrant)) THEN
				BEGIN
				END
			ELSE
				BEGIN
				XYStr:=ObjectName (Player,ID,ShortFormat);
   	   	AddToInputStream (EscKey+'WC'+XYStr+ReturnKey);
				END;
			END;
      END;
   END;  { SelectPoint }

PROCEDURE DeActivateWindow(VAR MAP: MapStructure);
   BEGIN
   WITH MAP DO
      ActivateWindow(Handle);
   EraseMapCursor(MAP);
   END;  { DeActivateWindow }

PROCEDURE ScanWindow(VAR MAP: MapStructure; Comm: Word);
   VAR
      CapID: IDNumber;
      CapXY: XYCoord;

   { ScanWindow: MAIN PROCEDURE }
   BEGIN
   GetCapital(Player,CapID);
   GetCoord(CapID,CapXY);

   CASE Comm OF
            CloseWCM: CloseMapWindow(MAP);
       CursorDownWCM: MoveCursorDown(MAP);
       CursorLeftWCM: MoveCursorLeft(MAP);
      CursorRightWCM: MoveCursorRight(MAP);
         CursorUpWCM: MoveCursorUp(MAP);
             OpenWCM: OpenMapWindow(MAP,CapXY);
         PageDownWCM: FastMoveDown(MAP);
           PageUpWCM: FastMoveUp(MAP);
        PageRightWCM: FastMoveRight(MAP);
         PageLeftWCM: FastMoveLeft(MAP);
           CenterWCM: CenterMap(MAP,CapXY);
           SelectWCM: SelectPoint(MAP);
            DeActWCM: DeActivateWindow(MAP);
      END;  { case }
   END;  { ScanWindow }

PROCEDURE UMFleets(x,y: Byte; VAR MAP: MapStructure);
{ UMFleets:
   This procedure will draw a fleet if there is a fleet there, or erase
   one if there isn't.  The grid will be preserved.  This procedure should not
   be called outside of PlayerTakesTurn.  }

   VAR
      Neb,PlayerFleet,EnemyFleet: Boolean;

   FUNCTION EnemyFleetInSector(x,y: Byte): Boolean;
      VAR
         EnemyFleets: FleetSet;
         FltID: IDNumber;
         FltXY: XYcoord;

      BEGIN
      FltXY.x:=x;
      FltXY.y:=y;
      GetFleets(FltXY,EnemyFleets);
      EnemyFleets:=EnemyFleets - SetOfFleetsOf[Player];
      WITH FltID DO
         BEGIN
         ObjTyp:=Flt;
         Index:=MaxNoOfFleets;
         WHILE (Index>0) AND ((NOT (Index IN EnemyFleets)) OR (NOT Known(Player,FltID))) DO
            Dec(Index);

         IF Index=0 THEN
            EnemyFleetInSector:=False
         ELSE
            EnemyFleetInSector:=True;
         END;
      END;  { EnemyFleetInSector }

   PROCEDURE SetFltCharAndColor(Neb: NebulaTypes; Flt,Grd: Boolean; FChar,NChar: Char; FColor: Byte;
                                VAR FltChar: Char; VAR FltColor: Byte);
      BEGIN
      IF Flt THEN
         BEGIN
         FltChar:=FChar;
         IF FltChar=PlayerFleetChar THEN
            FltColor:=C.PlayerFleetColor
         ELSE
            FltColor:=C.EnemyColor;
         END
      ELSE IF (NChar IN ['0'..'z']) THEN
         BEGIN
         FltChar:=NChar;
         FltColor:=FColor;
         END
      ELSE IF Neb<>NoNeb THEN
         BEGIN
         FltChar:=NebulaChar[Neb];
         FltColor:=C.NebulaColor;
         END
      ELSE IF Grd THEN
         BEGIN
         FltChar:=CrossChar1;
         FltColor:=C.MapBackground;
         END
      ELSE
         BEGIN
         FltChar:=BlankChar;
         FltColor:=FColor;
         END;
      END;  { FltChar }

   BEGIN
   WITH MAP,Sector[x]^[y] DO
      BEGIN
      SetFltCharAndColor(NebulaTypes(Special MOD 16),
                         Player IN Flts,
                         ((RelativeX(x) MOD GridSep)=0) AND ((RelativeY(y) MOD GridSep)=0),
                         PlayerFleetChar,
                         Buffer[y,x].PlayerFltChar,
                         Buffer[y,x].PlayerFltCharColor,
                         Buffer[y,x].PlayerFltChar,
                         Buffer[y,x].PlayerFltCharColor);

      SetFltCharAndColor(NebulaTypes(Special MOD 16),
                         (Buffer[y,x].EnemyFltChar=EnemyFleetChar) AND (EnemyFleetInSector(x,y)),
                         ((RelativeX(x) MOD GridSep)=0) AND ((RelativeY(y) MOD GridSep)=0),
                         EnemyFleetChar,
                         Buffer[y,x].EnemyFltChar,
                         Buffer[y,x].EnemyFltCharColor,
                         Buffer[y,x].EnemyFltChar,
                         Buffer[y,x].EnemyFltCharColor);
      END;  { with scope }
   END;  { UMFleets }

PROCEDURE UMSector(x,y: Byte; VAR MAP: MapStructure);
   VAR
      SectMined: Boolean;
      Neb: NebulaTypes;

   BEGIN
   WITH MAP,Sector[x]^[y] DO
      BEGIN
      SectMined:=(Empire(Special DIV 16)=Player);
      Neb:=NebulaTypes(Special MOD 16);
      IF Known(Player,Obj) OR SectMined THEN
         BEGIN
         IF SectMined THEN
            BEGIN
            Buffer[y,x].WorldChar:=MineChar;
            Buffer[y,x].WorldCharColor:=C.PlayerColor;
            END
         ELSE
            BEGIN
            IF Obj.ObjTyp=Pln THEN
               Buffer[y,x].WorldChar:=TypeStr[GetType(Obj)]
            ELSE IF Obj.ObjTyp=Base THEN
               Buffer[y,x].WorldChar:=BaseTypeData[GetBaseType(Obj)]
            ELSE IF Obj.ObjTyp=Con THEN
               Buffer[y,x].WorldChar:=ConsChar
            ELSE IF Obj.ObjTyp=Gate THEN
               Buffer[y,x].WorldChar:=GateTypeData[GetGateType(Obj)];

            IF GetStatus(Obj)=Player THEN
               Buffer[y,x].WorldCharColor:=C.PlayerColor
            ELSE
               Buffer[y,x].WorldCharColor:=C.EnemyColor;
            END;
         END
      ELSE IF (Obj.ObjTyp=Pln) AND (Neb<>NoNeb) THEN
         BEGIN
         Buffer[y,x].WorldChar:=UnkPlanetChar;
         Buffer[y,x].WorldCharColor:=C.UnscoutedColor;
         END
      ELSE IF (Buffer[y,x].WorldChar IN ['0'..'z']) THEN
         BEGIN END
      ELSE IF Neb<>NoNeb THEN
         BEGIN
         Buffer[y,x].WorldChar:=NebulaChar[Neb];
         Buffer[y,x].WorldCharColor:=C.NebulaColor;
         END
      ELSE IF (RelativeX(x) MOD GridSep)=0 THEN
         BEGIN
         IF (RelativeY(y) MOD GridSep)=0 THEN
            BEGIN
            Buffer[y,x].WorldChar:=CrossChar2;
            Buffer[y,x].WorldCharColor:=C.MapBackground;
            END
         ELSE
            BEGIN
            Buffer[y,x].WorldChar:=VertChar;
            Buffer[y,x].WorldCharColor:=C.MapBackground;
            END;
         END
      ELSE IF (RelativeY(y) MOD GridSep)=0 THEN
         BEGIN
         Buffer[y,x].WorldChar:=HorzChar;
         Buffer[y,x].WorldCharColor:=C.MapBackground;
         END
      ELSE
         Buffer[y,x].WorldChar:=BlankChar;
      END;  { with scope }
   END;  { UMSector }

PROCEDURE UMAddName(VAR Map: MapStructure; Name: String16; XY: XYCoord);
   TYPE
      MapArray2 = ARRAY [1..MaxSizeOfGalaxy,1..MaxSizeOfGalaxy2] OF Char;

   VAR
      x1,y1: Integer;
      Map2: ^MapArray2;

   FUNCTION NameFits(Len: Byte; x,y: Integer): Boolean;
      VAR
         i: Byte;
         x1: Word;

      BEGIN
      IF (x+2*(Len-1))>(SizeOfGalaxy*6) THEN
         NameFits:=False
      ELSE IF (x<1) OR (y<1) OR (y>SizeOfGalaxy) THEN
         NameFits:=False
      ELSE
         BEGIN
         x1:=x;
         i:=0;
         WHILE (i<Len) AND (Map2^[y,x1] IN [BlankChar,CrossChar1,CrossChar2,CrossChar3,HorzChar,VertChar,
               NebulaChar[Nebula],NebulaChar[DarkNebula]]) DO
            BEGIN
            Inc(i);
            Inc(x1,2);
            END;

         IF i=Len THEN
            NameFits:=True
         ELSE
            NameFits:=False;
         END;
      END;  { NameFits }

   PROCEDURE WriteName(Name: String16; x,y: Integer);
      VAR
         i: Byte;

      BEGIN
      FOR i:=0 TO Length(Name)-1 DO
         Map2^[y,x+i*2]:=Name[i+1];
      END;  { PutName }

   BEGIN
   Map2:=Pointer(@Map.Buffer);
   x1:=6*XY.x-5;  y1:=XY.y;

   IF NameFits(Length(Name)+1,x1+6,y1) THEN
      WriteName(Name,x1+6,y1)
   ELSE IF NameFits(Length(Name)+1,x1-(Length(Name)*2+2),y1) THEN
      WriteName(Name+'-',x1-(Length(Name)*2+2),y1)
   ELSE IF NameFits(Length(Name)+1,x1,y1+1) THEN
      WriteName(Name,x1,y1+1)
   ELSE IF NameFits(Length(Name)+1,x1,y1-1) THEN
      WriteName(Name,x1,y1-1);
   END;  { UMAddName }

BEGIN
New(MAP);
END.
