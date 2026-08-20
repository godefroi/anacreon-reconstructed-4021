(* CLSCOMM ---------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT ClsComm;

{$IFDEF Overlay}
{$O+,F+}
{$ENDIF}

INTERFACE

USES Strg,
     Int,
     EIO,
     Types,
     Galaxy,
     DataCnst,
     Misc,
     PrimIntr,
     Intrface,
     Scena,
(*
     Artifact,
*)
     Display;

PROCEDURE CloseUpCom(Obj: IDNumber);
PROCEDURE ProductionCom(Obj: IDNumber);

IMPLEMENTATION

USES
	Fleet,
   Environ;

CONST
   ClassName: ARRAY [WorldClass] OF String16 =
      ( 'Ambrosia',
        'Arid',
        'Artificial',
        'Barren',
        'Class j',
        'Class k',
        'Class l',
        'Class m',
        'Desert',
        'Earth-like',
        'Forest world',
        'Gas Giant',
        'Hostile life',
        'Ice world',
        'Jungle world',
        'Ocean world',
        'Paradise',
        'Poisonous',
        'Ancient ruins',
        'Underground',
	'Terraforming',
        'Volcanic'
      );

PROCEDURE GetBasicInfo(Obj: IDNumber;
                       VAR Name: String32;
                       VAR Cls: WorldClass;
                       VAR Typ: WorldTypes;
                       VAR Tech: TechLevel;
                       VAR Pop: Population;
                       VAR Eff: Index;
                       VAR AmbAdd: Boolean;
                       VAR Rev: Index);
   VAR
      TStr: String32;
      XY: XYCoord;
      Special: SetOfSpecialConditions;

   BEGIN
   Name:=ObjectName(Player,Obj,LongFormat);
   IF Pos(',',Name)=0 THEN
      BEGIN
      GetCoord(Obj,XY);
      GetCoordName(XY,TStr);
      Name:=Name+' ('+TStr+')';
      END;

   Cls:=GetClass(Obj);
   Typ:=GetType(Obj);
   Pop:=GetPopulation(Obj);
   Tech:=GetTech(Obj);
   Eff:=GetEfficiency(Obj);
   Rev:=GetRevIndex(Obj);
   GetSpecial(Obj,Special);
   AmbAdd:=(AmbAddict IN Special);
   END;  { GetBasicInfo }

PROCEDURE ProductionCom(Obj: IDNumber);
   VAR
      Cls: WorldClass;
      Typ: WorldTypes;
      Tech: TechLevel;
      Pop: Population;
      Eff,Rev: Index;
      AmbAdd: Boolean;
      ATIP: Integer;
      TriRes: Word;
      Special: SetOfSpecialConditions;
      Ships,SProd: ShipArray;
      Cargo,CCons,CProd: CargoArray;
      ISSPIndex,ClsAdj,IndDist: IndusDistArray;
      OptInd,Indus: IndusArray;
      Defns,DOpt,DProd: DefnsArray;
      Name: String32;

   PROCEDURE DisplayBasicInfo(VAR Name: String32;
                              Cls: WorldClass;
                              Typ: WorldTypes;
                              Tech: TechLevel;
                              Pop: Population;
                              Eff: Index);
      VAR
         TStr: String32;

      BEGIN
      WriteString('Production: '+Name,1,1,C.SYSDispHigh);

      WriteString('Class:',7,3,C.SYSDispWind);  WriteString(ClassName[Cls],14,3,C.SYSDispWind);
      WriteString('Type:',8,4,C.SYSDispWind);  WriteString(TypeName[Typ],14,4,C.SYSDispWind);
      WriteString('Technology:',2,5,C.SYSDispWind);  WriteString(TechN[Tech],14,5,C.SYSDispWind);

      WriteString('Population: ',2,6,C.SYSDispWind);
      IF Pop<100 THEN
         BEGIN
         Str(Pop*10,TStr);
         TStr:=TStr+' million';
         END
      ELSE
         BEGIN
         Str((Pop/100):5:2,TStr);
         TStr:=TStr+' billion';
         END;
      WriteString(TStr,14,6,C.SYSDispWind);

      WriteString('Efficiency:',2,7,C.SYSDispWind);
      Str(Eff,TStr);
      TStr:=TStr+'%';
      WriteString(TStr,14,7,C.SYSDispWind);
      END;  { DisplayBasicInfo }

   PROCEDURE GetIndusInfo(Obj: IDNumber;
                          ATIP: Integer;
                          Cls: WorldClass;
                          VAR ISSPInd,ClsAdj,IndDist: IndusDistArray;
                          VAR OptInd,Indus: IndusArray);

      VAR
         IndI: IndusTypes;

      BEGIN
      GetIndustrialDistribution(Obj,IndDist);
      GetIndus(Obj,Indus);

      FOR IndI:=BioInd TO TriInd DO
         BEGIN
         ISSPInd[IndI]:=ISSP[GetISSP(Obj,IndI)];
         ClsAdj[IndI]:=ClassIndAdj[Cls,IndI];
         OptInd[IndI]:=Round(ATIP*IndDist[IndI]*ClsAdj[IndI]/10000);
         IF (OptInd[IndI]=0) AND (IndDist[IndI]<>0) THEN
            OptInd[IndI]:=1;
         END;
      END;  { GetIndusInfo }

   PROCEDURE GetProdInfo(Obj: IDNumber;
                         Cls: WorldClass;
                         Typ: WorldTypes;
                         Pop: Population;
                         Tech: TechLevel;
                         Eff: Index;
                         VAR Indus: IndusArray;
                         VAR SProd: ShipArray;
                         VAR CProd,CCons: CargoArray;
                         VAR TriRes: Word);
      VAR
         ProdAdj,IP: Real;
         IndI: IndusTypes;
         ThgI,RawI: ResourceTypes;
         Technology: TechnologySet;
         ETech: TechLevel;
         Special: SetOfSpecialConditions;

      BEGIN
      TriRes:=TrillumReserves(Obj);

      IP:=(TechAdj2[Tech]/100)*((Eff+250)/100)/K6;
      GetEmpireTechnology(Player,ETech,Technology);
      Technology:=Technology * TechDev[Tech];

      GetSpecial(Obj,Special);

      FillChar(CCons,SizeOf(CCons),0);
      FillChar(CProd,SizeOf(CProd),0);
      FillChar(SProd,SizeOf(SProd),0);
      FOR IndI:=BioInd TO TriInd DO
         IF Indus[IndI]>0 THEN
            BEGIN
            ProdAdj:=IP*Sqr(Indus[IndI]+K4);
            FOR ThgI:=fgt TO tri DO
               IF (ThgAdj[IndI,ThgI]<>0) AND (ThgI IN Technology) THEN
                  BEGIN
                  IF ThgI IN [fgt..trn] THEN
                     SProd[ThgI]:=ThgLmt(ProdAdj*ThgAdj[IndI,ThgI])
                  ELSE
                     BEGIN
                     CProd[ThgI]:=IntLmt(ProdAdj*ThgAdj[IndI,ThgI]);

                     IF (ThgI=nnj) AND (Typ<>NnjTyp) THEN
                        CProd[nnj]:=0
                     ELSE IF (ThgI=amb) AND (Typ<>AmbTyp) THEN
                        CProd[amb]:=0
                     ELSE IF (ThgI=amb) AND (NOT (Cls IN [AmbCls,ParCls])) THEN
                        CProd[amb]:=0;
                     END;
                  END;
            END;  { loop }

      FOR ThgI:=fgt TO trn DO
         FOR RawI:=amb TO tri DO
            CCons[RawI]:=CCons[RawI]+ThgLmt(SProd[ThgI]*(RawM[ThgI][RawI]/100));

      FOR ThgI:=amb TO tri DO
         FOR RawI:=amb TO tri DO
            CCons[RawI]:=CCons[RawI]+ThgLmt(CProd[ThgI]*(RawM[ThgI][RawI]/100));

      CCons[sup]:=ThgLmt((Pop/100)*SuppliesPerBillion);

      IF AmbAddict IN Special THEN
         CCons[amb]:=CCons[amb]+ThgLmt((Pop/100)*DrugsPerBillion);
      END;  { GetProdInfo }

   PROCEDURE DisplayIndusInfo(VAR ISSPIndex,ClsAdj,IndDist: IndusDistArray;
                              VAR OptInd,Indus: IndusArray);
      CONST
         PosX = 40;
         PosY = 2;

      VAR
         IndI: IndusTypes;
         Name,TStr: String32;
         x: Byte;
         SYInd: IndusTypes;

      BEGIN
      Name:=ObjectName(Player,Obj,LongFormat);
      WriteString('Production: '+Name,1,1,C.SYSDispHigh);

      SYInd:=ShipYardInd(Indus);

      WriteString('Bio  Che  Min  SY-  Sup  Tri',PosX+8,PosY,C.SYSDispWind);
      WriteString(SYLetN[SYInd],PosX+25,PosY,C.SYSDispWind);

      WriteString('ISSP:',PosX+2,PosY+1,C.SYSDispWind);
      WriteString('ClsAdj:',PosX,PosY+2,C.SYSDispWind);
      WriteString('Dist:',PosX+2,PosY+3,C.SYSDispWind);
      WriteString('Opt:',PosX+3,PosY+4,C.SYSDispWind);
      WriteString('Indus:',PosX+1,PosY+5,C.SYSDispWind);

      x:=PosX+8;
      FOR IndI:=BioInd TO TriInd DO
         IF (NOT (IndI IN [SYGInd..SYTInd])) OR (IndI=SYInd) THEN
            BEGIN
            IF IndI IN [BioInd,SYGInd..SYTInd] THEN
               WriteString('---',x,PosY+1,C.SYSDispWind)
            ELSE BEGIN
               Str(ISSPIndex[IndI]*100:3:0,TStr);
               WriteString(TStr,x,PosY+1,C.SYSDispWind);
               END;

            Str(ClsAdj[IndI]:3:0,TStr);
            WriteString(TStr,x,PosY+2,C.SYSDispWind);
            Str(IndDist[IndI]:3:0,TStr);
            WriteString(TStr,x,PosY+3,C.SYSDispWind);
            Str(OptInd[IndI]:3,TStr);
            WriteString(TStr,x,PosY+4,C.SYSDispWind);
            Str(Indus[IndI]:3,TStr);
            WriteString(TStr,x,PosY+5,C.SYSDispWind);

            Inc(x,5);
            END;
      END;  { DisplayIndusInfo }

   PROCEDURE DisplayProdInfo(VAR Ships: ShipArray;
                             VAR Cargo: CargoArray;
                             VAR SProd: ShipArray;
                             VAR CProd,CCons: CargoArray;
                             TriRes: Word);
      CONST
         PosX = 1;
         PosY = 9;

      VAR
         ShpI: ShipTypes;
         CarI: CargoTypes;
         TStr: String32;
         x: Byte;

      BEGIN
      WriteString('fgt  hkr  jmp  jtn  pen  str  trn  men  nnj  amb  che  met  sup  tri',PosX+8,PosY,C.SYSDispWind);
      WriteString('Avail:',PosX,PosY+1,C.SYSDispWind);
      WriteString(' Prod:',PosX,PosY+2,C.SYSDispWind);
      WriteString(' Cons:',PosX,PosY+3,C.SYSDispWind);

      x:=PosX+7;
      FOR ShpI:=fgt TO trn DO
         BEGIN
         Str(Ships[ShpI]:4,TStr);
         WriteString(TStr,x,PosY+1,C.SYSDispWind);
         Str(SProd[ShpI]:4,TStr);
         WriteString(TStr,x,PosY+2,C.SYSDispWind);
         Inc(x,5);
         END;

      x:=PosX+42;
      FOR CarI:=men TO tri DO
         BEGIN
         Str(Cargo[CarI]:4,TStr);
         WriteString(TStr,x,PosY+1,C.SYSDispWind);

         IF CProd[CarI]<=MaxResources THEN
            Str(CProd[CarI]:4,TStr)
         ELSE
            BEGIN
            Str(Round(CProd[CarI]/1000):3,TStr);
            TStr:=TStr+'K';
            END;
         WriteString(TStr,x,PosY+2,C.SYSDispWind);

         IF CCons[CarI]<=MaxResources THEN
            Str(CCons[CarI]:4,TStr)
         ELSE
            BEGIN
            Str(Round(CCons[CarI]/1000):3,TStr);
            TStr:=TStr+'K';
            END;
         WriteString(TStr,x,PosY+3,C.SYSDispWind);

         Inc(x,5);
         END;

      WriteString('Trillum reserves: ',PosX,PosY+5,C.SYSDispWind);
      Str(10*Round(TriRes/100),TStr);
      WriteString(TStr+' thousand KTons',PosX+18,PosY+5,C.SYSDispWind);
      END;  { DisplayProdInfo }

   PROCEDURE GetOutpostInfo(VAR ISSPIndex,ClsAdj,IndDist: IndusDistArray;
                            VAR OptInd,Indus: IndusArray);

      BEGIN
      FillChar(ISSPIndex,SizeOf(ISSPIndex),5);
      FillChar(ClsAdj,SizeOf(ClsAdj),0);
      FillChar(IndDist,SizeOf(IndDist),0);
      FillChar(OptInd,SizeOf(OptInd),0);
      FillChar(Indus,SizeOf(Indus),0);
      END;  { GetOutpostInfo }

   PROCEDURE DisplayDefnsInfo(Defns,DOpt,DProd: DefnsArray);
      CONST
         PosX = 50;
         PosY = 14;

      VAR
         x: Word;
         DefI: DefnsTypes;
         TStr: String8;

      BEGIN
      WriteString('LAM  def  GDM  ion',PosX+8,PosY,C.SYSDispWind);
      WriteString('Avail:',PosX,PosY+1,C.SYSDispWind);
      WriteString('Opt:',PosX+2,PosY+2,C.SYSDispWind);
      WriteString('Prod:',PosX+1,PosY+3,C.SYSDispWind);
      x:=PosX+7;
      FOR DefI:=LAM TO ion DO
         BEGIN
         Str(Defns[DefI]:4,TStr);
         WriteString(TStr,x,PosY+1,C.SYSDispWind);
         Str(DOpt[DefI]:4,TStr);
         WriteString(TStr,x,PosY+2,C.SYSDispWind);
         Str(DProd[DefI]:4,TStr);
         WriteString(TStr,x,PosY+3,C.SYSDispWind);

         Inc(x,5);
         END;
      END;  { DisplayDefnsInfo }

   PROCEDURE GetDefnsInfo(Obj: IDNumber; Pop: Population; Men: Resources; Eff: Index;
                          VAR Defns,DOpt,DProd: DefnsArray);
      VAR
         DefI: DefnsTypes;
         MaxBuild: Resources;
         Optimum,BuildRate: Real;
         BTyp: StarbaseTypes;
         Tech: TechLevel;
         Technology: TechnologySet;

      BEGIN
      GetEmpireTechnology(Player,Tech,Technology);
      Technology:=Technology * TechDev[GetTech(Obj)];

      BuildRate:=(Men/2000)*(1+((Eff-50)/100));
      Optimum:=(Men/100);

      IF Obj.ObjTyp=Pln THEN
         BuildRate:=BuildRate*(Pop/2000)
      ELSE IF Obj.ObjTyp=Base THEN
         BEGIN
         BTyp:=GetBaseType(Obj);
         IF BTyp=out THEN
            Optimum:=Optimum/4
         ELSE IF (BTyp=cmm) OR (BTyp=frt) THEN
            BEGIN
            Optimum:=Optimum*4;
            BuildRate:=BuildRate*4;
            END;
         END;

      FOR DefI:=LAM TO ion DO
         IF DefI IN Technology THEN
            BEGIN
            DOpt[DefI]:=ThgLmt(Optimum*DefAdj[DefI]);
            MaxBuild:=GreaterInt(ThgLmt(BuildRate*DefBuildRate[DefI]),1);
            DProd[DefI]:=MaxBuild;

            IF (Obj.ObjTyp=Base) AND (BTyp=out) AND (DefI=def) THEN
               BEGIN
               DOpt[DefI]:=0;
               DProd[DefI]:=0;
               END
            ELSE IF (DefI=LAM) AND (NOT (Typ IN [BseTyp,CapTyp])) THEN
               BEGIN
               DOpt[DefI]:=0;
               DProd[DefI]:=0;
               END;
            END
         ELSE
            BEGIN
            DProd[DefI]:=0;
            DOpt[DefI]:=0;
            END
      END;  { GetDefnsInfo }

   { ProductionCom: MAIN PROCEDURE }
   BEGIN
   ClrDisplayScreen;

   GetBasicInfo(Obj,Name,Cls,Typ,Tech,Pop,Eff,AmbAdd,Rev);
   DisplayBasicInfo(Name,Cls,Typ,Tech,Pop,Eff);

   GetSpecial(Obj,Special);
   ATIP:=TotalProd(Pop,Tech);
   IF AmbAddict IN Special THEN
      ATIP:=Round(ATIP*AmbrosiaAdj);

   IF ATIP>999 THEN
      ATIP:=999;

   IF (Obj.ObjTyp=Base) AND (GetBaseType(Obj)<>cmp) THEN
      GetOutpostInfo(ISSPIndex,ClsAdj,IndDist,OptInd,Indus)
   ELSE
      GetIndusInfo(Obj,ATIP,Cls,ISSPIndex,ClsAdj,IndDist,OptInd,Indus);
   GetProdInfo(Obj,Cls,Typ,Pop,Tech,Eff,Indus,SProd,CProd,CCons,TriRes);
   GetShips(Obj,Ships);
   GetCargo(Obj,Cargo);
   GetDefns(Obj,Defns);
   GetDefnsInfo(Obj,Pop,Cargo[men],Eff,Defns,DOpt,DProd);

   DisplayIndusInfo(ISSPIndex,ClsAdj,IndDist,OptInd,Indus);
   DisplayProdInfo(Ships,Cargo,SProd,CProd,CCons,TriRes);
   DisplayDefnsInfo(Defns,DOpt,DProd);

   WriteCommLine('');
   WriteErrorMessage('');
   END;  { ProductionCom }

PROCEDURE CloseUpCom(Obj: IDNumber);
   VAR
      NameStr: String32;
      Cls: WorldClass;
      Typ: WorldTypes;
      Tech: TechLevel;
      Pop: Population;
      Rev,Eff: Index;
      Ships: ShipArray;
      Cargo: CargoArray;
      Defns: DefnsArray;
      AmbAdd,Dummy: Boolean;
      ArtID: IDNumber;
		EDA, Range: Word;
		FltStatus: FleetStatus;
		FltType: FleetTypes;
		PosXY,DestXY: XYCoord;
		Resource: ResourceArray;

   PROCEDURE DisplayBasicInfo(VAR Name: String32;
                              Cls: WorldClass;
                              Typ: WorldTypes;
                              Tech: TechLevel;
                              Pop: Population;
                              Eff: Index;
                              AmbAdd: Boolean;
                              Rev: Index);
      VAR
         TStr: String32;
         Line: LineStr;

      BEGIN
      Line:='Close Up: '+Name;
      AdjustString(Line,36);
      Line:=Line+TypeName[Typ];
      AdjustString(Line,60);
		IF Known(Player,Obj) THEN
	      Line:=Line+EmpireName(GetStatus(Obj));

      WriteString(Line,1,1,C.SYSDispHigh);

      WriteString(' Cls:',2,3,C.SYSDispWind);
      WriteString('Tech:',2,4,C.SYSDispWind);
      WriteString(' Pop:',2,5,C.SYSDispWind);
      WriteString('Eff:',24,3,C.SYSDispWind);
      WriteString('Amb:',24,4,C.SYSDispWind);
      WriteString('Rev:',24,5,C.SYSDispWind);

		IF Scouted(Player,Obj) THEN
			BEGIN
			WriteString(TechN[Tech],8,4,C.SYSDispWind);
			WriteString(ClassName[Cls],8,3,C.SYSDispWind);

      	IF Pop<100 THEN
         	BEGIN
         	Str(Pop*10,TStr);
         	TStr:=TStr+' million';
         	END
      	ELSE
         	BEGIN
         	Str((Pop/100):5:2,TStr);
         	TStr:=TStr+' billion';
         	END;
      	WriteString(TStr,8,5,C.SYSDispWind);

      	Str(Eff,TStr);
      	TStr:=TStr+'%';
      	WriteString(TStr,29,3,C.SYSDispWind);

      	IF AmbAdd THEN
         	TStr:='yes'
      	ELSE
         	TStr:='no';
      	WriteString(TStr,29,4,C.SYSDispWind);

      	WriteString(HiLo(Rev),29,5,C.SYSDispWind);
			END;
      END;  { DisplayBasicInfo }

   PROCEDURE DisplayCargoInfo(Emp: Empire; Cargo: CargoArray);
      CONST
         PosX = 51;
         PosY = 3;

      VAR
         CarI: CargoTypes;
         TStr: String16;
         Pos: Word;

      BEGIN
      WriteString('amb  che  met  sup  tri',PosX+1,PosY,C.SYSDispWind);
      IF Emp=Player THEN
         BEGIN
         Pos:=PosX-1;
         FOR CarI:=amb TO tri DO
            BEGIN
            Str(Cargo[CarI]:5,TStr);
            WriteString(TStr,Pos,PosY+1,C.SYSDispWind);
            Inc(Pos,5);
            END;
         END
      ELSE
         WriteString('???? ???? ???? ???? ????',PosX,PosY+1,C.SYSDispWind);
      END;  { DisplayCargoInfo }

   PROCEDURE DisplayMilitaryInfo(Emp: Empire; Ships: ShipArray; Cargo: CargoArray; Defns: DefnsArray);
      CONST
         PosX = 2;
         PosY = 7;

      VAR
         ResI: ResourceTypes;
         Pos: Word;

      PROCEDURE DisplayLevel(Emp: Empire; x,y: Word; Level: Resources);
         VAR
            TStr: String8;

         BEGIN
         IF Emp=Player THEN
            Str(Level:5,TStr)
         ELSE IF Scouted(Player,Obj) THEN
            TStr:=YesNo(Level)
			ELSE
				TStr:=' ????';

         WriteString(TStr,x,y,C.SYSDispWind);
         END;  { DisplayLevel }

      BEGIN
      WriteString('fgt  hkr  jmp  jtn  pen  str  trn    men  nnj    LAM  def  GDM  ion',
                  PosX+1,PosY,C.SYSDispWind);

      Pos:=PosX-1;
      FOR ResI:=fgt TO trn DO
         BEGIN
         DisplayLevel(Emp,Pos,PosY+1,Ships[ResI]);
         Inc(Pos,5);
         END;

      Inc(Pos,2);
      FOR ResI:=men TO nnj DO
         BEGIN
         DisplayLevel(Emp,Pos,PosY+1,Cargo[ResI]);
         Inc(Pos,5);
         END;

      Inc(Pos,2);
      FOR ResI:=LAM TO ion DO
         BEGIN
         DisplayLevel(Emp,Pos,PosY+1,Defns[ResI]);
         Inc(Pos,5);
         END;
      END;  { DisplayMilitaryInfo }

	PROCEDURE GetFleetInfo(FltID: IDNumber;
								  VAR NameStr: String32;
								  VAR Range: Word;
								  VAR EDA: Word;
								  VAR FltType: FleetTypes;
								  VAR FltStatus: FleetStatus;
								  VAR PosXY: XYCoord;
								  VAR DestXY: XYCoord);
		BEGIN
		NameStr := ObjectName (Player, FltID, LongFormat);
		Range := EstimatedRange (FltID);
		EDA := EstimatedDateOfArrival (FltID);
		FltType := TypeOfFleet (FltID);
		FltStatus := GetFleetStatus (FltID);
		GetCoord (FltID, PosXY);
		GetFleetDestination (FltID, DestXY);
		END;   { GetFleetInfo }

	PROCEDURE DisplayFleetInfo(FltID: IDNumber;
								  NameStr: String32;
								  Emp: Empire;
								  Range: Word;
								  EDA: Word;
								  FltType: FleetTypes;
								  FltStatus: FleetStatus;
								  PosXY: XYCoord;
								  DestXY: XYCoord);
   	CONST
      	FltStatusName: ARRAY [FleetStatus] OF STRING [18] =
         	( 'at destination    ',
           	'In transit (?)    ',
           	'out of trillum    ',
           	'lost              ' );

			FltTypeName: ARRAY [FleetTypes] OF STRING [20] =
				( 'Warpfleet',
				  'Jumpfleet',
				  'Hunter-Killer Fleet',
				  'Stealth Fleet',
				  'Fast-Warp Fleet' );

		VAR
			Line: LineStr;
			Loc: Location;
			temp1: String32;

		BEGIN
		Line:='Close Up: '+NameStr;
		AdjustString(Line,36);
		IF Scouted(Player,FltID) THEN
			BEGIN
			Line:=Line+FltTypeName[FltType];
			AdjustString(Line,60);
			Line:=Line+EmpireName(Emp);
			END;

		WriteString(Line,1,1,C.SYSDispHigh);

		Loc.XY:=PosXY;
		Loc.ID:=EmptyQuadrant;
		GetName(Emp,Loc,ShortFormat,Line);
		WriteString('   Position:',2,3,C.SYSDispWind);
		WriteString(Line, 15,3,C.SYSDispWind);

		IF (Emp=Player) THEN
			BEGIN
      	IF FltStatus=FInTrans THEN
         	BEGIN
         	Str(EDA,temp1);
         	Line:='In transit ('+temp1+')';
         	END
      	ELSE
         	Line:=FltStatusName[FltStatus];
			END
		ELSE
			BEGIN
			IF Scouted(Player,FltID) THEN
				Line:=FltStatusName[FltStatus]
			ELSE
				Line:='(unknown)';
			END;
		WriteString('     Status:',2,4,C.SYSDispWind);
		WriteString(Line, 15,4,C.SYSDispWind);

		IF (Emp=Player) THEN
			BEGIN
			Loc.XY:=DestXY;
			Loc.ID:=EmptyQuadrant;
			GetName(Emp,Loc,ShortFormat,Line);
			END
		ELSE
			BEGIN
			IF (FltStatus=FReady) AND Scouted(Player,FltID) THEN
				GetName(Emp,Loc,ShortFormat,Line)
			ELSE
				Line:='(unknown)';
			END;
		WriteString('Destination:',2,5,C.SYSDispWind);
		WriteString(Line, 15,5,C.SYSDispWind);

		IF (Emp=Player) THEN
			Str(Range,Line)
		ELSE
			Line:='(unknown)';
		WriteString('      Range:',2,6,C.SYSDispWind);
		WriteString(Line, 15,6,C.SYSDispWind);
		END;   { DisplayFleetInfo }


	PROCEDURE DisplayFleetComplement (Emp: Empire; Resource: ResourceArray);
		VAR
			ResI: ResourceTypes;
			TempStr: String16;
			x: Word;

		BEGIN
      WriteString('fgt  hkr  jmp  jtn  pen  str  trn  men  nnj  amb  che  met  sup  tri',
                  3,8,C.SYSDispWind);

		x:=1;
		FOR ResI:=fgt to tri DO
			BEGIN
			IF Emp=Player THEN
				Str(Resource[ResI]:5,TempStr)
			ELSE IF Scouted(Player,Obj) AND (ResI IN [fgt..trn]) THEN
				TempStr:=YesNo(Resource[ResI])
			ELSE
				TempStr:=' ????';

			WriteString (TempStr, x, 9, C.SYSDispWind);
			Inc(x,5);
			END;
		END;  { DisplayFleetComplement }

   { CloseUpCommand: MAIN PROCEDURE }
   BEGIN
   ClrDisplayScreen;

	IF (Obj.ObjTyp = Flt) THEN
		BEGIN
		GetFleetInfo(Obj,NameStr,Range,EDA,FltType,FltStatus,PosXY,DestXY);
		GetResources (Obj, Resource);

		DisplayFleetInfo(Obj,NameStr,GetStatus(Obj),Range,EDA,FltType,FltStatus,PosXY,DestXY);
		DisplayFleetComplement(GetStatus(Obj), Resource);
		END
	ELSE
		BEGIN
   	GetBasicInfo(Obj,NameStr,Cls,Typ,Tech,Pop,Eff,AmbAdd,Rev);
   	GetShipsKnown(Player,Obj,Ships);
   	GetDefns(Obj,Defns);
   	GetCargo(Obj,Cargo);

   	DisplayBasicInfo(NameStr,Cls,Typ,Tech,Pop,Eff,AmbAdd,Rev);
   	DisplayCargoInfo(GetStatus(Obj),Cargo);
   	DisplayMilitaryInfo(GetStatus(Obj),Ships,Cargo,Defns);
   
		(*ARTIFACT
   	ArtID:=FirstArtifact;
   	NextArtifactOn(ArtID,Obj);
   	WHILE ArtID.ObjTyp<>Void DO
      	BEGIN
      	IF ArtifactSituation(ArtID,CloseUpSIT) THEN
         	CloseUpSituation(ArtID,Player);
      	NextArtifactOn(ArtID,Obj);
      	END;  { while }
		*)

		IF Scouted(Player,Obj) THEN
	   	DisplayBackground(Player,Obj,2,10,C.SYSDispWind,False,Dummy);
		END;

   WriteCommLine('');
   WriteErrorMessage('');
   END;  { CloseUpCommand }

END.

