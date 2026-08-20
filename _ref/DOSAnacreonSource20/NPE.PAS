(* NPE.PAS ---------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT NPE;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Types,
     NPETypes;

PROCEDURE CleanUpNPE(Emp: Empire);
PROCEDURE InitializeNPE(Emp: Empire; ETyp: NPEmpireTypes);
PROCEDURE ImplementNPE(Emp: Empire);
PROCEDURE LoadNPE(VAR GameFile: FILE; Emp: Empire; Version: Word; VAR Error: Word);
PROCEDURE SaveNPE(VAR GameFile: FILE; Emp: Empire; VAR Error: Word);

IMPLEMENTATION

USES
   CRT,
   EIO,
   Wnd,
   PrimIntr,
   NPE01,		{ pirate empire }
   NPE02,		{ kingdom empire }
	NPE03,		{ guardian empire }
	NPE04;		{ berserker empire }

PROCEDURE CleanUpNPE(Emp: Empire);
   BEGIN
   WITH NPEData[Emp] DO
      CASE Typ OF
         PirateNPE: CleanUpPirateNPE(Data);
         Kingdom1NPE..Kingdom2NPE: CleanUpKingdomNPE(Data);
			BerserkerNPE: CleanUpBerserkerNPE(Data);
			GuardianNPE: CleanUpGuardianNPE(Data);
		ELSE
			CleanUpPirateNPE(Data);
      END;  { case }
   END;  { CleanUpNPE }

PROCEDURE InitializeNPE(Emp: Empire; ETyp: NPEmpireTypes);
   BEGIN
   WITH NPEData[Emp] DO
      BEGIN
      Typ:=ETyp;
      CASE ETyp OF
         PirateNPE: InitializePirateNPE(Emp,Data);
         Kingdom1NPE: InitializeKingdom1NPE(Emp,Data);
         Kingdom2NPE: InitializeKingdom2NPE(Emp,Data);
			BerserkerNPE: InitializeBerserkerNPE(Emp,Data);
			GuardianNPE: InitializeGuardianNPE(Emp,Data);
		ELSE
         InitializePirateNPE(Emp,Data);
      END;  { case }
      END;
   END;  { InitializeNPE }

PROCEDURE ImplementNPE(Emp: Empire);
   VAR
      Wind: WindowHandle;
		NumbStr: STRING [8];

   BEGIN
   TextAttr:=7;
   ClrScr;
   OpenWindow(20,7,40,7,ThinBRD,'',7,C.SYSWBorder,Wind);
	Str(Ord(Emp)+1,NumbStr);
   WriteString('Updating Empire '+NumbStr+'...',2,1,7);
   WITH NPEData[Emp] DO
      CASE Typ OF
         PirateNPE: ImplementPirateNPE(Emp,Data);
         Kingdom1NPE..Kingdom2NPE: ImplementKingdom1NPE(Emp,Data);
			BerserkerNPE: ImplementBerserkerNPE(Emp, Data);
			GuardianNPE: ImplementGuardianNPE(Emp, Data);
		ELSE
         ImplementPirateNPE(Emp,Data);
      END;  { case }
   CloseWindow;
   END;  { ImplementNPE }

PROCEDURE LoadNPE(VAR GameFile: FILE; Emp: Empire; Version: Word; VAR Error: Word);
   BEGIN
   WITH NPEData[Emp] DO
      CASE Typ OF
         PirateNPE: LoadPirateNPE(GameFile,Emp,Data,Version,Error);
         Kingdom1NPE..Kingdom2NPE: LoadKingdom1NPE(GameFile,Emp,Data,Version,Error);
			BerserkerNPE: LoadBerserkerNPE (GameFile, Emp, Data, Version, Error);
			GuardianNPE: LoadGuardianNPE (GameFile, Emp, Data, Version, Error);
		ELSE
         LoadPirateNPE(GameFile,Emp,Data,Version,Error);
      END;  { case }
   END;  { LoadNPE }

PROCEDURE SaveNPE(VAR GameFile: FILE; Emp: Empire; VAR Error: Word);
   BEGIN
   WITH NPEData[Emp] DO
      CASE Typ OF
         PirateNPE: SavePirateNPE(GameFile,Data,Error);
         Kingdom1NPE..Kingdom2NPE: SaveKingdom1NPE(GameFile,Data,Error);
			BerserkerNPE: SaveBerserkerNPE (GameFile, Data, Error);
			GuardianNPE: SaveGuardianNPE (GameFile, Data, Error);
		ELSE
         SavePirateNPE(GameFile,Data,Error);
      END;  { case }
   END;  { SaveNPE }

END.
