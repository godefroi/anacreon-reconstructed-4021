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
   One output line per case, in order, to stdout -- consumed by PatchHarness
   in the C# test project via GoldenFile.Regenerate's runHarness override.
   With no arguments, runs a single hardcoded techlevel case as a
   human-readable sanity check (manual use).

   Not itself a patch target -- this is new code, checked in directly and
   copied into pascal/ by build.ps1 (or PatchHarness, from the C# test
   project) alongside the patched source.
--------------------------------------------------------------------------- *)

PROGRAM RunWorld;

USES Types, DataCnst, DataStrc, Galaxy, Int, Misc, PrimIntr, Environ, News, Update;

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
