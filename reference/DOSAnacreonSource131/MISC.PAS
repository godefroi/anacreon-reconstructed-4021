(* MISC.PAS --------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT Misc;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Strg,
     Int,                                       { Integer routines. }
     Real1,                                     { Real number routines. }
     Types,
     Galaxy,
     DataCnst;                                  { Universal constants. }

FUNCTION ThgLmt(X: Real): Resources;
FUNCTION InGalaxy(x,y: Integer): Boolean;
FUNCTION Distance(First,Second: XYCoord): Integer;
FUNCTION SameXY(Coord1,Coord2: XYCoord): Boolean;
FUNCTION SameID(ID1,ID2: IDNumber): Boolean;
FUNCTION SameLocation(C1,C2: Location): Boolean;
FUNCTION NoShips(Sh: ShipArray): Boolean;
FUNCTION FuelCapacity(Sh: ShipArray): Real;
FUNCTION FuelConsumption(Sh: ShipArray; Cr: CargoArray): Real;
FUNCTION FleetCargoSpace(Sh: ShipArray; Cr: CargoArray): Integer;
FUNCTION ShipYardInd(var Ind: IndusArray): IndusTypes;
FUNCTION TotalProd(Pop: Population;
                   Tech: TechLevel): IndusIndex;
PROCEDURE AddThings(var Sh: ShipArray; var Cr: CargoArray;
                    Sh2: ShipArray; Cr2: CargoArray);
PROCEDURE SubThings(var Sh: ShipArray; var Cr: CargoArray;
                    Sh2: ShipArray; Cr2: CargoArray);
PROCEDURE MoveThings(NoOfThg: Integer; var Source,Dest: Resources);
FUNCTION YesNo(Ind: Integer): String16;
FUNCTION HiLo(Ind: Integer): String16;
FUNCTION MilitaryPower(VAR Ship: ShipArray; VAR Defns: DefnsArray): LongInt;

IMPLEMENTATION

FUNCTION MilitaryPower(VAR Ship: ShipArray; VAR Defns: DefnsArray): LongInt;
{ MilitaryPower: ---------------------------------------------------------------
   Returns a value that represents the military power of a force of ships and
   defenses. See MPower.
   TESTED: 8/2/88
------------------------------------------------------------------------------ }
   VAR
      ResI: AttackTypes;
      Temp: LongInt;

   BEGIN
   Temp:=0;
   FOR ResI:=LAM TO ion DO
      Temp:=Temp+LongInt(Defns[ResI])*MPower[ResI];

   FOR ResI:=fgt TO trn DO
      Temp:=Temp+LongInt(Ship[ResI])*MPower[ResI];

   MilitaryPower:=Temp;
   END;  { MilitaryPower }

FUNCTION HiLo(Ind: Integer): String16;
   BEGIN
   CASE Ind OF
        0..10: HiLo:='no ';
       11..25: HiLo:='Lo-';
       26..50: HiLo:='Lo+';
       51..75: HiLo:='Hi-';
      76..100: HiLo:='Hi+';
   ELSE
      HiLo:='---';
      END;  { case }
   END;  { HiLo }

FUNCTION YesNo(Ind: Integer): String16;
   BEGIN
   CASE Ind OF
               0: YesNo:='   no';
          1..500: YesNo:=' yes-';
       501..1500: YesNo:=' yes1';
      1501..2500: YesNo:=' yes2';
      2501..3500: YesNo:=' yes3';
      3501..4500: YesNo:=' yes4';
      4501..5500: YesNo:=' yes5';
      5501..6500: YesNo:=' yes6';
      6501..7500: YesNo:=' yes7';
      7501..8500: YesNo:=' yes8';
      8501..9500: YesNo:=' yes9';
      9501..9999: YesNo:=' yes+';
   ELSE
      YesNo:=' ----';
   END;  { case }
   END;  { YesNo }

FUNCTION ThgLmt(X: Real): Resources;
{ ThgLmt:
   This function makes sure that no value for thing goes above MaxResources. }

   BEGIN
   IF X>MaxResources THEN
      ThgLmt:=MaxResources
   ELSE IF X<0 THEN
      ThgLmt:=0
   ELSE
      ThgLmt:=Trunc(X);
   END;  { ThgLmt }

FUNCTION InGalaxy(x,y: Integer): Boolean;
   BEGIN
   IF (x>0) AND (y>0) AND (x<=SizeOfGalaxy) AND (y<=SizeOfGalaxy) THEN
      InGalaxy:=True
   ELSE
      InGalaxy:=False;
   END;  { InGalaxy }

FUNCTION Distance(First,Second: XYCoord): Integer;
   BEGIN
   Distance:=GreaterInt(Abs(First.X-Second.X),Abs(First.Y-Second.Y));
   END;

FUNCTION SameXY(Coord1,Coord2: XYCoord): Boolean;
   BEGIN
   IF Integer(Coord1)=Integer(Coord2) THEN
      SameXY:=True
   ELSE
      SameXY:=False;
   END;  { SameXY }

FUNCTION SameID(ID1,ID2: IDNumber): Boolean;
  BEGIN
  IF Integer(ID1)=Integer(ID2) THEN
     SameID:=True
  ELSE
     SameID:=False;
  END;  { SameID }

FUNCTION SameLocation(C1,C2: Location): Boolean;
   BEGIN
   IF LongInt(C1)=LongInt(C2) THEN
      SameLocation:=True
   ELSE
      SameLocation:=False;
   END;  { SameLocation }

{ FLEET ROUTINES }

FUNCTION NoShips(Sh: ShipArray): Boolean;
{ NoShips:
   This function returns true if there are no ships in 'Sh' }

   VAR
      ShI: ShipTypes;

   BEGIN
   FOR ShI:=fgt TO trn DO
      IF Sh[ShI]<>0 THEN
         BEGIN
         NoShips:=False;
         Exit;
         END;

   NoShips:=True;
   END;  { NoShips }

FUNCTION FuelCapacity(Sh: ShipArray): Real;
{ FuelCapacity:
   Returns maximum fuel to be loaded on the given ship distribution. }

   VAR
      ShpI: ShipTypes;
      temp1: Real;

   { FuelCapacity: MAIN PROCEDURE }
   BEGIN
   temp1:=1;
   FOR ShpI:=fgt TO trn DO
      temp1:=temp1+(FuelCap[ShpI]/100)*Sh[ShpI];

   FuelCapacity:=temp1;
   END;  { FuelCapacity }

FUNCTION FuelConsumption(Sh: ShipArray; Cr: CargoArray): Real;
{ FuelConsumption:
   Returns the number of units of fuel that the fleet uses per year. }

   VAR
      ThgI: ResourceTypes;
      temp1: real;

   BEGIN
   temp1:=1;
   FOR ThgI:=fgt TO trn DO
      temp1:=temp1+(FuelCons[ThgI]/1000)*Sh[ThgI];
   FOR ThgI:=men TO tri DO
      temp1:=temp1+(FuelCons[ThgI]/1000)*Cr[ThgI];
   FuelConsumption:=temp1;
   END;  { FuelConsumption }

FUNCTION FleetCargoSpace(Sh: ShipArray; Cr: CargoArray): Integer;
{ CargoSpace: 
   This routine returns the amount of free cargo space left in the fleet
   in units of transports.  (NOTE: A fleet need not be balanced.  This
   procedure will return a negative number if necessary. }

   var
      FreeSpace,SpaceUsed: Real;
      ThgI: ResourceTypes;

   begin
   FreeSpace:=Sh[trn] + Sh[jtn]*TrnAdj[jtn];

   for ThgI:=men to tri do
      begin
      SpaceUsed:=Cr[ThgI]/CargoSpace[ThgI];
      FreeSpace:=FreeSpace-SpaceUsed;
      end;

   FleetCargoSpace:=Round(FreeSpace);
   end;  { CargoSpace }

{ INDUSTRY ROUTINES }

FUNCTION ShipYardInd{var Ind: IndusArray): IndusTypes};
{ ShipYardInd:
   Given an industrial array, this function will return the type of
   ship industry that is on this planet.  e.g. SYAll,SYJump.. etc.  }

   var
      IndI,temp: IndusTypes;
      GreatestInd: IndusIndex;

   begin
   temp:=SYGInd;
   GreatestInd:=Ind[SYGInd];
   for IndI:=SYGInd to SYTInd do
      if Ind[IndI]>GreatestInd then
         begin
         temp:=IndI;
         GreatestInd:=Ind[IndI];
         end;
   ShipYardInd:=temp;
   end;  { ShipYardInd }

FUNCTION TotalProd(Pop: Population;
                   Tech: TechLevel): IndusIndex;
{ TotalProd:
   Returns the Total Industrial Production of a planet (TIP) given
   Population, and Tech level. }

   VAR
      temp1: Real;

   BEGIN
   IF Pop<=0 THEN
      Pop:=1;
   temp1:=K1*Expnt( (Pop+K2),K3 )*TechAdj[Tech]/100;
   IF temp1>999 THEN
      temp1:=999
   ELSE IF temp1<0 THEN
      temp1:=0;
   TotalProd:=Round(temp1);
   END;  { TotalProd }

{ SHIPS AND CARGO ROUTINES }

PROCEDURE AddThings(VAR Sh: ShipArray; VAR Cr: CargoArray;
                    Sh2: ShipArray; Cr2: CargoArray);
{ AddThings:
   Given Sh, Cr, Sh2 and Cr2, this procedure adds the elements of
   Sh2 and Cr2 to Sh and Cr respectively.  Sh2 and Cr2 are unchanged,
   but Sh and Cr are altered. }

   VAR
      a: ResourceTypes;

   BEGIN
   FOR a:=fgt TO trn DO
      Sh[a]:=ThgLmt(Sh[a]+Sh2[a]);

   FOR a:=men TO tri DO
      Cr[a]:=ThgLmt(Cr[a]+Cr2[a]);
   END;  { AddThings }

PROCEDURE SubThings(VAR Sh: ShipArray; VAR Cr: CargoArray;
                    Sh2: ShipArray; Cr2: CargoArray);
{ SubThings:
   Given Sh, Cr, Sh2 and Cr2, this procedure subtracts the elements of
   Sh2 and Cr2 from Sh and Cr respectively.  Sh2 and Cr2 are unchanged,
   but Sh and Cr are altered. }

   var
      a: ResourceTypes;

   begin
   for a:=fgt TO trn do
      Sh[a]:=ThgLmt(Sh[a]-Sh2[a]);

   for a:=men TO tri do
      Cr[a]:=ThgLmt(Cr[a]-Cr2[a]);
   end;  { SubThings }

procedure MoveThings{NoOfThg: Integer; var Source,Dest: Resources};
{ MoveThings:
   This procedure moves 'NoOfThg' things from Source to Dest.
   if there are not enough at Source, then it moves only as many as there
   are.  }

   begin
   if Source>NoOfThg then
      begin
      Source:=ThgLmt(Source-NoOfThg);
      Dest:=ThgLmt(Dest+NoOfThg);
      end
   else
      begin
      Dest:=ThgLmt(Dest+Source);
      Source:=0;
      end;
   end;  { MoveThings }

END.
