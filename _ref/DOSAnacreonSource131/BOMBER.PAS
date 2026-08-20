(* BOMBER.PAS ----------------------------------------------------------------

	Strategic Bombing Unit

	This unit contains procedures to resolve strategic bombing attacks.

	No user interface in this module.
--------------------------------------------------------------------------- *)

unit Bomber;

interface

uses
	Types,
	Battle;

type
	BombingTypes = ( NoBMB, MilitaryBMB, IndustryBMB, PopulationBMB, HolocaustBMB);
	BombingResultTypes = ( NoBMR, LimitedBMR, WinterBMR, DesertBMR, VolcanicBMR,
	                       UndergroundBMR, WastelandBMR );

const
	MinForPopulationBombing = 10000;
	MinForHolocaustBombing = 50000;

procedure Bombing1stPhase (FltID: IDNumber;
                           TargetID: IDNumber;
									BombingType: BombingTypes;
									var Casualties: ResourceArray);
procedure Bombing2ndPhase (FltID: IDNumber;
                           TargetID: IDNumber;
									BombingType: BombingTypes;
									var Result: BombingResultTypes;
									var FltCasualties: ResourceArray;
									var TrgCasualties: ResourceArray;
									var IndCasualties: IndusArray;
									var PopCasualties: Population);

implementation

uses
	Resource,
	Primintr,
	Fleet;

procedure Bombing1stPhase (FltID: IDNumber;
                           TargetID: IDNumber;
									BombingType: BombingTypes;
									var Casualties: ResourceArray);
{ ----------------------------------------------------------------------------
	FltID enters system in hyperbolic orbit, passes through defenses taking
	damage. The routine will inflict damage on the fleet and send news
	to the defending empire.
---------------------------------------------------------------------------- }
	var
		BmbEmp, DefEmp: Empire;
		BmbResources, DefResources: ResourceArray;
		Ships: ShipArray;
		Defns: DefnsArray;
		NoShipsLeft: boolean;

	begin
	GetResources (TargetID, DefResources);
	GetResources (FltID, BmbResources);
	BmbEmp := GetStatus (FltID);
	DefEmp := GetStatus (TargetID);

	CalcAttackRound (DefEmp, BmbEmp, DefResources, BmbResources, Casualties);
	SubtractCasualties (BmbResources, Casualties, True, NoShipsLeft);
	if NoShipsLeft then
		begin
		DestroyFleet (FltID);
		end;
	end;  { Bombing1stPhase }

procedure Bombing2ndPhase (FltID: IDNumber;
                           TargetID: IDNumber;
									BombingType: BombingTypes;
									var Result: BombingResultTypes;
									var FltCasualties: ResourceArray;
									var TrgCasualties: ResourceArray;
									var IndCasualties: IndusArray;
									var PopCasualties: Population);
{ ----------------------------------------------------------------------------
	FltID bombs the surface of the planet using the given tactic. Fleet and
	world take damage. News sent to defender. If attempting to holocaust,
	Result contains extra information about result of atack.
---------------------------------------------------------------------------- }
	begin
	end;  { Bombing2ndPhase }

end.
