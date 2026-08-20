(* DISPLAY.PAS -----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT Display;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Crt,                                       { Standard CRT Library. }
     Strg,                                      { String Library. }
     EIO,                                       { Extended IO Library. }
     WND,
     Menu,
     Types,
     Galaxy,
     Misc,
     PrimIntr,
     SWindows;                                  { Status windows. }

PROCEDURE WriteErrorMessage(Text: LineStr);
PROCEDURE WriteCommLine(Text: LineStr);
PROCEDURE WriteTitleLine(Title: LineStr);
PROCEDURE ClrDisplayScreen;
PROCEDURE GetIDMenuChoice(VAR Menu: MenuStructure; VAR PotentialGround: IDList;
                          VAR ID: IDNumber; VAR Exit: Boolean);
PROCEDURE InputStrgDisplayScreen(Line: LineStr; x,y: Byte;
                                 InpLen: Byte; VAR Strg: LineStr);
PROCEDURE InputIntegerDisplayScreen(Line: LineStr; x,y: Byte;
                                    VAR Int: Integer);
PROCEDURE DrawScreen(Year: Word);

PROCEDURE InputParameter(Prompt: LineStr; VAR Parm: String32);
PROCEDURE InterpretXY(Parm: String32; VAR XY: XYCoord; VAR Error: Boolean);
PROCEDURE InterpretObj(Parm: String32; VAR ID: IDNumber; VAR Error: Boolean);
PROCEDURE WriteMainHelpLine;

IMPLEMENTATION

USES
   Environ;

PROCEDURE WriteMainHelpLine;
   BEGIN
   WriteHelpLine('F1:Help F3:Status F5:Fleets F7:News F8:Empire F9:Names F10:Map');
   END;  { WriteMainHelpLine }

PROCEDURE GetIDMenuChoice(VAR Menu: MenuStructure; VAR PotentialGround: IDList;
                          VAR ID: IDNumber; VAR Exit: Boolean);
   VAR
      Choice: Char;

   BEGIN
   DisplayMenu(Menu,5,12,LightGray,C.SYSDispSelect,45,7);
   WriteHelpLine(',:Move cursor <Enter>:Select <Esc>:Exit');

   REPEAT
      GetCharacter(AnyKey,NoCaseDistinct,Choice);
      ActivateMenu(Menu,Choice);
   UNTIL Choice IN [ESCKey,ReturnKey];

   IF Choice=ReturnKey THEN
      BEGIN
      ID:=PotentialGround[GetMenuSelect(Menu)];
      Exit:=False;
      END
   ELSE
      Exit:=True;

   CleanUpMenu(Menu);
   END;  { GetIDMenuChoice }

PROCEDURE WriteErrorMessage(Text: LineStr);
{ WriteErrorMessage:
   This routine will write an error message on the last line of the
   Command Window.  The whole line is cleared before writing. }

   BEGIN
   ActivateWindow(CommandWindow);
   WriteBlanks(80,1,2,C.CommWind);
   WriteString(Text,1,2,C.CommWind);
   END;  { WriteErrorMessage }

PROCEDURE WriteCommLine(Text: LineStr);
   BEGIN
   ActivateWindow(CommandWindow);
   WriteBlanks(80,1,1,C.CommWind);
   WriteString(Text,1,1,C.CommWind);
   END;  { WriteCommLine }

PROCEDURE WriteTitleLine(Title: LineStr);
{ WriteTitleLine:
   This procedure writes a title line on the first line of the screen. }

   VAR
      TempX,TempY: Word;
      TempCol: Byte;

   BEGIN
   TempX:=WindMin;  TempY:=WindMax;
   Window(1,1,80,25);

   WriteBlanks(80,1,1,C.TitleLine);
   WriteString(Title,1,1,C.TitleLine);

   WindMin:=TempX;  WindMax:=TempY;
   END;  { WriteTitleLine }

PROCEDURE ClrDisplayScreen;
   BEGIN
   ActivateWindow(DisplayWindow);
   ClrScr;
   END;  { ClrDisplayScreen }

PROCEDURE InputStrgDisplayScreen(Line: LineStr; x,y: Byte;
                                 InpLen: Byte; VAR Strg: LineStr);
   BEGIN
   ActivateWindow(DisplayWindow);
   WriteString(Line,x,y,C.SYSDispWind);
   GetInputString(Length(Line)+x,y,Strg,InpLen);
   ActivateWindow(DisplayWindow);
   END;  { InputStrgDisplayScreen }

PROCEDURE InputIntegerDisplayScreen(Line: LineStr; x,y: Byte;
                                    VAR Int: Integer);
   VAR
      Number: String8;
      Error: Integer;

   BEGIN
   Number:='';
   InputStrgDisplayScreen(Line,x,y,6,Number);
   IF (Number='') OR (Number=ESCKey) THEN
      Int:=0
   ELSE
      BEGIN
      Val(Number,Int,Error);
      IF Error<>0 THEN
         Int:=-MaxInt;
      END;
   END;  { InputIntegerDisplayScreen }

PROCEDURE DrawScreen(Year: Word);
{ DrawScreen:
   This procedure sets up the screen display that will be used in the
   rest of the command mode.  Windows }
   VAR
      YearStr,FoundStr: String8;
      Line: LineStr;
      Age: Word;

   { DrawScreen: MAIN PROCEDURE }
   BEGIN
   Str(Year,YearStr);
   Age:=EmpireAge(Player)+1;
   Str(Age,FoundStr);
   Line:='Anacreon: '+YearStr+' ('+FoundStr+OrdinalString(Age)+' year of your reign.)';
   OpenWindow(1,2,80,2,NoBRD,'',C.CommWind,C.SYSWBorder,CommandWindow);
   OpenWindow(1,4,80,21,ThinBRD,Line,C.SYSDispWind,C.SYSWBorder,DisplayWindow);
   END;  { DrawScreen }

PROCEDURE InputParameter(Prompt: LineStr; VAR Parm: String32);
   BEGIN
   Parm:='';
   WriteCommLine(Prompt);
   GetInputString(Length(Prompt)+1,1,Parm,16);
   IF Parm=EscKey THEN
      Parm:='';
   END;  { InputLocation }

PROCEDURE InterpretXY(Parm: String32; VAR XY: XYCoord; VAR Error: Boolean);
   VAR
      Loc: Location;

   BEGIN
   Error:=False;
   XY:=Limbo;
   GetLocation(Player,Parm,Loc);
   IF NOT SameID(Loc.ID,EmptyQuadrant) THEN
      GetCoord(Loc.ID,XY)
   ELSE IF NOT SameXY(Loc.XY,Limbo) THEN
      XY:=Loc.XY
   ELSE
      BEGIN
      WriteErrorMessage('"'+Parm+'" are undefined coordinates, '+MyLord(Player)+'.');
      Error:=True;
      END;
   END;  { InterpretXY }

PROCEDURE InterpretObj(Parm: String32; VAR ID: IDNumber; VAR Error: Boolean);
   VAR
      Loc: Location;

   BEGIN
   ID:=EmptyQuadrant;
   GetLocation(Player,Parm,Loc);
   Parm[1]:=UpCase(Parm[1]);
   IF SameID(Loc.ID,EmptyQuadrant) THEN
      BEGIN
      Error:=True;
      WriteErrorMessage('There is no object at that location, '+MyLord(Player)+'.');
      END
   ELSE IF ((Loc.ID.ObjTyp=Base) AND (NOT (Loc.ID.Index IN SetOfActiveStarbases)))
           OR ((Loc.ID.ObjTyp=Gate) AND (NOT (Loc.ID.Index IN SetOfActiveGates))) THEN
      BEGIN
      WriteErrorMessage(Parm+' has been destroyed.');
      Error:=True;
      END
   ELSE
      BEGIN
      ID:=Loc.ID;
      Error:=False;
      END;
   END;  { InterpretObject }

END.
