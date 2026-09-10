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
     defenses   PlanetPop,TechOrd,Legions,NinjaLegions,TypOrd,Efficiency,
                CargoChe,CargoMet,CargoTri,TechnologyBitmask,RngFixedValue
                -> "lam=<v>;def=<v>;gdm=<v>;ion=<v>" -- TechnologyBitmask uses the same
                26-bit encoding as the empire domain (bit i = TechnologyTypes(i+1));
                only bits 0-3 (LAM,def,GDM,ion) matter here
     combat     AttackerCapTechOrd,DefenderTechOrd,DefenderClassOrd,DefenderRevIndex,
                AttackerFgt,AttackerHkr,AttackerJmp,AttackerPen,AttackerSsp,
                DefenderFgt,DefenderHkr,DefenderJmp,DefenderJtn,DefenderPen,DefenderSsp,DefenderTrn,
                DefenderLam,DefenderDef,DefenderGdm,DefenderIon,
                FighterGroupTargetOrd(0=NoRes else 1+the C# port's own AttackType ordinal, LAM..nnj
                with no NoRes slot -- Pascal's own AttackTypes ordinal for the same value is exactly
                one more, from its leading NoRes=0 member),RngFixedValue
                -> "groups=<v>;g<N>num=<v>;g<N>sta=<GroupStatus ordinal>;
                    en_fgt=<v>;en_hkr=<v>;en_jmp=<v>;en_pen=<v>;en_ssp=<v>;
                    kill<ShpI>=<v>;cas<ShpI>=<v> (fgt..ssp, ShpI=ShipTypes ordinal);
                    surrenders=<0|1>" -- one round of real Battle at the DpSpc shell; see
                RunCombatCase's own comment
     npeattack  DefenderTechOrd,DefenderClassOrd,AttackerCarriesTroops(0/1 -- adds a 20-ship jtn group
                carrying 1000 nnj cargo to the attacker's fleet when nonzero),DefenderFgt,DefenderHkr,
                DefenderMen,IntentOrd(AttackIntentionTypes ordinal, NoAIT..CaptTrnAIT -- same ordinal
                order as the C# port's own AttackIntentionType),TargetIsFleet(0=Planet[2] via
                WorldEngage,1=Fleet[2] via FleetEngage),RngFixedValue,
                Planet3Present(0/1 -- a second Empire2 world, positioned/populated by the next 5
                fields, letting a case drive ConquerEmpire's per-planet cascade once Planet[2]'s
                capital falls; ignored/all-zero when 0),Planet3X,Planet3Y,Planet3Pop,Planet3RevIndex,
                Planet3TechOrd
                -> "result=<AttackResultTypes ordinal>;cas_fgt=<v>;cas_hkr=<v>;cas_jtn=<v>;cas_nnj=<v>;
                    kill_fgt=<v>;kill_hkr=<v>;kill_men=<v>;def_owner=<Empire ordinal>;def_eff=<v>;
                    def_rev=<v>;def_type=<WorldTypes ordinal>[;p3_owner=<v>;p3_eff=<v>;p3_rev=<v>;
                    p3_type=<v> -- only when Planet3Present];newcap_idx=<Empire2's post-attack capital
                    Planet index, 0 if none>" -- real NPEAttack end to end (its own multi-round
                FleetRetreats/Targetting/GroupEngage/AdvanceGroups loop, not one round in isolation),
                now including outcome application (ResolveAttack/ConquerWorld/ConquerEmpire/
                RestoreCombatant, Phase 5 commit 5f); see RunNpeAttackCase's own comment. Note:
                Planet3X/Y are laid out relative to Planet[1]=(0,0) (attacker capital) and
                Planet[2]=(50,50) (defender capital), both fixed by RunNpeAttackCase itself, not
                caller-supplied
     lamattack  TargetIsFleet(0=Planet[1]'s Defns,1=Fleet[1]'s Ships),LAMToUse,
                Fgt,Hkr,Pen,Trn(Fleet[1]'s ship counts, ignored when TargetIsFleet=0),
                Lam,Def,Gdm,Ion(Planet[1]'s defense counts, ignored when TargetIsFleet=1)
                -> "shipsdest_fgt=<v>;shipsdest_hkr=<v>;shipsdest_pen=<v>;shipsdest_trn=<v>;
                    defnsdest_lam=<v>;defnsdest_def=<v>;defnsdest_gdm=<v>;defnsdest_ion=<v>" --
                real ATTACK.PAS's own LAMAttack called directly (not through NPEAttack -- LAMAttack
                has no Rnd calls at all, so no RngFixedValue field here), see RunLamAttackCase's own
                comment. Both target and player are always Empire2/Empire1 respectively; DestroyFleet
                is the same no-op stand-in ATTACK.PAS.patch already carries for 5f, so this domain
                only asserts LAMAttack's own ShipsDest/DefnsDest VAR out-params, not whether a
                totally-destroyed fleet was actually removed.
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
     fleetlogistics Fgt,Hkr,Jmp,Jtn,Pen,Ssp,Trn,Men,Nnj,Amb,Che,Met,Sup,Tri -> "fuelcap=<v,6dp>;
                fuelcons=<v,6dp>;cargospace=<v>;balanced_men=<v>;balanced_nnj=<v>;balanced_amb=<v>;
                balanced_che=<v>;balanced_met=<v>;balanced_sup=<v>;balanced_tri=<v>" -- FuelCapacity/
                FuelConsumption/FleetCargoSpace/BalanceFleet (MISC.PAS:168-222, INTRFACE.PAS:431-465
                as trimmed into this harness's own INTRFACE.PAS -- see that file's header comment for
                why), called directly against a hand-built ShipArray/CargoArray; no Universe^/Rnd
                involved, these are pure functions over their parameters. balanced_* is Cr after
                BalanceFleet runs, letting a case drive an over-capacity fleet and check the trim.
     fleetmove  PosX,PosY,DestX,DestY,NebulaX,NebulaY(both 0 disables),GateKind(0=none,1=public
                gte,2=private lnk)AtPos,GateOwnerOrd,DestGateKind(same encoding)AtDest,
                DestGateOwnerOrd,FleetOwnerKnowsDestGate(0/1),FortressAtPos(0/1 -- mutually exclusive
                with GateKind<>0 at Pos, matching real Pascal: a sector's Obj slot holds at most one
                thing) -> "newpos_x=<v>;newpos_y=<v>;passgate=<0|1>;passfortress=<0|1>" -- GetNewPos
                (FLEET.PAS:437-450) and PassingThroughGate/PassingThroughFortress (relocated into
                this harness's own INTRFACE.PAS), against one Empire1 fleet at (PosX,PosY). Requires
                InitializeSector plus direct Sector[x]^[y].Obj writes for the gate(s)/fortress, same
                requirement as the starbase/probescout/construction domains.
     probescout DestOwnerOrd,DestLegions,DestAlreadyScouted(0/1),RngFixedValue -> "destscouted=<0|1>;
                ringscouted=<0|1>" -- calls the already-exported ProbeScout (INTRFACE.PAS:1289-1344)
                directly against one planet at the probe's destination (5,5) and one at the very next
                ring cell in Pascal's fixed offset order, (5,4). Covers ISqrt(Cargo[men]) and the
                Rnd(1,100)<ChanceToDestroy threshold plus its Exit-before-ScoutObject sequencing -- ring
                ordering/early-exit control flow itself is hardcoded-tested on the C# side
                (VisibilityHandlerProbeTests), since there's no separate Pascal formula to cross-check
                there.
     groundtruthrng Seed,Range,Count -> "values=<Count comma-joined GroundTruthNextU32-scaled draws
                after GroundTruthSeed:=Seed>" -- not a UpdateWorld/GalaxySetup domain; a standing
                regression fixture for the C# test project's GroundTruthRandom, Rnd's own ground-truth
                generator (see RunGroundTruthRngCase's own comment and reference/verify/README.md's
                "ground-truth RNG is a generator this project owns" section)
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

USES Types, DataCnst, DataStrc, Galaxy, Int, Misc, PrimIntr, Environ, News, Update, Attack, AttNPE, Fleet, Intrface, DFA, Strg, NewGame;

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

procedure RunDefensesCase(const arg: String);
   { Same shape as RunMilitaryCase (Empire1, capital pointing at itself so
     UpdateTechLevel can't drift Tech mid-tick and perturb anything read
     downstream), plus the fields UpdateDefenses itself reads: Cargo[nnj]
     (TroopStrength's other half), Efficiency (BuildRate), and Cargo[che,met,tri]
     (the two-pass raw-material draw -- deliberately settable low to exercise
     the DefLack clamp). TechnologyBitmask gates which of LAM/def/GDM/ion can
     build at all; the case set covers both "researched" and "not researched"
     so DefI IN Technology's guard is exercised, not just always-true. The
     real UpdateMilitary step still runs first (same real UpdateWorld
     sequence as every other domain) and may grow Cargo[men] toward its own
     optimum before UpdateDefenses ever reads TroopStrength -- not shielded
     against, since the golden file captures whatever the real pipeline
     produces end to end, the same way MilitaryCase's own legions=<value>
     already does for a different field. }
   var
      parts: array[0..10] of LongInt;
      ID, CapID: IDNumber;
      i: Integer;
      techSet: TechnologySet;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   NoOfPlanets:=1;

   Universe^.Planet[1].Cls:=ClsM;
   Universe^.Planet[1].Typ:=WorldTypes(parts[4]);
   Universe^.Planet[1].Tech:=TechLevel(parts[1]);
   Universe^.Planet[1].Eff:=parts[5];
   Universe^.Planet[1].Pop:=parts[0];
   Universe^.Planet[1].Cargo[sup]:=9999;
   Universe^.Planet[1].Cargo[men]:=parts[2];
   Universe^.Planet[1].Cargo[nnj]:=parts[3];
   Universe^.Planet[1].Cargo[che]:=parts[6];
   Universe^.Planet[1].Cargo[met]:=parts[7];
   Universe^.Planet[1].Cargo[tri]:=parts[8];
   Universe^.Planet[1].Emp:=Empire1;

   SetOfActivePlanets:=[1];
   SetOfPlanetsOf[Empire1]:=[1];
   Universe^.EmpireData[Empire1].InUse:=True;
   Universe^.EmpireData[Empire1].IsAPlayer:=False;
   CapID.ObjTyp:=Pln;  CapID.Index:=1;
   Universe^.EmpireData[Empire1].Capital:=CapID;

   techSet:=[];
   for i:=0 to 25 do
      if ((parts[9] shr i) and 1)=1 then
         techSet:=techSet+[TechnologyTypes(i+1)];
   Universe^.EmpireData[Empire1].Technology:=techSet;

   ForcedRandomValue:=parts[10];

   ID.ObjTyp:=Pln;  ID.Index:=1;
   UpdateWorld(ID);

   WriteLn('lam=',Universe^.Planet[1].Defns[LAM],
           ';def=',Universe^.Planet[1].Defns[def],
           ';gdm=',Universe^.Planet[1].Defns[GDM],
           ';ion=',Universe^.Planet[1].Defns[ion]);

   Dispose(Universe);
   end;

procedure RunCombatCase(const arg: String);
   { One round of real ATTACK.PAS group/shell combat at the DpSpc shell: Empire1's fleet (built via
     DefaultDistribution, one group each of fgt/hkr/jmp/pen/ssp -- fixed order, deliberately no jtn/trn
     since ground-troop groups only exist after the not-yet-ported AdvanceGroups swap) attacks Empire2's
     capital planet. CalculateCombatData/GetEnemy/DefaultDistribution/Battle/EnemySurrenders are all
     exercised for real; Empire2's DefenseSettings is seeded from the same InitDefenseRecord constant
     the C# side's EmpireFactory.SeedDefenseSettings copies, so GetEnemy's per-shell ship split is
     nonzero and comparable on both sides. FighterGroupTargetOrd optionally aims the fighter group's own
     Trg at a real AttackType (rather than leaving it NoRes, the default DefaultDistribution produces)
     so GroupAttack's actual-damage path gets exercised too, not just the defender's counterattack. }
   var
      parts: array[0..21] of LongInt;
      AttackerCapID, DefenderCapID, TargetID, FltID: IDNumber;
      NoOfGroups: Byte;
      Gp: GroupArray;
      En: EnemyArray;
      Details: DetailArray;
      Casualties, Killed: AttackArray;
      CombatData: CombatDataRecord;
      GroupsDestroyed: GroupSet;
      i: Integer;
      ShpI: AttackTypes;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   NoOfPlanets:=2;

   { Attacker's capital: Planet[1], Empire1, tech only (nothing else read by this domain). }
   Universe^.Planet[1].Cls:=ClsM;
   Universe^.Planet[1].Typ:=CapTyp;
   Universe^.Planet[1].Tech:=TechLevel(parts[0]);
   Universe^.Planet[1].Emp:=Empire1;

   { Defender: Planet[2], Empire2, also its own capital. }
   Universe^.Planet[2].Cls:=WorldClass(parts[2]);
   Universe^.Planet[2].Typ:=CapTyp;
   Universe^.Planet[2].Tech:=TechLevel(parts[1]);
   Universe^.Planet[2].RevIndex:=parts[3];
   Universe^.Planet[2].Ships[fgt]:=parts[9];
   Universe^.Planet[2].Ships[hkr]:=parts[10];
   Universe^.Planet[2].Ships[jmp]:=parts[11];
   Universe^.Planet[2].Ships[jtn]:=parts[12];
   Universe^.Planet[2].Ships[pen]:=parts[13];
   Universe^.Planet[2].Ships[ssp]:=parts[14];
   Universe^.Planet[2].Ships[trn]:=parts[15];
   Universe^.Planet[2].Defns[LAM]:=parts[16];
   Universe^.Planet[2].Defns[def]:=parts[17];
   Universe^.Planet[2].Defns[GDM]:=parts[18];
   Universe^.Planet[2].Defns[ion]:=parts[19];
   Universe^.Planet[2].Emp:=Empire2;

   SetOfActivePlanets:=[1,2];
   SetOfPlanetsOf[Empire1]:=[1];
   SetOfPlanetsOf[Empire2]:=[2];

   Universe^.EmpireData[Empire1].InUse:=True;
   AttackerCapID.ObjTyp:=Pln;  AttackerCapID.Index:=1;
   Universe^.EmpireData[Empire1].Capital:=AttackerCapID;

   Universe^.EmpireData[Empire2].InUse:=True;
   Universe^.EmpireData[Empire2].DefenseSettings:=InitDefenseRecord;
   DefenderCapID.ObjTyp:=Pln;  DefenderCapID.Index:=2;
   Universe^.EmpireData[Empire2].Capital:=DefenderCapID;

   { Attacker's fleet: Fleet[1], Empire1. }
   New(Universe^.Fleet[1]);
   FillChar(Universe^.Fleet[1]^,SizeOf(Universe^.Fleet[1]^),0);
   Universe^.Fleet[1]^.XY.x:=5;  Universe^.Fleet[1]^.XY.y:=5;
   Universe^.Fleet[1]^.Emp:=Empire1;
   Universe^.Fleet[1]^.Ships[fgt]:=parts[4];
   Universe^.Fleet[1]^.Ships[hkr]:=parts[5];
   Universe^.Fleet[1]^.Ships[jmp]:=parts[6];
   Universe^.Fleet[1]^.Ships[pen]:=parts[7];
   Universe^.Fleet[1]^.Ships[ssp]:=parts[8];
   SetOfActiveFleets:=[1];

   ForcedRandomValue:=parts[21];

   FltID.ObjTyp:=Flt;  FltID.Index:=1;
   TargetID.ObjTyp:=Pln;  TargetID.Index:=2;

   CalculateCombatData(Empire1,FltID,TargetID,CombatData);
   GetEnemy(TargetID,En);
   DefaultDistribution(FltID,NoOfGroups,Gp);

   { parts[20]=1+X's C# AttackType ordinal (LAM..nnj, no NoRes slot) -- Pascal's own AttackTypes
     ordinal for the same X is exactly one more than that (its own leading NoRes=0 member), so
     AttackTypes(parts[20]) (not parts[20]-1) is the direct, unshifted conversion. }
   if parts[20]<>0 then
      Gp[1].Trg:=AttackTypes(parts[20]);

   FillChar(Details,SizeOf(Details),0);
   FillChar(Casualties,SizeOf(Casualties),0);
   FillChar(Killed,SizeOf(Killed),0);

   Battle(NoOfGroups,Gp,GroupsDestroyed,En,DpSpc,CombatData,Details,Casualties,Killed);

   Write('groups=',NoOfGroups);
   for i:=1 to NoOfGroups do
      Write(';g',i,'num=',Gp[i].Num,';g',i,'sta=',Ord(Gp[i].Sta));
   Write(';en_fgt=',En[DpSpc][fgt],';en_hkr=',En[DpSpc][hkr],';en_jmp=',En[DpSpc][jmp],
         ';en_pen=',En[DpSpc][pen],';en_ssp=',En[DpSpc][ssp]);
   for ShpI:=fgt to ssp do
      Write(';kill',Ord(ShpI),'=',Killed[ShpI],';cas',Ord(ShpI),'=',Casualties[ShpI]);
   WriteLn(';surrenders=',Ord(EnemySurrenders(NoOfGroups,Gp,En,Casualties,Killed,CombatData)));

   Dispose(Universe^.Fleet[1]);
   Dispose(Universe);
   end;

procedure RunNpeAttackCase(const arg: String);
   { Runs real ATTNPE.PAS's own NPEAttack end to end -- now the FULL body (Phase 5 commit 5f restored
     ATTACK.PAS's ConquerWorld/ConquerEmpire/RestoreCombatant/ResolveAttack, see ATTACK.PAS.patch),
     not just the Casualties/Killed/Result-producing resolution loop 5e covered: Empire1's fleet (200
     fgt, 200 hkr, plus an optional troop-carrying jtn group when AttackerCarriesTroops<>0) attacks
     either Empire2's capital planet (WorldEngage) or Empire2's own fleet (FleetEngage). Empire2's
     DefenseSettings is the same InitDefenseRecord distribution EmpireFactory.SeedDefenseSettings
     seeds on the C# side.

     Planet[1] (attacker capital) sits at (0,0); Planet[2] (defender capital, the usual Planet target)
     sits at (50,50) -- fixed, distinct locations so ConquerEmpire's own Dist/DistToConq math (relative
     to each capital) is exercised meaningfully once Planet[2] falls and EmpireConquered fires
     ConquerEmpire. Planet[3] is optional (Planet3Present<>0): a second world of Empire2's, positioned
     and populated by the caller, letting a case drive any one of ConquerEmpire's four per-planet
     branches (immediate conquest / forced independence / distance-conquest / new-capital-candidate)
     deliberately -- see NpeAttackCases.cs's own doc comment for which case drives which branch. }
   var
      parts: array[0..14] of LongInt;
      AttackerCapID, DefenderCapID, TargetID, FltID, Planet2ID, Planet3ID, NewCapID: IDNumber;
      Result: AttackResultTypes;
      Killed, Casualties: AttackArray;
      Planet3Present: Boolean;
   begin
   ParseFields(arg,parts);
   Planet3Present:=parts[9]<>0;

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   NoOfPlanets:=3;

   { PATCH-note: found via a real Runtime error 216 once ATTACK.PAS's real DestroyFleet (not the
     old lane's no-op stand-in) started running for real -- DestroyFleet's own
     Sector[FltPos.x]^[FltPos.y].Flts write needs Sector allocated at the attacker/target fleets'
     (5,5) position, which nothing in this procedure ever allocated (this domain never exercised
     any Sector-touching code path before). 50 covers every position this domain places anything
     at, including Planet[2]/Planet3 at up to (50,50) -- same InitializeSector pattern every other
     Sector-touching domain in this file already uses. }
   InitializeSector(50);

   Universe^.Planet[1].Cls:=ClsM;
   Universe^.Planet[1].Typ:=CapTyp;
   Universe^.Planet[1].Tech:=JmpTchLvl;
   Universe^.Planet[1].Emp:=Empire1;
   Universe^.Planet[1].XY.x:=0;  Universe^.Planet[1].XY.y:=0;

   Universe^.Planet[2].Cls:=WorldClass(parts[1]);
   Universe^.Planet[2].Typ:=CapTyp;
   Universe^.Planet[2].Tech:=TechLevel(parts[0]);
   Universe^.Planet[2].Ships[fgt]:=parts[3];
   Universe^.Planet[2].Ships[hkr]:=parts[4];
   Universe^.Planet[2].Cargo[men]:=parts[5];
   Universe^.Planet[2].Emp:=Empire2;
   Universe^.Planet[2].XY.x:=50;  Universe^.Planet[2].XY.y:=50;

   SetOfActivePlanets:=[1,2];
   SetOfPlanetsOf[Empire1]:=[1];
   SetOfPlanetsOf[Empire2]:=[2];

   if Planet3Present then
      begin
      { Planet3X,Planet3Y,Planet3Pop,Planet3RevIndex,Planet3TechOrd -- parts[10..14]. }
      Universe^.Planet[3].Cls:=ClsM;
      Universe^.Planet[3].Typ:=AgrTyp;
      Universe^.Planet[3].Tech:=TechLevel(parts[14]);
      Universe^.Planet[3].XY.x:=parts[10];  Universe^.Planet[3].XY.y:=parts[11];
      Universe^.Planet[3].Pop:=parts[12];
      Universe^.Planet[3].RevIndex:=parts[13];
      Universe^.Planet[3].Emp:=Empire2;

      SetOfActivePlanets:=SetOfActivePlanets+[3];
      SetOfPlanetsOf[Empire2]:=SetOfPlanetsOf[Empire2]+[3];
      end;

   Universe^.EmpireData[Empire1].InUse:=True;
   AttackerCapID.ObjTyp:=Pln;  AttackerCapID.Index:=1;
   Universe^.EmpireData[Empire1].Capital:=AttackerCapID;

   Universe^.EmpireData[Empire2].InUse:=True;
   Universe^.EmpireData[Empire2].DefenseSettings:=InitDefenseRecord;
   DefenderCapID.ObjTyp:=Pln;  DefenderCapID.Index:=2;
   Universe^.EmpireData[Empire2].Capital:=DefenderCapID;

   New(Universe^.Fleet[1]);
   FillChar(Universe^.Fleet[1]^,SizeOf(Universe^.Fleet[1]^),0);
   Universe^.Fleet[1]^.XY.x:=5;  Universe^.Fleet[1]^.XY.y:=5;
   Universe^.Fleet[1]^.Emp:=Empire1;
   Universe^.Fleet[1]^.Ships[fgt]:=200;
   Universe^.Fleet[1]^.Ships[hkr]:=200;
   if parts[2]<>0 then
      begin
      Universe^.Fleet[1]^.Ships[jtn]:=20;
      Universe^.Fleet[1]^.Cargo[nnj]:=1000;
      end;
   SetOfActiveFleets:=[1];

   FltID.ObjTyp:=Flt;  FltID.Index:=1;

   if parts[7]<>0 then
      begin
      { Fleet target: Fleet[2], Empire2, same fgt/hkr strength as the Planet[2] case above -- Cargo[men]
        is irrelevant here, GetEnemy's Flt branch never reads it. }
      New(Universe^.Fleet[2]);
      FillChar(Universe^.Fleet[2]^,SizeOf(Universe^.Fleet[2]^),0);
      Universe^.Fleet[2]^.XY.x:=5;  Universe^.Fleet[2]^.XY.y:=5;
      Universe^.Fleet[2]^.Emp:=Empire2;
      Universe^.Fleet[2]^.Ships[fgt]:=parts[3];
      Universe^.Fleet[2]^.Ships[hkr]:=parts[4];
      SetOfActiveFleets:=SetOfActiveFleets+[2];
      TargetID.ObjTyp:=Flt;  TargetID.Index:=2;
      end
   else
      begin
      TargetID.ObjTyp:=Pln;  TargetID.Index:=2;
      end;

   ForcedRandomValue:=parts[8];

   { PATCH-note: NPEAttack's real signature has no Killed/Casualties VAR out-params (NPEINTR.PAS
     calls it for real now that the fuller tree links the whole SCC) -- ATTNPE.PAS.patch adds
     GetLastKilled/GetLastCasualties test-only getters instead, see that patch's own comment. }
   NPEAttack(FltID,TargetID,AttackIntentionTypes(parts[6]),0,Result);
   Killed:=GetLastKilled;
   Casualties:=GetLastCasualties;

   Write('result=',Ord(Result),
         ';cas_fgt=',Casualties[fgt],';cas_hkr=',Casualties[hkr],';cas_jtn=',Casualties[jtn],';cas_nnj=',Casualties[nnj],
         ';kill_fgt=',Killed[fgt],';kill_hkr=',Killed[hkr],';kill_men=',Killed[men]);

   { Planet[2]'s post-attack state -- observable even when the target was a Fleet (unattacked, so
     unchanged) or the attacker retreated/was destroyed (also unchanged); only meaningfully different
     from the pre-attack input once ConquerWorld actually ran on it. }
   Planet2ID.ObjTyp:=Pln;  Planet2ID.Index:=2;
   Write(';def_owner=',Ord(GetStatus(Planet2ID)),';def_eff=',GetEfficiency(Planet2ID),
         ';def_rev=',GetRevIndex(Planet2ID),';def_type=',Ord(GetType(Planet2ID)));

   if Planet3Present then
      begin
      Planet3ID.ObjTyp:=Pln;  Planet3ID.Index:=3;
      Write(';p3_owner=',Ord(GetStatus(Planet3ID)),';p3_eff=',GetEfficiency(Planet3ID),
            ';p3_rev=',GetRevIndex(Planet3ID),';p3_type=',Ord(GetType(Planet3ID)));
      end;

   { Empire2's post-attack capital, if any -- 0 when ConquerEmpire's "totally destroyed" branch fired
     (DestroyEmpire's own no-op stub can't be observed directly, see ATTACK.PAS.patch) instead of
     choosing a new one. }
   GetCapital(Empire2,NewCapID);
   if NewCapID.ObjTyp=Pln then
      WriteLn(';newcap_idx=',NewCapID.Index)
   else
      WriteLn(';newcap_idx=0');

   { PATCH-note: found via a real Runtime error 204 (heap corruption from a double-dispose) on the
     case immediately after this one -- ATTACK.PAS's real DestroyFleet (not the old lane's no-op
     stand-in) may already have Disposed Fleet[1] and/or Fleet[2] during combat resolution above
     (e.g. a destroyed, not just surrendered, attacker or target), so this cleanup can't
     unconditionally Dispose either one anymore -- same SetOfActiveFleets liveness check
     DestroyFleet itself uses before touching a fleet. }
   if 1 in SetOfActiveFleets then
      Dispose(Universe^.Fleet[1]);
   if (parts[7]<>0) and (2 in SetOfActiveFleets) then
      Dispose(Universe^.Fleet[2]);
   Dispose(Universe);
   end;

procedure RunLamAttackCase(const arg: String);
   { Runs real ATTACK.PAS's own LAMAttack directly (Phase 5 commit 5g) -- not through NPEAttack,
     since LAMAttack has no Rnd calls at all (pure proportional-distribution arithmetic, Round/Trunc
     against ProtecNeeded/CombatTable), so there's no combat-engine setup to exercise, only the
     formula itself. Player is always Empire1; the target (Fleet[1] or Planet[1], whichever
     TargetIsFleet selects) is always owned by Empire2. DestroyFleet/FleetNameDestruction/
     BalanceFleet reuse the same no-op stand-ins ATTACK.PAS.patch already carries for 5f -- this
     domain reports LAMAttack's own ShipsDest/DefnsDest VAR out-params directly, not whatever state
     those stand-ins would have left behind. }
   var
      parts: array[0..9] of LongInt;
      TargetIsFleet: Boolean;
      LAMToUse: Resources;
      TargetID: IDNumber;
      ShipsDest: ShipArray;
      DefnsDest: DefnsArray;
   begin
   ParseFields(arg,parts);
   TargetIsFleet:=parts[0]<>0;
   LAMToUse:=parts[1];

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   { PATCH-note: same real-DestroyFleet-needs-Sector-allocated gap as RunNpeAttackCase's own
     InitializeSector fix -- LAMAttack's DestroyFleet path (real, not ATTACK.PAS.patch's old
     stand-in, now that that patch is gone) touches Sector[FltPos.x]^[FltPos.y] too. }
   InitializeSector(20);
   Universe^.EmpireData[Empire1].InUse:=True;
   Universe^.EmpireData[Empire2].InUse:=True;
   NoOfPlanets:=0;

   if TargetIsFleet then
      begin
      New(Universe^.Fleet[1]);
      FillChar(Universe^.Fleet[1]^,SizeOf(Universe^.Fleet[1]^),0);
      Universe^.Fleet[1]^.Emp:=Empire2;
      Universe^.Fleet[1]^.XY.x:=5;  Universe^.Fleet[1]^.XY.y:=5;
      Universe^.Fleet[1]^.Ships[fgt]:=parts[2];
      Universe^.Fleet[1]^.Ships[hkr]:=parts[3];
      Universe^.Fleet[1]^.Ships[pen]:=parts[4];
      Universe^.Fleet[1]^.Ships[trn]:=parts[5];
      SetOfActiveFleets:=[1];
      TargetID.ObjTyp:=Flt;  TargetID.Index:=1;
      end
   else
      begin
      NoOfPlanets:=1;
      Universe^.Planet[1].Cls:=ClsM;
      Universe^.Planet[1].Typ:=CapTyp;
      Universe^.Planet[1].Tech:=WrpTchLvl;
      Universe^.Planet[1].Emp:=Empire2;
      Universe^.Planet[1].XY.x:=5;  Universe^.Planet[1].XY.y:=5;
      Universe^.Planet[1].Defns[LAM]:=parts[6];
      Universe^.Planet[1].Defns[def]:=parts[7];
      Universe^.Planet[1].Defns[GDM]:=parts[8];
      Universe^.Planet[1].Defns[ion]:=parts[9];
      SetOfActivePlanets:=[1];
      SetOfPlanetsOf[Empire2]:=[1];
      TargetID.ObjTyp:=Pln;  TargetID.Index:=1;
      end;

   LAMAttack(Empire1,LAMToUse,TargetID,ShipsDest,DefnsDest);

   WriteLn('shipsdest_fgt=',ShipsDest[fgt],';shipsdest_hkr=',ShipsDest[hkr],
           ';shipsdest_pen=',ShipsDest[pen],';shipsdest_trn=',ShipsDest[trn],
           ';defnsdest_lam=',DefnsDest[LAM],';defnsdest_def=',DefnsDest[def],
           ';defnsdest_gdm=',DefnsDest[GDM],';defnsdest_ion=',DefnsDest[ion]);

   { PATCH-note: same double-dispose fix as RunNpeAttackCase's own cleanup -- real DestroyFleet may
     already have Disposed Fleet[1] during LAMAttack's own resolution. }
   if TargetIsFleet and (1 in SetOfActiveFleets) then
      Dispose(Universe^.Fleet[1]);
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
     InitializeSector first: CreatePlanet (Intrface's real home, called by CreateRndPlanet) writes
     through Sector[x]^[y].Obj, same requirement as RunStarbaseCase/RunConstructionCase. Doesn't set
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

procedure RunGroundTruthRngCase(const arg: String);
   { Not a UpdateWorld/GalaxySetup domain at all -- a permanent regression fixture for the C# test
     project's own GroundTruthRandom, the generator Rnd's real (non-ForcedRandomValue) branch draws
     from (see INT.PAS.patch's own doc comment). Calls GroundTruthNextU32 and scales it exactly the
     way GroundTruthRandom.Next(maxValue) does on the C# side ((draw*range) shr 32), not through Rnd's
     Min/Max wrapper -- Rnd's own Max<=Min degenerate-range clamp skips drawing entirely, which would
     desync the two sides' state for a Range=1 case if this domain went through Rnd instead. (This
     domain replaces a now-deleted one, RunRngCase, which played the same role for PascalRandom.cs, a
     from-scratch reverse-engineered port of fpc's actual Random/RandSeed algorithm -- retired along
     with PascalRandom.cs once Rnd itself no longer called fpc's real Random at all, so there was
     nothing left needing that reverse-engineered replica to be proven correct against.) }
   var
      parts: array[0..2] of LongInt;
      i: Integer;
      Values, Piece: AnsiString;
   begin
   ParseFields(arg,parts);

   GroundTruthSeed:=LongWord(parts[0]);

   Values:='';
   for i:=1 to parts[2] do
      begin
      if i>1 then
         Values:=Values+',';
      Str((QWord(GroundTruthNextU32)*QWord(parts[1])) SHR 32,Piece);
      Values:=Values+Piece;
      end;

   WriteLn('values=',Values);
   end;

procedure RunScenarioCase(const arg: String);
   { Calls the real NEWGAME.PAS LoadScenario end to end (promoted to this unit's INTERFACE, see
     NEWGAME.PAS's own PATCH note) instead of hand-reimplementing its header-parse/command-dispatch
     loop -- LoadScenario's own two interactive UI touchpoints (InputEmpireName's name/gender/
     password prompts, ScenarioIntroduction's player-count prompt and page-pause) are bypassed via
     NEWGAME.PAS's TestNumPlayers test-only override (see its own declaration comment for the full
     mechanism, including why RandSeed's file-driven reseed is also skipped in test mode).

     Player identity is deterministic ("test_player_N"/"test_pass_N", alternating gender), computed
     directly from each InputEmpireName call's own NewEmp parameter -- see that patch's own comment
     for why no queue/array-with-pointers is needed here.

     Emits the same aggregate checksum shape the old reimplementation-based domain did (see this
     procedure's own prior version in git history for the byte-for-byte field list this matches):
     Year, active Planet/Starbase counts and Card(SetOfActiveGates), active empire count, and sums
     of every RNG-or-parsing-sensitive numeric field across all active planets/starbases/empires
     plus a galaxy-wide painted-nebula-cell count and mined-cell count -- sourced from the real
     SetOfActivePlanets/SetOfActiveStarbases (LoadScenario's own real bookkeeping) instead of the
     old reimplementation's manually-tracked FirstWorld/FirstBase counters, which no longer exist
     now that the real procedure -- which already maintains these sets correctly -- is what runs. }
   var
      PathAndCounts: String;
      Path: LineStr;
      Seed,NumPlayers: LongInt;
      CommaPos1,CommaPos2: Integer;
      Abort: Boolean;

      i,x,y: Integer;
      Emp: Empire;
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
   { Path,Seed,NumPlayers -- Path may itself contain no commas (a plain relative path), so this is
     a manual split on the LAST two commas, not ParseFields (which assumes every field is a
     LongInt). }
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
   { Rnd no longer draws from fpc's real Random/RandSeed (see INT.PAS's own GroundTruthSeed patch) --
     seed the ground-truth generator instead so LoadScenario's Rnd-driven placement is deterministic
     from this case's own Seed field. RandSeed itself is left alone: NEWGAME.PAS's own file-driven
     reseed of it is unconditionally skipped in test mode (TestNumPlayers>=0, set just below) anyway. }
   GroundTruthSeed:=LongWord(Seed);
   ForcedRandomValue:=-1;
   TestNumPlayers:=NumPlayers;

   LoadScenario(Path,Abort);

   TestNumPlayers:=-1;

   { Aggregate checksum -- see this procedure's own doc comment for why. }
   PlanetCount:=0;  SumPlanetX:=0;  SumPlanetY:=0;  SumPop:=0;  SumEff:=0;  SumTri:=0;
   SumClass:=0;  SumTech:=0;  SumShips:=0;  SumCargo:=0;  SumDefns:=0;
   { PATCH-note: LastFirstWorld-1, not SetOfActivePlanets membership -- real Pascal doesn't
     maintain that set during scenario creation at all (only LOADSAVE.PAS's LoadGame writes it),
     see LoadScenario's own PATCH note for LastFirstWorld/LastFirstBase. }
   for i:=1 to LastFirstWorld-1 do
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
   for i:=1 to LastFirstBase-1 do
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
   for x:=1 to SizeOfGalaxy do
      for y:=1 to SizeOfGalaxy do
         begin
         Cell.x:=x;  Cell.y:=y;
         if GetNebula(Cell)<>NoNeb then
            Inc(NebulaCellCount);
         end;

   MinedCellCount:=0;
   for x:=1 to SizeOfGalaxy do
      for y:=1 to SizeOfGalaxy do
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


procedure RunFleetLogisticsCase(const arg: String);
   { FuelCapacity/FuelConsumption/FleetCargoSpace/BalanceFleet (MISC.PAS:168-222, INTRFACE.PAS:431-465
     as trimmed into this harness's own INTRFACE.PAS -- see that file's header comment), called
     directly against a hand-built ShipArray/CargoArray pair -- no Universe^ state needed, these are
     pure functions over their own parameters. balanced_* reports Cr after BalanceFleet runs, so a
     case with more cargo than the ship distribution can carry exercises the trim. }
   var
      parts: array[0..13] of LongInt;
      Sh: ShipArray;
      Cr: CargoArray;
      cap,cons: Real;
      space: Integer;
   begin
   ParseFields(arg,parts);
   FillChar(Sh,SizeOf(Sh),0);
   FillChar(Cr,SizeOf(Cr),0);
   Sh[fgt]:=parts[0];  Sh[hkr]:=parts[1];  Sh[jmp]:=parts[2];  Sh[jtn]:=parts[3];
   Sh[pen]:=parts[4];  Sh[ssp]:=parts[5];  Sh[trn]:=parts[6];
   Cr[men]:=parts[7];  Cr[nnj]:=parts[8];  Cr[amb]:=parts[9];  Cr[che]:=parts[10];
   Cr[met]:=parts[11]; Cr[sup]:=parts[12]; Cr[tri]:=parts[13];

   cap:=FuelCapacity(Sh);
   cons:=FuelConsumption(Sh,Cr);
   space:=FleetCargoSpace(Sh,Cr);
   BalanceFleet(Sh,Cr);

   WriteLn('fuelcap=',cap:0:6,';fuelcons=',cons:0:6,';cargospace=',space,
           ';balanced_men=',Cr[men],';balanced_nnj=',Cr[nnj],';balanced_amb=',Cr[amb],
           ';balanced_che=',Cr[che],';balanced_met=',Cr[met],';balanced_sup=',Cr[sup],
           ';balanced_tri=',Cr[tri]);
   end;

procedure RunFleetMoveCase(const arg: String);
   { GetNewPos (FLEET.PAS:437-450) and PassingThroughGate/PassingThroughFortress (relocated into
     this harness's own INTRFACE.PAS -- see that file's header comment), against one Empire1 fleet at
     (PosX,PosY). A gate or fortress at Pos are mutually exclusive in a real case (Sector.Obj holds
     at most one thing), matching source; this driver doesn't enforce that itself, since a bad case
     is a test-authoring error, not something Pascal needs to guard against here. }
   var
      parts: array[0..11] of LongInt;
      fltID,gateID: IDNumber;
      pos,dest,neb: XYCoord;
      newPos: XYCoord;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   InitializeSector(20);
   Universe^.EmpireData[Empire1].InUse:=True;
   Universe^.EmpireData[Empire2].InUse:=True;

   pos.x:=parts[0];   pos.y:=parts[1];
   dest.x:=parts[2];  dest.y:=parts[3];

   New(Universe^.Fleet[1]);
   FillChar(Universe^.Fleet[1]^,SizeOf(Universe^.Fleet[1]^),0);
   Universe^.Fleet[1]^.Emp:=Empire1;
   Universe^.Fleet[1]^.XY:=pos;
   SetOfActiveFleets:=[1];
   fltID.ObjTyp:=Flt;  fltID.Index:=1;

   if (parts[4]<>0) or (parts[5]<>0) then
      begin
      neb.x:=parts[4];  neb.y:=parts[5];
      PutNebula(neb,DenseNebula);
      end;

   if parts[6]<>0 then
      begin
      Universe^.Stargate[1].XY:=pos;
      Universe^.Stargate[1].Emp:=Empire(parts[7]);
      if parts[6]=1 then Universe^.Stargate[1].GTyp:=gte else Universe^.Stargate[1].GTyp:=lnk;
      SetOfActiveGates:=[1];
      gateID.ObjTyp:=Gate;  gateID.Index:=1;
      Sector[pos.x]^[pos.y].Obj:=gateID;
      end
   else
      SetOfActiveGates:=[];

   if parts[8]<>0 then
      begin
      Universe^.Stargate[2].XY:=dest;
      Universe^.Stargate[2].Emp:=Empire(parts[9]);
      if parts[8]=1 then Universe^.Stargate[2].GTyp:=gte else Universe^.Stargate[2].GTyp:=lnk;
      SetOfActiveGates:=SetOfActiveGates+[2];
      gateID.ObjTyp:=Gate;  gateID.Index:=2;
      Sector[dest.x]^[dest.y].Obj:=gateID;
      if parts[10]<>0 then
         Universe^.Stargate[2].KnownBy:=[Empire1];
      end;

   if parts[11]<>0 then
      begin
      Universe^.Starbase[1].XY:=pos;
      Universe^.Starbase[1].Emp:=Empire1;
      Universe^.Starbase[1].STyp:=frt;
      SetOfActiveStarbases:=[1];
      gateID.ObjTyp:=Base;  gateID.Index:=1;
      Sector[pos.x]^[pos.y].Obj:=gateID;
      end;

   newPos:=pos;
   GetNewPos(newPos,dest);

   WriteLn('newpos_x=',newPos.x,';newpos_y=',newPos.y,
           ';passgate=',Ord(PassingThroughGate(fltID,pos,dest)),
           ';passfortress=',Ord(PassingThroughFortress(pos)));

   Dispose(Universe^.Fleet[1]);
   Dispose(Universe);
   end;

procedure RunProbeScoutCase(const arg: String);
   { ProbeScout (INTRFACE.PAS:1289-1344), called directly (already exported, no patch needed) against
     a hand-assembled Universe^: one planet at the probe's destination (5,5) with configurable
     owner/legions/already-scouted, and one at the very next ring cell in Pascal's fixed offset order,
     (5,4) -- (dx,dy)=(0,-1) -- used purely as a "did the scan continue past the destination" signal;
     its own owner (Empire2) and legions (0) never vary. Covers ISqrt(Cargo[men]) and the
     Rnd(1,100)<ChanceToDestroy threshold plus its Exit-before-ScoutObject sequencing -- ring
     ordering/early-exit control flow itself is hardcoded-tested on the C# side
     (VisibilityHandlerProbeTests), since there's no separate Pascal formula to cross-check there.

     Requires InitializeSector plus direct Sector[x]^[y].Obj writes for both planets -- ProbeScout
     resolves them via GetObject, same requirement as the starbase/construction domains. }
   var
      parts: array[0..3] of LongInt;
      destID, ringID: IDNumber;
      dest: XYCoord;
      destOwner: Empire;
   begin
   ParseFields(arg,parts);

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   InitializeSector(20);
   NoOfPlanets:=2;

   destOwner:=Empire(parts[0]);

   Universe^.Planet[1].XY.x:=5;  Universe^.Planet[1].XY.y:=5;
   Universe^.Planet[1].Emp:=destOwner;
   Universe^.Planet[1].Cargo[men]:=parts[1];
   if parts[2]<>0 then
      begin
      Universe^.Planet[1].ScoutedBy:=[Empire1];
      Universe^.Planet[1].KnownBy:=[Empire1];
      end;

   destID.ObjTyp:=Pln;  destID.Index:=1;
   Sector[5]^[5].Obj:=destID;

   Universe^.Planet[2].XY.x:=5;  Universe^.Planet[2].XY.y:=4;
   Universe^.Planet[2].Emp:=Empire2;

   ringID.ObjTyp:=Pln;  ringID.Index:=2;
   Sector[5]^[4].Obj:=ringID;

   SetOfActivePlanets:=[1,2];
   SetOfPlanetsOf[destOwner]:=SetOfPlanetsOf[destOwner]+[1];
   SetOfPlanetsOf[Empire2]:=SetOfPlanetsOf[Empire2]+[2];

   ForcedRandomValue:=parts[3];

   dest.x:=5;  dest.y:=5;
   ProbeScout(Empire1,dest);

   WriteLn('destscouted=',Ord(Empire1 IN Universe^.Planet[1].ScoutedBy),
           ';ringscouted=',Ord(Empire1 IN Universe^.Planet[2].ScoutedBy));

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
      else if domain='defenses' then
         RunDefensesCase(ParamStr(i))
      else if domain='combat' then
         RunCombatCase(ParamStr(i))
      else if domain='npeattack' then
         RunNpeAttackCase(ParamStr(i))
      else if domain='lamattack' then
         RunLamAttackCase(ParamStr(i))
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
      else if domain='groundtruthrng' then
         RunGroundTruthRngCase(ParamStr(i))
      else if domain='scenario' then
         RunScenarioCase(ParamStr(i))
      else if domain='probescout' then
         RunProbeScoutCase(ParamStr(i))
      else if domain='fleetlogistics' then
         RunFleetLogisticsCase(ParamStr(i))
      else if domain='fleetmove' then
         RunFleetMoveCase(ParamStr(i))
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
