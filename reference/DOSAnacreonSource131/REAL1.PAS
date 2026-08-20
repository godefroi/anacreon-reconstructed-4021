(* REAL1.LIB ------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT Real1;

{$V-,R-}

INTERFACE

FUNCTION Expnt(Base: Real; Exponent: Real): Real;

IMPLEMENTATION

FUNCTION Expnt{Base: Real; Exponent: Real): Real};
{ Expnt:
   This function returns the result of 'Base' raised to the 'Exponent' power. }

   BEGIN
   Expnt:=Exp( Exponent* Ln(Base) );
   END;  { Expnt }

END.

