(* WND.PAS --------------------------------------------- TURBO Pascal 4.0 ------

   Windows Library   v1.0

   This UNIT contains simple windowing procedures that are used with EIO.  All
   the routines in this unit use the standard CRT Window command to set an
   area of the screen, so they are fully compatiple with WriteLn.  On the other
   hand, WriteString, and others, so not check for the end of a window.  The
   programmer must make sure that these routines do not write outside window
   boundaries.

   USAGE:

   1. Declare a variable of type WindowHandle for every window that you wish 
      to have simultaneously open.

   2. Call OpenWindow with the appropriate parameters for each of the windows.  
      Once a window is active, all WriteLn and EIO routines will take place 
      only in the window.  If another window is opened on top, then that 
      window will become active. 

   3. Call ActivateWindow to activate a window that is underneath another.

   4. Call CloseWindow to close the top-most window.  Repeat until all windows 
      are closed.

   INTERFACE:

   ActivateWindow(WNum: WindowHandle)                   PROCEDURE
      This procedure will bring the given window to the foreground.

   ActiveWindow: WindowHandle                           FUNCTION
      Returns the window handle of the top-most window.

   AttentionWindow(S1,S2: LineStr; VAR Abort: Boolean)  PROCEDURE
      This procedure opens a small window in the middle of the screen and 
      prints the two strings, one on a line, centered in the window.  
      AttentionWindow then waits for the user to hit a key before continuing.  
      If Abort is True on entry, then the procedure will also implement a 
      cancel option:  If the Esc key is hit it will return True in Abort, 
      otherwise, False is returned.  A help line will be displayed as 
      necessary.  This procedure is very useful for implementing warnings as 
      in: "Are you sure you want to quit?".

   BorderTypes                                          TYPE
      There are 7 different borders that can be used with windows.  They are:
               NoBRD                   No border.
               TopOnlyBRD              Border on top line only.
               TopAndBottomBRD         Top and bottom borders only.
               TextBRD                 Top border is the title, bottom is a 
                                          line.
               ThinBRD                 Full border.  Thin lines.
               DoubleBRD               Full border.  Double lines.
               ThickBRD                Full border.  Thick lines.

   ChangeWindowColor(NewCol: Byte)                      PROCEDURE
      This routine will clear the window and change the background color to 
      the specified attribute.  NOTE: NewCol is an attribute (0..255) not a 
      color. 

   ChangeWindowTitle(WNum: WindowHandle;                PROCEDURE
                     NewTitle: LineStr)
      Changes the title of the given window.  The new title should no be 
      longer than the old title, however.

   CloseWindow                                          PROCEDURE
      Closes the top-most window.  Repeated calls to the procedure must be 
      made to close all the windows.  No error is generated if CloseWindow is 
      called with no open windows.

   OpenWindow(Nx,Ny,NWidth,NHeight: Byte;               PROCEDURE
              Bord: BorderTypes;
              NTitle: LineStr;
              WindowCol,BorderCol: Byte;
              VAR WNum: WindowHandle)
      This procedure creates and opens a new window of the specified 
      dimensions.  Nx and Ny are the x,y coordinates of the top-left corner of 
      the window.  These are always expressed in absolute coordinates starting 
      with 1,1 at the top-left of the screen.  NWidth and NHeight are the 
      width and height of the window (in characters) including the border.  
      (The actual work area may be smaller because of the borders.)  Bord is 
      the tpye of border that should be used (see BorderTypes.)  WindowCol and 
      BorderCol are the video attributes of the window and the border, 
      respectively.  Finaly, OpenWindow returns a handle that identifies the 
      window for further commands.

   WindowHandle                                         TYPE
      This variable type is used to identify each window.  When OpenWindow is 
      called it returns a variable of type WindowHandle.  That variable 
      specifies the window and must be used in any further procedures.

----------------------------------------------------------------------------- *)

UNIT WND;

INTERFACE

USES Strg,                                      { Strings Library }
     Int,                                       { Integers Library }
     CRT,                                       { CRT Unit. }
     EIO;                                       { Extended I/O Library }

TYPE
   WindowHandle = Byte;
   BorderTypes = ( NoBRD,
                   TopOnlyBRD,
                   TopAndBottomBRD,
                   TextBRD,
                   ThinBRD,
                   DoubleBRD,
                   ThickBRD );

PROCEDURE ActivateWindow(WNum: WindowHandle);
FUNCTION ActiveWindow: WindowHandle;
PROCEDURE AttentionWindow(S1,S2: LineStr; VAR Abort: Boolean);
PROCEDURE ChangeWindowColor(NewCol: Byte);
PROCEDURE ChangeWindowTitle(WNum: WindowHandle; NewTitle: LineStr);
PROCEDURE CloseWindow;
PROCEDURE DOSErrorWindow(Error: Word; Parm: LineStr);
PROCEDURE OpenWindow(Nx,Ny,NWidth,NHeight: Byte;
                     Bord: BorderTypes;
                     NTitle: LineStr;
                     WindowCol: Byte;
                     BorderCol: Byte;
                     VAR WNum: WindowHandle);
PROCEDURE MessageWindow(S1,S2: LineStr);

IMPLEMENTATION

CONST
   MaxNoOfWindows = 20;

TYPE
   WindowRecord = RECORD
      InUse: Boolean;
      X,Wx: Byte;  { position of window }
      Y,Wy: Byte;
      W,Ww: Byte;  { width and height of window }
      H,Wh: Byte;
      CursorX,CursorY: Byte;
      Title: LineStr;
      Active: Boolean;
      Border: BorderTypes;
      Col: Byte;
      BCol: Byte;
      SavedWindow: SaveAreaRecord;
   END;  { WindowRecord }

   WindowStackArray = ARRAY [1..MaxNoOfWindows] OF Byte;
   WindowArray = ARRAY [1..MaxNoOfWindows] OF WindowRecord;

VAR
   WindowStack: WindowStackArray;
   WNDW: WindowArray;
   CurWind: WindowHandle;

PROCEDURE DrawBorder(Bord: BorderTypes;
                     x1,y1,w,h: Byte; Title: LineStr;
                     BCol: Byte;
                     VAR Wx,Wy,Ww,Wh: Byte);

   CONST
      PosXAdj: ARRAY [BorderTypes] OF Byte =
         ( 0,0,0,0,1,1,1 );
      PosYAdj: ARRAY [BorderTypes] OF Byte =
         ( 0,1,1,1,1,1,1 );
      WidthAdj: ARRAY [BorderTypes] OF Byte =
         ( 0,0,0,0,2,2,2 );
      HeightAdj: ARRAY [BorderTypes] OF Byte =
         ( 0,1,2,2,2,2,2 );
      CornerChr: array[BorderTypes] of array[1..4] of Byte =
         (  ( 32,32,32,32 ),
            ( 220,220,32,32 ),
            ( 220,220,223,223 ),
            ( 32,32,196,196 ),
            ( 218,191,192,217 ),
            ( 201,187,200,188 ),
            ( 220,220,223,223 )  );
      TopChr: array[BorderTypes] of Byte =
            ( 32,220,220,220,196,205,220 );
      BottomChr: array[BorderTypes] of Byte =
            ( 32,32,223,196,196,205,223 );
      LeftChr: array[BorderTypes] of Byte =
            ( 32,32,32,32,179,186,221 );
      RightChr: array[BorderTypes] of Byte =
            ( 32,32,32,32,179,186,222 );
      LeftStopChr: array[BorderTypes] of Byte =
            ( 32,221,221,221,180,181,221 );
      RightStopChr: array[BorderTypes] of Byte =
            ( 32,221,222,222,195,198,222 );

   procedure DrawTextTop(Title: LineStr;
                         x1,y1,w,h: Byte);
      var
         Offset: Integer;
         i: Integer;

      begin
      Offset:=(x1-1)*2+(y1-1)*160;
      if Length(Title)>w then
         Title:=Copy(Title,1,w);

      for i:=1 to w do
         Mem[ScrSeg:Offset+(i-1)*2+1]:=BCol;
      for i:=1 to Length(Title) do
         Mem[ScrSeg:Offset+(i-1)*2]:=Integer(Title[i]);
      end;  { DrawTextTop }

   procedure DrawTop(Bord: BorderTypes;
                     x1,y1,w,h: Byte; Title: LineStr);
      var
         Offset: Integer;
         StrLen: Byte;
         i: Integer;

      begin
      Offset:=(x1-1)*2+(y1-1)*160;
      for i:=1 to w-2 do
         begin
         Mem[ScrSeg:Offset+i*2]:=TopChr[Bord];
         Mem[ScrSeg:Offset+i*2+1]:=BCol;
         end;

      StrLen:=Length(Title)+4;
      if (StrLen<=(w-2)) and (StrLen>4) then
         begin
         Offset:=Offset+4;
         Mem[ScrSeg:Offset]:=LeftStopChr[Bord];
         Mem[ScrSeg:Offset+2]:=32;
         Mem[ScrSeg:Offset+StrLen*2-4]:=32;
         Mem[ScrSeg:Offset+StrLen*2-2]:=RightStopChr[Bord];

         for i:=1 to Length(Title) do
            Mem[ScrSeg:Offset+2+i*2]:=Ord(Title[i]);
         end;
      end;  { DrawTop }

   procedure DrawBottom(Bord: BorderTypes;
                        x1,y1,w,h: Byte);
      var
         i,OffSet: Integer;

      begin
      Offset:=(x1-1)*2+(y1-1)*160;
      for i:=1 to w-2 do
         begin
         Mem[ScrSeg:Offset+i*2+(h-1)*160]:=BottomChr[Bord];
         Mem[ScrSeg:Offset+i*2+(h-1)*160+1]:=BCol;
         end;
      end;  { DrawBottom }

   procedure DrawSides(Bord: BorderTypes;
                       x1,y1,w,h: Byte);
      var
         i,Offset: Word;

      begin
      Offset:=(x1-1)*2+(y1-1)*160;
      for i:=1 to h-2 do
         begin
         Mem[ScrSeg:Offset+i*160]:=LeftChr[Bord];
         Mem[ScrSeg:Offset+i*160+1]:=BCol;
         Mem[ScrSeg:Offset+i*160+(w-1)*2]:=RightChr[Bord];
         Mem[ScrSeg:Offset+i*160+(w-1)*2+1]:=BCol;
         end;

      end;  { DrawSides }

   procedure DrawCorners(Bord: BorderTypes;
                         x1,y1,w,h: Byte);
      var
         Offset,i: Integer;

      begin
      Offset:=(x1-1)*2+(y1-1)*160;
      Mem[ScrSeg:Offset]:=CornerChr[Bord][1];
      Mem[ScrSeg:Offset+1]:=BCol;
      Mem[ScrSeg:Offset+(w-1)*2]:=CornerChr[Bord][2];
      Mem[ScrSeg:Offset+(w-1)*2+1]:=BCol;
      Mem[ScrSeg:Offset+(h-1)*160]:=CornerChr[Bord][3];
      Mem[ScrSeg:Offset+(h-1)*160+1]:=BCol;
      Mem[ScrSeg:Offset+(h-1)*160+(w-1)*2]:=CornerChr[Bord][4];
      Mem[ScrSeg:Offset+(h-1)*160+(w-1)*2+1]:=BCol;
      end;  { DrawCorners }

   { DrawBorder: MAIN PROCEDURE }
   BEGIN
   CASE Bord OF
      NoBRD: BEGIN END;
      TopOnlyBRD: BEGIN
         DrawTop(Bord,x1,y1,w,h,Title);
         DrawCorners(Bord,x1,y1,w,h);
         END;
      TopAndBottomBRD: BEGIN
         DrawTop(Bord,x1,y1,w,h,Title);
         DrawBottom(Bord,x1,y1,w,h);
         DrawCorners(Bord,x1,y1,w,h);
         END;
      TextBRD: BEGIN
         DrawCorners(Bord,x1,y1,w,h);
         DrawTextTop(Title,x1,y1,w,h);
         DrawBottom(Bord,x1,y1,w,h);
         END;
      ThinBRD..
      ThickBRD: BEGIN
         DrawTop(Bord,x1,y1,w,h,Title);
         DrawBottom(Bord,x1,y1,w,h);
         DrawSides(Bord,x1,y1,w,h);
         DrawCorners(Bord,x1,y1,w,h);
         END;
   END;  { case }

   Wx:=x1+PosXAdj[Bord];
   Wy:=y1+PosYAdj[Bord];
   Ww:=w-WidthAdj[Bord];
   Wh:=h-HeightAdj[Bord];
   END;  { DrawBorder }

FUNCTION EmptyWindowSlot: WindowHandle;
   VAR
      i: WindowHandle;

   BEGIN
   FOR i:=1 TO MaxNoOfWindows DO
      WITH WNDW[i] do
         IF NOT InUse THEN
            BEGIN
            EmptyWindowSlot:=i;
            Exit;
            END;

   EmptyWindowSlot:=0;
   END;  { EmptyWindowSlot }

PROCEDURE DeActivateWindow(WNum: WindowHandle);

   BEGIN
   WITH WNDW[WNum] do
      BEGIN
      CursorX:=WhereX;
      CursorY:=WhereY;
      Active:=False;
      SaveArea(X,Y,W,H,SavedWindow);
      END;  { with scope }
   END;  { DeActivateWindow }

PROCEDURE OpenWindow(Nx,Ny,NWidth,NHeight: Byte;
                     Bord: BorderTypes;
                     NTitle: LineStr;
                     WindowCol: Byte;
                     BorderCol: Byte;
                     VAR WNum: WindowHandle);

   { OpenWindow: MAIN PROCEDURE }
   BEGIN
   IF CurWind<>0 then
      DeActivateWindow(WindowStack[CurWind]);

   CurWind:=CurWind+1;
   WNum:=EmptyWindowSlot;
   IF WNum>0 THEN
      BEGIN
      WindowStack[CurWind]:=WNum;

      WITH WNDW[WNum] DO
         BEGIN
         InUse:=True;
         x:=Nx;
         y:=Ny;
         W:=NWidth;
         H:=NHeight;
         Active:=True;

         DrawBorder(Bord,x,y,W,H,NTitle,BorderCol,Wx,Wy,Ww,Wh);
         Window(Wx,Wy,Wx+(Ww-1),Wy+(Wh-1));

         Col:=WindowCol;
         BCol:=BorderCol;

         TextAttr:=Col;

         ClrScr;
         Border:=Bord;
         Title:=NTitle;
         END;  { with scope }
      END;  { if }
   END;  { OpenWindow }

PROCEDURE ActivateWindow(WNum: WindowHandle);
{ ActivateWindow:
   This procedure will re-activate a window that is in the background. }

   VAR
      i: Byte;
      Pos: Byte;

   FUNCTION Position(WNum: WindowHandle): WindowHandle;
   { Position:
      This function returns the position of 'WNum' on the stack. }
      VAR
         i: Byte;

      BEGIN
      FOR i:=1 TO MaxNoOfWindows DO
         BEGIN
         IF WindowStack[i]=WNum THEN
            BEGIN
            Position:=i;
            Exit;
            END;
         END;

      Position:=0;
      END;  { Position }

   { ActivateWindow: MAIN PROCEDURE }
   BEGIN
   IF WNDW[WNum].InUse THEN
      BEGIN
      IF WindowStack[CurWind]<>WNum THEN
         BEGIN
         { re-order stack }
         Pos:=Position(WNum);                 { Pos = position of wind in stack }
         FOR i:=Pos+1 TO CurWind DO
            BEGIN
            WindowStack[i-1]:=WindowStack[i];
            END;

         IF CurWind>1 THEN
            DeActivateWindow(WindowStack[CurWind-1]);

         WindowStack[CurWind]:=WNum;
         END;

      { call window }
      WITH WNDW[WNum] DO
         IF NOT Active THEN
            BEGIN
            RestoreArea(SavedWindow);
            Active:=True;
            Window(Wx,Wy,Wx+(Ww-1),Wy+(Wh-1));
            GotoXY(CursorX,CursorY);
            TextAttr:=Col;
            END;  { with scope }
      END;  { with scope and if }
   END;  { ActivateWindow }

PROCEDURE CloseWindow;
{ CloseWindow:
   This procedure closes the current window.  The window beneath it is activated.
   }

   VAR
      i: Integer;
      TempScr: ScrArray;
      OldSetting: Boolean;

   BEGIN
   IF CurWind>0 THEN
      BEGIN
      WITH WNDW[WindowStack[CurWind]] DO
         BEGIN
         Window(x,y,x+w-1,y+h-1);
         ClrScr;
         InUse:=False;
         END;  { with scope }

      SaveScreen(TempScr);
      VirtualScreen:=Ptr(Seg(TempScr),Ofs(TempScr));
      OldSetting:=CheckSnow;
      CheckSnow:=False;

      FOR i:=1 TO CurWind-1 DO
         WITH WNDW[WindowStack[i]] DO
            BEGIN
            RestoreArea(SavedWindow);
            SaveArea(X,Y,W,H,SavedWindow);
            END;

      VirtualScreen:=RealScreen;
      CheckSnow:=OldSetting;
      RestoreScreen(TempScr);

      CurWind:=CurWind-1;
      IF CurWind>0 THEN
         ActivateWindow(WindowStack[CurWind])
      ELSE
         BEGIN
         Window(1,1,80,25);
         ClrScr;
         END;
      END;  { if }
   END;  { CloseWindow }

FUNCTION ActiveWindow: WindowHandle;
{ ActiveWindow:
   This procedure returns the handle of the currently active window. }

   BEGIN
   IF CurWind=0 THEN
      ActiveWindow:=0
   ELSE
      ActiveWindow:=WindowStack[CurWind];
   END;  { ActiveWindow }

PROCEDURE ChangeWindowColor(NewCol: Byte);
{ ChangeWindowColor:
   This procedure will change the background color of the current window.  The 
   window is cleared. }

   VAR
      WNum: WindowHandle;

   BEGIN
   WNum:=ActiveWindow;
   IF WNum>0 THEN
      WITH WNDW[WNum] DO
         BEGIN
         Col:=NewCol;
         TextAttr:=Col;
         ClrScr;
         END;  { with scope }
   END;  { ChangeWindowColor }

PROCEDURE ChangeWindowTitle(WNum: WindowHandle; NewTitle: LineStr);
   VAR
      OldX,OldY: Byte;

   BEGIN 
   ActivateWindow(WNum); 
   OldX:=WhereX;  OldY:=WhereY; 
   Window(1,1,80,25); 
   WITH WNDW[WNum] DO
      BEGIN
      NewTitle:=Copy(NewTitle,1,Length(Title));
      WriteString(NewTitle,X+4,Y,BCol);
      Window(Wx,Wy,Wx+(Ww-1),Wy+(Wh-1));
      GotoXY(OldX,OldY);
      TextAttr:=Col;
      END;  { with scope }
   END;  { NewTitle }

PROCEDURE MessageWindow(S1,S2: LineStr);
   VAR
      Width: Word;
      Wind: WindowHandle;

   BEGIN
   Width:=LesserInt(GreaterInt(Length(S1),Length(S2))+10,80);
   S1:=Copy(S1,1,75);
   S2:=Copy(S2,1,75);
   OpenWindow((82-Width) DIV 2,7,Width,4,ThinBRD,'',C.SYSAttnWind,C.SYSWBorder,Wind);

   WriteString(S1,5,1,C.SYSAttnWind);
   WriteString(S2,5,2,C.SYSAttnWind);
   END;  { MessageWindow }

PROCEDURE AttentionWindow(S1,S2: LineStr; VAR Abort: Boolean);
{ AttentionWindow: -------------------------------------------------------------
   This procedure opens a small window in the middle of the screen and prints
   the two strings, one on a line.  AttentionWindow then waits for the user to
   hit a key before continuing.  If Abort is true, then the procedure will
   also implement a cancel option:  if the Esc key is hit it will return True
   in Abort, otherwise, False is always returned.  A help line will be displayed
   as necessary.
------------------------------------------------------------------------------ }

   VAR
      Handle: WindowHandle;
      WindowWidth: Byte;
      Ch: Char;

   BEGIN
   WindowWidth:=LesserInt(GreaterInt(Length(S1),Length(S2))+14,80);
   S1:=Copy(S1,1,67);
   S2:=Copy(S2,1,67);
   OpenWindow((82-WindowWidth) DIV 2,7,WindowWidth,7,ThinBRD,'',C.SYSAttnWind,C.SYSWBorder,Handle);

   IF Abort THEN
      BEGIN
      WriteString(' ‹‹‹ ',3,1,C.SYSAttnWind);
      WriteString('ﬂ  ﬁ€',3,2,C.SYSAttnWind);
      WriteString('  ‹ﬂ ',3,3,C.SYSAttnWind);
      WriteString('  ‹  ',3,4,C.SYSAttnWind);
      END
   ELSE
      BEGIN
      WriteString(' ‹',4,1,C.SYSAttnWind);
      WriteString('ﬁ€›',4,2,C.SYSAttnWind);
      WriteString(' €',4,3,C.SYSAttnWind);
      WriteString(' ‹',4,4,C.SYSAttnWind);
      END;

   WriteString(S1,10,2,C.SYSAttnWind);
   WriteString(S2,10,3,C.SYSAttnWind);

   IF Abort THEN
      BEGIN
      WriteHelpLine('<Esc>:Cancel <Any key>:Continue...');
      GetChoice(AnyKey,NoCaseDistinct,Ch);
      IF (Ch=ESCKey) THEN
         Abort:=True
      ELSE
         Abort:=False;
      END
   ELSE
      BEGIN
      WriteHelpLine('<Any key>:Continue...');
      GetChoice(AnyKey,NoCaseDistinct,Ch);
      Abort:=False;
      END;

   CloseWindow;
   END;  { AttentionWindow }

PROCEDURE DOSErrorWindow(Error: Word; Parm: LineStr);
{ DOSErrorWindow: --------------------------------------------------------------
------------------------------------------------------------------------------ }
   VAR
      ErrLine: LineStr;
      ErrorStr: String8;
      Abort: Boolean;

   BEGIN
   CASE Error OF
        2: ErrLine:='"'+Parm+'" does not exist.';
        3: ErrLine:='Bad path: "'+Parm+'".';
        4: ErrLine:='DOS reports too many open files.';
        5: ErrLine:='Access to "'+Parm+'" is denied.';
        8: ErrLine:='Not enough memory.';
       15: ErrLine:='Bad drive letter: "'+Parm+'".';
       16: ErrLine:='The current directory cannot be removed!';
       17: ErrLine:='You can''t rename across drives!';
      100: ErrLine:='"'+Parm+'" ends unexpectedly!';
      101: ErrLine:='Error writing "'+Parm+'" to the disk. (Disk full.)';
      106: ErrLine:='Bad numeric format in "'+Parm+'".';
      150: ErrLine:='The disk is write-protected!';
      152: IF Parm<>'PRN' THEN
              ErrLine:='Drive is not ready.  Make sure the drive door is closed.'
           ELSE
              ErrLine:='Printer is off-line or not connected.';
      159: ErrLine:='Printer is out of paper!';
      160: ErrLine:='Device write fault.';
      161: ErrLine:='Device read fault.';
   ELSE
      BEGIN
      Str(Error,ErrorStr);
      ErrLine:='I/O ERROR '+ErrorStr+':  Sorry, I don''t known what that means.';
      END;
   END;  { case }
   Abort:=False;
   AttentionWindow(ErrLine,'Press any key to continue...',Abort);
   END;  { DOSErrorWindow }

BEGIN
{ Initialize window stack. }
FillChar(WNDW,SizeOf(WNDW),0);
FillChar(WindowStack,SizeOf(WindowStack),0);
CurWind:=0;
END.

