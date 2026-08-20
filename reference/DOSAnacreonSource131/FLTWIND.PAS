(* FLTWIND.PAS -----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT FltWind;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES
   Strg,
   Int,
   CRT,
   EIO,
   WND,
   QSort,
   Types,
   Galaxy,
   PrimIntr,
   Intrface,
   WndTypes;

TYPE
   FleetDataArray = ARRAY [1..MaxNoOfFleets] OF IDNumber;

   FleetWindowStructurePtr = ^FleetWindowStructure;
   FleetWindowStructure = RECORD
      Handle: WindowHandle;
      Col: Word;
      Data: FleetDataArray;
      BeginIndex: Word;
      EndIndex: Word;
      EndData: Word;
      NoOfLines: Word;
   END;

VAR
   FLT: FleetWindowStructurePtr;

PROCEDURE FleetWindow(VAR FLT: FleetWindowStructure; Comm: Word);
PROCEDURE InitializeFleetWindow(VAR FLT: FleetWindowStructure);

IMPLEMENTATION

USES
   Environ;

CONST
   InitX = 1;
   InitY = 4;
   InitWidth = 80;
   InitHeight = 21;

   InitTitle   = '     Fleet      Pos        Des        Status              Range                 ';
   DividingBar = 'Fleet     fgt   hk  jmp  jtn  pen  str  trn  men ninj  amb  che  met  sup  tri  ';

PROCEDURE InitializeFleetDataArray(VAR FLT: FleetWindowStructure);
   TYPE
      ElementRecord = RECORD
         XY: XYCoord;
         ID: IDNumber;
      END;

   VAR
      ID: IDNumber;
      DataIndex,i,ListIndex,ListMark: Byte;
      EnemyFleets,ActiveFleets: FleetSet;
      TempList: ARRAY [1..MaxNoOfFleets] OF ElementRecord;

   PROCEDURE AddToTempList(ID: IDNumber);
      BEGIN
      Inc(ListIndex);
      GetCoord(ID,TempList[ListIndex].XY);
      TempList[ListIndex].ID:=ID;
      END;

   PROCEDURE SortSection;
      BEGIN
      IF ListIndex>ListMark THEN
         QuickSortD(TempList,SizeOf(ElementRecord),ListMark,ListIndex);
      ListMark:=ListIndex+1;
      END;  

   BEGIN
   WITH FLT DO
      BEGIN
      ActiveFleets:=SetOfFleetsOf[Player] * SetOfActiveFleets;
      EnemyFleets:=SetOfActiveFleets - SetOfFleetsOf[Player];
      DataIndex:=0;

      ListIndex:=0;
      ListMark:=1;

      WITH ID DO
         BEGIN
         ObjTyp:=Types.Flt;
         FOR Index:=1 TO MaxNoOfFleets DO
            IF Index IN ActiveFleets THEN
               AddToTempList(ID);

         SortSection;

         ObjTyp:=Base;
         FOR Index:=1 TO MaxNoOfStarbases DO
            IF (Index IN SetOfStarbasesOF[Player]) AND (GetBaseType(ID) IN [cmm,frt]) THEN
               AddToTempList(ID);

         SortSection;

         ObjTyp:=Types.Flt;
         FOR Index:=1 TO MaxNoOfFleets DO
            IF (Index IN EnemyFleets) AND Scouted(Player,ID) THEN
               AddToTempList(ID);

         SortSection;

         FOR Index:=1 TO MaxNoOfFleets DO
            IF (Index IN EnemyFleets) AND (NOT Scouted(Player,ID)) AND Known(Player,ID) THEN
               AddToTempList(ID);

         SortSection;

         FOR i:=1 TO ListIndex DO
            BEGIN
            Inc(DataIndex);
            Data[DataIndex]:=TempList[i].ID;
            END;
         END;  { with scope }

      EndData:=DataIndex;
      BeginIndex:=1;
      EndIndex:=LesserInt(EndData,NoOfLines);
      END;  { with scope }
   END;  { InitializeFleetDataArray }

PROCEDURE WriteStatus(VAR FLT: FleetWindowStructure; ID: IDNumber; y: Byte);
   VAR
      Line: LineStr;

   BEGIN
   WITH FLT DO
      BEGIN
      GetFleetPositionStatus(Player,ID,Line);
      WriteString(Line,1,y,Col);
      GetFleetStatusLine(Player,ID,Line);
      WriteString(Line,1,y+NoOfLines+1,Col);
      END;
   END;  { WriteStatus }

PROCEDURE DrawFleetWindow(VAR FLT: FleetWindowStructure);
   VAR
      i,LoopEnd: Word;

   BEGIN
   WITH FLT DO
      BEGIN
      ClrScr;
      WriteString(DividingBar,1,NoOfLines+1,C.SYSTBorder);
      IF EndData=0 THEN
         BEGIN
         WriteString('No fleets have been deployed.',1,2,Col);
         WriteString('No fleets have been deployed.',1,3+NoOfLines,Col);
         END
      ELSE
         BEGIN
         FOR i:=BeginIndex TO EndIndex DO
            WriteStatus(FLT,Data[i],i-BeginIndex+1);
         END;
      END;
   END;  { DrawFleetWindow }

PROCEDURE OpenFleetWindow(VAR FLT: FleetWindowStructure);
   BEGIN
   WITH FLT DO
      BEGIN
      IF Handle=0 THEN
         OpenWindow(InitX,InitY,InitWidth,InitHeight,TextBRD,InitTitle,Col,C.SYSTBorder,Handle)
      ELSE
         ActivateWindow(Handle);

      InitializeFleetDataArray(FLT);
      DrawFleetWindow(FLT);
      END;
   END;  { OpenFleetWindow }

PROCEDURE CloseFleetWindow(VAR FLT: FleetWindowStructure);
   BEGIN
   WITH FLT DO
      BEGIN
      IF Handle<>0 THEN
         BEGIN
         ActivateWindow(Handle);
         CloseWindow;
         Handle:=0;
         END;
      END;
   END;  { CloseFleetWindow }

PROCEDURE ScrollPageUp(VAR FLT: FleetWindowStructure);
   BEGIN
   WITH FLT DO
      BEGIN
      ActivateWindow(Handle);
      IF EndIndex<EndData THEN
         BEGIN
         WriteBlanks(80,1,NoOfLines+1,Col);
         ScrollUp;
         WriteBlanks(80,1,NoOfLines,Col);
         WriteBlanks(80,1,NoOfLines*2+1,Col);
         WriteString(DividingBar,1,NoOfLines+1,C.SYSTBorder);
         Inc(BeginIndex);
         Inc(EndIndex);
         WriteStatus(FLT,Data[EndIndex],NoOfLines);
         END;
      END;
   END;  { ScrollPageUp }

PROCEDURE ScrollPageDown(VAR FLT: FleetWindowStructure);
   BEGIN
   WITH FLT DO
      BEGIN
      ActivateWindow(Handle);
      IF BeginIndex>1 THEN
         BEGIN
         WriteBlanks(80,1,NoOfLines+1,Col);
         ScrollDown;
         WriteBlanks(80,1,1,Col);
         WriteBlanks(80,1,NoOfLines+2,Col);
         WriteString(DividingBar,1,NoOfLines+1,C.SYSTBorder);
         Dec(BeginIndex);
         Dec(EndIndex);
         WriteStatus(FLT,Data[BeginIndex],1);
         END;
      END;
   END;  { ScrollPageDown }

PROCEDURE HomePage(VAR FLT: FleetWindowStructure);
   BEGIN
   WITH FLT DO
      BEGIN
      ActivateWindow(Handle);
      BeginIndex:=1;
      EndIndex:=LesserInt(EndData,NoOfLines);
      DrawFleetWindow(FLT);
      END;
   END;  { HomePage }

PROCEDURE EndPage(VAR FLT: FleetWindowStructure);
   BEGIN
   WITH FLT DO
      BEGIN
      ActivateWindow(Handle);
      BeginIndex:=GreaterInt(1,(Integer(EndData)-NoOfLines)+1);
      EndIndex:=EndData;
      DrawFleetWindow(FLT);
      END;
   END;  { EndPage }

PROCEDURE NextPage(VAR FLT: FleetWindowStructure);
   VAR
      Disp: Word;

   BEGIN
   WITH FLT DO
      BEGIN
      ActivateWindow(Handle);
      Disp:=LesserInt(NoOfLines,EndData-EndIndex);
      IF Disp<>0 THEN
         BEGIN
         Inc(BeginIndex,Disp);
         Inc(EndIndex,Disp);
         DrawFleetWindow(FLT);
         END;
      END;
   END;  { NextPage }

PROCEDURE PreviousPage(VAR FLT: FleetWindowStructure);
   VAR
      Disp: Word;

   BEGIN
   WITH FLT DO
      BEGIN
      ActivateWindow(Handle);
      Disp:=LesserInt(NoOfLines,BeginIndex-1);
      IF Disp<>0 THEN
         BEGIN
         Dec(BeginIndex,Disp);
         Dec(EndIndex,Disp);
         DrawFleetWindow(FLT);
         END;
      END;
   END;  { PreviousPage }

PROCEDURE FleetWindow(VAR FLT: FleetWindowStructure; Comm: Word);
   BEGIN
   CASE Comm OF
          CenterWCM: HomePage(FLT);
           CloseWCM: CloseFleetWindow(FLT);
             EndWCM: EndPage(FLT);
            OpenWCM: OpenFleetWindow(FLT);

          PageUpWCM: PreviousPage(FLT);
        CursorUpWCM: ScrollPageDown(FLT);

        PageDownWCM: NextPage(FLT);
      CursorDownWCM: ScrollPageUp(FLT);
      END;  { case }
   END;  { FleetWindow }

PROCEDURE InitializeFleetWindow(VAR FLT: FleetWindowStructure);
   BEGIN
   WITH FLT DO
      BEGIN
      Handle:=0;
      Col:=C.StatWind3;
      NoOfLines:=(InitHeight DIV 2)-1;
      END;
   END;  { initializeFleetWindow }

BEGIN
New(FLT);
END.
