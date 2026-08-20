(* NEWS.PAS --------------------------------------------------------------------

   ANACREON Reconstruction 4021  v1.0
   News Module

----------------------------------------------------------------------------- *)

UNIT News;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Strg,Types,Galaxy,PrimIntr;

TYPE

   { News Types }
   NewsTypes = ( NoNews,
                 Lack,        { (Loc, material) lacks raw material. }
                 Starv,       { (Loc, 10s of mil) people starve. }
                 NTech,       { (Loc,NewTech) new tech level. }
                 RTech,       { (Loc,NewTech) regressed level. }
                 ConsLack,    { (Loc, material) lacks raw material. }
                 ConsDone,    { (Loc, ConsTyp) construction done. }
                 Rebel,       { (Loc) world rebels. }
                 URebel,      { (Loc, MenKilled) unsuccessfuel rebellion. }
                 RebelW1,     { (Loc) rebellion warning 1 }
                 RebelW2,     { (Loc) rebellion warning 2 }
                 RebelW3,     { (Loc) rebellion warning 3 }
                 RebelW4,     { (Loc) rebellion warning 4 }
                 POk,         { (Loc) probe ok. }
                 NCapTech,    { (Loc, NewTech) empire has new technology. }
                 NCapLvl,     { (Loc, NewTech) empire has new tech level. }
                 BattleL,     { (Loc, Emp) enemy empire conquered. }
                 BattleW1,    { (Loc, Emp) enemy empire destroyed. }
                 BattleW2,    { (Loc, Emp) enemy empire retreated. }
                 WAddict,     { (Loc) world addicted to ambrosia. }
                 UAddict,     { (Loc) world no longer addicted. }
                 AddictDie,   { (Loc,Deaths) addicts die. }
                 RiotsDie,    { (Loc,Deaths) people die from ambrosia riots. }
                 IndDs,       { (Loc,Ind,IndI) industry destroyed. }
                 DInd,        { (Loc) world declares independence. }
                 Join,        { (Loc, Emp) world has joined other empire. }
                 NewCap,      { (Loc) world is now capital. }
                 EndEmp,      { the empire has been conquered. }
                 NoFuel,      { (Loc) fleet is out of fuel. }
                 FltDet,      { (Loc, Emp) world scanned enemy fleet from emp. }
                 Mines,       { (Loc, Emp) enemy fleet took damage in mine field. }
                 MinesDm,     { (Loc, Emp) fleet was damaged by enemy mines. }
                 MinesDs,     { (Loc, Emp) fleet destroyed by enemy mines. } 
                 ConDs,       { (Loc, Emp) construction site destroyed by emp. }
                 GteDs,       { (Loc, Emp) stargate destroyed by emp. }
                 LAMDm,       { (Loc, Emp) fleet damaged by enemy LAMs. }
                 LAMDs,       { (Loc, Emp) fleet destroyed by enemy LAMs. }
                 TrnsShp,     { (Loc, Emp) Emp gave ships/cargo to Loc. }
                 Trns2,       { (Num,Typ) detail line. }
                 NSellTech,   { (Emp, Tch) Emp sells technology. }
                 GInd,        { (Loc, Emp) Emp has given you Loc. }
                 NewPlEmp,    { (Loc, Emp) is a new empire. }
                 DefLack,     { (Loc, material) needs res to build defenses. }
                 IndLack,     { (Loc, material) needs metals to build ind. }
                 PCap,        { (Loc, Emp) probe from emp destroyed at. }
                 PDest,       { (Loc) probe destroyed at loc. }
                 MessR,       { (Emp) message received from emp. }
                 MessI,       { (Loc,Emp) Loc intercepts message from emp. }
                 ConDsUNK,    { (Loc) construction destroyed by unknown. }
                 GteDsUNK,    { (Loc) gate destroyed by unknown. }
                 BattleW2UNK, { (Loc) attacked by unknown. }
                 BattleLUNK,  { (Loc) unknown destroyed fleet. }
                 HLPopKill,   { (Loc,pop) aliens kill population. }
                 HLMenKill,   { (Loc,men,nnj) aliens attack troops. }
                 HLJoin,      { (Loc,nnj) aliens join troops. }
                 LAMDef,      { (Loc,emp) empire attacks with LAMs. }
                 DestDetail,  { (Num,Typ) detail line for destruction. }
                 BseFuel,     { (Loc) is out of fuel. }
                 BseBlocked,  { (Loc) is blocked. }
                 FltBlocked,  { (Loc) is blocked. }
                 NebGate,     { (Loc) can't gate to dense nebula. }
                 SRMClear,    { (Loc,emp) SRM cleared by emp. }
                 BseSD,       { (Loc,emp) base self-destruct by emp. }
                 FltSD,       { (Loc) fleets destroyed in exp. }
                 WHolo,       { (Loc,emp) Loc holocausted by emp. }
                 DthHolo,     { (Deaths) deaths from holocaust. }
                 Disrupt,     { (Loc,emp) fleet stopped by disrupter. }
                 NoTriRes,    { (Loc) Loc has no more trillum. }
                 TriResWarn1, { (Loc) Loc is very low on trillum reserves }
                 TriResWarn2, { (Loc) Loc is low on trillum reserves }
                 MilitRev,    { (Loc) Loc wants troops out }
                 RevControl,  { (Loc) Military on Loc quiets rebellion }
                 GLBDest,     { (Loc,Enemy,Emp) Enemy attacks Emp at Loc. Dest }
                 GLBConq,     { (Loc,Enemy,Emp) Enemy conquers Loc from Emp }
                 GLBCapConq,  { (Loc,Enemy,Emp) Enemy conquers Emp capital. }
                 GLBLAMStrk,  { (Loc,Emp,Enemy) Emp hits Enemy with LAMs. }
                 GLBRev,      { (Loc,Emp) Loc revolts from Emp. }
		 OutProbe,    { (Loc) Loc discovered by outpost. }
                 OrdersSRMClear,{ (Loc) SRMs cleared by fleet(loc) under orders. }
                 OrdersNoSRMs,{ (Loc) SRMs not found for fleet(loc) under orders. }
		 OrdersNoSSP, { (Loc) Ordered SRM sweep by loc failed - not enough Ssp }
                 TerChaos,    { (Loc) Terraforming at Loc failed miserably }
		 TerSuccess,  { (Loc) Terraforming at Loc completed successfully }


                 GTech,
                 CLost,
                 LostP,
                 SMnR,

                 JumpDm,
                 JumpDs,
                 ELost,
                 TriAcc,
                 LostF );

   { News Data Structure }
   NewsRecordPtr = ^NewsRecord;
   NewsRecord = RECORD
      Headline: NewsTypes;             { what the news is about }

      Loc1: Location;                  { data (location) }
      Parm1: Integer;                  { data }
      Parm2: Integer;                  { data }
      Parm3: Integer;                  { data }

      Next: NewsRecordPtr;
   END;  { NewsRecord }

CONST
	LocalNews: SET OF NewsTypes = 
		[Lack,RebelW1,RebelW2,RebelW3,RebelW4,POk,NoFuel,FltDet,
		 DefLack,IndLack,PCap,PDest,BseFuel,BseBlocked,FltBlocked,
		 NebGate,SRMClear,NoTriRes,TriResWarn1,TriResWarn2,MilitRev,
		 RevControl,OutProbe,OrdersSRMClear,OrdersNoSRMs, OrdersNoSSP,
                 TerChaos,TerSuccess];

PROCEDURE AddGlobalNews(Exclude: EmpireSet; Source: IDNumber;
                        Head: NewsTypes; Loc: Location;
                        Parm1,Parm2,Parm3: Word);
PROCEDURE AddNews(Player: Empire;
                  Head: NewsTypes;
                  Loc: Location;
                  P1,P2,P3: Integer);
PROCEDURE EraseNews(Player: Empire);
PROCEDURE GetNewsItem(Item: NewsRecordPtr; VAR Head: NewsTypes;
                      VAR Loc: Location; VAR P1,P2,P3: Integer);
PROCEDURE GetNewsList(Emp: Empire; VAR List: NewsRecordPtr);
FUNCTION LoadNewsData(VAR SF: FILE): Word;
FUNCTION SaveNewsData(VAR SF: FILE): Word;

IMPLEMENTATION

USES
   Dos2;

TYPE
   NewsDataRecord = RECORD
      FirstItem: NewsRecordPtr;
      LastItem: NewsRecordPtr;
   END; { NewsDataRecord }

VAR
   NewsData: ARRAY [Empire1..Empire8] OF NewsDataRecord;

PROCEDURE GetOpenSlot(Player: Empire; VAR NewSlot: NewsRecordPtr);
{ GetOpenSlot:
   This procedure returns a pointer to a news record that has been allocated
   and added to the news list for the given empire.  If no more room on the
   heap is available, then NewSlot is Nil. }

   BEGIN
   WITH NewsData[Player] DO
      BEGIN
      IF MaxAvail>20 THEN
         BEGIN
         New(NewSlot);

         IF FirstItem=Nil THEN
            FirstItem:=NewSlot
         ELSE
            LastItem^.Next:=NewSlot;

         LastItem:=NewSlot;
         NewSlot^.Next:=Nil;
         END
      ELSE
         NewSlot:=Nil;
      END;  { with scope }
   END;  { GetOpenSlot }

PROCEDURE GetNewsList(Emp: Empire; VAR List: NewsRecordPtr);
   BEGIN
   List:=NewsData[Emp].FirstItem;
   END;  { GetNewsList }

PROCEDURE GetNewsItem(Item: NewsRecordPtr; VAR Head: NewsTypes;
                      VAR Loc: Location; VAR P1,P2,P3: Integer);
{ GetNewsItem:
   This procedure returns the news data associated with the given Item.
   No checks are made to see if the data is defined. }

   BEGIN
   WITH Item^ DO
      BEGIN
      Head:=Headline;
      Loc:=Loc1;
      P1:=Parm1;
      P2:=Parm2;
      P3:=Parm3;
      END;  { with scope }
   END;  { GetNewsItem }

PROCEDURE AddNews(Player: Empire; 
                  Head: NewsTypes; Loc: Location; P1,P2,P3: Integer);

   VAR
      NewSlot: NewsRecordPtr;

   { AddNews: MAIN PROCEDURE }
   BEGIN
   IF (Player<>Indep) AND (EmpireActive(Player)) THEN
      BEGIN
      GetOpenSlot(Player,NewSlot);
      IF NewSlot<>Nil THEN
         WITH NewSlot^ DO
            BEGIN
            Headline:=Head;
            Loc1:=Loc;
            Parm1:=P1;
            Parm2:=P2;
            Parm3:=P3;
            END;  { with scope }
      END;  { if }
   END;  { AddNews }

PROCEDURE AddGlobalNews(Exclude: EmpireSet; Source: IDNumber;
                        Head: NewsTypes; Loc: Location;
                        Parm1,Parm2,Parm3: Word);
{ AddGlobalNews: ---------------------------------------------------------------
   Sends news item to all empire not in Exclude and which have scouted Source.
------------------------------------------------------------------------------ }
   VAR
      Emp: Empire;

   BEGIN
   FOR Emp:=Empire1 TO Empire8 DO
      IF (NOT (Emp IN Exclude)) AND EmpireActive(Emp) AND Scouted(Emp,Source) THEN
         AddNews(Emp,Head,Loc,Parm1,Parm2,Parm3);
   END;  { AddGlobalNews }

PROCEDURE EraseNews(Player: Empire);
{ EraseNews: }
   VAR
      Delete,NextToDelete: NewsRecordPtr;

   BEGIN
   WITH NewsData[Player] DO
      BEGIN
      Delete:=FirstItem;
      FirstItem:=Nil;
      LastItem:=Nil;
      END;  { with scope }

   WHILE Delete<>Nil DO
      BEGIN
      NextToDelete:=Delete^.Next;
      Dispose(Delete);
      Delete:=NextToDelete;
      END;
   END;  { EraseNews }

FUNCTION LoadNewsData(VAR SF: FILE): Word;
{ LoadNewsData:
   This procedure loads previously saved news data.  It expects the given file
   to be opened and correctly positioned.  The data is read in the following
   format:

      - Data for Empire1
         NoOfItems (Word)
         1st News Record (NewsRecord)
         2nd News Record (NewsRecord)
                .
                .
                .
      - Data for Empire2
         NoOfItems (Word)
                .
                .
                .
      - Data for Empire3
                .
                .
                .

      }

   VAR
      Emp: Empire;
      Error,NoOfItems,i: Word;
      NextNews: NewsRecordPtr;

   BEGIN
   FOR Emp:=Empire1 TO Empire8 DO
      WITH NewsData[Emp] DO
         BEGIN
         Error:=ReadVariable(SF,NoOfItems,2);

         IF (Error=0) AND (NoOfItems>0) THEN
            BEGIN
            New(FirstItem);
            NextNews:=FirstItem;

            FOR i:=1 TO NoOfItems DO
               BEGIN
               Error:=ReadVariable(SF,NextNews^,SizeOf(NewsRecord));

               IF i<NoOfItems THEN
                  BEGIN
                  New(NextNews^.Next);
                  NextNews:=NextNews^.Next;
                  END;
               END;

            LastItem:=NextNews;
            NextNews^.Next:=Nil;
            END
         ELSE
            BEGIN
            FirstItem:=Nil;
            LastItem:=Nil;
            END;
         END;  { loop and with scope }

   LoadNewsData:=Error;
   END;  { LoadNewsData }

FUNCTION SaveNewsData(VAR SF: FILE): Word;
   VAR
      Error,i,NoOfItems: Word;
      Emp: Empire;
      NextNews: NewsRecordPtr;

   BEGIN
   FOR Emp:=Empire1 TO Empire8 DO
      WITH NewsData[Emp] DO
         BEGIN
         NextNews:=FirstItem;
         NoOfItems:=0;

         WHILE NextNews<>Nil DO
            BEGIN
            NoOfItems:=NoOfItems+1;
            NextNews:=NextNews^.Next;
            END;

         Error:=WriteVariable(SF,NoOfItems,2);

         NextNews:=FirstItem;

         WHILE NextNews<>Nil DO
            BEGIN
            Error:=WriteVariable(SF,NextNews^,SizeOf(NewsRecord));
            NextNews:=NextNews^.Next;
            END;
         END;  { loop and with scope }

   SaveNewsData:=Error;
   END; { SaveNewsData }

BEGIN
FillChar(NewsData,SizeOf(NewsData),0);
END.
