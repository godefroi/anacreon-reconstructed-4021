(* TEXTSTRC.PAS ---------------------------------------- TURBO Pascal 4.0 ------

   Text Structure Library   v1.0

----------------------------------------------------------------------------- *)

UNIT TextStrc;

INTERFACE

USES Strg;

CONST
   RMargin = 77;                                { Right margin. }
   ParaChar = #020;

TYPE
   LineRecordPtr = ^LineRecord;
   LineRecord = RECORD
      Line: LineStr;
      Para: Boolean;                            { True if this is last line in par. }
      Prev: LineRecordPtr;
      Next: LineRecordPtr;
   END;  { LineRecord }

   TextStructure = RECORD
      NoOfLines: Word;
      FirstLine: LineRecordPtr;
      LastLine: LineRecordPtr;
   END;  { TextStructure }

PROCEDURE DeleteLine(VAR Txt: TextStructure; CurrentLine: LineRecordPtr);
PROCEDURE DisposeText(VAR Txt: TextStructure);
PROCEDURE InitializeText(VAR Txt: TextStructure);
PROCEDURE InsertLine(VAR Txt: TextStructure; CurrentLine: LineRecordPtr);
PROCEDURE LoadText(VAR Txt: TextStructure; Filename: String64; VAR Error: Word);
PROCEDURE SaveText(VAR Txt: TextStructure; Filename: String64; VAR Error: Word);
FUNCTION TXTFirstLine(VAR Txt: TextStructure): LineRecordPtr;
FUNCTION TXTLine(CurrLine: LineRecordPtr): LineStr;
FUNCTION TXTNextLine(CurrLine: LineRecordPtr): LineRecordPtr;
FUNCTION TXTNumberOfLines(VAR Txt: TextStructure): Word;
PROCEDURE TXTSetLine(CurrLine: LineRecordPtr; Line: LineStr);
PROCEDURE TXTSetParagraph(CurrLine: LineRecordPtr);

IMPLEMENTATION

PROCEDURE TXTSetParagraph(CurrLine: LineRecordPtr);
   BEGIN
   CurrLine^.Para:=True;
   END;  { TXTSetParagraph }

PROCEDURE TXTSetLine(CurrLine: LineRecordPtr; Line: LineStr);
   BEGIN
   CurrLine^.Line:=Line;
   END;  { TXTSetLine }

FUNCTION TXTNumberOfLines(VAR Txt: TextStructure): Word;
   BEGIN
   TXTNumberOfLines:=Txt.NoOfLines;
   END;  { TXTNumberOfLines }

FUNCTION TXTFirstLine(VAR Txt: TextStructure): LineRecordPtr;
   BEGIN
   TXTFirstLine:=Txt.FirstLine;
   END;  { TXTFirstLine }

FUNCTION TXTNextLine(CurrLine: LineRecordPtr): LineRecordPtr;
   BEGIN
   TXTNextLine:=CurrLine^.Next;
   END;  { TXTNextLine }

FUNCTION TXTLine(CurrLine: LineRecordPtr): LineStr;
   BEGIN
   TXTLine:=CurrLine^.Line;
   END;  { TXTLine }

PROCEDURE DeleteLine(VAR Txt: TextStructure; CurrentLine: LineRecordPtr);
{ DeleteLine: ------------------------------------------------------------------
   This procedure deletes the given line, restoring the heap and updating all
   pointers.  After this procedure, Txt is uninitialized.  If Txt is to be used
   again, InitializeText should be called.
------------------------------------------------------------------------------ }
   BEGIN
   WITH Txt DO
      BEGIN
      IF (CurrentLine=FirstLine) AND (CurrentLine^.Next=Nil) THEN
         CurrentLine^.Line:=''
      ELSE
         BEGIN
         IF CurrentLine^.Prev<>Nil THEN
            BEGIN
            CurrentLine^.Prev^.Next:=CurrentLine^.Next;
            IF CurrentLine^.Next<>Nil THEN
               CurrentLine^.Next^.Prev:=CurrentLine^.Prev;
            END;

         IF CurrentLine=FirstLine THEN
            FirstLine:=CurrentLine^.Next;
         IF CurrentLine=LastLine THEN
            LastLine:=CurrentLine^.Prev;

         Dec(NoOfLines);
         Dispose(CurrentLine);
         END;
      END;  { with scope }
   END;  { DeleteLine }

PROCEDURE InitializeText(VAR Txt: TextStructure);
{ InitializeText: --------------------------------------------------------------
   This procedure will initialize Txt so that it is completely blank.
------------------------------------------------------------------------------ }
   BEGIN
   WITH Txt DO
      BEGIN
      NoOfLines:=1;
      New(FirstLine);
      FirstLine^.Line:='';
      FirstLine^.Para:=True;
      FirstLine^.Prev:=Nil;
      FirstLine^.Next:=Nil;
      LastLine:=FirstLine;
      END;  { with scope }
   END;  { InitializeText }

PROCEDURE DisposeText(VAR Txt: TextStructure);
   VAR
      NextLine,LineToDispose: LineRecordPtr;

   BEGIN
   NextLine:=Txt.FirstLine;
   LineToDispose:=NextLine;

   WHILE NextLine<>Nil DO
      BEGIN
      NextLine:=NextLine^.Next;
      Dispose(LineToDispose);
      LineToDispose:=NextLine;
      END;
   END;  { DisposeText }

PROCEDURE InsertLine(VAR Txt: TextStructure; CurrentLine: LineRecordPtr);
{ InsertLine: ------------------------------------------------------------------
   This routine will insert a new (blank) line AFTER the given line.
------------------------------------------------------------------------------ }
   VAR
      NewLine: LineRecordPtr;

   BEGIN
   New(NewLine);
   IF CurrentLine^.Next<>Nil THEN
      CurrentLine^.Next^.Prev:=NewLine;
   NewLine^.Prev:=CurrentLine;
   NewLine^.Next:=CurrentLine^.Next;
   CurrentLine^.Next:=NewLine;
   NewLine^.Line:='';
   NewLine^.Para:=False;

   WITH Txt DO
      BEGIN
      IF CurrentLine=LastLine THEN
         LastLine:=NewLine;
      Inc(NoOfLines);
      END;  { with scope }
   END;  { InsertLine }

PROCEDURE LoadText(VAR Txt: TextStructure; Filename: String64; VAR Error: Word);
{ LoadText: --------------------------------------------------------------------
   This procedure assumes that Txt is an empty initialized variable.
   Unpredictable results will occur if Txt is not empty or not initialized.
   If there is an error, LoadText will dispose the variable.
------------------------------------------------------------------------------ }
   VAR
      TextFile: TEXT;
      CurLine: LineRecordPtr;
      TmpStrg: MaxStr;
      ParaPos: Word;

   BEGIN
   Assign(TextFile,Filename);
   {$I-}
   Reset(TextFile);                             { Open the file. }
   {$I+}
   Error:=IOResult;

   IF Error=0 THEN
      BEGIN
      CurLine:=Txt.FirstLine;
      WHILE (NOT EoF(TextFile)) AND (Error=0) DO
         BEGIN
         {$I-}
         ReadLn(TextFile,TmpStrg);
         {$I+}
         Error:=IOResult;
         IF Error=0 THEN
            BEGIN
            IF TmpStrg=ParaChar THEN
               BEGIN
               CurLine^.Line:='';
               CurLine^.Para:=True;
               InsertLine(Txt,CurLine);
               CurLine:=CurLine^.Next;
               END
            ELSE
               BEGIN
               WHILE Length(TmpStrg)>0 DO
                  BEGIN
                  ParaPos:=Pos(ParaChar,TmpStrg);
                  IF ParaPos<>0 THEN
                     Delete(TmpStrg,ParaPos,1);
                  CurLine^.Line:=Copy(TmpStrg,1,RMargin+1);
                  Delete(TmpStrg,1,RMargin+1);
                  InsertLine(Txt,CurLine);
                  CurLine:=CurLine^.Next;
                  END;
               IF ParaPos<>0 THEN
                  CurLine^.Prev^.Para:=True
               ELSE
                  CurLine^.Prev^.Para:=False;
               END;
            END
         ELSE
            DisposeText(Txt);
         END;
      Close(TextFile);
      END
   ELSE
      DisposeText(Txt);
   END;  { LoadText }

PROCEDURE SaveText(VAR Txt: TextStructure; Filename: String64; VAR Error: Word);
{ SaveText: --------------------------------------------------------------------
------------------------------------------------------------------------------ }
   VAR
      TextFile: TEXT;
      CurLine: LineRecordPtr;
      TmpStrg: MaxStr;

   BEGIN
   Assign(TextFile,Filename);
   {$I-}
   ReWrite(TextFile);                           { Open the file. }
   {$I+}
   Error:=IOResult;

   IF Error=0 THEN
      BEGIN
      CurLine:=Txt.FirstLine;
      WHILE (CurLine<>Nil) AND (Error=0) DO
         BEGIN
         {$I-}
         Write(TextFile,CurLine^.Line);
         IF CurLine^.Para THEN
            WriteLn(TextFile,ParaChar)
         ELSE
            WriteLn(TextFile);
         {$I+}
         Error:=IOResult;
         CurLine:=CurLine^.Next;
         END;
      Close(TextFile);
      END;
   END;  { SaveText }

END.

