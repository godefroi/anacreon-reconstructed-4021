(* runworld.pas -----------------------------------------------------------
   Driver: assembles a minimal but real Universe^ (via the actual
   DataStrc/Galaxy globals, not a simplified stand-in), then calls the real,
   unmodified (only relocated/lifted, never behavior-changed) UpdateWorld
   from the patched UPDATE.PAS unit -- the full real per-planet tick
   (production, efficiency, tech level, population, food/ambrosia, military,
   defenses, revolution), not an isolated transcription of one procedure.

   Machine-parseable mode, one case per command-line argument, each a
   comma-separated tuple -- currently TechLevelCases' own 4-field shape,
   since this driver's first use is generating techlevel.golden at higher
   fidelity than a per-procedure transcription can (see
   reference/verify/patch-based/README.md):
     TechOrd,IsIndependent,CapitalTechOrd,RngFixedValue
   The rest of the fixture is fixed across every case -- Population=10,
   Efficiency=100, Class=ClsM, Type=AgrTyp, Cargo[sup]=9999 -- matching
   AnnualTickHandlerTechLevelTests.MakePlanet exactly, so the C# and Pascal
   sides run the identical planet through their own full per-tick pipeline.
   Prints one "techlevel=<ordinal>" line per case to stdout:
     ./runworld case 5,0,6,0
   With no arguments, runs the same single case as a human-readable sanity
   check (manual use).

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

procedure RunCase(const arg: String);
   var
      parts: array[1..4] of LongInt;
      partIdx,i,startPos: Integer;
      tok: String;
      ID, CapID: IDNumber;
   begin
   partIdx:=1;
   startPos:=1;
   for i:=1 to Length(arg)+1 do
      if (i>Length(arg)) or (arg[i]=',') then
         begin
         tok:=Copy(arg,startPos,i-startPos);
         if partIdx>4 then
            begin
            WriteLn(StdErr,'runworld: too many fields in "',arg,'"');
            Halt(1);
            end;
         parts[partIdx]:=ParseLongInt(tok);
         Inc(partIdx);
         startPos:=i+1;
         end;
   if partIdx<>5 then
      begin
      WriteLn(StdErr,'runworld: expected 4 comma-separated fields, got ',partIdx-1,' in "',arg,'"');
      Halt(1);
      end;

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
   Universe^.Planet[1].Tech:=TechLevel(parts[1]);
   Universe^.Planet[1].Eff:=100;
   Universe^.Planet[1].Pop:=10;
   Universe^.Planet[1].Cargo[sup]:=9999;

   if parts[2]<>0 then
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
      Universe^.Planet[2].Tech:=TechLevel(parts[3]);
      Universe^.Planet[2].Eff:=100;
      Universe^.Planet[2].Pop:=10;

      Universe^.EmpireData[Empire1].InUse:=True;
      Universe^.EmpireData[Empire1].IsAPlayer:=False;
      CapID.ObjTyp:=Pln;  CapID.Index:=2;
      Universe^.EmpireData[Empire1].Capital:=CapID;
      end;

   ForcedRandomValue:=parts[4];

   ID.ObjTyp:=Pln;  ID.Index:=1;
   UpdateWorld(ID);

   WriteLn('techlevel=',Ord(Universe^.Planet[1].Tech));

   Dispose(Universe);
   end;

procedure RunCaseMode;
   var i: Integer;
   begin
   for i:=2 to ParamCount do
      RunCase(ParamStr(i));
   end;

begin
if (ParamCount>0) and (ParamStr(1)='case') then
   RunCaseMode
else
   RunCase('5,0,6,0');
end.
