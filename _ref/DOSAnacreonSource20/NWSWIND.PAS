(* NWSWIND.PAS -----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT NwsWind;

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
   Types,
   News,
   Intrface,
   WndTypes;

TYPE
   NewsStructure = RECORD
      Handle: WindowHandle;
      Col: Word;
      Data: ARRAY [1..500] OF NewsRecordPtr;

      NoOfLines: Word;
      BeginIndex: Word;
      EndIndex: Word;
      EndData: Word;
   END;

VAR
   NWS: ^NewsStructure;

PROCEDURE InitializeNewsWindow(VAR NWS: NewsStructure);
PROCEDURE NewsWindow(VAR NWS: NewsStructure; Comm: Word);

IMPLEMENTATION

USES
   Environ;

CONST
   InitX = 1;
   InitY = 4;
   InitWidth = 80;
   InitHeight = 21;
   InitTitle = 'News                                                                            ';

PROCEDURE InitNewsDataArray(VAR NWS: NewsStructure);
   VAR
      NextItem: NewsRecordPtr;
      DataIndex: Byte;

   BEGIN
   WITH NWS DO
      BEGIN
      DataIndex:=0;

      GetNewsList(Player,NextItem);
      WHILE NextItem<>Nil DO
         BEGIN
			IF NOT (NextItem^.Headline IN LocalNews) THEN
				BEGIN
	         DataIndex:=DataIndex+1;
   	      Data[DataIndex]:=NextItem;
				END;
     	   NextItem:=NextItem^.Next;
         END;  { while }

      GetNewsList(Player,NextItem);
      WHILE NextItem<>Nil DO
         BEGIN
			IF (NextItem^.Headline IN LocalNews) THEN
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
   END;  { InitNewsDataArray }

PROCEDURE WriteLine(VAR NWS: NewsStructure; Item: NewsRecordPtr; x,y: Byte);
   VAR
      Line: LineStr;

   BEGIN
   WITH NWS DO
      BEGIN
      GetNewsLine(Player,Item,Line);
      WriteString(Line,x,y,Col);
      END;
   END;  { WriteLine }

PROCEDURE DrawNewsWindow(VAR NWS: NewsStructure);
   VAR
      i,LoopEnd: Integer;

   BEGIN
   WITH NWS DO
      BEGIN
      ClrScr;
      IF EndData=0 THEN
         BEGIN
         Writeln;
         Writeln('No news.');
         BeginIndex:=0;
         EndIndex:=0;
         END
      ELSE
         BEGIN
         FOR i:=BeginIndex TO EndIndex DO
            WriteLine(NWS,Data[i],1,i-BeginIndex+1);
         END;
      END;
   END;  { DrawNewsWindow }

PROCEDURE CloseNewsWindow(VAR NWS: NewsStructure);
   BEGIN
   WITH NWS DO
      BEGIN
      IF Handle<>0 THEN
         BEGIN
         ActivateWindow(Handle);
         CloseWindow;
         Handle:=0;
         END;
      END;
   END;  { CloseNewsWindow }

PROCEDURE OpenNewsWindow(VAR NWS: NewsStructure);
   BEGIN
   WITH NWS DO
      BEGIN
      IF Handle=0 THEN
         BEGIN
         OpenWindow(InitX,InitY,InitWidth,InitHeight,TextBRD,InitTitle,Col,C.SYSTBorder,Handle);
         InitNewsDataArray(NWS);
         DrawNewsWindow(NWS);
         END
      ELSE
         ActivateWindow(Handle);
      END;
   END;  { OpenNewsWindow }

PROCEDURE ScrollPageDown(VAR NWS: NewsStructure);
   BEGIN
   WITH NWS DO
      BEGIN
      ActivateWindow(Handle);
      IF EndIndex<EndData THEN
         BEGIN
         ScrollUp;
         Inc(BeginIndex);
         Inc(EndIndex);
         WriteBlanks(80,1,NoOfLines,Col);
         WriteLine(NWS,Data[EndIndex],1,NoOfLines);
         END;
      END;
   END;  { ScrollPageDown }

PROCEDURE ScrollPageUp(VAR NWS: NewsStructure);
   BEGIN
   WITH NWS DO
      BEGIN
      ActivateWindow(Handle);
      IF BeginIndex>1 THEN
         BEGIN
         ScrollDown;
         Dec(BeginIndex);
         Dec(EndIndex);
         WriteBlanks(80,1,1,Col);
         WriteLine(NWS,Data[BeginIndex],1,1);
         END;
      END;
   END;  { ScrollPageUp }

PROCEDURE PreviousPage(VAR NWS: NewsStructure);
   VAR
      Disp: Word;

   BEGIN
   WITH NWS DO
      BEGIN
      ActivateWindow(Handle);
      Disp:=LesserInt(NoOfLines,BeginIndex-1);
      IF Disp<>0 THEN
         BEGIN
         Dec(BeginIndex,Disp);
         Dec(EndIndex,Disp);
         DrawNewsWindow(NWS);
         END;
      END;
   END;  { PreviousPage }

PROCEDURE NextPage(VAR NWS: NewsStructure);
   VAR
      Disp: Word;

   BEGIN
   WITH NWS DO
      BEGIN
      ActivateWindow(Handle);
      Disp:=LesserInt(NoOfLines,EndData-EndIndex);
      IF Disp<>0 THEN
         BEGIN
         Inc(BeginIndex,Disp);
         Inc(EndIndex,Disp);
         DrawNewsWindow(NWS);
         END;
      END;
   END;  { NextPage }

PROCEDURE HomePage(VAR NWS: NewsStructure);
   BEGIN
   WITH NWS DO
      BEGIN
      ActivateWindow(Handle);
      BeginIndex:=1;
      EndIndex:=LesserInt(EndData,NoOfLines);
      DrawNewsWindow(NWS);
      END;
   END;  { HomePage }

PROCEDURE EndPage(VAR NWS: NewsStructure);
   BEGIN
   WITH NWS DO
      BEGIN
      ActivateWindow(Handle);
      BeginIndex:=GreaterInt(1,(Integer(EndData)-NoOfLines)+1);
      EndIndex:=EndData;
      DrawNewsWindow(NWS);
      END;
   END;  { EndPage }

PROCEDURE InitializeNewsWindow(VAR NWS: NewsStructure);
   BEGIN
   WITH NWS DO
      BEGIN
      Handle:=0;
      Col:=C.StatWind3;
      NoOfLines:=InitHeight-2;
      END;
   END;  { InitializeNewsWindow }

PROCEDURE NewsWindow(VAR NWS: NewsStructure; Comm: Word);
   BEGIN
   CASE Comm OF
              CloseWCM: CloseNewsWindow(NWS);
               OpenWCM: OpenNewsWindow(NWS);

             CenterWCM: HomePage(NWS);
                EndWCM: EndPage(NWS);

             PageUpWCM: PreviousPage(NWS);
            ScrllUpWCM,
           CursorUpWCM: ScrollPageUp(NWS);

           PageDownWCM: NextPage(NWS);
          ScrllDownWCM,
         CursorDownWCM: ScrollPageDown(NWS);
      END;  { case }
   END;  { NewsWindow }

BEGIN
New(NWS);
END.
