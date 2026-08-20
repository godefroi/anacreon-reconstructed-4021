(* UPDATE.PAS ------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT Update;

{$IFDEF Overlay}
{$F+}
{$O+}
{$ENDIF}

INTERFACE

USES Strg,
     Crt,                                       { Standard CRT library. }
     Int,                                       { Integer routines. }
     EIO,                                       { Extended IO Library. }
     WND,
     Types,
     Galaxy,
     DataStrc,                                  { Universe data structure. }
     DataCnst,
     News,
     Misc,                                      { Miscellaneous routines. }
     Primintr,                                  { Primitive procedures. }
     Intrface,                                  { Interface routines. }
     Mess;

PROCEDURE UpdateUniverse;

IMPLEMENTATION

USES
   Environ;

VAR
   NewTotalRevIndex: ARRAY [Empire] of Integer;

PROCEDURE ReportPlanetLack(Sta: Empire; ID: IDNumber; 
                           Headline: NewsTypes; P1,P2,P3: Integer;
                           VAR OtherReports: ResourceSet);
   VAR
      Loc: Location;

   BEGIN
   IF (NOT (ResourceTypes(P1) IN OtherReports))
   AND (Sta<>Indep) THEN
      BEGIN
      Loc.ID:=ID;
      Loc.XY:=Limbo;

      AddNews(Sta,Headline,Loc,P1,P2,P3);
      OtherReports:=OtherReports+[ResourceTypes(P1)];
      ChangeRevIndex(ID,1);
      END;
   END;  { ReportPlanetLack }

PROCEDURE ConstructStarbase(Emp: Empire; STyp: StarbaseTypes; XY: XYCoord; VAR ID: IDNumber);
   VAR
      EmpTech: TechLevel;
      EmpTechnology: TechnologySet;
      Indus: IndusArray;

   BEGIN
   ID.ObjTyp:=Base;
   ID.Index:=NextStarbaseSlot;
   IF ID.Index>0 THEN
      BEGIN
      CreateStarbase(ID,Emp,XY,STyp);
      SetEfficiency(ID,Rnd(10,20));
      GetEmpireTechnology(Emp,EmpTech,EmpTechnology);
      SetTech(ID,EmpTech);

      IF STyp=out THEN
         BEGIN
         SetPopulation(ID,1);
         SetType(ID,OutTyp);
         END
      ELSE IF STyp=cmp THEN
         BEGIN
         SetPopulation(ID,Rnd(400,700));
         SetType(ID,BseTyp);
         GetOptimumIndus(ID,Indus);
         PutIndus(ID,Indus);
         END
      ELSE
         BEGIN
         SetPopulation(ID,Rnd(10,20));
         SetType(ID,BseTyp);
         END;
      END;
   END;  { ConstructStarbase }

PROCEDURE ConstructStargate(Emp: Empire; GTyp: StargateTypes; XY: XYCoord; VAR ID: IDNumber);
   BEGIN
   ID.ObjTyp:=Gate;
   ID.Index:=NextStargateSlot;
   IF ID.Index>0 THEN
      CreateStargate(ID,Emp,GTyp,XY);
   END;  { ConstructStargate }

{ MAIN PROCEDURES }
PROCEDURE UpdateConstruction(i: Byte);
{ INTERFACE PROCEDURE: UpdateConstruction: }   
   VAR
      ConstrFleets: FleetSet;
      EnoughRawMaterial,Error: Boolean;
      ID: IDnumber;
      Loc: Location;
      ConName: String16;
      ConNamePtr: NameRecordPtr;

   procedure UseUpRawMaterial(Typ: ConstrTypes;
                              Sta: Empire;
                              ConID: IDNumber;
                              ConstrFleets: FleetSet;
                              var EnoughRawMaterial: Boolean);
      label
         ExitLoop;

      var
         i: Byte;
         ThgI: CargoTypes;
         ID: IDNumber;
         Cargo: array[1..MaxNoOfFleets] of CargoArray;
         CargoToUse,RawNeeded: Resources;
         Loc: Location;

      begin
      EnoughRawMaterial:=True;
      FOR i:=1 TO MaxNoOfFleets DO
         IF i IN ConstrFleets THEN
            BEGIN
            ID.ObjTyp:=Flt;  ID.Index:=i;
            GetCargo(ID,Cargo[i]);
            END;

      for ThgI:=amb TO tri do
         begin
         RawNeeded:=ConsCargoNeeded[Typ,ThgI];
         for i:=1 to MaxNoOfFleets do
            begin
            if i in ConstrFleets then
               begin
               CargoToUse:=LesserInt(RawNeeded,Cargo[i][ThgI]);
               Cargo[i][ThgI]:=Cargo[i][ThgI]-CargoToUse;
               RawNeeded:=RawNeeded-CargoToUse;
               end;
            end;

         if RawNeeded>0 then
            begin
            EnoughRawMaterial:=False;
            Loc.ID:=ConID;  Loc.XY:=Limbo;
            AddNews(Sta,ConsLack,Loc,Integer(ThgI),0,0);
            goto ExitLoop;
            end;
         end;

      { if loop finishes, then enough material.  Update fleet cargo. }
      for i:=1 to MaxNoOfFleets do
         if i in ConstrFleets then
            begin
            ID.ObjTyp:=Flt;  ID.Index:=i;
            PutCargo(ID,Cargo[i]);
            end;

      { if loop ends here, then not enough material to build }
      ExitLoop:
      end;  { UseUpRawMaterial }

   { UpdateConstruction: MAIN PROCEDURE }
   BEGIN
   WITH Universe^.Constr[i] DO
      BEGIN
      GetFleets(XY,ConstrFleets);
      ConstrFleets:=ConstrFleets * SetOfFleetsOf[Emp];
      ID.ObjTyp:=Con;  ID.Index:=i;
      UseUpRawMaterial(CTyp,Emp,ID,ConstrFleets,EnoughRawMaterial);
      IF EnoughRawMaterial THEN
         BEGIN
         TimeToCompletion:=TimeToCompletion-1;
         IF TimeToCompletion=0 THEN
            BEGIN
            SetOfActiveConstructionSites:=SetOfActiveConstructionSites-[i];
            SetOfConstructionSitesOf[Emp]:=SetOfConstructionSitesOf[Emp]-[i];

            Loc.XY:=Limbo;  Loc.ID:=ID;
            Location2Index(Emp,Loc,ConNamePtr);
            IF ConNamePtr<>Nil THEN
               BEGIN
               GetDefinedName(Emp,ConNamePtr,ConName,Loc);
               DeleteName(Emp,ConName);
               END
            ELSE
               ConName:='';

            Sector[XY.x]^[XY.y].Obj:=EmptyQuadrant;

            Loc.XY:=Limbo;
            CASE CTyp OF
               SRM: PutMine(XY,Emp);
               cmm..out: BEGIN
                  ConstructStarbase(Emp,CTyp,XY,Loc.ID);
                  IF ConName<>'' THEN
                     AddName(Emp,Loc,ConName,Error);
                  END;
               gte..dis: BEGIN
                  ConstructStargate(Emp,CTyp,XY,Loc.ID);
                  IF ConName<>'' THEN
                     AddName(Emp,Loc,ConName,Error);
                  END;
               END;  { case }

            Loc.ID:=EmptyQuadrant;  Loc.XY:=XY;
            AddNews(Emp,ConsDone,Loc,Integer(CTyp),0,0);
            END;
         END;
      END;  { with scope }
   END;  { UpdateConstruction }

PROCEDURE UpdateEmpire(Emp: Empire);

   PROCEDURE NewTechLevel(Emp: Empire);
      VAR
         Tech: TechLevel;
         TechSet: TechnologySet;
         NewTech: TechnologyTypes;
         Chance: Index;
         LabID,ID: IDNumber;
         i: Word;
         Loc: Location;

      PROCEDURE GetChanceForNewTech(Emp: Empire; EmpTech: TechLevel;
                                    VAR TotalChance: Index; VAR LabID: IDNumber);
      { GetChanceForNewTech:
         This procedure returns the per cent chance that the capital will 
         gain a new technology (or increase in level) this year.  University, 
         ruins, etc. are taken into account.  Out of all the places that 
         research is possible, it chooses a random place to be the lab at 
         which the technology was discovered. }

         LABEL
            ExitLoop;

         TYPE
            LabRecord = RECORD
               WorldID: IDNumber;
               Chance: Index;
            END;  { LabRecord }

         VAR
            Lab: ARRAY [1..20] OF LabRecord;
            Roll,i,NoOfLabs: Integer;
            Typ: WorldTypes;
            Cls: WorldClass;
            Tch: TechLevel;
            Eff: Index;
            ID: IDNumber;

         BEGIN
         { get labs }
         NoOfLabs:=0;

         ID.ObjTyp:=Pln;
         FOR i:=1 TO NoOfPlanets DO
            IF i IN SetOfPlanetsOf[Emp] THEN
               IF NoOfLabs<20 THEN
                  BEGIN
                  ID.Index:=i;
                  Typ:=GetType(ID);
                  Cls:=GetClass(ID);
                  Eff:=GetEfficiency(ID);
                  Tch:=GetTech(ID);
                  IF Typ=CapTyp THEN
                     BEGIN
                     NoOfLabs:=NoOfLabs+1;
                     Lab[NoOfLabs].WorldID:=ID;
                     Lab[NoOfLabs].Chance:=Trunc(TechIncCap*Eff/100);
                     END
                  ELSE IF (Typ=RsrTyp) AND (Tch=EmpTech) THEN
                     BEGIN
                     NoOfLabs:=NoOfLabs+1;
                     Lab[NoOfLabs].WorldID:=ID;
                     IF Cls=RnsCls THEN
                        BEGIN
                        Lab[NoOfLabs].Chance:=Trunc(TechIncUnvRns*Eff/100);
                        END
                     ELSE
                        BEGIN
                        Lab[NoOfLabs].Chance:=Trunc(TechIncUnv*Eff/100);
                        END;
                     END
                  ELSE IF (Tch>EmpTech) THEN
                     BEGIN
                     Inc(NoOfLabs);
                     Lab[NoOfLabs].WorldID:=ID;
                     Lab[NoOfLabs].Chance:=Trunc(TechIncUnv*Eff/100);
                     END
                  ELSE IF Cls=RnsCls THEN
                     BEGIN
                     NoOfLabs:=NoOfLabs+1;
                     Lab[NoOfLabs].WorldID:=ID;
                     Lab[NoOfLabs].Chance:=Trunc(TechIncRns*Eff/100);
                     END;
                  END;  { loop and if }

         ID.ObjTyp:=Base;
         FOR i:=1 TO MaxNoOfStarbases DO
            IF i IN SetOfStarbasesOf[Emp] THEN
               IF NoOfLabs<20 THEN
                  BEGIN
                  ID.Index:=i;
                  Typ:=GetType(ID);
                  Eff:=GetEfficiency(ID);
                  Tch:=GetTech(ID);
                  IF Typ=CapTyp THEN
                     BEGIN
                     NoOfLabs:=NoOfLabs+1;
                     Lab[NoOfLabs].WorldID:=ID;
                     Lab[NoOfLabs].Chance:=Trunc(TechIncCap*Eff/100);
                     END
                  ELSE IF (Typ=RsrTyp) AND (Tch=EmpTech) THEN
                     BEGIN
                     NoOfLabs:=NoOfLabs+1;
                     Lab[NoOfLabs].WorldID:=ID;
                     Lab[NoOfLabs].Chance:=Trunc(TechIncUnv*Eff/100);
                     END;
                  END;  { loop and if }

         { get total chance }
         TotalChance:=0;
         FOR i:=1 TO NoOfLabs DO
            WITH Lab[i] DO
               TotalChance:=TotalChance+Chance;

         { get lab }
         Roll:=Rnd(1,TotalChance);
         FOR i:=1 TO NoOfLabs DO
            WITH Lab[i] DO
               IF Roll<=Chance THEN
                  BEGIN
                  LabID:=WorldID;
                  GOTO ExitLoop;
                  END
               ELSE
                  BEGIN
                  Roll:=Roll-Chance;
                  END;

         { loop should never exit here, but nothing bad happens if it does. }
         GetCapital(Emp,LabID);

         ExitLoop:

         END;  { GetChanceForNewTech }

      PROCEDURE GetNewTech(TechSet,PossibleTechSet: TechnologySet;
                           VAR NewTech: TechnologyTypes);
      { GetNewTech:
         This procedure returns a technology that is in PossibleTechSet but
         not in TechSet.  Returns the answer in NewTech. }

         VAR
            Tech: ARRAY [1..30] OF TechnologyTypes;
            TechNumber: Integer;
            TchI: TechnologyTypes;

         BEGIN
         PossibleTechSet:=PossibleTechSet-TechSet;

         TechNumber:=0;
         FOR TchI:=LAM TO ter DO
            IF TchI IN PossibleTechSet THEN
               BEGIN
               TechNumber:=TechNumber+1;
               Tech[TechNumber]:=TchI;
               END;

         NewTech:=Tech[Rnd(1,TechNumber)];
         END;  { GetNewTech }

      { NewTechLevel: MAIN PROCEDURE }
      BEGIN
      GetEmpireTechnology(Emp,Tech,TechSet);
      IF TechSet<>TechDev[GteTchLvl] THEN
         BEGIN
         IF TechSet<>TechDev[Tech] THEN
            { new technology }
            BEGIN
            GetChanceForNewTech(Emp,Tech,Chance,LabID);
            IF Rnd(1,100)<=Chance THEN
               BEGIN
               GetNewTech(TechSet,TechDev[Tech],NewTech);
               TechSet:=TechSet+[NewTech];
               SetEmpireTechnology(Emp,Tech,TechSet);

               Loc.ID:=LabID;  Loc.XY:=Limbo;
               AddNews(Emp,NCapTech,Loc,Ord(NewTech),0,0);
               END;  { if }
            END
         ELSE
            { new tech level }
            BEGIN
            GetChanceForNewTech(Emp,Tech,Chance,LabID);
            IF Rnd(1,100)<=Chance THEN
               BEGIN
               Tech:=Succ(Tech);
               SetEmpireTechnology(Emp,Tech,TechSet);
               SetTech(LabID,Tech);

               { Make all other university worlds go up also. }
               ID.ObjTyp:=Pln;
               FOR i:=1 TO NoOfPlanets DO
                  IF i IN SetOfPlanetsOf[Emp] THEN
                     BEGIN
                     ID.Index:=i;
                     IF (GetType(ID) IN [RsrTyp,CapTyp])
                        AND (GetTech(ID)=Pred(Tech)) THEN
                        SetTech(ID,Tech);
                     END;

               Loc.ID:=LabID;  Loc.XY:=Limbo;
               AddNews(Emp,NCapLvl,Loc,Ord(Tech),0,0);
               END;  { if }
            END;  { if }
         END;  { if }
      END;  { NewTechLevel }

   { UpdateEmpire: MAIN PROCEDURE }
   BEGIN
   SetTotalRevIndex(Emp,NewTotalRevIndex[Emp]);
   NewTechLevel(Emp)
   END;  { UpdateEmpire }

PROCEDURE GetTotalCargo(WorldID: IDNumber; VAR Cargo: TotalCargoArray);
   VAR
      CarI: CargoTypes;
      Cr: CargoArray;

   BEGIN
   GetCargo(WorldID,Cr);
   FOR CarI:=men TO tri DO
      Cargo[CarI]:=Cr[CarI];
   END;  { GetTotalCargo }

PROCEDURE PutTotalCargo(WorldID: IDNumber; VAR Cargo: TotalCargoArray);
   VAR
      CarI: CargoTypes;
      Cr: CargoArray;

   BEGIN
   FOR CarI:=men TO tri DO
      Cr[CarI]:=ThgLmt(Cargo[CarI]);
   PutCargo(WorldID,Cr);
   END;  { PutTotalCargo }

PROCEDURE HostileLife(WorldID: IDNumber);
{ HostileLife: -----------------------------------------------------------------
   This procedure will implement the special characteristics of hostile life
   worlds.  It should only be called for worlds of class hostile life.
------------------------------------------------------------------------------ }
   VAR
      Eff: Index;
      Pop: Population;
      Cargo: CargoArray;
      ChanceOfAttack: Word;
      MenAdj: Real;
      RevI: Index;
      Loc: Location;
      Aliens,PopKilled,MenKilled,NnjKilled: Word;
      Emp: Empire;

   BEGIN
   Eff:=GetEfficiency(WorldID);
   Pop:=GetPopulation(WorldID);
   GetCargo(WorldID,Cargo);
   MenAdj:=(Eff+50)*((Cargo[men]+5*Cargo[nnj])/100);
   ChanceOfAttack:=GreaterInt(0,25-Round((MenAdj-2000)/100));
   Emp:=GetStatus(WorldID);
   Loc.ID:=WorldID;
   Loc.XY:=Limbo;

   { Aliens attack }
   IF Rnd(1,100)<=ChanceOfAttack THEN
      BEGIN
      IF Rnd(1,100)<=25 THEN
         { ASSERT: Population killed. }
         BEGIN
         PopKilled:=LesserInt(Pop,Rnd(10,50));
         Pop:=Pop-PopKilled;
         ChangeRevIndex(WorldID,Rnd(5,15));
         SetPopulation(WorldID,Pop);
         AddNews(Emp,HLPopKill,Loc,PopKilled,0,0);
         END
      ELSE
         { ASSERT: Troops killed. }
         BEGIN
         MenKilled:=LesserInt(Cargo[men],Rnd(200,300));
         NnjKilled:=LesserInt(Cargo[nnj],Rnd(20,50));
         Dec(Cargo[men],MenKilled);
         Dec(Cargo[nnj],NnjKilled);
         PutCargo(WorldID,Cargo);
         AddNews(Emp,HLMenKill,Loc,MenKilled,NnjKilled,0);
         END;
      END
   ELSE IF (Emp<>Indep) AND (Rnd(1,100)<=20) THEN
      { ASSERT: Aliens join world }
      BEGIN
      Aliens:=Rnd(50,150);
      Cargo[nnj]:=Cargo[nnj]+Aliens;
      PutCargo(WorldID,Cargo);
      AddNews(Emp,HLJoin,Loc,Aliens,0,0);
      END;
   END;  { HostileLife }

PROCEDURE SupplyLink(BaseID: IDNumber; VAR TempCargo: TotalCargoArray);
{ SupplyLink: ------------------------------------------------------------------
   This procedure will take raw materials and supplies from any worlds that are
   nearby.
------------------------------------------------------------------------------ }
   VAR
      BaseXY,XY: XYCoord;
      x,y: Integer;
      Dir: Directions;
      RawMineID: IDNumber;
      Cargo,CargoTrans,BaseCargo: CargoArray;
      RawI: CargoTypes;
      Emp: Empire;

   BEGIN
   GetCoord(BaseID,BaseXY);
   Emp:=GetStatus(BaseID);
   FOR Dir:=No TO NW DO
      BEGIN
      x:=BaseXY.x+DirX[Dir];  y:=BaseXY.y+DirY[Dir];
      IF InGalaxy(x,y) THEN
         BEGIN
         XY.x:=x;  XY.y:=Y;
         GetObject(XY,RawMineID);
         IF (RawMineID.ObjTyp=Pln) AND (GetStatus(RawMineID)=Emp)
            AND (GetType(RawMineID) IN [AgrTyp,CheTyp,MinTyp,RawTyp,TriTyp]) THEN
            BEGIN
            GetCargo(RawMineID,Cargo);
            FOR RawI:=che TO tri DO
               BEGIN
               IF Cargo[RawI]>250 THEN
                  CargoTrans[RawI]:=Cargo[RawI]-Rnd(200,250)
               ELSE
                  CargoTrans[RawI]:=0;

               Cargo[RawI]:=Cargo[RawI]-CargoTrans[RawI];
               TempCargo[RawI]:=TempCargo[RawI]+CargoTrans[RawI];
               END;

            PutCargo(RawMineID,Cargo);
            END;
         END;
      END;  { loop }
   END;  { SupplyLink }

PROCEDURE SurplusLink(BaseID: IDNumber; VAR TempCargo: TotalCargoArray);
{ SupplyLink: ------------------------------------------------------------------
   This procedure will return raw materials and supplies from a base to
   any raw material worlds nearby.
------------------------------------------------------------------------------ }
   VAR
      BaseXY,XY: XYCoord;
      x,y: Integer;
      Dir: Directions;
      RawMineID: IDNumber;
      CargoTrans: Word;
      Cargo: CargoArray;
      RawI: CargoTypes;
      Emp: Empire;

   BEGIN
   GetCoord(BaseID,BaseXY);
   Emp:=GetStatus(BaseID);
   FOR Dir:=No TO NW DO
      BEGIN
      x:=BaseXY.x+DirX[Dir];  y:=BaseXY.y+DirY[Dir];
      IF InGalaxy(x,y) THEN
         BEGIN
         XY.x:=x;  XY.y:=Y;
         GetObject(XY,RawMineID);
         IF (RawMineID.ObjTyp=Pln) AND (GetStatus(RawMineID)=Emp)
            AND (GetType(RawMineID) IN [AgrTyp,CheTyp,MinTyp,RawTyp,TriTyp]) THEN
            BEGIN
            GetCargo(RawMineID,Cargo);
            FOR RawI:=che TO tri DO
               IF TempCargo[RawI]>MaxResources THEN
                  BEGIN
                  CargoTrans:=LesserInt(MaxResources-Cargo[RawI],TempCargo[RawI]-MaxResources);

                  Cargo[RawI]:=Cargo[RawI]+CargoTrans;
                  TempCargo[RawI]:=TempCargo[RawI]-CargoTrans;
                  END;

            PutCargo(RawMineID,Cargo);
            END;
         END;
      END;  { loop }
   END;  { SurplusLink }

PROCEDURE UpdateMilitary(WorldID: IDNumber;
                         Typ: WorldTypes;
                         Pop: Population;
                         VAR MPop: Resources);
   VAR
      OptimumMilitary: Resources;

   BEGIN
   OptimumMilitary:=ThgLmt(RndVar(Round((Pop/150)*OptMilitary[Typ]),10));
   IF OptimumMilitary>MPop THEN
      MPop:=ThgLmt(MPop+(Pop/10)*(OptMilitary[Typ]/100));
   END;  { UpdateMilitary }

PROCEDURE Rebellion(WorldID: IDNumber;
                    RevIndex: Index;
                    Military: Resources);

   VAR
      MenLost,Lost: Word;
      Rebels: Resources;
      ChanceToEndRebel: Real;
      Cargo: CargoArray;
      Loc: Location;
      Emp: Empire;
      Pop: Population;
      Eff: Index;

   { Rebellion: MAIN PROCEDURE }
   BEGIN
   Pop:=GetPopulation(WorldID);
   Eff:=GetEfficiency(WorldID);
   Emp:=GetStatus(WorldID);
   GetCargo(WorldID,Cargo);
   Loc.ID:=WorldID;  Loc.XY:=Limbo;

   Rebels:=GreaterInt(1,ThgLmt(Sqrt(Pop)*65));
   MenLost:=Rebels DIV 5;
   ChanceToEndRebel:=(Military/Sqrt(Rebels))*1.414213;

   { Military men lost }
   Lost:=LesserInt(Cargo[men],MenLost);
   Dec(Cargo[men],Lost);
   Dec(MenLost,Lost);
   Lost:=LesserInt(Cargo[nnj],MenLost DIV 5);
   Dec(Cargo[nnj],Lost);

   IF Rnd(1,100)<ChanceToEndRebel THEN
      { ASSERT: Empire puts down rebellion }
      BEGIN
      ChangeRevIndex(WorldID,Rnd(-15,5));
      AddNews(Emp,URebel,Loc,MenLost,0,0);
      NewTotalRevIndex[Emp]:=NewTotalRevIndex[Emp]-Rnd(1,5);
      END
   ELSE
      { ASSERT: World rebels }
      BEGIN
      SetStatus(WorldID,Indep);
      SetType(WorldID,IndTyp);
      InitializeISSP(WorldID);
      ChangeRevIndex(WorldID,-Rnd(40,50));
      AddGlobalNews([Emp],WorldID,GLBRev,Loc,Ord(Emp),0,0);
      AddNews(Emp,Rebel,Loc,0,0,0);

      Cargo[men]:=ThgLmt(Rebels);

      NewTotalRevIndex[Emp]:=NewTotalRevIndex[Emp]+Rnd(5,10);
      END;

   PutCargo(WorldID,Cargo);
   Pop:=ThgLmt(Pop-Military/1000);
   SetPopulation(WorldID,Pop);
   Eff:=GreaterInt(0,Eff-Rnd(5,15));
   SetEfficiency(WorldID,Eff);
   END;  { Rebellion }

PROCEDURE UpdateRevolution(WorldID: IDNumber);
   VAR
      Emp: Empire;
      Pop: Population;
      Typ: WorldTypes;
      Cargo: CargoArray;
      Loc: Location;
      EmpRevAdj,Factor: Integer;
      RevIndex: Index;
      Military,OptimumMilitary: Resources;

   { UpdateRevolution: MAIN PROCEDURE }
   BEGIN
   Emp:=GetStatus(WorldID);
   Pop:=GetPopulation(WorldID);
   Typ:=GetType(WorldID);

   { Decrease revolution index }
   EmpRevAdj:=RndVar(TotalRevIndex(Emp),50);
   IF Typ=CapTyp THEN
      ChangeRevIndex(WorldID,-Rnd(20,30))
   ELSE
      ChangeRevIndex(WorldID,EmpRevAdj+Rnd(-5,2));

   IF Emp<>Indep THEN
      { ASSERT: World is not independent, chance of rebellion exists. }
      BEGIN
      RevIndex:=GetRevIndex(WorldID);
      GetCargo(WorldID,Cargo);
      Loc.ID:=WorldID;  Loc.XY:=Limbo;

      { Military presence affects revolution }
      OptimumMilitary:=ThgLmt(RndVar(Round((Pop/150)*OptMilitary[Typ]),10));
      Military:=ThgLmt(Cargo[men]+5.0*Cargo[nnj]);
      IF Military>OptimumMilitary THEN
         { ASSERT: Too many troops }
         BEGIN
         IF RevIndex>30 THEN
            { ASSERT: Troops calm rebellion }
            BEGIN
            Factor:=Rnd(1,(Military-OptimumMilitary) DIV 100);
            ChangeRevIndex(WorldID,-Factor);
            IF Factor>5 THEN
               AddNews(Emp,RevControl,Loc,0,0,0);
            END
         ELSE IF (Typ<>CapTyp) AND (Typ<>BseTyp) THEN
            { ASSERT: Too many troops cause resentment }
            BEGIN
            IF Rnd(1,5)=1 THEN
               BEGIN
               ChangeRevIndex(WorldID,Rnd(5,15));
               AddNews(Emp,MilitRev,Loc,0,0,0);
               END;
            END;
         END;

      RevIndex:=GetRevIndex(WorldID);

      IF RevIndex>75 THEN
         BEGIN
         IF (Rnd(1,100)<RevIndex) AND (Typ<>CapTyp) THEN
            Rebellion(WorldID,RevIndex,Military)
         ELSE
            AddNews(Emp,RebelW4,Loc,0,0,0);
         END
      ELSE IF RevIndex>70 THEN
         AddNews(Emp,RebelW4,Loc,0,0,0)
      ELSE IF RevIndex>66 THEN
         AddNews(Emp,RebelW3,Loc,0,0,0)
      ELSE IF RevIndex>43 THEN
         AddNews(Emp,RebelW2,Loc,0,0,0)
      ELSE IF RevIndex>30 THEN
         AddNews(Emp,RebelW1,Loc,0,0,0);
      END;
   END;  { UpdateRevolution }

PROCEDURE ProduceTrillum(WorldID: IDNumber; VAR TriProd: Resources; TriAvail: LongInt);
{ ProduceTrillum: --------------------------------------------------------------
   Decrements the trillum reserves on the world by the amount indicated by
   TriProd. If trillum reserves are low, the procedure will decrease the
   amount produced and send out warnings.
------------------------------------------------------------------------------ }
   VAR
      TriReserves: Word;
      Loc: Location;
      Emp: Empire;

   BEGIN
   TriReserves:=TrillumReserves(WorldID);
   Loc.ID:=WorldID;  Loc.XY:=Limbo;
   Emp:=GetStatus(WorldID);

   TriAvail:=LesserInt(TriAvail,MaxResources);
   TriProd:=LesserInt(TriProd,MaxResources-TriAvail);

   IF TriReserves=0 THEN
      BEGIN
      TriProd:=0;
      AddNews(Emp,NoTriRes,Loc,0,0,0);
      ChangeRevIndex(WorldID,Rnd(10,20));
      END
   ELSE IF (TriReserves*LongInt(20))<TriProd THEN
      BEGIN
      AddNews(Emp,TriResWarn1,Loc,0,0,0);
      ChangeRevIndex(WorldID,Rnd(5,10));
      END
   ELSE IF ((TriReserves*LongInt(10))<TriProd) AND (Rnd(1,2)=1) THEN
      BEGIN
      AddNews(Emp,TriResWarn2,Loc,0,0,0);
      ChangeRevIndex(WorldID,Rnd(3,5));
      END;

   TriReserves:=GreaterInt(0,Integer(TriReserves)-Round(TriProd/100));
   PutTrillumReserves(WorldID,TriReserves);
   END;  { ProduceTrillum }

PROCEDURE ProduceRawMaterial(ObjID: IDNumber;
                             Indus: IndusArray;
                             Technology: TechnologySet;
                             IP: Real;
                             VAR TempCargo: TotalCargoArray);
{ ProduceRawMaterial:
   Given the industrial array of a world, this procedure will produce
   chem, metals, etc.  TempCargo is updated. }

   var
      Prod: Resources;
      IndI: IndusTypes;
      ThgI: ResourceTypes;
      ProdAdj: Real;

   { ProduceRawMaterial: MAIN PROCEDURE }
   BEGIN
   FOR IndI:=CheInd TO TriInd DO
      IF Indus[IndI]>0 THEN
         BEGIN
         ProdAdj:=IP*Sqr(Indus[IndI]+K4);
         FOR ThgI:=che TO tri DO
            IF (ThgAdj[IndI,ThgI]<>0) AND (ThgI IN Technology) THEN
               BEGIN
               { Prod = no of things produced }
               Prod:=GreaterInt(1,ThgLmt(ProdAdj*ThgAdj[IndI,ThgI]));

               IF ThgI=tri THEN
                  ProduceTrillum(ObjID,Prod,TempCargo[tri]);

               { add new production }
               Inc(TempCargo[ThgI],Prod);
               END;  { if and loop }
         END;  { loop }
   END;  { ProduceRawMaterial }

PROCEDURE UpdateWorld(World: IDNumber);
{ INTERFACE PROCEDURE: UpdateWorld }

   VAR
      IndDist: IndusDistArray;
      OtherReports: ResourceSet;
      ETech: TechLevel;
      Technology: TechnologySet;
      IP: Real;
      TempCargo: TotalCargoArray;

   PROCEDURE Production(ID: IDNumber;
                        Typ: WorldTypes;
                        Indus: IndusArray;
                        Technology: TechnologySet;
                        IP: Real;
                        VAR Ships: ShipArray;
                        VAR Cargo: TotalCargoArray;
                        VAR OtherReports: ResourceSet);
   { Production:
      Given the industrial array of a world, this procedure will produce ships,
      metals, etc.  Ships and Cargo are updated.  Raw materials are used up. }

      VAR
         Prod: Resources;
         IndI: IndusTypes;
         ThgI: ResourceTypes;
         RawI: CargoTypes;
         RawNeeded: ARRAY [CargoTypes] OF Integer;
         InfiniteSource,UsedUp: Resources;
         ProdAdj: Real;

      { Production: MAIN PROCEDURE }
      BEGIN
      FOR IndI:=BioInd TO SYTInd DO
         IF Indus[IndI]>0 THEN
            BEGIN
            ProdAdj:=IP*Sqr(Indus[IndI]+K4);
            FOR ThgI:=fgt TO amb DO
               IF (ThgAdj[IndI,ThgI]<>0) AND (ThgI in Technology) then
                  begin
                  { Prod = no of things produced }
                  Prod:=ThgLmt(ProdAdj*ThgAdj[IndI,ThgI]);

                  { Only ninja type worlds make ninjas }
                  IF (ThgI=nnj) AND (Typ<>NnjTyp) THEN
                     Prod:=0
                  { Only ambrosia type worlds make ambrosia }
                  ELSE IF (ThgI=amb) AND (Typ<>AmbTyp) THEN
                     Prod:=0
                  { Only ambrosia and paradise class worlds make ambrosia }
                  ELSE IF (ThgI=amb) AND (NOT (GetClass(ID) IN [AmbCls,ParCls])) THEN
                     Prod:=0
                  ELSE IF Prod<=0 THEN
                     Prod:=1;

                  { do not produce if already at MaxResources }
                  IF ThgI IN [fgt..trn] THEN
                     Prod:=LesserInt(Prod,MaxResources-Ships[ThgI])
                  ELSE
                     Prod:=LesserInt(Prod,MaxResources-LesserInt(Cargo[ThgI],MaxResources));

                  { adjust Prod if not enough raw material }
                  FOR RawI:=amb TO tri DO
                     IF RawM[ThgI][RawI]>0 THEN
                        BEGIN
                        RawNeeded[RawI]:=ThgLmt(Prod*(RawM[ThgI][RawI]/100));
                        IF RawNeeded[RawI]>Cargo[RawI] THEN
                           BEGIN
                           Prod:=ThgLmt((Cargo[RawI]/RawM[ThgI][RawI])*100);
                           RawNeeded[RawI]:=ThgLmt(Prod*(RawM[ThgI][RawI]/100));
                           IF ID.ObjTyp<>Base THEN
                              ReportPlanetLack(GetStatus(ID),ID,Lack,Integer(RawI),0,0,OtherReports);
                           END;
                        END
                     ELSE
                        RawNeeded[RawI]:=0;

                  { subtract raw material }
                  FOR RawI:=che TO tri DO
                     BEGIN
                     RawNeeded[RawI]:=LesserInt(Cargo[RawI],RawNeeded[RawI]);
                     Dec(Cargo[RawI],RawNeeded[RawI]);
                     END;

                  { add new production }
                  IF ThgI IN [fgt..trn] THEN
                     Ships[ThgI]:=LesserInt(MaxResources,Ships[ThgI]+Prod)
                  ELSE
                     Inc(Cargo[ThgI],Prod);
                  END;  { if and loop }
            END;  { loop }
      END;  { Production }

   PROCEDURE UpdateTerraforming(ID: IDNumber);

     VAR
     Class, TargetClass: WorldClass;
     Loc: Location;

     CONST
     ProbabilityOfChange: ARRAY [WorldClass] OF Word =  { This is the yearly likelihood that }
                                                        { the terraforming will be completed }
{ AmbCls }( 14,
{ ArdCls }  20,
{ ArtCls }  0,
{ BarCls }  20,
{ ClsJ   }  17,
{ ClsK   }  17,
{ ClsL   }  17,
{ ClsM   }  16,
{ DrtCls }  13,
{ EthCls }  20,
{ FstCls }  21,
{ GsGCls }  13,
{ HLfCls }  0,
{ IceCls }  22,
{ JngCls }  21,
{ OcnCls }  18,
{ ParCls }  10,
{ PsnCls }  16,
{ RnsCls }  0,
{ UndCls }  22,
{ TerCls }  0,
{ VlcCls }  17 );

     ProbabilityOfChaos: ARRAY [WorldClass] OF Word =  { This is the yearly likelihood that }
                                                       { the disaster will result           }

{ AmbCls }( 3,
{ ArdCls }  1,
{ ArtCls }  100,
{ BarCls }  1,
{ ClsJ   }  1,
{ ClsK   }  1,
{ ClsL   }  1,
{ ClsM   }  1,
{ DrtCls }  2,
{ EthCls }  2,
{ FstCls }  3,
{ GsGCls }  1,
{ HLfCls }  100,
{ IceCls }  1,
{ JngCls }  3,
{ OcnCls }  3,
{ ParCls }  4,
{ PsnCls }  3,
{ RnsCls }  100,
{ UndCls }  2,
{ TerCls }  100,
{ VlcCls }  4 );

     TargetOfChaos: ARRAY [WorldClass] OF WorldClass =  { This is the class the planet will }
                                                        { become in case of disaster        }
{ AmbCls }( ArdCls,
{ ArdCls }  BarCls,
{ ArtCls }  BarCls,
{ BarCls }  IceCls,
{ ClsJ   }  ArdCls,
{ ClsK   }  IceCls,
{ ClsL   }  ArdCls,
{ ClsM   }  DrtCls,
{ DrtCls }  IceCls,
{ EthCls }  IceCls,
{ FstCls }  DrtCls,
{ GsGCls }  IceCls,
{ HLfCls }  IceCls,
{ IceCls }  UndCls,
{ JngCls }  PsnCls,
{ OcnCls }  IceCls,
{ ParCls }  OcnCls,
{ PsnCls }  BarCls,
{ RnsCls }  IceCls,
{ UndCls }  BarCls,
{ TerCls }  IceCls,
{ VlcCls }  BarCls );

     BEGIN          
     Class:=GetClass(ID);        
     IF (Class = TerCls) THEN
       BEGIN
         Loc.ID:=ID;
	 GetCoord(ID,Loc.XY);
         TargetClass:=GetTerraformTarget(ID);
         IF Rnd(1,100) <= ProbabilityOfChaos[TargetClass] THEN      { The terraforming fails }
           BEGIN
             SetType(ID,IndTyp);
             SetClass(ID,TargetOfChaos[TargetClass]);
             AddNews(Player,TerChaos,Loc,0,0,0);
           END
         ELSE IF Rnd(1,100) <= ProbabilityOfChange[TargetClass] THEN  { Terraforming succeeds }
           BEGIN
	     SetType(ID,IndTyp);
             SetClass(ID,TargetClass);
             AddNews(Player,TerSuccess,Loc,0,0,0);
           END           
	 ELSE                { Terraforming continues }
           BEGIN
             SetPopulation(ID,Round(GetPopulation(ID)/100*Rnd(97,99)));  { Drop pop by 1-3% }
           END;
       END;
     END; { UpdateTerraforming }

   PROCEDURE UpdateIndustry(ID: IDNumber;
                            Cls: WorldClass;
                            Tech: TechLevel;
                            Eff: Index;
                            Pop: Population;
                            AmbAddict: Boolean;
                            VAR IndDist: IndusDistArray;
                            VAR Indus: IndusArray;
                            VAR Cargo: TotalCargoArray;
                            VAR OtherReports: ResourceSet);
   { UpdateIndustry: }

      var
         TIP: Integer;
         OptimumLevel: IndusIndex;
         RawNeeded: Resources;
         IndI: IndusTypes;
         ConsRate: Integer;
         Temp: Real;

      BEGIN
      TIP:=TotalProd(Pop,Tech);
      IF AmbAddict THEN
         TIP:=Round(TIP*AmbrosiaAdj);
      IF TIP>999 THEN
         TIP:=999;

      Temp:=TIP/10000;
      FOR IndI:=BioInd TO TriInd DO
         BEGIN
         OptimumLevel:=Round(Temp*IndDist[IndI]*ClassIndAdj[Cls,IndI]);
         IF (IndDist[IndI]>0) AND (OptimumLevel=0) THEN
            OptimumLevel:=1;
         IF Indus[IndI]<OptimumLevel THEN
            BEGIN
            ConsRate:=Round(OptimumLevel*(Eff/500));
            IF ConsRate<1 THEN
               ConsRate:=1;
            ConsRate:=LesserInt(ConsRate,OptimumLevel-Indus[IndI]);
            RawNeeded:=ThgLmt((ConsRate/100)*NewIndRawN[IndI]);
            IF RawNeeded>Cargo[Met] THEN
               BEGIN
               ConsRate:=Trunc(100*(Cargo[met]/NewIndRawN[IndI]));
               RawNeeded:=Cargo[met];
               ReportPlanetLack(GetStatus(ID),ID,IndLack,Integer(met),0,0,OtherReports);
               END;
            END
         ELSE IF Indus[IndI]>OptimumLevel THEN
            BEGIN
            ConsRate:=-Round(Eff/2);
            IF ConsRate>-1 THEN
               ConsRate:=-1;
            IF Indus[IndI]+ConsRate<OptimumLevel then
               ConsRate:=OptimumLevel-Indus[IndI];
            RawNeeded:=0;
            END
         ELSE
            BEGIN
            ConsRate:=0;
            RawNeeded:=0;
            END;

         IF Indus[IndI]+ConsRate>999 THEN
            BEGIN
            ConsRate:=999-Indus[IndI];
            RawNeeded:=ThgLmt((ConsRate/100)*NewIndRawN[IndI]);
            END
         ELSE IF Indus[IndI]+ConsRate<0 THEN
            BEGIN
            ConsRate:=-Indus[IndI];
            RawNeeded:=0;
            END;

         Indus[IndI]:=Indus[IndI]+ConsRate;

         RawNeeded:=LesserInt(Cargo[met],RawNeeded);
         Dec(Cargo[met],RawNeeded);
         END;  { loop }
      END;  { UpdateIndustry }

   PROCEDURE UpdateEfficiency(Emp: Empire; VAR Eff: Index);
      VAR
         Inc: Index;

      BEGIN
      IF Emp=Indep THEN
         BEGIN
         Inc:=Rnd(0,1);
         END
      ELSE
         BEGIN
         CASE Eff OF
             0..25 : Inc:=Rnd(5,12);
            26..50 : Inc:=Rnd(3,8);
            51..75 : Inc:=Rnd(2,5);
            76..90 : Inc:=Rnd(0,3);
            91..99 : Inc:=Rnd(0,1);
         ELSE
            Inc:=0;
         END;  { case }
         END;

      Eff:=LesserInt(100,Eff+Inc);
      END;  { UpdateEfficiency }

   PROCEDURE UpdateTechLevel(ID: IDNumber; Emp: Empire;
                             VAR Tech: TechLevel);
      VAR
         Loc: Location;
         CapitalTech: TechLevel;
         CapitalID: IDNumber;

      BEGIN
      IF Tech<>GteTchLvl THEN
         BEGIN
         IF Emp=Indep THEN
            BEGIN
            IF Rnd(1,50)=1 THEN
               Tech:=Succ(Tech);
            END
         ELSE
            BEGIN
            GetCapital(Emp,CapitalID);
            CapitalTech:=GetTech(CapitalID);

            IF CapitalTech>Tech THEN
               BEGIN
               IF Rnd(1,100)<=TechLvlInc THEN
                  BEGIN
                  Tech:=Succ(Tech);
                  Loc.ID:=ID;  Loc.XY:=Limbo;
                  AddNews(Emp,NTech,Loc,Integer(Tech),0,0);
                  END;
               END
            ELSE IF CapitalTech<Tech THEN
               BEGIN
               IF Rnd(1,15)=1 THEN
                  BEGIN
                  Tech:=Pred(Tech);
                  Loc.ID:=ID;  Loc.XY:=Limbo;
                  AddNews(Emp,RTech,Loc,Integer(Tech),0,0);
                  END;
               END;
            END;
         END;
      END;  { UpdateTechLevel }

   PROCEDURE UpdatePopulation(Class: WorldClass;
                              Tech: TechLevel;  var Pop: Population);
      CONST
         MaxPop: ARRAY [WorldClass] OF Integer =
         ( 4830,  { AmbCls }
           4100,  { ArdCls }
           3100,  { ArtCls }
           2340,  { BarCls }
           4610,  { ClsJ   }
           4600,  { ClsK   }
           4220,  { ClsL   }
           4800,  { ClsM   }
           3580,  { DrtCls }
           4500,  { EthCls }
           4710,  { FstCls }
           2010,  { GsGCls }
           4010,  { HLfCls }
           1920,  { IceCls }
           4720,  { JngCls }
           3520,  { OcnCls }
           5000,  { ParCls }
           2100,  { PsnCls }
           4590,  { RnsCls }
           4500,  { UndCls }
	    100,  { TerCls }
           3950   { VlcCls } );

      VAR
         Increase: Real;

      BEGIN
      IF Pop>=MaxPop[Class] THEN
         Increase:=Rnd(-10,10)
      ELSE IF Pop<75 THEN
         Increase:=Rnd(2,5)
      ELSE IF Pop>BasePop[Tech] THEN
         { linear growth }
         Increase:=BasePop[Tech]/100
      ELSE
         { exponential growth }
         Increase:=128.0*Pop/(MaxPop[Class]);

      Pop:=Pop+Round(Increase);
      END;  { UpdatePopulation }

   PROCEDURE UseUpFood(WorldID: IDNumber; var Pop: Population;
                       var Food: Resources);

      { This array adjusts for tech level when increasing revolution index.  
        Low tech level planets will be used to starving and will not revolt 
        as quickly as high level planets.  The constant is in units of 
        index points per 100 million people starved. }
      CONST
         TechAdj: ARRAY [TechLevel] OF Integer =
         {  pt   p  pa   a  pw   w   j   b   s  pg   g }
         ( 100,100, 30, 20, 15, 13, 12, 13, 14, 14, 15 );

      VAR
         Loc: Location;
         FoodNeeded,Lack: Resources;
         RevInc,Starve: Integer;
         Tech: TechLevel;

      BEGIN
      FoodNeeded:=ThgLmt((Pop/100)*SuppliesPerBillion);
      IF FoodNeeded>Food THEN
         BEGIN
         Lack:=FoodNeeded-Food;
         Food:=0;
         Starve:=Lack DIV 6;
         IF Starve>(Pop DIV 10) THEN
            Starve:=Pop DIV 10;

         Pop:=Pop-Starve;

         IF Starve>0 THEN
            BEGIN
            Tech:=GetTech(WorldID);
            Loc.ID:=WorldID;  Loc.XY:=Limbo;
            AddNews(GetStatus(WorldID),Starv,Loc,Starve,0,0);
            RevInc:=LesserInt(Trunc(TechAdj[Tech]*(Starve/10)),45);
            ChangeRevIndex(WorldID,RevInc);
            END;
         END
      ELSE
         BEGIN
         Food:=Food-FoodNeeded;
         END;
      END;  { UseUpFood }

   PROCEDURE UseUpAmbrosia(WorldID: IDNumber;
                           Emp: Empire;
                           VAR Special: SetOfSpecialConditions;
                           VAR Pop: Population;
                           VAR Eff: Index;
                           VAR Tech: TechLevel;
                           VAR Indus: IndusArray;
                           VAR Ambrosia: Resources);

      VAR
         AmbNeeded,Lack: Resources;
         Die: Population;
         Loc: Location;
         EffChange,IndDest: Integer;
         IndI: IndusTypes;

      BEGIN
      AmbNeeded:=ThgLmt((Pop/100)*DrugsPerBillion);
      Loc.ID:=WorldID;  Loc.XY:=Limbo;

      IF AmbAddict IN Special THEN
         BEGIN
         IF AmbNeeded<=Ambrosia THEN
            BEGIN
            Ambrosia:=Ambrosia-AmbNeeded;
            END
         ELSE
            { ASSERT: not enough ambrosia }
            BEGIN
            Lack:=AmbNeeded-Ambrosia;
            Ambrosia:=0;

            { PEOPLE DIE }
            Die:=ThgLmt(AddictDeathCoeff*Lack);
            IF Die>(Pop div 7) THEN
               Die:=Pop div 7;

            Pop:=Pop-Die;

            IF Die>0 THEN
               BEGIN
               AddNews(Emp,AddictDie,Loc,Die,0,0);
               END;

            { EFFICIENCY DECREASES }
            EffChange:=Trunc(AddictEffCoeff*Die);
            EffChange:=LesserInt(EffChange,Eff);
            Eff:=Eff-EffChange;

            { REV INDEX INCREASES }
            ChangeRevIndex(WorldID,Trunc(AddictRevICoeff*Die));

            { RANDOM EFFECTS }
            CASE Rnd(1,10) OF
               1..4: BEGIN END;
               5..7: BEGIN
                     Die:=ThgLmt((Rnd(50,120)/100)*Die);
                     IF Die>0 THEN
                        BEGIN
                        Pop:=Pop-Die;
                        AddNews(Emp,RiotsDie,Loc,Die,0,0);
                        END;
                     END;
               8..9: BEGIN
                     FOR IndI:=BioInd TO TriInd DO
                        BEGIN
                        IndDest:=Trunc(Indus[IndI]*Rnd(0,20)/100);
                        Indus[IndI]:=Indus[IndI]-IndDest;
                        IF IndDest>0 THEN
                           AddNews(Emp,IndDs,Loc,IndDest,Integer(IndI),0);
                        END;
                     END;
                 10: BEGIN
                     IF Tech>PreTchLvl THEN
                        Tech:=Pred(Tech);

                     AddNews(Emp,RTech,Loc,Integer(Tech),0,0);
                     END;
            END;  { case }

            { UN-ADDICT }
            IF Rnd(1,100)<=ChanceToAddict THEN
               BEGIN
               Special:=Special-[AmbAddict];
               AddNews(Emp,UAddict,Loc,0,0,0);
               END;  { if }
            END;  { if }
         END
      ELSE
         { ASSERT: world not addicted. }
         BEGIN
         IF Ambrosia>0 THEN
            BEGIN
            IF AmbNeeded<=Ambrosia THEN
               BEGIN
               IF Rnd(1,100)<ChanceToAddict THEN
                  BEGIN
                  Special:=Special+[AmbAddict];
                  AddNews(Emp,WAddict,Loc,0,0,0);
                  END;
               END;

            AmbNeeded:=AmbNeeded DIV 2;
            IF AmbNeeded<=Ambrosia THEN
               BEGIN
               Ambrosia:=Ambrosia-AmbNeeded;
               END
            ELSE
               BEGIN
               Ambrosia:=0;
               END;
            END;
         END;  { if }
      END;  { UseUpAmbrosia }

   PROCEDURE UpdateDefenses(WorldID: IDNumber;
                            Pop: Population;
                            Tech: TechLevel;
                            Typ: WorldTypes;
                            Eff: Index;
                            Technology: TechnologySet;
                            VAR Defns: DefnsArray;
                            VAR Cargo: CargoArray;
                            VAR OtherReports: ResourceSet);
      VAR
         DefI: DefnsTypes;
         MPop: Word;
         UsedUp,InfiniteSource,OptimumDef: Resources;
         Build,MaxBuild,RawNeeded: Resources;
         Car: CargoTypes;
         Optimum,BuildRate: Real;
         BTyp: StarbaseTypes;

      BEGIN
      MPop:=TroopStrength(WorldID);
      BuildRate:=(MPop/2000)*(1+((Eff-50)/100));
      Optimum:=(MPop/100);

      IF WorldID.ObjTyp=Pln THEN
         BuildRate:=BuildRate*(Pop/2000)
      ELSE IF WorldID.ObjTyp=Base THEN
         BEGIN
         BTyp:=GetBaseType(WorldID);
         IF BTyp=out THEN
            Optimum:=Optimum/4
         ELSE IF (BTyp=cmm) OR (BTyp=frt) THEN
            BEGIN
            Optimum:=Optimum*4;
            BuildRate:=BuildRate*4;
            END;
         END;

      FOR DefI:=LAM TO ion DO
         IF DefI IN Technology THEN
            BEGIN
            OptimumDef:=ThgLmt(Optimum*DefAdj[DefI]);
            IF (WorldID.ObjTyp=Base) AND (BTyp=out) AND (DefI=def) THEN
               OptimumDef:=0;

            IF (DefI=LAM) AND (NOT (Typ IN [BseTyp,CapTyp])) THEN
               OptimumDef:=0;

            IF Defns[DefI]<OptimumDef THEN
               BEGIN
               MaxBuild:=GreaterInt(ThgLmt(BuildRate*DefBuildRate[DefI]),1);
               Build:=LesserInt(OptimumDef-Defns[DefI] , MaxBuild);

               FOR Car:=che TO tri DO
                  BEGIN
                  RawNeeded:=ThgLmt((RawM[DefI][Car]/100)*Build);
                  IF RawNeeded>Cargo[Car] THEN
                     BEGIN
                     Build:=ThgLmt((Cargo[Car]/RawM[DefI][Car])*100);
                     ReportPlanetLack(GetStatus(WorldID),WorldID,DefLack,Integer(Car),0,0,OtherReports);
                     END;
                  END;

               FOR Car:=che TO tri DO
                  BEGIN
                  UsedUp:=0;
                  RawNeeded:=ThgLmt(Build*(RawM[DefI][Car]/100));
                  MoveThings(RawNeeded,Cargo[Car],UsedUp);
                  END;

               InfiniteSource:=MaxResources;
               MoveThings(Build,InfiniteSource,Defns[DefI]);
               END;
            END;  { loop }
      end;  { UpdateDefenses }

   { UpdateWorld: MAIN PROCEDURE }
   BEGIN
   IF World.ObjTyp=Pln THEN
      WITH Universe^.Planet[World.Index] DO
         BEGIN
         OtherReports:=[];
         IF Emp=Indep THEN
            BEGIN
            IF Tech>PreTchLvl THEN
               Technology:=TechDev[Pred(Tech)]
            ELSE
               Technology:=TechDev[Tech];
            END
         ELSE BEGIN
            GetEmpireTechnology(Emp,ETech,Technology);
            Technology:=Technology * TechDev[Tech];
            END;

	 { Update terraforming status }
	 UpdateTerraforming(World);         

         { Calculate Industrial Production }
         IP:=(TechAdj2[Tech]/100)*((Eff+250)/100)/K6;

         GetTotalCargo(World,TempCargo);
         ProduceRawMaterial(World,Indus,Technology,IP,TempCargo);
         GetIndustrialDistribution(World,IndDist);
         UpdateIndustry(World,Cls,Tech,Eff,Pop,(AmbAddict IN Special),IndDist,Indus,TempCargo,OtherReports);
         Production(World,Typ,Indus,Technology,IP,Ships,TempCargo,OtherReports);
         PutTotalCargo(World,TempCargo);

         IF Cls<>TerCls THEN
            UpdateEfficiency(Emp,Eff);
         UpdateTechLevel(World,Emp,Tech);
         UpdatePopulation(Cls,Tech,Pop);
         UseUpFood(World,Pop,Cargo[sup]);
         UseUpAmbrosia(World,Emp,Special,Pop,Eff,Tech,Indus,Cargo[amb]);
         UpdateMilitary(World,Typ,Pop,Cargo[men]);
         UpdateDefenses(World,Pop,Tech,Typ,Eff,Technology,Defns,Cargo,OtherReports);
         UpdateRevolution(World);
         IF Cls=HLfCls THEN
            HostileLife(World);
         END  { with scope }
   ELSE IF World.ObjTyp=Base THEN
      WITH Universe^.Starbase[World.Index] DO
         BEGIN
         OtherReports:=[];
         IF Emp=Indep THEN
            Technology:=TechDev[Pred(Tech)]
         ELSE BEGIN
            GetEmpireTechnology(Emp,ETech,Technology);
            Technology:=Technology * TechDev[Tech];
            END;

         IF STyp=cmp THEN
            BEGIN
            IP:=(TechAdj2[Tech]/100)*((Eff+250)/100)/K6;

            GetTotalCargo(World,TempCargo);
            SupplyLink(World,TempCargo);
            ProduceRawMaterial(World,Indus,Technology,IP,TempCargo);
            GetIndustrialDistribution(World,IndDist);
            UpdateIndustry(World,ArtCls,Tech,Eff,Pop,(AmbAddict IN Special),IndDist,Indus,TempCargo,OtherReports);
            Production(World,Typ,Indus,Technology,IP,Ships,TempCargo,OtherReports);
            SurplusLink(World,TempCargo);
            PutTotalCargo(World,TempCargo);
            END;

         UpdateEfficiency(Emp,Eff);
         UpdateTechLevel(World,Emp,Tech);

         IF STyp=cmp THEN
            BEGIN
            UpdatePopulation(ArtCls,Tech,Pop);
            UseUpFood(World,Pop,Cargo[sup]);
            UseUpAmbrosia(World,Emp,Special,Pop,Eff,Tech,Indus,Cargo[amb]);
            UpdateMilitary(World,Typ,Pop,Cargo[men]);
            UpdateRevolution(World);
            END;

         UpdateDefenses(World,Pop,Tech,Typ,Eff,Technology,Defns,Cargo,OtherReports);
         END;  { with scope }
   END;  { UpdateWorld }

PROCEDURE UpdateUniverse;
   VAR
      UpdateWindow: WindowHandle;
      i: Byte;
      Emp: Empire;
      ID: IDNumber;

   { UpdateUniverse: MAIN PROCEDURE }
   BEGIN
   TextAttr:=7;
   ClrScr;
   OpenWindow(20,7,40,7,ThinBRD,'',7,C.SYSWBorder,UpdateWindow);
   FillChar(NewTotalRevIndex,SizeOf(NewTotalRevIndex),0);

   Year:=Year+1;

   { update planets }
   FOR i:=1 TO NoOfPlanets DO
      BEGIN
      WriteString('World #'+Int2Str(i)+'     ',2,1,7);
      ID.ObjTyp:=Pln;  ID.Index:=i;
      UpdateWorld(ID);
      END;

   ClrScr;
   FOR i:=1 TO MaxNoOfStarbases DO
      BEGIN
      WriteString('Starbase #'+Int2Str(i)+'   ',2,1,7);
      IF i IN SetOfActiveStarbases THEN
         BEGIN
         ID.ObjTyp:=Base;  ID.Index:=i;
         UpdateWorld(ID);
         END;
      END;

   { update construction }
   ClrScr;
   FOR i:=1 TO MaxNoOfConstrSites DO
      begin
      WriteString('Construction #'+Int2Str(i)+'     ',2,1,7);
      IF i IN SetOfActiveConstructionSites THEN
         BEGIN
         UpdateConstruction(i);
         END;
      END;

   ClrScr;
   WriteString('Updating Empires...',2,1,7);
   FOR Emp:=Empire1 TO Empire8 do
      IF EmpireActive(Emp) THEN
         UpdateEmpire(Emp);

   DeleteReadMessages;

   CloseWindow;
   END;  { UpdateUniverse }

END.
