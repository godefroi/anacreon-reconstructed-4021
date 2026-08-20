{ Standalone harness to verify the C# reconstruction of UPDATE.PAS's production formulas
  (AnnualTickHandler, economy phase Commit 2) against the real Pascal tables and formulas, without
  needing the full Universe data structure. Constants and formulas are transcribed directly from
  DATACNST.PAS / INTRFACE.PAS / MISC.PAS / UPDATE.PAS.

  Build and run (FreePascal, tested with fpc 3.2.2):
    fpc verify.pas && ./verify.exe

  Rnd(Min,Max) always returns Min — the deterministic stand-in matching the C# test double
  FixedRandom(0) used in AnnualTickHandlerTests.cs, so expected values line up exactly.

  KNOWN DEVIATION: ProductionShips/ProductionCargo below are split into two procedures, each with
  its own "FOR IndI:=BioInd TO SYTInd" loop, where the real Production procedure (and the C# port)
  runs ONE such loop with both ship and cargo production nested inside per industry. Because raw
  material cargo mutates in place across the whole loop, the split changes results whenever two
  industries in BioInd..SYTInd both have a developed level AND raw materials are scarce enough to
  throttle production — Scenario F/H below avoid this by using scenarios where only one such
  industry is ever nonzero. If a future scenario needs multiple simultaneously-developed
  industries under raw-material scarcity, merge these back into one procedure first. }
program Verify;

{$mode fpc}

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
   { deterministic stand-in matching the C# test double FixedRandom(0): every roll floors to Mn }
   Rnd:=Mn;
   end;

{ FreePascal's built-in Round() is IEEE round-half-to-even (confirmed empirically: Round(2.5)=2,
  Round(3.5)=4 — even under {$MODE TP}, which doesn't change this). Real Turbo Pascal's Round()
  rounds half away from zero. Every Round() call in this file MUST go through this function instead
  of the built-in, or a value landing on an exact .5 boundary silently diverges from Turbo Pascal. }
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

procedure GetIndustrialDistribution(Tech: TechLevel; Cls: WorldClass; Pop: LongInt;
                                    Typ: WorldTypes; Eff: Integer; AmbAdd: Boolean;
                                    IssChe,IssMin,IssSup,IssTri: Integer;
                                    var IndDist: IndusRArray);
   var
      X: IndusRArray;
      Alpha: Real;
      Beta: IndusRArray;
      A,B,I: Real;
      TIP: Real;
      MainInd,IndI: IndusTypes;
      Temp: Real;
   begin
   FillChar(IndDist,SizeOf(IndDist),0);

   Alpha:=(TechAdj2[Tech]/100)*(Eff+250)/K6;
   TIP:=TotalProd(Pop,Tech);
   if AmbAdd then TIP:=TIP*AmbrosiaAdj;
   if TIP>999 then TIP:=999;

   Temp:=TIP/10000;
   for IndI:=BioInd to TriInd do
      begin
      Beta[IndI]:=Temp*ClassIndAdj[Cls,IndI];
      if Beta[IndI]=0 then Beta[IndI]:=1;
      end;

   I:=( Sqrt( (SafetyAdj*ISSP[IssSup]*(SuppliesPerBillion/100)*Pop) /
              ((ThgAdjMenTri[SupInd,sup]/100)*Alpha) ) - K4) / Beta[SupInd];
   if (I>95) or (I<0) or (Typ=AgrTyp) then I:=95;
   IndDist[SupInd]:=I;

   if Typ in [AgrTyp,CheTyp,MinTyp,RawTyp,RawSTyp,RsrTyp..TriTyp] then
      begin
      I:=100-IndDist[SupInd];
      IndDist[CheInd]:=I*(TypeData[Typ,CheInd]/100);
      IndDist[MinInd]:=I*(TypeData[Typ,MinInd]/100);
      IndDist[TriInd]:=I*(TypeData[Typ,TriInd]/100);
      end
   else
      begin
      MainInd:=PrincipalIndustry[Typ];

      A:=-K4*(1/Beta[CheInd]+1/Beta[MinInd]+1/Beta[TriInd]);
      B:=(Sqrt(SafetyAdj*ISSP[IssChe]*Gamma[CheInd,MainInd])/Beta[CheInd] +
          Sqrt(SafetyAdj*ISSP[IssMin]*Gamma[MinInd,MainInd])/Beta[MinInd] +
          Sqrt(SafetyAdj*ISSP[IssTri]*Gamma[TriInd,MainInd])/Beta[TriInd] );

      I:=(100-(IndDist[SupInd]+A+(K4*B)))/(1+(Beta[MainInd]*B));
      if I>TypeData[Typ,MainInd] then I:=TypeData[Typ,MainInd];
      IndDist[MainInd]:=I;

      I:=( (IndDist[MainInd]*Beta[MainInd]+K4) *
           Sqrt(SafetyAdj*ISSP[IssChe]*Gamma[CheInd,MainInd]) - K4 )/Beta[CheInd];
      if I<0 then I:=1;
      IndDist[CheInd]:=I;

      I:=( (IndDist[MainInd]*Beta[MainInd]+K4) *
           Sqrt(SafetyAdj*ISSP[IssMin]*Gamma[MinInd,MainInd]) - K4 )/Beta[MinInd];
      if I<0 then I:=1;
      IndDist[MinInd]:=I;

      I:=( (IndDist[MainInd]*Beta[MainInd]+K4) *
           Sqrt(SafetyAdj*ISSP[IssTri]*Gamma[TriInd,MainInd]) - K4 )/Beta[TriInd];
      if I<0 then I:=1;
      IndDist[TriInd]:=I;
      end;
   end;

procedure UpdateIndustry(Cls: WorldClass; Tech: TechLevel; Eff: Integer; Pop: LongInt;
                         AmbAddict: Boolean; var IndDist: IndusRArray;
                         var Indus: IndusArray; var CargoMet: LongInt);
   var
      TIP: LongInt;
      OptimumLevel: LongInt;
      RawNeeded: LongInt;
      IndI: IndusTypes;
      ConsRate: LongInt;
      Temp: Real;
   begin
   TIP:=TotalProd(Pop,Tech);
   if AmbAddict then TIP:=PascalRound(TIP*AmbrosiaAdj);
   if TIP>999 then TIP:=999;

   Temp:=TIP/10000;
   for IndI:=BioInd to TriInd do
      begin
      OptimumLevel:=PascalRound(Temp*IndDist[IndI]*ClassIndAdj[Cls,IndI]);
      if (IndDist[IndI]>0) and (OptimumLevel=0) then OptimumLevel:=1;
      if Indus[IndI]<OptimumLevel then
         begin
         ConsRate:=PascalRound(OptimumLevel*(Eff/500));
         if ConsRate<1 then ConsRate:=1;
         ConsRate:=LesserInt(ConsRate,OptimumLevel-Indus[IndI]);
         RawNeeded:=ThgLmt((ConsRate/100)*NewIndRawN[IndI]);
         if RawNeeded>CargoMet then
            begin
            ConsRate:=Trunc(100*(CargoMet/NewIndRawN[IndI]));
            RawNeeded:=CargoMet;
            end;
         end
      else if Indus[IndI]>OptimumLevel then
         begin
         ConsRate:=-PascalRound(Eff/2);
         if ConsRate>-1 then ConsRate:=-1;
         if Indus[IndI]+ConsRate<OptimumLevel then ConsRate:=OptimumLevel-Indus[IndI];
         RawNeeded:=0;
         end
      else
         begin
         ConsRate:=0;
         RawNeeded:=0;
         end;

      if Indus[IndI]+ConsRate>999 then
         begin
         ConsRate:=999-Indus[IndI];
         RawNeeded:=ThgLmt((ConsRate/100)*NewIndRawN[IndI]);
         end
      else if Indus[IndI]+ConsRate<0 then
         begin
         ConsRate:=-Indus[IndI];
         RawNeeded:=0;
         end;

      Indus[IndI]:=Indus[IndI]+ConsRate;

      RawNeeded:=LesserInt(CargoMet,RawNeeded);
      Dec(CargoMet,RawNeeded);
      end;
   end;

procedure ProduceTrillum(var TriProd: LongInt; TriAvail: LongInt; var TriReserves: LongInt;
                         var RevIndexDelta: LongInt);
   begin
   TriAvail:=LesserInt(TriAvail,MaxResources);
   TriProd:=LesserInt(TriProd,MaxResources-TriAvail);

   if TriReserves=0 then
      begin
      TriProd:=0;
      RevIndexDelta:=RevIndexDelta+Rnd(10,20);
      end
   else if (TriReserves*LongInt(20))<TriProd then
      RevIndexDelta:=RevIndexDelta+Rnd(5,10)
   else if ((TriReserves*LongInt(10))<TriProd) and (Rnd(1,2)=1) then
      RevIndexDelta:=RevIndexDelta+Rnd(3,5);

   TriReserves:=GreaterInt(0,TriReserves-PascalRound(TriProd/100));
   end;

procedure ProduceRawMaterial(var Indus: IndusArray; Technology: TechnologySet; IP: Real;
                             var TempCargo: CargoArray; var TriReserves: LongInt;
                             var RevIndexDelta: LongInt);
   var
      Prod: LongInt;
      IndI: IndusTypes;
      ThgI: CargoTypes;
      ProdAdj: Real;
   begin
   for IndI:=CheInd to TriInd do
      if Indus[IndI]>0 then
         begin
         ProdAdj:=IP*Sqr(Indus[IndI]+K4);
         for ThgI:=che to tri do
            if (ThgAdjMenTri[IndI,ThgI]<>0) and (ThgI in Technology) then
               begin
               Prod:=GreaterInt(1,ThgLmt(ProdAdj*ThgAdjMenTri[IndI,ThgI]));
               if ThgI=tri then
                  ProduceTrillum(Prod,TempCargo[tri],TriReserves,RevIndexDelta);
               Inc(TempCargo[ThgI],Prod);
               end;
         end;
   end;

procedure ProductionShips(Typ: WorldTypes; var Indus: IndusArray; Technology: TechnologySet;
                          IP: Real; var Ships: ShipArray; var Cargo: CargoArray);
   var
      Prod: LongInt;
      IndI: IndusTypes;
      ThgI: ShipTypes;
      RawI: CargoTypes;
      RawNeeded: array[CargoTypes] of LongInt;
      ProdAdj: Real;
   begin
   for IndI:=BioInd to SYTInd do
      if Indus[IndI]>0 then
         begin
         ProdAdj:=IP*Sqr(Indus[IndI]+K4);
         for ThgI:=fgt to trn do
            if (ThgAdjFgtTrn[IndI,ThgI]<>0) and (ThgI in Technology) then
               begin
               Prod:=ThgLmt(ProdAdj*ThgAdjFgtTrn[IndI,ThgI]);
               if Prod<=0 then Prod:=1;

               Prod:=LesserInt(Prod,MaxResources-Ships[ThgI]);

               for RawI:=amb to tri do
                  if RawMShips[ThgI,RawI]>0 then
                     begin
                     RawNeeded[RawI]:=ThgLmt(Prod*(RawMShips[ThgI,RawI]/100));
                     if RawNeeded[RawI]>Cargo[RawI] then
                        begin
                        Prod:=ThgLmt((Cargo[RawI]/RawMShips[ThgI,RawI])*100);
                        RawNeeded[RawI]:=ThgLmt(Prod*(RawMShips[ThgI,RawI]/100));
                        end;
                     end
                  else
                     RawNeeded[RawI]:=0;

               for RawI:=che to tri do
                  begin
                  RawNeeded[RawI]:=LesserInt(Cargo[RawI],RawNeeded[RawI]);
                  Dec(Cargo[RawI],RawNeeded[RawI]);
                  end;

               Ships[ThgI]:=LesserInt(MaxResources,Ships[ThgI]+Prod);
               end;
         end;
   end;

procedure ProductionCargo(Typ: WorldTypes; Cls: WorldClass; var Indus: IndusArray;
                          Technology: TechnologySet; IP: Real; var Cargo: CargoArray);
   var
      Prod: LongInt;
      IndI: IndusTypes;
      ThgI: CargoTypes;
      RawI: CargoTypes;
      RawNeeded: array[CargoTypes] of LongInt;
      ProdAdj: Real;
   begin
   for IndI:=BioInd to SYTInd do
      if Indus[IndI]>0 then
         begin
         ProdAdj:=IP*Sqr(Indus[IndI]+K4);
         for ThgI:=men to amb do
            if (ThgAdjMenTri[IndI,ThgI]<>0) and (ThgI in Technology) then
               begin
               Prod:=ThgLmt(ProdAdj*ThgAdjMenTri[IndI,ThgI]);

               if (ThgI=nnj) and (Typ<>NnjTyp) then
                  Prod:=0
               else if (ThgI=amb) and (Typ<>AmbTyp) then
                  Prod:=0
               else if (ThgI=amb) and (NOT (Cls in [AmbCls,ParCls])) then
                  Prod:=0
               else if Prod<=0 then
                  Prod:=1;

               Prod:=LesserInt(Prod,MaxResources-LesserInt(Cargo[ThgI],MaxResources));

               for RawI:=amb to tri do
                  if RawMMenTri[ThgI,RawI]>0 then
                     begin
                     RawNeeded[RawI]:=ThgLmt(Prod*(RawMMenTri[ThgI,RawI]/100));
                     if RawNeeded[RawI]>Cargo[RawI] then
                        begin
                        Prod:=ThgLmt((Cargo[RawI]/RawMMenTri[ThgI,RawI])*100);
                        RawNeeded[RawI]:=ThgLmt(Prod*(RawMMenTri[ThgI,RawI]/100));
                        end;
                     end
                  else
                     RawNeeded[RawI]:=0;

               for RawI:=che to tri do
                  begin
                  RawNeeded[RawI]:=LesserInt(Cargo[RawI],RawNeeded[RawI]);
                  Dec(Cargo[RawI],RawNeeded[RawI]);
                  end;

               Inc(Cargo[ThgI],Prod);
               end;
         end;
   end;

{ ---------------------------------------------------------------------------------------------- }

procedure PrintDist(const label_: String; var D: IndusRArray);
   var IndI: IndusTypes;
   begin
   Write(label_,': ');
   for IndI:=BioInd to TriInd do
      Write(D[IndI]:0:6,' ');
   WriteLn;
   end;

procedure Scenario1;
   var
      Dist: IndusRArray;
      Indus: IndusArray;
      IndI: IndusTypes;
      Metal: LongInt;
      TIP: LongInt;
   begin
   WriteLn('--- Scenario 1: TotalProd sanity ---');
   WriteLn('TotalProd(1000,GteTchLvl) = ', TotalProd(1000,GteTchLvl));
   WriteLn('TotalProd(209,GteTchLvl) = ', TotalProd(209,GteTchLvl));
   WriteLn('TotalProd(1,GteTchLvl) = ', TotalProd(1,GteTchLvl));
   WriteLn('TotalProd(0,GteTchLvl) = ', TotalProd(0,GteTchLvl));
   WriteLn;

   WriteLn('--- Scenario 2: GetIndustrialDistribution, EthCls/RsrTyp (PI-less), Pop=1000, Eff=100, Gate, ISSP all index 5 ---');
   GetIndustrialDistribution(GteTchLvl,EthCls,1000,RsrTyp,100,False,5,5,5,5,Dist);
   PrintDist('Dist',Dist);
   WriteLn;

   WriteLn('--- Scenario 3: GetIndustrialDistribution, EthCls/AgrTyp (PI-less, Sup forced 95), Pop=1000, Eff=100, Gate ---');
   GetIndustrialDistribution(GteTchLvl,EthCls,1000,AgrTyp,100,False,5,5,5,5,Dist);
   PrintDist('Dist',Dist);
   WriteLn;

   WriteLn('--- Scenario 4: GetIndustrialDistribution, EthCls/IndTyp (has PI, SYG), Pop=1000, Eff=100, Gate ---');
   GetIndustrialDistribution(GteTchLvl,EthCls,1000,IndTyp,100,False,5,5,5,5,Dist);
   PrintDist('Dist',Dist);
   WriteLn;

   WriteLn('--- Scenario 5: UpdateIndustry starting from zero industry, EthCls/RsrTyp, Pop=1000, Eff=100, Gate, Metal=9999 ---');
   GetIndustrialDistribution(GteTchLvl,EthCls,1000,RsrTyp,100,False,5,5,5,5,Dist);
   FillChar(Indus,SizeOf(Indus),0);
   Metal:=9999;
   UpdateIndustry(EthCls,GteTchLvl,100,1000,False,Dist,Indus,Metal);
   Write('Indus: ');
   for IndI:=BioInd to TriInd do Write(Indus[IndI],' ');
   WriteLn;
   WriteLn('Metal remaining: ',Metal);
   WriteLn;

   WriteLn('--- Scenario 6: UpdateIndustry with Indus already AT optimum for one field, Cls=EthCls Typ=RsrTyp ---');
   GetIndustrialDistribution(GteTchLvl,EthCls,1000,RsrTyp,100,False,5,5,5,5,Dist);
   TIP:=TotalProd(1000,GteTchLvl);
   WriteLn('TIP=',TIP,' OptimumSupply=PascalRound(TIP/10000*Dist[Sup]*100)=',PascalRound((TIP/10000)*Dist[SupInd]*100));
   end;

procedure Scenario2;
   var
      Indus: IndusArray;
      Cargo: CargoArray;
      Ships: ShipArray;
      Tech: TechnologySet;
      IP: Real;
      TriRes, RevDelta, Prod: LongInt;
      CT: CargoTypes;
      ST: ShipTypes;
   begin
   WriteLn('--- Scenario A: ProduceRawMaterial, TrillumMining=100, Eff=100, Gate, reserves=500, cargo empty ---');
   FillChar(Indus,SizeOf(Indus),0);
   Indus[TriInd]:=100;
   FillChar(Cargo,SizeOf(Cargo),0);
   Tech:=TechDev[GteTchLvl];
   IP:=(TechAdj2[GteTchLvl]/100)*((100+250)/100)/K6;
   WriteLn('IP=',IP:0:10);
   TriRes:=500;
   RevDelta:=0;
   ProduceRawMaterial(Indus,Tech,IP,Cargo,TriRes,RevDelta);
   WriteLn('Cargo[tri]=',Cargo[tri],' TriReserves=',TriRes,' RevDelta=',RevDelta);
   WriteLn;

   WriteLn('--- Scenario B: Fighter production from ShipyardGeneral=100, Eff=100, Gate, plenty of raw materials ---');
   FillChar(Indus,SizeOf(Indus),0);
   Indus[SYGInd]:=100;
   FillChar(Ships,SizeOf(Ships),0);
   FillChar(Cargo,SizeOf(Cargo),0);
   Cargo[che]:=1000; Cargo[met]:=1000; Cargo[tri]:=1000;
   Tech:=TechDev[GteTchLvl];
   IP:=(TechAdj2[GteTchLvl]/100)*((100+250)/100)/K6;
   ProductionShips(CapTyp,Indus,Tech,IP,Ships,Cargo);
   WriteLn('Ships[fgt]=',Ships[fgt],' che=',Cargo[che],' met=',Cargo[met],' tri=',Cargo[tri]);
   WriteLn;

   WriteLn('--- Scenario C: Ninja production from Bioindustry=100, Eff=100, Gate, Ambrosia scarce (5), Chemicals plenty ---');
   FillChar(Indus,SizeOf(Indus),0);
   Indus[BioInd]:=100;
   FillChar(Cargo,SizeOf(Cargo),0);
   Cargo[amb]:=5; Cargo[che]:=1000;
   Tech:=TechDev[GteTchLvl];
   IP:=(TechAdj2[GteTchLvl]/100)*((100+250)/100)/K6;
   ProductionCargo(NnjTyp,EthCls,Indus,Tech,IP,Cargo);
   WriteLn('Cargo[nnj]=',Cargo[nnj],' Cargo[amb]=',Cargo[amb],' Cargo[che]=',Cargo[che]);
   WriteLn;

   WriteLn('--- Scenario D: same as C but ambrosia=0 entirely (edge: reserves=0 style throttle) ---');
   FillChar(Indus,SizeOf(Indus),0);
   Indus[BioInd]:=100;
   FillChar(Cargo,SizeOf(Cargo),0);
   Cargo[amb]:=0; Cargo[che]:=1000;
   ProductionCargo(NnjTyp,EthCls,Indus,Tech,IP,Cargo);
   WriteLn('Cargo[nnj]=',Cargo[nnj],' Cargo[amb]=',Cargo[amb],' Cargo[che]=',Cargo[che]);
   WriteLn;

   WriteLn('--- Scenario E: PreTchLvl independent-equivalent tech (TechDev[PreTchLvl]) blocks everything but sup ---');
   FillChar(Indus,SizeOf(Indus),0);
   Indus[TriInd]:=100;
   Indus[CheInd]:=100;
   Indus[SupInd]:=100;
   FillChar(Cargo,SizeOf(Cargo),0);
   Tech:=TechDev[PreTchLvl];
   IP:=(TechAdj2[PreTchLvl]/100)*((100+250)/100)/K6;
   TriRes:=500; RevDelta:=0;
   ProduceRawMaterial(Indus,Tech,IP,Cargo,TriRes,RevDelta);
   WriteLn('Cargo[tri]=',Cargo[tri],' Cargo[che]=',Cargo[che],' Cargo[sup]=',Cargo[sup],' TriReserves=',TriRes);
   end;

procedure FullPipeline(Cls: WorldClass; Typ: WorldTypes; Pop: LongInt; Eff: Integer;
                       Tech: TechLevel; AmbAddict: Boolean;
                       var Indus: IndusArray; var Cargo: CargoArray; var Ships: ShipArray;
                       var TriReserves: LongInt);
   { Mirrors AnnualTickHandler.RunProductionPipeline's exact call order. }
   var
      Dist: IndusRArray;
      Technology: TechnologySet;
      IP: Real;
      RevDelta: LongInt;
   begin
   Technology:=TechDev[Tech];
   IP:=(TechAdj2[Tech]/100)*((Eff+250)/100)/K6;
   RevDelta:=0;

   ProduceRawMaterial(Indus,Technology,IP,Cargo,TriReserves,RevDelta);
   GetIndustrialDistribution(Tech,Cls,Pop,Typ,Eff,AmbAddict,5,5,5,5,Dist);
   UpdateIndustry(Cls,Tech,Eff,Pop,AmbAddict,Dist,Indus,Cargo[met]);
   ProductionShips(Typ,Indus,Technology,IP,Ships,Cargo);
   ProductionCargo(Typ,Cls,Indus,Technology,IP,Cargo);
   end;

procedure Scenario3;
   var
      Indus: IndusArray;
      Cargo: CargoArray;
      Ships: ShipArray;
      TriRes: LongInt;
      IndI: IndusTypes;
      ST: ShipTypes;
   begin
   WriteLn('--- Scenario F: FULL PIPELINE, CapTyp/EthCls, Pop=1000, Eff=100, Gate, Indus[SYG]=100, Indus[Tri]=100 ---');
   FillChar(Indus,SizeOf(Indus),0);
   Indus[SYGInd]:=100;
   Indus[TriInd]:=100;
   FillChar(Cargo,SizeOf(Cargo),0);
   Cargo[che]:=5000; Cargo[met]:=5000; Cargo[sup]:=5000; Cargo[tri]:=5000;
   FillChar(Ships,SizeOf(Ships),0);
   TriRes:=5000;
   FullPipeline(EthCls,CapTyp,1000,100,GteTchLvl,False,Indus,Cargo,Ships,TriRes);
   Write('Indus: '); for IndI:=BioInd to TriInd do Write(Indus[IndI],' '); WriteLn;
   Write('Ships: '); for ST:=fgt to trn do Write(Ships[ST],' '); WriteLn;
   WriteLn('Cargo che=',Cargo[che],' met=',Cargo[met],' sup=',Cargo[sup],' tri=',Cargo[tri],' TriReserves=',TriRes);
   WriteLn;

   WriteLn('--- Scenario H: FULL PIPELINE, NnjTyp/EthCls, Pop=1000, Eff=100, Gate, Indus[Bio]=100, Ambrosia scarce ---');
   FillChar(Indus,SizeOf(Indus),0);
   Indus[BioInd]:=100;
   FillChar(Cargo,SizeOf(Cargo),0);
   Cargo[che]:=5000; Cargo[met]:=5000; Cargo[sup]:=5000; Cargo[tri]:=5000; Cargo[amb]:=5;
   FillChar(Ships,SizeOf(Ships),0);
   TriRes:=5000;
   FullPipeline(EthCls,NnjTyp,1000,100,GteTchLvl,False,Indus,Cargo,Ships,TriRes);
   Write('Indus: '); for IndI:=BioInd to TriInd do Write(Indus[IndI],' '); WriteLn;
   WriteLn('Cargo nnj=',Cargo[nnj],' amb=',Cargo[amb],' che=',Cargo[che]);
   end;

begin
Scenario1;
WriteLn;
Scenario2;
WriteLn;
Scenario3;
end.
