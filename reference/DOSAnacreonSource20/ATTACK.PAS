(* ATTACK.PAS ------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT Attack;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Int,
     Types,
     Galaxy,
     DataCnst,
     Misc,
     PrimIntr,
     Intrface,
     DataStrc,
     News;

CONST
   MaxNoOfGroups = 9;

TYPE
   AttackIntentionTypes = ( NoAIT,
                            ConquerAIT,               { conquer world }
                            DestTrnAIT,               { destroy transports }
                            CaptTrnAIT );             { capture transports }

   AttackResultTypes = ( NoART, AttDestroyedART, AttRetreatsART,
                         DefConqueredART, DefCapturedART );

   HoloResultTypes = ( NoHRT, WorldSurrendersHRT, WorldDestroyedHRT );

   AttackArray = ARRAY [AttackTypes] OF Resources;
   TargetArray = array[1..MaxNoOfGroups] of AttackArray;
   { TargetArray is the enemy targetting array.  For each group
     attacking, there is an array contataining the number of
     things attacking each particular group.  Basically this is a
     2D matrix of Group vs AttackType.  Each element indicates how
     many ships of that AttackType are attacking each group. }

   CombatDataRecord = RECORD
      DTyp: ObjectTypes;
      DTech: TechLevel;
      AShipAdj: Integer;
      DShipAdj: Integer;
      DGrndAdj: Integer;
      MaxGDM: Integer;
      RevIndex: Index;
   END;

   GroupStatus = ( GReady,GAdvc,GRtrt,GDst );

   GroupRecord = RECORD
      Typ: AttackTypes;
      Num: Resources;
      Trg: AttackTypes;
      Pos: ShellPos;
      Sta: GroupStatus;
      GAT: Resources;        { no of Ground Assault Troops }
      GATTyp: AttackTypes; { type of Ground Assault Troops }
      TrnTyp: ShipTypes;   { temporary storage for transport type }
      Flg: Boolean;        { misc. flag }
   END;
   GroupArray = ARRAY [1..MaxNoOfGroups] OF GroupRecord;
   DestArray = ARRAY [1..MaxNoOfGroups] OF Resources;
   GroupSet = SET OF 1..MaxNoOfGroups;

   EnemyArray = ARRAY [ShellPos] OF AttackArray;

   DetailArray = ARRAY [AttackTypes,0..MaxNoOfGroups] OF Resources;

CONST
   { Military power of different weapons (for surrender algorithm)
     values are from 1 to 100. }
   CombatPower: ARRAY [AttackTypes] OF Integer =
   { None LAM def GDM ion fgt hkr jmp jtn pen str trn men nnj }
   (    0, 80, 75, 20, 65,  2, 15, 10,  1, 20,100,  1, 20,100 );

PROCEDURE AdvanceGroups(NoOfGroups: Word; VAR Gp: GroupArray);
FUNCTION AllGroupsDestroyed(NoOfGroups: Byte; VAR Gp: GroupArray): Boolean;
PROCEDURE Battle(NoOfGroups: Byte;
                 VAR Gp: GroupArray;
                 VAR GroupsDestroyed: GroupSet;
                 VAR En: EnemyArray;
                 CurPos: ShellPos;
                 CombatData: CombatDataRecord;
                 VAR Details: DetailArray;
                 VAR Casualties,Killed: AttackArray);
PROCEDURE CalculateCombatData(Attacker: Empire; FltID,TargetID: IDNumber;
                              VAR CombatData: CombatDataRecord);
PROCEDURE ConquerWorld(WorldID: IDNumber; Emp: Empire);
PROCEDURE DefaultDistribution(FltID: IDNumber; VAR NoOfGroups: Byte; VAR Gp: GroupArray);
PROCEDURE DestroyConstructionOrGate(Emp: Empire; ForcesUnk: Boolean; TargetID: IDNumber);
FUNCTION EnemySurrenders(NoOfGroups: Byte;
                         VAR Gp: GroupArray;
                         VAR En: EnemyArray;
                         VAR Casualties,Killed: AttackArray;
                         CombatData: CombatDataRecord): Boolean;
FUNCTION ForcesUnknown(FltID,Target: IDNumber): Boolean;
PROCEDURE GetEnemy(Target: IDNumber; VAR En: EnemyArray);
FUNCTION HolocaustEffectiveness(FltID,WorldID: IDNumber): Index;
PROCEDURE HolocaustWorld(Emp: Empire; Effectiveness: Index;
                         WorldID,FltID: IDNumber; VAR Losses: ShipArray;
                         VAR IndusDest: IndusArray; VAR Deaths: Population;
                         VAR Revert: Boolean;
                         VAR Result: HoloResultTypes);
PROCEDURE LAMAttack(Player: Empire; LAMToUse: Resources; Target: IDNumber;
                    VAR ShipsDest: ShipArray;
                    VAR DefnsDest: DefnsArray);
PROCEDURE ResolveAttack(Result: AttackResultTypes;
                        FltID,Target: IDNumber;
                        HKAttack,Capture: Boolean;
                        VAR Casualties,Killed: AttackArray;
                        VAR Booty: PlanetSet);
PROCEDURE RestoreCombatant(ObjID: IDNumber; VAR Casualties: AttackArray);

IMPLEMENTATION

USES
	Fleet;

CONST
   { CombatTechAdj:
     Adjustment of destructive power of AttTech vs DefTech.  100 means
     no adjustment.  >100 means defender at disadvantage.  <100 means attacker
     at disadvantage. }
   CombatTechAdj: ARRAY [TechLevel,TechLevel] OF Integer =
      {  Defender:
           pt    p   pa    a   pw    w    j    b    s   pg    g      Attacker: }

   (   (  100,  90,  50,  25,  10,   5,   3,   1,   1,   1,   1 ),      { pt }
       (  120, 100,  80,  40,  25,  10,   5,   3,   1,   1,   1 ),      { p  }
       (  150, 110, 100,  60,  40,  25,  10,   5,   3,   1,   1 ),      { pa }
       (  160, 140, 130, 100,  60,  40,  30,  20,  10,   5,   3 ),      { a  }
       (  200, 170, 145, 120, 100,  90,  85,  75,  70,  55,  45 ),      { pw }
       (  250, 200, 175, 140, 110, 100,  95,  90,  85,  70,  55 ),      { w  }
       (  280, 210, 190, 175, 115, 110, 100,  97,  93,  80,  70 ),      { j  }
       (  320, 250, 210, 195, 120, 115, 105, 100,  95,  90,  80 ),      { b  }
       (  360, 320, 280, 220, 125, 120, 115, 110, 100,  97,  90 ),      { s  }
       (  500, 400, 300, 250, 130, 125, 120, 115, 110, 100,  95 ),      { pg }
       (  530, 415, 310, 260, 135, 130, 125, 120, 115, 110, 100 ) );    { g  }

   CombatClassAdj: ARRAY [WorldClass] OF Integer =
      ( 100,   { AmbCls }
        100,   { ArdCls }
        100,   { ArtCls }
        50,    { BarCls }
        100,   { ClsJ   }
        100,   { ClsK   }
        100,   { ClsL   }
        100,   { ClsM   }
        140,   { DrtCls }
        100,   { EthCls }
        140,   { FstCls }
        50,    { GsGCls }
        100,   { HLfCls }
        160,   { IceCls }
        150,   { JngCls }
        80,    { OcnCls }
        100,   { ParCls }
        60,    { PsnCls }
        100,   { RnsCls }
        130,   { UndCls }
	100,   { TerCls }
        120 ); { VlcCls }

   CombatBaseAdj: ARRAY [StarbaseTypes] OF Integer =
      ( 150,250,100,125 );

   { No of GDMs launched at one time per tech level }
   GDMLaunch: ARRAY [TechLevel] OF Resources =
      {    pt    p   pa    a   pw    w    j    b    s   pg    g }
      (     0,   0,   0,  20, 217, 662,1013,1261,2163,2644,2759 );

   { No of GDMs killed on approach by ships (per 10 ships) }
   GDMKill: ARRAY [ShipTypes] OF Integer =
      { fgt hkr jmp jtn pen ssp trn }
      (   0,  3,  2,  1, 15, 20,  0 );

PROCEDURE AdvanceGroups(NoOfGroups: Word; VAR Gp: GroupArray);
   VAR
      i,temp: Integer;

   BEGIN
   FOR i:=1 TO NoOfGroups DO
      WITH Gp[i] DO
         BEGIN
         IF Sta=GAdvc THEN
            BEGIN
            Pos:=Succ(Pos);
            Sta:=GReady;
            IF Pos=Grnd THEN
               BEGIN
               IF GAT<>0 THEN
                  { if GATs in group, begin combat }
                  BEGIN
                  temp:=Num;
                  Num:=GAT;
                  GAT:=temp;
                  TrnTyp:=Typ;
                  Typ:=GATTyp;
                  Trg:=Men;
                  END;
               END;
            END
         ELSE IF Sta=GRtrt THEN
            BEGIN
            Pos:=Pred(Pos);
            Sta:=GReady;
            END;
         END;  { with scope and loop }
   END;  { AdvanceGroups }

FUNCTION AllGroupsDestroyed(NoOfGroups: Byte; VAR Gp: GroupArray): Boolean;
   VAR
      i: Word;

   BEGIN
   i:=NoOfGroups;
   WHILE (i>0) AND (Gp[i].Sta=GDst) DO
      Dec(i);

   IF i=0 THEN
      AllGroupsDestroyed:=True
   ELSE
      AllGroupsDestroyed:=False;
   END;  { AllGroupsDestroyed }

FUNCTION EnemySurrenders(NoOfGroups: Byte;
                         VAR Gp: GroupArray;
                         VAR En: EnemyArray;
                         VAR Casualties,Killed: AttackArray;
                         CombatData: CombatDataRecord): Boolean;
   VAR
      PlGAT,EnMen,AtShPow,PlShPow,EnShPow,AttA,DefA: Real;

   PROCEDURE GetForces;
      VAR
         i: Integer;
         PosI: ShellPos;
         ShpI: AttackTypes;
         TechAdj: Real;
         Temp,AttKilled,DefKilled: Real;

      BEGIN
      PlGAT:=0;         { attacking troops }
      PlShPow:=0;       { attacking forces }
      EnMen:=0;         { defending men }
      EnShPow:=0;       { defending forces }
      AtShPow:=0;       { defending forces (not counting transports) }
      AttKilled:=0;
      DefKilled:=0;

      FOR i:=1 TO NoOfGroups DO
         WITH Gp[i] DO
            IF Sta<>GDst THEN
               BEGIN
               IF Typ=nnj THEN
                  PlGAT:=PlGAT+5.0*Num
               ELSE IF Typ=men THEN
                  PlGAT:=PlGAT+Num
               ELSE IF Typ=trn THEN
                  PlGAT:=PlGAT+(Num/5)
               ELSE IF Typ=jtn THEN
                  PlGAT:=PlGAT+(Num/2)
               ELSE
                  PlShPow:=PlShPow+(CombatPower[Typ]*(Num/100));
               END;

      PlGAT:=PlGAT*CombatData.AShipAdj/100;
      PlShPow:=PlShPow*CombatData.AShipAdj/100;

      FOR PosI:=DpSpc TO Grnd DO
         FOR ShpI:=LAM TO trn DO
            BEGIN
            Temp:=CombatPower[ShpI]*(En[PosI][ShpI]/100);
            EnShPow:=EnShPow+Temp;
            IF ShpI IN [LAM..jmp,pen,ssp,men,nnj] THEN
               AtShPow:=AtShPow+Temp;
            END;
      EnMen:=En[Grnd][Men]+5.0*En[Grnd][nnj];

      EnMen:=EnMen*CombatData.DGrndAdj/100;
      EnShPow:=EnShPow*CombatData.DShipAdj/100;

      FOR ShpI:=LAM TO trn DO
         BEGIN
         AttKilled:=AttKilled+(CombatPower[ShpI]/100)*Casualties[ShpI];
         DefKilled:=DefKilled+(CombatPower[ShpI]/100)*Killed[ShpI];
         END;

      IF PlShPow>0 THEN
         AttA:=AttKilled/PlShPow
      ELSE
         AttA:=0;

      IF EnShPow>0 THEN
         DefA:=DefKilled/EnShPow
      ELSE
         DefA:=0;
      END;  { GetForces }

   { EnemySurrenders: MAIN PROCEDURE }
   BEGIN
   GetForces;

   IF CombatData.DTyp=Flt THEN
      BEGIN
      IF (DefA>(AttA*2)) AND (EnShPow<(PlShPow/3)) THEN
         EnemySurrenders:=True
      ELSE IF (DefA>0) AND (AttA=0) AND (PlShPow>AtShPow) THEN
         EnemySurrenders:=True
      ELSE IF EnShPow=0 THEN
         EnemySurrenders:=True
      ELSE
         EnemySurrenders:=False;
      END
   ELSE
      BEGIN
      IF (CombatData.RevIndex>70) AND
         (PlGAT>EnMen) AND (PlShPow>AtShPow) THEN
         EnemySurrenders:=True
      ELSE IF (EnMen=0) AND (PlGAT>0) THEN
         EnemySurrenders:=True
      ELSE IF (AtShPow<(PlShPow/2)) AND
         (EnMen<(PlGAT/2)) AND
         (DefA>AttA) AND
         (CombatData.DTech IN [AtomicLvl..WrpTchLvl]) THEN
         EnemySurrenders:=True
      ELSE IF (EnMen<(PlGAT/4)) AND
         (CombatData.DTech IN [AtomicLvl..WrpTchLvl]) THEN
         EnemySurrenders:=True
      ELSE IF (DefA>(AttA*2)) AND (EnMen<(PlGAT/2)) AND
         (CombatData.DTech IN [WrpTchLvl..JmpTchLvl]) THEN
         EnemySurrenders:=True
      ELSE
         EnemySurrenders:=False;
      END;
   END;  { EnemySurrenders }

PROCEDURE CalculateCombatData(Attacker: Empire; FltID,TargetID: IDNumber;
                              VAR CombatData: CombatDataRecord);
   VAR
      ATech: TechLevel;
      Dummy: TechnologySet;
      CapID: IDNumber;

   BEGIN
   WITH CombatData DO
      BEGIN
      DTyp:=TargetID.ObjTyp;

      GetCapital(Attacker,CapID);
      ATech:=GetTech(CapID);
      IF DTyp=Flt THEN
         GetEmpireTechnology(GetStatus(TargetID),DTech,Dummy)
      ELSE
         DTech:=GetTech(TargetID);

      { adjust for tech level }
      AShipAdj:=CombatTechAdj[ATech,DTech];
      DShipAdj:=CombatTechAdj[DTech,ATech];
      DGrndAdj:=DShipAdj;

      { adjust for class and base }
      IF DTyp=Base THEN
         DGrndAdj:=Round((DGrndAdj/100)*CombatBaseAdj[GetBaseType(TargetID)])
      ELSE IF DTyp=Flt THEN
         DGrndAdj:=0
      ELSE
         DGrndAdj:=Round((DGrndAdj/100)*CombatClassAdj[GetClass(TargetID)]);

      MaxGDM:=GDMLaunch[DTech];

      IF DTyp=Pln THEN
         RevIndex:=GetRevIndex(TargetID)
      ELSE
         RevIndex:=0;
      END;  { with scope }
   END;  { CalculateCombatData }

PROCEDURE AttackFleet(FltID,Target: IDNumber;
                      Intention: AttackIntentionTypes;
                      VAR AttDest,DefDest: AttackArray;
                      VAR Result: AttackResultTypes);
{ AttackFleet: -----------------------------------------------------------------
   This procedure will carry out an attack on Target by FltID.  (Target must
   be a fleet.)  AttackFleet will return the casualties suffered, and the
   attack result, but the fleets themselves will not be updated, nor will
   news reports be sent out.
------------------------------------------------------------------------------ }
   BEGIN

   END;  { AttackFleet }

PROCEDURE GetConflict(CurPos: ShellPos;
                      NoOfGroups: Word;
                      VAR Gp: GroupArray;
                      VAR AttackingGroups: GroupSet);
{ GetConflict: -----------------------------------------------------------------
   This procedure will return the set of all groups in the orbit.
------------------------------------------------------------------------------ }
   VAR
      i: Word;

   BEGIN
   AttackingGroups:=[];
   FOR i:=1 TO NoOfGroups DO
      IF (Gp[i].Pos=CurPos) AND (Gp[i].Sta<>GDst) THEN
         AttackingGroups:=AttackingGroups+[i];
   END;  { GetConflict }

FUNCTION TotalProtection(NoOfGroups: Byte; VAR Gp: GroupArray; AttackingGroups: GroupSet): Real;
{ TotalProtection: -------------------------------------------------------------
   This function returns a value representing the total
   protection offered by the fleet.
------------------------------------------------------------------------------ }
   VAR
      temp: Real;
      i: Word;

   BEGIN
   temp:=0;
   FOR i:=1 TO NoOfGroups DO
      IF i IN AttackingGroups THEN
         WITH Gp[i] DO
            IF (Trg<>NoRes) AND (Typ<>Men) AND (Typ<>nnj) THEN
               BEGIN
               temp:=temp+ProtecOffered[Typ]*(Num/1);
               END;
   TotalProtection:=temp;
   END;  { TotalProtection }

FUNCTION ShipsDestroyed(NumberAttacking: Resources;
                        Attacker,Defender: AttackTypes;
                        Adj: Integer): Resources;
{ ShipsDestroyed:
   Given 'Resources' ships of type 'Attacker' all attacking ships of type
   'Defender,' this function returns the number of ships that the
   attacker destroyed.  'Adj' is the DEFENDER adjustment for tech
   levels and terrain.  The greater the Adj, the less effective the
   attack will be. }

   VAR
      temp,temp2: Real;
      temp3: Integer;

   BEGIN
   { calculate standard combat resolution }
   temp:=NumberAttacking*(CombatTable[Attacker,Defender]/100);

   { adjust for tech level, etc }
   IF Adj=0 THEN
      Adj:=1;
   temp:=100*(temp/Adj);

   temp3:=Trunc(IntLmt(temp));
   temp2:=(temp-temp3)*100;   { get remainder }

   IF temp2>100 THEN
      temp2:=0;               { if temp3 is already max then don't add }

   IF Rnd(1,100)<temp2 THEN
      temp3:=temp3+1;

   ShipsDestroyed:=ThgLmt(temp3);
   END;  { ShipsDestroyed }

PROCEDURE GetTargetArray(CurPos: ShellPos;
                         NoOfGroups: Byte;
                         VAR Gp: GroupArray;
                         AttackingGroups: GroupSet;
                         VAR En: EnemyArray;
                         VAR Killed: AttackArray;
                         CombatData: CombatDataRecord;
                         VAR Targ: TargetArray);
{ GetTargetArray: --------------------------------------------------------------
   This procedure initializes the enemy targetting array.  On
   entry, AttackingGroups is the set of groups attacking, and
   Enemy is an array of enemy ships in range to attack.
------------------------------------------------------------------------------ }

   TYPE
      PriorityTable1 = ARRAY [0..MaxNoOfGroups] OF Integer;
      PriorityTable2 = ARRAY [0..MaxNoOfGroups,AttackTypes] OF Integer;

   VAR
      Priority1: PriorityTable1;
      Priority2: PriorityTable2;

   PROCEDURE BuildPriority1(NoOfGroups: Byte;
                            VAR Gp: GroupArray;
                            AttackingGroups: GroupSet;
                            CombatData: CombatDataRecord;
                            VAR Priority1: PriorityTable1);
   { BuildPriority1:
      This procedure creates a table with an entry for each group
      indicating how important it is to attack it.  The more
      powerful a group, the higher the entry in the table.  Also,
      because transports must be stopped before they reach the
      ground, transport groups are given a higher priority if they
      are approaching the surface. }

      VAR
         i: Word;
         Total: Real;
         Cover: Real;
         PerCentCover: Integer;

      { BuildPriority1: MAIN PROCEDURE }
      BEGIN
      Cover:=TotalProtection(NoOfGroups,Gp,AttackingGroups);
      Total:=0;

      FOR i:=1 TO NoOfGroups DO
         IF i IN AttackingGroups THEN
            WITH CombatData,Gp[i] DO
               BEGIN
               Priority1[i]:=Round((ShipValue[Typ]/1000)*Num)+1;

               { adjust for transports }
               IF ((Typ=Trn) OR (Typ=Jtn)) AND (DTyp<>Flt) THEN
                  Priority1[i]:=IntLmt(Priority1[i]*(1.0+Integer(Pos)))
               { adjust for starships }
               ELSE IF Typ=ssp THEN
                  Priority1[i]:=IntLmt(Priority1[i]*(16.0-2*Integer(Pos)));

               { adjust for defended groups }
               IF (Trg=NoRes) AND (Typ<>men) AND (Typ<>nnj) THEN
                  BEGIN
                  PerCentCover:=IntLmt(((Cover/Num)/ProtecNeeded[Typ])*100);
                  if PerCentCover>100 then
                     PerCentCover:=100;

                  Priority1[i]:=Round(Priority1[i]*(1-PerCentCover/100));
                  END;

               { adjust for hkr groups }
               IF (Trg=NoRes) AND (Typ=hkr) AND (Flg=False) THEN
                  Priority1[i]:=0;

               Total:=Total+Priority1[i];
               END;

      { scale priority table 1 }

      { avoid division by zero }
      IF Total=0 THEN
         Total:=1;

      FOR i:=1 TO NoOfGroups DO
         Priority1[i]:=Round((Priority1[i]/Total)*1000);
      END;  { BuildPriority1 }

   PROCEDURE BuildPriority2(NoOfGroups: Byte;
                            VAR Gp: GroupArray;
                            AttackingGroups: GroupSet;
                            VAR Priority1: PriorityTable1;
                            VAR Priority2: PriorityTable2);
   { BuildPriority2:
      Since some ships are more effective than others in combat, this
      procedure constructs a 2D matrix of groups vs AttackTypes so that
      each entry in the matrix is a scaled representation of how
      important it is for the given attack type to attack the given
      group.  For example, because jumpships are more effective against
      fighters than penetrators are, the entry for jumpships attacking
      a groups of fighters will be higher than the one for penetrators
      attacking that same group.  Of course, the results of priority1
      are included so that important ships like starships and
      transports are attacked regardless of how hard it is to destroy
      them. }

      VAR
         i: Integer;
         ShpI: AttackTypes;
         Total: ARRAY [AttackTypes] OF Real;

      BEGIN
      FOR i:=1 TO NoOfGroups DO
         IF i IN AttackingGroups THEN
            WITH Gp[i] DO
               BEGIN
               FOR ShpI:=LAM TO nnj DO
                  BEGIN
                  Priority2[i,ShpI]:=Round( Priority1[i] *
                     (CombatTable[ShpI,Typ]/WeapEff[ShpI])  );
                  END;
               END;  { loop }

      { scale priority table 2 }
      FOR ShpI:=LAM TO nnj DO
         BEGIN
         Total[ShpI]:=0;
         FOR i:=1 TO NoOfGroups DO
            Total[ShpI]:=Total[ShpI]+Priority2[i,ShpI];

         IF Total[ShpI]=0 THEN
            Total[ShpI]:=1;
         END;

      FOR ShpI:=LAM TO nnj DO
         BEGIN
         FOR i:=1 TO NoOfGroups DO
            Priority2[i,ShpI]:=Round((Priority2[i,ShpI]/Total[ShpI])*1000);
         END;
      END;  { BuildPriority2 }

   PROCEDURE BuildTargetArray(CurPos: ShellPos;
                              NoOfGroups: Byte;
                              VAR En: EnemyArray;
                              CombatData: CombatDataRecord;
                              VAR Killed: AttackArray;
                              VAR Priority2: PriorityTable2;
                              VAR Targ: TargetArray);
   { BuildTargetArray:
      This procedure uses Priority2 to form the final TargetArray. }

      VAR
         ShpI: AttackTypes;
         NoOfGDM,GDMAtTarget,i: Integer;
         NoOfLAM: Integer;

      { BuildTargetArray: MAIN PROCEDURE }
      BEGIN
      FOR ShpI:=fgt TO trn DO
         FOR i:=1 TO NoOfGroups DO
            Targ[i,ShpI]:=Round((Priority2[i,ShpI]/1000)*En[CurPos][ShpI]);

      { ground troops }
      IF CurPos=Grnd THEN
         FOR i:=1 TO NoOfGroups DO
            BEGIN
            Targ[i,Men]:=Round((Priority2[i,Men]/1000)*En[Grnd][men]);
            Targ[i,nnj]:=Round((Priority2[i,nnj]/1000)*En[Grnd][nnj]);
            END;

      { defense satellites }
      IF (CurPos=HiOrb) OR (CurPos=Orbit) THEN
         FOR i:=1 TO NoOfGroups DO
            Targ[i,def]:=Round((Priority2[i,def]/1000)*En[Orbit][def]);

      { ion cannons }
      IF CurPos=SbOrb then
         FOR i:=1 TO NoOfGroups DO
            Targ[i,ion]:=Round((Priority2[i,ion]/1000)*En[SbOrb][ion]);

      { LAM }
      NoOfLAM:=0;
      FOR i:=1 TO NoOfGroups DO
         IF Priority2[i,LAM]<>0 THEN
            WITH Gp[i] DO
               NoOfLAM:=ThgLmt(NoOfLAM+(Num*((100+Rnd(0,100))/CombatTable[LAM,Typ])));
      NoOfLAM:=LesserInt(NoOfLAM,En[SbOrb][LAM]);

      En[SbOrb][LAM]:=En[SbOrb][LAM]-NoOfLAM;
      Killed[LAM]:=Killed[LAM]+NoOfLAM;
      FOR i:=1 TO NoOfGroups DO
         WITH Gp[i] DO
            BEGIN
            Targ[i,LAM]:=Round((Priority2[i,LAM]/1000)*NoOfLAM);
            END;  { with scope and loop }

      { GDMs }
      IF CurPos=Orbit THEN
         BEGIN
         NoOfGDM:=0;
         FOR i:=1 TO NoOfGroups DO
            IF Priority2[i,GDM]<>0 THEN
               WITH Gp[i] DO
                  NoOfGDM:=ThgLmt(NoOfGDM+(Num*((100+Rnd(0,100))/CombatTable[GDM,Typ])));

         NoOfGDM:=LesserInt(NoOfGDM,CombatData.MaxGDM+Rnd(0,10));
         NoOfGDM:=LesserInt(NoOfGDM,En[SbOrb][GDM]);

         En[SbOrb][GDM]:=En[SbOrb][GDM]-NoOfGDM;
         Killed[GDM]:=Killed[GDM]+NoOfGDM;
         FOR i:=1 TO NoOfGroups DO
            WITH Gp[i] DO
               BEGIN
               GDMAtTarget:=Round((Priority2[i,GDM]/1000)*NoOfGDM);

               { ships destroy GDMs from orbit }
               GDMAtTarget:=GDMAtTarget-Round((GDMKill[Typ]/10)*Num);
               IF GDMAtTarget<0 THEN
                  GDMAtTarget:=0;

               Targ[i,GDM]:=GDMAtTarget;
               END;
         END;
      END;  { BuildTargetArray }

   { GetTargetArray: MAIN PROCEDURE }
   BEGIN
   FillChar(Targ,SizeOf(Targ),0);
   FillChar(Priority1,SizeOf(Priority1),0);
   FillChar(Priority2,SizeOf(Priority2),0);
   BuildPriority1(NoOfGroups,Gp,AttackingGroups,CombatData,Priority1);
   BuildPriority2(NoOfGroups,Gp,AttackingGroups,Priority1,Priority2);
   BuildTargetArray(CurPos,NoOfGroups,En,CombatData,Killed,Priority2,Targ);
   END;  { GetTargetArray }

PROCEDURE GroupAttack(NoOfGroups: Byte;
                      VAR Gp: GroupArray;
                      AttackingGroups: GroupSet;
                      VAR EShDest: AttackArray;
                      CombatData: CombatDataRecord);
{ GroupAttack:
   This procedure resolves the attack turn of all groups in range to
   attack.  EShDest is updated to reflect the maximum number of ships
   that the groups destroyed. }

   VAR
      i: Word;
      temp: Integer;

   FUNCTION InRangeOfDefense(Attacker,Target: AttackTypes;
                             AttackingFrom: ShellPos): Boolean;
   { InRangeOfDefense:
      Returns false if the group at 'AttackingFrom' is too far to damage 
      the given Target.  }

      VAR
         temp: Boolean;

      BEGIN
      temp:=False;
      CASE Target OF
         GDM,LAM: IF AttackingFrom=SbOrb THEN
                     temp:=True;
         ion: IF (AttackingFrom=SbOrb) AND (Attacker<>fgt) THEN
                 temp:=True
              ELSE IF (AttackingFrom=Grnd) THEN
                 temp:=True;
         def: IF AttackingFrom=Orbit THEN
                 temp:=True;
         Men,nnj: IF AttackingFrom=Grnd THEN
                     temp:=True
                  ELSE IF (AttackingFrom=SbOrb) AND (Attacker IN [pen,ssp]) THEN
                     temp:=True;
      ELSE
         temp:=True;
      END;  { case }
      InRangeOfDefense:=temp;
      END;  { InRangeOfDefense }

   { GroupAttack: MAIN PROCEDURE }
   BEGIN
   FOR i:=1 TO NoOfGroups DO
      IF i IN AttackingGroups THEN
         WITH Gp[i] DO
            BEGIN
            IF Trg IN [men,nnj] THEN
               temp:=ShipsDestroyed(Num,Typ,Trg,CombatData.DGrndAdj)
            ELSE
               temp:=ShipsDestroyed(Num,Typ,Trg,CombatData.DShipAdj);

            { if group advancing, increase casualties }
            IF (Sta=GAdvc) AND (Trg IN [ssp..trn,def]) THEN
               temp:=temp+(temp DIV 2);

            { if attacking defenses out of range, no damage }
            IF NOT InRangeOfDefense(Typ,Trg,Pos) THEN
               temp:=0;

            EShDest[Trg]:=ThgLmt(EShDest[Trg]+temp);

            { if khr attacking, cloak is off }
            IF (Typ=hkr) AND (Trg<>NoRes) THEN
               Flg:=True;
            END;  { with scope and loop }
   END;  { GroupAttack }

PROCEDURE EnemyAttack(NoOfGroups: Byte;
                      VAR Gp: GroupArray;
                      AttackingGroups: GroupSet;
                      VAR GShDest: DestArray;
                      VAR Targ: TargetArray;
                      CombatData: CombatDataRecord;
                      VAR Details: DetailArray);
   VAR
      i: Byte;
      ShpI: AttackTypes;
      temp: Integer;

   BEGIN
   FOR i:=1 TO NoOfGroups DO
      IF i IN AttackingGroups THEN
         BEGIN
         FOR ShpI:=LAM TO nnj DO
            BEGIN
            temp:=ShipsDestroyed(Targ[i][ShpI],ShpI,Gp[i].Typ,CombatData.AShipAdj);

            { if groups advancing through the enemy, increase casualties. }
            IF (Gp[i].Sta=GAdvc)
               AND ((ShpI in [ssp..trn]) 
               OR ((ShpI=def) AND (Gp[i].Pos=Orbit))) THEN
               temp:=temp+(temp DIV 2);

            GShDest[i]:=ThgLmt(GShDest[i]+temp);
            Details[ShpI,i]:=LesserInt(ThgLmt(Details[ShpI,i]+temp),Gp[i].Num);
            IF temp>0 THEN
               Details[ShpI,0]:=1;
            END;
         END;  { loop }
   END;  { EnemyAttack }

PROCEDURE UpdateEnemyDestroyed(CurPos: ShellPos;
                               VAR En: EnemyArray;
                               VAR EShDest: AttackArray;
                               VAR Killed: AttackArray);
   VAR
      Destroyed: Resources;
      ShpI: AttackTypes;

   BEGIN
   FOR ShpI:=LAM TO nnj DO
      BEGIN
      Destroyed:=LesserInt(EShDest[ShpI],En[CurPos,ShpI]);
      Dec(En[CurPos,ShpI],Destroyed);
      Inc(Killed[ShpI],Destroyed);
      END;
   END;  { UpdateEnemyDestroyed }

PROCEDURE UpdateGroupsDestroyed(NoOfGroups: Byte;
                                VAR Gp: GroupArray;
                                AttackingGroups: GroupSet;
                                VAR GroupsDestroyed: GroupSet;
                                VAR GShDest: DestArray;
                                VAR Casualties: AttackArray);
   VAR
      i,j: Word;
      Loc: Integer;
      TrnWithMen: Real;
      MenLost: Resources;

   BEGIN
   GroupsDestroyed:=[];
   FOR i:=1 TO NoOfGroups DO
      IF i IN AttackingGroups THEN
         WITH Gp[i] DO
            BEGIN
            IF Num-GShDest[i]<=0 THEN
               { group destroyed }
               BEGIN
               Inc(Casualties[Typ],Num);
               Num:=0;
               Sta:=GDst;

               IF (Typ IN [jtn,trn]) AND (GAT>0) THEN
                  BEGIN
                  Inc(Casualties[GATTyp],GAT);
                  END;

               GroupsDestroyed:=GroupsDestroyed+[i];
               END
            ELSE
               { some ships destroyed }
               BEGIN
               { take care of GATs }
               IF (Typ IN [JTn,Trn]) AND (GShDest[i]>0) AND (GAT>0) THEN
                  BEGIN
                  TrnWithMen:=(GAT/(CargoSpace[Men]*TrnAdj[Typ]));
                  MenLost:=ThgLmt((GShDest[i]*TrnWithMen/Num)+2);
                  MenLost:=LesserInt(GAT,MenLost);
                  GAT:=GAT-MenLost;
                  Inc(Casualties[GATTyp],MenLost);
                  END;

               Dec(Num,GShDest[i]);
               Inc(Casualties[Typ],GShDest[i]);
               END;
            END;  { with scope and loop }
   END;  { UpdateDestroyedShips }

PROCEDURE Battle(NoOfGroups: Byte;
                 VAR Gp: GroupArray;
                 VAR GroupsDestroyed: GroupSet;
                 VAR En: EnemyArray;
                 CurPos: ShellPos;
                 CombatData: CombatDataRecord;
                 VAR Details: DetailArray;
                 VAR Casualties,Killed: AttackArray);
{ Battle:
   This is the most important procedure of GroupEngage.  Battle is called
   once for each orbit in conflict.  }

   VAR
      AttackingGroups: GroupSet;
      Targ: TargetArray;
      GShDest: DestArray;
      EShDest: AttackArray;

   { Battle: MAIN PROCEDURE }
   BEGIN
   GroupsDestroyed:=[];
   GetConflict(CurPos,NoOfGroups,Gp,AttackingGroups);

   IF AttackingGroups<>[] THEN
      BEGIN
      FillChar(EShDest,SizeOf(EShDest),0);
      FillChar(GShDest,SizeOf(GShDest),0);
      GetTargetArray(CurPos,NoOfGroups,Gp,AttackingGroups,En,Killed,CombatData,Targ);

      GroupAttack(NoOfGroups,Gp,AttackingGroups,EShDest,CombatData);
      EnemyAttack(NoOfGroups,Gp,AttackingGroups,GShDest,Targ,CombatData,Details);

      UpdateGroupsDestroyed(NoOfGroups,Gp,AttackingGroups,GroupsDestroyed,GShDest,Casualties);
      UpdateEnemyDestroyed(CurPos,En,EShDest,Killed);
      END;
   END;  { Battle }

PROCEDURE ConquerWorld(WorldID: IDNumber; Emp: Empire);
{ ConquerWorld: ----------------------------------------------------------------
   This procedure will handle the conquering of WorldID by Emp.  The
   status of the world is changed, efficiency is decreased because of the
   administrative take-over, and the revolution index is changed according 
   to how satisfied the world was with its previous empire/government.  If
   the revolution index is low, then it will rise because the people yearn
   for the old status quo.  If the revolution index is high, then it will 
   be lowered because the people, tired of their old government, will be 
   ready for change.  Of course, some random factors are thrown in. 
------------------------------------------------------------------------------ }

   VAR
      XY: XYCoord;
      Dir: Directions;
      x,y: Integer;

   PROCEDURE ChangeEfficiency(WorldID: IDNumber);
      VAR
         temp: Integer;
         NewEfficiency: Index;

      BEGIN
      temp:=Rnd(10,20);
      IF GetEfficiency(WorldID)-temp<0 THEN
         NewEfficiency:=0
      ELSE
         NewEfficiency:=GetEfficiency(WorldID)-temp;
      SetEfficiency(WorldID,NewEfficiency);
      END;  { ChangeEfficiency }

   PROCEDURE ChangeRevolutionIndex(WorldID: IDNumber);
      VAR
         Rev: Index;

      BEGIN
      Rev:=GetRevIndex(WorldID);
      CASE Rev OF
         0..10 : ChangeRevIndex(WorldID,Rnd(10,20));
        11..30 : BEGIN
                 CASE Rnd(1,100) OF
                    1..20 : ChangeRevIndex(WorldID,-Rnd(5,15));
                   21..50 : ChangeRevIndex(WorldID,-Rnd(3,10));
                   51..90 : ChangeRevIndex(WorldID,Rnd(10,15));
                  91..100 : ChangeRevIndex(WorldID,Rnd(20,30));
                 END;  { case }
                 END;
        31..55 : BEGIN
                 CASE Rnd(1,100) OF
                    1..50 : ChangeRevIndex(WorldID,-Rnd(10,20));
                   51..75 : ChangeRevIndex(WorldID,-Rnd(5,10));
                   76..90 : ChangeRevIndex(WorldID,Rnd(1,5));
                  91..100 : ChangeRevIndex(WorldID,Rnd(5,15));
                 END;  { case }
                 END;
        56..75 : ChangeRevIndex(WorldID,-Rnd(40,55));
        76..90 : ChangeRevIndex(WorldID,-Rnd(50,65));
       91..100 : ChangeRevIndex(WorldID,-Rnd(30,40));
      END;  { case }
      END;  { ChangeRevolutionIndex }

   { ConquerWorld: MAIN PROCEDURE }
   BEGIN
   SetStatus(WorldID,Emp);
   ChangeEfficiency(WorldID);
   ChangeRevolutionIndex(WorldID);
   GetCoord(WorldID,XY);
   Scout(Emp,XY);
   END;  { ConquerWorld }

PROCEDURE ConquerEmpire(Player,EnemyEmp: Empire; VAR Booty: PlanetSet);
{ ConquerEmpire:
   This procedure will implement the conquest of EnemyEmp by Player.  All
   small (low population) worlds far from the enemy capital will
   automatically become part of the conquering empire.  All large worlds will
   remain with the enemy empire if their revolution index is not too high,
   else they will become independent.  If there is at least one large world
   that remains with the empire, then it will become the new capital.  All
   starbases and fleets will remain with the enemy empire. }

   VAR
      XY,CapXY,ConqXY: XYCoord;
      Loc: Location;
      EnemyCapID,WorldID,NewCapID,PlayCapID: IDNumber;
      Pop: Population;
      RevI: Index;
      Dist,DistToConq,i: Integer;

   PROCEDURE NewCapital(Emp: Empire; NewCap: IDNumber);
      VAR
         OldTech,NewTech: TechLevel;
         OldTechnology: TechnologySet;

      BEGIN
      GetEmpireTechnology(Emp,OldTech,OldTechnology);
      NewTech:=GetTech(NewCap);
      IF OldTech>NewTech THEN
         SetEmpireTechnology(Emp,NewTech,TechDev[NewTech])
      ELSE IF OldTech<NewTech THEN
         SetEmpireTechnology(Emp,NewTech,TechDev[Pred(NewTech)]);
      SetCapital(Emp,NewCap);
      SetType(NewCap,CapTyp);
      ChangeRevIndex(NewCap,-Rnd(30,50));
      SetEfficiency(NewCap,Rnd(40,60));
      END;  { NewCapital }

   { ConquerEmpire: MAIN PROCEDURE }
   BEGIN
   GetCapital(EnemyEmp,EnemyCapID);
   GetCapital(Player,PlayCapID);
   GetCoord(EnemyCapID,CapXY);
   GetCoord(PlayCapID,ConqXY);
   NewCapID:=EmptyQuadrant;
   Booty:=[];

   Loc.XY:=Limbo;
   WorldID.ObjTyp:=Pln;
   FOR i:=1 TO NoOfPlanets DO
      IF i IN SetOfPlanetsOf[EnemyEmp] THEN
         BEGIN
         WorldID.Index:=i;
         Loc.ID:=WorldID;
         GetCoord(WorldID,XY);

         Dist:=Distance(XY,CapXY);
         DistToConq:=Distance(XY,ConqXY);
         Pop:=GetPopulation(WorldID);
         RevI:=GetRevIndex(WorldID);

         IF ((Dist>10) AND (DistToConq<10) AND (Pop<Rnd(900,1100))) THEN
            BEGIN
            ConquerWorld(WorldID,Player);
            AddNews(EnemyEmp,Join,Loc,Integer(Player),0,0);
            Booty:=Booty+[i];
            END
         ELSE IF (Pop>Rnd(900,1100)) AND (RevI>50) AND (Rnd(1,100)<75) THEN
            BEGIN
            SetStatus(WorldID,Indep);
            SetType(WorldID,IndTyp);
            AddNews(EnemyEmp,DInd,Loc,0,0,0);
            END
         ELSE IF (DistToConq<10) AND (Rnd(1,100)<60) AND (RevI>35) THEN
            BEGIN
            ConquerWorld(WorldID,Player);
            AddNews(EnemyEmp,Join,Loc,Integer(Player),0,0);
            Booty:=Booty+[i];
            END
         ELSE
            BEGIN
            { CHOOSE NEW CAPITAL }
            IF SameID(NewCapID,EmptyQuadrant) THEN
               BEGIN
               IF GetTech(WorldID)>JmpTchLvl THEN
                  NewCapID:=WorldID;
               END
            ELSE
               BEGIN
               IF GetTech(WorldID)>GetTech(NewCapID) THEN
                  NewCapID:=WorldID
               ELSE IF GetTech(WorldID)=GetTech(NewCapID) THEN

                  { ASSERT: new world at least as advanced as NewCapID. }
                  BEGIN
                  IF NOT (GetType(NewCapID) IN [BseTyp,BseSTyp,JmpTyp,JmpSTyp,StrTyp,StrSTyp]) THEN

                     { ASSERT: NewCapID not a base planet }
                     BEGIN
                     IF (Pop>GetPopulation(NewCapID)) OR
                        (GetType(WorldID) IN [BseTyp,BseSTyp,JmpTyp,JmpSTyp,StrTyp,StrSTyp]) THEN
                        NewCapID:=WorldID;
                     END
                  ELSE

                     { ASSERT: NewCapID is a base planet }
                     BEGIN
                     IF (GetType(WorldID) IN [BseTyp,BseSTyp,JmpTyp,JmpSTyp,StrTyp,StrSTyp]) AND
                        (Pop>GetPopulation(NewCapID)) THEN
                           NewCapID:=WorldID;
                     END;
                  END;
               END;
            END;
         END;  { loop }

	(*
   WorldID.ObjTyp:=Base;
   FOR i:=1 TO MaxNoOfStarbases DO
      IF i IN SetOfStarbasesOf[EnemyEmp] THEN
         BEGIN
         WorldID.Index:=i;
	 if GetBaseType(WorldID) in [cmm,frt] then
	   begin
            Pop:=GetPopulation(WorldID);
            IF SameID(NewCapID,EmptyQuadrant) THEN
              NewCapID:=WorldID
            ELSE
              BEGIN
            	IF Pop>GetPopulation(NewCapID) THEN
               	NewCapID:=WorldID;
              END;
  	   end;
         END;  { loop }
	*)

   IF CentralizedCapital(EnemyEmp) OR SameID(NewCapID,EmptyQuadrant) THEN
      { ASSERT: enemy empire totally destroyed. }
      BEGIN
      IF EmpirePlayer(EnemyEmp) THEN
         BEGIN
         NewCapID.ObjTyp:=Void;
         NewCapID.Index:=Ord(Player);
         SetCapital(EnemyEmp,NewCapID);
         END
      ELSE
         DestroyEmpire(EnemyEmp);
      END
   ELSE
      BEGIN
      NewCapital(EnemyEmp,NewCapID);
      Loc.ID:=NewCapID;
      AddNews(EnemyEmp,NewCap,Loc,0,0,0);
      GetCoord(NewCapID,CapXY);
      END;
   END;  { ConquerEmpire }

PROCEDURE RestoreCombatant(ObjID: IDNumber; VAR Casualties: AttackArray);

   VAR
      Sh: ShipArray;
      Cr: CargoArray;
      Df: DefnsArray;
      ThgI: AttackTypes;
      FuelCap: Real;

   BEGIN
   GetShips(ObjID,Sh);
   GetCargo(ObjID,Cr);

   FOR ThgI:=fgt TO trn DO
      Sh[ThgI]:=GreaterInt(0,Sh[ThgI]-Casualties[ThgI]);
   FOR ThgI:=men TO nnj DO
      Cr[ThgI]:=GreaterInt(0,Cr[ThgI]-Casualties[ThgI]);

   CASE ObjID.ObjTyp OF
      Pln, Base: BEGIN
         GetDefns(ObjID,Df);
         FOR ThgI:=LAM TO ion DO
            Df[ThgI]:=GreaterInt(0,Df[ThgI]-Casualties[ThgI]);

         PutShips(ObjID,Sh);
         PutCargo(ObjID,Cr);
         PutDefns(ObjID,Df);
         END;
      Flt: BEGIN
         IF FleetCargoSpace(Sh,Cr)<0 THEN
            BalanceFleet(Sh,Cr);

         PutShips(ObjID,Sh);
         PutCargo(ObjID,Cr);

         FuelCap:=FuelCapacity(Sh);
         IF GetFleetFuel(ObjID)>FuelCap THEN
            SetFleetFuel(ObjID,FuelCap);
         END;
   END;  { case }
   END;  { RestoreCombatant }

PROCEDURE ResolveAttack(Result: AttackResultTypes;
                        FltID,Target: IDNumber;
                        HKAttack,Capture: Boolean;
                        VAR Casualties,Killed: AttackArray;
                        VAR Booty: PlanetSet);
   VAR
      Emp,EnemyEmp: Empire;
      Loc: Location;
      RevChange: Integer;
      ShpI: ResourceTypes;
      Sh: ShipArray;
      EmpireConquered: Boolean;

   PROCEDURE ReportLosses(Emp: Empire; Loc: Location; VAR Killed: AttackArray);
      VAR
         ShpI: ResourceTypes;

      BEGIN
      FOR ShpI:=LAM TO nnj DO
         IF Killed[ShpI]>0 THEN
            AddNews(Emp,DestDetail,Loc,Killed[ShpI],Integer(ShpI),0);
      END;  { ReportLosses }

   { ResolveAttack: MAIN PROCEDURE }
   BEGIN
   Loc.ID:=Target;  Loc.XY:=Limbo;
   Emp:=GetStatus(FltID);
   EnemyEmp:=GetStatus(Target);
   EmpireConquered:=False;
   Booty:=[];

   CASE Result OF
      AttDestroyedART: BEGIN
         IF EnemyEmp=Indep THEN
            ChangeTotalRevIndex(Emp,Rnd(1,4))
         ELSE
            BEGIN
            ChangeTotalRevIndex(Emp,Rnd(2,5));
            ChangeTotalRevIndex(EnemyEmp,-Rnd(3,6));
            AddNews(EnemyEmp,BattleW1,Loc,Ord(Emp),0,0);
            ReportLosses(EnemyEmp,Loc,Killed);
            AddGlobalNews([Emp,EnemyEmp],Target,GLBDest,Loc,Ord(Emp),Ord(EnemyEmp),0);
            END;
	 Loc.ID:=FltID;
	 FleetNameDestruction(Emp,FltID,Loc);
	 DestroyFleet(FltID);
         END;
      AttRetreatsART: BEGIN
         IF EnemyEmp=Indep THEN
            BEGIN
            END
         ELSE
            BEGIN
            IF HKAttack THEN
               AddNews(EnemyEmp,BattleW2UNK,Loc,0,0,0)
            ELSE
               BEGIN
               AddNews(EnemyEmp,BattleW2,Loc,Ord(Emp),0,0);
               AddGlobalNews([Emp,EnemyEmp],Target,GLBDest,Loc,Ord(Emp),Ord(EnemyEmp),0);
               END;

            ReportLosses(EnemyEmp,Loc,Killed);
            ChangeTotalRevIndex(Emp,Rnd(1,3));
            ChangeTotalRevIndex(EnemyEmp,-Rnd(1,3));
            END;
         END;
      DefConqueredART: BEGIN
         IF Target.ObjTyp IN [Pln,Base] THEN
            BEGIN
            ConquerWorld(Target,Emp);
            IF GetType(Target)=CapTyp THEN
               BEGIN
               SetType(Target,IndTyp);
               EmpireConquered:=True;
               RevChange:=Rnd(25,50);
               AddGlobalNews([Emp,EnemyEmp],Target,GLBCapConq,Loc,Ord(Emp),Ord(EnemyEmp),0);
               END
            ELSE
               BEGIN
               RevChange:=Rnd(5,10);
               AddGlobalNews([Emp,EnemyEmp],Target,GLBConq,Loc,Ord(Emp),Ord(EnemyEmp),0);
               END;
            END
         ELSE
            BEGIN
            GetShips(Target,Sh);
            FOR ShpI:=fgt TO trn DO
               Killed[ShpI]:=Killed[ShpI]+Sh[ShpI];

            FleetNameDestruction(EnemyEmp,Target,Loc);

            IF (Target.Index IN SetOfActiveFleets) AND Capture THEN
               AbortFleet(Target,FltID,False);

            DestroyFleet(Target);
            RevChange:=Rnd(1,5);
            AddGlobalNews([Emp,EnemyEmp],Target,GLBConq,Loc,Ord(Emp),Ord(EnemyEmp),0);
            END;

         IF EnemyEmp=Indep THEN
            ChangeTotalRevIndex(Emp,-Rnd(2,4))
         ELSE
            BEGIN
            IF (HKAttack) AND (Target.ObjTyp=Flt) THEN
               AddNews(EnemyEmp,BattleLUNK,Loc,0,0,0)
            ELSE
               AddNews(EnemyEmp,BattleL,Loc,Ord(Emp),0,0);

            ReportLosses(EnemyEmp,Loc,Killed);
            ChangeTotalRevIndex(Emp,-Rnd(3,6));
            ChangeTotalRevIndex(EnemyEmp,RevChange);
            END;

         IF EmpireConquered THEN
            ConquerEmpire(Emp,EnemyEmp,Booty);
         END;
   END;  { case }
   END;  { ResolveAttack }

PROCEDURE DefaultGroup(VAR Sh: ShipArray; VAR Cr: CargoArray;
                       VAR Gp: GroupRecord;
                       ThgI: ResourceTypes);
   VAR
      MaxMen: Integer;

   BEGIN
   WITH Gp DO
      BEGIN
      Typ:=ThgI;
      Num:=Sh[ThgI];
      Sh[ThgI]:=0;
      Trg:=NoRes;
      Pos:=DpSpc;
      Sta:=GReady;
      Flg:=False;

      IF Typ IN [jtn,trn] THEN
         BEGIN
         IF Cr[nnj]>0 THEN
            BEGIN
            MaxMen:=ThgLmt(Round(TrnAdj[Typ]*Num*CargoSpace[nnj]));
            MaxMen:=LesserInt(MaxMen,Cr[nnj]);
            GAT:=MaxMen;
            GATTyp:=nnj;
            Cr[nnj]:=Cr[nnj]-MaxMen;
            END
         ELSE
            BEGIN
            MaxMen:=ThgLmt(Round(TrnAdj[Typ]*Num*CargoSpace[men]));
            MaxMen:=LesserInt(MaxMen,Cr[men]);
            GAT:=MaxMen;
            GATTyp:=men;
            Cr[men]:=Cr[men]-MaxMen;
            END;
         END
      ELSE
         BEGIN
         GAT:=0;
         END;
      END;
   END;  { DefaultGroup }

PROCEDURE DefaultDistribution(FltID: IDNumber; VAR NoOfGroups: Byte; VAR Gp: GroupArray);
   VAR
      ThgI: ResourceTypes;
      MaxMen: Integer;
      Sh: ShipArray;
      Cr: CargoArray;

   BEGIN
   GetShips(FltID,Sh);
   GetCargo(FltID,Cr);
   NoOfGroups:=0;
   FOR ThgI:=fgt TO trn DO
      IF (Sh[ThgI]<>0) AND (ThgI IN [fgt..jmp,pen,ssp]) THEN
         BEGIN
         NoOfGroups:=NoOfGroups+1;
         DefaultGroup(Sh,Cr,Gp[NoOfGroups],ThgI);
         END;
   FOR ThgI:=fgt TO trn DO
      IF (Sh[ThgI]<>0) AND (ThgI IN [jtn,trn]) THEN
         BEGIN
         NoOfGroups:=NoOfGroups+1;
         DefaultGroup(Sh,Cr,Gp[NoOfGroups],ThgI);
         END;
   END;  { DefaultDistribution }

PROCEDURE GetEnemy(Target: IDNumber; VAR En: EnemyArray);
   VAR
      Status: Empire;
      PosI: ShellPos;
      ShpI: ResourceTypes;
      Defenses: DefenseRecord;
      Sh: ShipArray;
      Cr: CargoArray;
      Defns: DefnsArray;
      Split: Real;
      Tech: TechLevel;

   BEGIN
   FillChar(En,SizeOf(En),0);
   Status:=GetStatus(Target);
   Tech:=GetTech(Target);
   GetShips(Target,Sh);
   GetCargo(Target,Cr);

   CASE Target.ObjTyp OF
      Pln,
      Base: BEGIN
         GetDefenseSettings(Status,Defenses);
         GetDefns(Target,Defns);

         FOR PosI:=DpSpc TO Grnd DO
            FOR ShpI:=fgt TO trn DO
               IF (Status<>Indep) OR (ShpI IN TechDev[Tech]) THEN
                  BEGIN
                  En[PosI][ShpI]:=Round((Defenses.ShellDefDist[PosI,ShpI]/100)*Sh[ShpI]);
                  END;

         En[Orbit][def]:=Defns[def];
         En[SbOrb][GDM]:=Defns[GDM];
         En[SbOrb][ion]:=Defns[ion];
         En[SbOrb][LAM]:=Defns[LAM];
         En[Grnd][men]:=Cr[men];
         En[Grnd][nnj]:=Cr[nnj];
         END;
      Flt: BEGIN
         IF Sh[jtn]+Sh[trn]=0 THEN
            Split:=1
         ELSE
            Split:=0.75;

         FOR ShpI:=fgt TO trn DO
            IF NOT (ShpI IN [jtn,trn]) THEN
               BEGIN
               En[HiOrb][ShpI]:=Round(Split*Sh[ShpI]);
               En[Orbit][ShpI]:=Round((1-Split)*Sh[ShpI]);
               END
            ELSE
               En[Orbit][ShpI]:=Sh[ShpI];
         END;
   END;  { case }
   END;  { GetEnemy }

FUNCTION ForcesUnknown(FltID,Target: IDNumber): Boolean;
   VAR
      Sh: ShipArray;

   BEGIN
   GetShips(FltID,Sh);
   IF (TypeOfFleet(FltID)=HKFleet) AND (Sh[hkr]<=500)
      AND (NOT Scouted(GetStatus(Target),FltID)) THEN
      ForcesUnknown:=True
   ELSE
      ForcesUnknown:=False;
   END;  { ForcesUnknown }

FUNCTION HolocaustEffectiveness(FltID,WorldID: IDNumber): Index;
   VAR
      TotalAttack,TotalDefense: LongInt;
      Sh,FltSh: ShipArray;
      Df: DefnsArray;

   BEGIN
   GetDefns(WorldID,Df);
   GetShips(WorldID,Sh);
   TotalDefense:=MilitaryPower(Sh,Df);

   FillChar(Df,SizeOf(Df),0);
   GetShips(FltID,FltSh);
   TotalAttack:=MilitaryPower(FltSh,Df);

   IF TotalDefense=0 THEN
      HolocaustEffectiveness:=100
   ELSE IF (TotalAttack<5000) OR (TotalAttack/TotalDefense<1) THEN
      HolocaustEffectiveness:=0
   ELSE
      HolocaustEffectiveness:=LesserInt(100,Round(10*TotalAttack/TotalDefense));
   END;  { HolocaustEffectiveness }

PROCEDURE HolocaustWorld(Emp: Empire; Effectiveness: Index;
                         WorldID,FltID: IDNumber; VAR Losses: ShipArray;
                         VAR IndusDest: IndusArray; VAR Deaths: Population;
                         VAR Revert: Boolean;
                         VAR Result: HoloResultTypes);
{ HolocaustWorld ---------------------------------------------------------------
   This procedure will carry out a nuclear attack on the population and
   industry of a world.
------------------------------------------------------------------------------ }
   VAR
      Indus: IndusArray;
      IndI: IndusTypes;
      RevChange,EnemyRev: Integer;
      EnemyEmp: Empire;
      Loc: Location;
      Pop: Population;

   FUNCTION WorldSurrenders(Emp: Empire; Effectiveness: Index; WorldID: IDNumber): Boolean;
      VAR
         Tech: TechLevel;
         Pop: Population;
         ChanceToSurrender: Real;

      BEGIN
      Tech:=GetTech(WorldID);
      Pop:=GetPopulation(WorldID);
      ChanceToSurrender:=(Sqr(Effectiveness/100));

      IF (Pop>1000) THEN
         ChanceToSurrender:=ChanceToSurrender/2;

      IF Tech<PreWrpLvl THEN
         WorldSurrenders:=True
      ELSE IF Tech>StrTchLvl THEN
         WorldSurrenders:=False
      ELSE IF Random(1)<=ChanceToSurrender THEN
         WorldSurrenders:=True
      ELSE
         WorldSurrenders:=False;
      END;  { WorldSurrenders }

   PROCEDURE CalculateDeaths(VAR Pop: Population; Effectiveness: Index;
                             VAR Deaths: Population);
      BEGIN
      Deaths:=Round((Pop/Rnd(850,1200))*Effectiveness);
      Pop:=Pop-Deaths;
      END;  { CalculateDeaths }

   PROCEDURE CalculateDestruction(VAR Indus: IndusArray; Effectiveness: Index;
                                  VAR IndusDest: IndusArray);
      VAR
         IndI: IndusTypes;
         PercentDamage: Real;

      BEGIN
      PercentDamage:=(Effectiveness/Rnd(200,800));
      FOR IndI:=BioInd TO TriInd DO
         BEGIN
         IndusDest[IndI]:=Round(Indus[IndI]*PercentDamage);
         Dec(Indus[IndI],IndusDest[IndI]);
         END;
      END;  { CalculateDestruction }

   PROCEDURE AttackerLosses(FltID: IDNumber; Effectiveness: Index;
                            VAR Losses: ShipArray);
      VAR
         ShpI: ShipTypes;
         FuelCap,PercentDamage: Real;
         Sh: ShipArray;

      BEGIN
      FillChar(Losses,SizeOf(Losses),0);
      PercentDamage:=(100-Effectiveness)/Rnd(600,1000);
      GetShips(FltID,Sh);

      FOR ShpI:=fgt TO trn DO
         IF (ShpI<>jtn) AND (ShpI<>trn) THEN
            BEGIN
            Losses[ShpI]:=Round(Sh[ShpI]*PercentDamage*CombatTable[GDM,ShpI]/100);
            Dec(Sh[ShpI],Losses[ShpI]);
            END;

      FuelCap:=FuelCapacity(Sh);
      IF GetFleetFuel(FltID)>FuelCap THEN
         SetFleetFuel(FltID,FuelCap);
      END;  { AttackLosses }

   PROCEDURE RevertTechnology(WorldID: IDNumber; Effectiveness: Index; 
                              VAR Revert: Boolean);
      BEGIN
      IF Rnd(1,100)<=(Effectiveness DIV 2) THEN
         BEGIN
         Revert:=True;
         SetTech(WorldID,PreTchLvl);
         END
      ELSE
         Revert:=False;
      END;  { RevertTechnology }

   { HolocaustWorld }
   BEGIN
   FillChar(Losses,SizeOf(Losses),0);
   FillChar(IndusDest,SizeOf(IndusDest),0);
   Deaths:=0;
   EnemyEmp:=GetStatus(WorldID);

   IF (EnemyEmp=Indep) AND WorldSurrenders(Emp,Effectiveness,WorldID) THEN
      BEGIN
      Result:=WorldSurrendersHRT;
      ConquerWorld(WorldID,Emp);

      { Change revolution of world }
      ChangeRevIndex(WorldID,Rnd(20,45));

      { Change revolution of empire }
      IF Rnd(1,100)<=50 THEN
         RevChange:=Rnd(15,25)
      ELSE
         RevChange:=-Rnd(1,10);
      END
   ELSE
      BEGIN
      Result:=WorldDestroyedHRT;

      Pop:=GetPopulation(WorldID);
      CalculateDeaths(Pop,Effectiveness,Deaths);
      SetPopulation(WorldID,Pop);

      GetIndus(WorldID,Indus);
      CalculateDestruction(Indus,Effectiveness,IndusDest);
      PutIndus(WorldID,Indus);

      SetEfficiency(WorldID,Rnd(1,10));

      RevertTechnology(WorldID,Deaths,Revert);

      AttackerLosses(FltID,Effectiveness,Losses);

      { Change revolution of enemy empire }
      EnemyRev:=(Deaths DIV 7)+Rnd(1,10);
      IF EnemyRev>30 THEN
         EnemyRev:=30;

      { Change revolution of empire }
      RevChange:=(Deaths DIV 6)+Rnd(-15,3);
      IF RevChange>50 THEN
         RevChange:=50;

      { Send news }
      Loc.ID:=WorldID;  Loc.XY:=Limbo;
      AddNews(EnemyEmp,WHolo,Loc,Ord(Emp),0,0);
      AddNews(EnemyEmp,DthHolo,Loc,Deaths,0,0);
      FOR IndI:=BioInd TO TriInd DO
         IF IndusDest[IndI]>0 THEN
            AddNews(EnemyEmp,IndDs,Loc,Indus[IndI],Ord(IndI),0);
      END;

   ChangeTotalRevIndex(Emp,RevChange);
   ChangeTotalRevIndex(EnemyEmp,EnemyRev);
   END;  { HolocaustWorld }

PROCEDURE LAMAttack(Player: Empire; LAMToUse: Resources; Target: IDNumber;
                    VAR ShipsDest: ShipArray;
                    VAR DefnsDest: DefnsArray);
   VAR
      ResI: ResourceTypes;
      Ships: ShipArray;
      Cargo: CargoArray;
      TotalShipSpace: Real;
      TotalDefns: LongInt;
      LAMsPerType: Resources;
      Loc: Location;
      Defns: DefnsArray;
      ChanceToDestroy: Integer;
      Emp: Empire;

   { LAMAttack }
   BEGIN
   FillChar(ShipsDest,SizeOf(ShipsDest),0);
   FillChar(DefnsDest,SizeOf(DefnsDest),0);
   Loc.ID:=Target;  Loc.XY:=Limbo;
   Emp:=GetStatus(Target);
   IF Target.ObjTyp=Flt THEN
      { ASSERT: Target is a fleet. }
      BEGIN
      GetShips(Target,Ships);
      TotalShipSpace:=0;
      FOR ResI:=fgt TO trn DO
         TotalShipSpace:=TotalShipSpace+(Ships[ResI]*(ProtecNeeded[ResI]/100));
      IF TotalShipSpace=0 THEN
         TotalShipSpace:=1;

      FOR ResI:=fgt TO trn DO
         BEGIN
         LAMsPerType:=Round(LAMToUse*((Ships[ResI]*(ProtecNeeded[ResI]/100))/TotalShipSpace));
         ShipsDest[ResI]:=LesserInt(Trunc((LAMsPerType/100)*CombatTable[LAM,ResI]),Ships[ResI]);
         Ships[ResI]:=Ships[ResI]-ShipsDest[ResI];
         END;

      IF NoShips(Ships) THEN
         BEGIN
         FleetNameDestruction(Emp,Target,Loc);
         AddNews(Emp,LAMDs,Loc,Integer(Player),0,0);
         DestroyFleet(Target);
         END
      ELSE
         BEGIN
         AddNews(GetStatus(Target),LAMDm,Loc,Integer(Player),0,0);
         GetCargo(Target,Cargo);
         BalanceFleet(Ships,Cargo);
         PutShips(Target,Ships);
         PutCargo(Target,Cargo);
         END;

      FOR ResI:=fgt TO trn DO
         IF ShipsDest[ResI]>0 THEN
            AddNews(Emp,DestDetail,Loc,Integer(ShipsDest[ResI]),Integer(ResI),0);
      END
   ELSE
      { ASSERT: Target is a world. }
      BEGIN
      AddNews(Emp,LAMDef,Loc,Integer(Player),0,0);
      GetDefns(Target,Defns);
      TotalDefns:=0;
      FOR ResI:=LAM TO ion DO
         Inc(TotalDefns,Defns[ResI]);
      IF TotalDefns=0 THEN
         TotalDefns:=1;

      FOR ResI:=LAM TO ion DO
         BEGIN
         LAMsPerType:=Round((LAMToUse/TotalDefns)*Defns[ResI]);
         DefnsDest[ResI]:=LesserInt(Trunc((LAMsPerType/100)*CombatTable[LAM,ResI]),Defns[ResI]);
         Dec(Defns[ResI],DefnsDest[ResI]);
         IF DefnsDest[ResI]>0 THEN
            AddNews(Emp,DestDetail,Loc,Integer(DefnsDest[ResI]),Integer(ResI),0);
         END;

      PutDefns(Target,Defns);
      END;

   AddGlobalNews([Emp,Player],Target,GLBLAMStrk,Loc,Ord(Player),Ord(Emp),0);
   END;  { LAMAttack }

PROCEDURE DestroyConstructionOrGate(Emp: Empire; ForcesUnk: Boolean; TargetID: IDNumber);
   VAR
      EnemyEmp: Empire;
      Loc: Location;

   BEGIN
   Loc.ID:=EmptyQuadrant;  GetCoord(TargetID,Loc.XY);
   EnemyEmp:=GetStatus(TargetID);

   CASE TargetID.ObjTyp OF
      Con: BEGIN
         DestroyConstruction(TargetID);
         ChangeTotalRevIndex(EnemyEmp,Rnd(3,7));
         IF ForcesUnk THEN
            AddNews(EnemyEmp,ConDsUNK,Loc,0,0,0)
         ELSE
            AddNews(EnemyEmp,ConDs,Loc,Ord(Emp),0,0);
         END;
      Gate: BEGIN
         DestroyStargate(TargetID);
         ChangeTotalRevIndex(EnemyEmp,Rnd(7,15));
         IF ForcesUnk THEN
            AddNews(EnemyEmp,GteDsUNK,Loc,0,0,0)
         ELSE
            AddNews(EnemyEmp,GteDs,Loc,Ord(Emp),0,0);
         END;
      END;
   END;  { DestroyConstructionOrGate }

END.
