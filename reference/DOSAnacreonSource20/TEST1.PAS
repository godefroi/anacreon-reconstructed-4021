program Test1;

uses
	Types,
	DataCnst,
	Battle;

var
	Attacker, Defender, Casualties: AttackARRAY;

procedure PrintData (var Force: AttackARRAY);
	var
		i: ResourceTypes;

	begin
	for i:=FirstWarMachine to LastWarMachine do
		WriteLn (ThingNames[i],': ',Force[i]);
	end;  { PrintData }

procedure GetData (var Force: AttackARRAY);
	var
		i: ResourceTypes;

	begin
	for i:=FirstWarMachine to LastWarMachine do
		begin
		Write (ThingNames[i],': ');
		ReadLn (Force[i]);
		end;
	WriteLn;
	end;  { GetData }

begin
WriteLn ('Enter attacking force');
GetData (Attacker);
WriteLn ('Enter defending force');
GetData (Defender);

CalcAttackRound (Empire1, Empire1, Attacker, Defender, Casualties);

WriteLn ('Casualties');
PrintData (Casualties);
end.
