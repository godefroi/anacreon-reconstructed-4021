(* BATTLE.PAS ----------------------------------------------------------------

	Battle Unit

	This unit contains procedures to resolve simple attacks and battles
	between objects.

	No user interface in this module.
--------------------------------------------------------------------------- *)

unit Battle;

interface

uses
	Int,
	Types;

procedure CalcAttackRound (AttEmp, DefEmp: Empire;
                           var Attacker, Defender, Casualties: ResourceArray);
function CalcMilitaryPower (Emp: Empire; var Force: ResourceArray): longint;

implementation

procedure CalcAttackRound (AttEmp, DefEmp: Empire;
                           var Attacker, Defender, Casualties: ResourceArray);
{ ----------------------------------------------------------------------------
	Calculates the damage inflicted by Attacker on Defender and returns the
	result in Casualties. Attacker and Defender are not modified. This routine
	will not destroy cargo when transports are destroyed; you must call
	BalanceFleet.

	Note: Do not assume that this routine is deterministic. Identical
	values of Attacker and Defender may produce different results.
---------------------------------------------------------------------------- }
	var
		DefensePower, AttackPower: longint;
		Destroyed: longint;
		i: ResourceTypes;

	begin
	AttackPower := CalcMilitaryPower (AttEmp, Attacker);
	DefensePower := CalcMilitaryPower (DefEmp, Defender);

	FillChar(Casualties, SizeOf(Casualties), 0);
	if (AttackPower>0) and (DefensePower>0) then
		begin
		Destroyed := LesserInt (1000, Round ((250.0*AttackPower + 1000.0*Rnd(1,100)) / DefensePower));
		{ Destroyed = per cent of fleet destroyed in 0.1% points }

		for i:=FirstWarMachine to LastWarMachine do
			Casualties[i] := LesserInt (Defender[i],
			                    GreaterInt (0,
									     ((Destroyed+Rnd(-50,50)) * Defender[i]) DIV longint(1000) + Rnd(-5,5)));
		end;
	end;  { AttackRound }

function CalcMilitaryPower (Emp: Empire; var Force: ResourceArray): longint;
{ ----------------------------------------------------------------------------
	Calculate the military power of an attack force.
---------------------------------------------------------------------------- }
	const
		MilitaryPower: array [FirstWarMachine..LastWarMachine] of byte =
			{ LAM, def, GDM, ion, fgt, hkr, jmp, jtn, pen, str, trn }
			(  30,  75,  10,  25,   2,  10,   4,   1,  15, 100,   1 );

	var
		i: ResourceTypes;
		Total: longint;

	begin
	Total:=0;
	for i:=FirstWarMachine to LastWarMachine do
		Inc(Total, longint(Force[i]) * longint(MilitaryPower[i]));

	CalcMilitaryPower := Total;
	end;  { CalcMilitaryPower }

end.
