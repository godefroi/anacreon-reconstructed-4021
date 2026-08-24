(* runworld.pas -----------------------------------------------------------
   Driver: assembles a minimal but real Universe^ (via the actual
   DataStrc/Galaxy globals, not a simplified stand-in), then calls the real,
   unmodified (only relocated/lifted, never behavior-changed) UpdateWorld
   from the patched UPDATE.PAS unit -- the full real per-planet tick
   (production, efficiency, tech level, population, food/ambrosia, military,
   defenses, revolution), not an isolated transcription of one procedure.

   Machine-parseable mode: one command line runs a batch of cases for a
   single domain --
     ./runworld case <domain> <case1> <case2> ...
   Each <caseN> is a comma-separated tuple, shape depends on <domain>:
     techlevel  TechOrd,IsIndependent,CapitalTechOrd,RngFixedValue -> "techlevel=<ordinal>"
     military   PlanetPop,TechOrd,Legions,TypOrd,RngFixedValue     -> "legions=<value>"
     starbase   StarbaseChemicals,NeighborChemicals,RngFixedValue  -> "starbaseChe=<v>;neighborChe=<v>"
     ambrosia   Addicted,Ambrosia,RngFixedValue                    -> "population=<v>;efficiency=<v>;techlevel=<v>;ambrosia=<v>;addicted=<TRUE|FALSE>"
     revolution PlanetPop,ClassOrd,TechOrd,Efficiency,RevIndex,Legions,RngFixedValue
                -> "rebelled=<TRUE|FALSE>;legions=<v>;ninja=<v>;population=<v>;efficiency=<v>;revindex=<v>;total_rev_delta=<v>"
     production ClassOrd,TypeOrd,Population,Efficiency,TechOrd,AmbAddict,
                IndusBio,IndusChe,IndusMin,IndusSYG,IndusSYJ,IndusSYS,IndusSYT,IndusSup,IndusTri,
                CargoMen,CargoNnj,CargoAmb,CargoChe,CargoMet,CargoSup,CargoTri,TrillumReserve
                -> "bio,che,min,syg,syj,sys,syt,sup,tri,fgt,hkr,jmp,jtn,pen,ssp,trn,cargomen,
                    cargonnj,cargoamb,cargoche,cargomet,cargosup,cargotri,trillumreserve,
                    population,efficiency,techlevel,revindex (all <key>=<value>)"
     empire     TechOrd,TechnologyBitmask,RngFixedValue,
                Lab1Present,Lab1TypeOrd,Lab1ClassOrd,Lab1TechOrd,Lab1Eff,
                Lab2Present,Lab2TypeOrd,Lab2ClassOrd,Lab2TechOrd,Lab2Eff,
                Lab3Present,Lab3TypeOrd,Lab3TechOrd,Lab3Eff (Lab3 is a starbase, no class)
                -> "techlevel=<ordinal>;technology=<26-bit mask, bit i = TechnologyTypes(i+1)>"
     empirecreate TechOrd,ExtraTechsMask (same 26-bit encoding as empire) -- calls the real
                CreateEmpire with NEWGAME.PAS's own tech-set formula, no RNG involved
                -> "techlevel=<ordinal>;technology=<26-bit mask>"
     construction ConstrTypesOrd (pre-offset: SRM=19..dis=26, TechnologyTypes' own
                ordinals, not a 0-based ConstrTypes-relative one), YearsToCompletion,
                TechOrd (Empire1's TechnologyLevel), RngFixedValue,
                Fleet1Present,Fleet1Che,Fleet1Met,Fleet1Tri,
                Fleet2Present,Fleet2Che,Fleet2Met,Fleet2Tri
                -> "timetocompletion=<v>;active=<0|1>;
                    fleet1che=<v>;fleet1met=<v>;fleet1tri=<v>;
                    fleet2che=<v>;fleet2met=<v>;fleet2tri=<v>;
                    mineowner=<Empire ordinal, Indep if no mine>;
                    starbasekind=<StarbaseTypes ordinal>;starbasepop=<v>;starbaseeff=<v>;
                    starbasetype=<WorldTypes ordinal>;starbasetech=<TechLevel ordinal>;
                    starbasebio,starbaseche,starbasemin,starbasesyg,starbasesyj,starbasesys,
                    starbasesyt,starbasesup,starbasetri=<v> (all 9 industries);
                    stargatekind=<StargateTypes ordinal> (all <key>=<value>)"
     trillumreserves ClassOrd,RegionReserves,RngFixedValue -> "reserves=<v>"
     randomplanet ClassOrd,TechOrd,RngFixedValue -> "population=<v>;efficiency=<v>;
                fgt=<v>;hkr=<v>;jmp=<v>;jtn=<v>;pen=<v>;ssp=<v>;trn=<v>;
                cargomen=<v>;cargoche=<v>;cargomet=<v>;cargosup=<v>;cargotri=<v>;
                defLAM=<v>;defDef=<v>;defGDM=<v>;defIon=<v> (all <key>=<value>)"
     nebula     SizeOfGalaxy,Mode(1=band,2=patches),PatchCount(patches mode only),RngFixedValue
                -> "grid=<SizeOfGalaxy*SizeOfGalaxy chars, row-major y=1..Size then x=1..Size,
                    '1'=Nebula '0'=None>" -- GetRandomXY/CreateRandomWorlds have no domain here; see
                the UPDATE.PAS patch's own relocation note for why
     rng        Seed,Range,Count -> "values=<Count comma-joined Random(Range) draws after
                RandSeed:=Seed>" -- not a UpdateWorld/GalaxySetup domain; a standing regression fixture
                for the C# test project's PascalRandom (see RunRngCase's own comment)
     scenario   Path,Seed,NumPlayers (Path is a real .SCN file; NumPlayers players get the fixed
                "PlayerN"/"pwN"/not-empress convention RunScenarioCase and the C# side's own
                ScenarioLoaderGoldenTests both hard-code, not a CLI field, since a name string can't
                round-trip through this domain's all-integer sibling cases) -> an aggregate checksum
                over the whole loaded Universe^ (RunScenarioCase's own doc comment has the full field
                list) -- not a per-entity dump, deliberately: real dos_131 files have up to ~160
                worlds, and a mismatch anywhere (a wrong coordinate, a dropped jitter, a missed empire)
                perturbs at least one of these sums, which is what a golden-file regression actually
                needs to catch. The C# side (ScenarioLoaderGoldenTests.MatchesGoldenFile) only
                exact-matches a subset of these fields, not all of them -- every field touched by a
                random draw anywhere in the file turned out to be fragile to RNG-stream-position drift
                between two independently-written implementations, even fields that look explicit/
                deterministic on their face (see that test's own doc comment, and the root README's
                "Known limitation" section, for why). This driver still emits every field: useful for
                manual diagnosis even where the C# test doesn't assert on it.
   One output line per case, in order, to stdout -- consumed by PatchHarness
   in the C# test project via GoldenFile.Regenerate's runHarness override.
   With no arguments, runs a single hardcoded techlevel case as a
   human-readable sanity check (manual use).

   Not itself a patch target -- this is new code, checked in directly and
   copied into patched/ by build.ps1 (or PatchHarness, from the C# test
   project) alongside the patched source.
--------------------------------------------------------------------------- *)

PROGRAM RunWorld;

{ Relaxes fpc's default strict var-string-checking so AllUpCase(VAR Strg: MaxStr) (STRG.PAS) can be
  called with LineStr/AnsiString variables in RunScenarioCase, below -- see UPDATE.PAS's own matching
  directive (added for the same relocated-code call pattern) for why this is a pure compile-time
  relaxation, not a behavior change. }
{$V-}

USES Types, DataCnst, DataStrc, Galaxy, Int, Misc, PrimIntr, Environ, News, Update, DFA, Strg;

function ParseLongInt(const s: String): LongInt;
   var
      code: Integer;
      value: LongInt;
   begin
   Val(s, value, code);
   if code<>0 then
      begin
      WriteLn(StdErr, 'runworld: bad integer "',s,'" (position ',code,')');
      Halt(1);
      end;
   ParseLongInt:=value;
   end;

{ Fills a caller-sized open array from a comma-separated tuple -- one shared
  parser for every domain's own fixed-width case shape. }
procedure ParseFields(const arg: String; var parts: array of LongInt);
   var
      partIdx,i,startPos: Integer;
      tok: String;
   begin
   partIdx:=0;
   startPos:=1;
   for i:=1 to Length(arg)+1 do
      if (i>Length(arg)) or (arg[i]=',') then
         begin
         tok:=Copy(arg,startPos,i-startPos);
         if partIdx>High(parts) then
            begin
            WriteLn(StdErr,'runworld: too many fields in "',arg,'"');
            Halt(1);
            end;
         parts[partIdx]:=ParseLongInt(tok);
         Inc(partIdx);
         startPos:=i+1;
         end;
   if partIdx<>High(parts)+1 then
      begin
      WriteLn(StdErr,'runworld: expected ',High(parts)+1,' comma-separated fields, got ',partIdx,' in "',arg,'"');
      Halt(1);
      end;
   end;

procedure RunTechLevelCase(const arg: String);
   var
      parts: array[0..3] of LongInt;
      ID, CapID: IDNumber;
   begin
   ParseFields(arg,parts);

   { PATCH note: never write through GlobalSets (DATASTRC.PAS:235's
     "ABSOLUTE SetOfActiveFleets" overlay) -- it assumes TP's declaration-order
     memory layout for the standalone vars in TYPES.PAS:180-188, which fpc does
     not guarantee; writing through it here corrupted the Universe pointer
     itself (runtime error 216). Write directly to the real standalone vars. }
   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   NoOfPlanets:=2;

   { World under test: Planet[1]. }
   Universe^.Planet[1].Cls:=ClsM;
   Universe^.Planet[1].Typ:=AgrTyp;
   Universe^.Planet[1].Tech:=TechLevel(parts[0]);
   Universe^.Planet[1].Eff:=100;
   Universe^.Planet[1].Pop:=10;
   Universe^.Planet[1].Cargo[sup]:=9999;

   if parts[1]<>0 then
      begin
      { Independent: no empire/capital state to assemble -- Planet[2] stays
        zeroed and inactive, never referenced by UpdateWorld's Emp=Indep path. }
      Universe^.Planet[1].Emp:=Indep;
      SetOfActivePlanets:=[1];
      end
   else
      begin
      Universe^.Planet[1].Emp:=Empire1;
      SetOfActivePlanets:=[1,2];
      SetOfPlanetsOf[Empire1]:=[1,2];

      { Capital: Planet[2], same empire. }
      Universe^.Planet[2].Emp:=Empire1;
      Universe^.Planet[2].Cls:=ClsM;
      Universe^.Planet[2].Typ:=CapTyp;
      Universe^.Planet[2].Tech:=TechLevel(parts[2]);
      Universe^.Planet[2].Eff:=100;
      Universe^.Planet[2].Pop:=10;

      Universe^.EmpireData[Empire1].InUse:=True;
      Universe^.EmpireData[Empire1].IsAPlayer:=False;
      CapID.ObjTyp:=Pln;  CapID.Index:=2;
      Universe^.EmpireData[Empire1].Capital:=CapID;
      end;

   ForcedRandomValue:=parts[3];

   ID.ObjTyp:=Pln;  ID.Index:=1;
   UpdateWorld(ID);

   WriteLn('techlevel=',Ord(Universe^.Planet[1].Tech));

   Dispose(Universe);
   end;

procedure RunMilitaryCase(const arg: String);
   { Owned by Empire1 with its capital set to itself, rather than left
     unassembled -- CapitalTech=Tech always makes UpdateTechLevel's own
     branch conditions false regardless of RngFixedValue, so tech level can
     never drift mid-tick and perturb UpdatePopulation's basePop lookup
     (which runs right after). That's the same "capital tech level never
     read" outcome the C# side gets from its test empire's Capital being
     null (AnnualTickHandlerMilitaryTests.MatchesGoldenFile) -- reached here
     by a route Pascal's IDNumber (no null planet reference) can represent.
     Class is hardcoded to ClsM and Efficiency to 100, matching every
     MilitaryCase (neither is varied by any case). }
   var
      parts: array[0..4] of LongInt;
      ID, CapID: IDNumber;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   NoOfPlanets:=1;

   Universe^.Planet[1].Cls:=ClsM;
   Universe^.Planet[1].Typ:=WorldTypes(parts[3]);
   Universe^.Planet[1].Tech:=TechLevel(parts[1]);
   Universe^.Planet[1].Eff:=100;
   Universe^.Planet[1].Pop:=parts[0];
   Universe^.Planet[1].Cargo[sup]:=9999;
   Universe^.Planet[1].Cargo[men]:=parts[2];
   Universe^.Planet[1].Emp:=Empire1;

   SetOfActivePlanets:=[1];
   SetOfPlanetsOf[Empire1]:=[1];
   Universe^.EmpireData[Empire1].InUse:=True;
   Universe^.EmpireData[Empire1].IsAPlayer:=False;
   CapID.ObjTyp:=Pln;  CapID.Index:=1;
   Universe^.EmpireData[Empire1].Capital:=CapID;

   ForcedRandomValue:=parts[4];

   ID.ObjTyp:=Pln;  ID.Index:=1;
   UpdateWorld(ID);

   WriteLn('legions=',Universe^.Planet[1].Cargo[men]);

   Dispose(Universe);
   end;

procedure RunStarbaseCase(const arg: String);
   { Complex (STyp=cmp) at (5,5), one eligible neighbor planet (AgrTyp, same
     empire) at (6,6) -- Chebyshev distance 1, matching
     AnnualTickHandlerStarbaseTests' own MakeComplex/MakeRawMaterialPlanet
     fixtures exactly, so this cross-checks SupplyLink/SurplusLink's
     arithmetic against the real Pascal formula instead of just this
     session's own reading of it. Population=0 on both (as in those C#
     tests) keeps UpdatePopulation/UseUpFood/UseUpAmbrosia from perturbing
     Cargo[che] -- see AnnualTickHandlerStarbaseTests' class doc comment for
     why che specifically is safe to check this way (Cargo.Metals starts at
     0, so GetIndustrialDistribution/UpdateIndustry's own growth never
     touches it). Capital points at the neighbor planet itself, purely so
     GetCapital/GetTech has something valid to read -- UpdateTechLevel's
     outcome isn't part of what this case checks.

     Requires InitializeSector (Galaxy unit) plus a direct Sector[x]^[y].Obj
     write for the neighbor -- SupplyLink/SurplusLink resolve neighbors via
     GetObject, unlike techlevel/military's own scenarios, which never call
     it. Never route through Intrface's PutObject for this (same reasoning
     as the GlobalSets note above: stay on the real standalone state this
     driver already owns, not a heavier unit pulled in for one write). }
   var
      parts: array[0..2] of LongInt;
      ID, PlanetID: IDNumber;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   InitializeSector(20);
   NoOfPlanets:=1;

   Universe^.Planet[1].XY.x:=6;  Universe^.Planet[1].XY.y:=6;
   Universe^.Planet[1].Emp:=Empire1;
   Universe^.Planet[1].Cls:=ClsM;
   Universe^.Planet[1].Typ:=AgrTyp;
   Universe^.Planet[1].Pop:=0;
   Universe^.Planet[1].Cargo[che]:=parts[1];

   PlanetID.ObjTyp:=Pln;  PlanetID.Index:=1;
   Sector[6]^[6].Obj:=PlanetID;

   Universe^.Starbase[1].XY.x:=5;  Universe^.Starbase[1].XY.y:=5;
   Universe^.Starbase[1].Emp:=Empire1;
   Universe^.Starbase[1].STyp:=cmp;
   Universe^.Starbase[1].Typ:=BseSTyp;
   Universe^.Starbase[1].Tech:=WrpTchLvl;
   Universe^.Starbase[1].Eff:=100;
   Universe^.Starbase[1].Pop:=0;
   Universe^.Starbase[1].Cargo[che]:=parts[0];

   SetOfActivePlanets:=[1];
   SetOfPlanetsOf[Empire1]:=[1];
   SetOfActiveStarbases:=[1];
   SetOfStarbasesOf[Empire1]:=[1];
   Universe^.EmpireData[Empire1].InUse:=True;
   Universe^.EmpireData[Empire1].IsAPlayer:=False;
   Universe^.EmpireData[Empire1].Capital:=PlanetID;

   ForcedRandomValue:=parts[2];

   ID.ObjTyp:=Base;  ID.Index:=1;
   UpdateWorld(ID);

   WriteLn('starbaseChe=',Universe^.Starbase[1].Cargo[che],
           ';neighborChe=',Universe^.Planet[1].Cargo[che]);

   Dispose(Universe);
   end;

procedure RunAmbrosiaCase(const arg: String);
   { Owned by Empire1 with its capital set to itself (same rationale as
     RunMilitaryCase) so UpdateTechLevel can never drift TechLevel mid-tick.
     PlanetPop=1000, Efficiency=100, Tech=Warp, Class=ClsM, Type=CapTyp are
     hardcoded -- every AmbrosiaCase uses the same values, so only what
     actually varies (Addicted, Ambrosia, RngFixedValue) is parametrized.
     Cargo[sup]=9999 keeps UseUpFood from starving Population before
     UseUpAmbrosia (which runs right after) reads it. }
   var
      parts: array[0..2] of LongInt;
      ID, CapID: IDNumber;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   NoOfPlanets:=1;

   Universe^.Planet[1].Cls:=ClsM;
   Universe^.Planet[1].Typ:=CapTyp;
   Universe^.Planet[1].Tech:=WrpTchLvl;
   Universe^.Planet[1].Eff:=100;
   Universe^.Planet[1].Pop:=1000;
   Universe^.Planet[1].Cargo[sup]:=9999;
   Universe^.Planet[1].Cargo[amb]:=parts[1];
   if parts[0]<>0 then
      Universe^.Planet[1].Special:=[AmbAddict]
   else
      Universe^.Planet[1].Special:=[];
   Universe^.Planet[1].Emp:=Empire1;

   SetOfActivePlanets:=[1];
   SetOfPlanetsOf[Empire1]:=[1];
   Universe^.EmpireData[Empire1].InUse:=True;
   Universe^.EmpireData[Empire1].IsAPlayer:=False;
   CapID.ObjTyp:=Pln;  CapID.Index:=1;
   Universe^.EmpireData[Empire1].Capital:=CapID;

   ForcedRandomValue:=parts[2];

   ID.ObjTyp:=Pln;  ID.Index:=1;
   UpdateWorld(ID);

   WriteLn('population=',Universe^.Planet[1].Pop,
           ';efficiency=',Universe^.Planet[1].Eff,
           ';techlevel=',Ord(Universe^.Planet[1].Tech),
           ';ambrosia=',Universe^.Planet[1].Cargo[amb],
           ';addicted=',(AmbAddict IN Universe^.Planet[1].Special));

   Dispose(Universe);
   end;

procedure RunRevolutionCase(const arg: String);
   { Owned by Empire1 with its capital set to itself (same rationale as
     RunMilitaryCase/RunAmbrosiaCase). Type=AgrTyp and Ninja=0 are hardcoded
     -- every RevolutionCase uses them; Class/Tech/Efficiency/RevIndex/Legions
     vary and are parametrized. Cargo[sup]=9999 keeps UseUpFood from starving
     Population before UpdateRevolution (which runs right after) reads it.

     total_rev_delta reads GetNewTotalRevIndex (a UPDATE.PAS patch, see there)
     -- the accumulator Rebellion writes to, normally committed to (and reset
     for the next year by) UpdateEmpire/UpdateUniverse (a later commit, not
     reachable from UpdateWorld), so calling UpdateWorld alone leaves it
     sitting in that private accumulator with no other way to read it back
     out, AND never zeroed between cases in the same batched CLI invocation.
     Reporting before/after and taking the difference sidesteps needing a
     reset hook entirely, and is exactly what "one year's worth of delta"
     means regardless of what an earlier case in this same process left
     sitting in the accumulator. }
   var
      parts: array[0..6] of LongInt;
      ID, CapID: IDNumber;
      RevDeltaBefore: Integer;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   NoOfPlanets:=1;

   Universe^.Planet[1].Cls:=WorldClass(parts[1]);
   Universe^.Planet[1].Typ:=AgrTyp;
   Universe^.Planet[1].Tech:=TechLevel(parts[2]);
   Universe^.Planet[1].Eff:=parts[3];
   Universe^.Planet[1].Pop:=parts[0];
   Universe^.Planet[1].RevIndex:=parts[4];
   Universe^.Planet[1].Cargo[sup]:=9999;
   Universe^.Planet[1].Cargo[men]:=parts[5];
   Universe^.Planet[1].Cargo[nnj]:=0;
   Universe^.Planet[1].Emp:=Empire1;

   SetOfActivePlanets:=[1];
   SetOfPlanetsOf[Empire1]:=[1];
   Universe^.EmpireData[Empire1].InUse:=True;
   Universe^.EmpireData[Empire1].IsAPlayer:=False;
   CapID.ObjTyp:=Pln;  CapID.Index:=1;
   Universe^.EmpireData[Empire1].Capital:=CapID;

   ForcedRandomValue:=parts[6];

   RevDeltaBefore:=GetNewTotalRevIndex(Empire1);

   ID.ObjTyp:=Pln;  ID.Index:=1;
   UpdateWorld(ID);

   WriteLn('rebelled=',(Universe^.Planet[1].Emp=Indep),
           ';legions=',Universe^.Planet[1].Cargo[men],
           ';ninja=',Universe^.Planet[1].Cargo[nnj],
           ';population=',Universe^.Planet[1].Pop,
           ';efficiency=',Universe^.Planet[1].Eff,
           ';revindex=',Universe^.Planet[1].RevIndex,
           ';total_rev_delta=',GetNewTotalRevIndex(Empire1)-RevDeltaBefore);

   Dispose(Universe);
   end;

procedure RunProductionCase(const arg: String);
   { Owned by Empire1 with its capital set to itself (same rationale as
     RunMilitaryCase/RunAmbrosiaCase/RunRevolutionCase). EmpireData.Technology
     is set to every TechnologyTypes value unconditionally: UpdateWorld
     intersects it with TechDev[Tech] (UPDATE.PAS:1367-1368) to get the
     Technology set production is actually gated on, so a full input set
     reduces that intersection to exactly TechDev[Tech] -- matching both the
     old isolated production.pas harness (which hardcoded Technology:=
     TechDev[Tech] directly, bypassing the per-empire set entirely) and
     CargoTechAvailable's C# model (gates purely on TechLevel, no per-empire
     cargo-research tracking). A partially-populated Technology set (e.g. only
     when some "AllShipsUnlocked" case flag was set) would model an empire
     that hasn't finished individually researching every item unlocked by its
     own tech level -- a real Pascal mechanic (UPDATE.PAS's NewTechLevel/
     GetNewTech), but one no reachable game state exercises for the resource
     types (che/met/sup/tri) production depends on: CreateEmpire always seeds
     a new empire with the full TechDev[Pred(Tech)] set (NEWGAME.PAS:1203,
     1240), so this harness's Technology set should always be "full" too.
     Runs the real UpdateWorld, not just the production sub-pipeline, so
     unlike the old isolated FullPipeline this also computes genuine post-tick
     Population/Efficiency/TechLevel/RevolutionIndex and Cargo.Supplies/
     Ambrosia/Legions values -- unlike production.pas, nothing here needs
     excluding from the golden comparison. }
   var
      parts: array[0..22] of LongInt;
      ID, CapID: IDNumber;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   NoOfPlanets:=1;

   Universe^.Planet[1].Cls:=WorldClass(parts[0]);
   Universe^.Planet[1].Typ:=WorldTypes(parts[1]);
   Universe^.Planet[1].Pop:=parts[2];
   Universe^.Planet[1].Eff:=parts[3];
   Universe^.Planet[1].Tech:=TechLevel(parts[4]);
   if parts[5]<>0 then
      Universe^.Planet[1].Special:=[AmbAddict]
   else
      Universe^.Planet[1].Special:=[];

   Universe^.Planet[1].Indus[BioInd]:=parts[6];
   Universe^.Planet[1].Indus[CheInd]:=parts[7];
   Universe^.Planet[1].Indus[MinInd]:=parts[8];
   Universe^.Planet[1].Indus[SYGInd]:=parts[9];
   Universe^.Planet[1].Indus[SYJInd]:=parts[10];
   Universe^.Planet[1].Indus[SYSInd]:=parts[11];
   Universe^.Planet[1].Indus[SYTInd]:=parts[12];
   Universe^.Planet[1].Indus[SupInd]:=parts[13];
   Universe^.Planet[1].Indus[TriInd]:=parts[14];

   Universe^.Planet[1].Cargo[men]:=parts[15];
   Universe^.Planet[1].Cargo[nnj]:=parts[16];
   Universe^.Planet[1].Cargo[amb]:=parts[17];
   Universe^.Planet[1].Cargo[che]:=parts[18];
   Universe^.Planet[1].Cargo[met]:=parts[19];
   Universe^.Planet[1].Cargo[sup]:=parts[20];
   Universe^.Planet[1].Cargo[tri]:=parts[21];

   Universe^.Planet[1].TriReserve:=parts[22];
   Universe^.Planet[1].Emp:=Empire1;
   Universe^.Planet[1].ImpExp:=DefaultISSP;  { every real planet gets this at settlement (PRIMINTR.PAS:631) --
                                               GetISSP reads it as four 4-bit dials (Che/Min/Sup/Tri), and a
                                               FillChar-zeroed planet would otherwise read ISSP index 0 (0.01)
                                               for all four instead of the real default index 5 (1.00). }

   SetOfActivePlanets:=[1];
   SetOfPlanetsOf[Empire1]:=[1];
   Universe^.EmpireData[Empire1].InUse:=True;
   Universe^.EmpireData[Empire1].IsAPlayer:=False;
   CapID.ObjTyp:=Pln;  CapID.Index:=1;
   Universe^.EmpireData[Empire1].Capital:=CapID;
   Universe^.EmpireData[Empire1].Technology:=[Low(TechnologyTypes)..High(TechnologyTypes)];

   ForcedRandomValue:=0;

   ID.ObjTyp:=Pln;  ID.Index:=1;
   UpdateWorld(ID);

   WriteLn('bio=',Universe^.Planet[1].Indus[BioInd],
           ';che=',Universe^.Planet[1].Indus[CheInd],
           ';min=',Universe^.Planet[1].Indus[MinInd],
           ';syg=',Universe^.Planet[1].Indus[SYGInd],
           ';syj=',Universe^.Planet[1].Indus[SYJInd],
           ';sys=',Universe^.Planet[1].Indus[SYSInd],
           ';syt=',Universe^.Planet[1].Indus[SYTInd],
           ';sup=',Universe^.Planet[1].Indus[SupInd],
           ';tri=',Universe^.Planet[1].Indus[TriInd],
           ';fgt=',Universe^.Planet[1].Ships[fgt],
           ';hkr=',Universe^.Planet[1].Ships[hkr],
           ';jmp=',Universe^.Planet[1].Ships[jmp],
           ';jtn=',Universe^.Planet[1].Ships[jtn],
           ';pen=',Universe^.Planet[1].Ships[pen],
           ';ssp=',Universe^.Planet[1].Ships[ssp],
           ';trn=',Universe^.Planet[1].Ships[trn],
           ';cargomen=',Universe^.Planet[1].Cargo[men],
           ';cargonnj=',Universe^.Planet[1].Cargo[nnj],
           ';cargoamb=',Universe^.Planet[1].Cargo[amb],
           ';cargoche=',Universe^.Planet[1].Cargo[che],
           ';cargomet=',Universe^.Planet[1].Cargo[met],
           ';cargosup=',Universe^.Planet[1].Cargo[sup],
           ';cargotri=',Universe^.Planet[1].Cargo[tri],
           ';trillumreserve=',Universe^.Planet[1].TriReserve,
           ';population=',Universe^.Planet[1].Pop,
           ';efficiency=',Universe^.Planet[1].Eff,
           ';techlevel=',Ord(Universe^.Planet[1].Tech),
           ';revindex=',Universe^.Planet[1].RevIndex);

   Dispose(Universe);
   end;

procedure RunEmpireCase(const arg: String);
   { Calls UpdateEmpire directly, not UpdateWorld -- NewTechLevel is empire-level,
     not per-world, so there's no need to run a full per-planet tick to exercise it.

     Empire.Technology is encoded as a 26-bit mask, bit i = TechnologyTypes(i+1)
     (i.e. LAM..dis in enum-declaration order, skipping NoRes) -- ParseFields
     only handles plain integers, and 26 bits fits a LongInt trivially. Up to
     two planet labs and one starbase lab, covering every GetChanceForNewTech
     branch (Capital; University at exactly EmpTech, with/without Ruins class;
     Ruins-only fallback; the same two branches again for a starbase) without
     needing Pascal's full 20-lab array -- a *Present flag of 0 leaves that
     slot out of SetOfPlanetsOf/SetOfStarbasesOf entirely, matching a real
     empire that simply doesn't have that many owned worlds. }
   var
      parts: array[0..16] of LongInt;
      CapID: IDNumber;
      techSet: TechnologySet;
      i: Integer;
      mask: LongInt;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   NoOfPlanets:=2;

   Universe^.EmpireData[Empire1].InUse:=True;
   Universe^.EmpireData[Empire1].IsAPlayer:=False;
   Universe^.EmpireData[Empire1].TechnologyLevel:=TechLevel(parts[0]);

   techSet:=[];
   for i:=0 to 25 do
      if ((parts[1] shr i) and 1)=1 then
         techSet:=techSet+[TechnologyTypes(i+1)];
   Universe^.EmpireData[Empire1].Technology:=techSet;

   ForcedRandomValue:=parts[2];

   SetOfActivePlanets:=[];
   SetOfPlanetsOf[Empire1]:=[];

   if parts[3]<>0 then
      begin
      Universe^.Planet[1].Emp:=Empire1;
      Universe^.Planet[1].Typ:=WorldTypes(parts[4]);
      Universe^.Planet[1].Cls:=WorldClass(parts[5]);
      Universe^.Planet[1].Tech:=TechLevel(parts[6]);
      Universe^.Planet[1].Eff:=parts[7];
      SetOfActivePlanets:=SetOfActivePlanets+[1];
      SetOfPlanetsOf[Empire1]:=SetOfPlanetsOf[Empire1]+[1];
      if Universe^.Planet[1].Typ=CapTyp then
         begin
         CapID.ObjTyp:=Pln;  CapID.Index:=1;
         Universe^.EmpireData[Empire1].Capital:=CapID;
         end;
      end;

   if parts[8]<>0 then
      begin
      Universe^.Planet[2].Emp:=Empire1;
      Universe^.Planet[2].Typ:=WorldTypes(parts[9]);
      Universe^.Planet[2].Cls:=WorldClass(parts[10]);
      Universe^.Planet[2].Tech:=TechLevel(parts[11]);
      Universe^.Planet[2].Eff:=parts[12];
      SetOfActivePlanets:=SetOfActivePlanets+[2];
      SetOfPlanetsOf[Empire1]:=SetOfPlanetsOf[Empire1]+[2];
      if Universe^.Planet[2].Typ=CapTyp then
         begin
         CapID.ObjTyp:=Pln;  CapID.Index:=2;
         Universe^.EmpireData[Empire1].Capital:=CapID;
         end;
      end;

   if parts[13]<>0 then
      begin
      Universe^.Starbase[1].Emp:=Empire1;
      Universe^.Starbase[1].Typ:=WorldTypes(parts[14]);
      Universe^.Starbase[1].Tech:=TechLevel(parts[15]);
      Universe^.Starbase[1].Eff:=parts[16];
      SetOfActiveStarbases:=[1];
      SetOfStarbasesOf[Empire1]:=[1];
      end
   else
      begin
      SetOfActiveStarbases:=[];
      SetOfStarbasesOf[Empire1]:=[];
      end;

   UpdateEmpire(Empire1);

   mask:=0;
   for i:=0 to 25 do
      if TechnologyTypes(i+1) in Universe^.EmpireData[Empire1].Technology then
         mask:=mask or (1 shl i);

   WriteLn('techlevel=',Ord(Universe^.EmpireData[Empire1].TechnologyLevel),
           ';technology=',mask);

   Dispose(Universe);
   end;

procedure RunEmpireCreateCase(const arg: String);
   { Calls the real, already-exported CreateEmpire (PRIMINTR.PAS:982) with the exact tech-set
     formula NEWGAME.PAS's CreatePlayerEmpire (:1203,1207) / CreateNPEmpire (:1240,1243) compute
     before calling it:
        KnownTechs:=TechDev[Pred(Tech)];
        KnownTechs:=KnownTechs+extras;
        KnownTechs:=KnownTechs*TechDev[Tech];
     reproduced inline here rather than pulling in all of NEWGAME.PAS's much larger USES clause
     (Crt/Dos/DOS2/EIO/WND/Menu/DFA/LoadSave/NPE/NPETypes) for 3 lines of pure set math -- the same
     "relocate the small formula, not the whole unit" precedent as GetIndustrialDistribution. No RNG
     anywhere in this domain -- the formula and CreateEmpire are both fully deterministic.

     ExtraTechsMask uses the same 26-bit encoding as the empire domain (bit i = TechnologyTypes(i+1)).

     Deliberately not exercised here: TechLevel(0)=PreTchLvl, since Pred(PreTchLvl) is an out-of-range
     TechDev index in real Pascal (a range-check error, not a well-defined "empty set") -- no real
     scenario file ever creates a player/NPE empire at that level. See EmpireFactoryTests' own
     hardcoded (non-golden) coverage of that defensive case. }
   var
      parts: array[0..1] of LongInt;
      Tech: TechLevel;
      KnownTechs: TechnologySet;
      i: Integer;
      mask: LongInt;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   NoOfPlanets:=0;

   Tech:=TechLevel(parts[0]);

   KnownTechs:=TechDev[Pred(Tech)];
   for i:=0 to 25 do
      if ((parts[1] shr i) and 1)=1 then
         KnownTechs:=KnownTechs+[TechnologyTypes(i+1)];
   KnownTechs:=KnownTechs*TechDev[Tech];

   CreateEmpire(Empire1,True,False,'Terra','',EmptyQuadrant,Tech,KnownTechs,0,[],4000);

   mask:=0;
   for i:=0 to 25 do
      if TechnologyTypes(i+1) in Universe^.EmpireData[Empire1].Technology then
         mask:=mask or (1 shl i);

   WriteLn('techlevel=',Ord(Universe^.EmpireData[Empire1].TechnologyLevel),
           ';technology=',mask);

   Dispose(Universe);
   end;

procedure RunConstructionCase(const arg: String);
   { Owned by Empire1, construction site + up to two fleets all at (5,5). NextStarbaseSlot/
     NextStargateSlot pick the highest available slot counting down from MaxNoOfStarbases/
     MaxNoOfStargates (INTRFACE.PAS:359-368,399-408); starting with zero active starbases/gates
     means a completion always lands at exactly MaxNoOfStarbases/MaxNoOfStargates, so those two
     fixed slots are read back unconditionally below regardless of what this case actually built. }
   var
      parts: array[0..11] of LongInt;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   InitializeSector(20); { required before any Sector[x]^[y] access -- PutMine/EnemyMine/
                           CreateStarbase/CreateStargate all touch it (same requirement as
                           RunStarbaseCase's own InitializeSector call). }

   Universe^.Constr[1].XY.x:=5;  Universe^.Constr[1].XY.y:=5;
   Universe^.Constr[1].Emp:=Empire1;
   Universe^.Constr[1].CTyp:=ConstrTypes(parts[0]);
   Universe^.Constr[1].TimeToCompletion:=parts[1];
   SetOfActiveConstructionSites:=[1];
   SetOfConstructionSitesOf[Empire1]:=[1];

   Universe^.EmpireData[Empire1].InUse:=True;
   Universe^.EmpireData[Empire1].IsAPlayer:=False;
   Universe^.EmpireData[Empire1].TechnologyLevel:=TechLevel(parts[2]);

   ForcedRandomValue:=parts[3];

   { Both fleet slots always allocated (never nil, so the unconditional Cargo reads below are
     always safe) -- only added to SetOfActiveFleets/SetOfFleetsOf, hence visible to
     GetFleets/UpdateConstruction, when the case actually wants that fleet present. }
   New(Universe^.Fleet[1]);
   FillChar(Universe^.Fleet[1]^,SizeOf(Universe^.Fleet[1]^),0);
   New(Universe^.Fleet[2]);
   FillChar(Universe^.Fleet[2]^,SizeOf(Universe^.Fleet[2]^),0);
   SetOfActiveFleets:=[];
   SetOfFleetsOf[Empire1]:=[];

   if parts[4]<>0 then
      begin
      Universe^.Fleet[1]^.XY.x:=5;  Universe^.Fleet[1]^.XY.y:=5;
      Universe^.Fleet[1]^.Emp:=Empire1;
      Universe^.Fleet[1]^.Cargo[che]:=parts[5];
      Universe^.Fleet[1]^.Cargo[met]:=parts[6];
      Universe^.Fleet[1]^.Cargo[tri]:=parts[7];
      SetOfActiveFleets:=SetOfActiveFleets+[1];
      SetOfFleetsOf[Empire1]:=SetOfFleetsOf[Empire1]+[1];
      end;

   if parts[8]<>0 then
      begin
      Universe^.Fleet[2]^.XY.x:=5;  Universe^.Fleet[2]^.XY.y:=5;
      Universe^.Fleet[2]^.Emp:=Empire1;
      Universe^.Fleet[2]^.Cargo[che]:=parts[9];
      Universe^.Fleet[2]^.Cargo[met]:=parts[10];
      Universe^.Fleet[2]^.Cargo[tri]:=parts[11];
      SetOfActiveFleets:=SetOfActiveFleets+[2];
      SetOfFleetsOf[Empire1]:=SetOfFleetsOf[Empire1]+[2];
      end;

   UpdateConstruction(1);

   WriteLn('timetocompletion=',Universe^.Constr[1].TimeToCompletion,
           ';active=',Ord(1 in SetOfActiveConstructionSites),
           ';fleet1che=',Universe^.Fleet[1]^.Cargo[che],
           ';fleet1met=',Universe^.Fleet[1]^.Cargo[met],
           ';fleet1tri=',Universe^.Fleet[1]^.Cargo[tri],
           ';fleet2che=',Universe^.Fleet[2]^.Cargo[che],
           ';fleet2met=',Universe^.Fleet[2]^.Cargo[met],
           ';fleet2tri=',Universe^.Fleet[2]^.Cargo[tri],
           ';mineowner=',Ord(EnemyMine(Universe^.Constr[1].XY)),
           ';starbasekind=',Ord(Universe^.Starbase[MaxNoOfStarbases].STyp),
           ';starbasepop=',Universe^.Starbase[MaxNoOfStarbases].Pop,
           ';starbaseeff=',Universe^.Starbase[MaxNoOfStarbases].Eff,
           ';starbasetype=',Ord(Universe^.Starbase[MaxNoOfStarbases].Typ),
           ';starbasetech=',Ord(Universe^.Starbase[MaxNoOfStarbases].Tech),
           ';starbasebio=',Universe^.Starbase[MaxNoOfStarbases].Indus[BioInd],
           ';starbaseche=',Universe^.Starbase[MaxNoOfStarbases].Indus[CheInd],
           ';starbasemin=',Universe^.Starbase[MaxNoOfStarbases].Indus[MinInd],
           ';starbasesyg=',Universe^.Starbase[MaxNoOfStarbases].Indus[SYGInd],
           ';starbasesyj=',Universe^.Starbase[MaxNoOfStarbases].Indus[SYJInd],
           ';starbasesys=',Universe^.Starbase[MaxNoOfStarbases].Indus[SYSInd],
           ';starbasesyt=',Universe^.Starbase[MaxNoOfStarbases].Indus[SYTInd],
           ';starbasesup=',Universe^.Starbase[MaxNoOfStarbases].Indus[SupInd],
           ';starbasetri=',Universe^.Starbase[MaxNoOfStarbases].Indus[TriInd],
           ';stargatekind=',Ord(Universe^.Stargate[MaxNoOfStargates].GTyp));

   Dispose(Universe);
   end;

procedure RunTrillumReservesCase(const arg: String);
   { Calls the now-exported RandomTrillumReserves directly (Phase 2 commit 2d) -- no Universe^ state
     needed at all beyond what New/FillChar/Dispose bracket for symmetry with every other domain;
     the function only reads its two value parameters and the module-level TriResByClass table. }
   var
      parts: array[0..2] of LongInt;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);

   ForcedRandomValue:=parts[2];

   WriteLn('reserves=',RandomTrillumReserves(WorldClass(parts[0]),parts[1]));

   Dispose(Universe);
   end;

procedure RunRandomPlanetCase(const arg: String);
   { Calls the now-exported CreateRndPlanet directly (Phase 2 commit 2d) at a fixed (5,5) -- location
     never varies in this domain, CreateRndPlanet's own formula doesn't read it. Requires
     InitializeSector first: CreatePlanet (relocated alongside CreateRndPlanet) writes through
     Sector[x]^[y].Obj, same requirement as RunStarbaseCase/RunConstructionCase. Doesn't set
     TrillumReserve -- matching real Pascal, CreateRndPlanet's own call sites always do that
     separately (see RunTrillumReservesCase / GalaxySetup.CreateRndPlanet's own doc comment). }
   var
      parts: array[0..2] of LongInt;
      ID: IDNumber;
      Coord: XYCoord;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   InitializeSector(10);
   NoOfPlanets:=1;

   ForcedRandomValue:=parts[2];

   ID.ObjTyp:=Pln;  ID.Index:=1;
   Coord.x:=5;  Coord.y:=5;
   CreateRndPlanet(ID,Coord,WorldClass(parts[0]),TechLevel(parts[1]));

   WriteLn('population=',Universe^.Planet[1].Pop,
           ';efficiency=',Universe^.Planet[1].Eff,
           ';fgt=',Universe^.Planet[1].Ships[fgt],
           ';hkr=',Universe^.Planet[1].Ships[hkr],
           ';jmp=',Universe^.Planet[1].Ships[jmp],
           ';jtn=',Universe^.Planet[1].Ships[jtn],
           ';pen=',Universe^.Planet[1].Ships[pen],
           ';ssp=',Universe^.Planet[1].Ships[ssp],
           ';trn=',Universe^.Planet[1].Ships[trn],
           ';cargomen=',Universe^.Planet[1].Cargo[men],
           ';cargoche=',Universe^.Planet[1].Cargo[che],
           ';cargomet=',Universe^.Planet[1].Cargo[met],
           ';cargosup=',Universe^.Planet[1].Cargo[sup],
           ';cargotri=',Universe^.Planet[1].Cargo[tri],
           ';defLAM=',Universe^.Planet[1].Defns[LAM],
           ';defDef=',Universe^.Planet[1].Defns[def],
           ';defGDM=',Universe^.Planet[1].Defns[GDM],
           ';defIon=',Universe^.Planet[1].Defns[ion]);

   Dispose(Universe);
   end;

procedure RunNebulaCase(const arg: String);
   { Calls the now-exported NebulaeBand/NebulaePatches directly (Phase 2 commit 2d). Dumps the whole
     SizeOfGalaxy-by-SizeOfGalaxy grid as a row-major '0'/'1' string (y=1..Size outer, x=1..Size inner,
     Pascal's own 1-based coordinate space -- the C# side shifts by -1 when comparing against its own
     0-based Coordinate, see NebulaCases' own doc comment) rather than a fixed field list, since
     which cells get painted is exactly what each case is checking. Requires InitializeSector first
     (also sets SizeOfGalaxy, GetNebula/PutNebula's own requirement, same as RunStarbaseCase). Grid is
     a plain Pascal String (255-char cap under -Mtp), so SizeOfGalaxy*SizeOfGalaxy must stay under
     that -- fine for a targeted case, not meant for galaxy-scale sizes. }
   var
      parts: array[0..3] of LongInt;
      x,y: Integer;
      Coord: XYCoord;
      Grid: String;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   InitializeSector(parts[0]);

   ForcedRandomValue:=parts[3];

   if parts[1]=1 then
      NebulaeBand
   else if parts[1]=2 then
      NebulaePatches(parts[2])
   else
      begin
      WriteLn(StdErr,'runworld: unknown nebula mode ',parts[1]);
      Halt(1);
      end;

   Grid:='';
   for y:=1 to parts[0] do
      for x:=1 to parts[0] do
         begin
         Coord.x:=x;  Coord.y:=y;
         if GetNebula(Coord)=Nebula then
            Grid:=Grid+'1'
         else
            Grid:=Grid+'0';
         end;

   WriteLn('grid=',Grid);

   Dispose(Universe);
   end;

procedure RunRngCase(const arg: String);
   { Not a UpdateWorld/GalaxySetup domain at all -- a permanent regression fixture for the C# test
     project's own PascalRandom (a from-scratch port of this fpc runtime's real Random/RandSeed
     algorithm, empirically reverse-engineered against this exact toolchain: it's a Mersenne Twister
     variant with fpc-specific reseed/tempering behavior, not the classic Turbo Pascal LCG one might
     expect and not the newer Xoshiro128** generator later fpc releases moved to -- verified by probing
     this project's own installed fpc 3.2.2, not by trusting any RTL source line in isolation). Real
     (non-ForcedRandomValue) Random is otherwise never golden-file-covered anywhere in this harness,
     since every other domain needs a single repeatable Rnd() value, not a real sequence -- this domain
     exists so that whenever a future domain genuinely needs a real, non-degenerate multi-call RNG
     sequence (e.g. Phase 2 commit 2e's CREATERANDOMWORLDS, whose retry-on-collision loop breaks under
     ForcedRandomValue's fixed-offset convention), PascalRandom is already proven correct against real
     Pascal output before anything is built on top of it. }
   var
      parts: array[0..2] of LongInt;
      i: Integer;
      { AnsiString, not the default 255-char-capped String -- StateBlockBoundary's 701 comma-joined
        draws need well over 255 characters. }
      Values, Piece: AnsiString;
   begin
   ParseFields(arg,parts);

   RandSeed:=parts[0];
   ForcedRandomValue:=-1;

   Values:='';
   for i:=1 to parts[2] do
      begin
      if i>1 then
         Values:=Values+',';
      Str(Random(parts[1]),Piece);
      Values:=Values+Piece;
      end;

   WriteLn('values=',Values);
   end;

procedure RunScenarioCase(const arg: String);
   { Reimplements NEWGAME.PAS:1650-1812's (LoadScenario) own header-parse + command-dispatch loop
     fresh -- it's saturated with real, load-bearing DOS UI (OpenWindow bracketing the whole
     procedure, ScenarioIntroduction deciding NoOfPlayers via a menu, ClrScr/PressAnyKey/CloseWindow)
     that can't be dropped without changing what the loop actually does, unlike the two single-call
     UI drops in the relocated primitives below it -- see UPDATE.PAS's own PATCH comment at this
     relocation. Calls those relocated primitives (ScenaError/NextInteger/GetRandomXY/GetRandomRange/
     GetNextXY/LoadClassArray/LoadTechArray/DefineXYPoint/DefineZone/ReadModifierList/
     SetTrillumReserves/GetRandomEmpireName/CreatePlayerEmpire/CreateNPEmpire/CreateWorld/CreateBase/
     CreateGate/CreateNebula/CreateRandomNebula/CreateSRMs/CreateRandomWorlds) in the same order/shape
     real LoadScenario does, tracking FirstWorld/FirstBase itself (nothing relocated maintains
     SetOfActivePlanets, so this driver's own counters are the only record of which Planet/Starbase
     slots are active -- matching CreateWorld/CreateBase/CreateRandomWorlds' own VAR FirstWorld/
     FirstBase parameters exactly).

     Player identity is a fixed "PlayerN"/"pwN"/not-an-empress convention, not a CLI field -- a name
     string can't round-trip through this domain's otherwise-all-integer sibling cases, and the C#
     side's own ScenarioLoaderGoldenTests hard-codes the identical convention so both sides agree.

     Emits an aggregate checksum over the whole loaded Universe^ rather than a per-entity dump (see
     this domain's own header-comment entry for why): Year, active Planet/Starbase counts and
     Card(SetOfActiveGates), active empire count, and sums of every RNG-or-parsing-sensitive numeric
     field across all active planets/starbases/empires plus a galaxy-wide painted-nebula-cell count
     and mined-cell count -- a wrong coordinate, a dropped jitter, a missed empire, or a nebula/mine
     placement bug perturbs at least one of these. }
   var
      PathAndCounts: String;
      Path: String;
      Seed,NumPlayers: LongInt;
      CommaPos1,CommaPos2: Integer;

      ScenaFile: Text;
      Dummy,SoG,NoP,Diff,MinLen,MaxLen,FirstYear: LongInt;
      VersLine,Title: AnsiString;
      BadToken: Boolean;
      Line: LineStr;

      FirstWorld,FirstBase: Word;
      EmpireName,Password: EmpireNameArray;
      Sex: SexArray;
      TriRes: Word;
      ClassTable: ClassArray;
      TechTable: TechArray;
      Zone: ZoneArray;
      XYPoint: XYPointArray;

      i,x,y: Integer;
      Emp: Empire;
      NoOfPlayersEmp: Empire;
      NumStr: String8;
      Cell: XYCoord;

      PlanetCount,StarbaseCount,StargateCount,EmpireCount: LongInt;
      SumPlanetX,SumPlanetY,SumPop,SumEff,SumTri,SumClass,SumTech: LongInt;
      SumShips,SumCargo,SumDefns: LongInt;
      SumStarbasePop,SumStarbaseEff: LongInt;
      SumEmpireTech,SumRevFactor,SumCentralModifier,SumEmpress: LongInt;
      NebulaCellCount,MinedCellCount: LongInt;
      ShpI: ShipTypes;
      CarI: CargoTypes;
      DefI: DefnsTypes;

   begin
   { Path,Seed,NumPlayers -- Path may itself be an absolute Windows path (drive-letter colon, no
     commas), so this is a manual split on the LAST two commas, not ParseFields (which assumes every
     field is a LongInt). }
   PathAndCounts:=arg;
   CommaPos2:=Length(PathAndCounts);
   while (CommaPos2>0) and (PathAndCounts[CommaPos2]<>',') do
      Dec(CommaPos2);
   CommaPos1:=CommaPos2-1;
   while (CommaPos1>0) and (PathAndCounts[CommaPos1]<>',') do
      Dec(CommaPos1);

   Path:=Copy(PathAndCounts,1,CommaPos1-1);
   Seed:=ParseLongInt(Copy(PathAndCounts,CommaPos1+1,(CommaPos2-CommaPos1)-1));
   NumPlayers:=ParseLongInt(Copy(PathAndCounts,CommaPos2+1,Length(PathAndCounts)));

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   ScenarioError:=False;
   DebugScena:=False;
   RandSeed:=Seed;
   ForcedRandomValue:=-1;

   Assign(ScenaFile,Path);
   Reset(ScenaFile);

   ReadLn(ScenaFile,VersLine);
   Val(Copy(VersLine,10,2),ScenaVersion,Dummy);

   Title:=DFA1NextToken(ScenaFile,BadToken);
   NextInteger(ScenaFile);              { Seed field in the file itself -- discarded, see header comment. }
   NextInteger(ScenaFile);              { MinPlay -- not enforced; NumPlayers is this call's own input. }
   NextInteger(ScenaFile);              { MaxPlay -- same reason. }
   SoG:=NextInteger(ScenaFile);
   NoP:=NextInteger(ScenaFile);         { NoOfPlanets cap -- Planet[] is a fixed 200-slot array regardless. }
   Diff:=NextInteger(ScenaFile);        { Difficulty -- never read anywhere in real NEWGAME.PAS either. }
   MinLen:=NextInteger(ScenaFile);
   MaxLen:=NextInteger(ScenaFile);
   FirstYear:=NextInteger(ScenaFile);

   InitializeSector(SoG);
   Year:=FirstYear;

   { NEWGAME.PAS:1388-1515 (ScenarioIntroduction), file-consumption only -- the display/PressAnyKey
     pagination and GetNoOfPlayers/NoChoice prompt are dead UI (NumPlayers is this call's own input
     instead); NEWPAGE markers only affect how the real UI paginates, not where the text block ends,
     so scanning straight for ENDTEXT (ignoring NEWPAGE) lands the file cursor in the same place. }
   repeat
      Line:=DFA1NextToken(ScenaFile,BadToken);
      AllUpCase(Line);
   until (Line='BEGINTEXT') or BadToken or EoF(ScenaFile);
   ReadLn(ScenaFile);
   repeat
      ReadLn(ScenaFile,Line);
      AllUpCase(Line);
   until (Pos('ENDTEXT',Line)<>0) or EoF(ScenaFile);

   FirstWorld:=1;
   FirstBase:=1;
   { PATCH-note: matches NEWGAME.PAS:1710-1713's own FillChar block exactly -- Zone/XYPoint are local
     VAR parameters here (unlike real LoadScenario's own locals, which the same reasoning still
     applies to), so leftover stack content from this same process's PREVIOUS RunScenarioCase call
     would otherwise leak into DefineXYPoint's own "find an empty Name slot" scan, corrupting a later
     scenario's coordinate resolution -- confirmed empirically via a real cross-case corruption when
     running multiple scenario cases back to back before this fix was added. }
   FillChar(EmpireName,SizeOf(EmpireName),0);
   FillChar(Password,SizeOf(Password),0);
   FillChar(Sex,SizeOf(Sex),0);
   FillChar(Zone,SizeOf(Zone),0);
   FillChar(XYPoint,SizeOf(XYPoint),0);
   for i:=0 to NumPlayers-1 do
      begin
      Str(i+1,NumStr);
      EmpireName[Empire(i)]:='Player'+NumStr;
      Password[Empire(i)]:='pw'+NumStr;
      Sex[Empire(i)]:=False;
      end;

   Zone[1].x1:=1;  Zone[1].y1:=1;
   Zone[1].x2:=SoG;  Zone[1].y2:=SoG;
   TriRes:=100;
   NoOfPlayersEmp:=Empire(NumPlayers-1);

   repeat
      Line:=DFA1NextToken(ScenaFile,BadToken);
      if BadToken then
         ScenaError('ERROR: Bad command token "'+Line+'"')
      else
         begin
         AllUpCase(Line);
         if Line='DEBUGSCENARIO' then
            DebugScena:=True
         else if Line='BEGINDESCRIPTION' then
            begin
            repeat
               ReadLn(ScenaFile,Line);
               AllUpCase(Line);
            until (Pos('ENDDESCRIPTION',Line)<>0) or EoF(ScenaFile);
            end
         else if Line='CLASSTABLE' then
            LoadClassArray(ScenaFile,ClassTable)
         else if Line='CREATENEBULA' then
            CreateNebula(ScenaFile,XYPoint,Zone)
         else if Line='CREATERANDOMNEBULA' then
            CreateRandomNebula(ScenaFile)
         else if Line='CREATESRMS' then
            CreateSRMs(ScenaFile,XYPoint,Zone)
         else if Line='CREATEPLAYEREMPIRE' then
            CreatePlayerEmpire(ScenaFile,Empire(NumPlayers-1),EmpireName,Password,Sex)
         else if Line='CREATENPEMPIRE' then
            CreateNPEmpire(ScenaFile,EmpireName)
         else if Line='CREATERANDOMWORLDS' then
            CreateRandomWorlds(ScenaFile,FirstWorld,ClassTable,TechTable,TriRes,Zone)
         else if Line='CREATEWORLD' then
            CreateWorld(ScenaFile,FirstWorld,NoOfPlayersEmp,XYPoint,Zone)
         else if Line='CREATESTARBASE' then
            CreateBase(ScenaFile,FirstBase,NoOfPlayersEmp,XYPoint,Zone)
         else if Line='CREATESTARGATE' then
            CreateGate(ScenaFile,Empire(NumPlayers-1),XYPoint,Zone)
         else if Line='DEFINEZONE' then
            DefineZone(ScenaFile,XYPoint,Zone)
         else if Line='DEFINEXY' then
            DefineXYPoint(ScenaFile,XYPoint,Zone)
         else if Line='REPORT' then
            DFA1NextToken(ScenaFile,BadToken)
         else if Line='TECHTABLE' then
            LoadTechArray(ScenaFile,TechTable)
         else if Line='SETTRILLUMRESERVES' then
            SetTrillumReserves(ScenaFile,TriRes)
         else if Line<>'ENDSCENARIO' then
            ScenaError('ERROR: Unknown command "'+Line+'"');
         end;
   until (Line='ENDSCENARIO') or EoF(ScenaFile) or ScenarioError;

   Close(ScenaFile);

   { Aggregate checksum -- see this procedure's own doc comment for why. }
   PlanetCount:=0;  SumPlanetX:=0;  SumPlanetY:=0;  SumPop:=0;  SumEff:=0;  SumTri:=0;
   SumClass:=0;  SumTech:=0;  SumShips:=0;  SumCargo:=0;  SumDefns:=0;
   for i:=1 to FirstWorld-1 do
      with Universe^.Planet[i] do
         begin
         Inc(PlanetCount);
         Inc(SumPlanetX,XY.x-1);
         Inc(SumPlanetY,XY.y-1);
         Inc(SumPop,Pop);
         Inc(SumEff,Eff);
         Inc(SumTri,TriReserve);
         Inc(SumClass,Ord(Cls));
         Inc(SumTech,Ord(Tech));
         for ShpI:=fgt to trn do
            Inc(SumShips,Ships[ShpI]);
         for CarI:=men to tri do
            Inc(SumCargo,Cargo[CarI]);
         for DefI:=LAM to ion do
            Inc(SumDefns,Defns[DefI]);
         end;

   StarbaseCount:=0;  SumStarbasePop:=0;  SumStarbaseEff:=0;
   for i:=1 to FirstBase-1 do
      with Universe^.Starbase[i] do
         begin
         Inc(StarbaseCount);
         Inc(SumStarbasePop,Pop);
         Inc(SumStarbaseEff,Eff);
         Inc(SumShips,Ships[fgt]);  { PATCH-note: only Ships[fgt] folded in here as a cheap starbase-touch signal -- full starbase ship/cargo/defns sums would need the same three loops as planets, not worth doubling for a checksum. }
         end;

   StargateCount:=0;
   for i:=1 to MaxNoOfStargates do
      if i in SetOfActiveGates then
         Inc(StargateCount);

   EmpireCount:=0;  SumEmpireTech:=0;  SumRevFactor:=0;  SumCentralModifier:=0;  SumEmpress:=0;
   for Emp:=Empire1 to Empire8 do
      if Universe^.EmpireData[Emp].InUse then
         with Universe^.EmpireData[Emp] do
            begin
            Inc(EmpireCount);
            Inc(SumEmpireTech,Ord(TechnologyLevel));
            Inc(SumRevFactor,RevFactor);
            if CentralEMD in Modifiers then
               Inc(SumCentralModifier);
            if IsAnEmpress then
               Inc(SumEmpress);
            end;

   NebulaCellCount:=0;
   for x:=1 to SoG do
      for y:=1 to SoG do
         begin
         Cell.x:=x;  Cell.y:=y;
         if GetNebula(Cell)<>NoNeb then
            Inc(NebulaCellCount);
         end;

   MinedCellCount:=0;
   for x:=1 to SoG do
      for y:=1 to SoG do
         begin
         Cell.x:=x;  Cell.y:=y;
         if EnemyMine(Cell)<>Indep then
            Inc(MinedCellCount);
         end;

   WriteLn('year=',Year,
           ';planetcount=',PlanetCount,
           ';sumplanetx=',SumPlanetX,';sumplanety=',SumPlanetY,
           ';sumpop=',SumPop,';sumeff=',SumEff,';sumtri=',SumTri,
           ';sumclass=',SumClass,';sumtech=',SumTech,
           ';sumships=',SumShips,';sumcargo=',SumCargo,';sumdefns=',SumDefns,
           ';starbasecount=',StarbaseCount,';sumstarbasepop=',SumStarbasePop,';sumstarbaseeff=',SumStarbaseEff,
           ';stargatecount=',StargateCount,
           ';empirecount=',EmpireCount,';sumempiretech=',SumEmpireTech,';sumrevfactor=',SumRevFactor,
           ';sumcentralmodifier=',SumCentralModifier,';sumempress=',SumEmpress,
           ';nebulacellcount=',NebulaCellCount,';minedcellcount=',MinedCellCount);

   Dispose(Universe);
   end;

procedure RunCaseMode;
   var
      domain: String;
      i: Integer;
   begin
   domain:=ParamStr(2);
   for i:=3 to ParamCount do
      if domain='techlevel' then
         RunTechLevelCase(ParamStr(i))
      else if domain='military' then
         RunMilitaryCase(ParamStr(i))
      else if domain='starbase' then
         RunStarbaseCase(ParamStr(i))
      else if domain='ambrosia' then
         RunAmbrosiaCase(ParamStr(i))
      else if domain='revolution' then
         RunRevolutionCase(ParamStr(i))
      else if domain='production' then
         RunProductionCase(ParamStr(i))
      else if domain='empire' then
         RunEmpireCase(ParamStr(i))
      else if domain='empirecreate' then
         RunEmpireCreateCase(ParamStr(i))
      else if domain='construction' then
         RunConstructionCase(ParamStr(i))
      else if domain='trillumreserves' then
         RunTrillumReservesCase(ParamStr(i))
      else if domain='randomplanet' then
         RunRandomPlanetCase(ParamStr(i))
      else if domain='nebula' then
         RunNebulaCase(ParamStr(i))
      else if domain='rng' then
         RunRngCase(ParamStr(i))
      else if domain='scenario' then
         RunScenarioCase(ParamStr(i))
      else
         begin
         WriteLn(StdErr,'runworld: unknown domain "',domain,'"');
         Halt(1);
         end;
   end;

begin
if (ParamCount>0) and (ParamStr(1)='case') then
   RunCaseMode
else
   RunTechLevelCase('5,0,6,0');
end.
