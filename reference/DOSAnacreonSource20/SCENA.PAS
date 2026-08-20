(* SCENA.PAS -------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT Scena;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Strg,
     EIO,
     DOS2,
     TextStrc,
     Types,
     Galaxy,
     Misc,
     PrimIntr,
     Environ;

PROCEDURE DisplayBackground(Player:Empire;WorldID: IDNumber; x,y,Col: Word; Conquer: Boolean;
                            VAR Found: Boolean);
PROCEDURE LoadScenaText(TextNum: Word; VAR Txt: TextStructure);

IMPLEMENTATION

TYPE
   ParmArray = ARRAY [1..10] OF String32;
   ByteSet = SET OF Byte;

PROCEDURE FindLine(VAR ScenaFile: TEXT; LineToFind: LineStr; VAR Error: Word);
{ FindLine: --------------------------------------------------------------------
   This procedure starts at the current file position and searches line by line
   for any occurrence of LineToFind. It leaves the file position at the line
   AFTER the search line. Error=100 if the end of file is reached.
   (case is ignored)
------------------------------------------------------------------------------ }
   VAR
      TestLine: LineStr;
      Found: Boolean;

   BEGIN
   Found:=False;
   AllUpCase(LineToFind);
   WHILE (NOT Found) AND (NOT EoF(ScenaFile)) DO
      BEGIN
      ReadLn(ScenaFile,TestLine);
      AllUpCase(TestLine);
      IF Pos(LineToFind,TestLine)<>0 THEN
         Found:=True;
      END;

   IF Found THEN
      Error:=0
   ELSE
      Error:=100;
   END;  { FindLine }

PROCEDURE SplitLine(Line: LineStr; VAR NoOfParms: Word; VAR Parm: ParmArray);
   CONST
      WhiteSpace: CharSet = [#9,#10,#13,' '];

   VAR
      i,Start,Finish,Comment: Word;
      TextReady: Boolean;

   BEGIN
   NoOfParms:=1;
   FillChar(Parm,SizeOf(Parm),0);

   { Strip leading and trailing spaces }
   Start:=1;
   Finish:=Length(Line);
   Comment:=Pos(';',Line);
   IF Comment>0 THEN
      Finish:=Comment-1;

   WHILE (Line[Start] IN WhiteSpace) DO
      Inc(Start);

   WHILE (Line[Finish] IN WhiteSpace) DO
      Dec(Finish);

   { ASSUME: First character is NOT white space }

   FOR i:=Start TO Finish DO
      IF (Line[i] IN WhiteSpace) THEN
         BEGIN
         IF TextReady THEN
            BEGIN
            Inc(NoOfParms);
            TextReady:=False;
            END;
         END
      ELSE
         BEGIN
         Parm[NoOfParms]:=Parm[NoOfParms]+Line[i];
         TextReady:=True;
         END;
   END;  { SplitLine }

PROCEDURE IDMatch(IDStr: String32; VAR ID: IDNumber);
{ IDMatch: ---------------------------------------------------------------------
   Converts a string of the format t:i (type:index) to an ID.
   Example: 2:6 is planet no. 6.
------------------------------------------------------------------------------ }
   VAR
      TempID: IDNumber;
      a,b,Error: Word;
      ColPos: Word;

   BEGIN
   ID:=EmptyQuadrant;
   ColPos:=Pos(':',IDStr);
   Val(Copy(IDStr,1,ColPos-1),a,Error);
   IF Error=0 THEN
      BEGIN
      Val(Copy(IDStr,ColPos+1,32),b,Error);
      IF Error=0 THEN
         BEGIN
         TempID.ObjTyp:=ObjectTypes(a);
         TempID.Index:=b;
         ID:=TempID;
         END;
      END;
   END;  { IDMatch }

PROCEDURE InterpretSet(Parm: String32; VAR Numbers: ByteSet);
   VAR
      i,Num,Error: Word;
      NumStr: String8;
      Build: Boolean;

   BEGIN
   NumStr:='';
   Numbers:=[];
   FOR i:=1 TO Length(Parm) DO
      BEGIN
      IF (Parm[i]=',') AND (NumStr<>'') THEN
         BEGIN
         Val(NumStr,Num,Error);
         IF (Error=0) AND (Num>=0) AND (Num<=255) THEN
            Numbers:=Numbers+[Num];
         NumStr:='';
         END
      ELSE
         BEGIN
         NumStr:=NumStr+Parm[i];
         END;
      END;

   IF NumStr<>'' THEN
      BEGIN
      Val(NumStr,Num,Error);
      IF (Error=0) AND (Num>=0) AND (Num<=255) THEN
         Numbers:=Numbers+[Num];
      NumStr:='';
      END;
   END;  { InterpretSet }

FUNCTION SatisfiesConditions(Player: Empire;Conquer: Boolean; ID: IDNumber; 
                             VAR Parm: ParmArray; First,Last: Word): Boolean;
   VAR
      Empires: EmpireSet;
      i: Word;
      Temp: Boolean;

   PROCEDURE BuildEmpireSet(Parm: String32; VAR Empires: EmpireSet);
      VAR
         Numbers: ByteSet;
         Emp: Empire;

      BEGIN
      Parm:=Copy(Parm,3,32);
      InterpretSet(Parm,Numbers);
      Empires:=[];
      AllUpCase(Parm);
      FOR Emp:=Empire1 TO Indep DO
         IF Byte(Emp) IN Numbers THEN
            Empires:=Empires+[Emp];
      IF parm = 'ALL' THEN
         BEGIN
	 Empires:=[];
         FOR Emp:=Empire1 TO Indep DO
	   Empires:=Empires+[Emp];
	 END
      END;  { BuildEmpireSet }

   { SatisfiesConditions: MAIN PROCEDURE }
   BEGIN
   Empires:=[Empire1..Indep];

   Temp:=True;
   FOR i:=First TO Last DO
      BEGIN
      CASE Parm[i][1] OF
         'E':BEGIN
            BuildEmpireSet(Parm[i],Empires);
            IF Conquer OR (NOT (GetStatus(ID) IN Empires)) THEN
               Temp:=False;
            END;
         'A':BEGIN
            BuildEmpireSet(Parm[i],Empires);
            IF (NOT Conquer) OR (NOT (GetStatus(ID) IN Empires)) THEN
               Temp:=False;
            END;
         'O':BEGIN
            IF Conquer OR (NOT (Player = GetStatus(ID))) THEN
               Temp:=False;
            END;
      END;  { case }
      END;

   SatisfiesConditions:=Temp;
   END;  { SatisfiesConditions }

PROCEDURE DisplayText(VAR ScenaFile: TEXT; TextNumber,x,y,Col: Word);
   VAR
      TextNumStr: String8;
      Error: Word;
      Line: LineStr;

   PROCEDURE ParseLine(VAR Line: LineStr);
      VAR
         OpenB,CloseB: Word;
         OldStr,NewStr: String32;
         ID: IDNumber;
         XY: XYCoord;

      BEGIN
      OpenB:=Pos('[',Line);
      CloseB:=Pos(']',Line);

      WHILE OpenB+CloseB>0 DO
         BEGIN
         OldStr:=Copy(Line,OpenB,(CloseB-OpenB)+1);
         Delete(Line,OpenB,Length(OldStr));
         IF OldStr[2]='C' THEN
            BEGIN
            IDMatch(Copy(OldStr,3,Length(OldStr)-3),ID);
            GetCoord(ID,XY);
            GetCoordName(XY,NewStr);
	    IF NOT SameXY(XY,Limbo) THEN
             BEGIN
               GetCoordName(XY,NewStr);
               Insert(NewStr,Line,OpenB);
             END
            ELSE
             Insert('     ',Line,OpenB)
            END;
         IF OldStr[2]='N' THEN
            BEGIN
            IDMatch(Copy(OldStr,3,Length(OldStr)-3),ID);
	    GetCoord(ID,XY);
	    IF NOT SameXY(XY,Limbo) THEN
               BEGIN
                 NewStr:=ObjectName(Player,ID,LongFormat);
                 Insert(NewStr,Line,OpenB);
	       END
	    ELSE
		 Insert('     ',Line,OpenB);
            END;
         OpenB:=Pos('[',Line);
         CloseB:=Pos(']',Line);    
         END;
      END;  { ParseLine }

   BEGIN
   Str(TextNumber,TextNumStr);
   FindLine(ScenaFile,'TEXT '+TextNumStr,Error);
   IF Error=0 THEN
      BEGIN
      ReadLn(ScenaFile,Line);
      REPEAT
         ParseLine(Line);
         WriteString(Line,x,y,Col);
         Inc(y);
         ReadLn(ScenaFile,Line);
      UNTIL Pos('ENDTEXT',Line)<>0;
      END;
   END;  { DisplayText }

PROCEDURE DisplayBackground(Player: Empire;WorldID: IDNumber; x,y,Col: Word; Conquer: Boolean;
                            VAR Found: Boolean);
   VAR
      ScenaFile: TEXT;
      Error,NoOfParms,TextNumber: Word;
      IndexEntry: LineStr;
      Parm: ParmArray;
      TempID: IDNumber;

   BEGIN
   Found:=False;
   Assign(ScenaFile,AddDefaultPath(SceDirect,ScenaFilename));
   {$I-}
   Reset(ScenaFile);
   {$I+}
   Error:=IOResult;
   IF Error=0 THEN
      { ASSERT: File opened ok. }
      BEGIN
      FindLine(ScenaFile,'WORLDBACKGROUNDINDEX',Error);
      IF Error=0 THEN
         { ASSERT: WorldBackgroundIndex found. }
         BEGIN
         REPEAT
            ReadLn(ScenaFile,IndexEntry);
            AllUpCase(IndexEntry);

            SplitLine(IndexEntry,NoOfParms,Parm);
            IDMatch(Parm[1],TempID);
            IF SameID(TempID,WorldID) THEN
               IF SatisfiesConditions(Player,Conquer,WorldID,Parm,2,NoOfParms-1) THEN
                  { ASSERT: Entry found. }
                  BEGIN
                  Val(Parm[NoOfParms],TextNumber,Error);
                  IF Error=0 THEN
                     { ASSERT: TextNumber found. }
                     BEGIN
                     DisplayText(ScenaFile,TextNumber,x,y,Col);
                     Found:=True;
                     END;
                  END;
         UNTIL (Pos('ENDINDEX',IndexEntry)<>0) OR Found;
         END;

      Close(ScenaFile);
      END;
   END;  { DisplayBackground }

PROCEDURE LoadScenaText(TextNum: Word; VAR Txt: TextStructure);
   VAR
      ScenaFile: TEXT;
      Error: Word;
      TextNumStr: String8;
      CurLine: LineRecordPtr;
      Line: LineStr;

   BEGIN
   Assign(ScenaFile,AddDefaultPath(SceDirect,ScenaFilename));
   {$I-}
   Reset(ScenaFile);
   {$I+}
   Error:=IOResult;
   IF Error=0 THEN
      { ASSERT: File opened ok. }
      BEGIN
      Str(TextNum,TextNumStr);
      FindLine(ScenaFile,'TEXT '+TextNumStr,Error);
      IF Error=0 THEN
         BEGIN
         CurLine:=TXTFirstLine(Txt);
         REPEAT
            ReadLn(ScenaFile,Line);
            IF Line<>'ENDTEXT' THEN
               BEGIN
               TXTSetLine(CurLine,Line);
               InsertLine(Txt,CurLine);
               CurLine:=TXTNextLine(CurLine);
               END;
         UNTIL Pos('ENDTEXT',Line)<>0;
         DeleteLine(Txt,CurLine);
         END;
      END;
   Close(ScenaFile);
   END;  { LoadScenaText }

END.
