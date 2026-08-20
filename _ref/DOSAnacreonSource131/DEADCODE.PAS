PROCEDURE GetCapitals(Empires: EmpireSet; MinDist,MaxSingleDist,MaxDist: Byte;
                      VAR CapitalID: CapitalArray);
{ GetCapitals:
   This procedure will return ID numbers for empire capitals, one for every
   empire in Empires.  Each capital will be at least MinDist sectors appart.  
   At least one capital will be within MaxSingleDist sectors away, and every
   capital will be within MaxDist of one another. }

   LABEL
      StartOver;

   VAR
      CapitalAtRightDistance,Found: Boolean;
      Emp,SetEmp: Empire;
      PlanetID: IDNumber;
      Coord1,Coord2: XYCoord;
      Count: Integer;
      NextPlanet: Byte;

   BEGIN
   PlanetID.ObjTyp:=Pln;
   Found:=True;

   StartOver:

   PlanetID.Index:=1;
   NextPlanet:=2;
   CapitalID[Empire1]:=PlanetID;

   REPEAT
      FOR Emp:=Empire2 TO Empire8 DO
         IF Emp IN Empires THEN
            BEGIN
            REPEAT
               PlanetID.Index:=NextPlanet;
               GetCoord(PlanetID,Coord1);
               CapitalAtRightDistance:=True;
               FOR SetEmp:=Empire1 TO Pred(Emp) DO
                  IF SetEmp IN Empires THEN
                     BEGIN
                     GetCoord(CapitalID[SetEmp],Coord2);
                     IF (Distance(Coord1,Coord2)<MinDist) OR
                        (Distance(Coord1,Coord2)>MaxDist) THEN
                        CapitalAtRightDistance:=False;
                     END;

               NextPlanet:=Succ(NextPlanet);
               IF NextPlanet>NoOfPlanets THEN              { if too many loops then }
                  BEGIN
                  MinDist:=Pred(MinDist);
                  GOTO StartOver;             { something is wrong. }
                  END;
            UNTIL CapitalAtRightDistance;

            CapitalID[Emp]:=PlanetID;
            END;  { loop }

(*
      { check to see if at least one within MaxSingleDist }
      Found:=False;
      Emp:=Empire1;
      WHILE (NOT Found) AND (Emp<=Empire8) DO
         BEGIN
         GetCoord(CapitalID[Emp],Coord1);
         FOR SetEmp:=Empire1 TO Empire8 DO
            IF SetEmp IN (Empires-[Emp]) THEN
               BEGIN
               GetCoord(CapitalID[SetEmp],Coord2);
               IF Distance(Coord1,Coord2)<=MaxSingleDist THEN
                  Found:=True;
               END;

         Emp:=Succ(Emp);
         END;  { while }
*)

   UNTIL Found;
   END;  { GetCapitals }

PROCEDURE CloseStatusWindow(Wind: WindowTypes);
{ CloseStatusWindow:
   Closes the window.  CurrentWindow is updated }

   function NewCurrentWindow(WH: Byte): WindowTypes;
      var
         i: WindowTypes;
         temp: Windowtypes;

      begin
      temp:=HelpW;
      for i:=HelpW to NameW do
         if i in OpenWindows then
            if StatusWindow[i].Handle=WH then
               temp:=i;

      NewCurrentWindow:=temp;
      end;  { NewCurrentWindow }

   { CloseStatusWindow: MAIN PROCEDURE }
   begin
   OpenWindows:=OpenWindows-[Wind];
   with StatusWindow[Wind] do
      begin
      CloseWindow;
      CurrentWindow:=NewCurrentWindow(ActiveWindow);
      end;  { with scope }
   end;  { CloseStatusWindow }

   procedure SplitCommandLine(Line: LineStr;
                              VAR Parm: ParmArray; VAR Error: Boolean);
   { SplitCommandLine:
      SplitCommandLine takes 'Line' and splits it into words, each word
      separated by a space.  Each word is then put on to one of the elements
      of the array 'Parm' in order.  If the length of any parameter
      is greater than 16, or if there are more than MaxNoOfParameters,
      then 'Error' is true. }

      VAR
         a,b: Integer;

      begin
      a:=1;
      for b:=1 to MaxNoOfParameters do
         Parm[b]:='';
      Error:=False;
      for b:=1 to Length(Line) do
         begin { string loop }
         if not Error then
            begin
            if Line[b]=' ' then
               begin
               if (b<3) or (a>MaxNoOfParameters) then
                  Error:=True;
               a:=a+1;
               end
            else
               begin
               parm[a]:=parm[a]+Line[b];
               if Length(parm[a])>16 then
                  Error:=True;
               end; { else }
            end;
         end; { string loop }
      end; { SplitCommandLine }

   PROCEDURE ReadFleets(VAR IOError: Word);
      VAR
         i: Byte;

      BEGIN
      ReadVariable(i,1,IOError);
      FOR i:=1 TO MaxNoOfFleets DO
         BEGIN
         ReadVariable(Universe^.Fleet[i],SizeOf(FleetRecord),IOError);
         END;
      END;  { ReadFleets }

   PROCEDURE WriteFleets(VAR IOError: Word);
      VAR
         i: Byte;

      BEGIN
      i:=MaxNoOfFleets;
      WriteVariable(i,1,IOError);
      FOR i:=1 TO MaxNoOfFleets DO
         BEGIN
         WriteVariable(Universe^.Fleet[i],SizeOf(FleetRecord),IOError);
         END;
      END;  { WriteFleets }

(* -----------------------------------------------------------------------------
   NPENature = ( Marauder,Rebellion,
                 Overlords,Slavers,
                 Robots,Anihilators,GateKeepers,
                 Isolationist,Cautious,Neutral,Colonial,Imperialist,
                 Aggressive,Spartan );

   NPEObjTypes = ( ReEnfW,ConqW,ReEnfF,SupW );

   SpecialAbilities = ( EliteFighters,Precognition,AdvanceJump,AdvancedWarp,
                        NoJump );

   SpecialAbilitiesSet = set of SpecialAbilities;
----------------------------------------------------------------------------- *)

(* -------------------------------------------------------------------------- *)

(* Fleet Manager Data Structure --------------------------------------------- *)

FMFleetPurposes = ( FPBattle,
                    FPCargo,
                    FPTrans );

FMFleetDataRecord = RECORD
   Purpose: FMFleetPurposes;
END;
FMFleetDataArray = ARRAY [1..NoOfFleetsPerEmpire] OF FMFleetDataRecord;

FleetManagerRecord = RECORD
   FMData: FMFleetDataArray;
END;  { FleetManagerRecord }

(* -------------------------------------------------------------------------- *)

NPEmpireTypes = ( NPEStand,                     { standard empire }
                  NPEBerserk,                   { berserker empire }
                  NPERebel,                     { rebel empire }
                  NPEGate,                      { gate keepers }
                  NPEAmbrosia );                { ambrosia empire }

NPEPhaseTypes = ( InitPHS,                      { initialize }
                  ConsolPHS,                    { consolidate }
                  ExpandPHS,                    { expand }
                  EndGamePHS );                 { final conflict }

NPEWorldPtr = ^NPEWorldRecord;                  { pointer to NPE world data }
NPEWorldRecord = RECORD                         { world data record }
   ID: IDNumber;
   Imports: ResourceSet;
   Exports: ResourceSet;
   LastCargoReq: Byte;                          { years since last cargo req. }
   Next: NPEWorldPtr;
END;

RegionPtr = ^RegionRecord;                      { pointer to region data }
RegionRecord = RECORD
   WorldsInRegion: PlanetSet;                   { set of worlds in region }
   FirstWorld: NPEWorldPtr;                     { head of world linked list }
   Dist: Byte;                                  { distance from capital }
   Next: RegionPtr;
END;

NPEWorldIndexArray = ARRAY [1..MaxNoOfPlanets] OF NPEWorldPtr;
WPArray = ARRAY [1..MaxNoOfPlanets] OF Index;

NPECharacterRecord = RECORD
   Aggressive: Index;                           { aggressiveness 100=high }
   Defensive: Index;                            { defenses 100=high }
END;  { NPECharacterRecord }

NPEIntStructRecord = RECORD
   SpecializedWorlds: Index;                    { % of worlds specialized }
   JumpshipWorlds: Index;                       { % of jumpship bases }
   StarshipWorlds: Index;                       { % of starship bases }
   UniversityWorlds: Byte;                      { no. of university worlds }
   AmbrosiaUse: Index;                          { % of max possible amb use }
END;  { NPEIntStructRecord }

NPEDataRecord = RECORD
   NPEType: NPEmpireTypes;                      { Type of empire }
   NPEPhase: NPEPhaseTypes;                     { Current phase }
   FreeWorlds: PlanetSet;                       { Set of worlds NOT in any 
                                                  region. }
   FirstRegion: RegionPtr;                      { Head of region linked list }
   WorldIndex: NPEWorldIndexArray;              { Array of NPEWorldPtr }
   IntStruct: NPEIntStructRecord;               { Internal structure of empire }
   Character: NPECharacterRecord;               { Empire characteristics }
END;  { NPEDataRecord }

NPEDataArray = ARRAY [Empire1..Empire8] OF NPEDataRecord;

(* -------------------------------------------------------------------------- *)

(* NPE Procedures ----------------------------------------------------------- *)

PROCEDURE NewNPERegion{Emp: Empire; RCapID: IDNumber; 
                       VAR NewRegPtr: RegionPtr};
{ NewNPERegion:
   Given a world and an empire, this procedure will create a new region
   with the given world as the regional capital.  All worlds within 5 sectors
   of the capital will be included in the region unless they already belong
   to another region. }

   VAR
      NextReg: RegionPtr;
      LastWorld: NPEWorldPtr;
      CapID,ObjID: IDNumber;
      RCapXY,CapXY,ObjXY: XYCoord;
      i: Byte;

   BEGIN
   { Get new region }
   WITH Universe^.NPEData[Emp] DO
      BEGIN
      IF FirstRegion=Nil THEN
         BEGIN
         New(FirstRegion);
         NewRegPtr:=FirstRegion;
         END
      ELSE
         BEGIN
         NextReg:=FirstRegion;
         WHILE NextReg^.Next<>Nil DO
            NextReg:=NextReg^.Next;

         New(NextReg^.Next);
         NewRegPtr:=NextReg^.Next;
         END;
      END;  { with scope }
   FillChar(NewRegPtr^,SizeOf(RegionRecord),0);

   { ASSERT: NewRegPtr points to a new region at the end of the linked list. }
   WITH NewRegPtr^ DO
      BEGIN
      { Add the regional capital as the first world of the region. }
      New(FirstWorld);   FillChar(FirstWorld^,SizeOf(NPEWorldRecord),0);
      FirstWorld^.ID:=RCapID;
      LastWorld:=FirstWorld;
      { ASSERT: LastWorld is a pointer to the last world in this region. }

      { Calculate distance of regional capital from imperial capital. }
      GetCapital(Emp,CapID);
      GetCoord(CapID,CapXY);
      GetCoord(RCapID,RCapXY);
      Dist:=Distance(CapXY,RCapXY);

      WorldsInRegion:=WorldsInRegion+[RCapID.Index];
      END;  { with scope }
   { Remove regional capital from list of free worlds. }
   WITH Universe^.NPEData[Emp] DO
      FreeWorlds:=FreeWorlds-[RCapID.Index];

   { Add all worlds within 5 sectors to the region. }
   ObjID.ObjTyp:=Pln;
   FOR i:=1 TO NoOfPlanets DO
      IF i IN SetOfActivePlanets THEN
         BEGIN
         ObjID.Index:=i;
         GetCoord(ObjID,ObjXY);
         IF Distance(ObjXY,RCapXY)<=5 THEN
            { ASSERT: ObjID is within 5 sectors of the regional capital. }
            BEGIN
            { Add world to WorldsInRegion }
            NewRegPtr^.WorldsInRegion:=NewRegPtr^.WorldsInRegion+[i];

            { Remove world from set of free worlds }
            WITH Universe^.NPEData[Emp] DO
               FreeWorlds:=FreeWorlds-[i];

            New(LastWorld^.Next);
            FillChar(LastWorld^.Next^,SizeOf(NPEWorldRecord),0);
            LastWorld:=LastWorld^.Next;

            LastWorld^.ID:=ObjID;
            END;
         END;
   END;  { NewNPERegion }

FUNCTION NPEmpireType{Emp: Empire): NPEmpireTypes};
   BEGIN
   NPEmpireType:=Universe^.NPEData[Emp].NPEType;
   END;  { NPEmpireType }

FUNCTION NPEPhase{Emp: Empire): NPEPhaseTypes};
   BEGIN
   NPEPhase:=Universe^.NPEData[Emp].NPEPhase;
   END;  { NPEPhase }

PROCEDURE SetNPEPhase{Emp: Empire; NewPhase: NPEPhaseTypes};
   BEGIN
   Universe^.NPEData[Emp].NPEPhase:=NewPhase;
   END;  { SetNPEPhase }

PROCEDURE GetNPEWorldImportExport{World: NPEWorldPtr;
                     VAR Imports,Exports: ResourceSet};
   BEGIN
   Imports:=World^.Imports;
   Exports:=World^.Exports;
   END;  { GetNPEWorldImportExport }

PROCEDURE SetNPEWorldImportExport{World: NPEWorldPtr;
                     Imports,Exports: ResourceSet};
   BEGIN
   World^.Imports:=Imports;
   World^.Exports:=Exports;
   END;  { SetNPEWorldImportExport }

FUNCTION NPEWorldLastCargoReq{World: NPEWorldPtr): Byte};
   BEGIN
   NPEWorldLastCargoReq:=World^.LastCargoReq;
   END;  { NPEWorldLastCargoReq }

PROCEDURE SetNPEWorldLastCargoReq{World: NPEWorldPtr; LCR: Byte};
   BEGIN
   World^.LastCargoReq:=LCR;
   END;  { SetNPEWorldLastCargoReq }

PROCEDURE GetNPECharacter{Emp: Empire; VAR Ch: NPECharacterRecord};
   BEGIN
   Ch:=Universe^.NPEData[Emp].Character;
   END;  { GetNPECharacter }

PROCEDURE SetNPECharacter{Emp: Empire; VAR Ch: NPECharacterRecord};
   BEGIN
   Universe^.NPEData[Emp].Character:=Ch;
   END;  { SetNPECharacter }

PROCEDURE GetNPEIntStruct{Emp: Empire; VAR IntStruct: NPEIntStructRecord};
   BEGIN
   IntStruct:=Universe^.NPEData[Emp].IntStruct;
   END;  { GetNPEIntStruct }

PROCEDURE SetNPEIntStruct{Emp: Empire; VAR IntStruct: NPEIntStructRecord};
   BEGIN
   Universe^.NPEData[Emp].IntStruct:=IntStruct;
   END;  { GetNPEIntStruct }

FUNCTION NextPlayer(Player: Empire): Empire;
   BEGIN
   {$IFNDEF DebugNPE}
   REPEAT
      IF Player=Empire8 THEN
         Player:=Empire1
      ELSE
         Inc(Player);
   UNTIL EmpireActive(Player) AND EmpirePlayer(Player);
   NextPlayer:=Player;
   {$ELSE}
   REPEAT
      IF Player=Empire8 THEN
         Player:=Empire1
      ELSE
         Inc(Player);
   UNTIL EmpireActive(Player);
   NextPlayer:=Player;
   {$ENDIF}
   END;  { NextPlayer }

FUNCTION NextNonPlayer(Player: Empire): Empire;
   BEGIN
   {$IFNDEF DebugNPE}
   REPEAT
      IF Player=Empire8 THEN
         Player:=Empire1
      ELSE
         Inc(Player);
   UNTIL EmpireActive(Player) AND (NOT EmpirePlayer(Player));
   NextNonPlayer:=Player;
   {$ELSE}
   NextNonPlayer:=Indep;
   {$ENDIF}
   END;  { NextNonPlayer }

PROCEDURE DrawTitleAndHelp(VAR EmpN: String32; Year: Integer);
   var
      Title: LineStr;
      temp1: String16;

   begin
   { draw title line }
   Title:=EmpN;
   AdjustString(Title,24);
   Str(Year:4,temp1);
   Title:=Title+'                   Year: '+temp1+'          Time: ';
   WriteTitleLine(Title);

   { draw help line }
   end;  { DrawTitleAndHelp }

PROCEDURE DeleteFleetName(FltID: IDNumber);
   VAR
      Loc: Location;
      NameStr: String16;

   BEGIN
   Loc.ID:=FltID;  Loc.XY:=Limbo;
   GetName(GetStatus(FltID),Loc,ShortFormat,NameStr);
   DeleteName(GetStatus(FltID),NameStr);
   END;  { DeleteFleetName }
