(* NMSWIND.PAS -----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT NmsWind;

{$IFDEF Overlay}
{$F+,O+}
{$ENDIF}

INTERFACE

USES
   Strg,
   Int,
   CRT,
   EIO,
   WND,
   Types,
   DataStrc,
   PrimIntr,
   Intrface,
   WndTypes;

TYPE
   NamesStructure = RECORD
      Handle: WindowHandle;
      Col: Word;
      Data: ARRAY [1..200] OF NameRecordPtr;

      NoOfLines: Word;
      BeginIndex: Word;
      EndIndex: Word;
      EndData: Word;
   END;

VAR
   NMS: ^NamesStructure;

PROCEDURE InitializeNamesWindow(VAR NMS: NamesStructure);
PROCEDURE NamesWindow(VAR NMS: NamesStructure; Comm: Word);

IMPLEMENTATION

USES
   Environ;

CONST
   InitX = 1;
   InitY = 6;
   InitWidth = 30;
   InitHeight = 19;
   InitTitle = 'Names';

PROCEDURE InitNamesDataArray(VAR NMS: NamesStructure);
   VAR
      NextItem: NameRecordPtr;
      DataIndex: Byte;

   BEGIN
   WITH NMS DO
      BEGIN
      DataIndex:=0;
      NextItem:=Universe^.EmpireData[Player].Names;

      WHILE NextItem<>Nil DO
         BEGIN
         IF NextItem^.Coord.ID.ObjTyp=Flt THEN
            BEGIN
            DataIndex:=DataIndex+1;
            Data[DataIndex]:=NextItem;
            END;
         NextItem:=NextItem^.Next;
         END;  { while }

      NextItem:=Universe^.EmpireData[Player].Names;
      WHILE (NextItem<>Nil) DO
         BEGIN
         IF NextItem^.Coord.ID.ObjTyp<>Flt THEN
            BEGIN
            DataIndex:=DataIndex+1;
            Data[DataIndex]:=NextItem;
            END;
         NextItem:=NextItem^.Next;
         END;  { while }

      EndData:=DataIndex;
      BeginIndex:=1;
      EndIndex:=LesserInt(EndData,NoOfLines);
      END;  { with scope }
   END;  { InitNamesDataArray }

PROCEDURE GetNameLine(Emp: Empire; NamePtr: NameRecordPtr;
                      VAR Line: LineStr);
{ INTERFACE PROCEDURE: GetNameLine: }

   VAR
      NameStr,CoordStr: String16;

   { GetNameList: MAIN PROCEDURE }
   BEGIN
   NameStr:=NamePtr^.Name;
   AdjustString(NameStr,8);
   IF NamePtr^.Coord.ID.ObjTyp=Flt THEN
      GetFleetName(Player,NamePtr^.Coord.ID,CoordStr)
   ELSE IF NamePtr^.Coord.ID.ObjTyp=DestFlt THEN
      CoordStr:='(destroyed)'
   ELSE
      GetCoordName(NamePtr^.Coord.XY,CoordStr);

   AdjustString(CoordStr,12);
   Line:=NameStr+'     '+CoordStr;
   END;  { GetNameLine }

PROCEDURE WriteLine(VAR NMS: NamesStructure; Item: NameRecordPtr; x,y: Byte);
   VAR
      Line: LineStr;

   BEGIN
   WITH NMS DO
      BEGIN
      GetNameLine(Player,Item,Line);

      WriteBlanks(InitWidth-2,1,y,Col);
      WriteString(Line,x,y,Col);
      END;
   END;  { WriteLine }

PROCEDURE DrawNamesWindow(VAR NMS: NamesStructure);
   VAR
      i,LoopEnd: Integer;

   BEGIN
   ClrScr;
   WITH NMS DO
      IF EndData=0 THEN
         BEGIN
         Writeln;
         Writeln('No names have been defined.');
         BeginIndex:=0;
         EndIndex:=0;
         END
      ELSE
         BEGIN
         BeginIndex:=1;
         LoopEnd:=LesserInt(EndData,NoOfLines);

         FOR i:=1 TO LoopEnd DO
            WriteLine(NMS,Data[i],1,i);

         EndIndex:=LoopEnd;
         END;
   END;  { DrawNamesWindow }

PROCEDURE CloseNamesWindow(VAR NMS: NamesStructure);
   BEGIN
   WITH NMS DO
      BEGIN
      IF Handle<>0 THEN
         BEGIN
         ActivateWindow(Handle);
         CloseWindow;
         Handle:=0;
         END;
      END;
   END;  { CloseNamesWindow }

PROCEDURE OpenNamesWindow(VAR NMS: NamesStructure);
   BEGIN
   WITH NMS DO
      BEGIN
      IF Handle=0 THEN
         OpenWindow(InitX,InitY,InitWidth,InitHeight,ThinBRD,InitTitle,Col,C.SYSWBorder,Handle)
      ELSE
         ActivateWindow(Handle);

      InitNamesDataArray(NMS);
      DrawNamesWindow(NMS);
      END;
   END;  { OpenNamesWindow }

PROCEDURE ScrollPageDown(VAR NMS: NamesStructure);
   BEGIN
   WITH NMS DO
      BEGIN
      ActivateWindow(Handle);
      IF EndIndex<EndData THEN
         BEGIN
         ScrollUp;
         Inc(BeginIndex);
         Inc(EndIndex);
         WriteLine(NMS,Data[EndIndex],1,NoOfLines);
         END;
      END;
   END;  { ScrollPageDown }

PROCEDURE ScrollPageUp(VAR NMS: NamesStructure);
   BEGIN
   WITH NMS DO
      BEGIN
      ActivateWindow(Handle);
      IF BeginIndex>1 THEN
         BEGIN
         ScrollDown;
         Dec(BeginIndex);
         Dec(EndIndex);
         WriteLine(NMS,Data[BeginIndex],1,1);
         END;
      END;
   END;  { ScrollPageUp }

PROCEDURE InitializeNamesWindow(VAR NMS: NamesStructure);
   BEGIN
   WITH NMS DO
      BEGIN
      Handle:=0;
      Col:=C.StatWind3;
      NoOfLines:=InitHeight-2;
      END;
   END;  { InitializeNamesWindow }

PROCEDURE NamesWindow(VAR NMS: NamesStructure; Comm: Word);
   BEGIN
   CASE Comm OF
              CloseWCM: CloseNamesWindow(NMS);
               OpenWCM: OpenNamesWindow(NMS);

             PageUpWCM,
            ScrllUpWCM,
           CursorUpWCM: ScrollPageUp(NMS);

           PageDownWCM,
          ScrllDownWCM,
         CursorDownWCM: ScrollPageDown(NMS);
      END;  { case }
   END;  { NamesWindow }

BEGIN
New(NMS);
END.
