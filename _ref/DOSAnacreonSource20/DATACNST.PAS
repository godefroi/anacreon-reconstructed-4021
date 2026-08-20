(* DATACNST.PAS ----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT DataCnst;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Strg,Types,DataStrc;

CONST

(* CONSTANTS USED IN CALCULATIONS ------------------------------------------- *)

{ TIP constants:
   These constants control the calculation of the TIP.  The coefficient
   and the exponent control the effect that population has on the TIP.
   Raising either will increase the TIP. }

   K1 = 1.76;
   K2 = 10;
   K3 = 0.75;

{ AP constants:
   These constants relate to the calculation of Absolute Production. }

   K4 = 0.0;
   K5 = 2.0;                     { DO NOT Change }
   K6 = 11000.0;

{ Industrial Distribution }

   SafetyAdj = 1.05;                { Safety margin for distribution }

{ AMBROSIA CONSTANTS }
   AmbrosiaAdj = 1.45;           { increase in industry due to ambrosia addiction. }
   DrugsPerBillion = 11.5;       { kilotons of ambrosia needed per billion people. }
   ChanceToAddict = 25;          { % chance per year to become addicted. }
   AddictDeathCoeff = 0.12;      { 10s of millions of deaths per ton lacking. }
   AddictEffCoeff = 0.9;         { points of eff lost per death. }
   AddictRevICoeff = 0.55;       { points of rev per death. }

{ Supply constants:
   These constants control the production and consumption of supplies. }

   SuppliesPerBillion = 25;     { megatons of supplies needed per billion people. }

{ Technology:
   The following constants govern the chance per year that an empire has of 
   obtaining new technology. }

   TechIncCap = 12;              { % chance for capital }
   TechIncUnv = 15;              { % chance per university world }
   TechIncRns = 5;               { % chance per ruins world }
   TechIncUnvRns = 17;           { % chance for university on ruins world }

   TechLvlInc = 16;              { % chance that world has of increasing tech. }

   TechnologyName: ARRAY [TechnologyTypes] OF STRING [18] = 
      ( '','LAM','defense satellite','GDM','ion cannon',
        'fighter','hunter-killer','jumpship','jumptransport','penetrator',
        'starship','transport','troop','ninja','ambrosia','chemical',
        'metal','supply','trillum','SRM',
        'command base','fortress','industrial complex','outpost',
        'gate','link','disrupter','terraforming' );

   TypeName: ARRAY [WorldTypes] OF STRING [21] = 
      ( 'agricultural world',
        'ambrosia world',
        'base planet',
        'base planet',
        'capital',
        'chemical planet',
        'independent world',
        'jumpship base',
        'jumpship base',
        'metal mine',
        'ninja world',
        'outpost',
        'raw material mine',
        'raw material mine',
        'starship base',
        'starship base',
        'transport base',
        'transport base',
        'university world',
        'terraforming',
        'trillum mine' );

   TerraformPotentialClasses: ARRAY [WorldClass,1..10] OF WorldClass = 

             { Potential Classes:                                                             }
  { Class: } {    1       2       3       4       5       6       7       8       9       10  }
  { AmbCls }(( OcnCls,   ClsM,   Clsj, AmbCls, AmbCls, AmbCls, AmbCls, AmbCls, AmbCls, AmbCls ), 
  { ArdCls } ( DrtCls, ArdCls, ArdCls, ArdCls, ArdCls, ArdCls, ArdCls, ArdCls, ArdCls, ArdCls ),
  { ArtCls } ( ArtCls, ArtCls, ArtCls, ArtCls, ArtCls, ArtCls, ArtCls, ArtCls, ArtCls, ArtCls ),
  { BarCls } ( VlcCls, BarCls, BarCls, BarCls, BarCls, BarCls, BarCls, BarCls, BarCls, BarCls ),
  { ClsJ   } (   ClsJ,   ClsK,   ClsL,   ClsM, EthCls, PsnCls, OcnCls,   ClsJ,   ClsJ,   ClsJ ),
  { ClsK   } (   ClsJ,   ClsK,   ClsL,   ClsM, EthCls, PsnCls, UndCls,   ClsK,   ClsK,   ClsK ),
  { ClsL   } (   ClsJ,   ClsK,   ClsL,   ClsM, EthCls, DrtCls, UndCls,   ClsL,   ClsL,   ClsL ),
  { ClsM   } (   ClsJ,   ClsK,   ClsL,   ClsM, EthCls, DrtCls, OcnCls,   ClsM,   ClsM,   ClsM ),
  { DrtCls } (   ClsL, ArdCls,   ClsM, DrtCls, DrtCls, DrtCls, DrtCls, DrtCls, DrtCls, DrtCls ),
  { EthCls } ( FstCls, EthCls,   ClsJ,   ClsK,   ClsL,   ClsM, EthCls, EthCls, EthCls, EthCls ),
  { FstCls } ( EthCls, JngCls, FstCls, FstCls, FstCls, FstCls, FstCls, FstCls, FstCls, FstCls ),
  { GsGCls } ( PsnCls, GsGCls, GsGCls, GsGCls, GsGCls, GsGCls, GsGCls, GsGCls, GsGCls, GsGCls ),
  { HLfCls } ( HLfCls, FstCls, EthCls, HLfCls, HLfCls, HLfCls, HLfCls, HLfCls, HLfCls, HLfCls ),
  { IceCls } ( OcnCls, IceCls, IceCls, IceCls, IceCls, IceCls, IceCls, IceCls, IceCls, IceCls ),
  { JngCls } ( ParCls, FstCls, JngCls, JngCls, JngCls, JngCls, JngCls, JngCls, JngCls, JngCls ),
  { OcnCls } ( IceCls, AmbCls, OcnCls, OcnCls, OcnCls, OcnCls, OcnCls, OcnCls, OcnCls, OcnCls ),
  { ParCls } ( ParCls, JngCls, ParCls, ParCls, ParCls, ParCls, ParCls, ParCls, ParCls, ParCls ),
  { PsnCls } ( GsGCls,   ClsJ,   ClsK, PsnCls, PsnCls, PsnCls, PsnCls, PsnCls, PsnCls, PsnCls ),
  { RnsCls } ( EthCls, RnsCls, RnsCls, RnsCls, RnsCls, RnsCls, RnsCls, RnsCls, RnsCls, RnsCls ),
  { UndCls } ( VlcCls,   ClsK,   ClsL, UndCls, UndCls, UndCls, UndCls, UndCls, UndCls, UndCls ),
  { TerCls } ( TerCls, TerCls, TerCls, TerCls, TerCls, TerCls, TerCls, TerCls, TerCls, TerCls ),
  { VlcCls } ( UndCls, BarCls, VlcCls, VlcCls, VlcCls, VlcCls, VlcCls, VlcCls, VlcCls, VlcCls ));

 


   BaseTypeData: ARRAY [StarbaseTypes] OF Char =
      'þðão';

   GateTypeData: ARRAY [StargateTypes] OF Char =
      '@';
         
   ThingNames: ARRAY [ResourceTypes] OF STRING [21] =
      ( { NUL }         '',
        { LAM }         'LAMs',
        { def }         'defense satellites',
        { GDM }         'GDMs',
        { ion }         'ion canons',
        { fgt }         'fighter squadrons',
        { hkr }         'hunter-killers',
        { jmp }         'jumpships',
        { jtn }         'jumptransports',
        { pen }         'penetrators',
        { ssp }         'starships',
        { trn }         'transports',
        { men }         'legions',
        { nnj }         'ninja legions',
        { amb }         'kilotons of ambrosia',
        { che }         'megatons of chemicals',
        { met }         'megatons of metals',
        { sup }         'megatons of supplies',
        { tri }         'kilotons of trillum' );

   IndusNames: ARRAY [IndusTypes] OF STRING [16] =
      ( { BioInd }      'bio-tech labs',
        { CheInd }      'chemical plants',
        { MetInd }      'metal mines',
        { SYGInd }      'ship yards',
        { SYJInd }      'jumpship yards',
        { SYSInd }      'starship yards',
        { SYTInd }      'transport yards',
        { SupInd }      'food factories',
        { TriInd }      'trillum mines' );

   ObjName: ARRAY [ObjectTypes] OF STRING [12] =
      ( { Void }        '',
        { Con }         'construction',
        { Pln }         'star system',
        { Base }        'starbase',
        { Gate }        'stargate',
        { BlkHl }       'black hole',
        { Plsr }        'pulsar',
        { WrmHl }       'worm hole',
        { Flt }         'fleet',
        { DestFlt }     '(destroyed)',
        { Wndr }        'unknown',
        { ArtOBJ }      'artifact');

   TypeStr: ARRAY [WorldTypes] OF Char =
      'aAbBCcijJmNorRsStTUXz';
   ClassStr: ARRAY [WorldClass] OF Char =
      'Aa0BjklmDEFGhIJO1P2UXV';
   TechStr: ARRAY [TechLevel] OF STRING [2] =
      ( 'pt',' p','pa',' a','pw',' w',' j',' b',' s','pg',' g' );
   TechN: ARRAY [TechLevel] OF STRING [10] =
      ( 'pre-tech',
        'primitive',
        'pre-atomic',
        'atomic',
        'pre-warp',
        'warp',
        'jump',
        'bio-tech',
        'starship',
        'pre-gate',
        'gate' );
   SYLetN: ARRAY [IndusTypes] OF Char =
      '---AJST--';

   { MPower: Military power of individual ships and defenses 0..100 }
   MPower: ARRAY [LAM..trn] OF Byte =
      { LAM, def, GDM, ion,   fgt, hkr, jmp, jtn, pen, str, trn }
      ( 100, 100,  10,  50,     1,  20,  12,   1,  25, 100,   0 );

   { CombatTable:
     Index of destructive power of AttTyp vs DefTyp.  100 pts will
     destroy one unit, 200 will destroy 2, etc.  Valid entries should 
     NEVER be 0. }
   CombatTable: ARRAY [AttackTypes,AttackTypes] OF Integer =
   {  Defender:
        LAM  def  GDM  ion     fgt  hkr  jmp  jtn  pen  ssp  trn  men  nnj }

   ((0,   0,   0,   0,   0,      0,   0,   0,   0,   0,   0,   0,   0,   0 ), { NUL }
    (0, 200, 100, 200, 150,    250,  60,  75, 100,  50,  15, 200,   0,   0 ), { LAM }
    (0,   0,   0,   0,   0,     25,  90, 100, 100,  50,   6, 250,   0,   0 ), { def }
    (0,   0,   0,   0,   0,    250, 100, 100, 100,  50,  25,  50,   0,   0 ), { GDM }
    (0,   0,   0,   0,   0,     50,  65,  75,  75,  15,   2, 100,   0,   0 ), { ion }
    (0,   5,   5,   1,  25,     25,  15,   8,  20,  12,   1,  25,   5,   4 ), { fgt }
    (0,  12,  40,  38,  20,     50,  50,  75,  50,  50,   4,  75,   0,   0 ), { hkr }
    (0,   8,  15,  25,  10,    150,  25,  38,  50,  25,   1, 150,   0,   0 ), { jmp }
    (0,   0,   1,   0,   0,     25,   1,   1,   5,   0,   0,  10,   0,   0 ), { jtn }
    (0,  15,  35,  50,  35,     50,  65, 105, 125,  50,   5, 250,   0,   0 ), { pen }
    (0,  45,  90, 200,  50,   1000, 250, 300, 500, 100,  15, 500,   0,   0 ), { ssp }
    (0,   0,   0,   0,   0,     12,   0,   0,   1,   0,   0,   5,   0,   0 ), { trn }
    (0,   0,   0,   0,   0,      5,   0,   0,   0,   0,   0,   0,   7,   2 ), { men }
    (0,   0,   0,   0,   0,      8,   0,   0,   0,   0,   0,   0,  20,  10 )  { nnj }
   );

   { WeapEff:
     Average effectiveness of weapons per ship type.
     These values should NEVER be 0. }
   WeapEff: ARRAY [AttackTypes] OF Integer =
     {     LAM,def GDM ion fgt hkr jmp jtn pen ssp trn men nnj }
      (  0,100,150,100, 30, 30,100,100, 10,150,100, 10,100,100 );

   { average value of a given ship }
   ShipValue: ARRAY [AttackTypes] OF Integer =
     {    LAM def GDM ion fgt hkr jmp jtn pen ssp trn men nnj }
      ( 0,  0,  0,  0,  0,  5, 50, 50, 30,100,200, 30, 50,100 );

   { how well a given ship can protect others }
   ProtecOffered: ARRAY [ShipTypes] OF Integer =
      { fgt hkr jmp jtn pen ssp trn }
      (   5, 10, 10,  1, 50,100,  0 );

   { ProtecNeeded:
      A relative measure of stength needed to protect each type of ship.
      This number should NEVER be 0. }
   ProtecNeeded: ARRAY [ShipTypes] OF Integer =
      { fgt hkr jmp jtn pen ssp trn }
      (   5, 30, 20, 50,200,500,150 );

   { average population at 50% eff by tech level }
   BasePop: ARRAY [TechLevel] OF Population =
      {   pt    p   pa    a   pw    w    j    b    s   pg    g }
      (    3,  10, 100, 250, 500, 700,1100,1700,2000,2500,3000 );

   { TIP adjustment by technology }
   TechAdj: array[TechLevel] of Index =
      {  pt   p  pa   a  pw   w   j   b   s  pg   g }
      (  25, 40, 49, 57, 66, 80, 85, 90, 94, 97,100 );

   { production adjustment by technology }
   TechAdj2: array[TechLevel] of Integer =
      {  pt   p  pa   a  pw   w   j   b   s  pg   g }
      (  12, 24, 36, 47, 58, 67, 76, 84, 90, 95,100 );

   { Minimum supply industry for self-sufficient worlds.  By tech level }
    MinSupInd: ARRAY [TechLevel] OF Index =
      {  pt   p  pa   a  pw   w   j   b   s  pg   g }
      (  95, 90, 80, 70, 50, 40, 34, 30, 28, 26, 23 );

   { minimum tech level required to have the given industry }
   MinTechLevel: ARRAY [IndusTypes] OF TechLevel =
      {    BioInd    CheInd    MinInd    SYGInd }
      ( BioTchLvl,PreAtmLvl,PrimitLvl,PreWrpLvl,

      {    SYJInd    SYSInd    SYTInd    SupInd    TriInd }
        JmpTchLvl,StrTchLvl,WrpTchLvl,PreTchLvl,AtomicLvl );

   { Minimum tech level required to designate a world to a certain type }
   MinTechForType: ARRAY [WorldTypes] OF TechLevel =
      ( { AgrTyp }         PreTchLvl,
        { AmbTyp }         BioTchLvl,
        { BseTyp }         PreWrpLvl,
        { BseSTyp }        GteTchLvl,
        { CapTyp }         JmpTchLvl,
        { CheTyp }         PreAtmLvl,
        { IndTyp }         PreTchLvl,
        { JmpTyp }         JmpTchLvl,
        { JmpSTyp }        GteTchLvl,
        { MinTyp }         PrimitLvl,
        { NnjTyp }         StrTchLvl,
        { OutTyp }         GteTchLvl,
        { RawTyp }         PrimitLvl,
        { RawSTyp }        GteTchLvl,
        { StrTyp }         BioTchLvl,
        { StrSTyp }        GteTchLvl,
        { TrnTyp }         PreWrpLvl,
        { TrnSTyp }        GteTchLvl,
        { RsrTyp }         JmpTchLvl,
        { TerTyp }         WrpTchLvl,
        { TriTyp }         AtomicLvl );

   { minimum tech level required to have a population on a given planet }
   MinTechForClass: ARRAY [WorldClass] OF TechLevel =
      ( { AmbCls }          PreTchLvl,
        { ArdCls }          PrimitLvl,
        { ArtCls }          JmpTchLvl,
        { BarCls }          PreWrpLvl,
        { ClsJ   }          PreTchLvl,
        { ClsK   }          PreTchLvl,
        { ClsL   }          PreTchLvl,
        { ClsM   }          PreTchLvl,
        { DrtCls }          PrimitLvl,
        { EthCls }          PreTchLvl,
        { FstCls }          PreTchLvl,
        { GsGCls }          PreWrpLvl,
        { HLfCls }          PreAtmLvl,
        { IceCls }          PreAtmLvl,
        { JngCls }          PreTchLvl,
        { OcnCls }          PreWrpLvl,
        { ParCls }          PreTchLvl,
        { PsnCls }          AtomicLvl,
        { RnsCls }          PreTchLvl,
        { UndCls }          PrimitLvl,
	{ TerCls }	    WrpTchLvl,
        { VlcCls }          PreAtmLvl );

   { average trillum reserves for a given class }
   TriResByClass: ARRAY [WorldClass] OF Word =
      ( { AmbCls }          200,
        { ArdCls }          350,
        { ArtCls }          50,
        { BarCls }          1200,
        { ClsJ   }          700,
        { ClsK   }          800,
        { ClsL   }          900,
        { ClsM   }          800,
        { DrtCls }          1500,
        { EthCls }          800,
        { FstCls }          500,
        { GsGCls }          450,
        { HLfCls }          500,
        { IceCls }          900,
        { JngCls }          600,
        { OcnCls }          300,
        { ParCls }          600,
        { PsnCls }          350,
        { RnsCls }          200,
        { UndCls }          900,
	{ TerCls }	    800,
        { VlcCls }          1100 );

   { % of industry that is effective by WorldClass (ClsAdj) }
   ClassIndAdj: ARRAY [WorldClass,IndusTypes] OF Integer =
                    { Bio Che Min SYG SYJ SYS SYT Sup Tri }
      ( { AmbCls }  ( 100,100, 75,100,100,100,100,100, 75 ),
        { ArdCls }  ( 100, 80,100,100,100,100,100, 85,100 ),
        { ArtCls }  ( 100, 40, 40,250,200,300,300, 40, 40 ),
        { BarCls }  ( 100, 60,175,100,100,100,100, 40,175 ),
        { ClsJ   }  ( 100,120,120,100,100,100,100,100, 90 ),
        { ClsK   }  ( 100, 90,120,100,100,100,100,100,100 ),
        { ClsL   }  ( 100,100,100,100,100,100,100, 90,120 ),
        { ClsM   }  ( 100,100,100,100,100,100,100,120, 90 ),
        { DrtCls }  ( 100, 60, 80,100,100,100,100, 60,190 ),
        { EthCls }  ( 100,100,100,100,100,100,100,100,100 ),
        { FstCls }  ( 100,120,100,100,100,100,100,145,100 ),
        { GsGCls }  ( 100,150, 50,150,125,175,150, 40, 60 ),
        { HLfCls }  ( 100,100,100,100,100,100,100,100,100 ),
        { IceCls }  ( 100, 90, 80,100,100,100,100, 60, 80 ),
        { JngCls }  ( 100,130,100,100,100,100,100,125,100 ),
        { OcnCls }  ( 100,135, 40,100,100,100,100,130, 40 ),
        { ParCls }  ( 120,150,125,100,100,100,100,200,150 ),
        { PsnCls }  ( 100,200, 80,100,100,100,100, 40, 80 ),
        { RnsCls }  ( 100,100,100,100,100,100,100,100,100 ),
        { UndCls }  ( 100,100,150,100,100,100,100, 70,125 ),
	{ TerCls }  (   1,  1,  1,  1,  1,  1,  1, 10,  1 ),
        { VlcCls }  ( 100,125,175,100,100,100,100, 75,150 ) );

   { %population in military at 50 military index by tech level (in .01%) }
   MilitPer: ARRAY [TechLevel] OF Integer =
      { pt   p  pa   a  pw   w   j   b   s  pg   g }
      (  1, 80,100, 90, 80, 75, 60, 50, 30, 15, 10 );

   { %population in military by WorldTypes. (in .01%) }
   OptMilitary: ARRAY [WorldTypes] of Integer =
      {   a   A   b   B   C   c   i   j   J   m   N   o }
      (   5,100,200,200,200, 10, 80,100,100, 20,150,  0,

      {   r   R   s   S   t   T   U   X   z         }
         20, 20,150,150, 80, 80,  1, 10, 30 );

   { technology development by technological level }
   TechDev: ARRAY [TechLevel] OF TechnologySet =
      (  [sup],                                         { pre-tech   }
         [men,met,sup],                                 { primitive  }
         [men,che..sup],                                { pre-atomic }
         [GDM,men,che..tri],                            { atomic     }
         [GDM,fgt,men,che..tri],                        { pre-warp   }
         [GDM,fgt,trn,men,che..tri],                    { warp       }
         [GDM,ion,fgt,trn,jmp,jtn,men,che..tri],        { jump       }
         [def..ion,fgt..pen,trn,men,amb..tri,out],      { bio-tech   }
         [LAM..trn,men..tri,SRM,cmm,cmp,out],           { starship   }
         [LAM..tri,SRM..out,lnk,dis],                   { pre-gate   }
         [LAM..ter]  );                                 { gate       }

   { Default distribution of defenses by ship type and orbit }
   InitDefenseRecord: DefenseRecord =
                        { fgt hkr jmp jtn pen ssp trn }
      ( ShellDefDist: ( (   5, 50, 10,  0, 15,  0,  0 ),        { deep space }
                        (  10, 10, 20,  0, 30, 50,  0 ),        { high orbit }
                        (  10, 10, 30,  0, 30, 30,  0 ),        { orbit }
                        (  55, 30, 40,  0, 25, 20,  0 ),        { sub-orbit }
                        (  20,  0,  0,100,  0,  0,100 ) ) );    { ground }

   { Number of units of cargo that will fit in a (trn) transport by cargo type. }
   CargoSpace: ARRAY [CargoTypes] OF Integer =
     { men nnj amb che met sup tri }
      (  5,  5,100,  3,  3,  2,100 );

   { cargo capacity of ships relative to transport = 1 }
   TrnAdj: ARRAY [ShipTypes] OF Real =
     { fgt hkr jmp  jtn pen ssp trn   }
      (  0,  0,  0, 0.2,  0,  0,  1 );

   JumpCargoRatio = 5;

   { Units of fuel to move each unit one space by thing type (in .001 units) }
   FuelCons: ARRAY [fgt..tri] OF Integer =
      {  fgt  hkr  jmp  jtn  pen  ssp  trn  men  nnj  amb  che  met  sup  tri }
      (   10,1086, 659, 894, 943,1427, 994,  10,  10,  15,  10,  10,  15,  20 );

   { Max. units of fuel that each ship can hold (in .01 units) }
   FuelCap: ARRAY [ShipTypes] OF Integer =
      {  fgt   hkr   jmp   jtn   pen   ssp   trn }
      (    3,  851,  329, 1341, 1886, 2854, 1988 );

   { Movement rate of fleet by FleetType in Quadrants per year }
   FltMovementRate: ARRAY [FleetTypes] OF Byte =
      ( 1,10,10,2,2 );

   { This is the relative amounts of all things that different
     industries make. }
   ThgAdj: ARRAY [IndusTypes] OF ARRAY [fgt..tri] OF Integer =

   {    fgt hkr jmp jtn pen ssp trn  men nnj amb che met sup tri  }

   (  (   0,  0,  0,  0,  0,  0,  0,   0, 10,175,  0,  0,  0,  0 ),     { Bio }
      (   0,  0,  0,  0,  0,  0,  0,   0,  0,  0,175,  0,  0,  0 ),     { Che }
      (   0,  0,  0,  0,  0,  0,  0,   0,  0,  0,  0,350,  0,  0 ),     { Min }
      (  27,  5,  9,  5,  4,  2, 10,   0,  0,  0,  0,  0,  0,  0 ),     { SYG }
      (   0, 25, 50, 30,  0,  0,  0,   0,  0,  0,  0,  0,  0,  0 ),     { SYJ }
      (   0,  0,  0,  0, 40, 15,  0,   0,  0,  0,  0,  0,  0,  0 ),     { SYS }
      (  75,  0,  0,  0,  0,  0, 45,   0,  0,  0,  0,  0,  0,  0 ),     { SYT }
      (   0,  0,  0,  0,  0,  0,  0,   0,  0,  0,  0,  0,320,  0 ),     { Sup }
      (   0,  0,  0,  0,  0,  0,  0,   0,  0,  0,  0,  0,  0, 75 ) );   { Tri }


    { This is the amount of raw material needed to make 100 units of
      a given thing.  }
    RawM: ARRAY [ResourceTypes] OF ARRAY [CargoTypes] OF Integer =
         { men nnj amb che met sup tri }
      (  (   0,  0,  0,  0,  0,  0,  0 ),       { none   }
         (   0,  0,  0, 25, 20,  0,  5 ),       { LAM    }
         (   0,  0,  0, 30,140,  0, 20 ),       { def    }
         (   0,  0,  0, 10, 20,  0,  2 ),       { GDM    }
         (   0,  0,  0, 25,150,  0, 10 ),       { ion    }
         (   0,  0,  0,  5, 30,  0,  2 ),       { fgt    }
         (   0,  0,  0,100,110,  0, 18 ),       { hkr    }
         (   0,  0,  0, 65, 70,  0, 12 ),       { jmp    }
         (   0,  0,  0,100,110,  0, 16 ),       { jtn    }
         (   0,  0,  0, 95,275,  0, 20 ),       { pen    }
         (   0,  0,  0,175,520,  0, 30 ),       { ssp    }
         (   0,  0,  0, 90,600,  0, 10 ),       { trn    }
         (   0,  0,  0,  0,  0,  0,  0 ),       { men    }
         (   0,  0,100, 50,  0,  0,  0 ),       { nnj    }
         (   0,  0,  0,110,  0,  0,  0 ),       { amb    }
         (   0,  0,  0,  0,  0,  0,  0 ),       { che    }
         (   0,  0,  0,  0,  0,  0,  0 ),       { met    }
         (   0,  0,  0,  0,  0,  0,  0 ),       { sup    }
         (   0,  0,  0,  0,  0,  0,  0 )  );    { tri    }

    { This is the amount of units of metals needed to build 100 points
      of industrial development.  }
    NewIndRawN: ARRAY [IndusTypes] OF Integer =
       {  Bio  Che  Min  SYG  SYJ  SYS  SYT  Sup  Tri }
       (  100, 500, 100,1900,1200,1500,1000,   0, 300 );

   { TypeData is an array that specifies how to distribute the
     industry of a world.  The industries are split into two groups,
     Production Industries (PI) which are Bio, SYG, SYJ, SYS, and SYT, and
     Raw Material Industries (RI) which include Che, Min, and Tri.
     Sup industry is special, and is calculated first according to 
     population and the value in the array.  The value given indicates
     how much extra supplies the world will produce (i.e. AgrTyp will
     produce 10 times as much sup as is needed to support the world.)
     If all the values for PI are 0 (i.e. AgrTyp) then the values for RI
     represent the % of industry that is left (all industry not used for
     sup) that is to be used for that particular industry.
     If one of the values for PI is not 0, then that value indicates the
     maximum setting that should be allocated to that industry.  The
     rest of the industries will be accomodated so that the world is
     balanced. }
   TypeData: ARRAY [WorldTypes] OF IndusRArray =
      {    Bio  Che  Min  SYG  SYJ  SYS  SYT  Sup  Tri }
      (  (   0,  40,  40,   0,   0,   0,   0,10.0,  20 ),   { AgrTyp }
         ( 100, 1.2, 5.0,   0,   0,   0,   0, 1.1, 5.0 ),   { AmbTyp }
         (   0, 1.2, 1.7, 100,   0,   0,   0, 1.1, 1.7 ),   { BseTyp }
         (   0, 0.5, 0.5, 100,   0,   0,   0, 0.5, 0.5 ),   { BseSTyp }
         (   0, 2.0, 2.5, 100,   0,   0,   0, 1.5, 2.0 ),   { CapTyp }
         (   0,  80,  10,   0,   0,   0,   0, 1.1,  10 ),   { CheTyp }
         (   0, 2.0, 2.5, 100,   0,   0,   0, 1.5, 2.0 ),   { IndTyp }
         (   0, 1.2, 1.7,   0, 100,   0,   0, 1.1, 1.7 ),   { JmpTyp }
         (   0, 0.5, 0.5,   0, 100,   0,   0, 0.5, 0.5 ),   { JmpSTyp }
         (   0,  10,  80,   0,   0,   0,   0, 1.1,  10 ),   { MinTyp }
         ( 100, 1.2, 5.0,   0,   0,   0,   0, 1.1, 5.0 ),   { NnjTyp }
         (   0,   0,   0,   0,   0,   0,   0,   0,   0 ),   { OutTyp }
         (   0,  40,  40,   0,   0,   0,   0, 1.1,  20 ),   { RawTyp }
         (   0,  40,  40,   0,   0,   0,   0, 0.5,  20 ),   { RawSTyp }
         (   0, 1.2, 1.3,   0,   0, 100,   0, 1.1, 1.2 ),   { StrTyp }
         (   0, 0.5, 0.5,   0,   0, 100,   0, 0.5, 0.5 ),   { StrSTyp }
         (   0, 1.2, 1.7,   0,   0,   0, 100, 1.1, 1.2 ),   { TrnTyp }
         (   0, 0.5, 0.5,   0,   0,   0, 100, 0.5, 0.5 ),   { TrnSTyp }
         (   0,  10,  10,   0,   0,   0,   0, 1.1,   5 ),   { RsrTyp }
         (   0,  10,  10,   0,   0,   0,   0, 1.1,   5 ),   { TerTyp }
         (   0,  10,  10,   0,   0,   0,   0, 1.1,  80 ) ); { TriTyp }

   PrincipalIndustry: ARRAY [WorldTypes] OF IndusTypes =
      ( { AgrTyp }  SupInd,
        { AmbTyp }  BioInd,
        { BseTyp }  SYGInd,
        { BseSTyp } SYGInd,
        { CapTyp }  SYGInd,
        { CheTyp }  CheInd,
        { IndTyp }  SYGInd,
        { JmpTyp }  SYJInd,
        { JmpSTyp } SYJInd,
        { MinTyp }  MinInd,
        { NnjTyp }  BioInd,
        { OutTyp }  SYGInd,
        { RawTyp }  MinInd,
        { RawSTyp } MinInd,
        { StrTyp }  SYSInd,
        { StrSTyp } SYSInd,
        { TrnTyp }  SYTInd,
        { TrnSTyp } SYTInd,
        { RsrTyp }  MinInd,
        { TerTyp }  MinInd,
        { TriTyp }  TriInd );

   DefaultISSP = $5555;

   MaxISSP = 10;
   NormalISSP = 5;                     { 100% }

   { The ISSP of an industry indicates how much over or under need the
     industry produces.  NOTE: Do not change these values without checking
     with ChangeISSP procedure  (esp. GetISSPLine in DisplayISSP.) }
   ISSP: ARRAY [0..MaxISSP] OF Real =
      (  0.01, 0.10, 0.25, 0.50, 0.75, 1.00, 1.50, 2.00, 3.00, 4.00, 5.00 );

   { number of years to build construction }
   YearsToBuild: ARRAY [ConstrTypes] OF Byte =
      ( { SRM }         2,
        { cmm }         6,
        { frt }        12,
        { cmp }        10,
        { out }         3,
        { gte }        15,
        { lnk }         5,
        { dis }         8 );

   { material needed per year per thing }
   ConsCargoNeeded: ARRAY [ConstrTypes] OF CargoArray =
         { men  nnj  amb  che  met  sup  tri  }
      (  (   0,   0,   0, 110, 500,   0,  80 ),   { SRM }
         (   0,   0,   0, 460,2300,   0, 180 ),   { cmm }
         (   0,   0,   0, 840,2870,   0, 250 ),   { frt }
         (   0,   0,   0, 590,2600,   0, 150 ),   { cmp }
         (   0,   0,   0, 350,1120,   0, 150 ),   { out }
         (   0,   0,   0,2530,3920,   0,1450 ),   { gte }
         (   0,   0,   0,1560,2550,   0, 290 ),   { lnk }
         (   0,   0,   0,1110,1180,   0,1120 ) ); { dis }

   { Optimum defenses for 100 men, 20 billion, and 50% eff. }
   DefAdj: ARRAY [DefnsTypes] OF Integer =
      ( 145,76,215,83 );
   DefBuildRate: ARRAY [DefnsTypes] OF Integer =
      ( 220,50,250,95 );

   FuelPerTon: Integer = 265;
   TriToLaunch: Byte = 0;
   SFuelCons: Byte = 10;  { amount of fuel used by standing fleet =
                            FuelCons/SFuelCons }

   DirX: ARRAY [Directions] OF Integer =
      ( 0,0,1,1,1,0,-1,-1,-1 );
   DirY: ARRAY [Directions] OF Integer =
      ( 0,-1,-1,0,1,1,1,0,-1 );

IMPLEMENTATION

END.
