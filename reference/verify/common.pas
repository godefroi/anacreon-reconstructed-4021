{ Shared types, constant tables, and helper functions for the verification harnesses in this
  directory. Constants and formulas are transcribed directly from TYPES.PAS / DATACNST.PAS /
  INT.PAS / MISC.PAS in ../DOSAnacreonSource131 — not USES'd directly, since DataCnst's own unit
  dependency chain (DataCnst -> DataStrc -> Galaxy -> (impl) Dos2 -> WND) drags in real-mode
  Mem[segment:offset] video writes that can't compile on a modern target, TP-compat mode or not
  (verified empirically before choosing hand-transcription over that route).

  Rnd(Min,Max) returns Min+RngFixedValue (RngFixedValue defaults to 0) — the deterministic stand-in
  matching the C# test double FixedRandom(n), which makes every Random.Next(maxValue) call return n
  regardless of maxValue. Set RngFixedValue before a call to reproduce a specific FixedRandom(n) case;
  it does not replicate FixedRandom's Mx<=Mn short-circuit (real Rnd/FixedRandom both return exactly
  Mn for a degenerate range, ignoring n) since no call in these harnesses hits a degenerate range with
  RngFixedValue<>0 — if one ever does, this needs the same guard C#'s Rnd has.

  FreePascal's built-in Round() is IEEE round-half-to-even (confirmed empirically: Round(2.5)=2,
  Round(3.5)=4 — even under {$MODE TP}, which doesn't change this). Real Turbo Pascal's Round()
  rounds half away from zero. Every Round() call anywhere in this directory's harnesses MUST go
  through PascalRound below instead of the built-in, or a value landing on an exact .5 boundary
  silently diverges from Turbo Pascal. }
unit Common;

{$mode fpc}

interface

type
   TechLevel = (PreTchLvl,PrimitLvl,PreAtmLvl,AtomicLvl,PreWrpLvl,WrpTchLvl,
                JmpTchLvl,BioTchLvl,StrTchLvl,PreGteLvl,GteTchLvl);
   WorldClass = (AmbCls,ArdCls,ArtCls,BarCls,ClsJ,ClsK,ClsL,ClsM,DrtCls,EthCls,
                 FstCls,GsGCls,HLfCls,IceCls,JngCls,OcnCls,ParCls,PsnCls,RnsCls,
                 UndCls,VlcCls);
   WorldTypes = (AgrTyp,AmbTyp,BseTyp,BseSTyp,CapTyp,CheTyp,IndTyp,JmpTyp,
                 JmpSTyp,MinTyp,NnjTyp,OutTyp,RawTyp,RawSTyp,StrTyp,StrSTyp,
                 TrnTyp,TrnSTyp,RsrTyp,TerTyp,TriTyp);
   IndusTypes = (BioInd,CheInd,MinInd,SYGInd,SYJInd,SYSInd,SYTInd,SupInd,TriInd);
   TechnologyTypes = (NoRes,LAM,defn,GDM,ion,fgt,hkr,jmp,jtn,pen,ssp,trn,
                       men,nnj,amb,che,met,sup,tri,SRM,cmm,frt,cmp,outp,gte,lnk,dis);

   IndusArray = array[IndusTypes] of LongInt;
   IndusRArray = array[IndusTypes] of Real;
   CargoTypes = men..tri;
   CargoArray = array[CargoTypes] of LongInt;
   ShipTypes = fgt..trn;
   ShipArray = array[ShipTypes] of LongInt;
   ResourceTypes = NoRes..tri;
   TechnologySet = set of TechnologyTypes;

const
   MaxResources = 9999;
   SuppliesPerBillion = 25;
   K1 = 1.76; K2 = 10; K3 = 0.75;
   K4 = 0.0; K6 = 11000.0;
   SafetyAdj = 1.05;
   AmbrosiaAdj = 1.45;

   TechAdj: array[TechLevel] of Integer =
      (  25, 40, 49, 57, 66, 80, 85, 90, 94, 97,100 );
   TechAdj2: array[TechLevel] of Integer =
      (  12, 24, 36, 47, 58, 67, 76, 84, 90, 95,100 );

   NewIndRawN: array[IndusTypes] of Integer =
      (  100, 500, 100,1900,1200,1500,1000,   0, 300 );

   ISSP: array[0..10] of Real =
      (  0.01, 0.10, 0.25, 0.50, 0.75, 1.00, 1.50, 2.00, 3.00, 4.00, 5.00 );

   Gamma: array[IndusTypes,IndusTypes] of Real =
         {         Bio   Che   Min      SYG      SYJ      SYS      SYT   Sup   Tri }
   (  (        0,    0,    0,       0,       0,       0,       0,    0,    0 ),
      (  1.12857,    0,    0, 0.19143, 0.50000, 0.36714, 0.25286,    0,    0 ),
      (  0.10000,    0,    0, 0.30514, 0.27286, 0.53714, 0.83571,    0,    0 ),
      (        0,    0,    0,       0,       0,       0,       0,    0,    0 ),
      (        0,    0,    0,       0,       0,       0,       0,    0,    0 ),
      (        0,    0,    0,       0,       0,       0,       0,    0,    0 ),
      (        0,    0,    0,       0,       0,       0,       0,    0,    0 ),
      (        0,    0,    0,       0,       0,       0,       0,    0,    0 ),
      (  0.10000,    0,    0, 0.07627, 0.20400, 0.16667, 0.08000,    0,    0 )  );

   ClassIndAdj: array[WorldClass,IndusTypes] of Integer =
                    { Bio Che Min SYG SYJ SYS SYT Sup Tri }
      ( ( 100,100, 75,100,100,100,100,100, 75 ),
        ( 100, 80,100,100,100,100,100, 85,100 ),
        ( 100, 40, 40,250,200,300,300, 40, 40 ),
        ( 100, 60,175,100,100,100,100, 40,175 ),
        ( 100,120,120,100,100,100,100,100, 90 ),
        ( 100, 90,120,100,100,100,100,100,100 ),
        ( 100,100,100,100,100,100,100, 90,120 ),
        ( 100,100,100,100,100,100,100,120, 90 ),
        ( 100, 60, 80,100,100,100,100, 60,190 ),
        ( 100,100,100,100,100,100,100,100,100 ),
        ( 100,120,100,100,100,100,100,145,100 ),
        ( 100,150, 50,150,125,175,150, 40, 60 ),
        ( 100,100,100,100,100,100,100,100,100 ),
        ( 100, 90, 80,100,100,100,100, 60, 80 ),
        ( 100,130,100,100,100,100,100,125,100 ),
        ( 100,135, 40,100,100,100,100,130, 40 ),
        ( 120,150,125,100,100,100,100,200,150 ),
        ( 100,200, 80,100,100,100,100, 40, 80 ),
        ( 100,100,100,100,100,100,100,100,100 ),
        ( 100,100,150,100,100,100,100, 70,125 ),
        ( 100,125,175,100,100,100,100, 75,150 ) );

   TypeData: array[WorldTypes] of IndusRArray =
      {    Bio  Che  Min  SYG  SYJ  SYS  SYT  Sup  Tri }
      (  (   0,  40,  40,   0,   0,   0,   0,10.0,  20 ),
         ( 100, 1.2, 5.0,   0,   0,   0,   0, 1.1, 5.0 ),
         (   0, 1.2, 1.7, 100,   0,   0,   0, 1.1, 1.7 ),
         (   0, 0.5, 0.5, 100,   0,   0,   0, 0.5, 0.5 ),
         (   0, 2.0, 2.5, 100,   0,   0,   0, 1.5, 2.0 ),
         (   0,  80,  10,   0,   0,   0,   0, 1.1,  10 ),
         (   0, 2.0, 2.5, 100,   0,   0,   0, 1.5, 2.0 ),
         (   0, 1.2, 1.7,   0, 100,   0,   0, 1.1, 1.7 ),
         (   0, 0.5, 0.5,   0, 100,   0,   0, 0.5, 0.5 ),
         (   0,  10,  80,   0,   0,   0,   0, 1.1,  10 ),
         ( 100, 1.2, 5.0,   0,   0,   0,   0, 1.1, 5.0 ),
         (   0,   0,   0,   0,   0,   0,   0,   0,   0 ),
         (   0,  40,  40,   0,   0,   0,   0, 1.1,  20 ),
         (   0,  40,  40,   0,   0,   0,   0, 0.5,  20 ),
         (   0, 1.2, 1.3,   0,   0, 100,   0, 1.1, 1.2 ),
         (   0, 0.5, 0.5,   0,   0, 100,   0, 0.5, 0.5 ),
         (   0, 1.2, 1.7,   0,   0,   0, 100, 1.1, 1.2 ),
         (   0, 0.5, 0.5,   0,   0,   0, 100, 0.5, 0.5 ),
         (   0,  10,  10,   0,   0,   0,   0, 1.1,   5 ),
         (   0,  30,  10,   0,   0,   0,   0, 1.1,  20 ),
         (   0,  10,  10,   0,   0,   0,   0, 1.1,  80 ) );

   PrincipalIndustry: array[WorldTypes] of IndusTypes =
      ( SupInd, BioInd, SYGInd, SYGInd, SYGInd, CheInd, SYGInd, SYJInd,
        SYJInd, MinInd, BioInd, SYGInd, MinInd, MinInd, SYSInd, SYSInd,
        SYTInd, SYTInd, MinInd, MinInd, TriInd );

   { %population in military at 50 military index, by world type (DATACNST.PAS:351-356). }
   OptMilitary: array[WorldTypes] of Integer =
      {  Agr Amb Bse BseS Cap  Che  Ind Jmp JmpS Min Nnj Out }
      (   5,100,200, 200,200,  10,  80,100,100, 20,150,  0,
      {  Raw RawS Str StrS Trn TrnS Rsr Ter Tri }
         20,  20,150, 150, 80,  80,  1, 10, 30 );

   { ThgAdj[IndusTypes][fgt..tri] , columns: fgt hkr jmp jtn pen ssp trn men nnj amb che met sup tri }
   ThgAdjFgtTrn: array[IndusTypes,ShipTypes] of Integer =
      (  (   0,  0,  0,  0,  0,  0,  0 ),
         (   0,  0,  0,  0,  0,  0,  0 ),
         (   0,  0,  0,  0,  0,  0,  0 ),
         (  27,  5,  9,  5,  4,  2, 10 ),
         (   0, 25, 50, 30,  0,  0,  0 ),
         (   0,  0,  0,  0, 40, 15,  0 ),
         (  75,  0,  0,  0,  0,  0, 45 ),
         (   0,  0,  0,  0,  0,  0,  0 ),
         (   0,  0,  0,  0,  0,  0,  0 )  );
   ThgAdjMenTri: array[IndusTypes,CargoTypes] of Integer =
      (  (   0, 10,175,  0,  0,  0,  0 ),
         (   0,  0,  0,175,  0,  0,  0 ),
         (   0,  0,  0,  0,350,  0,  0 ),
         (   0,  0,  0,  0,  0,  0,  0 ),
         (   0,  0,  0,  0,  0,  0,  0 ),
         (   0,  0,  0,  0,  0,  0,  0 ),
         (   0,  0,  0,  0,  0,  0,  0 ),
         (   0,  0,  0,  0,  0,320,  0 ),
         (   0,  0,  0,  0,  0,  0, 75 )  );

   RawMShips: array[ShipTypes,CargoTypes] of Integer =
      { men nnj amb che met sup tri }
      (  (   0,  0,  0,  5, 30,  0,  2 ),   { fgt }
         (   0,  0,  0,100,110,  0, 18 ),   { hkr }
         (   0,  0,  0, 65, 70,  0, 12 ),   { jmp }
         (   0,  0,  0,100,110,  0, 16 ),   { jtn }
         (   0,  0,  0, 95,275,  0, 20 ),   { pen }
         (   0,  0,  0,175,520,  0, 30 ),   { ssp }
         (   0,  0,  0, 90,600,  0, 10 )  );{ trn }
   RawMMenTri: array[CargoTypes,CargoTypes] of Integer =
      { men nnj amb che met sup tri }
      (  (   0,  0,  0,  0,  0,  0,  0 ),   { men }
         (   0,  0,100, 50,  0,  0,  0 ),   { nnj }
         (   0,  0,  0,110,  0,  0,  0 ),   { amb }
         (   0,  0,  0,  0,  0,  0,  0 ),   { che }
         (   0,  0,  0,  0,  0,  0,  0 ),   { met }
         (   0,  0,  0,  0,  0,  0,  0 ),   { sup }
         (   0,  0,  0,  0,  0,  0,  0 )  );{ tri }

   TechDev: array[TechLevel] of TechnologySet =
      (  [sup],
         [men,met,sup],
         [men,che..sup],
         [GDM,men,che..tri],
         [GDM,fgt,men,che..tri],
         [GDM,fgt,trn,men,che..tri],
         [GDM,ion,fgt,trn,jmp,jtn,men,che..tri],
         [defn..ion,fgt..pen,trn,men,amb..tri,outp],
         [LAM..trn,men..tri,SRM,cmm,cmp,outp],
         [LAM..tri,SRM..outp,lnk,dis],
         [LAM..dis]  );

var
   { See the Rnd doc comment at the top of this file. Defaults to 0 (every global var in Pascal is
     zero-initialized), matching plain FixedRandom(0) until a harness opts into a different value. }
   RngFixedValue: LongInt;

function GreaterInt(a,b: LongInt): LongInt;
function LesserInt(a,b: LongInt): LongInt;
function ThgLmt(x: Real): LongInt;
function Rnd(Mn,Mx: Integer): Integer;
function RndVar(Value,Variation: LongInt): LongInt;
procedure ChangeRevIndexV(var RevIndex: LongInt; Chg: LongInt);
function PascalRound(x: Real): LongInt;
function Expnt(Base,Exponent: Real): Real;
function TotalProd(Pop: LongInt; Tech: TechLevel): LongInt;
procedure UpdateMilitaryScenario(Pop: LongInt; Typ: WorldTypes; var MPop: LongInt);

implementation

function GreaterInt(a,b: LongInt): LongInt; begin if a>b then GreaterInt:=a else GreaterInt:=b; end;
function LesserInt(a,b: LongInt): LongInt; begin if a<b then LesserInt:=a else LesserInt:=b; end;
function ThgLmt(x: Real): LongInt;
   begin
   if x>MaxResources then ThgLmt:=MaxResources
   else if x<0 then ThgLmt:=0
   else ThgLmt:=Trunc(x);
   end;
function Rnd(Mn,Mx: Integer): Integer;
   begin
   Rnd:=Mn+RngFixedValue;
   end;

{ INT.PAS:120-131 — value +/- variation% of itself. }
function RndVar(Value,Variation: LongInt): LongInt;
   var temp1: LongInt;
   begin
   temp1:=Trunc(Value*(Variation/100));
   RndVar:=Rnd(Value-temp1,Value+temp1);
   end;

{ PRIMINTR.PAS:ChangeRevIndex — clamps a world's revolution index to [0,100]. }
procedure ChangeRevIndexV(var RevIndex: LongInt; Chg: LongInt);
   begin
   if RevIndex+Chg>100 then RevIndex:=100
   else if RevIndex+Chg<0 then RevIndex:=0
   else RevIndex:=RevIndex+Chg;
   end;

function PascalRound(x: Real): LongInt;
   begin
   if x>=0 then PascalRound:=Trunc(x+0.5)
   else PascalRound:=Trunc(x-0.5);
   end;

function Expnt(Base,Exponent: Real): Real;
   begin
   Expnt:=Exp(Exponent*Ln(Base));
   end;

function TotalProd(Pop: LongInt; Tech: TechLevel): LongInt;
   var temp1: Real;
   begin
   if Pop<=0 then Pop:=1;
   temp1:=K1*Expnt(Pop+K2,K3)*TechAdj[Tech]/100;
   if temp1>999 then temp1:=999
   else if temp1<0 then temp1:=0;
   TotalProd:=PascalRound(temp1);
   end;

{ UPDATE.PAS:606-617, verbatim. Used by revolution.pas, which chains it before
  UpdateRevolutionScenario, matching UpdateWorld's real call order at UPDATE.PAS:1386-1388:
  UpdateMilitary always runs immediately before UpdateRevolution. (military.pas itself was retired in
  favor of the patch-based runworld.pas driver's military domain, which runs the real UpdateWorld
  instead of this isolated transcription — see MilitaryCases's doc comment.) }
procedure UpdateMilitaryScenario(Pop: LongInt; Typ: WorldTypes; var MPop: LongInt);
   var
      OptimumMilitary: LongInt;
   begin
   OptimumMilitary:=ThgLmt(RndVar(PascalRound((Pop/150)*OptMilitary[Typ]),10));
   if OptimumMilitary>MPop then
      MPop:=ThgLmt(MPop+(Pop/10)*(OptMilitary[Typ]/100));
   end;

end.
