{ Standalone harness verifying the C# reconstruction of UPDATE.PAS's UpdateMilitary (AnnualTickHandler,
  economy phase Commit 2c) against the real Pascal formula. Shared tables/helpers live in common.pas.

  Build (FreePascal, tested with fpc 3.2.2):
    fpc military.pas

  Machine-parseable mode, one case per command-line argument, each a comma-separated tuple:
    Pop,Legions,TypOrd,RngFixedValue
  where TypOrd is WorldTypes' 0-based ordinal (AgrTyp=0, AmbTyp=1, ..., TriTyp=20 — see the
  WorldTypes declaration in common.pas for the full order). Prints one "key=value;..." line per
  case to stdout, consumed by PascalHarness in the C# test project. Example:
    ./military case 2340,0,0,0
  With no arguments, runs a small human-readable sanity dump instead (manual use). }
program Military;

{$mode fpc}

uses Common;

{ UpdateMilitaryScenario (UPDATE.PAS:606-617) now lives in common.pas, shared with revolution.pas —
  see its doc comment there. }

{ ---------------------------------------------------------------------------------------------- }

function ParseLongInt(const s: String): LongInt;
   var code: Integer;
   begin
   Val(s, ParseLongInt, code);
   if code<>0 then
      begin
      WriteLn(StdErr, 'military: bad integer "',s,'" (position ',code,')');
      Halt(1);
      end;
   end;

procedure RunCase(const arg: String);
   var
      parts: array[1..4] of LongInt;
      partIdx,i,startPos: Integer;
      tok: String;
      Typ: WorldTypes;
      MPop: LongInt;
   begin
   partIdx:=1;
   startPos:=1;
   for i:=1 to Length(arg)+1 do
      if (i>Length(arg)) or (arg[i]=',') then
         begin
         tok:=Copy(arg,startPos,i-startPos);
         if partIdx>4 then
            begin
            WriteLn(StdErr,'military: too many fields in "',arg,'"');
            Halt(1);
            end;
         parts[partIdx]:=ParseLongInt(tok);
         Inc(partIdx);
         startPos:=i+1;
         end;
   if partIdx<>5 then
      begin
      WriteLn(StdErr,'military: expected 4 comma-separated fields, got ',partIdx-1,' in "',arg,'"');
      Halt(1);
      end;

   Typ:=WorldTypes(parts[3]);
   RngFixedValue:=parts[4];
   MPop:=parts[2];

   UpdateMilitaryScenario(parts[1],Typ,MPop);

   WriteLn('legions=',MPop);
   end;

procedure RunCaseMode;
   var i: Integer;
   begin
   for i:=2 to ParamCount do
      RunCase(ParamStr(i));
   end;

procedure SanityDump;
   var MPop: LongInt;
   begin
   WriteLn('--- Sanity: Agricultural world, Pop=2340, Legions=0 ---');
   MPop:=0;
   UpdateMilitaryScenario(2340,AgrTyp,MPop);
   WriteLn('legions=',MPop);
   end;

begin
if (ParamCount>0) and (ParamStr(1)='case') then
   RunCaseMode
else
   SanityDump;
end.
