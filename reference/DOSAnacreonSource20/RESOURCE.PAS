(* RESOURCE.PAS --------------------------------------------------------------

	Resources Unit

	This unit deals with procedures and functions that affect resources.

--------------------------------------------------------------------------- *)

unit Resource;

interface

uses
	Types;

function CargoSpaceAvail (var Resources: ResourceArray): longint;
procedure SubtractCasualties (var Resources, Casualties: ResourceArray;
                              DestroyCargoInTrans: boolean;
										var NoShipsLeft: boolean);

implementation

const
	CargoSize: array [CargoTypes] of word =
     { men nnj amb che met sup tri }
      (  2,  2,  1, 10, 30, 50,  1 );

	TransCapacity: array [ShipTypes] of word =
     { fgt hkr jmp  jtn pen ssp trn }
      (  0,  0,  0,  20,  0,  0,100 );

procedure BalanceResources (var Resources: ResourceArray);
   const
      CargoPriority: array [1..7] of CargoTypes =
         ( che, sup, met, men, nnj, tri, amb );

   var
      ThgI: ResourceTypes;
      SpaceLeft,NewSpaceLeft: longint;
      CargoToRemove: Word;

	begin
   CargoToRemove:=1;
   ThgI:=CargoPriority[CargoToRemove];
   SpaceLeft:=CargoSpaceAvail(Resources);
   while SpaceLeft<0 do
      begin
      Resources[ThgI]:=0;
      NewSpaceLeft:=CargoSpaceAvail(Resources);
      if NewSpaceLeft<0 then
         begin
         SpaceLeft:=NewSpaceLeft;
         Inc(CargoToRemove);
         ThgI:=CargoPriority[CargoToRemove];
         end
      else
         begin
         Resources[ThgI]:=NewSpaceLeft DIV CargoSize[ThgI];
         SpaceLeft:=0;
         end;
      end;  { while }
	end;  { BalanceResources }

function CargoSpaceAvail (var Resources: ResourceArray): longint;
{ ----------------------------------------------------------------------------
	Returns the amount of cargo space left.
---------------------------------------------------------------------------- }
	var
		FreeSpace: longint;
		i: ResourceTypes;

	begin
	FreeSpace := longint(Resources[trn])*TransCapacity[trn]
	             +longint(Resources[jtn])*TransCapacity[jtn];

	for i:=men to tri do
		Dec(FreeSpace, longint(Resources[i])*CargoSize[i]);

	CargoSpaceAvail := FreeSpace;
	end;  { CargoSpaceAvail }

procedure SubtractCasualties (var Resources, Casualties: ResourceArray;
                              DestroyCargoInTrans: boolean;
										var NoShipsLeft: boolean);
{ ----------------------------------------------------------------------------
	This procedure will subtract the resources in Casualties from the
	given resources. If DestroyCargoInTrans is true, then cargo is destroyed
	as transports are destroyed.

	ASSUMPTIONS:
		Casualties is never more than Resources.
---------------------------------------------------------------------------- }
	var
		i: ResourceTypes;

	begin
	NoShipsLeft:=True;
	for i:=FirstResource to LastResource do
		begin
		Dec(Resources[i],Casualties[i]);
		if (i in [fgt..trn]) and (Resources[i]>0) then
			NoShipsLeft:=False;
		end;

	if DestroyCargoInTrans then
		BalanceResources(Resources);
	end;  { SubtractCasualties }

end.

