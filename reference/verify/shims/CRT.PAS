(* CRT.PAS -- fullbuild lane's own stand-in for Turbo Pascal's CRT unit.

   Not a patch against any pristine file -- there is no CRT.PAS in
   reference/DOSAnacreonSource131; the real Crt unit is Borland's, and this
   lane doesn't want the real screen-manipulation behavior anyway (see chat
   history: the goal is no UI, not a faithfully reproduced one). This unit
   gives every Crt symbol the pristine source actually calls a harmless
   no-op/plain-variable definition instead, so units that only touch the
   screen through Crt (GotoXY/TextColor/Window/etc, as opposed to raw
   Mem[]/Ptr()/Intr() access) compile and run without any terminal output.

   Grown on demand: add a symbol here the moment fpc reports it missing while
   compiling a real unit, not speculatively ahead of time. *)

UNIT Crt;

INTERFACE

CONST
   BlackBG = 0;
   Black = 0; Blue = 1; Green = 2; Cyan = 3; Red = 4; Magenta = 5;
   Brown = 6; LightGray = 7; DarkGray = 8; LightBlue = 9; LightGreen = 10;
   LightCyan = 11; LightRed = 12; LightMagenta = 13; Yellow = 14; White = 15;
   Blink = 128;

   BW40 = 0; CO40 = 1; BW80 = 2; CO80 = 3; Mono = 7; Font8x8 = 256;

VAR
   CheckBreak: Boolean;
   CheckEOF: Boolean;
   CheckSnow: Boolean;
   DirectVideo: Boolean;
   LastMode: Word;
   TextAttr: Byte;
   WindMin: Word;
   WindMax: Word;

PROCEDURE ClrEol;
PROCEDURE ClrScr;
PROCEDURE Delay(MS: Word);
PROCEDURE GotoXY(X, Y: Byte);
FUNCTION KeyPressed: Boolean;
PROCEDURE HighVideo;
PROCEDURE LowVideo;
PROCEDURE NormVideo;
PROCEDURE NoSound;
FUNCTION ReadKey: Char;
PROCEDURE Sound(Hz: Word);
PROCEDURE TextBackground(Col: Byte);
PROCEDURE TextColor(Col: Byte);
PROCEDURE TextMode(Mode: Integer);
FUNCTION WhereX: Byte;
FUNCTION WhereY: Byte;
PROCEDURE Window(X1, Y1, X2, Y2: Byte);

IMPLEMENTATION

PROCEDURE ClrEol; BEGIN END;
PROCEDURE ClrScr; BEGIN END;
PROCEDURE Delay(MS: Word); BEGIN END;
PROCEDURE GotoXY(X, Y: Byte); BEGIN END;
FUNCTION KeyPressed: Boolean; BEGIN KeyPressed:=False; END;
PROCEDURE HighVideo; BEGIN END;
PROCEDURE LowVideo; BEGIN END;
PROCEDURE NormVideo; BEGIN END;
PROCEDURE NoSound; BEGIN END;
FUNCTION ReadKey: Char; BEGIN ReadKey:=#0; END;
PROCEDURE Sound(Hz: Word); BEGIN END;
PROCEDURE TextBackground(Col: Byte); BEGIN END;
PROCEDURE TextColor(Col: Byte); BEGIN END;
PROCEDURE TextMode(Mode: Integer); BEGIN LastMode:=Mode; END;
FUNCTION WhereX: Byte; BEGIN WhereX:=1; END;
FUNCTION WhereY: Byte; BEGIN WhereY:=1; END;
PROCEDURE Window(X1, Y1, X2, Y2: Byte); BEGIN END;

BEGIN
CheckBreak:=False;
CheckEOF:=False;
CheckSnow:=False;
DirectVideo:=False;
LastMode:=CO80;
TextAttr:=LightGray;
WindMin:=0;
WindMax:=$184F;
END.
