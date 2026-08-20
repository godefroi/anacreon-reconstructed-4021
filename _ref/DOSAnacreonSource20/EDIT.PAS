(* EDIT.PAS -------------------------------------------- TURBO Pascal 4.0 ------

   Simple Editor Library   v1.0

----------------------------------------------------------------------------- *)

UNIT Edit;

INTERFACE

USES Strg,
     CRT,
     TextStrc,
     EIO,
     WND;

TYPE
   EditStructure = RECORD
      Txt: TextStructure;
      CurX,CurY: Word;                          { Pos of cursor in window. }
      Width,Height: Word;                       { Width and height of window. }
      Col: Word;                                { Window color. }
      TopY: Word;                               { Y co-ordinate of top screen. }
      CurLine: LineRecordPtr;                   { Pointer to current line. }
      CurLen: Word;                             { Length of current line. }
      Modified: Boolean;                        { True if file is changed. }

      EditWind: WindowHandle;
   END;

PROCEDURE CleanUpEdit(VAR Edt: EditStructure);
PROCEDURE EditText(VAR Edt: EditStructure; Ch: Char);
PROCEDURE InitializeEdit(VAR Edt: EditStructure;
                         Title: LineStr; NewX,NewY,NewWidth,NewHeight,NewCol: Word);
PROCEDURE ViewText(VAR Txt: TextStructure;
                   StartY,NoOfLines,Col: Word);

IMPLEMENTATION

FUNCTION BottomScreenLine(Txt: TextStructure; CurLine: LineRecordPtr;
                          Height,CurY: Word): LineRecordPtr;
   BEGIN
   WHILE (CurY<Height) AND (CurLine<>Nil) DO
      BEGIN
      CurLine:=CurLine^.Next;
      Inc(CurY);
      END;

   BottomScreenLine:=CurLine;
   END;  { BottomScreenLine }

FUNCTION TopScreenLine(Txt: TextStructure; CurLine: LineRecordPtr;
                       CurY: Word): LineRecordPtr;
   BEGIN
   WHILE CurY>1 DO
      BEGIN
      CurLine:=CurLine^.Prev;
      Dec(CurY);
      END;

   TopScreenLine:=CurLine;
   END;  { BottomScreenLine }

PROCEDURE DisplayLine(Line: LineStr; PosY,Width,Col: Word);
   BEGIN
   AdjustString(Line,Width);
   WriteString(Line,1,PosY,Col);
   END;  { DisplayLine }

PROCEDURE DisplayScreen(VAR Edt: EditStructure; TmpLine: LineRecordPtr);
   VAR
      TmpY: Word;

   BEGIN
   WITH Edt DO
      BEGIN
      TmpY:=1;
      WHILE TmpY<=Height DO
         BEGIN
         IF TmpLine=Nil THEN
            DisplayLine('',TmpY,Width,Col)
         ELSE
            BEGIN
            DisplayLine(TmpLine^.Line,TmpY,Width,Col);
            TmpLine:=TmpLine^.Next;
            END;

         Inc(TmpY);
         END;
      END;  { with scope }
   END;  { DisplayScreen }

FUNCTION FirstWord(Line: LineStr): LineStr;
{ FirstWord: -------------------------------------------------------------------
   Returns the first word of Line.
------------------------------------------------------------------------------ }
   VAR
      PosX: Word;

   BEGIN
   PosX:=1;
   WHILE (Line[PosX]<>' ') AND (Line[PosX]<>'-') AND (PosX<Length(Line)) DO
      Inc(PosX);
   FirstWord:=Copy(Line,1,PosX);
   END;  { FirstWord }

FUNCTION LastWord(Line: LineStr): LineStr;
   VAR
      PosX: Word;

   BEGIN
   PosX:=Length(Line)-1;
   WHILE (Line[PosX]<>' ') AND (Line[PosX]<>'-') AND (PosX>1) DO
      Dec(PosX);
   LastWord:=Copy(Line,PosX+1,255);
   END;  { LastWord }

PROCEDURE FormatLine(VAR Line,Strg: MaxStr);
{ FormatLine: ------------------------------------------------------------------
   This procedure will cut Line at a space or hyphen, and return the rest in
   Strg.
------------------------------------------------------------------------------ }
   VAR
      PosX: Word;

   BEGIN
   PosX:=RMargin+1;
   WHILE (Line[PosX]<>' ') AND (Line[PosX]<>'-') AND (PosX>1) DO
      Dec(PosX);

   IF PosX=1 THEN
      PosX:=RMargin;

   Strg:=Copy(Line,PosX+1,255);
   Line:=Copy(Line,1,PosX);
   END;  { FormatLine }

PROCEDURE BackwardWordWrap(VAR Edt: EditStructure;
                           TmpLine: LineRecordPtr; TmpY: Word);
   VAR
      TmpStrg: MaxStr;

   BEGIN
   WITH Edt DO
      BEGIN
      WHILE (TmpLine^.Next<>Nil) AND (NOT TmpLine^.Para) DO
         BEGIN
         TmpStrg:=FirstWord(TmpLine^.Next^.Line);
         IF (Length(TmpLine^.Line)+Length(TmpStrg))>RMargin THEN
            BEGIN
            IF TmpY<=Height THEN
               DisplayLine(TmpLine^.Line,TmpY,Width,Col);

            Inc(TmpY);
            TmpLine:=TmpLine^.Next;
            END
         ELSE
            BEGIN
            TmpLine^.Line:=TmpLine^.Line+TmpStrg;
            Delete(TmpLine^.Next^.Line,1,Length(TmpStrg));
            IF TmpLine^.Next^.Line='' THEN
               BEGIN
               IF TmpLine^.Next^.Para THEN
                  TmpLine^.Para:=True;
               DeleteLine(Txt,TmpLine^.Next);
               END;
            END;
         END;

      WHILE (TmpY<=Height) DO
         BEGIN
         IF TmpLine=Nil THEN
            DisplayLine('',TmpY,Width,Col)
         ELSE
            BEGIN
            DisplayLine(TmpLine^.Line,TmpY,Width,Col);
            TmpLine:=TmpLine^.Next;
            END;
         Inc(TmpY);
         END;
      END;  { with scope }
   END;  { BackwardWordWrap }

PROCEDURE ForwardWordWrap(VAR Edt: EditStructure; Carry: LineStr;
                          TmpLine: LineRecordPtr; TmpY: Word);
{ ForwardWordWrap: -------------------------------------------------------------
   This procedure will add format lines beginning with TmpLine, and updating all
   following lines.  
------------------------------------------------------------------------------ }
   VAR
      TmpStrg: MaxStr;

   BEGIN
   WITH Edt DO
      BEGIN
      REPEAT
         TmpStrg:=Carry+TmpLine^.Line;
         IF Length(TmpStrg)>RMargin THEN
            BEGIN
            FormatLine(TmpStrg,Carry);
            IF (TmpLine^.Next=Nil) OR (TmpLine^.Para) THEN
               BEGIN
               InsertLine(Txt,TmpLine);
               TmpLine^.Para:=False;
               TmpLine^.Next^.Para:=True;
               END;
            END
         ELSE
            Carry:='';

         TmpLine^.Line:=TmpStrg;

         IF TmpY<=Height THEN
            DisplayLine(TmpLine^.Line,TmpY,Width,Col);

         Inc(TmpY);
         TmpLine:=TmpLine^.Next;
      UNTIL Length(Carry)=0;

      WHILE (TmpY<=Height) AND (TmpLine<>Nil) DO
         BEGIN
         DisplayLine(TmpLine^.Line,TmpY,Width,Col);
         TmpLine:=TmpLine^.Next;
         Inc(TmpY);
         END;
      END;  { with scope }
   END;  { ForwardWordWrap }

PROCEDURE InsertChar(VAR Edt: EditStructure;  Ch: Char;
                     VAR TmpLine: LineRecordPtr;
                     VAR TmpX,TmpY,TmpLen: Word);
   VAR
      TmpStrg: MaxStr;
      PrevLine: LineRecordPtr;

   BEGIN
   WITH Edt DO
      BEGIN
      Modified:=True;
      { Add extra blanks if insert is beyond end of line. }
      WHILE TmpX>(TmpLen+1) DO
         BEGIN
         TmpLine^.Line:=TmpLine^.Line+' ';
         Inc(TmpLen);
         END;

      Insert(Ch,TmpLine^.Line,TmpX);
      Inc(TmpLen);
      Inc(TmpX);

      IF TmpLen<=RMargin THEN
         { ASSERT: No need to format line. }
         BEGIN
         IF TmpY<=Height THEN
            WriteString(TmpLine^.Line,1,TmpY,Col);
         END
      ELSE
         { ASSERT: Must word wrap lines. }
         BEGIN
         ForwardWordWrap(Edt,'',TmpLine,TmpY);
         TmpLen:=Length(TmpLine^.Line);

         IF TmpX>(TmpLen+1) THEN
            BEGIN
            TmpX:=TmpX-TmpLen;
            TmpLine:=TmpLine^.Next;
            TmpLen:=Length(TmpLine^.Line);

            IF TmpY=Height THEN
               BEGIN
               ScrollUp;
               Inc(TopY);
               DisplayLine(TmpLine^.Line,TmpY,Width,Col);
               END
            ELSE
               Inc(TmpY);
            END;
         END;

      IF (Ch=' ') OR (Ch='-') THEN
         BEGIN
         PrevLine:=TmpLine^.Prev;
         TmpStrg:=FirstWord(TmpLine^.Line);
         IF (PrevLine<>Nil) AND (NOT PrevLine^.Para)
            AND (Length(PrevLine^.Line)+Length(TmpStrg)<=RMargin) THEN
            BEGIN
            TmpX:=Length(PrevLine^.Line)+TmpX;
            TmpLine:=PrevLine;
            IF TmpY=1 THEN
               BEGIN
               ScrollDown;
               Dec(TopY);
               DisplayLine(TmpLine^.Line,TmpY,Width,Col);
               END
            ELSE
               Dec(TmpY);
            BackwardWordWrap(Edt,TmpLine,TmpY);
            TmpLen:=Length(TmpLine^.Line);
            END;
         END;
      END;  { with scope }
   END;  { InsertChar }

PROCEDURE DeleteChar(VAR Edt: EditStructure;
                     VAR TmpLine: LineRecordPtr;
                     VAR TmpX,TmpY,TmpLen: Word);
   VAR
      PrevLine: LineRecordPtr;
      TmpStrg: MaxStr;

   BEGIN
   WITH Edt DO
      BEGIN
      Modified:=True;
      IF TmpX<>1 THEN
         BEGIN
         Dec(TmpX);
         Delete(TmpLine^.Line,TmpX,1);
         Dec(TmpLen);
         DisplayLine(TmpLine^.Line,TmpY,Width,Col);

         PrevLine:=TmpLine^.Prev;
         TmpStrg:=FirstWord(TmpLine^.Line);
         IF (PrevLine<>Nil) AND (NOT PrevLine^.Para)
            AND (Length(PrevLine^.Line)+Length(TmpStrg)<=RMargin) THEN
            BEGIN
            TmpX:=Length(PrevLine^.Line)+TmpX;
            TmpLine:=PrevLine;
            IF TmpY=1 THEN
               BEGIN
               ScrollDown;
               Dec(TopY);
               DisplayLine(TmpLine^.Line,TmpY,Width,Col);
               END
            ELSE
               Dec(TmpY);
            END;

         BackwardWordWrap(Edt,TmpLine,TmpY);
         TmpLen:=Length(TmpLine^.Line);
         END
      ELSE
         BEGIN
         PrevLine:=TmpLine^.Prev;
         IF PrevLine^.Para THEN
            { ASSERT: Join two paragraphs. }
            BEGIN
            TmpLine:=TmpLine^.Prev;
            TmpLine^.Para:=False;
            TmpLen:=Length(TmpLine^.Line);
            TmpX:=TmpLen+1;
            IF TmpY=1 THEN
               BEGIN
               ScrollDown;
               Dec(TopY);
               DisplayLine(TmpLine^.Line,TmpY,Width,Col);
               END
            ELSE
               Dec(TmpY);

            BackwardWordWrap(Edt,TmpLine,TmpY);
            TmpLen:=Length(TmpLine^.Line);
            END
         ELSE
            BEGIN
            IF Copy(PrevLine^.Line,Length(PrevLine^.Line)-1,2)='  ' THEN
               BEGIN
               TmpLine:=PrevLine;
               TmpLine^.Line:=Copy(TmpLine^.Line,1,Length(TmpLine^.Line)-1);
               TmpLen:=Length(TmpLine^.Line);
               TmpX:=TmpLen+1;
               IF TmpY=1 THEN
                  BEGIN
                  ScrollDown;
                  Dec(TopY);
                  END
               ELSE
                  Dec(TmpY);
               DisplayLine(TmpLine^.Line,TmpY,Width,Col);
               END
            ELSE
               BEGIN
               TmpStrg:=LastWord(PrevLine^.Line);
               PrevLine^.Line:=Copy(PrevLine^.Line,1,Length(PrevLine^.Line)-Length(TmpStrg));
               IF TmpY>1 THEN
                  DisplayLine(PrevLine^.Line,TmpY-1,Width,Col);

               Delete(TmpStrg,Length(TmpStrg),1);
               ForwardWordWrap(Edt,TmpStrg,TmpLine,TmpY);
               TmpLen:=Length(TmpLine^.Line);
               TmpX:=Length(TmpStrg)+1;
               END;
            END;
         END;
      END;  { with scope }
   END;  { DeleteChar }

PROCEDURE InsertPageBreak(VAR Edt: EditStructure;
                          VAR TmpLine: LineRecordPtr;
                          VAR TmpX,TmpY,TmpLen: Word);
   VAR
      TmpStrg: LineStr;
      Paragraph: Boolean;

   BEGIN
   WITH Edt DO
      BEGIN
      Modified:=True;
      Paragraph:=TmpLine^.Para;
      TmpLine^.Para:=True;
      TmpStrg:=Copy(TmpLine^.Line,TmpX,255);
      TmpLine^.Line:=Copy(TmpLine^.Line,1,TmpX-1);
      DisplayLine(TmpLine^.Line,TmpY,Width,Col);

      InsertLine(Txt,TmpLine);
      TmpLine:=TmpLine^.Next;
      TmpLine^.Line:=TmpStrg;
      TmpLine^.Para:=Paragraph;
      IF TmpY=Height THEN
         BEGIN
         ScrollUp;
         Inc(TopY);
         DisplayLine(TmpLine^.Line,TmpY,Width,Col);
         END
      ELSE
         Inc(TmpY);
      BackwardWordWrap(Edt,TmpLine,TmpY);
      TmpX:=1;
      TmpLen:=Length(TmpLine^.Line);
      END;  { with scope }
   END;  { InsertPageBreak }

PROCEDURE EditText(VAR Edt: EditStructure; Ch: Char);
   VAR
      TmpLine: LineRecordPtr;

   BEGIN
   WITH Edt DO
      BEGIN
      ActivateWindow(EditWind);

      CASE Ch OF
             ' '..'~': InsertChar(Edt,Ch,CurLine,CurX,CurY,CurLen);

            ReturnKey: InsertPageBreak(Edt,CurLine,CurX,CurY,CurLen);
         BackspaceKey: IF (CurX>1) OR (CurLine^.Prev<>Nil) THEN
                          DeleteChar(Edt,CurLine,CurX,CurY,CurLen);
               DelKey: IF (CurX<=CurLen) THEN
                          BEGIN
                          Inc(CurX);
                          DeleteChar(Edt,CurLine,CurX,CurY,CurLen);
                          END;
              HomeKey: CurX:=1;
               EndKey: CurX:=CurLen+1;
              LArrKey: IF CurX>1 THEN
                          Dec(CurX);
              RArrKey: IF CurX<(CurLen+1) THEN
                          Inc(CurX);
              UArrKey: IF CurLine^.Prev<>Nil THEN
                          BEGIN
                          CurLine:=CurLine^.Prev;
                          CurLen:=Length(CurLine^.Line);
                          IF CurY=1 THEN
                             BEGIN
                             ScrollDown;
                             Dec(TopY);
                             DisplayLine(CurLine^.Line,CurY,Width,Col);
                             END
                          ELSE
                             Dec(CurY);
                          END;
              DArrKey: IF CurLine^.Next<>Nil THEN
                          BEGIN
                          CurLine:=CurLine^.Next;
                          CurLen:=Length(CurLine^.Line);
                          IF CurY=Height THEN
                             BEGIN
                             ScrollUp;
                             Inc(TopY);
                             DisplayLine(CurLine^.Line,CurY,Width,Col);
                             END
                          ELSE
                             Inc(CurY);
                          END;
              PgUpKey: IF TopY>1 THEN
                          BEGIN
                          ScrollDown;
                          Dec(TopY);
                          IF CurY=Height THEN
                             BEGIN
                             CurLine:=CurLine^.Prev;
                             CurLen:=Length(CurLine^.Line);
                             END
                          ELSE
                             Inc(CurY);

                          TmpLine:=TopScreenLine(Txt,CurLine,CurY);
                          DisplayLine(TmpLine^.Line,1,Width,Col);
                          END;
              PgDnKey: IF (CurY>1) OR (CurLine^.Next<>Nil) THEN
                          BEGIN
                          ScrollUp;
                          Inc(TopY);
                          IF CurY=1 THEN
                             BEGIN
                             CurLine:=CurLine^.Next;
                             CurLen:=Length(CurLine^.Line);
                             END
                          ELSE
                             Dec(CurY);

                          TmpLine:=BottomScreenLine(Txt,CurLine,Height,CurY);
                          IF TmpLine=Nil THEN
                             DisplayLine('',Height,Width,Col)
                          ELSE
                             DisplayLine(TmpLine^.Line,Height,Width,Col);
                          END;
         END;  { case }

      GotoXY(CurX,CurY);
      END;  { with scope }
   END;  { EditText }

PROCEDURE InitializeEdit(VAR Edt: EditStructure;
                         Title: LineStr; NewX,NewY,NewWidth,NewHeight,NewCol: Word);
   BEGIN
   WITH Edt DO
      BEGIN
      CurX:=1;
      CurY:=1;
      Width:=NewWidth-2;
      Height:=NewHeight-2;
      Col:=NewCol;
      TopY:=1;
      CurLine:=Txt.FirstLine;
      CurLen:=Length(CurLine^.Line);
      Modified:=False;

      OpenWindow(NewX,NewY,NewWidth,NewHeight,ThinBRD,Title,Col,C.SYSWBorder,EditWind);
      DisplayScreen(Edt,CurLine);
      END;  { with scope }
   END;  { InitializeEdit }

PROCEDURE CleanUpEdit(VAR Edt: EditStructure);
   BEGIN
   WITH Edt DO
      BEGIN
      ActivateWindow(EditWind);
      CloseWindow;
      END;  { with scope }
   END;  { CleanUpEdit }

PROCEDURE ViewText(VAR Txt: TextStructure;
                   StartY,NoOfLines,Col: Word);
   VAR
      Ch: Char;
      Line: LineRecordPtr;
      LineNo: Word;

   BEGIN
   LineNo:=0;
   WITH Txt DO
      BEGIN
      Line:=FirstLine;
      WHILE (Line<>Nil) AND (LineNo<NoOfLines) DO
         BEGIN
         WriteString(Line^.Line,1,StartY+LineNo,Col);
         Inc(LineNo);
         Line:=Line^.Next;
         END;
      END;
   END;  { ViewText }

END.
