(* EIO.PAS --------------------------------------------- TURBO Pascal 4.0 ------

   Extended Input Output Library   v1.0

   This UNIT contains procedures that write to the screen and enter data from
   the keyboard.

----------------------------------------------------------------------------- *)

UNIT EIO;

INTERFACE

USES Int,                                       { Integer routines. }
     Strg,                                      { String routines. }
     System2,                                   { Miscellaneous routines. }
     DOS,                                       { DOS Unit. }
     CRT;                                       { CRT Unit. }

TYPE
   LineArray = ARRAY [1..80] OF Word;
   ScrArray = ARRAY [1..25] OF LineArray;
   ScrArrayPtr = ^ScrArray;

   { Area structure. }
   SaveAreaRecord = RECORD
      x: Byte;
      y: Byte;
      W: Byte;
      H: Byte;
      Area: ^Byte;
   END;  { SaveAreaRecord }

   CharSet = SET OF Char;

   { Different cursor types }
   CursorTypes = ( NoCursor,BlockCursor,UnderlineCursor );

   KeyCode = Word;

CONST
   { Options for GetChoice }
   CaseDistinct = True;
   NoCaseDistinct = False;

   { Key constants. }
   FnKey1 = #187;    FnKey6 = #192;
   FnKey2 = #188;    FnKey7 = #193;
   FnKey3 = #189;    FnKey8 = #194;
   FnKey4 = #190;    FnKey9 = #195;
   FnKey5 = #191;    FnKey10 = #196;

   A_FnKey1 = #232;

   LArrKey = #203;   RArrKey = #205;
   UArrKey = #200;   DArrKey = #208;
   PgUpKey = #201;   PgDnKey = #209;
   HomeKey = #199;   EndKey = #207;

   DelKey = #211;

   S_LArrKey = #052; S_RArrKey = #054;
   S_UArrKey = #056; S_DArrKey = #050;
   S_PgUpKey = #057; S_PgDnKey = #051;

   C_PgUpKey = #132;
   C_PgDnKey = #246;

   AltP = #153;
   AltD = #160;
   AltZ = #172;
   AltN = #177;
	AltT = #148;

   ReturnKey = #013;
   BackspaceKey = #008;
   EscKey = #027;
   NoKey = #000;

   AnyKey: CharSet = [#001..#255];
   AltKeys: CharSet = [#144..#153,#158..#166,#172..#178];

{$I COLORS.INC}                                 { Include color definitions }

VAR
   ScrSeg: Word;                                { Seg addr of video buffer }
   VirtualScreen,RealScreen: ScrArrayPtr;
   C: ColorRecord;                              { Color definition record }
   StartUpMode: Word;

FUNCTION Alt2Char(Ch: Char): Char;
PROCEDURE EditString(VAR Line: LineStr; Ch: Char; VAR InpLen: Word;
                     x,y,Col,MaxLen: Word);
PROCEDURE EmptyKeyBuffer;
PROCEDURE GetChoice(LegalSet: CharSet; CaseDist: Boolean; VAR Ch: Char);
PROCEDURE GetInkey(VAR Ch: Char);
PROCEDURE InputPassword(VAR Word: String8; VAR ESCHit: Boolean);
PROCEDURE InputString(Prompt: LineStr; x,y,Col: Word; MaxInp: Word;
                      VAR Strg: LineStr );
PROCEDURE PressAnyKey(x,y: Word; Message: LineStr);
PROCEDURE RestoreArea(VAR S: SaveAreaRecord);
PROCEDURE RestoreScreen(VAR ScrBuf: ScrArray);
PROCEDURE SaveArea(PosX,PosY,Width,Height: Word; VAR S: SaveAreaRecord);
PROCEDURE SaveScreen(VAR ScrBuf: ScrArray);
PROCEDURE ScrollDown;
PROCEDURE ScrollLeft;
PROCEDURE ScrollRight;
PROCEDURE ScrollUp;
PROCEDURE SetCursor(NewCursor: CursorTypes);
PROCEDURE ToggleColorBW;
PROCEDURE TurnScreenOff;
   INLINE($BA/$DA/$03/$EC/$A8/$08/$74/$FB/$B0/$21/$B2/$D8/$EE);
PROCEDURE TurnScreenOn;
   INLINE($BA/$DA/$03/$EC/$A8/$08/$74/$FB/$B0/$29/$B2/$D8/$EE);
PROCEDURE Wait(Seconds: Word; VAR Key: Char);
PROCEDURE WriteString(St: LineStr; Col,Row,Attr: Word);
PROCEDURE WriteBlanks(Len,x,y,Col: Word);
PROCEDURE WriteHelpLine(HelpLine: LineStr);

(* -------------------------------------------------------------------------- *)

IMPLEMENTATION

CONST
   ColorCursorParameters: ARRAY [CursorTypes] OF Integer =
      ( $1010,$0007,$0607 );
   MonoCursorParameters: ARRAY [CursorTypes] OF Integer =
      ( $1010,$010D,$0C0D );

   AltTable: ARRAY [#144..#178] OF Char =
      ( 'Q','W','E','R','T','Y','U','I','O','P',' ',' ',' ',' ',
        'A','S','D','F','G','H','J','K','L',' ',' ',' ',' ',' ',
        'Z','X','C','V','B','N','M' );

(* ASSEMBLY LANGUAGE Routines ----------------------------------------------- *)
{$L FASTSCR}

PROCEDURE ScrollDown; EXTERNAL;
PROCEDURE ScrollLeft; EXTERNAL;
PROCEDURE ScrollRight; EXTERNAL;
PROCEDURE ScrollUp; EXTERNAL;
PROCEDURE WriteBlanks(Len,x,y,Col: Word); EXTERNAL;
PROCEDURE WriteString(St: LineStr; Col,Row,Attr: Word); EXTERNAL;

(* -------------------------------------------------------------------------- *)

FUNCTION Alt2Char(Ch: Char): Char;
{ Alt2Char:
   Ch is an character returned by GetInkey that is in AltKeys. This procedure
   will convert it to its proper letter. }

   BEGIN
   IF Ch IN AltKeys THEN
      Alt2Char:=AltTable[Ch]
   ELSE
      Alt2Char:=Ch;
   END;  { Alt2Char }

PROCEDURE SaveScreen(VAR ScrBuf: ScrArray);
{ SaveScreen: ------------------------------------------------------------------
   This procedure will save the video buffer to the given variable.
------------------------------------------------------------------------------ }
   BEGIN
   IF CheckSnow THEN
      BEGIN
      TurnScreenOff;
      ScrBuf:=VirtualScreen^;
      TurnScreenOn;
      END
   ELSE
      ScrBuf:=VirtualScreen^;
   END;  { SaveScreen }

PROCEDURE RestoreScreen(VAR ScrBuf: ScrArray);
{ RestoreScreen: ---------------------------------------------------------------
   This routine will restore a previously saved screen.
------------------------------------------------------------------------------ }
   BEGIN
   IF CheckSnow THEN
      BEGIN
      TurnScreenOff;
      VirtualScreen^:=ScrBuf;
      TurnScreenOn;
      END
   ELSE
      VirtualScreen^:=ScrBuf;
   END;  { RestoreScreen }

PROCEDURE SaveArea(PosX,PosY,Width,Height: Word; VAR S: SaveAreaRecord);
   VAR
      i,Segm,Offs,TotalSpace: Word;
      temp: ^Byte;

   BEGIN
   WITH S DO
      BEGIN
      X:=PosX;
      Y:=PosY;
      W:=Width;
      H:=Height;
      TotalSpace:=Width*Height*2;
      GetMem(Area,TotalSpace);
      Segm:=Seg(Area^);
      Offs:=Ofs(Area^);
      END;

   Width:=Width*2;
   FOR i:=0 TO Height-1 DO
      BEGIN
      temp:=Ptr(Segm,Offs);
      Move(VirtualScreen^[PosY][PosX],temp^,Width);
      Inc(Offs,Width);
      Inc(PosY);
      END;
   END;  { SaveArea }

PROCEDURE RestoreArea(VAR S: SaveAreaRecord);
   VAR
      Segm,Offs,TotalSpace,i,PosY,Width: Word;
      temp: ^Byte;

   BEGIN
   WITH S DO
      BEGIN
      Segm:=Seg(Area^);
      Offs:=Ofs(Area^);
      Width:=W*2;
      PosY:=Y;

      IF CheckSnow THEN
         BEGIN
         TurnScreenOff;
         FOR i:=0 TO H-1 DO
            BEGIN
            temp:=Ptr(Segm,Offs);
            Move(temp^,VirtualScreen^[PosY][X],Width);
            Inc(Offs,Width);
            Inc(PosY);
            END;
         TurnScreenOn;
         END
      ELSE
         BEGIN
         FOR i:=0 TO H-1 DO
            BEGIN
            temp:=Ptr(Segm,Offs);
            Move(temp^,VirtualScreen^[PosY][X],Width);
            Inc(Offs,Width);
            Inc(PosY);
            END;
         END;

      TotalSpace:=W*H*2;
      FreeMem(Area,TotalSpace);
      END;  { with scope }
   END;  { RestoreArea }

PROCEDURE GetInkey(VAR Ch: Char);
{ GetInkey: --------------------------------------------------------------------
   This routine will check to see if a key has been pressed.  If no key is
   pressed, it will return 'NoKey.'  If an ASCII key (letter, number, symbol, 
   etc.) is pressed, it will return that character.  If the key is a function
   key (cursor key, etc.) it will return a character with the code normally
   returned (minus the ESC char) except with bit 7 set.
------------------------------------------------------------------------------ }

   BEGIN
   IF KeyPressed THEN
      BEGIN
      Ch:=ReadKey;
      IF (Ch=NoKey) THEN
         Ch:=Char(Ord(ReadKey) OR 128);
      END
   ELSE
      Ch:=NoKey;
   END;  { GetInkey }

PROCEDURE GetChoice(LegalSet: CharSet; CaseDist: Boolean; VAR Ch: Char);
{ GetChoice: -------------------------------------------------------------------
   Waits until the key hit is in the given set of acceptable characters.
   If Mode=NoCaseDistinct, then it returns only uppercase characters and does
   not discriminate between cases.  (In which case, LegalSet must be in 
   uppercase.)
------------------------------------------------------------------------------ }

   BEGIN
   REPEAT
      GetInkey(Ch);
      IF NOT CaseDist THEN
         Ch:=UpCase(Ch);
   UNTIL Ch IN LegalSet;
   END;  { GetChoice }

PROCEDURE WriteHelpLine(HelpLine: LineStr);
{ WriteHelpLine:
   This procedure will write the given line on the last line of the real screen.
   Windowing is ignored. }

   VAR
      TempMin,TempMax: Word;

   BEGIN
   TempMin:=WindMin;
   TempMax:=WindMax;

   Window(1,1,80,25);

   WriteBlanks(80,1,25,C.SYSHelpLine);
   WriteString(Copy(HelpLine,1,80),1,25,C.SYSHelpLine);

   Window(Lo(TempMin)+1,Hi(TempMin)+1,Lo(TempMax)+1,Hi(TempMax)+1);
   END;  { WriteHelpLine }

PROCEDURE EmptyKeyBuffer;
   VAR
      Ch: Char;

   BEGIN
   WHILE KeyPressed DO
      Ch:=ReadKey;
   END;  { EmptyKeyBuffer }

PROCEDURE InputString(Prompt: LineStr; x,y,Col: Word; MaxInp: Word;
                      VAR Strg: LineStr);
{ InputString: -----------------------------------------------------------------
   This procedure will output 'Prompt' at location x,y relative to the
   current window.  It will allow an input up to 'MaxInp'  (max 255).
   This routine calls GetChoice to get the actual characters typed.
   If ESCKey is returned, then the user hit the ESC key at some point
   in the input.  If a blank line ('') is returned, then the user
   defaulted.
   If Strg is not blank on entry, then the string is taken as partial
   input.
------------------------------------------------------------------------------ }

   VAR
      InpString: LineStr;
      InpStart,InpLength: Word;
      Ch: Char;
      Done: Boolean;

   { InputString: MAIN PROCEDURE }
   BEGIN
   WriteString(Prompt,x,y,Col);
   InpStart:=Length(Prompt)+x;
   WriteBlanks(MaxInp,InpStart,y,Col);

   InpString:=Strg;
   InpLength:=Length(Strg);
   WriteString(InpString,InpStart,y,Col);
   GotoXY(InpStart+InpLength,y);

   Done:=False;
   REPEAT
      GetChoice(AnyKey,CaseDistinct,Ch);
      CASE Ch OF
         ESCKey: BEGIN
            Strg:=ESCKey;
            WriteBlanks(MaxInp,InpStart,y,Col);
            Done:=True;
            END;
         ReturnKey: BEGIN
            Strg:=InpString;
            Done:=True;
            END;
         ELSE
            EditString(InpString,Ch,InpLength,InpStart,y,Col,MaxInp);
         END;  { case }
   UNTIL Done;
   END;  { InputString }

PROCEDURE EditString(VAR Line: LineStr; Ch: Char; VAR InpLen: Word;
                     x,y,Col,MaxLen: Word);
   VAR
      StrLen: Word;

   BEGIN
   StrLen:=Length(Line);
   GotoXY(x+InpLen,y);
   CASE Ch OF
      BackspaceKey: BEGIN
         IF InpLen>0 THEN
            BEGIN
            Delete(Line,InpLen,1);
            Dec(InpLen);
            Write(BackspaceKey);
            WriteString(Line+' ',x,y,Col);
            END;
         END;
      DelKey: BEGIN
         IF (InpLen<StrLen) THEN
            BEGIN
            Delete(Line,InpLen+1,1);
            WriteString(Line+' ',x,y,Col);
            END;
         END;
      ' '..'~': BEGIN
         IF InpLen<MaxLen THEN
            BEGIN
            Inc(InpLen);
            Insert(Ch,Line,InpLen);
            Line:=Copy(Line,1,MaxLen);
            WriteString(Line,x,y,Col);
            GotoXY(x+InpLen,y);
            END;
         END;
      HomeKey: BEGIN
         InpLen:=0;
         GotoXY(x,y);
         END;
      EndKey: BEGIN
         InpLen:=StrLen;
         GotoXY(x+InpLen,y);
         END;
      LArrKey: BEGIN
         IF InpLen>0 THEN
            BEGIN
            Dec(InpLen);
            GotoXY(x+InpLen,y);
            END;
         END;
      RArrKey: BEGIN
         IF InpLen<StrLen THEN
            BEGIN
            Inc(InpLen);
            GotoXY(x+InpLen,y);
            END;
         END;
      END;  { case }
   END;  { EditString }

PROCEDURE PressAnyKey(x,y: Word; Message: LineStr);
{ PressAnyKey: -----------------------------------------------------------------
   This procedure will write the given message at the given co-ordinates and
   wait until a key is pressed. }

   VAR
      Ch: Char;

   BEGIN
   EmptyKeyBuffer;
   WriteString(Message,x,y,TextAttr);
	GotoXY(x+Length(Message),y);
   REPEAT UNTIL Keypressed;
   Ch:=ReadKey;
   END;  { PressAnyKey }

PROCEDURE SetCursor(NewCursor: CursorTypes);
   VAR
      R: Registers;

   BEGIN
   WITH R DO
      BEGIN
      IF LastMode<>Mono THEN
         CX:=ColorCursorParameters[NewCursor]
      ELSE
         CX:=MonoCursorParameters[NewCursor];

      AX:=$0100;
      END;  { with scope }

   Intr($10,R);
   END;  { SetCursor }

PROCEDURE InputPassword(VAR Word: String8; VAR ESCHit: Boolean);
   VAR
      Exit: Boolean;
      Inp: Char;

   BEGIN
   Exit:=False;
   ESCHit:=False;
   Word:='';
   REPEAT
      GetChoice(AnyKey,NoCaseDistinct,Inp);
      IF Inp=ESCKey THEN
         BEGIN
         ESCHit:=True;
         Exit:=True;
         END
      ELSE IF Inp=ReturnKey THEN
         Exit:=True
      ELSE IF Inp=BackspaceKey THEN
         BEGIN
         IF Length(Word)>0 THEN
            BEGIN
            Word:=Copy(Word,1,Length(Word)-1);
            Write(Inp);
            Write(' ');
            Write(Inp);
            END;
         END
      ELSE
         BEGIN
         IF Length(Word)<8 THEN
            BEGIN
            Word:=Word+Inp;
            Write('Þ');
            END;
         END;
   UNTIL Exit;
   END;  { InputPassword }

PROCEDURE ToggleColorBW;
   BEGIN
   IF LastMode=CO80 THEN
      BEGIN
      TextMode(BW80);
      C:=BWScrColor;
      END
   ELSE IF LastMode=BW80 THEN
      BEGIN
      TextMode(CO80);
      C:=ColorScrColor;
      END;
   END;  { ToggleColorBW }

PROCEDURE Wait(Seconds: Word; VAR Key: Char);
{ Wait: ------------------------------------------------------------------------
   This procedure wait for the specified number of seconds or until a key 
   is pressed.  Not very accurate for intervals <5 seconds.
------------------------------------------------------------------------------ }

   VAR
      EndTime: LongInt;

   BEGIN
   EndTime:=(AbsoluteTime+Seconds) MOD SecondsPerDay;

   EmptyKeyBuffer;
   REPEAT
      GetInkey(Key);
   UNTIL (Key<>NoKey) OR (AbsoluteTime>=EndTime);
   END;  { Wait }

{ EIO Initialization }
BEGIN
TextAttr:=LightGray;
StartUpMode:=LastMode;
{ Get video memory base segment. }
CASE Lo(LastMode) OF
   Mono: BEGIN
      ScrSeg:=$B000;
      C:=MonoScrColor;
      END;
   CO80: BEGIN
      ScrSeg:=$B800;
      C:=ColorScrColor;
      END;
   BW80: BEGIN
      ScrSeg:=$B800;
      C:=BWScrColor;
      END;
END;  { case }

{ Initialize virtual screen variables. }
RealScreen:=Ptr(ScrSeg,0);
VirtualScreen:=RealScreen;

CheckBreak:=False;
END.

