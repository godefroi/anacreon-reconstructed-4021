PROGRAM Test;

USES
   LoadSave;

VAR
	Error: Word;

BEGIN
Error:=LoadGame('C:\GAMES\ANACREON\SAVE\ANACREON.BAK');
CleanUpUniverse;
END.
