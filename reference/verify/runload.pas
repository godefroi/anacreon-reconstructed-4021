(* runload.pas ---------------------------------------------------------------
   Phase 7g driver: calls the real, unmodified LOADSAVE.PAS LoadGame directly against
   a `.SAV` file on disk, then emits a structural checksum of what real Pascal actually
   loaded. A separate driver from runworld.pas, not a new `case <domain>` there --
   LOADSAVE.PAS/MESS.PAS/NEWS.PAS/NPETYPES.PAS/NPE.PAS/ORDERS.PAS aren't in runworld's
   own USES clause, and adding them would run their unit-initialization sections before
   every one of runworld's 20 existing domains (several depend on exact RandSeed/other
   global state at startup) -- isolating this driver avoids re-verifying all of them.

   CLI: `runload <path>` (bare filename -- Filename is Pascal's FilenameStr, STRING[64],
   so an absolute path silently truncates; run from inside patched/ with a short name,
   same trap ScenarioCases.cs already documents for `.SCN` paths).

   Output, one line: `error=<LoadGame's own Word result>` alone on failure, or
   `error=0;year=<v>;player=<EmpireName>;planetcount=<v>;sumplanetpop..sumplanetdefns=<v>
   (aggregate sums across every active planet -- order/ownership-independent);
   starbasecount=<v>;sumstarbasepop=<v>;sumstarbasseeff=<v>;stargatecount=<v>;
   constructioncount=<v>;fleetcount=<v>;sumfleetx=<v>;sumfleety=<v>;sumdestx=<v>;
   sumdesty=<v> (catches SavGameWriter.WriteFleets' own null-Destination convention actually
   round-tripping, not just its per-empire fleet count); minedcellcount=<v> (catches
   SavGameWriter.WriteSector's own mine-owner-nibble sentinel actually round-tripping);
   empirecount=<v>;
   empire<N>name=<v>;empire<N>planets=<v>;empire<N>starbases=<v>;empire<N>fleets=<v>;
   empire<N>constr=<v>;empire<N>tech=<v>;empire<N>revfactor=<v>;empire<N>founding=<v>;
   empire<N>central=<0|1>;empire<N>empress=<0|1>;empire<N>news=<v> (for N=1..empirecount,
   SORTED BY EmpireName -- not by on-disk empire-slot ordinal, since Phase 7g's own
   SavGameWriter compacts slots (Game.Empires holds only in-use empires, no slot number
   to preserve), so a real save's ordinal gaps (a defeated-and-removed empire, see
   SavGameLoader's own orphan-empire doc comment) don't survive a round trip even though
   every empire's own identity and data do. Sorting by name gives a stable join key
   between "run against the original file" and "run against SavGameWriter's rewrite" --
   the whole point of this driver: the C# test harness runs it twice (original, then
   round-tripped) and diffs the two checksum lines directly, no independently-computed
   C# side of the checksum needed, since both sides are the *same* real Pascal code.
   Per-empire Planets/Starbases/Fleets/ConstructionSites counts come from
   Card(SetOfPlanetsOf[Emp]) etc -- the real Owner-ownership sets LoadPlanets/
   LoadStarbases/LoadFleets/LoadConstr populate from each entity's own on-disk Emp field
   -- so a wrong owner-ordinal mapping in the C# writer shows up here directly, not just
   in Empire Data's own fields. News via NewsData[Emp].FirstItem walked to a count
   (order/content-independent, just proves the section round-trips at all -- see
   SavGameWriter's own doc comment for why full News fidelity is in scope while Messages
   is not). Fuel/order-queue/message content are deliberately absent: Fleet.Fuel is a
   double on the C# side (fractional for a played/scenario game, integral only for a
   fresh file-load-file-write round trip) and the order queue/message text have no
   in-memory representation on the C# side at all (docs/OPEN_GAPS.md's tracked
   gaps) -- neither is meaningful to checksum here. ---------------------------------- *)

PROGRAM RunLoad;

USES Types, DataCnst, DataStrc, Galaxy, Int, Misc, PrimIntr, Environ, News, Mess, NPETypes, NPE, Orders, Fleet, Intrface, Strg, LoadSave;

TYPE
   EmpireSummary = RECORD
      Name: String32;
      Planets,Starbases,Fleets,Constr: Word;
      Tech: Byte;
      { Named distinctly from EmpireDataRecord's own RevFactor/Founding fields -- both are
        assigned from inside a nested WITH Universe^.EmpireData[Emp] DO ... WITH Summaries[..]
        DO block below, and Pascal's WITH resolves a matching identifier to the *innermost*
        scope for both sides of an assignment, so a same-named field would just assign itself
        to itself instead of copying across. }
      RevFac,FoundYear: Integer;
      Central,Empress: Boolean;
      NewsCount: Word;
   END;

VAR
   Summaries: ARRAY [1..8] OF EmpireSummary;
   NoOfSummaries: Word;

FUNCTION CountNews(Emp: Empire): Word;
   VAR
      Item: NewsRecordPtr;
      Count: Word;
   BEGIN
   Count:=0;
   GetNewsList(Emp,Item);
   WHILE Item<>Nil DO
      BEGIN
      Inc(Count);
      Item:=Item^.Next;
      END;
   CountNews:=Count;
   END;  { CountNews }

{ No built-in Set cardinality function under -Mtp -- plain membership-counting loops instead. }
FUNCTION CountPlanetsOf(Emp: Empire): Word;
   VAR i,n: Word;
   BEGIN
   n:=0;
   FOR i:=1 TO MaxNoOfPlanets DO
      IF i IN SetOfPlanetsOf[Emp] THEN Inc(n);
   CountPlanetsOf:=n;
   END;

FUNCTION CountStarbasesOf(Emp: Empire): Word;
   VAR i,n: Word;
   BEGIN
   n:=0;
   FOR i:=1 TO MaxNoOfStarbases DO
      IF i IN SetOfStarbasesOf[Emp] THEN Inc(n);
   CountStarbasesOf:=n;
   END;

FUNCTION CountFleetsOf(Emp: Empire): Word;
   VAR i,n: Word;
   BEGIN
   n:=0;
   FOR i:=1 TO MaxNoOfFleets DO
      IF i IN SetOfFleetsOf[Emp] THEN Inc(n);
   CountFleetsOf:=n;
   END;

FUNCTION CountConstrOf(Emp: Empire): Word;
   VAR i,n: Word;
   BEGIN
   n:=0;
   FOR i:=1 TO MaxNoOfConstrSites DO
      IF i IN SetOfConstructionSitesOf[Emp] THEN Inc(n);
   CountConstrOf:=n;
   END;

PROCEDURE CollectEmpireSummaries;
   VAR
      Emp: Empire;
   BEGIN
   NoOfSummaries:=0;
   FOR Emp:=Empire1 TO Empire8 DO
      IF Universe^.EmpireData[Emp].InUse THEN
         WITH Universe^.EmpireData[Emp] DO
            BEGIN
            Inc(NoOfSummaries);
            WITH Summaries[NoOfSummaries] DO
               BEGIN
               Name:=EmpireName;
               Planets:=CountPlanetsOf(Emp);
               Starbases:=CountStarbasesOf(Emp);
               Fleets:=CountFleetsOf(Emp);
               Constr:=CountConstrOf(Emp);
               Tech:=Ord(TechnologyLevel);
               RevFac:=RevFactor;
               FoundYear:=Founding;
               Central:=CentralEMD IN Modifiers;
               Empress:=IsAnEmpress;
               NewsCount:=CountNews(Emp);
               END;
            END;
   END;  { CollectEmpireSummaries }

{ Simple insertion sort by Name -- at most 8 elements, no need for anything fancier. }
PROCEDURE SortEmpireSummariesByName;
   VAR
      i,j: Word;
      Temp: EmpireSummary;
   BEGIN
   FOR i:=2 TO NoOfSummaries DO
      BEGIN
      Temp:=Summaries[i];
      j:=i-1;
      WHILE (j>=1) AND (Summaries[j].Name>Temp.Name) DO
         BEGIN
         Summaries[j+1]:=Summaries[j];
         Dec(j);
         END;
      Summaries[j+1]:=Temp;
      END;
   END;  { SortEmpireSummariesByName }

VAR
   Error: Word;
   i,x,y: Integer;
   PlanetCount,SumPlanetPop,SumPlanetEff,SumPlanetTri,SumPlanetClass,SumPlanetTech: LongInt;
   SumPlanetShips,SumPlanetCargo,SumPlanetDefns: LongInt;
   StarbaseCount,SumStarbasePop,SumStarbaseEff: LongInt;
   StargateCount,ConstrCount: LongInt;
   FleetCount,SumFleetX,SumFleetY,SumDestX,SumDestY: LongInt;
   MinedCellCount: LongInt;
   Cell: XYCoord;
   PlayerName: String32;
   ShpI: ShipTypes;
   CarI: CargoTypes;
   DefI: DefnsTypes;

BEGIN
   IF ParamCount=0 THEN
      Halt(0);

   Error:=LoadGame(ParamStr(1));

   IF Error<>0 THEN
      BEGIN
      WriteLn('error=',Error);
      Halt(0);
      END;

   PlanetCount:=0;  SumPlanetPop:=0;  SumPlanetEff:=0;  SumPlanetTri:=0;
   SumPlanetClass:=0;  SumPlanetTech:=0;  SumPlanetShips:=0;  SumPlanetCargo:=0;  SumPlanetDefns:=0;
   FOR i:=1 TO MaxNoOfPlanets DO
      IF i IN SetOfActivePlanets THEN
         WITH Universe^.Planet[i] DO
            BEGIN
            Inc(PlanetCount);
            Inc(SumPlanetPop,Pop);
            Inc(SumPlanetEff,Eff);
            Inc(SumPlanetTri,TriReserve);
            Inc(SumPlanetClass,Ord(Cls));
            Inc(SumPlanetTech,Ord(Tech));
            FOR ShpI:=fgt TO trn DO
               Inc(SumPlanetShips,Ships[ShpI]);
            FOR CarI:=men TO tri DO
               Inc(SumPlanetCargo,Cargo[CarI]);
            FOR DefI:=LAM TO ion DO
               Inc(SumPlanetDefns,Defns[DefI]);
            END;

   StarbaseCount:=0;  SumStarbasePop:=0;  SumStarbaseEff:=0;
   FOR i:=1 TO MaxNoOfStarbases DO
      IF i IN SetOfActiveStarbases THEN
         WITH Universe^.Starbase[i] DO
            BEGIN
            Inc(StarbaseCount);
            Inc(SumStarbasePop,Pop);
            Inc(SumStarbaseEff,Eff);
            END;

   StargateCount:=0;
   FOR i:=1 TO MaxNoOfStargates DO
      IF i IN SetOfActiveGates THEN
         Inc(StargateCount);

   ConstrCount:=0;
   FOR i:=1 TO MaxNoOfConstrSites DO
      IF i IN SetOfActiveConstructionSites THEN
         Inc(ConstrCount);

   { FleetCount/SumFleetX/Y/SumDestX/Y -- catches SavGameWriter.WriteFleets' own null-Destination
     convention (Dest:=XY, never literal (0,0)) actually round-tripping: a fleet mistakenly written
     with Dest=(0,0) trips LoadFleets' own "any zero axis resets both XY and Dest to (1,1)" quirk,
     which would move the fleet and change this sum even though FleetCount alone stays unchanged. }
   FleetCount:=0;  SumFleetX:=0;  SumFleetY:=0;  SumDestX:=0;  SumDestY:=0;
   FOR i:=1 TO MaxNoOfFleets DO
      IF i IN SetOfActiveFleets THEN
         WITH Universe^.Fleet[i]^ DO
            BEGIN
            Inc(FleetCount);
            Inc(SumFleetX,XY.x);  Inc(SumFleetY,XY.y);
            Inc(SumDestX,Dest.x);  Inc(SumDestY,Dest.y);
            END;

   { MinedCellCount -- catches SavGameWriter.WriteSector's own mine-owner nibble convention
     (NoSRMField=Ord(Indep)*16, not 0) actually round-tripping: a wrong sentinel or a swapped
     nibble changes which cells EnemyMine reports as mined. }
   MinedCellCount:=0;
   FOR x:=1 TO SizeOfGalaxy DO
      FOR y:=1 TO SizeOfGalaxy DO
         BEGIN
         Cell.x:=x;  Cell.y:=y;
         IF EnemyMine(Cell)<>Indep THEN
            Inc(MinedCellCount);
         END;

   PlayerName:=Universe^.EmpireData[Player].EmpireName;

   CollectEmpireSummaries;
   SortEmpireSummariesByName;

   Write('error=0',
         ';year=',Year,
         ';player=',PlayerName,
         ';planetcount=',PlanetCount,
         ';sumplanetpop=',SumPlanetPop,';sumplaneteff=',SumPlanetEff,';sumplanettri=',SumPlanetTri,
         ';sumplanetclass=',SumPlanetClass,';sumplanettech=',SumPlanetTech,
         ';sumplanetships=',SumPlanetShips,';sumplanetcargo=',SumPlanetCargo,';sumplanetdefns=',SumPlanetDefns,
         ';starbasecount=',StarbaseCount,';sumstarbasepop=',SumStarbasePop,';sumstarbaseeff=',SumStarbaseEff,
         ';stargatecount=',StargateCount,
         ';constructioncount=',ConstrCount,
         ';fleetcount=',FleetCount,';sumfleetx=',SumFleetX,';sumfleety=',SumFleetY,
         ';sumdestx=',SumDestX,';sumdesty=',SumDestY,
         ';minedcellcount=',MinedCellCount,
         ';empirecount=',NoOfSummaries);

   FOR i:=1 TO NoOfSummaries DO
      WITH Summaries[i] DO
         Write(';empire',i,'name=',Name,
               ';empire',i,'planets=',Planets,
               ';empire',i,'starbases=',Starbases,
               ';empire',i,'fleets=',Fleets,
               ';empire',i,'constr=',Constr,
               ';empire',i,'tech=',Tech,
               ';empire',i,'revfactor=',RevFac,
               ';empire',i,'founding=',FoundYear,
               ';empire',i,'central=',Ord(Central),
               ';empire',i,'empress=',Ord(Empress),
               ';empire',i,'news=',NewsCount);

   WriteLn;
END.
