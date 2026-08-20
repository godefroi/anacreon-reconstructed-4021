{ Standalone harness verifying the C# reconstruction of UPDATE.PAS's UseUpAmbrosia (AnnualTickHandler,
  economy phase Commit 2b) against the real Pascal formula. Shared tables/helpers live in common.pas.
  UseUpAmbrosiaScenario below is transcribed directly from UPDATE.PAS:1163-1276, re-read from source
  for this file — not copied from the C# port, so a C# translation mistake can't hide behind
  agreement with this harness.

  Build (FreePascal, tested with fpc 3.2.2):
    fpc ambrosia.pas

  Machine-parseable mode, one case per command-line argument, each a comma-separated tuple:
    Pop,Eff,TechOrd,Addicted,Ambrosia,RevIndexStart,RngFixedValue
  where TechOrd is TechLevel's 0-based ordinal (PreTchLvl=0, PrimitLvl=1, ..., GteTchLvl=10 — see the
  TechLevel declaration in common.pas), Addicted is 0 or 1, and RngFixedValue reproduces a specific
  FixedRandom(n) case (see common.pas's Rnd doc comment). Prints one "key=value;..." line per case to
  stdout, consumed by PascalHarness in the C# test project.
    ./ambrosia case 1007,100,5,1,50,0,0

  The Indus array isn't exposed on this CLI: UseUpAmbrosia's industrial-sabotage random branch
  (UPDATE.PAS:1226-1234) needs it, but that branch isn't covered by any case run through this harness
  (see AnnualTickHandlerAmbrosiaTests.cs's doc comment for why) — a zero-filled array standing in for
  it is never read or written by any covered case.

  Special (a SetOfSpecialConditions in Pascal) is simplified to a single Addicted boolean, matching
  the C# port's IsAddictedToAmbrosia (the architecture research found the other SpecialConditions
  flags dead in the turn loop). AddNews calls are skipped — no news subsystem in the C# port either
  (same precedent as revolution.pas/production.pas) — but every state effect, including RevIndex, is
  kept faithfully. }
program Ambrosia;

{$mode fpc}

uses Common;

const
   DrugsPerBillion = 11.5;
   ChanceToAddict = 25;
   AddictDeathCoeff = 0.12;
   AddictEffCoeff = 0.9;
   AddictRevICoeff = 0.55;

{ UPDATE.PAS:1163-1276. }
procedure UseUpAmbrosiaScenario(var Pop,Eff: LongInt; var Tech: TechLevel; var Indus: IndusArray;
                                 var Ambrosia: LongInt; var Addicted: Boolean; var RevIndex: LongInt);
   var
      AmbNeeded,Lack: LongInt;
      Die: LongInt;
      EffChange,IndDest: LongInt;
      IndI: IndusTypes;
   begin
   AmbNeeded:=ThgLmt((Pop/100)*DrugsPerBillion);

   if Addicted then
      begin
      if AmbNeeded<=Ambrosia then
         begin
         Ambrosia:=Ambrosia-AmbNeeded;
         end
      else
         { ASSERT: not enough ambrosia }
         begin
         Lack:=AmbNeeded-Ambrosia;
         Ambrosia:=0;

         { PEOPLE DIE }
         Die:=ThgLmt(AddictDeathCoeff*Lack);
         if Die>(Pop div 7) then
            Die:=Pop div 7;
         Pop:=Pop-Die;

         { EFFICIENCY DECREASES }
         EffChange:=Trunc(AddictEffCoeff*Die);
         EffChange:=LesserInt(EffChange,Eff);
         Eff:=Eff-EffChange;

         { REV INDEX INCREASES }
         ChangeRevIndexV(RevIndex,Trunc(AddictRevICoeff*Die));

         { RANDOM EFFECTS }
         case Rnd(1,10) of
            1..4: begin end;
            5..7: begin
                  Die:=ThgLmt((Rnd(50,120)/100)*Die);
                  if Die>0 then
                     Pop:=Pop-Die;
                  end;
            8..9: begin
                  for IndI:=BioInd to TriInd do
                     begin
                     IndDest:=Trunc(Indus[IndI]*Rnd(0,20)/100);
                     Indus[IndI]:=Indus[IndI]-IndDest;
                     end;
                  end;
              10: begin
                  if Tech>PreTchLvl then
                     Tech:=Pred(Tech);
                  end;
         end;  { case }

         { UN-ADDICT }
         if Rnd(1,100)<=ChanceToAddict then
            Addicted:=False;
         end;
      end
   else
      { ASSERT: world not addicted. }
      begin
      if Ambrosia>0 then
         begin
         if AmbNeeded<=Ambrosia then
            begin
            if Rnd(1,100)<ChanceToAddict then
               Addicted:=True;
            end;

         AmbNeeded:=AmbNeeded div 2;
         if AmbNeeded<=Ambrosia then
            begin
            Ambrosia:=Ambrosia-AmbNeeded;
            end
         else
            begin
            Ambrosia:=0;
            end;
         end;
      end;  { if }
   end;  { UseUpAmbrosiaScenario }

{ ---------------------------------------------------------------------------------------------- }

function ParseLongInt(const s: String): LongInt;
   var code: Integer;
   begin
   Val(s, ParseLongInt, code);
   if code<>0 then
      begin
      WriteLn(StdErr, 'ambrosia: bad integer "',s,'" (position ',code,')');
      Halt(1);
      end;
   end;

procedure RunCase(const arg: String);
   var
      parts: array[1..7] of LongInt;
      partIdx,i,startPos: Integer;
      tok: String;
      Pop,Eff,Ambrosia,RevIndex: LongInt;
      Tech: TechLevel;
      Addicted: Boolean;
      Indus: IndusArray;
      IndI: IndusTypes;
   begin
   partIdx:=1;
   startPos:=1;
   for i:=1 to Length(arg)+1 do
      if (i>Length(arg)) or (arg[i]=',') then
         begin
         tok:=Copy(arg,startPos,i-startPos);
         if partIdx>7 then
            begin
            WriteLn(StdErr,'ambrosia: too many fields in "',arg,'"');
            Halt(1);
            end;
         parts[partIdx]:=ParseLongInt(tok);
         Inc(partIdx);
         startPos:=i+1;
         end;
   if partIdx<>8 then
      begin
      WriteLn(StdErr,'ambrosia: expected 7 comma-separated fields, got ',partIdx-1,' in "',arg,'"');
      Halt(1);
      end;

   Pop:=parts[1];
   Eff:=parts[2];
   Tech:=TechLevel(parts[3]);
   Addicted:=parts[4]<>0;
   Ambrosia:=parts[5];
   RevIndex:=parts[6];
   RngFixedValue:=parts[7];

   for IndI:=BioInd to TriInd do
      Indus[IndI]:=0;

   UseUpAmbrosiaScenario(Pop,Eff,Tech,Indus,Ambrosia,Addicted,RevIndex);

   WriteLn('population=',Pop,';efficiency=',Eff,';techlevel=',Ord(Tech),
           ';ambrosia=',Ambrosia,';addicted=',Addicted,';revindex=',RevIndex);
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
   begin
   WriteLn(StdErr,'ambrosia: usage: ambrosia case Pop,Eff,TechOrd,Addicted,Ambrosia,RevIndexStart,RngFixedValue [...]');
   Halt(1);
   end;
end.
