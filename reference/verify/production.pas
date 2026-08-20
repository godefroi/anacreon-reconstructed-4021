{ Standalone harness to verify the C# reconstruction of UPDATE.PAS's production formulas
  (AnnualTickHandler, economy phase Commit 2) against the real Pascal tables and formulas, without
  needing the full Universe data structure. Formulas here are transcribed directly from
  INTRFACE.PAS / MISC.PAS / UPDATE.PAS in ../DOSAnacreonSource131; shared tables/helpers live in
  common.pas (see its header for why this doesn't just USES the real DataCnst unit).

  Build and run (FreePascal, tested with fpc 3.2.2):
    fpc production.pas && ./production.exe

  Machine-parseable mode, one case per command-line argument, each a comma-separated tuple:
    Cls,Typ,Pop,Eff,TechOrd,AmbAddict,
    IndusBio,IndusChe,IndusMin,IndusSYG,IndusSYJ,IndusSYS,IndusSYT,IndusSup,IndusTri,
    CargoMen,CargoNnj,CargoAmb,CargoChe,CargoMet,CargoSup,CargoTri,TrillumReserve
  where Cls/Typ/TechOrd are WorldClass/WorldTypes/TechLevel's 0-based ordinals (see common.pas) and
  AmbAddict is 0 or 1. Runs FullPipeline (below — the same call sequence RunProductionPipeline uses)
  and prints one "key=value;..." line per case to stdout, consumed by PascalHarness. Example:
    ./production case 9,20,1000,100,10,0,0,0,0,0,0,0,0,0,100,0,0,0,0,0,1000,0,500

  KNOWN DEVIATION: ProductionShips/ProductionCargo below are split into two procedures, each with
  its own "FOR IndI:=BioInd TO SYTInd" loop, where the real Production procedure (and the C# port)
  runs ONE such loop with both ship and cargo production nested inside per industry. Because raw
  material cargo mutates in place across the whole loop, the split changes results whenever two
  industries in BioInd..SYTInd both have a developed level AND raw materials are scarce enough to
  throttle production — every case (Scenario F/H below, and every ProductionCases case) avoids this
  by using scenarios where only one such industry is ever nonzero. If a future case needs multiple
  simultaneously-developed industries under raw-material scarcity, merge these back into one
  procedure first. }
program Production;

{$mode fpc}

uses Common;

procedure GetIndustrialDistribution(Tech: TechLevel; Cls: WorldClass; Pop: LongInt;
                                    Typ: WorldTypes; Eff: Integer; AmbAdd: Boolean;
                                    IssChe,IssMin,IssSup,IssTri: Integer;
                                    var IndDist: IndusRArray);
   var
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
      TriRes, RevDelta: LongInt;
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

{ ---------------------------------------------------------------------------------------------- }

function ParseLongInt(const s: String): LongInt;
   var code: Integer;
   begin
   Val(s, ParseLongInt, code);
   if code<>0 then
      begin
      WriteLn(StdErr, 'production: bad integer "',s,'" (position ',code,')');
      Halt(1);
      end;
   end;

procedure RunCase(const arg: String);
   var
      parts: array[1..23] of LongInt;
      partIdx,i,startPos: Integer;
      tok: String;
      Cls: WorldClass;
      Typ: WorldTypes;
      Pop: LongInt;
      Eff: Integer;
      Tech: TechLevel;
      AmbAddict: Boolean;
      Indus: IndusArray;
      Cargo: CargoArray;
      Ships: ShipArray;
      TriReserves: LongInt;
   begin
   partIdx:=1;
   startPos:=1;
   for i:=1 to Length(arg)+1 do
      if (i>Length(arg)) or (arg[i]=',') then
         begin
         tok:=Copy(arg,startPos,i-startPos);
         if partIdx>23 then
            begin
            WriteLn(StdErr,'production: too many fields in "',arg,'"');
            Halt(1);
            end;
         parts[partIdx]:=ParseLongInt(tok);
         Inc(partIdx);
         startPos:=i+1;
         end;
   if partIdx<>24 then
      begin
      WriteLn(StdErr,'production: expected 23 comma-separated fields, got ',partIdx-1,' in "',arg,'"');
      Halt(1);
      end;

   Cls:=WorldClass(parts[1]);
   Typ:=WorldTypes(parts[2]);
   Pop:=parts[3];
   Eff:=parts[4];
   Tech:=TechLevel(parts[5]);
   AmbAddict:=parts[6]<>0;

   Indus[BioInd]:=parts[7];  Indus[CheInd]:=parts[8];  Indus[MinInd]:=parts[9];
   Indus[SYGInd]:=parts[10]; Indus[SYJInd]:=parts[11]; Indus[SYSInd]:=parts[12];
   Indus[SYTInd]:=parts[13]; Indus[SupInd]:=parts[14]; Indus[TriInd]:=parts[15];

   Cargo[men]:=parts[16]; Cargo[nnj]:=parts[17]; Cargo[amb]:=parts[18];
   Cargo[che]:=parts[19]; Cargo[met]:=parts[20]; Cargo[sup]:=parts[21]; Cargo[tri]:=parts[22];

   TriReserves:=parts[23];
   FillChar(Ships,SizeOf(Ships),0);

   FullPipeline(Cls,Typ,Pop,Eff,Tech,AmbAddict,Indus,Cargo,Ships,TriReserves);

   WriteLn('bio=',Indus[BioInd],';che=',Indus[CheInd],';min=',Indus[MinInd],
           ';syg=',Indus[SYGInd],';syj=',Indus[SYJInd],';sys=',Indus[SYSInd],
           ';syt=',Indus[SYTInd],';sup=',Indus[SupInd],';tri=',Indus[TriInd],
           ';fgt=',Ships[fgt],';hkr=',Ships[hkr],';jmp=',Ships[jmp],';jtn=',Ships[jtn],
           ';pen=',Ships[pen],';ssp=',Ships[ssp],';trn=',Ships[trn],
           ';cargomen=',Cargo[men],';cargonnj=',Cargo[nnj],';cargoamb=',Cargo[amb],
           ';cargoche=',Cargo[che],';cargomet=',Cargo[met],';cargosup=',Cargo[sup],';cargotri=',Cargo[tri],
           ';trillumreserve=',TriReserves);
   end;

procedure RunCaseMode;
   var i: Integer;
   begin
   for i:=2 to ParamCount do
      RunCase(ParamStr(i));
   end;

begin
if (ParamCount>0) and (ParamStr(1)='case') then
   RunCaseMode
else
   begin
   Scenario1;
   WriteLn;
   Scenario2;
   WriteLn;
   Scenario3;
   end;
end.
