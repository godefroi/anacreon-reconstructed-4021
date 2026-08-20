(* LOADSAVE.PAS ----------------------------------------------------------------
----------------------------------------------------------------------------- *)

UNIT LoadSave;

{$D+,L+}

{$IFDEF Overlay}
{$F+}
{$O+}
{$ENDIF}

INTERFACE

USES
   Strg;                                      { String primitives }

TYPE
   FilenameStr = STRING [64];

FUNCTION LoadGame(Filename: FilenameStr): Word;
FUNCTION SaveGame(Filename: FilenameStr): Word;
PROCEDURE InitializeUniverse(StartingYear: Integer;
                             Size: Byte;
                             Planets: Byte);
PROCEDURE CleanUpUniverse;

IMPLEMENTATION

USES
   Dos2,
   Types,
   DataStrc,                                  { Data Structure }
   DataCnst,
   Misc,
   PrimIntr,
   Intrface,
   News,
   Mess,
   TMA,
   Environ,
   NPETypes,
   NPE,
   Galaxy,
   EIO,
   Wnd,
(*
   Artifact,
   Transact,
*)
	Orders,
	Fleet;

CONST
   SFSignature = 'Anacreon save file v2.0'#13#10#26'      ';
   CurrentSFVersion = 15;

VAR
   SFVersion: Word;

{ Header ------------------------------------------------------------------- }

FUNCTION LoadHeader(VAR SF: FILE; VAR Sign: String32): Word;
   VAR
      Error: Word;

   BEGIN
   Sign[0]:=Char(32);
   Error:=ReadVariable(SF,Sign[1],32);
   Error:=ReadVariable(SF,SFVersion,2);

   LoadHeader:=Error;
   END;  { LoadHeader }

FUNCTION SaveHeader(VAR SF: FILE): Word;
   VAR
      Error: Word;
      Sign: String32;
      Version: Word;

   BEGIN
   Sign:=SFSignature;
   Version:=CurrentSFVersion;

   Error:=WriteVariable(SF,Sign[1],32);
   Error:=WriteVariable(SF,Version,2);

   SaveHeader:=Error;
   END;  { SaveHeader }

{ Planets ------------------------------------------------------------------ }

FUNCTION SavePlanets(VAR SF: FILE): Word;
   VAR
      Error,Size,i: Word;

   BEGIN
   FOR i:=1 TO NoOfPlanets DO
      BEGIN
      Error:=WriteVariable(SF,i,2);
      Error:=WriteVariable(SF,Universe^.Planet[i],SizeOf(PlanetRecord));
      END;

   i:=0;
   Error:=WriteVariable(SF,i,2);

   SavePlanets:=Error;
   END;  { SavePlanets }

FUNCTION LoadPlanets(VAR SF: FILE): Word;
   VAR
      Error,Size,i,Index: Word;
      Emp: Empire;
      TestString: String64;

   BEGIN
   SetOfActivePlanets:=[];
   FOR Emp:=Empire1 TO Indep DO
      SetOfPlanetsOf[Emp]:=[];

   Error:=ReadVariable(SF,Index,2);

   NoOfPlanets:=0;
   WHILE (Index>0) DO
      BEGIN
      Inc(NoOfPlanets);
     
      IF (SFVersion > 13) THEN 
        Error:=ReadVariable(SF,Universe^.Planet[Index],SizeOf(PlanetRecord))
      ELSE { We won't have stored TerraformTarget }
        WITH Universe^.Planet[Index] DO
         BEGIN
	  Error:=ReadVariable(SF,XY,SizeOf(XY));
	  Error:=ReadVariable(SF,Emp,SizeOf(Emp));
	  Error:=ReadVariable(SF,ScoutedBy,SizeOf(ScoutedBy));
	  Error:=ReadVariable(SF,KnownBy,SizeOf(KnownBy));
	  Error:=ReadVariable(SF,Cls,SizeOf(Cls));
            IF Cls = TerCls THEN Cls:=VlcCls;
	  Error:=ReadVariable(SF,Typ,SizeOf(Typ));
	  Error:=ReadVariable(SF,ImpExp,SizeOf(ImpExp));
	  Error:=ReadVariable(SF,Tech,SizeOf(Tech));
	  Error:=ReadVariable(SF,Eff,SizeOf(Eff));
	  Error:=ReadVariable(SF,RevIndex,SizeOf(RevIndex));
	  Error:=ReadVariable(SF,Special,SizeOf(Special));
	  Error:=ReadVariable(SF,Pop,SizeOf(Pop));
	  Error:=ReadVariable(SF,Ships,SizeOf(Ships));
	  Error:=ReadVariable(SF,Cargo,SizeOf(Cargo));
	  Error:=ReadVariable(SF,Defns,SizeOf(Defns));
	  Error:=ReadVariable(SF,Indus,SizeOf(Indus));
	  Error:=ReadVariable(SF,TriReserve,SizeOf(TriReserve));
	  Error:=ReadVariable(SF,Reserved,SizeOf(Reserved));
	  Error:=ReadVariable(SF,NextID,SizeOf(NextID));
          TerraformTarget:=AmbCls;

         END;
      WITH Universe^.Planet[Index] DO
         BEGIN
         SetOfActivePlanets:=SetOfActivePlanets+[Index];
         SetOfPlanetsOf[Emp]:=SetOfPlanetsOf[Emp]+[Index];
         END;

      Error:=ReadVariable(SF,Index,2);
      END;

   LoadPlanets:=Error;
   END;  { LoadPlanets }

{ Starbases ---------------------------------------------------------------- }

FUNCTION SaveStarbases(VAR SF: FILE): Word;
   VAR
      Error,i: Word;

   BEGIN
   FOR i:=1 TO MaxNoOfStarbases DO
      IF i IN SetOfActiveStarbases THEN
         BEGIN
         Error:=WriteVariable(SF,i,2);
         Error:=WriteVariable(SF,Universe^.Starbase[i],SizeOf(StarbaseRecord));
         END;

   i:=0;
   Error:=WriteVariable(SF,i,2);

   SaveStarbases:=Error;
   END;  { SaveStarbases }

FUNCTION LoadStarbases(VAR SF: FILE): Word;
   VAR
      Error,NoOfStarbases,i,Index: Word;
      Emp: Empire;

   BEGIN
   SetOfActiveStarbases:=[];
   FOR Emp:=Empire1 TO Indep DO
      SetOfStarbasesOf[Emp]:=[];

   Error:=ReadVariable(SF,Index,2);

   WHILE (Index>0) DO
      BEGIN
      Error:=ReadVariable(SF,Universe^.Starbase[Index],SizeOf(StarbaseRecord));
      WITH Universe^.Starbase[Index] DO
         BEGIN
         SetOfActiveStarbases:=SetOfActiveStarbases+[Index];
         SetOfStarbasesOf[Emp]:=SetOfStarbasesOf[Emp]+[Index];
         END;

      Error:=ReadVariable(SF,Index,2);
      END;

   LoadStarbases:=Error;
   END;  { LoadStarbases }

{ Fleets ------------------------------------------------------------------- }

FUNCTION SaveFleets(VAR SF: FILE): Word;
   VAR
      i,j,NoOfComs,Error: Word;
      FltID: IDNumber;
      Code: OrderStructure;
      Command: CommandRecord;
      
   BEGIN
   FltID.ObjTyp:=Flt;
   FOR i:=1 TO MaxNoOfFleets DO
      IF i IN SetOfActiveFleets THEN
         BEGIN
         FltID.Index:=i;
         Error:=WriteVariable(SF,i,2);
         Error:=WriteVariable(SF,Universe^.Fleet[i]^,SizeOf(FleetRecord));

         IF FleetNextStatement(FltID)>0 THEN
            BEGIN
            GetFleetCode(FltID,Code);
            NoOfComs:=NumberOfCommands(Code);
            Error:=WriteVariable(SF,NoOfComs,2);
            FOR j:=1 TO NoOfComs DO
               BEGIN
               GetCommandRecord(Code,j,Command);
               Error:=WriteVariable(SF,Command,SizeOf(Command));
               END;
            END
         ELSE
            BEGIN
            NoOfComs:=0;
            Error:=WriteVariable(SF,NoOfComs,2);
            END;
         END;

   i:=0;
   Error:=WriteVariable(SF,i,2);
   SaveFleets:=Error;
   END;  { SaveFleets }

FUNCTION LoadFleets(VAR SF: FILE): Word;
   VAR
      Index,Error,OrdersToLoad: Word;
      Code: OrderStructure;
      Command: CommandRecord;
      FltID: IDNumber;
      Emp: Empire;

   BEGIN
   SetOfActiveFleets:=[];
   FOR Emp:=Empire1 TO Indep DO
      SetOfFleetsOf[Emp]:=[];

   FltID.ObjTyp:=Flt;
   Error:=ReadVariable(SF,Index,2);

   WHILE (Index>0) DO
      BEGIN
      New(Universe^.Fleet[Index]);
      Error:=ReadVariable(SF,Universe^.Fleet[Index]^,SizeOf(FleetRecord));

      WITH Universe^.Fleet[Index]^ DO
         BEGIN
			IF (Dest.x=0) OR (Dest.y=0) OR (XY.x=0) OR (XY.y=0) THEN
				BEGIN
				Dest.x:=1;
				Dest.y:=1;
				XY.x:=1;
				XY.y:=1;
				END;

         SetOfFleetsOf[Emp]:=SetOfFleetsOf[Emp]+[Index];
         SetOfActiveFleets:=SetOfActiveFleets+[Index];
         END;

      { read orders data }
      Error:=ReadVariable(SF,OrdersToLoad,2);
      InitializeOrders(Code);
      WHILE OrdersToLoad>0 DO
         BEGIN
         Error:=ReadVariable(SF,Command,SizeOf(Command));
         AddOrders(Code,Command);
         Dec(OrdersToLoad);
         END;
      FltID.Index:=Index;
      SetFleetCode(FltID,Code);

      Error:=ReadVariable(SF,Index,2);
      END;

   LoadFleets:=Error;
   END;  { LoadFleets }

{ Stargates ---------------------------------------------------------------- }

FUNCTION LoadStargates(VAR SF: FILE): Word;
   VAR
      Index,Error: Word;
      EmpI: Empire;

   BEGIN
   SetOfActiveGates:=[];
   Error:=ReadVariable(SF,Index,2);

   WHILE Index>0 DO
      BEGIN
      IF (SFVersion > 14) THEN
       Error:=ReadVariable(SF,Universe^.Stargate[Index],SizeOf(StargateRecord))
      ELSE
        WITH Universe^.Stargate[Index] DO
         BEGIN
	  Error:=ReadVariable(SF,XY,SizeOf(XY));
	  Error:=ReadVariable(SF,Emp,SizeOf(Emp));
	  Error:=ReadVariable(SF,ScoutedBy,SizeOf(ScoutedBy));
	  Error:=ReadVariable(SF,KnownBy,SizeOf(KnownBy));
	  Error:=ReadVariable(SF,GTyp,SizeOf(GTyp));
	  Error:=ReadVariable(SF,Dest,SizeOf(Dest));
          FOR EmpI:=Empire1 TO Indep DO
            WLF[EmpI]:=Random(9999);
	  Error:=ReadVariable(SF,NextID,SizeOf(NextID));
         END;

      SetOfActiveGates:=SetOfActiveGates+[Index];

      Error:=ReadVariable(SF,Index,2);
      END;

   LoadStargates:=Error;
   END;  { LoadStargates }

FUNCTION SaveStargates(VAR SF: FILE): Word;
   VAR
      Index,Error: Word;

   BEGIN
   FOR Index:=1 TO MaxNoOfStargates DO
      IF Index IN SetOfActiveGates THEN
         BEGIN
         Error:=WriteVariable(SF,Index,2);
         Error:=WriteVariable(SF,Universe^.Stargate[Index],SizeOf(StargateRecord));
         END;

   Index:=0;
   Error:=WriteVariable(SF,Index,2);

   SaveStargates:=Error;
   END;  { SaveStargates }

{ Constructions ------------------------------------------------------------ }

FUNCTION LoadConstr(VAR SF: FILE): Word;
   VAR
      Index,Error: Word;
      Emp: Empire;

   BEGIN
   SetOfActiveConstructionSites:=[];
   FOR Emp:=Empire1 TO Indep DO
      SetOfConstructionSitesOf[Emp]:=[];

   Error:=ReadVariable(SF,Index,2);

   WHILE Index>0 DO
      BEGIN
      Error:=ReadVariable(SF,Universe^.Constr[Index],SizeOf(ConstrRecord));
      WITH Universe^.Constr[Index] DO
         BEGIN
         SetOfActiveConstructionSites:=SetOfActiveConstructionSites+[Index];
         SetOfConstructionSitesOf[Emp]:=SetOfConstructionSitesOf[Emp]+[Index];
         END;

      Error:=ReadVariable(SF,Index,2);
      END;

   LoadConstr:=Error;
   END;  { LoadConstr }

FUNCTION SaveConstr(VAR SF: FILE): Word;
   VAR
      Index,Error: Word;

   BEGIN
   FOR Index:=1 TO MaxNoOfConstrSites DO
      IF Index IN SetOfActiveConstructionSites THEN
         BEGIN
         Error:=WriteVariable(SF,Index,2);
         Error:=WriteVariable(SF,Universe^.Constr[Index],SizeOf(ConstrRecord));
         END;

   Index:=0;
   Error:=WriteVariable(SF,Index,2);

   SaveConstr:=Error;
   END;  { SaveConstr }

{ Empire Data -------------------------------------------------------------- }

FUNCTION LoadEmpireData(VAR SF: FILE): Word;
   VAR
      Emp: Empire;
      i,NoOfNames: Byte;
      NextName: NameRecordPtr;
      Error: Word;

   BEGIN
   FOR Emp:=Empire1 TO Empire8 DO
      BEGIN
      Error:=ReadVariable(SF,Universe^.EmpireData[Emp],SizeOf(EmpireDataRecord));
      WITH Universe^.EmpireData[Emp] DO
         BEGIN
         Error:=ReadVariable(SF,NoOfNames,1);
         IF NoOfNames>0 THEN
            BEGIN
            New(Names);
            NextName:=Names;

            FOR i:=1 TO NoOfNames DO
               BEGIN
               Error:=ReadVariable(SF,NextName^,SizeOf(NameRecord));
               IF i<NoOfNames THEN
                  BEGIN
                  New(NextName^.Next);
                  NextName:=NextName^.Next;
                  END;
               END;

            LastName:=NextName;
            NextName^.Next:=Nil;
            END
         ELSE
            BEGIN
            Names:=Nil;
            LastName:=Nil;
            END;

         END;  { with scope }
      END;  { loop }

   LoadEmpireData:=Error;
   END;  { LoadEmpireData }

FUNCTION SaveEmpireData(VAR SF: FILE): Word;
   VAR
      Emp: Empire;
      NoOfNames: Byte;
      Error: Word;
      NextName: NameRecordPtr;

   BEGIN
   FOR Emp:=Empire1 TO Empire8 DO
      BEGIN
      Error:=WriteVariable(SF,Universe^.EmpireData[Emp],SizeOf(EmpireDataRecord));

      WITH Universe^.EmpireData[Emp] DO
         BEGIN
         NextName:=Names;
         NoOfNames:=0;

         WHILE NextName<>Nil DO
            BEGIN
            NoOfNames:=NoOfNames+1;
            NextName:=NextName^.Next;
            END;

         Error:=WriteVariable(SF,NoOfNames,1);

         NextName:=Names;

         WHILE NextName<>Nil DO
            BEGIN
            Error:=WriteVariable(SF,NextName^,SizeOf(NameRecord));
            NextName:=NextName^.Next;
            END;

         END;  { with scope }
      END;  { loop }

   SaveEmpireData:=Error;
   END;  { SaveEmpireData }

{ Non-Player Empire Data --------------------------------------------------- }

FUNCTION LoadNPEData(VAR SF: FILE; Version: Word): Word;
   VAR
      Emp: Empire;
      Error: Word;

   BEGIN
   Error:=ReadVariable(SF,NPEData,SizeOF(NPEData));
   FOR Emp:=Empire1 TO Empire8 DO
      IF EmpireActive(Emp) AND (NOT EmpirePlayer(Emp)) THEN
         LoadNPE(SF,Emp,Version,Error);

   LoadNPEData:=Error;
   END;  { LoadNPEData }

FUNCTION SaveNPEData(VAR SF: FILE): Word;
   VAR
      Emp: Empire;
      Error: Word;

   BEGIN
   Error:=WriteVariable(SF,NPEData,SizeOF(NPEData));
   FOR Emp:=Empire1 TO Empire8 DO
      IF EmpireActive(Emp) AND (NOT EmpirePlayer(Emp)) THEN
         SaveNPE(SF,Emp,Error);

   SaveNPEData:=Error;
   END;  { SaveNPEData }

{ Main Procedures ========================================================== }

PROCEDURE InitializeUniverse(StartingYear: Integer;
                             Size: Byte;
                             Planets: Byte);
{ InitializeUniverse:
   This procedure sets up the main data structure.  Initially the universe
   is blank.  There are no active planets, starbase, constructions, gates, or
   fleets.  The galaxy is empty, no nebulae or mines.
   This procedure also initializes some global variables:  The universe starts 
   out on the given year, and the time per year is set at 5 minutes (300 sec.) }

   VAR
      Emp: Empire;
   
   procedure InitializeIndependentRecord;
   { InitializeIndependentRecord:
      This procedure fills in the data for planets that are independent.
      }

      begin
      with Universe^.EmpireData[Indep] do
         begin
         EmpireName:='Independent';
         DefenseSettings:=InitDefenseRecord;
         end;  { with scope }
      end;  { InitializeIndependentRecord }

   { InitializeUniverse^: MAIN PROCEDURE }
   BEGIN
   WITH Universe^ DO
      BEGIN
      FillChar(Universe^,SizeOf(Universe^),0);
      FillChar(GlobalSets,SizeOf(GlobalSets),0);
      END;  { with scope }

   Year:=StartingYear;
   TimePerTurn:=300;

   IF Planets>MaxNoOfPlanets THEN
      NoOfPlanets:=MaxNoOfPlanets
   ELSE
      NoOfPlanets:=Planets;

   InitializeSector(Size);
   InitializeIndependentRecord;

   FOR Emp:=Empire1 TO Empire8 DO
      EraseNews(Emp);

(*
   InitializeArtifacts;
   InitializeTransactions;
*)
   END;  { InitializeUniverse }

PROCEDURE CleanUpUniverse;
{ CleanUpUniverse:
   This procedure disposes of all dynamic variables allocated for the
   data base. }

   VAR
      Emp: Empire;
      i: Word;
      Code: OrderStructure;
      FltID: IDNumber;

   BEGIN
   DeleteAllMessages;

   FltID.ObjTyp:=Flt;
   FOR i:=1 TO MaxNoOfFleets DO
      IF i IN SetOfActiveFleets THEN
         BEGIN
         FltID.Index:=i;
			DestroyFleet(FltID);
         END;

   FOR Emp:=Empire1 TO Empire8 DO
      IF EmpireActive(Emp) THEN
         BEGIN
         EraseNews(Emp);
         DeleteAllNames(Emp);

         IF NOT EmpirePlayer(Emp) THEN
            CleanUpNPE(Emp);
         END;

   CleanUpSector;
   END;  { CleanUpUniverse }

FUNCTION LoadGame(Filename: FilenameStr): Word;
   LABEL
      FileError;

   VAR
      SF: FILE;
      HeaderSign: String32;
      Error: Word;
      Window: Byte;

   { LoadGame: MAIN PROCEDURE }
   BEGIN
   InitializeUniverse(0,0,0);

   Assign(SF,Filename);
   {$I-}
   Reset(SF,1);
   {$I+}
   Error:=IOResult;

   IF Error=0 THEN
      { ASSERT: File opened ok. }
      BEGIN
      Error:=LoadHeader(SF,HeaderSign);
      IF Error<>0 THEN GOTO FileError;

      IF (SFVersion<CurrentSFVersion) THEN
	BEGIN
          OpenWindow(15,12,50,8,ThinBRD,'Warning!',C.CommWind,C.CommWind,Window);
            WriteLn;
            WriteLn(' The savegame format has changed.  If you  ');
            WriteLn(' save this game over the old save file, you');
            WriteLn(' will not be able to re-open it in earlier');
            WriteLn(' versions of Anacreon.');
            PressAnyKey(1,1,'');
          CloseWindow;
	END;
      
      IF (SFVersion>CurrentSFVersion) THEN
	BEGIN
          OpenWindow(15,12,50,7,ThinBRD,'Warning!',C.CommWind,C.CommWind,Window);
            WriteLn;
            WriteLn(' This save file was created by a version of');
            WriteLn(' Anacreon that is newer than this version. ');
            WriteLn(' It may not load properly.');
            PressAnyKey(1,1,'');
          CloseWindow;
	END;

      Error:=LoadEnvironment(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=LoadSector(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=LoadPlanets(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=LoadStarbases(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=LoadFleets(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=LoadStargates(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=LoadConstr(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=LoadMessageData(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=LoadEmpireData(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=LoadNewsData(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=LoadNPEData(SF,SFVersion);
      IF Error<>0 THEN GOTO FileError;

      { If control gets to this point, then there are no errors. }
      Close(SF);
      Error:=0;
      END;

   LoadGame:=Error;
   Exit;

   { This part of the procedure handles an error in an open file.  Control
     is never passed to this section unless by a GOTO. }
   FileError:
   Close(SF);
   LoadGame:=Error;
   END;  { LoadGame }

FUNCTION SaveGame(Filename: FilenameStr): Word;
   LABEL
      FileError;

   VAR
      SF: FILE;
      Error: Word;

   { SaveGame: MAIN PROCEDURE }
   BEGIN
   Assign(SF,Filename);
   {$I-}
   Rewrite(SF,1);
   {$I+}
   Error:=IOResult;

   IF Error=0 THEN
      { ASSERT: File opened ok. }
      BEGIN
      Error:=SaveHeader(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=SaveEnvironment(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=SaveSector(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=SavePlanets(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=SaveStarbases(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=SaveFleets(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=SaveStargates(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=SaveConstr(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=SaveMessageData(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=SaveEmpireData(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=SaveNewsData(SF);
      IF Error<>0 THEN GOTO FileError;

      Error:=SaveNPEData(SF);
      IF Error<>0 THEN GOTO FileError;

      { If control gets to this point, then there are no errors. }
      Close(SF);
      Error:=0;
      END;

   SaveGame:=Error;
   Exit;

   { This part of the procedure handles an error.  Control is never passed
     to this section unless by a GOTO. }
   FileError:
   Close(SF);
   Erase(SF);
   SaveGame:=Error;
   END;  { SaveGame }

END.
