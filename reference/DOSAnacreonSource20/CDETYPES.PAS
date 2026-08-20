(* CDETYPES.PAS --------------------------------------------------------------

--------------------------------------------------------------------------- *)

UNIT CdeTypes;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES
   Types,
   Galaxy;

TYPE
   VariableTypes = ( BoolVRT, ScalarVRT, EmpVRT, IDVRT, XYVRT );
   VariableRecord = RECORD
      CASE VType: VariableTypes OF
         BoolVRT: (Bool: Boolean);
         ScalarVRT: (Scalar: Integer);
         EmpVRT: (Emp: Empire);
         IDVRT: (ID: IDNumber);
         XYVRT: (XY: XYCoord);
   END;
   RegisterArray = ARRAY [0..9] OF VariableRecord;

   ActionTypes = (
      NoACT,
      CaseCODE,
      ElseCODE,
      EndCODE,
      IfCODE,
      SwitchCODE,
      WhileCODE,

      AnyKeyACT,                 { prints message and waits for any key }
      AssignACT,                 { assign p1 := p2 }
      ClsACT,                    { clears display screen }
      CreateACT,                 { create artifact of type p1 at p2 }
      DebugDumpACT,              { displays all registers }
      DestructACT,               { destroy artifact p1 }
      DisplayACT,                { display text p1 on screen }
      GetArtifactCoordACT,       { returns the coords of artifact in p1 }
      GetObjectPowerACT,
      IsEqualACT,                { p3 := (p1 = p2) }
      IsGreaterACT,              { p3 := (p1 > p2) }
      IsLesserACT,               { p3 := (p1 < p2) }
      IsNotEqualACT,             { p3 := (p1 <> p2) }
      MenuAddFleetsACT,
      MenuDisplayACT,
      MenuInitializeACT,         { initializes a menu of type p1 }
      NullACT,                   { no action }
      WinGameACT );              { p1 player wins the game }

   ActionRecord = RECORD
      AType: ActionTypes;
      Immediate: VariableRecord;
      Parm: ARRAY [1..4] OF Byte;
   END;
   ActionArray = ARRAY [1..1000] OF ActionRecord;
   ActionArrayPtr = ^ActionArray;

CONST
   LastACT = WinGameACT;

IMPLEMENTATION

END.

