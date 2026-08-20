{ Standalone harness verifying the C# reconstruction of UPDATE.PAS's UpdateRevolution/Rebellion
  pair (AnnualTickHandler, economy phase Commit 1) against the real Pascal formulas. Shared
  tables/helpers live in common.pas.

  Build (FreePascal, tested with fpc 3.2.2):
    fpc revolution.pas

  Machine-parseable mode, one case per command-line argument, each a comma-separated tuple:
    Pop,Eff,RevIndexStart,EmpTotalRevIndex,EmpRevFactor,Legions,Ninja,TypOrd
  where TypOrd is WorldTypes' 0-based ordinal (AgrTyp=0, AmbTyp=1, ..., TriTyp=20 — see the
  WorldTypes declaration in common.pas for the full order). Prints one "key=value;..." line per
  case to stdout, consumed by PascalHarness in the C# test project. Example:
    ./revolution case 2340,80,95,0,0,0,0,0
  With no arguments, runs a small human-readable sanity dump instead (manual use).

  The owning empire is always assumed non-independent (Emp<>Indep), matching the C# port's
  UpdateRevolution/Rebellion pair, which is only ever reached for owned planets. }
program Revolution;

{$mode fpc}

uses Common;

{ UPDATE.PAS:619-679 — this fires only from inside UpdateRevolutionScenario below; not called
  standalone anywhere else, matching how AnnualTickHandler.Rebellion is private and only reachable
  through UpdateRevolution. }
procedure RebellionScenario(Pop,Eff,Military,CargoMenStart,CargoNnjStart: LongInt;
                            var RevIndex: LongInt;
                            var Rebelled: Boolean;
                            var CargoMenFinal,CargoNnjFinal,PopFinal,EffFinal,TotalRevDelta: LongInt);
   var
      MenLost,Lost: LongInt;
      Rebels: LongInt;
      ChanceToEndRebel: Real;
      CargoMen,CargoNnj: LongInt;
   begin
   CargoMen:=CargoMenStart;
   CargoNnj:=CargoNnjStart;

   Rebels:=GreaterInt(1,ThgLmt(Sqrt(Pop)*65));
   MenLost:=Rebels div 5;
   ChanceToEndRebel:=(Military/Sqrt(Rebels))*1.414213;

   Lost:=LesserInt(CargoMen,MenLost);
   Dec(CargoMen,Lost);
   Dec(MenLost,Lost);
   Lost:=LesserInt(CargoNnj,MenLost div 5);
   Dec(CargoNnj,Lost);

   if Rnd(1,100)<ChanceToEndRebel then
      begin
      Rebelled:=False;
      ChangeRevIndexV(RevIndex,Rnd(-15,5));
      TotalRevDelta:=TotalRevDelta-Rnd(1,5);
      end
   else
      begin
      Rebelled:=True;
      ChangeRevIndexV(RevIndex,-Rnd(40,50));
      CargoMen:=ThgLmt(Rebels);
      TotalRevDelta:=TotalRevDelta+Rnd(5,10);
      end;

   PopFinal:=ThgLmt(Pop-Military/1000);
   EffFinal:=GreaterInt(0,Eff-Rnd(5,15));
   CargoMenFinal:=CargoMen;
   CargoNnjFinal:=CargoNnj;
   end;

{ UPDATE.PAS:681-755. }
procedure UpdateRevolutionScenario(Pop,Eff,RevIndexStart,EmpTotalRevIndex,EmpRevFactor,
                                   CargoMenStart,CargoNnjStart: LongInt;
                                   Typ: WorldTypes;
                                   var Rebelled: Boolean;
                                   var CargoMenFinal,CargoNnjFinal,PopFinal,EffFinal,
                                       RevIndexFinal,TotalRevDelta: LongInt);
   var
      EmpRevAdj,Factor: LongInt;
      RevIndex: LongInt;
      Military,OptimumMil: LongInt;
      TotalRevIdx: LongInt;
   begin
   RevIndex:=RevIndexStart;
   TotalRevDelta:=0;

   TotalRevIdx:=EmpTotalRevIndex+EmpRevFactor;
   EmpRevAdj:=RndVar(TotalRevIdx,50);

   if Typ=CapTyp then
      ChangeRevIndexV(RevIndex,-Rnd(20,30))
   else
      ChangeRevIndexV(RevIndex,EmpRevAdj+Rnd(-5,2));

   OptimumMil:=ThgLmt(RndVar(PascalRound((Pop/150)*OptMilitary[Typ]),10));
   Military:=ThgLmt(CargoMenStart+5.0*CargoNnjStart);

   if Military>OptimumMil then
      begin
      if RevIndex>30 then
         begin
         Factor:=Rnd(1,(Military-OptimumMil) div 100);
         ChangeRevIndexV(RevIndex,-Factor);
         end
      else if (Typ<>CapTyp) and (Typ<>BseTyp) then
         begin
         if Rnd(1,5)=1 then
            ChangeRevIndexV(RevIndex,Rnd(5,15));
         end;
      end;

   if (RevIndex>75) and (Rnd(1,100)<RevIndex) and (Typ<>CapTyp) then
      begin
      RebellionScenario(Pop,Eff,Military,CargoMenStart,CargoNnjStart,RevIndex,
                        Rebelled,CargoMenFinal,CargoNnjFinal,PopFinal,EffFinal,TotalRevDelta);
      RevIndexFinal:=RevIndex;
      end
   else
      begin
      Rebelled:=False;
      CargoMenFinal:=CargoMenStart;
      CargoNnjFinal:=CargoNnjStart;
      PopFinal:=Pop;
      EffFinal:=Eff;
      RevIndexFinal:=RevIndex;
      end;
   end;

{ ---------------------------------------------------------------------------------------------- }

function ParseLongInt(const s: String): LongInt;
   var code: Integer;
   begin
   Val(s, ParseLongInt, code);
   if code<>0 then
      begin
      WriteLn(StdErr, 'revolution: bad integer "',s,'" (position ',code,')');
      Halt(1);
      end;
   end;

procedure RunCase(const arg: String);
   var
      parts: array[1..8] of LongInt;
      partIdx,i,startPos: Integer;
      tok: String;
      Typ: WorldTypes;
      Rebelled: Boolean;
      CargoMenFinal,CargoNnjFinal,PopFinal,EffFinal,RevIndexFinal,TotalRevDelta: LongInt;
   begin
   partIdx:=1;
   startPos:=1;
   for i:=1 to Length(arg)+1 do
      if (i>Length(arg)) or (arg[i]=',') then
         begin
         tok:=Copy(arg,startPos,i-startPos);
         if partIdx>8 then
            begin
            WriteLn(StdErr,'revolution: too many fields in "',arg,'"');
            Halt(1);
            end;
         parts[partIdx]:=ParseLongInt(tok);
         Inc(partIdx);
         startPos:=i+1;
         end;
   if partIdx<>9 then
      begin
      WriteLn(StdErr,'revolution: expected 8 comma-separated fields, got ',partIdx-1,' in "',arg,'"');
      Halt(1);
      end;

   Typ:=WorldTypes(parts[8]);

   UpdateRevolutionScenario(parts[1],parts[2],parts[3],parts[4],parts[5],parts[6],parts[7],Typ,
                            Rebelled,CargoMenFinal,CargoNnjFinal,PopFinal,EffFinal,
                            RevIndexFinal,TotalRevDelta);

   WriteLn('rebelled=',Rebelled,';legions=',CargoMenFinal,';ninja=',CargoNnjFinal,
           ';population=',PopFinal,';efficiency=',EffFinal,';revindex=',RevIndexFinal,
           ';total_rev_delta=',TotalRevDelta);
   end;

procedure RunCaseMode;
   var i: Integer;
   begin
   for i:=2 to ParamCount do
      RunCase(ParamStr(i));
   end;

procedure SanityDump;
   var
      Rebelled: Boolean;
      CargoMenFinal,CargoNnjFinal,PopFinal,EffFinal,RevIndexFinal,TotalRevDelta: LongInt;
   begin
   WriteLn('--- Sanity: matches the hand-traced Commit-1 test WorldRebelsWhenRevolutionIndexExceedsThreshold ---');
   UpdateRevolutionScenario(2350,80,95,0,0,0,0,AgrTyp,
                            Rebelled,CargoMenFinal,CargoNnjFinal,PopFinal,EffFinal,
                            RevIndexFinal,TotalRevDelta);
   WriteLn('rebelled=',Rebelled,' legions=',CargoMenFinal,' ninja=',CargoNnjFinal,
           ' population=',PopFinal,' efficiency=',EffFinal,' revindex=',RevIndexFinal,
           ' total_rev_delta=',TotalRevDelta);
   end;

begin
if (ParamCount>0) and (ParamStr(1)='case') then
   RunCaseMode
else
   SanityDump;
end.
