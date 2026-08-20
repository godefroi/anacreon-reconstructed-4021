(* INTRFACE.PAS ----------------------------------------------------------------

   These procedures serve as an interface between the Data Base and the
   rest of the program.  All access to the Data Base should be done
   through these procedures.

----------------------------------------------------------------------------- *)

UNIT Intrface;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Strg,                                      { String primitives. }
     Int,                                       { Integer routines. }
     EIO,                                       { Extended IO routines. }
     Types,
     Galaxy,
     DataStrc,                                  { Universe data structure. }
     DataCnst,
     Misc,                                      { Miscellaneous procedures. }
     News,                                      { News module. }
     Mess;

PROCEDURE Scout(Emp: Empire; XY: XYCoord);
PROCEDURE ScoutFleets(PlayerEmp: Empire);
PROCEDURE ScoutObjects(Emp: Empire);
PROCEDURE ClearScoutSet(Emp: Empire);
PROCEDURE ClearKnownSet(Emp: Empire);
PROCEDURE DesignateWorld(World: IDNumber; NewType: WorldTypes);
PROCEDURE TerraformWorld(World: IDNumber; NewClass: WorldClass);
PROCEDURE GetIndustrialDistribution(Obj: IDNumber;
                                    VAR IndDist: IndusDistArray);
PROCEDURE CreatePlanet(ID: IDNumber; NewXY: XYCoord);
PROCEDURE CreateStarbase(ID: IDNumber; NewEmp: Empire; NewXY: XYCoord; NewTyp: StarbaseTypes);
FUNCTION NextStarbaseSlot: Word;

{ FLEET Procedures }
FUNCTION EstimatedDateOfArrival(ID: IDNumber): Integer;
FUNCTION EstimatedRange(ID: IDNumber): Integer;
PROCEDURE BalanceFleet(var Sh: ShipArray; var Cr: CargoArray);
FUNCTION PassingThroughFortress(FltXY: XYCoord): Boolean;
FUNCTION PassingThroughGate(FltID: IDNumber; FltXY,DestXY: XYCoord): Boolean;

{ GATE Procedures }
PROCEDURE CreateStargate(ID: IDNumber; NewEmp: Empire; GateType: StargateTypes; Pos: XYCoord);
PROCEDURE DestroyStargate(GteID: IDNumber);
FUNCTION NextStargateSlot: Word;
FUNCTION CanUseGate(Emp: Empire; GateID: IDNumber): Boolean;

{ CONSTRUCTION Procedures }
PROCEDURE Construction(Empr: Empire; ConsType: ConstrTypes; Loc: XYCoord;
                       VAR ConID: IDNumber);
PROCEDURE DestroyConstruction(ConID: IDNumber);

{ STATUS WINDOW Procedures }
PROCEDURE GetWorldStatus(Emp: Empire; WorldID: IDNumber; VAR Line: LineStr);
PROCEDURE GetEmpireStatusLine(Emp: Empire; Full: Boolean; VAR Line: LineStr);
PROCEDURE GetMilitaryStatus(Emp: Empire; WorldID: IDNumber; var Line: LineStr);
PROCEDURE GetFleetPositionStatus(Emp: Empire; ID: IDNumber;
                                         var Line: LineStr);
PROCEDURE GetFleetStatusLine(Emp: Empire; ID: IDNumber;
                                 var Line: LineStr);
PROCEDURE GetNewsLine(Player: Empire; Item: NewsRecordPtr;
                      VAR Line: LineStr);

{ MISCELLANEOUS Procedures }
PROCEDURE DestroyEmpire(Emp: Empire);
PROCEDURE ProbeScout(Emp: Empire; XY: XYCoord);
PROCEDURE UpdateProbes(Emp: Empire);
PROCEDURE ProbesReturn(Emp: Empire);
PROCEDURE GetEmpireStatus(Emp: Empire;
                          VAR Planets: Word;
                          VAR TotalPop: LongInt;
                          VAR SInd: Word;
                          VAR TotalShips: TotalShipArray);
PROCEDURE GetNearestWorlds(XY: XYCoord; N: Integer; SetToConsider: PlanetSet;
                           VAR NearWorld: IDPtr);
PROCEDURE GetOptimumIndus(Obj: IDNumber; VAR Indus: IndusArray);

IMPLEMENTATION

USES
   PrimIntr,
   Environ,
   Fleet,
   Orders,
   NPE;

PROCEDURE Scout(Emp: Empire; XY: XYCoord);
{ Scout:
   Emp scouts the area around Loc. }
   VAR
      Dir: Directions;
      x,y: Integer;
      TempLoc: XYCoord;
      Obj: IDNumber;
      Loc: Location;
      OtherEmp: Empire;
      NamePtr: NameRecordPtr;
      NameToAdd: String16;

   { Scout: MAIN PROCEDURE }
   BEGIN
   IF NOT SameXY(XY,Limbo) THEN
      BEGIN
      FOR Dir:=NoDir TO NW DO
         BEGIN
         x:=XY.x+DirX[Dir];
         y:=XY.y+DirY[Dir];
         IF InGalaxy(x,y) THEN
            BEGIN
            TempLoc.x:=x;
            TempLoc.y:=y;
            GetObject(TempLoc,Obj);
            OtherEmp:=GetStatus(Obj);

            IF (NOT Scouted(Emp,Obj)) AND (Obj.ObjTyp<>Void) THEN
               BEGIN
               { send news to empire }
               IF (NOT Known(Emp,Obj)) AND 
                  (OtherEmp<>Emp) THEN
                  BEGIN
                  Loc.XY:=Limbo;  Loc.ID:=Obj;
                  AddNews(Emp,POk,Loc,0,0,0);
                  END;

               ScoutObject(Emp,Obj);
               END;

            IF GetNebula(TempLoc)=DarkNebula THEN
               Exit;
            END;
         END;  { loop }
      END;
   END;  { Scout }

PROCEDURE ClearScoutSet{Emp: Empire};
   VAR
      i: Byte;

   BEGIN
   WITH Universe^ DO
      BEGIN
      FOR i:=1 TO NoOfPlanets DO
         Planet[i].ScoutedBy:=Planet[i].ScoutedBy-[Emp];

      FOR i:=1 TO MaxNoOfStarbases DO
         IF i IN SetOfActiveStarbases THEN
            Starbase[i].ScoutedBy:=Starbase[i].ScoutedBy-[Emp];

      FOR i:=1 TO MaxNoOfStargates DO
         IF i IN SetOfActiveGates THEN
            Stargate[i].ScoutedBy:=Stargate[i].ScoutedBy-[Emp];

      FOR i:=1 TO MaxNoOfConstrSites DO
         IF i IN SetOfActiveConstructionSites THEN
            Constr[i].ScoutedBy:=Constr[i].ScoutedBy-[Emp];
      END;  { with scope }
   END;  { ClearScoutSet }

PROCEDURE ClearKnownSet(Emp: Empire);
   VAR
      i: Byte;

   BEGIN
   WITH Universe^ DO
      BEGIN
      FOR i:=1 TO NoOfPlanets DO
         Planet[i].KnownBy:=Planet[i].KnownBy-[Emp];

      FOR i:=1 TO MaxNoOfStarbases DO
         IF i IN SetOfActiveStarbases THEN
            Starbase[i].KnownBy:=Starbase[i].KnownBy-[Emp];

      FOR i:=1 TO MaxNoOfStargates DO
         IF i IN SetOfActiveGates THEN
            Stargate[i].KnownBy:=Stargate[i].KnownBy-[Emp];

      FOR i:=1 TO MaxNoOfConstrSites DO
         IF i IN SetOfActiveConstructionSites THEN
            Constr[i].KnownBy:=Constr[i].KnownBy-[Emp];
      END;  { with scope }
   END;  { ClearKnownSet }

PROCEDURE DesignateWorld(World: IDNumber; NewType: WorldTypes);
   VAR
      Emp: Empire;
      CapID: IDNumber;
      NewTech,OldTech: TechLevel;
      Eff: Index;

   BEGIN
   IF NewType=CapTyp THEN
      BEGIN
      Emp:=GetStatus(World);
      GetCapital(Emp,CapID);
      OldTech:=GetTech(CapID);
      NewTech:=GetTech(World);
      IF OldTech>NewTech THEN
         SetEmpireTechnology(Emp,NewTech,TechDev[NewTech])
      ELSE IF OldTech<NewTech THEN
         SetEmpireTechnology(Emp,NewTech,TechDev[Pred(NewTech)]);

      SetCapital(Emp,World);
      SetType(CapID,BseTyp);

      { change efficiency }
      Eff:=GetEfficiency(CapID);
      SetEfficiency(CapID,Eff-Round(Eff/(1.5+Random)));

      ChangeTotalRevIndex(Emp,Rnd(35,45));
      END;

   { change efficiency }
   Eff:=GetEfficiency(World);
   SetEfficiency(World,Eff-Round(Eff/(1.5+Random)));

   SetType(World,NewType);
   END;  { DesignateWorld }

PROCEDURE TerraformWorld(World: IDNumber; NewClass: WorldClass);
   VAR
     OldIndustry: IndusArray;
     NewIndustry: IndusArray;
     IndusI: IndusTypes;
   
   BEGIN
     GetIndus(World,OldIndustry);
     SetClass(World,TerCls);
     SetType(World,TerTyp);
     SetTerraformTarget(World,NewClass);  
     For IndusI:=BioInd TO TriInd DO
       NewIndustry[IndusI]:=Round(OldIndustry[IndusI]/2);
     PutIndus(World,NewIndustry);
     SetEfficiency(World,Round(GetEfficiency(World)/100*Rnd(30,60)));     
   END;  { TerraformWorld }


PROCEDURE GetIndustrialDistribution{Obj: IDNumber;
                                    VAR IndDist: IndusDistArray};
{ GetIndustrialDistribution:
   This procedure calculates the optimum industrial distribution for a
   world given its vital statistics. }

   CONST
      Gamma: ARRAY [IndusTypes,IndusTypes] OF Real =
            {         Bio   Che   Min      SYG      SYJ      SYS      SYT   Sup   Tri }
{ Bio }     (  (        0,    0,    0,       0,       0,       0,       0,    0,    0 ),
{ Che }        (  1.12857,    0,    0, 0.19143, 0.50000, 0.36714, 0.25286,    0,    0 ),
{ Min }        (  0.10000,    0,    0, 0.30514, 0.27286, 0.53714, 0.83571,    0,    0 ),
{ SYG }        (        0,    0,    0,       0,       0,       0,       0,    0,    0 ),
{ SYJ }        (        0,    0,    0,       0,       0,       0,       0,    0,    0 ),
{ SYS }        (        0,    0,    0,       0,       0,       0,       0,    0,    0 ),
{ SYT }        (        0,    0,    0,       0,       0,       0,       0,    0,    0 ),
{ Sup }        (        0,    0,    0,       0,       0,       0,       0,    0,    0 ),
{ Tri }        (  0.10000,    0,    0, 0.07627, 0.20400, 0.16667, 0.08000,    0,    0 )  );

   VAR
      X: IndusRArray;
      Alpha: Real;         { Alpha = T2(tech)(Eff+250)/K6 }
      Beta: IndusRArray;   { Beta = (TIP)(ClsAdj)(AmbAdj)/100 }
      A,B,I: Real;
      TIP: Real;           { TIP = Total Industrial Production }

      MainInd,IndI: IndusTypes;
      Special: SetOfSpecialConditions;

      Tech: TechLevel;
      Eff: Index;
      Typ: WorldTypes;
      Cls: WorldClass;
      Pop: Population;
      AmbAdd: Boolean;
      Temp: Real;

   { MAIN PROCEDURE: GetIndustrialDistribution }
   BEGIN
   Tech:=GetTech(Obj);
   Cls:=GetClass(Obj);
   Pop:=GetPopulation(Obj);
   Typ:=GetType(Obj);
   Eff:=GetEfficiency(Obj);
   GetSpecial(Obj,Special);
   AmbAdd:=(AmbAddict IN Special);

   { initialize arrays }
   FillChar(IndDist,SizeOf(IndDist),0);

   Alpha:=(TechAdj2[Tech]/100)*(Eff+250)/K6;
   TIP:=TotalProd(Pop,Tech);
   IF AmbAdd THEN
      TIP:=TIP*AmbrosiaAdj;
   IF TIP>999 THEN
      TIP:=999;

   { Calculate Beta array:   Beta[IndI] = TIP * ClassIndAdj[Cls,IndI]/10000 }
   Temp:=TIP/10000;
   FOR IndI:=BioInd TO TriInd DO
      BEGIN
      Beta[IndI]:=Temp*ClassIndAdj[Cls,IndI];
      IF Beta[IndI]=0 THEN
         Beta[IndI]:=1;
      END;

   { calculate sup industry }
   I:=( Sqrt( (SafetyAdj*ISSP[GetISSP(Obj,SupInd)]*(SuppliesPerBillion/100)*Pop) /
              ((ThgAdj[SupInd,sup]/100)*Alpha) ) - K4) / Beta[SupInd];
   IF (I>95) OR (I<0) OR (Typ=AgrTyp) THEN
      I:=95;
   IndDist[SupInd]:=I;

   IF Typ IN [AgrTyp,CheTyp,MinTyp,RawTyp,RawSTyp,RsrTyp..TriTyp] THEN
      { ASSERT: all values of PI = 0 }
      BEGIN
      I:=100-IndDist[SupInd];
      IndDist[CheInd]:=I*(TypeData[Typ,CheInd]/100);
      IndDist[MinInd]:=I*(TypeData[Typ,MinInd]/100);
      IndDist[TriInd]:=I*(TypeData[Typ,TriInd]/100);
      END
   ELSE
      { ASSERT: at least one value of PI is >0 }
      BEGIN
      { get main PI }
      MainInd:=PrincipalIndustry[Typ];

      { calculate temporaries }
      A:=-K4*(1/Beta[CheInd]+1/Beta[MinInd]+1/Beta[TriInd]);
      B:=(Sqrt(SafetyAdj*ISSP[GetISSP(Obj,CheInd)]*Gamma[CheInd,MainInd])/Beta[CheInd] +
          Sqrt(SafetyAdj*ISSP[GetISSP(Obj,MinInd)]*Gamma[MinInd,MainInd])/Beta[MinInd] +
          Sqrt(SafetyAdj*ISSP[GetISSP(Obj,TriInd)]*Gamma[TriInd,MainInd])/Beta[TriInd] );

      { calculate I(MainInd) }
      I:=(100-(IndDist[SupInd]+A+(K4*B)))/(1+(Beta[MainInd]*B));
      IF I>TypeData[Typ,MainInd] THEN
         I:=TypeData[Typ,MainInd];

      IndDist[MainInd]:=I;

      { calculate RI industries }
      I:=( (IndDist[MainInd]*Beta[MainInd]+K4) *
           Sqrt(SafetyAdj*ISSP[GetISSP(Obj,CheInd)]*Gamma[CheInd,MainInd]) - K4 )/Beta[CheInd];
      IF I<0 THEN
         I:=1;
      IndDist[CheInd]:=I;

      I:=( (IndDist[MainInd]*Beta[MainInd]+K4) *
           Sqrt(SafetyAdj*ISSP[GetISSP(Obj,MinInd)]*Gamma[MinInd,MainInd]) - K4 )/Beta[MinInd];
      IF I<0 THEN
         I:=1;
      IndDist[MinInd]:=I;

      I:=( (IndDist[MainInd]*Beta[MainInd]+K4) *
           Sqrt(SafetyAdj*ISSP[GetISSP(Obj,TriInd)]*Gamma[TriInd,MainInd]) - K4 )/Beta[TriInd];
      IF I<0 THEN
         I:=1;
      IndDist[TriInd]:=I;
      END;
   END;  { GetIndustrialDistribution }

PROCEDURE CreatePlanet(ID: IDNumber; NewXY: XYCoord);
   BEGIN
   WITH Universe^ DO
      BEGIN
      WITH Sector[NewXY.x]^[NewXY.y] DO
         Obj:=ID;

      WITH Planet[ID.Index] DO
         BEGIN
         XY:=NewXY;
         ImpExp:=DefaultISSP;
         END;
      END;  { with scope }
   END;  { CreatePlanet }

FUNCTION NextStarbaseSlot: Word;
   VAR
      Slot: Word;

   BEGIN
   Slot:=MaxNoOfStarbases;
   WHILE (Slot>0) AND (Slot IN SetOfActiveStarbases) DO
      Dec(Slot);
   NextStarbaseSlot:=Slot;
   END;  { NextStarbaseSlot }

PROCEDURE CreateStarbase(ID: IDNumber; NewEmp: Empire; NewXY: XYCoord; NewTyp: StarbaseTypes);
   BEGIN
   WITH Universe^ DO
      BEGIN
      WITH Sector[NewXY.x]^[NewXY.y] DO
         Obj:=ID;

      WITH Starbase[ID.Index] DO
         BEGIN
         XY:=NewXY;
         STyp:=NewTyp;
         Dest:=XY;
         Status:=FReady;
         Emp:=NewEmp;
         ScoutedBy:=[NewEmp];
         KnownBy:=[NewEmp];
         RevIndex:=0;

         FillChar(Ships,SizeOf(Ships),0);
         FillChar(Cargo,SizeOf(Cargo),0);
         FillChar(Indus,SizeOf(Indus),0);
         FillChar(Defns,SizeOf(Defns),0);
         END;
      END;  { with scope }

   SetOfActiveStarbases:=SetOfActiveStarbases+[ID.Index];
   SetOfStarbasesOf[NewEmp]:=SetOfStarbasesOf[NewEmp]+[ID.Index];
   END;  { CreateStarbase }

FUNCTION NextStargateSlot: Word;
   VAR
      Slot: Word;

   BEGIN
   Slot:=MaxNoOfStargates;
   WHILE (Slot>0) AND (Slot IN SetOfActiveGates) DO
      Dec(Slot);
   NextStargateSlot:=Slot;
   END;  { NextStargateSlot }

FUNCTION CanUseGate(Emp: Empire; GateID: IDNumber) : Boolean;
   BEGIN
     CanUseGate:=(GetWarpLinkFreq(Emp,GateID)=GetWarpLinkFreq(GetStatus(GateID),GateID))
   END;

PROCEDURE CreateStargate(ID: IDNumber; NewEmp: Empire; GateType: StargateTypes; Pos: XYCoord);
   BEGIN
   SetOfActiveGates:=SetOfActiveGates+[ID.Index];
   WITH Universe^ DO
      BEGIN
      WITH Stargate[ID.Index] DO
         BEGIN
         XY:=Pos;
         Emp:=NewEmp;
         ScoutedBy:=[NewEmp];
         KnownBy:=[NewEmp];
         GTyp:=GateType;
         Dest:=Limbo;
	 FillChar(WLF,SizeOf(WLF),0);
	 WLF[Emp]:=Random(9999);
         END;

      Sector[Pos.x]^[Pos.y].Obj:=ID;
      END;  { with scope }
   END;  { CreateStargate }

{ FLEET routines }

PROCEDURE BalanceFleet(VAR Sh: ShipArray; VAR Cr: CargoArray);
{ BalanceFleet: ----------------------------------------------------------------
   This procedure will insure that the given configuration is balanced
   by deleting cargo starting with tri and ending with men.
------------------------------------------------------------------------------ }
   CONST
      CargoPriority: ARRAY [1..7] OF CargoTypes =
         ( che, sup, met, men, nnj, tri, amb );

   VAR
      ThgI: ResourceTypes;
      SpaceLeft,NewSpaceLeft: Integer;
      CargoToRemove: Word;

   BEGIN
   CargoToRemove:=1;
   ThgI:=CargoPriority[CargoToRemove];
   SpaceLeft:=FleetCargoSpace(Sh,Cr);
   WHILE SpaceLeft<0 DO
      BEGIN
      Cr[ThgI]:=0;
      NewSpaceLeft:=FleetCargoSpace(Sh,Cr);
      IF NewSpaceLeft<0 THEN
         BEGIN
         SpaceLeft:=NewSpaceLeft;
         Inc(CargoToRemove);
         ThgI:=CargoPriority[CargoToRemove];
         END
      ELSE
         BEGIN
         Cr[ThgI]:=NewSpaceLeft*CargoSpace[ThgI];
         SpaceLeft:=0;
         END;
      END;  { while }
   END;  { BalanceFleet }

(* STARGATE Procedures ------------------------------------------------------ *)

PROCEDURE DestroyStargate(GteID: IDNumber);
   BEGIN
   WITH Universe^.Stargate[GteID.Index] DO
      BEGIN
      Sector[XY.x]^[XY.y].Obj:=EmptyQuadrant;
      END;  { with scope }

   SetOfActiveGates:=SetOfActiveGates-[GteID.Index];
   END;  { DestroyStargate }

(* CONSTRUCTION Procedures -------------------------------------------------- *)

PROCEDURE Construction(Empr: Empire; ConsType: ConstrTypes; Loc: XYCoord; 
                       VAR ConID: IDNumber);
   VAR
      i: Word;

   PROCEDURE GetConstructionSlot(VAR Slot: Word);

      BEGIN
      Slot:=MaxNoOfConstrSites;
      WHILE (Slot>0) AND (Slot IN SetOfActiveConstructionSites) DO
         Dec(Slot);
      END;  { GetConstructionSlot }

   { Construction: MAIN PROCEDURE }
   BEGIN
   GetConstructionSlot(i);
   IF i<>0 THEN
      BEGIN
      ConID.ObjTyp:=Con;
      ConID.Index:=i;

      WITH Universe^.Constr[i] DO
         BEGIN
         XY:=Loc;
         Emp:=Empr;
         CTyp:=ConsType;
         ScoutedBy:=[Empr];
         KnownBy:=[Empr];
         TimeToCompletion:=YearsToBuild[ConsType];
         END;  { with scope }

      SetOfActiveConstructionSites:=SetOfActiveConstructionSites+[i];
      SetOfConstructionSitesOf[Empr]:=SetOfConstructionSitesOf[Empr]+[i];
      Sector[Loc.x]^[Loc.y].Obj:=ConID;
      END;
   END;  { Construction }

PROCEDURE DestroyConstruction(ConID: IDNumber);
{ DestroyConstruction: }
   VAR
      OldEmp: Empire;

   BEGIN
   WITH Universe^,Constr[ConID.Index] DO
      BEGIN
      OldEmp:=Emp;
      Sector[XY.x]^[XY.y].Obj:=EmptyQuadrant;
      END;  { with scope }

   SetOfActiveConstructionSites:=SetOfActiveConstructionSites-[ConID.Index];
   SetOfConstructionSitesOf[OldEmp]:=SetOfConstructionSitesOf[OldEmp]-[ConID.Index];
   END;  { DestroyConstruction }

(* STATUS Procedures -------------------------------------------------------- *)

PROCEDURE GetWorldStatus(Emp: Empire; WorldID: IDNumber; VAR Line: LineStr);
{ GetWorldStatus:
   This procedure returns the status of 'WorldID' in 'Line' as seen from
   empire 'Emp.' }

   VAR
      Coord: XYCoord;
      NameStr,EmpNameStr,temp1: String16;
      Class: WorldClass;
      Typ: WorldTypes;
      Tech: TechLevel;
      Sta: Empire;
      Eff: Index;
      RevI: Index;
      Pop: Population;
      Shp: ShipArray;
      Car: CargoArray;
      Ind: IndusArray;
      Special: SetOfSpecialConditions;
      ThgI: ResourceTypes;

   FUNCTION GetImportExportStr(ID: IDNumber): String16;
      CONST
         IndN: ARRAY [IndusTypes] OF Char =
            'BCM----ST';
      VAR
         i: Real;
         TempE,TempI: String16;
         Ind: IndusTypes;

      BEGIN
      TempE:='';
      TempI:='';
      FOR Ind:=CheInd TO TriInd DO
         IF Ind IN [CheInd,MinInd,SupInd,TriInd] THEN
            BEGIN
            i:=GetISSP(ID,Ind);
            IF i=NormalISSP THEN
               BEGIN
               TempE:=TempE+'-';
               TempI:=TempI+'-';
               END
            ELSE IF i>NormalISSP THEN
               BEGIN
               TempE:=TempE+IndN[Ind];
               TempI:=TempI+'-';
               END
            ELSE
               BEGIN
               TempE:=TempE+'-';
               TempI:=TempI+IndN[Ind];
               END;
            END;

      GetImportExportStr:=TempI+' '+TempE;
      END;  { GetImportExportStr }

   { GetWorldStatus: MAIN PROCEDURE }
   BEGIN
   NameStr:=ObjectName(Emp,WorldID,ShortFormat);
   AdjustString(NameStr,8);

   { get data }
   Class:=GetClass(WorldID);
   Typ:=GetType(WorldID);
   Tech:=GetTech(WorldID);
   Sta:=GetStatus(WorldID);
   Eff:=GetEfficiency(WorldID);
   RevI:=GetRevIndex(WorldID);
   Pop:=GetPopulation(WorldID);
   GetShipsKnown(Emp,WorldID,Shp);
   GetCargo(WorldID,Car);
   GetIndus(WorldID,Ind);
   GetSpecial(WorldID,Special);

   EmpNameStr:=EmpireName(Sta);
   AdjustString(EmpNameStr,3);
   Line:=NameStr+' '+EmpNameStr+' ';
   Line:=Line+ClassStr[Class]+' ';
   Line:=Line+TypeStr[Typ]+' ';
   Line:=Line+TechStr[Tech]+' ';

   IF Pop>9 THEN
      Str(Pop/100:4:1,temp1)
   ELSE
      temp1:='<0.1';
   Line:=Line+temp1+' ';
   Str(Eff:3,temp1);
   Line:=Line+temp1+' ';

   IF AmbAddict IN Special THEN
      Line:=Line+'y '
   ELSE
      Line:=Line+'- ';

   Line:=Line+GetImportExportStr(WorldID)+' ';

   Line:=Line+HiLo(RevI);

   IF Emp=Sta THEN
      BEGIN
      Str(Shp[jtn]:5,temp1);
      Line:=Line+temp1;
      Str(Shp[trn]:5,temp1);
      Line:=Line+temp1;

      FOR ThgI:=amb TO tri DO
         BEGIN
         Str(Car[ThgI]:5,temp1);
         Line:=Line+temp1;
         END;
      END
   ELSE
      BEGIN
      Line:=Line+YesNo(Shp[jtn])+YesNo(Shp[trn])+'  --   --   --   --   --  ';
      END;  { if }
   END;  { GetWorldStatus }

PROCEDURE GetEmpireStatus(Emp: Empire;
                          VAR Planets: Word;
                          VAR TotalPop: LongInt;
                          VAR SInd: Word;
                          VAR TotalShips: TotalShipArray);
{ GetEmpireStatus: -------------------------------------------------------------
   Returns vital stats for the given empire:

   Planets:     No. of planets
   TotalPop:    Population of the empire (10 millions)
   SInd:        Ship yard indus (tenths)
   TotalShips:  Total number of ships in the empire.
------------------------------------------------------------------------------ }
   VAR
      i: Word;
      ShipInd,IP: Real;
      ActiveEmpireFleets: FleetSet;
      ShpI: ShipTypes;
      IndI: IndusTypes;

   BEGIN
   Planets:=0;
   TotalPop:=0;
   ShipInd:=0;
   FillChar(TotalShips,SizeOf(TotalShips),0);

   FOR i:=1 TO NoOfPlanets DO
      IF i IN SetOfPlanetsOf[Emp] THEN
         WITH Universe^.Planet[i] DO
            BEGIN
            Inc(Planets);
            Inc(TotalPop,Pop);
            IP:=(TechAdj2[Tech]/100)*((Eff+250)/100)/K6;
            FOR IndI:=SYGInd TO SYTInd DO
               ShipInd:=ShipInd+IP*Sqr(Indus[IndI]+K4);
            FOR ShpI:=fgt TO trn DO
               TotalShips[ShpI]:=TotalShips[ShpI]+Ships[ShpI];
            END;

   FOR i:=1 TO MaxNoOfStarbases DO
      IF i IN SetOfStarbasesOf[Emp] THEN
         WITH Universe^.Starbase[i] DO
            BEGIN
            Inc(Planets);
            Inc(TotalPop,Pop);
            FOR ShpI:=fgt TO trn DO
               TotalShips[ShpI]:=TotalShips[ShpI]+Ships[ShpI];
            IF STyp=cmp THEN
               BEGIN
               IP:=(TechAdj2[Tech]/100)*((Eff+250)/100)/K6;
               FOR IndI:=SYGInd TO SYTInd DO
                  ShipInd:=ShipInd+IP*Sqr(Indus[IndI]+K4);
               END;
            END;

   ActiveEmpireFleets:=SetOfFleetsOf[Emp] * SetOfActiveFleets;
   FOR i:=1 TO MaxNoOfFleets DO
      IF i IN ActiveEmpireFleets THEN
         WITH Universe^.Fleet[i]^ DO
            BEGIN
            FOR ShpI:=fgt TO trn DO
               TotalShips[ShpI]:=TotalShips[ShpI]+Ships[ShpI];
            END;  { with scope }

   SInd:=Round(ShipInd);
   END;  { GetEmpireStatus }

PROCEDURE GetEmpireStatusLine(Emp: Empire; Full: Boolean; VAR Line: LineStr);
{ GetEmpireStatusLine: }
   VAR

      TotalShips: TotalShipArray;
      TotalPop: LongInt;
      Planets,SInd: Word;
      CapitalID: IDNumber;
      CapTech: TechLevel;
      i: Byte;
      ShpI: ShipTypes;
      IndI: IndusTypes;
      temp: String8;

   BEGIN
   CapitalID:=Universe^.EmpireData[Emp].Capital;
   CapTech:=GetTech(CapitalID);

   Line:=EmpireName(Emp);
   AdjustString(Line,12);
   Line:=Line+' '+TechStr[CapTech]+' ';

   GetEmpireStatus(Emp,Planets,TotalPop,SInd,TotalShips);

   Str(Planets:3,temp);  Line:=Line+temp+' ';
   Str((SInd/10):4:1,temp);  Line:=Line+temp+' ';
   Str((TotalPop/100):5:1,temp);  Line:=Line+temp+' ';
   IF Full THEN
      FOR ShpI:=fgt TO trn DO
         BEGIN
         Str(TotalShips[ShpI]:6,temp);
         Line:=Line+temp+' ';
         END
   ELSE
      Line:=Line+' ----   ----   ----   ----   ----   ----   ----  ';
   END;  { GetEmpireStatusLine }

PROCEDURE GetMilitaryStatus{Emp: Empire; WorldID: IDNumber; var Line: LineStr};
{ GetMilitaryStatus:
   Returns the military status of a world. }

   var
      Coord: XYCoord;
      NameStr,EmpNameStr,temp1: String16;
      Sta: Empire;
      Shp: ShipArray;
      Car: CargoArray;
      Defns: DefnsArray;

      b: ResourceTypes;
      c: ResourceTypes;

   { GetMilitaryStatus: MAIN PROCEDURE }
   BEGIN
   NameStr:=ObjectName(Emp,WorldID,ShortFormat);
   AdjustString(NameStr,8);

   { get data }
   Sta:=GetStatus(WorldID);
   GetCargo(WorldID,Car);
   GetDefns(WorldID,Defns);

   EmpNameStr:=EmpireName(Sta);
   AdjustString(EmpNameStr,3);
   Line:=NameStr+' '+EmpNameStr+' ';

   if Emp=Sta then
      begin
      GetShips(WorldID,Shp);
      Str(Car[men]:5,temp1);
      Line:=Line+temp1;
      Str(Car[nnj]:5,temp1);
      Line:=Line+temp1;

      for b:=fgt TO trn do
         begin
         Str(Shp[b]:5,temp1);
         Line:=Line+temp1;
         end;

      for c:=LAM TO ion do
         begin
         Str(Defns[c]:5,temp1);
         Line:=Line+temp1;
         end;
      end
   else
      begin
      GetShipsKnown(Emp,WorldID,Shp);
      Line:=Line+YesNo(Car[men])+YesNo(Car[nnj]);

      for b:=fgt TO trn do
         Line:=Line+YesNo(Shp[b]);

      for c:=LAM to ion do
         Line:=Line+YesNo(Defns[c]);
      end;
   end;  { GetMilitaryStatus }

FUNCTION PassingThroughFortress(FltXY: XYCoord): Boolean;
   VAR
      BaseObj: IDNumber;

   BEGIN
   GetObject(FltXY,BaseObj);
   IF (BaseObj.ObjTyp=Base) AND (GetBaseType(BaseObj)=frt) THEN
      PassingThroughFortress:=True
   ELSE
      PassingThroughFortress:=False;
   END;  { PassingThroughFortress }

FUNCTION PassingThroughGate(FltID: IDNumber; FltXY,DestXY: XYCoord): Boolean;
{ PassingThroughGate: ----------------------------------------------------------
   Returns true if the fleet at FltXY and going to DestXY can use a stargate
   or link. This procedure does not take fortresses into account.
------------------------------------------------------------------------------ }
   VAR
      GateObj,DestObj: IDNumber;
      TempResult: Boolean;
      GateType: StargateTypes;

   BEGIN
   TempResult:=False;
   GetObject(FltXY,GateObj);
   IF (GateObj.ObjTyp=Gate) 
   AND (GetWarpLinkFreq(GetStatus(FltID),GateObj)=GetWarpLinkFreq(GetStatus(GateObj),GateObj)) THEN
      BEGIN
      GateType:=GetGateType(GateObj);
      IF GateType=gte THEN
         TempResult:=True
      ELSE IF GateType=lnk THEN
         BEGIN
         GetObject(DestXY,DestObj);
         IF (DestObj.ObjTyp=Gate)
            AND (GetGateType(DestObj) IN [lnk,gte])
            AND (GetWarpLinkFreq(GetStatus(FltID),DestObj)<>GetWarpLinkFreq(GetStatus(DestObj),DestObj)) THEN
            TempResult:=True;
         END
      END;
   PassingThroughGate:=TempResult;
   END;  { PassingThroughGate }

FUNCTION EstimatedDateOfArrival(ID: IDNumber): Integer;
   VAR
      EDA,Dist,QuadsPerYear: Integer;

   BEGIN {EstimatedDateOfArrival - Main Procedure}
   WITH Universe^ DO
      CASE ID.ObjTyp OF
         Flt : WITH Fleet[ID.Index]^ DO
                  BEGIN
                  IF PassingThroughGate(ID,XY,Dest) THEN
                     EDA:=1
                  ELSE
                     BEGIN
                     Dist:=GreaterInt(Abs(XY.x-Dest.x),Abs(XY.y-Dest.y));
                     IF PassingThroughFortress(XY) THEN
                        Dist:=GreaterInt(Integer(Dist)-4,1);
		     
                     QuadsPerYear:=FltMovementRate[TypeOfFleet(ID)];
                     EDA:=(Dist DIV QuadsPerYear);
                     IF (Dist MOD QuadsPerYear)>0 THEN
                        EDA:=EDA+1;
                     END;
                  END;
         Base: WITH Starbase[ID.Index] DO
                  BEGIN
                  EDA:=GreaterInt(Abs(XY.x-Dest.x),Abs(XY.y-Dest.y));
                  END;
      END;  { case }

   EstimatedDateOfArrival:=EDA;
   END;  { EstimatedDateOfArrival }

function EstimatedRange(ID: IDNumber): Integer;
   var
      FltFuel: Real;
      Ships: ShipArray;
      Cargo: CargoArray;

   { EstimatedRange: MAIN PROCEDURE }
   BEGIN
   GetShips(ID,Ships);
   GetCargo(ID,Cargo);
   CASE ID.ObjTyp OF
      Flt : BEGIN
            FltFuel:=GetFleetFuel(ID);
            EstimatedRange:=Trunc(FltFuel/FuelConsumption(Ships,Cargo));
            END;
     Base : BEGIN
            EstimatedRange:=Cargo[tri] DIV 100;
            END;
   END;  { case }
   END;  { EstimatedRange }

PROCEDURE GetFleetPositionStatus{Emp: Empire; ID: IDNumber;
                                 VAR Line: LineStr};
{ GetFleetPositionStatus: }

   CONST
      FltStatusName: ARRAY [FleetStatus] OF STRING [18] =
         ( 'at destination    ',
           'In transit (?)    ',
           'out of trillum    ',
           'lost              ' );

   var
      Loc: Location;
      Location,Destination: XYCoord;
      EDA,Range: Integer;
      temp1,FltName,PosName,DesName: String16;
      StaName: String32;
      FltSta: FleetStatus;
      FltEmp: Empire;

   { GetFleetPositionStatus: MAIN PROCEDURE }
   BEGIN
   WITH Universe^ DO
      BEGIN
      CASE ID.ObjTyp OF
         Flt : WITH Fleet[ID.Index]^ DO
                  BEGIN
                  Location:=XY;
                  Destination:=Dest;
                  FltSta:=Status;
                  FltEmp:=Emp;
                  END;
         Base : WITH Starbase[ID.Index] DO
                  BEGIN
                  Location:=XY;
                  Destination:=Dest;
                  FltSta:=Status;
                  FltEmp:=Emp;
                  END;
      END;  { case }

      IF ID.ObjTyp=Flt THEN
         BEGIN
         Loc.XY:=Location;
         Loc.ID:=EmptyQuadrant;
         GetName(Emp,Loc,ShortFormat,PosName);

         Loc.XY:=Destination;
         Loc.ID:=EmptyQuadrant;
         GetName(Emp,Loc,ShortFormat,DesName);
         END
      ELSE
         BEGIN
         GetCoordName(Location,PosName);
         GetCoordName(Destination,DesName);
         END;

      AdjustString(PosName,8);
      AdjustString(DesName,8);
      end;  { with scope }

   EDA:=EstimatedDateOfArrival(ID);
   Range:=EstimatedRange(ID);

   FltName:=ObjectName(Emp,ID,ShortFormat);
   AdjustString(FltName,8);

   IF FltEmp=Emp THEN
      BEGIN
      Line:='     '+FltName+'   ';
      Line:=Line+PosName+'   ';
      Line:=Line+DesName+'   ';

      IF FltSta=FInTrans THEN
         BEGIN
         Str(EDA,temp1);
         StaName:='In transit ('+temp1+')';
         END
      ELSE
         StaName:=FltStatusName[FltSta];

      AdjustString(StaName,18);
      Line:=Line+StaName+'   ';

      Str(Range:3,temp1);
      Line:=Line+temp1;
      IF (ID.ObjTyp=Flt) AND (FleetNextStatement(ID)<>0) THEN
         Line:=Line+'       (orders)';
      END
   ELSE
      BEGIN
      Line:=EmpireName(FltEmp);
      AdjustString(Line,3);
      Line:=Line+'  '+FltName+'   ';
      Line:=Line+PosName+'   ';
      IF (FltSta=FReady) AND Scouted(Player,ID) THEN
         Line:=Line+PosName+'   '
      ELSE
         Line:=Line+'(unknown)  ';

      IF Scouted(Player,ID) THEN
         StaName:=FltStatusName[FltSta]
      ELSE
         StaName:='(out of range)';

      AdjustString(StaName,18);
      Line:=Line+StaName+'      ';
      END;
   END;  { GetFleetPositionStatus }

PROCEDURE GetFleetStatusLine(Emp: Empire; ID: IDNumber;
                             VAR Line: LineStr);
{ GetFleetStatus: }

   var
      Sta: Empire;
      XY: XYCoord;
      Shp: ShipArray;
      Car: CargoArray;
      NameStr,temp1: String16;
      ThgI: ResourceTypes;

   { GetFleetStatus: MAIN PROCEDURE }
   BEGIN
   NameStr:=ObjectName(Emp,ID,ShortFormat);
   AdjustString(NameStr,8);

   Sta:=GetStatus(ID);
   GetShips(ID,Shp);
   GetCargo(ID,Car);

   Line:=NameStr;
   IF Sta=Player THEN
      BEGIN
      FOR ThgI:=fgt TO trn DO
         BEGIN
         Str(Shp[ThgI]:5,temp1);
         Line:=Line+temp1;
         END;
      FOR ThgI:=men TO tri DO
         BEGIN
         Str(Car[ThgI]:5,temp1);
         Line:=Line+temp1;
         END;
      END
   ELSE IF Scouted(Player,ID) THEN
      BEGIN
      FOR ThgI:=fgt TO trn DO
         Line:=Line+YesNo(Shp[ThgI]);
      Line:=Line+'  --   --   --   --   --   --   -- '
      END
   ELSE
      BEGIN
      Line:=Line+' (out of range)';
      END;
   END;  { GetFleetStatusLine }

PROCEDURE GetNewsLine(Player: Empire; Item: NewsRecordPtr;
                      VAR Line: LineStr);
{ GetNewsLine: }

   CONST
      ThgN: ARRAY [CargoTypes] OF String16 =
         ( '','','ambrosia','chemicals','metals','supplies','trillum' );

   VAR
      Headline: NewsTypes;
      Loc: Location;
      Parm1,Parm2,Parm3: Integer;

      LocN: String32;
      EmpN,Parm1N,Parm2N: String16;
      DeathN: String32;
      Error: Integer;

   PROCEDURE LackArticle(LocN: String32;
                         Thg: ResourceTypes;
                         VAR Line: LineStr);
      VAR
         Verb: String32;

      BEGIN
      CASE Rnd(1,5) OF
         1 : Verb:=' needs more ';
         2 : Verb:=' lacks ';
         3 : Verb:=' has run out of ';
         4 : verb:=' requires more ';
         5 : verb:=' has used up all its ';
      END;  { case }

      Line:=LocN+Verb+ThgN[Thg]+'.';
      END;  { LackArticle }

   { GetNewsLine: MAIN PROCEDURE }
   BEGIN
   GetNewsItem(Item,Headline,Loc,Parm1,Parm2,Parm3);
   GetName(Player,Loc,LongFormat,LocN);
   IF (Parm1<=8) AND (Parm1>=0) THEN
      EmpN:=EmpireName(Empire(Parm1));
   Str(Parm1,Parm1N);
   Str(Parm2,Parm2N);
   IF Parm1<100 THEN
      BEGIN
      Str(Parm1*10,DeathN);
      DeathN:=DeathN+' million';
      END
   ELSE
      BEGIN
      Str(Parm1/100:4:1,DeathN);
      DeathN:=DeathN+' billion';
      END;

   CASE Headline OF
             Lack : LackArticle(LocN,ResourceTypes(Parm1),Line);
          DefLack : Line:='* needs '+ThgN[ResourceTypes(Parm1)]+' to build defenses.';
          IndLack : Line:='* cannot build up its industry due to a lack of metals.';
            Starv : Line:=DeathN+' people have died of starvation on *.';
            NTech : Line:='* has advanced to '+TechN[TechLevel(Parm1)]+' level technology.';
            RTech : Line:='* has regressed to '+TechN[TechLevel(Parm1)]+' level technology.';
         ConsLack : LackArticle(LocN,ResourceTypes(Parm1),Line);
         ConsDone : Line:='Construction at * has been completed.';
          RebelW1 : Line:='The people of * are dissatisfied with the empire.';
          RebelW2 : Line:='Riots and demonstrations are widespread on *.';
          RebelW3 : Line:='Some signs of organized rebellion detected on *.';
          RebelW4 : Line:='Rebel forces on * are well organized and plan an attack.';
            Rebel : Line:='Rebel forces on * have succeeded in taking over.';
           URebel : Line:='Imperial troops ended a rebellion on *.  '+Parm1N+' legions lost.';
              POk : Line:='Imperial probe has scouted *.';
         NCapTech : Line:='* has developed '+TechnologyName[TechnologyTypes(Parm1)]+' technology.';
          NCapLvl : Line:='* has developed '+TechN[TechLevel(Parm1)]+' level technology.';

         BattleW1 : Line:='* was attacked by @.  Attack force destroyed.';
         BattleW2 : Line:='* was attacked by @.  Attack force retreated.';
          BattleL : Line:='* has been conquered by the empire of @.';
          WAddict : Line:='The people of * are now addicted to ambrosia.';
          UAddict : Line:='* is no longer addicted to ambrosia.';
        AddictDie : Line:=DeathN+' people have died on * of ambrosia withdrawal.';
         RiotsDie : Line:=DeathN+' people have died in large-scale riots on *.';
            IndDs : Line:='   '+Parm1N+' '+IndusNames[IndusTypes(Parm2)]+' have been destroyed on *.';
             DInd : Line:='* has declared independence.';
             Join : Line:='* has joined the empire of @.';
           NewCap : Line:='* has become the temporary capital of the empire.';
           EndEmp : Line:='The empire has been conquered.';
           NoFuel : Line:='* is out of fuel.';
           FltDet : Line:='@ fleet has been scanned near *.';
          MinesDm : Line:='* suffered damage from @ SRM field.';
          MinesDs : Line:='* was destroyed in @ SRM field.';
            Mines : Line:='@ fleet damaged in SRM field at *.';
            ConDs : Line:='@ has attacked and destroyed construction at *.';
            GteDs : Line:='Stargate at * has been destroyed by @.';
            LAMDm : Line:='* was damaged by @ LAMs.';
            LAMDs : Line:='* has been destroyed by a @ LAM attack.';
           LAMDef : Line:='* has been hit by @ LAMs.';
       DestDetail : Line:='   '+Parm1N+' '+ThingNames[ResourceTypes(Parm2)]+' destroyed.';
          TrnsShp : Line:='* has received the following resources from @:';
            Trns2 : Line:='   '+Parm1N+' '+ThingNames[ResourceTypes(Parm2)];
        NSellTech : Line:='@ has given the empire '
                          +TechnologyName[TechnologyTypes(Parm2)]+' technology.';
             GInd : Line:='@ has granted this empire the rights to *.';
         NewPlEmp : Line:='The empire of @ has been formed on *.';
             PCap : Line:='Enemy probe from @ destroyed at *.';
            PDest : Line:='Lost contact with probe at *.';
            MessR : Line:='Message received from @.';
            MessI : Line:='* has intercepted a message from @.';
         ConDsUNK : Line:='Construction at * has been destroyed by unknown force.';
         GteDsUNK : Line:='Unknown forces have destroyed stargate at *.';
      BattleW2UNK : Line:='* has been attacked by unknown forces.';
       BattleLUNK : Line:='Lost contact with *.  Presume destroyed.';
        HLPopKill : Line:='Native alien life-forms have killed '+DeathN+' on *.';
        HLMenKill : Line:='Aliens on * attack.  Casualties: '+Parm1N+' men, '+Parm2N+' ninja.';
           HLJoin : Line:=Parm1N+' alien legions have joined imperial forces on *.';
          BseFuel : Line:='* is out of trillum.';
       BseBlocked : Line:='* blocked in transit.';
         SRMClear : Line:='SRMs at * have been cleared by @.';
   OrdersSRMClear : Line:='* sweeped SRMs as ordered.';
     OrdersNoSRMs : Line:='SRM sweep by * failed - no SRMs found.';
      OrdersNoSSP : Line:='SRM sweep by * failed - not enough starships.';
       FltBlocked : Line:='* blocked by dense nebula.';
          NebGate : Line:='* unable to gate to inpenetrable nebula.';
            BseSD : Line:='@ has destroyed *.';
            FltSD : Line:='* destroyed by explosion.';
            WHolo : Line:='Population centers on * have been bombarded by @.';
          DthHolo : Line:='   '+DeathN+' people were killed in the attack.';
          Disrupt : Line:='* has been stopped by @ disrupter.';
         NoTriRes : Line:='All trillum deposits on * have been exhausted.';
      TriResWarn1 : Line:='Trillum deposits on * are nearly depleted.';
      TriResWarn2 : Line:='Trillum deposits on * are very low.';
         MilitRev : Line:='Demonstrations on * call for removal of imperial troops.';
       RevControl : Line:='Imperial troops on * disband angry rioters.';
          GLBDest : Line:='@ has attacked * ('+EmpireName(Empire(Parm2))+'). Attack Failed.';
          GLBConq : Line:='@ has conquered * ('+EmpireName(Empire(Parm2))+').';
       GLBCapConq : Line:='@ has attacked and conquered the '+EmpireName(Empire(Parm2))+' capital.';
       GLBLAMStrk : Line:='@ has hit * ('+EmpireName(Empire(Parm2))+') with LAMs.';
           GLBRev : Line:='* has declared independence from @.';
	 OutProbe : Line:='* has been scouted by outpost scanners.';
	 TerChaos : Line:='Terraforming on * catastrophically failed.';
       TerSuccess : Line:='Terraforming on * has been completed successfully.'
   END;  { case }

   StringReplace(Line,'*',LocN);
   StringReplace(Line,'@',EmpN);
   END;  { GetNewsLine }

(* MISCELLANEOUS Procedures ------------------------------------------------- *)

PROCEDURE GetNearestWorlds{XY: XYCoord; N: Integer; SetToConsider: PlanetSet;
                           VAR NearWorld: IDPtr};
{ GetNearestWorlds:
   This procedure returns a list of N active planets in order of their
   distance from the point XY.  If there is a planet at XY, that object is
   returned first. NearWorld must be initialized.
   TESTED: 7/1/87. }

   VAR
      Bucket: ARRAY [0..MaxSizeOfGalaxy] OF IDPtr;
      i,Dist: Byte;
      NoSoFar: Integer;
      PlnID: IDNumber;
      PlnXY: XYCoord;
      NextID: IDPtr;

   BEGIN
   FOR i:=0 TO SizeOfGalaxy DO
       Bucket[i]:=Nil;

   { sort all planets into buckets by distance }
   PlnID.ObjTyp:=Pln;
   FOR i:=1 TO NoOfPlanets DO
      IF i IN SetToConsider THEN
         BEGIN
         PlnID.Index:=i;
         GetCoord(PlnID,PlnXY);
         Dist:=Distance(PlnXY,XY);
         IF Dist<=SizeOfGalaxy THEN
            AddIDCell(Bucket[Dist],PlnID);
         END;

   { merge all buckets into NearWorld and dispose of Bucket array. }
   NoSoFar:=0;
   FOR i:=0 TO SizeOfGalaxy DO
      BEGIN
      NextID:=Bucket[i];
      WHILE (NextID<>Nil) AND (NoSoFar<N) DO
         BEGIN
         AddIDCell(NearWorld,NextID^.ID);
         NextID:=NextID^.Next;
         Inc(NoSoFar);
         END;

      DisposeIDList(Bucket[i]);
      END;
   END;  { GetNearestWorlds }

PROCEDURE GetOptimumIndus(Obj: IDNumber; VAR Indus: IndusArray);
   VAR
      TIP: IndusIndex;
      IndusDist: IndusDistArray;
      IndI: IndusTypes;
      Pop: Population;
      Class: WorldClass;
      Tech: TechLevel;
      Special: SetOfSpecialConditions;

   BEGIN
   GetIndustrialDistribution(Obj,IndusDist);
   Pop:=GetPopulation(Obj);
   Class:=GetClass(Obj);
   Tech:=GetTech(Obj);
   GetSpecial(Obj,Special);

   TIP:=TotalProd(Pop,Tech);
   IF AmbAddict IN Special THEN
      TIP:=Round(TIP*AmbrosiaAdj);

   FOR IndI:=BioInd TO TriInd DO
      Indus[IndI]:=Round(TIP*(IndusDist[IndI]/100)*(ClassIndAdj[Class,IndI]/100));
   END;  { GetOptimumIndus }

PROCEDURE ProbeScout(Emp: Empire; XY: XYCoord);
{ ProbeScout:
   Emp scouts the area around Loc. }

   VAR
      Dir: Directions;
      x,y: Integer;
      TempLoc: XYCoord;
      Obj: IDNumber;
      Loc: Location;
      OtherEmp: Empire;
      NamePtr: NameRecordPtr;
      NameToAdd: String16;
      Cargo: CargoArray;
      ChanceToDestroy: Integer;

   { ProbeScout: MAIN PROCEDURE }
   BEGIN
   IF NOT SameXY(XY,Limbo) THEN
      FOR Dir:=NoDir TO NW DO
         BEGIN
         x:=XY.x+DirX[Dir];
         y:=XY.y+DirY[Dir];
         IF InGalaxy(x,y) THEN
            BEGIN
            TempLoc.x:=x;
            TempLoc.y:=y;
            GetObject(TempLoc,Obj);
            OtherEmp:=GetStatus(Obj);

            IF (NOT Scouted(Emp,Obj)) AND (Obj.ObjTyp<>Void) THEN
               BEGIN
               Loc.ID:=Obj;  Loc.XY:=Limbo;
               GetCargo(Obj,Cargo);
               ChanceToDestroy:=ISqrt(Cargo[men]);
               IF (OtherEmp<>Indep)
                  AND (Rnd(1,100)<ChanceToDestroy) THEN
                  { ASSERT: Probe is destroyed }
                  BEGIN
                  AddNews(Emp,PDest,Loc,0,0,0);
                  AddNews(OtherEmp,PCap,Loc,Ord(Emp),0,0);
                  Exit;
                  END;

               { send news to empire }
               IF (NOT Known(Emp,Obj)) AND (OtherEmp<>Emp) THEN
                  AddNews(Emp,POk,Loc,0,0,0);

               ScoutObject(Emp,Obj);
               END;

            IF GetNebula(TempLoc)=DarkNebula THEN
               Exit;
            END;
         END;  { loop }
   END;  { ProbeScout }

PROCEDURE UpdateProbes(Emp: Empire);
   VAR
      i: Word;

   BEGIN
   FOR i:=1 TO NoOfProbesPerEmpire DO
      WITH Universe^ DO
         WITH EmpireData[Emp].Probe[i] DO
            IF (Status=PInTrans) THEN
               BEGIN
               ProbeScout(Emp,Dest);
               Status:=PReady;
               END;
   END;  { UpdateProbes }

PROCEDURE ProbesReturn(Emp: Empire);
   VAR
      i: Word;

   BEGIN
   FOR i:=1 TO NoOfProbesPerEmpire DO
      WITH Universe^.EmpireData[Emp].Probe[i] DO
         IF Status=PAtDest THEN
            Status:=PReady;
   END;  { ProbesReturn }

FUNCTION InRangeOfStarbase(Emp: Empire; ObjXY: XYCoord): Boolean;
   VAR
      i: Byte;
      BaseID: IDNumber;
      BaseXY: XYCoord;

   BEGIN
   InRangeOfStarbase:=False;

   BaseID.ObjTyp:=Base;
   FOR i:=1 TO MaxNoOfStarbases DO
      IF i IN SetOfStarbasesOf[Emp] THEN
         BEGIN
           BaseID.Index:=i;
           GetCoord(BaseID,BaseXY);
           IF Distance(BaseXY,ObjXY)<6 THEN
             IF GetBaseType(BaseID)<>cmp THEN
               InRangeOfStarbase:=True;
         END;
   END;  { InRangeOfStarbase }

FUNCTION InRangeOfPlanet(Emp: Empire; FTyp: FleetTypes; ObjXY: XYCoord): Boolean;
   VAR
      i: Byte;
      BaseID: IDNumber;
      BaseXY: XYCoord;

   BEGIN
   InRangeOfPlanet:=False;

   BaseID.ObjTyp:=Pln;
   FOR i:=1 TO NoOfPlanets DO
      IF i IN SetOfPlanetsOf[Emp] THEN
         BEGIN
         BaseID.Index:=i;
         GetCoord(BaseID,BaseXY);
         IF (Distance(BaseXY,ObjXY)<=5) 
            AND (NOT (FTyp IN [HKFleet,Penetrator]))
            AND (GetNebula(ObjXY)=NoNeb) THEN
               BEGIN
               InRangeOfPlanet:=True;
               Exit;
               END;
         END;
   END;  { InRangeOfPlanet }

PROCEDURE DetermineIfScouted(Emp: Empire;  Obj: IDNumber);
   VAR
      CapID: IDNumber;
      ObjXY,CapXY: XYCoord;
		Loc: Location;

   BEGIN

   GetCoord(Obj,ObjXY);
   IF Known(Emp,Obj) THEN
     BEGIN
       IF NOT Scouted(Emp,Obj) THEN
     	 BEGIN
      	   GetCapital(Emp,CapID);
	   GetCoord(CapID,CapXY);
           IF GetStatus(Obj)=Emp THEN
             ScoutObject(Emp,Obj)
	   ELSE IF Distance(CapXY,ObjXY)<6 THEN
   	     ScoutObject(Emp,Obj)
      	   ELSE IF InRangeOfStarbase(Emp,ObjXY) THEN
             ScoutObject(Emp,Obj);
	 END; {if not scouted}
     END {if known is true}
   ELSE {i.e. unknown}
       BEGIN
         IF (Rnd(1,2)=1) AND InRangeOfStarbase(Emp,ObjXY) THEN
	   BEGIN
	     Loc.XY:=Limbo;  Loc.ID:=Obj;
	     AddNews(Emp,OutProbe,Loc,0,0,0);
	     ScoutObject(Emp,Obj);
	   END;
       END;
   END;  { DetermineIfScouted }

PROCEDURE ScoutFleets(PlayerEmp: Empire);
{ INTERFACE PROCEDURE ScoutFleets:
   This procedure will set the scout bit on all the active fleets depending
   on whether or not Player has scouted it.  A Player has scouted a fleet
   if:

      - enemy fleet is not an hk fleet and is next to a player world or
           fleet.
      - enemy fleet is in range of a base scan. }

   LABEL
      NextFleet;

   VAR
      j,i: Word;
      Dir: Directions;
      x,y: Integer;
      FltID: IDNumber;
      Loc: Location;
      FTyp: FleetTypes;
      Fleets: FleetSet;

   { ScoutFleets: MAIN PROCEDURE }
   BEGIN
   FltID.ObjTyp:=Flt;

   FOR i:=1 TO MaxNoOfFleets DO
      IF i IN SetOfActiveFleets THEN
         WITH Universe^.Fleet[i]^ DO
            BEGIN
            FltID.Index:=i;
            FTyp:=TypeOfFleet(FltID);
            ScoutedBy:=ScoutedBy-[PlayerEmp];
            KnownBy:=KnownBy-[PlayerEmp];

            { All player fleets are scouted. }
            IF Emp=PlayerEmp THEN
               BEGIN
               ScoutedBy:=ScoutedBy+[PlayerEmp];
               KnownBy:=KnownBy+[PlayerEmp];
               GOTO NextFleet;
               END;

            IF FTyp<>HKFleet THEN
               BEGIN
               { check to see if next to a player world }
               FOR Dir:=NoDir TO NW DO
                  BEGIN
                  x:=XY.x+DirX[Dir];
                  y:=XY.y+DirY[Dir];
                  IF InGalaxy(x,y) THEN
                     WITH Sector[x]^[y] DO
                        BEGIN
                        IF (GetStatus(Obj)=PlayerEmp) OR (PlayerEmp IN Flts) THEN
                           BEGIN
                           ScoutedBy:=ScoutedBy+[PlayerEmp];
                           KnownBy:=KnownBy+[PlayerEmp];
                           Loc.ID:=Obj;  Loc.XY:=Limbo;
                           IF (GetStatus(Obj)<>PlayerEmp) THEN
                              BEGIN
                              j:=MaxNoOfFleets;
                              Loc.XY.x:=x;
                              Loc.XY.y:=y;
                              GetFleets(Loc.XY,Fleets);
                              Fleets:=Fleets*SetOfFleetsOf[PlayerEmp];
                              WHILE (NOT (j IN Fleets)) AND (j>0) DO
                                 Dec(j);
                              Loc.ID.ObjTyp:=Flt;
                              Loc.ID.Index:=j;
                              Loc.XY:=Limbo;
                              END;
                           AddNews(PlayerEmp,FltDet,Loc,Integer(Emp),0,0);
                           GOTO NextFleet;
                           END;
                        END;  { with scope }
                  END;  { dir loop }

               END;  { if }

            { check to see if within base scan }
            IF InRangeOfStarbase(PlayerEmp,XY) THEN
               BEGIN
               ScoutedBy:=ScoutedBy+[PlayerEmp];
               KnownBy:=KnownBy+[PlayerEmp];
               GOTO NextFleet;
               END;

            { check to see if the fleet is detected but not scanned. }
            IF InRangeOfPlanet(PlayerEmp,FTyp,XY) THEN
               BEGIN
               KnownBy:=KnownBy+[PlayerEmp];
               END;

            { if scouted, loop exits here }
            NextFleet:
            END;  { if, with scope, and loop }
   END;  { ScoutFleets }

PROCEDURE ScoutObjects(Emp: Empire);
   VAR
      Obj: IDNumber;
      i: Byte;
      XY: XYCoord;
      ActiveEmpireFleets: FleetSet;

   { ScoutObjects: MAIN PROCEDURE }
   BEGIN
   { Scout sectors around empire territory. }
   Obj.ObjTyp:=Pln;
   FOR i:=1 TO NoOfPlanets DO
      IF i IN SetOfPlanetsOf[Emp] THEN
         BEGIN
         Obj.Index:=i;
         GetCoord(Obj,XY);
         Scout(Emp,XY);
         END;

   { Scout sectors around empire fleets. }
   ActiveEmpireFleets:=SetOfFleetsOf[Emp] * SetOfActiveFleets;
   Obj.ObjTyp:=Flt;
   FOR i:=1 TO MaxNoOfFleets DO
      IF i IN ActiveEmpireFleets THEN
         BEGIN
         Obj.Index:=i;
         GetCoord(Obj,XY);
         Scout(Emp,XY);
         END;

   { see if any objs are within scanning range }
   Obj.ObjTyp:=Pln;
   FOR i:=1 TO NoOfPlanets DO
      BEGIN
      Obj.Index:=i;

      DetermineIfScouted(Emp,Obj);
      END;  { loop }

   Obj.ObjTyp:=Base;
   FOR i:=1 TO MaxNoOfStarbases DO
      IF i IN SetOfActiveStarbases THEN
         BEGIN
         Obj.Index:=i;

	      DetermineIfScouted(Emp,Obj);
         END;  { loop }

   Obj.ObjTyp:=Gate;
   FOR i:=1 TO MaxNoOfStargates DO
      IF i IN SetOfActiveGates THEN
         BEGIN
         Obj.Index:=i;

         DetermineIfScouted(Emp,Obj);
         END;  { loop }
   END;  { ScoutObjects }

PROCEDURE DestroyEmpire(Emp: Empire);
{ DestroyEmpire: ---------------------------------------------------------------
   This procedure will delete an empire, turning all worlds free and destroying
   all fleets.
------------------------------------------------------------------------------ }
   VAR
      Ground,ID: IDNumber;
      XY: XYCoord;
      SetOfFleets: FleetSet;

   BEGIN
   { All worlds are independent }
   ID.ObjTyp:=Pln;
   FOR ID.Index:=1 TO NoOfPlanets DO
      IF GetStatus(ID)=Emp THEN
         BEGIN
         SetStatus(ID,Indep);
         SetType(ID,IndTyp);
         InitializeISSP(ID);
         END;

   { All starbases are independent }
   ID.ObjTyp:=Base;
   FOR ID.Index:=1 TO MaxNoOfStarbases DO
      IF ID.Index IN SetOfStarbasesOf[Emp] THEN
         SetStatus(ID,Indep);

   { Abort all fleets }
   ID.ObjTyp:=Flt;
   SetOfFleets:=SetOfFleetsOf[Emp] * SetOfActiveFleets;
   FOR ID.Index:=1 TO MaxNoOfFleets DO
      IF ID.Index IN SetOfFleets THEN
         BEGIN
         GetCoord(ID,XY);
         GetObject(XY,Ground);
         IF NOT SameID(Ground,EmptyQuadrant) THEN
            AbortFleet(ID,Ground,True);
         DestroyFleet(ID);
         END;

   EraseNews(Emp);
   DeleteAllNames(Emp);
   IF (NOT EmpirePlayer(Emp)) THEN
      CleanUpNPE(Emp);

   Universe^.EmpireData[Emp].InUse:=False;
   END;  { DestroyEmpire }

END.
