(* SWINDOWS.PAS ----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT SWindows;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES System2,
     Crt,                                       { Standard CRT Library. }
     Strg,                                      { String Library. }
     Int,                                       { Integer Library. }
     EIO,                                       { Extended IO Library. }
     WND,
     TextStrc,
     Types,
     Environ,
     PrimIntr,
     WndTypes;

TYPE
   WindowTypes = ( NoWND, HelpWND, ScanWND, StatusWND, FleetWND,
                   NewsWND, EmpireWND, NamesWND );

VAR
   ElapsedTime,TimeMark,SecondsLeft: LongInt;
   CommandWindow,DisplayWindow: WindowHandle;
   SuppressWindows: Boolean;
	ModalInput: Boolean;
	InCommand: Boolean;
	InProcess: Boolean;

PROCEDURE ActivateLatestStatusWindow;
PROCEDURE AddToInputStream(Line: LineStr);
PROCEDURE CloseAllWindows;
PROCEDURE GetKeyPressed(VAR Key: Char);
PROCEDURE GetCharacter(LegalSet: CharSet; Mode: Boolean; VAR Ch: Char);
PROCEDURE GetInputString(x,y: Word; VAR Strg: LineStr; MaxInp: Word);
PROCEDURE InitializeWindows(Player: Empire);
PROCEDURE PauseCommand;
PROCEDURE StopClock(VAR PausedTimeMark: LongInt);
PROCEDURE StartClock(VAR PausedTimeMark: LongInt);
PROCEDURE UpdateClock;

IMPLEMENTATION

USES
   HlpWind,
   MapWind,
   FltWind,
   NwsWind,
   NmsWind,
   EmpWind,
   StaWind;

CONST
   CommandKeys = [' '..'/',':'..'~'];

   ProbeMacro      = 'FP'+FnKey10+ReturnKey;
   ProductionMacro = 'WP'+FnKey10+ReturnKey;
   DesignateMacro  = 'WD'+FnKey10+ReturnKey;
   NameMacro       = 'WN'+FnKey10+ReturnKey;

VAR
   CurrentWindow: WindowTypes;

   { This variable is True if a command window (CommW, DispW, etc.) is active,
     as opposed to a status window. }
   ComWindActive: Boolean;

   InputStream: LineStr;

   { This is the handle of the command window currently in use.  Even though
     status windows may be open above this, the variable keeps track of the
     command window so that it can be re-opened when the user presses an
     alphabetic key. }
   CurrentComWind: WindowHandle;

   TimeUpdateLoop: Integer;

PROCEDURE GetWindowCommand(ExtCh: Char; VAR Com: Word; VAR Wind: WindowTypes);
{ GetWindowCommand:
   Translates from extended character code to the command that it
   represents.  }

   BEGIN
   Com:=NoWCM;
   Wind:=CurrentWindow;
   CASE ExtCh OF
         FnKey1: BEGIN Com:=OpenWCM; Wind:=HelpWND; END;

         FnKey3,
         FnKey4: BEGIN Com:=OpenWCM; Wind:=StatusWND; END;

         FnKey5,
         FnKey6: BEGIN Com:=OpenWCM; Wind:=FleetWND; END;

         FnKey7: BEGIN Com:=OpenWCM; Wind:=NewsWND; END;
         FnKey8: BEGIN Com:=OpenWCM; Wind:=EmpireWND; END;
         FnKey9: BEGIN Com:=OpenWCM; Wind:=NamesWND; END;
        FnKey10: BEGIN Com:=OpenWCM; Wind:=ScanWND; END;

        PgUpKey,
      S_UArrKey: Com:=PageUpWCM;
        UArrKey: Com:=CursorUpWCM;

        PgDnKey,
      S_DArrKey: Com:=PageDownWCM;
        DArrKey: Com:=CursorDownWCM;

        LArrKey: Com:=CursorLeftWCM;
      S_LArrKey: Com:=PageLeftWCM;

        RArrKey: Com:=CursorRightWCM;
      S_RArrKey: Com:=PageRightWCM;

        '5','%',
        HomeKey: Com:=CenterWCM;
         EndKey: Com:=EndWCM;
        ReturnKey: Com:=SelectWCM;
      END;  { case }
   END;  { GetWindowCommand }

PROCEDURE WindowRoutine(Wind: WindowTypes; Com: Word);
   BEGIN
   CASE Wind OF
        HelpWND: HelpWindow(HLP^,Com);
        ScanWND: ScanWindow(MAP^,Com);
      StatusWND: StatusWindow(STA^,Com);
       FleetWND: FleetWindow(FLT^,Com);
        NewsWND: NewsWindow(NWS^,Com);
      EmpireWND: EmpireWindow(EMP^,Com);
       NamesWND: NamesWindow(NMS^,Com);
      END;  { case }
   END;  { WindowRoutine }

PROCEDURE GetNextChar(VAR Key: Char);
   BEGIN
   IF InputStream='' THEN
      GetInkey(Key)
   ELSE
      BEGIN
      Key:=InputStream[1];
      InputStream:=Copy(InputStream,2,80);
      END;
   END;  { GetNextChar }

(* MAIN PROCEDURES ---------------------------------------------------------- *)

PROCEDURE AddToInputStream(Line: LineStr);
   BEGIN
   InputStream:=Line;
   END;  { AddToInputStream }

PROCEDURE ActivateLatestStatusWindow;
   BEGIN
   IF (ComWindActive) AND (CurrentWindow<>NoWND) THEN
      BEGIN
      ComWindActive:=False;
      CurrentComWind:=CommandWindow;
      WindowRoutine(CurrentWindow,OpenWCM);
      END;
   END;  { ActivateLatesStatusWindow }

PROCEDURE GetKeyPressed(VAR Key: Char);
{ GetKeyPressed:
   This is the main input routine used by all parts of the program.  This
   procedure will check for input.  If there is input, it will return the
   character hit.  If there is no input, it will return Chr(0).  In addition,
   this procedure will call status procedure if one of the function keys is
   pressed. If the variable CommandMode is true, then any letter or number hit
   will activate the command window (even if other windows are active). }

   VAR
      KeyHit: Char;
      Code: Word;
      NewWindow: WindowTypes;

   { GetKeyPressed: MAIN PROCEDURE }
   BEGIN
   GetNextChar(Key);

   { Pause }
   IF (Key=A_FnKey1) AND (PauseActive) THEN
      BEGIN
      PauseCommand;
      Key:=NoKey;
      END
	ELSE IF ModalInput THEN
		BEGIN
		{ do not process any keys }
		END
   { macros }
   ELSE IF Key=AltP THEN
      BEGIN
      AddToInputStream(ProductionMacro);
      Key:=NoKey;
      END
   ELSE IF Key=AltD THEN
      BEGIN
      AddToInputStream(DesignateMacro);
      Key:=NoKey;
      END
   ELSE IF Key=AltZ THEN
      BEGIN
      AddToInputStream(ProbeMacro);
      Key:=NoKey;
      END
   ELSE IF Key=AltN THEN
      BEGIN
      AddToInputStream(NameMacro);
      Key:=NoKey;
      END
   ELSE IF ComWindActive THEN
      BEGIN
      GetWindowCommand(Key,Code,CurrentWindow);
      IF (Code=OpenWCM) THEN
         BEGIN
         IF NOT SuppressWindows THEN
            BEGIN
            ComWindActive:=False;
            CurrentComWind:=ActiveWindow;
            WindowRoutine(CurrentWindow,Code);
            END;
         Key:=NoKey;
         END
      ELSE IF (Key=FnKey2) AND (NOT SuppressWindows) THEN
         BEGIN
         ActivateWindow(DisplayWindow);
         ActivateWindow(CurrentComWind);
         Key:=NoKey;
         END;
      END
   ELSE
      BEGIN
      IF Key=ESCKey THEN
         BEGIN
         WindowRoutine(CurrentWindow,DeActWCM);
         ActivateWindow(CurrentComWind);
         ComWindActive:=True;
         WriteHelpLine('F1:Help F3:Status F5:Fleets F7:News F8:Empire F9:Names F10:Map');
         Key:=NoKey;
         END
      ELSE IF (Key IN CommandKeys) OR ((Key IN ['0'..'9']) AND (CurrentWindow<>ScanWND))THEN
         BEGIN
         WindowRoutine(CurrentWindow,DeActWCM);
         ActivateWindow(CurrentComWind);
         ComWindActive:=True;
         WriteHelpLine('F1:Help F3:Status F5:Fleets F7:News F8:Empire F9:Names F10:Map');
         END
      ELSE IF (Key=FnKey2) THEN
         BEGIN
         WindowRoutine(CurrentWindow,DeActWCM);
         ActivateWindow(DisplayWindow);
         ActivateWindow(CurrentComWind);
         ComWindActive:=True;
         WriteHelpLine('F1:Help F3:Status F5:Fleets F7:News F8:Empire F9:Names F10:Map');
         Key:=NoKey;
         END
      ELSE
         BEGIN
         GetWindowCommand(Key,Code,NewWindow);
         IF NewWindow<>CurrentWindow THEN
            WindowRoutine(CurrentWindow,DeActWCM);
         CurrentWindow:=NewWindow;
         WindowRoutine(CurrentWindow,Code);
         Key:=NoKey;
         END;
      END;

   UpdateClock;
   END;  { GetKeyPressed }

PROCEDURE GetCharacter(LegalSet: CharSet; Mode: Boolean; var Ch: Char);
{ GetCharacter:
   Waits until a key pressed is in the given set and returns the key pressed.
   Calls GetKeyPressed. }

   begin
   repeat
      GetKeyPressed(Ch);
      if NOT Mode then
         Ch:=UpCase(Ch);
   until Ch in LegalSet;
   end;  { GetCharacter }

PROCEDURE GetInputString(x,y: Word; VAR Strg: LineStr; MaxInp: Word);
{ GetInputString: --------------------------------------------------------------
   This procedure will input 'Strg' at location x,y relative to the
   current window.  It will allow an input up to 'MaxInp'  (max 255).
   This routine calls GetCharacter to get the actual characters typed.
   This routine will consider a space to be a return.
------------------------------------------------------------------------------ }

   VAR
      InputString: LineStr;
      InputLength: Word;
      Ch: Char;
      Done: Boolean;

   { GetInputString: MAIN PROCEDURE }
   BEGIN
   WriteBlanks(MaxInp,x,y,TextAttr);

   InputString:=Copy(Strg,1,MaxInp);
   InputLength:=Length(InputString);
   WriteString(Strg,x,y,TextAttr);
   GotoXY(x+InputLength,y);

   Done:=False;
   REPEAT
      GetCharacter(AnyKey,CaseDistinct,Ch);
      CASE Ch OF
         ReturnKey,' ': BEGIN
            Strg:=InputString;
            Done:=True;
            END;
         ESCKey: BEGIN
            Strg:=ESCKey;
            WriteBlanks(MaxInp,x,y,TextAttr);
            Done:=True;
            END;
         ELSE
            EditString(InputString,Ch,InputLength,x,y,TextAttr,MaxInp);
         END; { case }
   UNTIL Done;
   END;  { GetInputString }

PROCEDURE CloseAllWindows;
   BEGIN
   NamesWindow(NMS^,CloseWCM);
   HelpWindow(HLP^,CloseWCM);
   FleetWindow(FLT^,CloseWCM);
   StatusWindow(STA^,CloseWCM);
   EmpireWindow(EMP^,CloseWCM);
   NewsWindow(NWS^,CloseWCM);
   ScanWindow(MAP^,CloseWCM);
   END;  { CloseAllWindows }

PROCEDURE InitializeWindows(Player: Empire);
   BEGIN
   ComWindActive:=True;
   InputStream:='';
   CurrentWindow:=NoWND;
   SuppressWindows:=False;

   InitScanWindowMap(MAP^,Player);
   InitializeHelpWindow(HLP^);
   InitializeFleetWindow(FLT^);
   InitializeStatusWindow(STA^);
   InitializeNewsWindow(NWS^);
   InitializeEmpireWindow(EMP^);
   InitializeNamesWindow(NMS^);

   TimeUpdateLoop:=0;
   END;

PROCEDURE UpdateClock;
   VAR
      TimeStr: String32;
      TempX,TempY,CursorX,CursorY,TempCol: Word;

   BEGIN
   IF TimeUpdateLoop=0 THEN
      BEGIN
      TimeUpdateLoop:=100;

      ElapsedTime:=AbsoluteTime-TimeMark;
      IF ElapsedTime<0 THEN
         ElapsedTime:=ElapsedTime+(24*60*60);
      SecondsLeft:=Round(GetTimeLeft(Player)-ElapsedTime);
      Seconds2Str(SecondsLeft,TimeStr);
      TimeStr:=TimeStr+'   ';

      TempX:=WindMin;  TempY:=WindMax;
      CursorX:=WhereX;  CursorY:=WhereY;
      TempCol:=TextAttr;
      Window(1,1,80,25);
      GotoXY(70,1);   TextAttr:=C.SYSMenuBar;
      Write(TimeStr);
      TextAttr:=TempCol;
      WindMin:=TempX;  WindMax:=TempY;
      GotoXY(CursorX,CursorY);
      END
   ELSE
      Dec(TimeUpdateLoop);
   END;  { UpdateClock }

PROCEDURE StopClock(VAR PausedTimeMark: LongInt);
   BEGIN
   PausedTimeMark:=AbsoluteTime;
   END;  { StopClock }

PROCEDURE StartClock(VAR PausedTimeMark: LongInt);
   VAR
      PausedTime: LongInt;

   BEGIN
   PausedTime:=AbsoluteTime-PausedTimeMark;
   IF PausedTime<0 THEN
      PausedTime:=PausedTime+(24*60*60);
   TimeMark:=TimeMark+PausedTime;
   IF TimeMark<0 THEN
      TimeMark:=TimeMark+(24*60*60);
   END;  { StartClock }

PROCEDURE PauseCommand;
   VAR
      PausedTimeMark: LongInt;
      BlankWind: WindowHandle;
      Abort: Boolean;

   BEGIN
   IF PauseActive THEN
      BEGIN
      StopClock(PausedTimeMark);
      OpenWindow(1,2,80,23,NoBRD,'',7,7,BlankWind);
      Abort:=False;
      AttentionWindow('Time has stopped until your return...','Press any key to continue.',Abort);
      CloseWindow;
      StartClock(PausedTimeMark);
      END;
   END;  { PauseCommand }

END.
