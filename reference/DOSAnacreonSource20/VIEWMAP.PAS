PROGRAM ViewMap;

USES
   Strg,
   Types,
   CRT,
   DOS2,
   EIO,
   MapWind,
   WndTypes,
   LoadSave;

VAR
   Error: Word;
   MAP: MapStructurePtr;
   Ch: Char;
   Filename: LineStr;
   Abort: Boolean;

BEGIN
REPEAT
   ClrScr;
   SelectFile('C:\GAMES\ANACREON\SAVE','*.*',Filename,Abort);

   IF NOT Abort THEN
      BEGIN
      LoadGame(Filename,Error);

      New(MAP);
      InitScanWindowMap(MAP^,Player);
      ScanWindow(MAP^,OpenWCM);

      REPEAT
         GetChoice(AnyKey,IgnoreCase,Ch);
         CASE Ch OF
            LArrKey: ScanWindow(MAP^,CursorLeftWCM);
            RArrKey: ScanWindow(MAP^,CursorRightWCM);
            UArrKey: ScanWindow(MAP^,CursorUpWCM);
            DArrKey: ScanWindow(MAP^,CursorDownWCM);
                '2': ScanWindow(MAP^,PageDownWCM);
                '8': ScanWindow(MAP^,PageUpWCM);
                '6': ScanWindow(MAP^,PageRightWCM);
                '4': ScanWindow(MAP^,PageLeftWCM);
            HomeKey: ScanWindow(MAP^,CenterWCM);
            END;
      UNTIL Ch=EscKey;

      CleanUpUniverse;
      END;
UNTIL Abort;
END.

