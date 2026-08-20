(* STAWIND.PAS -----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT StaWind;

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
   Misc,
   PrimIntr,
   Intrface,
   WndTypes;

TYPE
   StatusDataArray = ARRAY [1..500] OF IDNumber;

   StatusWindowStructurePtr = ^StatusWindowStructure;
   StatusWindowStructure = RECORD
      Handle: WindowHandle;
      Col: Word;
      Data: StatusDataArray;
      BeginIndex: Word;
      EndIndex: Word;
      EndData: Word;
      NoOfLines: Word;
   END;

VAR
   STA: StatusWindowStructurePtr;

PROCEDURE StatusWindow(VAR STA: StatusWindowStructure; Comm: Word);
PROCEDURE InitializeStatusWindow(VAR STA: StatusWindowStructure);

IMPLEMENTATION

USES
   Environ;

CONST
   InitX = 1;
   InitY = 4;
   InitWidth = 80;
   InitHeight = 21;

   InitTitle   = 'PlntName Sta C T Tl  Pop Eff A Impt Expt Rev  jtn  trn  amb  che  met  sup  tri ';
   DividingBar = 'PlntName Sta   men ninj  fgt  hkr  jmp  jtn  pen  str  trn  LAM  def  GDM  ion  ';

PROCEDURE InitializeStatusDataArray(VAR STA: StatusWindowStructure);
{ InitStatusDataArray:
   This procedure initializes Data which is a list of the ID
   numbers of the planets and starbases to be included in status. }

   TYPE
      ElementRecord = RECORD
         Pop: Byte;
         Tech: TechLevel;
         ID: IDNumber;
      END;

   VAR
      ID: IDNumber;
      i,DataIndex,ListIndex,ListMark: Integer;
      CapID: IDNumber;
      WorldSet: PlanetSet;
      TempList: ARRAY [1..500] OF ElementRecord;

   PROCEDURE AddToTempList(ID: IDNumber);
      BEGIN
      Inc(ListIndex);
      TempList[ListIndex].Tech:=GetTech(ID);
      TempList[ListIndex].Pop:=Hi(GetPopulation(ID));
      TempList[ListIndex].ID:=ID;
      END;  { AddToTempList }

   PROCEDURE SortSection;
      BEGIN
      IF ListIndex>ListMark THEN
         BEGIN
         QuickSortD(TempList,SizeOf(ElementRecord),ListMark,ListIndex);
         END;
      ListMark:=ListIndex+1;
      END;  { SortSection }

   BEGIN
   WITH STA DO
      BEGIN
      GetCapital(Player,CapID);
      { first line is Capital status }
      DataIndex:=1;
      Data[DataIndex]:=CapID;

      ListIndex:=0;
      ListMark:=1;
      WITH ID DO
         BEGIN
         ObjTyp:=Pln;
         FOR Index:=1 TO NoOfPlanets DO
            IF (Index IN SetOfPlanetsOf[Player]) AND (NOT SameID(ID,CapID)) THEN
               AddToTempList(ID);

         ObjTyp:=Base;
         FOR Index:=1 TO MaxNoOfStarbases DO
            IF (Index IN SetOfStarbasesOf[Player]) AND (NOT SameID(ID,CapID)) THEN
               AddToTempList(ID);
         SortSection;

         ObjTyp:=Pln;
         FOR Index:=1 TO NoOfPlanets DO
            IF (NOT (Index IN SetOfPlanetsOf[Player])) AND (Scouted(Player,ID)) THEN
               AddToTempList(ID);

         ObjTyp:=Base;
         WorldSet:=SetOfActiveStarbases - SetOfStarbasesOf[Player];
         FOR Index:=1 TO MaxNoOfStarbases DO
            IF (Index IN WorldSet) AND (Scouted(Player,ID)) THEN
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
      END;
   END;  { InitializeStatusDataArray }

PROCEDURE WriteStatus(VAR STA: StatusWindowStructure; ID: IDNumber; y: Byte);
   VAR
      Line: LineStr;

   BEGIN
   WITH STA DO
      BEGIN
      GetWorldStatus(Player,ID,Line);
      WriteString(Line,1,y,Col);
      GetMilitaryStatus(Player,ID,Line);
      WriteString(Line,1,y+NoOfLines+1,Col);
      END;
   END;  { WriteStatus }

PROCEDURE DrawStatusWindow(VAR STA: StatusWindowStructure);
   VAR
      i,LoopEnd: Word;

   BEGIN
   WITH STA DO
      BEGIN
      ClrScr;
      WriteString(DividingBar,1,NoOfLines+1,C.SYSTBorder);
      FOR i:=BeginIndex TO EndIndex DO
         WriteStatus(STA,Data[i],i-BeginIndex+1);
      END;
   END;  { DrawStatusWindow }

PROCEDURE OpenStatusWindow(VAR STA: StatusWindowStructure);
   BEGIN
   WITH STA DO
      BEGIN
      IF Handle=0 THEN
         OpenWindow(InitX,InitY,InitWidth,InitHeight,TextBRD,InitTitle,Col,C.SYSTBorder,Handle)
      ELSE
         ActivateWindow(Handle);

      InitializeStatusDataArray(STA);
      DrawStatusWindow(STA);
      END;
   END;  { OpenStatusWindow }

PROCEDURE CloseStatusWindow(VAR STA: StatusWindowStructure);
   BEGIN
   WITH STA DO
      BEGIN
      IF Handle<>0 THEN
         BEGIN
         ActivateWindow(Handle);
         CloseWindow;
         Handle:=0;
         END;
      END;
   END;  { CloseStatusWindow }

PROCEDURE ScrollPageUp(VAR STA: StatusWindowStructure);
   BEGIN
   WITH STA DO
      BEGIN
      ActivateWindow(Handle);
      IF EndIndex<EndData THEN
         BEGIN
         WriteBlanks(80,1,NoOfLines+1,Col);
         WriteBlanks(80,1,NoOfLines+2,Col);
         ScrollUp;
         WriteString(DividingBar,1,NoOfLines+1,C.SYSTBorder);
         Inc(BeginIndex);
         Inc(EndIndex);
         WriteStatus(STA,Data[EndIndex],NoOfLines);
         END;
      END;
   END;  { ScrollPageUp }

PROCEDURE ScrollPageDown(VAR STA: StatusWindowStructure);
   BEGIN
   WITH STA DO
      BEGIN
      ActivateWindow(Handle);
      IF BeginIndex>1 THEN
         BEGIN
         WriteBlanks(80,1,NoOfLines,Col);
         WriteBlanks(80,1,NoOfLines+1,Col);
         ScrollDown;
         WriteString(DividingBar,1,NoOfLines+1,C.SYSTBorder);
         Dec(BeginIndex);
         Dec(EndIndex);
         WriteStatus(STA,Data[BeginIndex],1);
         END;
      END;
   END;  { ScrollPageDown }

PROCEDURE HomePage(VAR STA: StatusWindowStructure);
   BEGIN
   WITH STA DO
      BEGIN
      ActivateWindow(Handle);
      BeginIndex:=1;
      EndIndex:=LesserInt(EndData,NoOfLines);
      DrawStatusWindow(STA);
      END;
   END;  { HomePage }

PROCEDURE EndPage(VAR STA: StatusWindowStructure);
   BEGIN
   WITH STA DO
      BEGIN
      ActivateWindow(Handle);
      BeginIndex:=GreaterInt(1,(Integer(EndData)-NoOfLines)+1);
      EndIndex:=EndData;
      DrawStatusWindow(STA);
      END;
   END;  { EndPage }

PROCEDURE NextPage(VAR STA: StatusWindowStructure);
   VAR
      Disp: Word;

   BEGIN
   WITH STA DO
      BEGIN
      ActivateWindow(Handle);
      Disp:=LesserInt(NoOfLines,EndData-EndIndex);
      IF Disp<>0 THEN
         BEGIN
         Inc(BeginIndex,Disp);
         Inc(EndIndex,Disp);
         DrawStatusWindow(STA);
         END;
      END;
   END;  { NextPage }

PROCEDURE PreviousPage(VAR STA: StatusWindowStructure);
   VAR
      Disp: Word;

   BEGIN
   WITH STA DO
      BEGIN
      ActivateWindow(Handle);
      Disp:=LesserInt(NoOfLines,BeginIndex-1);
      IF Disp<>0 THEN
         BEGIN
         Dec(BeginIndex,Disp);
         Dec(EndIndex,Disp);
         DrawStatusWindow(STA);
         END;
      END;
   END;  { PreviousPage }

PROCEDURE StatusWindow(VAR STA: StatusWindowStructure; Comm: Word);
   BEGIN
   CASE Comm OF
          CenterWCM: HomePage(STA);
           CloseWCM: CloseStatusWindow(STA);
             EndWCM: EndPage(STA);
            OpenWCM: OpenStatusWindow(STA);

          PageUpWCM: PreviousPage(STA);
        CursorUpWCM: ScrollPageDown(STA);

        PageDownWCM: NextPage(STA);
      CursorDownWCM: ScrollPageUp(STA);
      END;  { case }
   END;  { StatusWindow }

PROCEDURE InitializeStatusWindow(VAR STA: StatusWindowStructure);
   BEGIN
   WITH STA DO
      BEGIN
      Handle:=0;
      Col:=C.StatWind3;
      NoOfLines:=(InitHeight DIV 2)-1;
      END;
   END;  { initializeStatusWindow }

BEGIN
New(STA);
END.
