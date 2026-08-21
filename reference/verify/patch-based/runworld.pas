(* runworld.pas -----------------------------------------------------------
   Driver: assembles a minimal but real Universe^ (via the actual
   DataStrc/Galaxy globals, not a simplified stand-in), then calls the real,
   unmodified (only relocated/lifted, never behavior-changed) UpdateWorld
   from the patched UPDATE.PAS unit. Hardcoded to reproduce the
   TechLevelCases.OwnedWorldBehindCapitalAdvances scenario from the existing
   C# golden-file suite: Tech=Warp, owned, capital ahead at Jump,
   RngFixedValue=0 -> expected techlevel=6 (JmpTchLvl).

   Not itself a patch target -- this is new code, checked in directly and
   copied into pascal/ by build.ps1 alongside the patched source.
--------------------------------------------------------------------------- *)

PROGRAM RunWorld;

USES Types, DataCnst, DataStrc, Galaxy, Int, Misc, PrimIntr, Environ, News, Update;

VAR
   ID, CapID: IDNumber;

BEGIN
{ PATCH note: never write through GlobalSets (DATASTRC.PAS:235's
  "ABSOLUTE SetOfActiveFleets" overlay) -- it assumes TP's declaration-order
  memory layout for the standalone vars in TYPES.PAS:180-188, which fpc does
  not guarantee; writing through it here corrupted the Universe pointer
  itself (runtime error 216). Write directly to the real standalone vars. }
New(Universe);
FillChar(Universe^,SizeOf(Universe^),0);

NoOfPlanets:=2;

{ World under test: Planet[1], owned by Empire1, behind its capital's tech }
Universe^.Planet[1].Emp:=Empire1;
Universe^.Planet[1].Cls:=AmbCls;
Universe^.Planet[1].Typ:=AgrTyp;
Universe^.Planet[1].Tech:=WrpTchLvl;
Universe^.Planet[1].Eff:=100;
Universe^.Planet[1].Pop:=10;

{ Capital: Planet[2], same empire, ahead in tech }
Universe^.Planet[2].Emp:=Empire1;
Universe^.Planet[2].Cls:=AmbCls;
Universe^.Planet[2].Typ:=CapTyp;
Universe^.Planet[2].Tech:=JmpTchLvl;
Universe^.Planet[2].Eff:=100;
Universe^.Planet[2].Pop:=10;

SetOfActivePlanets:=[1,2];
SetOfPlanetsOf[Empire1]:=[1,2];

Universe^.EmpireData[Empire1].InUse:=True;
Universe^.EmpireData[Empire1].IsAPlayer:=False;
CapID.ObjTyp:=Pln;  CapID.Index:=2;
Universe^.EmpireData[Empire1].Capital:=CapID;

ForcedRandomValue:=0;

ID.ObjTyp:=Pln;  ID.Index:=1;
UpdateWorld(ID);

WriteLn('techlevel=',Ord(Universe^.Planet[1].Tech));
WriteLn('population=',Universe^.Planet[1].Pop);
WriteLn('legions=',Universe^.Planet[1].Cargo[men]);
WriteLn('revindex=',Universe^.Planet[1].RevIndex);
END.
