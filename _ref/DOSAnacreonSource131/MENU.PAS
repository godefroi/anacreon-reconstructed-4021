(* MENU.PAS -------------------------------------------- TURBO Pascal 4.0 ------

   Menu Library   v1.0

   This UNIT implements menus that can be popped up using windows and allow 
   the user to make a selection using the cursor keys.   

----------------------------------------------------------------------------- *)

UNIT Menu;

{$D-,I-,R-,S-,V-}

INTERFACE

USES Strg,
     Int,
     CRT,
     EIO,
     WND;

TYPE

   { The menu is implemented as nil-bounded linked list of lines. }
   MenuLinePtr = ^MenuLineRecord;
   MenuLineRecord = RECORD
      LineNo: Byte;
      Line: LineStr;
      NextLine: MenuLinePtr;
      PrevLine: MenuLinePtr;
   END;  { MenuLineRecord }

   MenuStructure = RECORD
      NoOfLines: Byte;
      LineCursor: MenuLinePtr;                  { current line selected }
      PosCursor: Byte;                          { Y pos of cursor in window }
      Head: MenuLinePtr;                        { first menu line }
      LastLine: MenuLinePtr;                    { last menu line }
      Handle: WindowHandle;
      Width: Byte;                              { width in characters }
      Height: Byte;                             { no of lines in menu }
      Col: Byte;                                { menu color }
      HCol: Byte;                               { highlighter color }
   END;  { MenuStructure }

PROCEDURE ActivateMenu(VAR Menu: MenuStructure; Ch: Char);
PROCEDURE AddMenuLine(VAR Menu: MenuStructure; NewLine: LineStr);
PROCEDURE CleanUpMenu(VAR Menu: MenuStructure);
PROCEDURE DisplayMenu(VAR Menu: MenuStructure;
                      x,y,NewCol,NewHCol: Byte; 
                      MaxWidth,MaxHeight: Byte);
FUNCTION GetMenuSelect(Menu: MenuStructure): Byte;
PROCEDURE InitializeMenu(VAR Menu: MenuStructure);
PROCEDURE MenuCursorDown(VAR Menu: MenuStructure);
PROCEDURE MenuCursorUp(VAR Menu: MenuStructure);

IMPLEMENTATION

PROCEDURE AddMenuLine(VAR Menu: MenuStructure; NewLine: LineStr);
{ AddMenuLine: -----------------------------------------------------------------
   This procedure will add the given line to the end of the menu structure.
   NOTE: If there is not enough memory, this procedure will crash.
------------------------------------------------------------------------------ }
   VAR
      NLine,PLine: MenuLinePtr;

   BEGIN
   WITH Menu DO
      BEGIN
      IF LastLine=Nil THEN
         BEGIN
         New(Head);
         LastLine:=Head;
         PLine:=Nil;
         END
      ELSE
         BEGIN
         PLine:=LastLine;
         New(LastLine);
         PLine^.NextLine:=LastLine;
         END;

      Inc(NoOfLines);
      WITH LastLine^ DO
         BEGIN
         LineNo:=NoOfLines;
         AdjustString(NewLine,80);
         Line:=NewLine;
         NextLine:=Nil;
         PrevLine:=PLine;
         END;  { with scope }
      END;  { with scope }
   END;  { AddMenuLine }

PROCEDURE CleanUpMenu(VAR Menu: MenuStructure);
   VAR
      i: Byte;
      CLine,NLine: MenuLinePtr;

   BEGIN
   WITH Menu DO
      BEGIN
      ActivateWindow(Handle);
      CloseWindow;

      CLine:=Head;
      WHILE CLine<>Nil DO
         BEGIN
         NLine:=CLine^.NextLine;
         Dispose(CLine);
         CLine:=NLine;
         END;  { while }
      END;  { with scope }
   END;  { CleanUpMenu }

PROCEDURE DisplayMenu(VAR Menu: MenuStructure;
                      x,y,NewCol,NewHCol: Byte;
                      MaxWidth,MaxHeight: Byte);
   VAR
      i: Byte;
      CLine: MenuLinePtr;

   BEGIN
   WITH Menu DO
      BEGIN
      Col:=NewCol;
      HCol:=NewHCol;

      OpenWindow(x,y,MaxWidth,MaxHeight,ThinBRD,'',Col,C.SYSWBorder,Handle);
      Width:=MaxWidth-2;
      Height:=MaxHeight-2;

      CLine:=Head;

      WriteString(Copy(CLine^.Line,1,Width-2),1,1,HCol);
      CLine:=CLine^.NextLine;
      FOR i:=2 TO LesserInt(NoOfLines,Height) DO
         BEGIN
         WriteString(Copy(CLine^.Line,1,Width-2),1,i,Col);
         CLine:=CLine^.NextLine;
         END;  { loop }

      LineCursor:=Head;
      PosCursor:=1;
      WriteString('Û',Width,1,LightCyan);
      IF NoOfLines>Height THEN
         WriteString('',Width,Height,LightCyan)
      ELSE
         WriteString('Û',Width,Height,LightCyan);

      FOR i:=1 TO Height DO
         BEGIN
         WriteString('³',Width-1,i,LightCyan);
         END;
      END;  { with scope }
   END;  { DisplayMenu }

FUNCTION GetMenuSelect(Menu: MenuStructure): Byte;
   BEGIN
   WITH Menu DO
      BEGIN
      GetMenuSelect:=LineCursor^.LineNo;
      END;  { with scope }
   END;  { GetMenuSelect }

PROCEDURE InitializeMenu(VAR Menu: MenuStructure);
   BEGIN
   WITH Menu DO
      BEGIN
      NoOfLines:=0;
      LineCursor:=Nil;
      PosCursor:=0;
      Head:=Nil;
      LastLine:=Nil;
      END;  { with scope }
   END;  { InitializeMenu }

PROCEDURE DrawMenuArrows(CurLine: MenuLinePtr; CurPos,Width,Height,NoOfLines: Word);
   VAR
      UpArr,DnArr: Char;
     
   BEGIN
   IF CurPos=CurLine^.LineNo THEN
      UpArr:='Û'
   ELSE
      UpArr:='';

   IF CurPos+NoOfLines-CurLine^.LineNo>Height THEN
      DnArr:=''
   ELSE
      DnArr:='Û';

   WriteString(UpArr,Width,1,LightCyan);
   WriteString(DnArr,Width,Height,LightCyan);
   END;  { DrawMenuArrows }

PROCEDURE MenuCursorDown(VAR Menu: MenuStructure);
   BEGIN
   WITH Menu DO
      BEGIN
      IF (LineCursor<>LastLine) THEN
         BEGIN
         WriteString(Copy(LineCursor^.Line,1,Width-2),1,PosCursor,Col);
         IF PosCursor=Height THEN
            BEGIN
            WriteString(' ',Width,Height,Col);
            ScrollUp;
            END
         ELSE
            PosCursor:=PosCursor+1;
         LineCursor:=LineCursor^.NextLine;

         WriteString(Copy(LineCursor^.Line,1,Width-2),1,PosCursor,HCol);
         DrawMenuArrows(LineCursor,PosCursor,Width,Height,NoOfLines);
         END;
      END;  { with scope }
   END;  { MenuCursorDown }

PROCEDURE MenuCursorUp(VAR Menu: MenuStructure);
   BEGIN
   WITH Menu DO
      BEGIN
      IF (LineCursor<>Head) THEN
         BEGIN
         WriteString(Copy(LineCursor^.Line,1,Width-2),1,PosCursor,Col);
         IF PosCursor=1 THEN
            BEGIN
            WriteString(' ',Width,1,Col);
            ScrollDown;
            END
         ELSE
            PosCursor:=PosCursor-1;
         LineCursor:=LineCursor^.PrevLine;
         DrawMenuArrows(LineCursor,PosCursor,Width,Height,NoOfLines);
         WriteString(Copy(LineCursor^.Line,1,Width-2),1,PosCursor,HCol);
         END;
      END;  { with scope }
   END;  { MenuCursorUp }

PROCEDURE MenuPageUp(VAR Menu: MenuStructure);
   VAR
      i: Word;
      CLine: MenuLinePtr;

   BEGIN
   WITH Menu DO
      BEGIN
      i:=Height;
      WHILE (i>0) AND (LineCursor<>Head) DO
         BEGIN
         Dec(i);
         LineCursor:=LineCursor^.PrevLine;
         END;
      { ASSERT: cursor up one screen or cursor at top of menu }

      CLine:=LineCursor;
      i:=PosCursor;
      WHILE (i>1) AND (CLine<>Head) DO
         BEGIN
         Dec(i);
         CLine:=CLine^.PrevLine;
         END;
      { ASSERT: CLine point to the first menu line in window. }

      Dec(i);
      PosCursor:=PosCursor-i;

      i:=1;
      REPEAT
         WriteString(Copy(CLine^.Line,1,Width-2),1,i,Col);
         CLine:=CLine^.NextLine;
         Inc(i);
      UNTIL (i>Height) OR (CLine=Nil);
      { ASSERT: Menu lines written }

      DrawMenuArrows(LineCursor,PosCursor,Width,Height,NoOfLines);
      WriteString(Copy(LineCursor^.Line,1,Width-2),1,PosCursor,HCol);
      END;  { with scope }
   END;  { MenuPageUp }

PROCEDURE MenuPageDown(VAR Menu: MenuStructure);
   VAR
      i: Word;
      CLine: MenuLinePtr;
      LastPos: Word;

   BEGIN
   WITH Menu DO
      BEGIN
      LastPos:=LesserInt(Height,NoOfLines);
      i:=Height;
      WHILE (i>0) AND (LineCursor<>LastLine) DO
         BEGIN
         Dec(i);
         LineCursor:=LineCursor^.NextLine;
         END;
      { ASSERT: cursor down one screen or cursor at bottom of menu }

      IF i>0 THEN
         PosCursor:=LesserInt(Height,PosCursor+(Height-i));

      CLine:=LineCursor;
      i:=PosCursor;
      WHILE (i<Height) AND (CLine<>LastLine) DO
         BEGIN
         Inc(i);
         CLine:=CLine^.NextLine;
         END;
      { ASSERT: CLine point to the last menu line in window. }

      PosCursor:=PosCursor+(LastPos-i);

      i:=LastPos;
      REPEAT
         WriteString(Copy(CLine^.Line,1,Width-2),1,i,Col);
         CLine:=CLine^.PrevLine;
         Dec(i);
      UNTIL (i<1) OR (CLine=Nil);
      { ASSERT: Menu lines written }

      DrawMenuArrows(LineCursor,PosCursor,Width,Height,NoOfLines);
      WriteString(Copy(LineCursor^.Line,1,Width-2),1,PosCursor,HCol);
      END;  { with scope }
   END;  { MenuPageDown }

PROCEDURE KeySearch(VAR Menu: MenuStructure; Ch: Char);
   VAR
      i: Word;
      CLine: MenuLinePtr;
      LastPos: Word;

   BEGIN
   Ch:=UpCase(Ch);
   WITH Menu DO
      BEGIN
      LastPos:=LesserInt(Height,NoOfLines);
      i:=0;
      IF LineCursor<>LastLine THEN
         BEGIN
         LineCursor:=LineCursor^.NextLine;
         Inc(i);
         END;

      WHILE (LineCursor<>LastLine) AND (Ch<>UpCase(LineCursor^.Line[1])) DO
         BEGIN
         Inc(i);
         LineCursor:=LineCursor^.NextLine;
         END;

      PosCursor:=LesserInt(Height,PosCursor+i);

      CLine:=LineCursor;
      i:=PosCursor;
      WHILE (i<Height) AND (CLine<>LastLine) DO
         BEGIN
         Inc(i);
         CLine:=CLine^.NextLine;
         END;
      { ASSERT: CLine point to the last menu line in window. }

      PosCursor:=PosCursor+(LastPos-i);

      i:=LastPos;
      REPEAT
         WriteString(Copy(CLine^.Line,1,Width-2),1,i,Col);
         CLine:=CLine^.PrevLine;
         Dec(i);
      UNTIL (i<1) OR (CLine=Nil);
      { ASSERT: Menu lines written }

      DrawMenuArrows(LineCursor,PosCursor,Width,Height,NoOfLines);
      WriteString(Copy(LineCursor^.Line,1,Width-2),1,PosCursor,HCol);
      END;  { with scope }
   END;  { KeySearch }

PROCEDURE ActivateMenu(VAR Menu: MenuStructure; Ch: Char);
   BEGIN
   ActivateWindow(Menu.Handle);
   CASE Ch OF
      UArrKey: MenuCursorUp(Menu);
      DArrKey: MenuCursorDown(Menu);
      PgUpKey: MenuPageUp(Menu);
      PgDnKey: MenuPageDown(Menu);
      '0'..'z': KeySearch(Menu,Ch);
   END;  { case }
   END;  { ActivateMenu }

END.
