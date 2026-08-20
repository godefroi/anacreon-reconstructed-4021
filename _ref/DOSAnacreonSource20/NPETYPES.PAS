(* NPETYPES.PAS ----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT NPETypes;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES
   Types,
   Galaxy;

CONST
   MaxNoOfBlocks = MaxSizeOfGalaxy DIV 5;

TYPE
   NPEmpireTypes = ( NoNPE,
                     PirateNPE,           { Pirate empire }
                     Kingdom1NPE,         { Passive empire, but easy to provoke }
                     Kingdom2NPE,         { Aggressive empire, attacks others }
                     BerserkerNPE,	  { Berserker empire }
		     GuardianNPE,	  { Guardian empire }
                     TraderNPE );

   NPEDataRecord = RECORD
      Typ: NPEmpireTypes;
      Data: Pointer;
   END;
   NPEDataArray = ARRAY [Empire] OF NPEDataRecord;

   NPECharacterRecord = RECORD
      ImpGene: Index;                  { Controls Imperialist }
      DefGene: Index;                  { Controls defensive }
      OffGene: Index;                  { Controls offensive }
      FactorGene: Index;               { Controls change in attributes }
      RandomGene: Index;               { Controls unpredictability }

      Defensive: Index;                { 100 = very defensive, protects all worlds }
      Offensive: Index;                { 100 = very aggressive, attacks others often }
      Techno: Index;                   { 100 = likes technology, university worlds }
      Provoke: Index;                  { 100 = provoked very easily }
      Imperialist: Index;              { 100 = likes to expand }
      WorldPower: Index;               { 100 = takes few powerful world }
      Honorable: Index;                { 100 = will not attack friends }
      SphereX: Index;                  { 100 = expand from capital out }
      Clock: Word;                     { years since beginning }
      Offset: Byte;                    { Clock offset. }
   END;

   MissionTypes = ( NoMSN,
                    ReturnMSN,               { Abort at destination. }
                    HKMSN,
                    WaitForTrnMSN,           { Wait for enemy transports. }
                    AttackTrnMSN,            { Attack enemy fleets. }
                    AttackWrldMSN,           { Attack enemy world. }
		    BSRKAttackMSN, 	     { Berserker attack. }
		    BSRKReturnMSN,	     { Return to berserker base. }
                    StackMSN,                { Stack at base planet. }
                    GuardMSN,                { Wait at base planet. }
                    ConquerMSN,              { Conquer independent world }
                    JumpAttackMSN,           { Conquer enemy with jumpships }
                    RefuelMSN,               { refuel another fleet }
                    SlowAttackMSN,           { Conquer enemy with slow fleets }
                    RaidTrnMSN,              { Raid transports over worlds }
                    SupplyMSN,               { supply world }
                    SupplyTrnMSN             { send trn to material worlds }
                    );

	BaseMissionTypes = ( NoBMS,
		    DefendBMS,			{ defend a base planet }
		    AttackBMS,			{ attack target }
		    FindHomeBMS,		{ look for base to go to }
		    RefuelBMS,			{ go to nearest base to refuel }
		    WaitForAttackBMS,		{ waiting }
		    WanderAroundBMS		{ wander around the galaxy }
		    );

   FleetDataRecord = RECORD
      Mission: MissionTypes;
      TargetID: IDNumber;                    { final destination or target }
      HomeBaseID: IDNumber; 		     { fleet base }
      Midway: IDNumber;                      { coord to gather at or rendezvous }
      Waiting: Byte;
      BlockX,BlockY: Byte;
      Index: Byte;                           { Fleet index }
   END;
   FleetDataArray = ARRAY [1..NoOfFleetsPerEmpire] OF FleetDataRecord;

	BaseDataRecord = RECORD
		Mission: BaseMissionTypes;
		TargetID: IDNumber;
		Count: Word;
	END;
	BaseDataArray = ARRAY [1..MaxNoOfStarbases] OF BaseDataRecord;

   PolicyTypes = ( NoPLT,
                   NeutralPLT,               { Ignore. }
                   DefendPLT,                { Defend worlds against attack. }
                   HarassPLT,                { Attack fleets. }
                   PreemptPLT,               { Attack small worlds. }
                   ConflictPLT,              { Attack major bases. }
                   WarPLT );                 { Take capital. }

   StateDeptRecord = RECORD
      Policy: PolicyTypes;                   { Empire policy towards enemy. }
      AttackChance: Index;                   { Chance to attack this enemy. }
      TotalMilitary: LongInt;                { Strength of enemy military. }
      Worlds: Word;                          { No of worlds in the empire. }
      ThreatAssess: Index;                   { Threath assessment of enemy. }
      Aggressiveness: Index;                 { Attacks on empire }

      Balance: Integer;			     { positive means NPE has       }
		  			     { conquered some of its worlds }
					     { negative means that NPE has  }
				 	     { to take some worlds back.    }
   END;

   StateDeptArray = ARRAY [Empire] OF StateDeptRecord;

(* Pirate Types ------------------------------------------------------------- *)
   HuntingGroundArray = ARRAY [1..MaxNoOfBlocks,1..MaxNoOfBlocks] OF Byte;

   PirateDataPtr = ^PirateDataRecord;
   PirateDataRecord = RECORD
      FleetData: FleetDataArray;
      HuntingGround: HuntingGroundArray;
      Sheep: ARRAY [Empire] OF Index;
   END;

(* Kingdom1 Types ----------------------------------------------------------- *)

   Kingdom1DataPtr = ^Kingdom1DataRecord;
   Kingdom1DataRecord = RECORD
      FleetData: FleetDataArray;
      State: StateDeptArray;
      Persona: NPECharacterRecord;
   END;

(* Berserker Types ---------------------------------------------------------- *)

   BerserkerDataPtr = ^BerserkerDataRecord;
   BerserkerDataRecord = RECORD
      FleetData: FleetDataArray;
      BaseData: BaseDataArray;
      Spare: ARRAY [1..50] OF Word;
   END;

(* Guardian Types --------------------------------------------------------- *)

	GuardianDataPtr = ^GuardianDataRecord;
	GuardianDataRecord = RECORD
		FleetData: FleetDataArray;
		Spare: ARRAY [1..50] OF Word;
	END;

VAR
   NPEData: NPEDataArray;

IMPLEMENTATION

END.
