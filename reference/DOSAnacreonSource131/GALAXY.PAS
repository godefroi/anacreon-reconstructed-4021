(* GALAXY.PAS ----------------------------------------------------------------

--------------------------------------------------------------------------- *)

UNIT Galaxy;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES
   Types;

CONST
   MaxSizeOfGalaxy = 100;     { in sectors }

TYPE
   Coordinate = 0..MaxSizeOfGalaxy;

   XYCoord = RECORD
      x,y: Coordinate;
   END;  { XYCoord }

   Location = RECORD
      XY: XYCoord;
      ID: IDNumber;
   END;

   { record of each quadrant in the universe }
   SectorRecord = RECORD
      Obj: IDNumber;                   { ID of object in this quad }
      Flts: ScoutSet;                  { set of empires that have a flt here }
		MineScout: ScoutSet;					{ set of empires that know about mine }
      Special: Byte;                   { the low order 4 bits = Nebula }
                                       { the high order 4 bits = mine placed }
   END;
   SectorRowArray = ARRAY [Coordinate] OF SectorRecord;
   SectorRowArrayPtr = ^SectorRowArray;

   SectorColumnSpine = ARRAY [Coordinate] OF SectorRowArrayPtr;

VAR
   Sector: SectorColumnSpine;
   SizeOfGalaxy: Word;

CONST
   Limbo: XYCoord = ( x: 00; y: 00 );
   NoSRMField = Ord(Indep) * 16;

PROCEDURE CleanUpSector;
PROCEDURE InitializeSector(SectorSize: Word);
FUNCTION LoadSector(VAR SF: FILE): Word;
FUNCTION SaveSector(VAR SF: FILE): Word;

PROCEDURE SetMineScout(Emp: Empire; XY: XYCoord);
PROCEDURE ClrMineScout(XY: XYCoord);

IMPLEMENTATION

USES
   Dos2;

PROCEDURE ClrMineScout(XY: XYCoord);
	BEGIN
	Sector[XY.x]^[XY.y].MineScout:=[];
	END;  { ClrMineScout }

PROCEDURE SetMineScout(Emp: Empire; XY: XYCoord);
	BEGIN
	Sector[XY.x]^[XY.y].MineScout:=Sector[XY.x]^[XY.y].MineScout+[Emp];
	END;  { SetMineScout }

FUNCTION SaveSector(VAR SF: FILE): Word;
   VAR
      Error,y: Word;

   BEGIN
   { size of map x,y }
   Error:=WriteVariable(SF,SizeOfGalaxy,2);
   Error:=WriteVariable(SF,SizeOfGalaxy,2);

   FOR y:=0 TO SizeOfGalaxy DO
      Error:=WriteVariable(SF,Sector[y]^,(SizeOf(SectorRecord)*(SizeOfGalaxy+1)));

   SaveSector:=Error;
   END;  { SaveSector }

FUNCTION LoadSector(VAR SF: FILE): Word;
   VAR
      Error,y: Word;

   BEGIN
   { size of map x,y }
   Error:=ReadVariable(SF,SizeOfGalaxy,2);
   Error:=ReadVariable(SF,SizeOfGalaxy,2);

   InitializeSector(SizeOfGalaxy);

   FOR y:=0 TO SizeOfGalaxy DO
      Error:=ReadVariable(SF,Sector[y]^,(SizeOf(SectorRecord)*(SizeOfGalaxy+1)));

   LoadSector:=Error;
   END;  { LoadSector }

PROCEDURE CleanUpSector;
   VAR
      i,RowSize: Word;

   BEGIN
   RowSize:=(SizeOfGalaxy+1)*SizeOf(SectorRecord);
   FOR i:=0 TO SizeOfGalaxy DO
      FreeMem(Sector[i],RowSize);
   END;  { CleanUpSector }

PROCEDURE InitializeSector(SectorSize: Word);
   VAR
      i,j,RowSize: Word;

   BEGIN
   IF SectorSize>0 THEN
      BEGIN
      IF SectorSize>MaxSizeOfGalaxy THEN
         SectorSize:=MaxSizeOfGalaxy;

      RowSize:=(SectorSize+1)*SizeOf(SectorRecord);
      FOR i:=0 TO SectorSize DO
         BEGIN
         GetMem(Sector[i],RowSize);
         FillChar(Sector[i]^,RowSize,0);
         FOR j:=0 TO SectorSize DO
            Sector[i]^[j].Special:=NoSRMField;
         END;
      END;

   SizeOfGalaxy:=SectorSize;
   END;  { InitializeSector }

END.
