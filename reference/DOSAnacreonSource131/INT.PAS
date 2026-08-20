(* INT.PAS --------------------------------------------------------------------

---------------------------------------------------------------------------- *)

UNIT Int;

INTERFACE

FUNCTION GreaterInt(a,b: LongInt): LongInt;
FUNCTION IntLmt(X: Real): Integer;
FUNCTION ISqrt(X: Integer): Integer;
FUNCTION LesserInt(a,b: LongInt): LongInt;
FUNCTION Rnd(Min,Max: Integer): Integer;
FUNCTION RndVar(Value,Variation: Integer): Integer;
FUNCTION Sgn(X: LongInt): LongInt;
PROCEDURE SwapInt(VAR a,b: Integer);

IMPLEMENTATION

PROCEDURE SwapInt{VAR a,b: Integer};
{ SwapInt:
   This procedure exchanges the values of a and b. }

   VAR
      x: Integer;

   BEGIN
   x:=b;
   b:=a;
   a:=x;
   END;  { SwapInt }

function Sgn{X: LongInt: LongInt};
{ Sgn:
   Returns -1 if X is negative,
            0 if X is zero, and
           +1 if X is positive. }

   begin
   if X=0 then
      Sgn:=0
   else if X<0 then
      Sgn:=-1
   else if X>0 then
      Sgn:=1;
   end;  { Sgn }

function IntLmt{X: Real): Integer};
{ IntLmt:
   Given a real number, this function truncates it to an integer in the
   range -MaxInt..MaxInt.   If the real number is out of range it will return
   either MaxInt or -MaxInt. }

   begin
   if X>MaxInt then
      IntLmt:=MaxInt
   else if X<-MaxInt then
      IntLmt:=-MaxInt
   else
      IntLmt:=Trunc(X);
   end;  { IntLmt }

FUNCTION GreaterInt{a,b: LongInt): LongInt};
{ GreatestInt:
   Given a and b, this function returns whichever number is greater. }

   begin
   if a>b then
      GreaterInt:=a
   else
      GreaterInt:=b;
   end;  { GreaterInt }

function LesserInt{a,b: LongInt): LongInt};
{ LeastInt:
   Given a and b, this function returns whichever number is least. }

   begin
   if a<b then
      LesserInt:=a
   else
      LesserInt:=b;
   end;  { LesserInt }

FUNCTION ISqrt{X: Integer): Integer};
{ ISqrt:
   Returns the square root of X rounded to the nearest integer. }

   VAR
      OddSeq,Square,Root: Integer;

   BEGIN
   OddSeq:=-1;
   Square:=0;

   REPEAT
      OddSeq:=OddSeq + 2;
      Square:=Square + OddSeq
   UNTIL X < Square;

   Root:= Succ(OddSeq shr 1);
   IF X <= Square - Root THEN 
      Root:=Pred(Root);

   ISqrt:=Root
   END; { ISqrt }

FUNCTION Rnd{Min,Max: Integer): Integer};
{ Rnd:
   Returns a random number between Max and Min inclusive.  If Min>Max then Min 
   is returned. }

   BEGIN
   IF Max<=Min THEN
      Rnd:=Min
   ELSE
      Rnd:=Random((Max-Min)+1)+Min;
   END;  { Rnd }

FUNCTION RndVar{Value,Variation: Integer): Integer};
{ RndVar:
   Given 'Value', RndVar returns an integer that is +/- 'Variation'%
   of 'Value.' }

   VAR
      temp1: Integer;

   BEGIN
   temp1:=Trunc(Value*(Variation/100));
   RndVar:=Rnd(Value-temp1,Value+temp1);
   END;  { RndVar }

END.
