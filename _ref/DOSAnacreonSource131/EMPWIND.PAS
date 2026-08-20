(* EMPWIND.PAS -----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT EmpWind;

{$IFDEF Overlay}
{$F+,O+}
{$ENDIF}

INTERFACE

USES
   Strg,
   Int,
   CRT,
   EIO,
   WND,
   Types,
   Galaxy,
   News,
   Intrface,
   PrimIntr,
   WndTypes;

TYPE
   EmpireStructure = RECORD
      Handle: WindowHandle;
      Col: Word;
   END;

VAR
   EMP: ^EmpireStructure;

PROCEDURE InitializeEmpireWindow(VAR EMP: EmpireStructure);
PROCEDURE EmpireWindow(VAR EMP: EmpireStructure; Comm: Word);

IMPLEMENTATION

USES
   Environ;

CONST
   InitX = 1;
   InitY = 4;
   InitWidth = 80;
   InitHeight = 10;
   InitTitle = 'Empire       Tl Pln SInd   Pop    fgt    hkr    jmp    jtn    pen    str    trn ';

PROCEDURE DrawEmpireWindow(VAR EMP: EmpireStructure);
   VAR
      EmpI: Empire;
      Line: LineStr;
      CapID: IDNumber;
      i: Integer;

   BEGIN
   WITH EMP DO
      BEGIN
      ClrScr;
      GetEmpireStatusLine(Player,True,Line);
      WriteString(Line,1,1,Col);
      i:=2;

      FOR EmpI:=Empire1 TO Empire8 DO
         IF EmpireActive(EmpI) THEN
            BEGIN
            GetCapital(EmpI,CapID);
            IF Known(Player,CapID) AND (EmpI<>Player) THEN
               BEGIN
               IF Scouted(Player,CapID) THEN
                  GetEmpireStatusLine(EmpI,True,Line)
               ELSE
                  GetEmpireStatusLine(EmpI,False,Line);
               WriteString(Line,1,i,TextAttr);
               Inc(i);
               END;
            END;
      END;
   END;  { DrawEmpireWindow }

PROCEDURE CloseEmpireWindow(VAR EMP: EmpireStructure);
   BEGIN
   WITH EMP DO
      BEGIN
      IF Handle<>0 THEN
         BEGIN
         ActivateWindow(Handle);
         CloseWindow;
         Handle:=0;
         END;
      END;
   END;  { CloseEmpireWindow }

PROCEDURE OpenEmpireWindow(VAR EMP: EmpireStructure);
   BEGIN
   WITH EMP DO
      BEGIN
      IF Handle=0 THEN
         OpenWindow(InitX,InitY,InitWidth,InitHeight,TextBRD,InitTitle,Col,C.SYSTBorder,Handle)
      ELSE
         ActivateWindow(Handle);

      DrawEmpireWindow(EMP);
      END;
   END;  { OpenEmpireWindow }

PROCEDURE InitializeEmpireWindow(VAR EMP: EmpireStructure);
   BEGIN
   WITH EMP DO
      BEGIN
      Handle:=0;
      Col:=C.StatWind3;
      END;
   END;  { InitializeEmpireWindow }

PROCEDURE EmpireWindow(VAR EMP: EmpireStructure; Comm: Word);
   BEGIN
   CASE Comm OF
              CloseWCM: CloseEmpireWindow(EMP);
               OpenWCM: OpenEmpireWindow(EMP);
      END;  { case }
   END;  { EmpireWindow }

BEGIN
New(EMP);
END.
