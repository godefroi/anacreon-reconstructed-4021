(* ATTNPE.PAS ------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT AttNPE;

{DEFINE Debug}

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Int,
     CRT,
     Types,
     DataCnst,
     PrimIntr,
     Attack;

PROCEDURE NPEAttack(FltID,Target: IDNumber; Intent: AttackIntentionTypes; RetrIndex: Index;
                    VAR Result: AttackResultTypes);

IMPLEMENTATION

{$IFDEF Debug}
USES
   Wnd;

VAR
   DebugWind: WindowHandle;
{$ENDIF}

TYPE
   EngageStateTypes = ( NoEST, GeneralEST, HKAttackEST, AttackTrnEST );

   TargetRecord = RECORD
      TargTyp: AttackTypes;
      TargNum: Resources;
      Priority: Integer;
   END;
   TargetArray = ARRAY [1..20] OF TargetRecord;

PROCEDURE GetTargetArray(Pos: ShellPos;
                         VAR En: EnemyArray;
                         VAR NoOfTargets: Word;
                         VAR Target: TargetArray);
   VAR
      AttI: AttackTypes;

   BEGIN
   NoOfTargets:=0;
   FOR AttI:=LAM TO nnj DO
      IF En[Pos,AttI]>0 THEN
         BEGIN
         Inc(NoOfTargets);
         WITH Target[NoOfTargets] DO
            BEGIN
            TargTyp:=AttI;
            TargNum:=En[Pos,AttI];
            Priority:=0;
            END;
         END;
   END;  { GetTargetArray }

PROCEDURE PrioritizeTargetArray(AttTyp: AttackTypes;
                                AttNum: Resources;
                                Intent: AttackIntentionTypes;
                                NoOfTargets: Word;
                                VAR Target: TargetArray);
   VAR
      i: Word;
      Total: Real;

   BEGIN
   FOR i:=1 TO NoOfTargets DO
      WITH Target[i] DO
         BEGIN
         Total:=(TargNum/1000)*(CombatTable[AttTyp,TargTyp]/100)*CombatPower[TargTyp];
         IF Total<2 THEN
            Total:=2;
         IF (Intent=CaptTrnAIT) AND (TargTyp IN [trn,jtn]) THEN
            Total:=1
         ELSE IF (Intent=DestTrnAIT) AND (NOT (TargTyp IN [trn,jtn])) THEN
            Total:=1;

         Priority:=IntLmt(Total);
         END;  { with scope and loop }
   END;  { PrioritizeTargetArray }

PROCEDURE GetBestTarget(NoOfTargets: Word; VAR Target: TargetArray;
                        VAR BestTarg: AttackTypes);
   VAR
      i: Word;
      HighestPriority: Integer;

   BEGIN
   HighestPriority:=0;
   FOR i:=1 TO NoOfTargets DO
      WITH Target[i] DO
         IF Priority>=HighestPriority THEN
            BEGIN
            HighestPriority:=Priority;
            BestTarg:=TargTyp;
            END;
   END;  { GetBestTarget }

PROCEDURE Consolidate(NoOfGroups: Byte; VAR Gp: GroupArray);
   BEGIN
   END;  { Consolidate }

PROCEDURE AllAdvance(NoOfGroups: Byte; VAR Gp: GroupArray);
   VAR
      i: Word;

   BEGIN
   FOR i:=1 TO NoOfGroups DO
      WITH Gp[i] DO
         IF (Sta<>GDst) AND (Pos<SbOrb) AND (Typ IN [fgt..jmp,pen,ssp]) THEN
            Sta:=GAdvc;
   END;  { AllAdvance }

PROCEDURE TrnAdvance(NoOfGroups: Byte; VAR Gp: GroupArray);
   VAR
      i: Word;

   BEGIN
   FOR i:=1 TO NoOfGroups DO
      WITH Gp[i] DO
         IF (Sta<>GDst) AND (Pos<Grnd) AND (Typ IN [fgt,jtn,trn]) THEN
            Sta:=GAdvc;
   END;  { TrnAdvance }

PROCEDURE GroupEngage(NoOfGroups: Word;
                      VAR Gp: GroupArray;
                      VAR En: EnemyArray;
                      CombatData: CombatDataRecord;
                      VAR Killed,Casualties: AttackArray;
                      VAR Result: AttackResultTypes);
      VAR
         P: ShellPos;
         Details: DetailArray;
         GroupsDestroyed: GroupSet;

   BEGIN
   FillChar(Details,SizeOf(Details),0);
   FOR P:=DpSpc TO Grnd DO
      Battle(NoOfGroups,Gp,GroupsDestroyed,En,P,CombatData,Details,Casualties,Killed);

   AdvanceGroups(NoOfGroups,Gp);

   IF AllGroupsDestroyed(NoOfGroups,Gp) THEN
      Result:=AttDestroyedART
   ELSE IF Result<>AttRetreatsART THEN
      IF EnemySurrenders(NoOfGroups,Gp,En,Casualties,Killed,CombatData) THEN
         Result:=DefConqueredART;
   END;  { GroupEngage }

PROCEDURE FleetRetreats(NoOfGroups: Byte; VAR Gp: GroupArray;
                        VAR En: EnemyArray;
                        CombatData: CombatDataRecord;
                        EngageRound: Word;
                        RetrIndex: Index;
                        VAR Result: AttackResultTypes);
   VAR
      Retreat: Boolean;

   FUNCTION NoMenLeft(NoOfGroups: Byte; VAR Gp: GroupArray): Boolean;
      VAR
         i: Word;

      BEGIN
      i:=NoOfGroups;
      WHILE (i>0) AND ((Gp[i].Sta=GDst) OR (Gp[i].Typ IN [fgt..trn])) DO
         Dec(i);

      IF i=0 THEN
         NoMenLeft:=True
      ELSE
         NoMenLeft:=False;
      END;  { NoMenLeft }

   BEGIN
   IF (EngageRound>30) AND (NoMenLeft(NoOfGroups,Gp)) THEN
      BEGIN
      Retreat:=True;
      Result:=AttRetreatsART;
      END
   ELSE
      Retreat:=False;
   END;  { FleetRetreats }

FUNCTION TransportsLeft(NoOfGroups: Byte; VAR Gp: GroupArray): Boolean;
   VAR
      i: Word;

   BEGIN
   i:=NoOfGroups;
   WHILE (i>0) AND ((Gp[i].Sta=GDst) OR (Gp[i].Typ IN [jtn,trn])) DO
      Dec(i);

   IF i=0 THEN
      TransportsLeft:=True
   ELSE
      TransportsLeft:=False;
   END;  { TransportsLeft }

{$IFDEF Debug}
PROCEDURE GroupStatus(NoOfGroups: Byte; VAR Gp: GroupArray);
   VAR
      i: Word;

   BEGIN
   FOR i:=1 TO NoOfGroups DO
      WITH Gp[i] DO
         WriteLn(i,': ',TechnologyName[Typ],' ',Num,' ',Ord(Pos),' T:',Copy(TechnologyName[Trg],1,6));
   END;  { GroupStatus }
{$ENDIF}

PROCEDURE FleetEngage(NoOfGroups: Byte; VAR Gp: GroupArray; VAR En: EnemyArray;
                      CombatData: CombatDataRecord;
                      RetrIndex: Index;
                      Intent: AttackIntentionTypes;
                      VAR Casualties,Killed: AttackArray;
                      VAR Result: AttackResultTypes);
   VAR
      EngageRound: Word;

   PROCEDURE Targetting(NoOfGroups: Byte; VAR Gp: GroupArray; 
                        VAR En: EnemyArray;
                        CombatData: CombatDataRecord;
                        Intent: AttackIntentionTypes);
      VAR
         Check,TargetAvailable,OnSameOrbit: Boolean;
         i,NoOfTargets: Word;
         Target: TargetArray;
         GeneralPos: ShellPos;

      BEGIN
      TargetAvailable:=False;
      OnSameOrbit:=True;
      Check:=False;

      FOR i:=1 TO NoOfGroups DO
         WITH Gp[i] DO
            IF Sta<>GDst THEN
               BEGIN
               IF Check THEN
                  BEGIN
                  IF GeneralPos<>Pos THEN
                     OnSameOrbit:=False;
                  END
               ELSE
                  BEGIN
                  GeneralPos:=Pos;
                  Check:=True;
                  END;

               GetTargetArray(Pos,En,NoOfTargets,Target);
               IF NoOfTargets>0 THEN
                  BEGIN
                  TargetAvailable:=True;
                  PrioritizeTargetArray(Typ,Num,Intent,NoOfTargets,Target);
                  GetBestTarget(NoOfTargets,Target,Trg);
                  END;
               END;

      IF (NOT TargetAvailable) AND (GeneralPos<>SbOrb) THEN
         AllAdvance(NoOfGroups,Gp);
      END;  { Targetting }

   { FleetEngage: MAIN PROCEDURE }
   BEGIN
   FillChar(Killed,SizeOf(Killed),0);
   FillChar(Casualties,SizeOf(Casualties),0);
   Result:=NoART;
   EngageRound:=0;

   REPEAT
      Inc(EngageRound);
      FleetRetreats(NoOfGroups,Gp,En,CombatData,EngageRound,RetrIndex,Result);
      Targetting(NoOfGroups,Gp,En,CombatData,Intent);
      GroupEngage(NoOfGroups,Gp,En,CombatData,Killed,Casualties,Result);
   UNTIL Result<>NoART;
   END;  { FleetEngage }

PROCEDURE WorldEngage(NoOfGroups: Byte;
                      VAR Gp: GroupArray;
                      VAR En: EnemyArray;
                      CombatData: CombatDataRecord;
                      RetrIndex: Index;
                      Intent: AttackIntentionTypes;
                      VAR Casualties,Killed: AttackArray;
                      VAR Result: AttackResultTypes);

   VAR
      EngageRound: Word;

   PROCEDURE Targetting(NoOfGroups: Byte; VAR Gp: GroupArray; 
                        VAR En: EnemyArray;
                        CombatData: CombatDataRecord;
                        Intent: AttackIntentionTypes);
      VAR
         Check,TargetAvailable,OnSameOrbit: Boolean;
         i,NoOfTargets: Word;
         Target: TargetArray;
         LowestPos,GeneralPos: ShellPos;

      BEGIN
      TargetAvailable:=False;
      OnSameOrbit:=True;
      Check:=False;

      FOR i:=1 TO NoOfGroups DO
         WITH Gp[i] DO
            IF Sta<>GDst THEN
               BEGIN
               IF Check THEN
                  BEGIN
                  IF GeneralPos<>Pos THEN
                     OnSameOrbit:=False;
                  IF Pos>LowestPos THEN
                     LowestPos:=Pos;
                  END
               ELSE
                  BEGIN
                  GeneralPos:=Pos;
                  LowestPos:=Pos;
                  Check:=True;
                  END;

               GetTargetArray(Pos,En,NoOfTargets,Target);
               IF NoOfTargets>0 THEN
                  BEGIN
                  TargetAvailable:=True;
                  PrioritizeTargetArray(Typ,Num,Intent,NoOfTargets,Target);
                  GetBestTarget(NoOfTargets,Target,Trg);
                  END;

               IF Pos=Grnd THEN
                  Trg:=men;
               END;

      IF (NOT TargetAvailable) THEN
         BEGIN
         IF (LowestPos=SbOrb) OR TransportsLeft(NoOfGroups,Gp) THEN
            TrnAdvance(NoOfGroups,Gp)
         ELSE
            AllAdvance(NoOfGroups,Gp);
         END;
      END;  { Targetting }

   { WorldEngage: MAIN PROCEDURE }
   BEGIN
   FillChar(Killed,SizeOf(Killed),0);
   FillChar(Casualties,SizeOf(Casualties),0);
   Result:=NoART;
   EngageRound:=0;

   {$IFDEF Debug}
   OpenWindow(40,1,40,15,ThinBRD,'Debug',7,7,DebugWind);
   {$ENDIF}

   REPEAT
      Inc(EngageRound);
      FleetRetreats(NoOfGroups,Gp,En,CombatData,EngageRound,RetrIndex,Result);
      Targetting(NoOfGroups,Gp,En,CombatData,Intent);
      GroupEngage(NoOfGroups,Gp,En,CombatData,Killed,Casualties,Result);

      {$IFDEF Debug}
      ClrScr;
      WriteLn('Round: ',EngageRound);
      WriteLn;
      GroupStatus(NoOfGroups,Gp);
      ReadLn;
      {$ENDIF}

   UNTIL Result<>NoART;

   {$IFDEF Debug}
   CloseWindow;
   {$ENDIF}
   END;  { WorldEngage }

PROCEDURE NPEAttack(FltID,Target: IDNumber; Intent: AttackIntentionTypes; RetrIndex: Index;
                    VAR Result: AttackResultTypes);
   VAR
      CombatData: CombatDataRecord;
      Killed,Casualties: AttackArray;
      NoOfGroups: Byte;
      Gp: GroupArray;
      En: EnemyArray;
      Capture,HKSurprise: Boolean;
      AttEmp,DefEmp: Empire;
      Booty: PlanetSet;

   BEGIN
   HKSurprise:=ForcesUnknown(FltID,Target);
   AttEmp:=GetStatus(FltID);
   DefEmp:=GetStatus(Target);

   IF Target.ObjTyp IN [Pln,Base,Flt] THEN
      BEGIN
      CalculateCombatData(AttEmp,FltID,Target,CombatData);
      DefaultDistribution(FltID,NoOfGroups,Gp);
      GetEnemy(Target,En);

      IF Target.ObjTyp=Flt THEN
         FleetEngage(NoOfGroups,Gp,En,CombatData,RetrIndex,Intent,Casualties,Killed,Result)
      ELSE
         WorldEngage(NoOfGroups,Gp,En,CombatData,RetrIndex,Intent,Casualties,Killed,Result);

      RestoreCombatant(FltID,Casualties);
      RestoreCombatant(Target,Killed);

      IF Intent=DestTrnAIT THEN Capture:=False ELSE Capture:=True;
      ResolveAttack(Result,FltID,Target,HKSurprise,Capture,Casualties,Killed,Booty);
      END
   ELSE IF Target.ObjTyp IN [Con,Gate] THEN
      BEGIN
      DestroyConstructionOrGate(AttEmp,HKSurprise,Target);
      Result:=DefConqueredART;
      END;
   END;  { NPEAttack }

END.
