(* TYPES -----------------------------------------------------------------------

   ANACREON v1.0
   Global Types and Primitive Structures.

----------------------------------------------------------------------------- *)

UNIT Types;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

CONST

(* DATA STRUCTURE CONSTANTS ----------------------------------------------- *)

   MaxNoOfScenarios = 10;
   MaxNoOfSavedGames = 20;

   MaxNoOfPlanets = 200;
   MaxNoOfStarbases = 100;
   NoOfFleetsPerEmpire = 30;
   MaxNoOfFleets = 240;      { NoOfFleetsPerEmpire * NoOfEmpires }
   MaxNoOfStargates = 50;
   MaxNoOfConstrSites = 50;
   MaxNoOfWanderers = 20;
   MaxNoOfMessages = 10;
   MaxNoOfNewsItems = 200;

   NoOfProbesPerEmpire = 10;
   NoOfNamesPerEmpire = 100;

   MaxNoOfNPEObjectives = 25;

   MaxNoOfParameters = 10;  { number of words max in input line }

   MaxResources = 9999;

TYPE

   { Ranges }
   Resources = 0..MaxResources;
   Index = 0..100;
   IndusIndex = 0..999;
   Population = 0..MaxResources;

   { Linear Types }
   Empire = ( Empire1, Empire2, Empire3, Empire4,
              Empire5, Empire6, Empire7, Empire8,
              Indep  );

   Freq = 0..9999;

CONST
   MaxNoOfEmpires: Empire = Empire8;

TYPE

   Directions = ( NoDir,No,Ne,Ea,Se,So,Sw,We,Nw );

   { Technology and Resources }
   TechnologyTypes = ( NoRes,LAM,def,GDM,ion,
                       fgt,hkr,jmp,jtn,pen,ssp,trn,
                       men,nnj,amb,che,met,sup,tri,
                       SRM,cmm,frt,cmp,out,gte,lnk,dis,ter );

CONST
	FirstResource = LAM;
	LastResource = tri;

	FirstWarMachine = LAM;
	LastWarMachine = trn;

TYPE
   ResourceTypes = NoRes..tri;
   ShipTypes = fgt..trn;
   RawMTypes = che..tri;
   CargoTypes = men..tri;
   DefnsTypes = LAM..ion;
   AttackTypes = NoRes..nnj;

   ConstrTypes = SRM..dis;
   StarbaseTypes = cmm..out;
   StargateTypes = gte..dis;

   { Technology Level }
   TechLevel = ( PreTchLvl,PrimitLvl,PreAtmLvl,AtomicLvl,
                 PreWrpLvl,WrpTchLvl,JmpTchLvl,BioTchLvl,StrTchLvl,
                 PreGteLvl,GteTchLvl );

   { World Classes }
   WorldClass = ( AmbCls,ArdCls,ArtCls,BarCls,ClsJ,ClsK,ClsL,ClsM,
                  DrtCls,EthCls,FstCls,GsGCls,HLfCls,IceCls,JngCls,
                  OcnCls,ParCls,PsnCls,RnsCls,UndCls,TerCls,VlcCls );

   { World Types }
   WorldTypes = ( AgrTyp,AmbTyp,BseTyp,BseSTyp,CapTyp,CheTyp,IndTyp,
                  JmpTyp,JmpSTyp,MinTyp,NnjTyp,OutTyp,RawTyp,RawSTyp,StrTyp,
                  StrSTyp,TrnTyp,TrnSTyp,RsrTyp,TerTyp,TriTyp );

   { Industry Types }
   IndusTypes = ( BioInd,CheInd,MinInd,SYGInd,SYJInd,SYSInd,
                  SYTInd,SupInd,TriInd );

   { Special Conditions }
   SpecialConditions = ( AmbAddict,Holocst,Plague,SelfSuff,Virgin );
   SetOfSpecialConditions = SET OF SpecialConditions;

   EmpireModifiers = 
      ( CentralEMD,           { Empire lost if capital conquered. }
        Reserved1EMD,
        Reserved2EMD,
        Reserved3EMD,
        Reserved4EMD,
        Reserved5EMD,
        Reserved6EMD );
   SetOfEmpireModifiers = SET OF EmpireModifiers;

   SelfSuffConditions = Integer;

   { Fleet and Probe Status }
   FleetTypes = ( Standard,JumpFleet,HKFleet,Penetrator,AdvWrpFleet );
   FleetStatus = ( FReady,FInTrans,FInactive,FLost );
   ProbeStatus = ( PReady,PInTrans,PAtDest,PLost );

   { Sector Objects }
   ObjectTypes = ( Void,Con,Pln,Base,Gate,BlkHl,Plsr,WrmHl,Flt,DestFlt,Wndr,ArtOBJ );
   PhenomenaTypes = Void..WrmHl;
   NebulaTypes = ( NoNeb,Nebula,DarkNebula,DenseNebula );

   { Primitive Data Structures }
   IDNumber = RECORD
      ObjTyp: ObjectTypes;
      Index: Byte;
   END;  { IDNumber }

   ShipArray = ARRAY [ShipTypes] OF Resources;
   CargoArray = ARRAY [CargoTypes] OF Resources;
   DefnsArray = ARRAY [DefnsTypes] OF Resources;
   ResourceArray = array [LAM..tri] of Resources;

   TotalShipArray = ARRAY [ShipTypes] OF LongInt;
   TotalCargoArray = ARRAY [CargoTypes] OF LongInt;

   IDList = ARRAY [1..500] OF IDNumber;

   IDPtr = ^IDCell;

   IDCell = RECORD
      ID: IDNumber;
      Next: IDPtr;
   END;

   { Industrial development:  Industrial development of each industry.  The sum
     of all industry must not exceed the TIP.  }
   IndusArray = ARRAY [IndusTypes] OF IndusIndex;
   IndusRArray = ARRAY [IndusTypes] OF Real;

   { Industrial distribution:  per cent of TIP devoted to each industry.
     Industrial development is adjusted to conform to these settings. }
   IndusDistArray = ARRAY [IndusTypes] OF Real;

   { Sets }
   EmpireSet = SET OF Empire;
   ScoutSet = SET OF Empire1..Empire8;
   FleetSet = SET OF 1..MaxNoOfFleets;
   PlanetSet = SET OF 1..MaxNoOfPlanets;
   TechnologySet = SET OF TechnologyTypes;
   ResourceSet = SET OF ResourceTypes;

   ShellPos = ( DpSpc,HiOrb,Orbit,SbOrb,Grnd );

CONST
   { special constants }
   EmptyQuadrant: IDNumber = ( ObjTyp: Void; Index: 0 );

VAR

   SetOfActiveFleets: SET OF 1..MaxNoOfFleets;
   SetOfFleetsOf: ARRAY [Empire] OF SET OF 1..MaxNoOfFleets;
   SetOfActivePlanets: PlanetSet;
   SetOfPlanetsOf: ARRAY [Empire] OF PlanetSet;
   SetOfActiveStarbases: SET OF 1..MaxNoOfStarbases;
   SetOfStarbasesOf: ARRAY [Empire] OF SET OF 1..MaxNoOfStarbases;
   SetOfActiveGates: SET OF 1..MaxNoOfStargates;
   SetOfActiveConstructionSites: SET OF 1..MaxNoOfConstrSites;
   SetOfConstructionSitesOf: ARRAY [Empire] OF SET OF 1..MaxNoOfConstrSites;

   NoOfPlanets: Word;

IMPLEMENTATION

END.
