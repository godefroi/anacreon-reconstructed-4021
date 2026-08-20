(* PROLOG.PAS ------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT Prolog;

{$IFDEF Overlay}
{$F+}
{$O+}
{$ENDIF}

INTERFACE

USES Strg,                                      { String primitives. }
     System2,
     DOS2,
     Int,                                       { Integer procedures. }
     Crt,                                       { Standard CRT Unit }
     EIO,                                       { Extended IO }
     WND,
     Menu,
     PullDown,
     Environ,
     Types,
     Galaxy,
     DataStrc,
     DataCnst,
     Misc,                                      { Miscellaneous proceduers }
     Primintr,                                  { Primitive interface. }
     Intrface,                                  { Interface procedures. }
     News,
     NewGame,                                   { Start new game }
     Mess,
     TMA,
     LoadSave;

PROCEDURE Prologue(VAR Player: Empire; VAR EndGame: Boolean);
PROCEDURE SetUpPlayer(Player: Empire; VAR ExitGame: Boolean);
PROCEDURE DoNotSaveGame;

(* -------------------------------------------------------------------------- *)

IMPLEMENTATION

USES
	HlpWind;

CONST
   GameLoaded: Boolean = False;
   GameModified: Boolean = False;
   FirstRun: Boolean = True;

TYPE
   StarRecord = RECORD
      Pos: Integer;
      Chr: Char;
      Cyc: Byte;
   END;
   StarArray = ARRAY [1..12] OF StarRecord;

CONST

(* ANACREON Title Page ------------------------------------------------------ *)
   TitleAnacreon: ARRAY [1..7] OF LineStr =
      ('        Û                                                                ',
       '       ÛÛÛ                                                               ',
       '      Û ßÛÛ                                                              ',
       '     Û   ßÛÛ      ßÛÛÜßßÛÜ  ÜßßßÛÜ  ÜÛßßßÜ ÛÛÜßß ÜÛßßßÜ  ÜÛßßÛÜ ßÛÛÜßßÛÜ ',
       '    ÛÜÜÜÜÜÛÛÛ      ÛÛ   ÛÛ    ÜÜÛÛ ÛÛ      ÛÛ   ÛÛÜÜÜÜß ÛÛ    ÛÛ ÛÛ   ÛÛ ',
       '   Û       ßÛÛ     ÛÛ   ÛÛ  ÜÛß ÛÛ ÛÛ      ÛÛ   ÛÛ      ÛÛ    ÛÛ ÛÛ   ÛÛ ',
       'ÜÜÛÜ      ÜÜÛÛÛÜÜ ÜÛÛÜ  ÛÛÜ ßÛÜÛßÛÜ ßÛÜÜÜß ÛÛ    ßÛÜÜÜß  ßÛÜÜÛß ÜÛÛÜ  ÛÛÜ' );

   LetterEnd: ARRAY [0..8] OF Byte =
      ( 0,18,28,35,43,48,56,64,73 );

   Cycle: ARRAY [1..28] OF Integer =
      (-160,-158,-158,-156,-154,  +4,  +4,
         +4,  +4,+166,+164,+162,+162,+160,
       +160,+158,+158,+156,+154,  -4,  -4,
         -4,  -4,-166,-164,-162,-162,-160 );

   InitCycle: ARRAY [1..28] OF Integer =
      ( -22,-182,-340,-498,-654,-808,-804,
       -800,-796,-792,-626,-462,-300,-138,
        +22,+182,+340,+498,+654,+808,+804,
       +800,+796,+792,+626,+462,+300,+138);

   ChrCycle: ARRAY [0..3] OF Char =
      'ùoo';

{$I BITPIC.INC}

PROCEDURE DisplayBitPicture(VAR Pic; PicLen: Word; y,Col: Word);
   CONST
      Bit: ARRAY [0..7] OF Byte =
         ( 1,2,4,8,16,32,64,128 );
   VAR
      BitPicture: ARRAY [1..25] OF BitLine ABSOLUTE Pic;
      LinePicture: ARRAY [1..25] OF LineStr;
      i,j,k: Word;

   BEGIN
   FOR i:=1 TO PicLen DO
      BEGIN
      LinePicture[i]:='';
      AdjustString(LinePicture[i],80);
      FOR j:=0 TO 9 DO
         BEGIN
         FOR k:=0 TO 7 DO
            BEGIN
            IF (Bit[k] AND BitPicture[i][j])>0 THEN
               LinePicture[i][j*8+k+1]:='Û'
            END;
         END;
      END;

   FOR i:=1 TO PicLen DO
      BEGIN
      WriteString(LinePicture[i],1,y-1+i,Col);
      END;
   END;  { DisplayBitPicture }

PROCEDURE InitStarArray(VAR Star: StarArray);
   VAR
      i: Byte;
      Cent: Integer;

   BEGIN
   Cent:=160*12+80;
   FOR i:=1 TO 7 DO
      WITH Star[i] DO
         BEGIN
         Cyc:=Rnd((i-1)*4+1,(i-1)*4+4);
         Pos:=Cent+InitCycle[Cyc];
         Chr:=Char(15);
         END;
   END;  { InitStarArray }

PROCEDURE UpdateStarArray(VAR Star: StarArray);
   VAR
      i: Byte;
      Pos2: Integer;
      Ch2: Char;

   BEGIN
   FOR i:=1 TO 7 DO
      WITH Star[i] DO
         BEGIN
         Pos2:=Pos;
         Ch2:=Chr;

         Inline(
                                  {; This routine will erase the character at Pos if it is Chr.}
           $1E                    {        PUSH DS}
           /$A1/>SCRSEG           {        MOV AX,[>ScrSeg]}
           /$8A/$1E/>CHECKSNOW    {        MOV BL,[<WaitForRetrace]}
           /$8E/$D8               {        MOV DS,AX}
           /$8B/$86/>POS2         {        MOV AX,[BP+>Pos]}
           /$89/$C7               {        MOV DI,AX}
           /$8A/$8E/>CH2          {        MOV CL,[BP+>Chr]}
           /$D0/$DB               {        RCR BL,1                ;If WaitForRetrace is False...}
           /$73/$1D               {        JNC Mono                ; use Mono routine.}
                                  {; COLOR ROUTINE}
           /$BA/$DA/$03           {        MOV DX,$03DA            ;status port}
           /$B4/$09               {        MOV AH,$09}
           /$FA                   {        CLI}
           /$EC                   {Wait1:  IN AL,DX}
           /$D0/$D8               {        RCR AL,1}
           /$72/$FB               {        JC Wait1}
           /$EC                   {Wait2:  IN AL,DX}
           /$20/$E0               {        AND AL,AH}
           /$74/$FB               {        JZ Wait2}
           /$FB                   {        STI}
           /$8A/$05               {        MOV AL,[DI]}
           /$38/$C8               {        CMP AL,CL}
           /$75/$0F               {        JNE End1}
           /$C6/$05/$20           {        MOV BYTE PTR [DI],32}
           /$E9/$09/$00           {        JMP End1}
                                  {; MONO ROUTINE}
           /$8A/$05               {Mono:   MOV AL,[DI]}
           /$38/$C8               {        CMP AL,CL}
           /$75/$03               {        JNE End1}
           /$C6/$05/$20           {        MOV BYTE PTR [DI],32}
           /$1F                   {End1:   POP DS}
         );

         Pos:=Pos+Cycle[Cyc];
         Pos2:=Pos;
         IF Cyc=28 THEN
            Cyc:=1
         ELSE
            Cyc:=Cyc+1;

         Chr:=ChrCycle[(Cyc-1) MOD 4];
         Ch2:=Chr;

         Inline(
                                  {; This routine will draw character Ch2 at Pos2 if Pos2 is blank.}
           $1E                    {        PUSH DS}
           /$A1/>SCRSEG           {        MOV AX,[>ScrSeg]}
           /$8A/$1E/>CHECKSNOW    {        MOV BL,[<WaitForRetrace]}
           /$8E/$D8               {        MOV DS,AX}
           /$8B/$86/>POS2         {        MOV AX,[BP+>Pos2]}
           /$89/$C7               {        MOV DI,AX}
           /$8A/$8E/>CH2          {        MOV CL,[BP+>Ch2]}
           /$B5/$0F               {        MOV CH,$0F              ;bright attribute}
           /$D0/$DB               {        RCR BL,1}
           /$73/$1C               {        JNC Mono}
                                  {; COLOR ROUTINE}
           /$BA/$DA/$03           {        MOV DX,$03DA            ;status port}
           /$B4/$09               {        MOV AH,$09}
           /$FA                   {        CLI}
           /$EC                   {Wait1:  IN AL,DX}
           /$D0/$D8               {        RCR AL,1}
           /$72/$FB               {        JC Wait1}
           /$EC                   {Wait2:  IN AL,DX}
           /$20/$E0               {        AND AL,AH}
           /$74/$FB               {        JZ Wait2}
           /$FB                   {        STI}
           /$8A/$05               {        MOV AL,[DI]}
           /$3C/$20               {        CMP AL,32}
           /$75/$0D               {        JNE End1}
           /$89/$0D               {        MOV [DI],CX}
           /$E9/$08/$00           {        JMP End1}
                                  {; MONO ROUTINE}
           /$8A/$05               {Mono:   MOV AL,[DI]}
           /$3C/$20               {        CMP AL,32}
           /$75/$02               {        JNE End1}
           /$89/$0D               {        MOV [DI],CX}
           /$1F                   {End1:   POP DS}
         );
         END;
   END;  { UpdateStarArray }

PROCEDURE Simple(x,y,Col: Byte);
   VAR
      i: Byte;

   BEGIN
   FOR i:=1 TO 7 DO
      BEGIN
      WriteString(TitleAnacreon[i],x,y-1+i,Col);
      END;
   END;  { Simple }

PROCEDURE Materialize(x,y,Col: Word);
   VAR
      i,RndX,RndY: Word;

   BEGIN
   FOR i:=1 TO 1000 DO
      BEGIN
      RndX:=Rnd(1,75);
      RndY:=Rnd(1,7);
      WriteString(TitleAnacreon[RndY][RndX],RndX+x-1,RndY+y-1,Col);
      Delay(1);
      END;

   Simple(x,y,Col);
   END;  { Materialize }

PROCEDURE Cascade(x,y,Col: Byte);
   VAR
      i,j: Byte;

   BEGIN
   FOR i:=7 DOWNTO 1 DO
      FOR j:=2 TO y+i-1 DO
         BEGIN
         WriteBlanks(75,x,j-1,Col);
         WriteString(TitleAnacreon[i],x,j,Col);
         Delay(i*3);
         END;
   END;  { Cascade }

PROCEDURE Expand(x,y,Col: Byte);
   VAR
      MidY: Byte;

   BEGIN
   MidY:=y+3;

   WriteString(TitleAnacreon[4],x,MidY,Col);
   Delay(100);

   WriteString(TitleAnacreon[1],x,MidY-1,Col);
   WriteString(TitleAnacreon[7],x,MidY+1,Col);
   Delay(100);

   WriteString(TitleAnacreon[1],x,MidY-2,Col);
   WriteString(TitleAnacreon[2],x,MidY-1,Col);
   WriteString(TitleAnacreon[6],x,MidY+1,Col);
   WriteString(TitleAnacreon[7],x,MidY+2,Col);
   Delay(100);

   WriteString(TitleAnacreon[1],x,MidY-3,Col);
   WriteString(TitleAnacreon[2],x,MidY-2,Col);
   WriteString(TitleAnacreon[3],x,MidY-1,Col);
   WriteString(TitleAnacreon[5],x,MidY+1,Col);
   WriteString(TitleAnacreon[6],x,MidY+2,Col);
   WriteString(TitleAnacreon[7],x,MidY+3,Col);
   END;  { Expand }

PROCEDURE LettersSFX(x,y,Col: Word);
   CONST
      Sequence: ARRAY [0..7] OF Byte =
         ( 3,1,5,8,2,4,7,6 );
   VAR
      i,j: Word;
      BeginSeq,Letter,Start,Len: Word;

   BEGIN
   BeginSeq:=Rnd(0,7);
   FOR i:=1 TO 8 DO
      BEGIN
      Letter:=Sequence[(BeginSeq+i-1) MOD 8];
      FOR j:=1 TO 7 DO
         BEGIN
         Start:=LetterEnd[Letter-1]+1;
         Len:=(LetterEnd[Letter]-Start)+1;
         WriteString(Copy(TitleAnacreon[j],Start,Len),x+Start-1,y+j-1,White);
         END;
      Delay(100);
      FOR j:=1 TO 7 DO
         BEGIN
         Start:=LetterEnd[Letter-1]+1;
         Len:=(LetterEnd[Letter]-Start)+1;
         WriteString(Copy(TitleAnacreon[j],Start,Len),x+Start-1,y+j-1,Col);
         END;
      END;
   END;  { LettersSFX }

PROCEDURE ZoomOutSFX;
   VAR
      i,j: Word;

   BEGIN
   ClrScr;
   DisplayBitPicture(AnacreonSize4,22,2,C.Title1);
   Delay(25);
   ClrScr;
   DisplayBitPicture(AnacreonSize3,12,7,C.Title1);
   Delay(50);
   ClrScr;
   DisplayBitPicture(AnacreonSize2,7,10,C.Title1);
   Delay(75);
   ClrScr;
   DisplayBitPicture(AnacreonSize1,8,9,C.Title1);
   Delay(100);
   ClrScr;
   Simple(4,9,C.Title1);

   FOR i:=1 TO Length(TitleAnacreon[1]) DO
      BEGIN
      FOR j:=1 TO 7 DO
         WriteString(Copy(TitleAnacreon[j],i,3),3+i,8+j,White);

      Delay(15);

      FOR j:=1 TO 7 DO
         WriteString(Copy(TitleAnacreon[j],i,3),3+i,8+j,C.Title1);
      END;
   END;  { ZoomOutSFX }

PROCEDURE MainTitle;
   BEGIN
   ClrScr;

   IF FirstRun THEN
      BEGIN
      ZoomOutSFX;
      FirstRun:=False;
      END
   ELSE
      BEGIN
      CASE Rnd(1,10) OF
             1: Materialize(4,9,C.Title1);
          2..3: Cascade(4,9,C.Title1);
          4..5: ZoomOutSFX;
          6..8: LettersSFX(4,9,C.Title1);
             9: Expand(4,9,C.Title1);
            10: Simple(4,9,C.Title1);
         END;  { case }
      END;

   WriteString('Reconstruction 4021   v'+Version,30,17,C.Title1);
   WriteString('(c) Copyright 1990 by T M A   All Rights Reserved',15,18,C.Title1);
   END;  { MainTitle }

(* SetUp --------------------------------------------------------------------- *)

PROCEDURE DoNotSaveGame;
	BEGIN
	GameModified:=False;
	END;  { DoNotSaveGame }

PROCEDURE SetUpPlayer(Player: Empire; VAR ExitGame: Boolean);
   VAR
      LastTurn,Dummy,ESCHit: Boolean;
      Error: Word;
      SetUpWindow: WindowHandle;
      Bar: MenuBar;

   PROCEDURE GetNewsItem(Item: NewsRecordPtr; VAR Head: NewsTypes;
                         VAR Loc: Location; VAR P1,P2,P3: Integer);
   { INTERFACE PROCEDURE: GetNewsItem:
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

   PROCEDURE GetPassword(Player: Empire; var ESCHit: Boolean);
   { GetPassword:
      Asks player for a password.  If the password does not match, then it
      will ask for a password twice more.  If still not right, it will ask
      for the password of all player empires until all correctly enter
      their passwords.  }

      VAR
         Pass,CorrectPassword: String8;
         Ok: Boolean;
         PasW: WindowHandle;

      { GetPassword: MAIN PROCEDURE }
      BEGIN
      CorrectPassword:=Universe^.EmpireData[Player].Pass;
      Ok:=False;
      OpenWindow(15,12,50,7,ThinBRD,'Password',C.CommWind,C.SYSWBorder,PasW);
      REPEAT
         Write('Password : ');
         EmptyKeyBuffer;
         InputPassword(Pass,ESCHit);
         IF NOT ESCHit THEN
            BEGIN
            IF Pass=CorrectPassword THEN
               Ok:=True
            ELSE
               BEGIN
               WriteLn;
               WriteLn('No, that''s not it.  Try again.');
               END;
            END;
      UNTIL ESCHit OR Ok;
      CloseWindow;
      END;  { GetPassword }

   PROCEDURE EmpireNews(Player: Empire; VAR LastTurn: Boolean);
      VAR
         CapID: IDNumber;
         OtherEmp: Empire;

      BEGIN
      GetCapital(Player,CapID);
      IF CapID.ObjTyp=Void THEN
         BEGIN
         ClrScr;
         OtherEmp:=Empire(CapID.Index);
         WriteString(MyLord(Player)+',',1,1,C.SYSDispWind);

         WriteString('I regret that I must communicate the dreadful news in this impersonal way,',1,3,C.SYSDispWind);
         WriteString('but by the time you read this I will most likely be either dead or',1,4,C.SYSDispWind);
         WriteString('imprisoned.  While you slept peacefully, our capital was attacked by the',1,5,C.SYSDispWind);
         WriteString(EmpireName(OtherEmp)+' Empire.  Though our men and women fought bravely,',1,6,C.SYSDispWind);
         WriteString('the strength of our adversary overwhelmed us and we were forced to surrender.',1,7,C.SYSDispWind);

         WriteString('I have arranged an honorable course of action for Your Majesty; you will find',1,9,C.SYSDispWind);
         WriteString('necessary materials by your bedside.  Good luck, '+MyLord(Player)+'.',1,10,C.SYSDispWind);

         WriteString('- Your Loyal Servant',40,12,C.SYSDispWind);
         PressAnyKey(1,22,'Press any key to continue...');
         ClrScr;
         LastTurn:=True;

			IF (EmpireActive(Player)) THEN
	         DestroyEmpire(Player);
         END
      ELSE
         LastTurn:=False;
      END;  { EmpireNews }

   PROCEDURE EmpireStatus(Player: Empire);
      VAR
         Col,i: Integer;
         TotalEff,TotalPop,TotalInd: Real;
         TotalWorlds: Integer;
         TotalShips: ARRAY [ShipTypes] OF Real;
         Ships: ShipArray;
         ID: IDNumber;
         ShpI: ShipTypes;
         SetOfFleets: FleetSet;
         AvrgEff: Index;
         AvrgInd: Integer;
         TechSet,TempTechSet: TechnologySet;
         Tech: TechLevel;
         TchI: TechnologyTypes;
         TempStr: LineStr;
         FoundStr: String8;

      BEGIN
      TotalWorlds:=0;   TotalPop:=0;   TotalEff:=0;   TotalInd:=0;
      FillChar(TotalShips,SizeOf(TotalShips),0);

      { add up totals }
      ID.ObjTyp:=Pln;
      FOR i:=1 TO NoOfPlanets DO
         IF i IN SetOfPlanetsOf[Player] THEN
            BEGIN
            ID.Index:=i;
            TotalWorlds:=TotalWorlds+1;
            TotalPop:=TotalPop+GetPopulation(ID);
            TotalEff:=TotalEff+GetEfficiency(ID);
            TotalInd:=TotalInd+TotalProd(GetPopulation(ID),GetTech(ID));

            GetShips(ID,Ships);
            FOR ShpI:=fgt TO trn DO
               TotalShips[ShpI]:=TotalShips[ShpI]+Ships[ShpI];
            END;

      ID.ObjTyp:=Base;
      FOR i:=1 TO MaxNoOfStarbases DO
         IF i IN SetOfStarbasesOf[Player] THEN
            BEGIN
            ID.Index:=i;
            TotalWorlds:=TotalWorlds+1;
            TotalPop:=TotalPop+GetPopulation(ID);
            TotalEff:=TotalEff+GetEfficiency(ID);
            TotalInd:=TotalInd+TotalProd(GetPopulation(ID),GetTech(ID));

            GetShips(ID,Ships);
            FOR ShpI:=fgt TO trn DO
               TotalShips[ShpI]:=TotalShips[ShpI]+Ships[ShpI];
            END;

      SetOfFleets:=SetOfFleetsOf[Player] * SetOfActiveFleets;
      ID.ObjTyp:=Flt;
      FOR i:=1 TO MaxNoOfFleets DO
         IF i IN SetOfFleets THEN
            BEGIN
            ID.Index:=i;
            GetShips(ID,Ships);
            FOR ShpI:=fgt TO trn DO
               TotalShips[ShpI]:=TotalShips[ShpI]+Ships[ShpI];
            END;

      { get other data }
      GetEmpireTechnology(Player,Tech,TechSet);
      IF TotalWorlds>0 THEN
         BEGIN
         AvrgEff:=Round(TotalEff/TotalWorlds);
         AvrgInd:=Round(TotalInd/TotalWorlds);
         END
      ELSE
         BEGIN
         AvrgEff:=0;
         AvrgInd:=0;
         END;

      Str(EmpireAge(Player)+1,FoundStr);
      FoundStr:=FoundStr+OrdinalString(EmpireAge(Player)+1);
      { display data }
      ClrScr;

      Writeln(EmpireName(Player),' Empire Status Report      ',Year);
      Writeln;
      WriteLn('In the year of our Lord ',Year,', the ',FoundStr,' year of your reign, the empire of ');
      Write(EmpireName(Player),' consists of ',TotalWorlds,' world');
      IF TotalWorlds>1 THEN
         Write('s');
      WriteLn(' with a total population of ',(TotalPop/100):5:1,' billion.');
      WriteLn('The average industrial production is ',AvrgInd,' and the average efficiency is ',AvrgEff,'%.');
      WriteLn;
      TempTechSet:=TechSet-TechDev[Pred(Tech)];
      IF TempTechSet=[] THEN
         BEGIN
         Writeln('None of the potential technologies of the ',TechN[Tech],' level have been ');
         Writeln('developed.');
         END
      ELSE
         BEGIN
         Writeln('The empire has mastered the following technologies:');
         Col:=1;
         FOR TchI:=LAM TO ter DO
            IF TchI IN TempTechSet THEN
               BEGIN
               TempStr:='     '+TechnologyName[TchI];
               IF Col+Length(TempStr)>=77 THEN
                  BEGIN
                  Writeln;
                  Col:=1;
                  END;
               Write(TempStr);
               Col:=Col+Length(TempStr);
               END;
         Writeln;
         END;

      Writeln;
      Writeln('The military force of the empire consists of the following:');
      Writeln;
      FOR ShpI:=fgt TO trn DO
         BEGIN
         Writeln(ThingNames[ShpI]:24,': ',TotalShips[ShpI]:6:0);
         END;

      PressAnyKey(1,22,'Press any key to continue...');
      END;  { EmpireStatus }

   PROCEDURE DisplayIntroScreen;
      VAR
         EmpN: String32;
         YearN: String8;

      BEGIN
      ClrScr;
      Str(Year,YearN);
      EmpN:=EmpireName(Player);

      WriteString(' '+EmpN+': '+YearN,1,2,C.SYSDispWind);

      CASE Rnd(1,3) OF
         1: BEGIN
            WriteString(' Welcome, '+MyLord(Player)+', I trust your sleep was peaceful and untroubled by',1,4,C.SYSDispWind);
            WriteString(' the events of the year.',1,5,C.SYSDispWind);
            END;
         2: BEGIN
            WriteString(' Welcome, '+MyLord(Player)+', Your humble servant awaits your instructions.',1,4,C.SYSDispWind);
            END;
         3: BEGIN
            WriteString(' Greetings, '+MyLord(Player)+', I hope your sleep was pleasant and peaceful.',1,4,C.SYSDispWind);
            END;
      END;  { case }
      END;  { DisplayIntroScreen }

   { SetUpPlayer: MAIN PROCEDURE }
   BEGIN
   OpenWindow(1,1,80,24,ThinBRD,'',C.SYSDispWind,C.SYSWBorder,SetUpWindow);
   ESCHit:=false;

   DisplayIntroScreen;
   WriteHelpLine('<Esc>:Menu');
   GetPassword(Player,ESCHit);

   IF NOT ESCHit THEN
      BEGIN
      EmpireNews(Player,LastTurn);
      IF NOT LastTurn THEN
         BEGIN
         EmpireStatus(Player);
         END;
      ExitGame:=False;
      END
   ELSE
      BEGIN
      ExitGame:=True;
      END;

	CloseWindow;
   END;  { SetUpPlayer }

(* -------------------------------------------------------------------------- *)

PROCEDURE UpdateMenuBar(VAR Bar: MenuBar);
   BEGIN
   IF AutoSave THEN
      ChangeMenuLine(Bar,3,4,'Auto backup            ON')
   ELSE
      ChangeMenuLine(Bar,3,4,'Auto Backup           OFF');

   IF PauseActive THEN
      ChangeMenuLine(Bar,3,5,'Pause              Active')
   ELSE
      ChangeMenuLine(Bar,3,5,'Pause            Inactive');

   IF AsyncTurns THEN
      ChangeMenuLine(Bar,3,6,'Sequential play       OFF')
   ELSE
      ChangeMenuLine(Bar,3,6,'Sequential play        ON');

   IF UseColor THEN
      ChangeMenuLine(Bar,4,1,'Mono/color        Color')
   ELSE
      ChangeMenuLine(Bar,4,1,'Mono/color         Mono');
   END;  { UpdateMenuBar }

PROCEDURE SaveTheGame(VAR GameLoaded,GameModified: Boolean);
   VAR
      Filename: LineStr;
      Null,Error: Word;
      Dummy: Boolean;
      SaveWind: WindowHandle;
      BakFile: FILE;
      FullFilename: LineStr;

   BEGIN
   IF (NOT GameLoaded) THEN
      BEGIN
      Dummy:=False;
      AttentionWindow('You must Load a game first.','',Dummy);
      END
   ELSE
      BEGIN
      OpenWindow(1,2,80,23,ThinBRD,'',C.SYSDispWind,C.SYSWBorder,SaveWind);
      REPEAT
         WriteHelpLine('<Esc>:Exit');
         Filename:=CurrentGame;
         InputString('Filename to save to : ',1,2,C.SYSDispWind,55,Filename);
         AllUpCase(Filename);
         IF Filename<>ESCKey THEN
            BEGIN
            FullFilename:=AddDefaultPath(SavDirect,FNameWithoutExt(Filename)+'.BAK');
            Error:=SaveGame(FullFilename);
            IF Error<>0 THEN
               BEGIN
               DOSErrorWindow(Error,FullFilename)
               END
            ELSE
               BEGIN
               CurrentGame:=Filename;
               GameModified:=False;

               { delete previous file }
               Assign(BakFile,AddDefaultPath(SavDirect,Filename));
               {$I-} 
               Erase(BakFile); 
               {$I+}
               Null:=IOResult;

               { rename file }
               Assign(BakFile,FullFilename);
               {$I-}
               Rename(BakFile,AddDefaultPath(SavDirect,Filename));
               {$I+}
               Error:=IOResult;
               IF Error<>0 THEN
                  DOSErrorWindow(Error,FullFilename);
               END;
            END
         ELSE
            BEGIN
            Error:=0;
            END;
      UNTIL Error=0;

      CloseWindow;
      END;
   END;  { SaveTheGame }

PROCEDURE GameNotSaved(VAR GameLoaded,GameModified: Boolean);
   VAR
      Abort: Boolean;

   BEGIN
   IF GameLoaded AND GameModified THEN
      BEGIN
      Abort:=True;
      AttentionWindow('This game is not saved.  Do you want to save it?',
                      'Press <Esc> to lose changes.',Abort);
      IF NOT Abort THEN
         SaveTheGame(GameLoaded,GameModified)
      ELSE
         GameModified:=False;
      END;
   END;  { GameNotSaved }

PROCEDURE ContinueOldGame(VAR Bar: MenuBar; VAR GameLoaded,GameModified: Boolean);
{ LoadScenario:
   This procedure allows the player to choose a saved game to play.  If
   he does not choose any of the ones offered, then Abort will be true upon exit.
   Otherwise, the game will be loaded. }

   var
      TextFile,Filename: FilenameStr;
      FullFilename: LineStr;
      Abort,Dummy,ExitMenu: Boolean;
      Error: Word;
      WindH: WindowHandle;
      Temp: String32;

   { ContinueOldGame: MAIN PROCEDURE }
   BEGIN
   GameNotSaved(GameLoaded,GameModified);
   IF (NOT GameLoaded) OR (NOT GameModified) THEN
      BEGIN
      IF GameLoaded THEN
         BEGIN
         CleanUpUniverse;
         GameLoaded:=False;
         GameModified:=False;
         END;

      Abort:=False;
      InitializeUniverse(0,0,0);

      REPEAT
         SelectFile(SavDirect,'*.*',FullFilename,ExitMenu);
         IF NOT ExitMenu THEN
            BEGIN
            Dummy:=False;
            Error:=LoadGame(FullFilename);
            IF Error=100 THEN
               AttentionWindow('"'+FullFilename+'" is probably not','an Anacreon save file.',Dummy)
            ELSE IF Error<>0 THEN
               DOSErrorWindow(Error,FullFilename)
            ELSE
               BEGIN
               ExitMenu:=True;
               CurrentGame:=FullFilename;
               AllUpCase(CurrentGame);
               UpdateMenuBar(Bar);
               GameLoaded:=True;
               GameModified:=False;
               END;
            END;
      UNTIL ExitMenu;
      END;
   END;  { ContinueOldGame }

PROCEDURE QuitGame(VAR EndGame,GameLoaded,GameModified: Boolean);

   BEGIN
   GameNotSaved(GameLoaded,GameModified);

   IF (NOT GameLoaded) OR (NOT GameModified) THEN
      BEGIN
      EndGame:=True;
      GameModified:=False;
      GameLoaded:=False;
      END;
   END;  { QuitGame }

PROCEDURE ChoosePlayer(Empires: EmpireSet; VAR Player: Empire);
   VAR
      Menu: MenuStructure;
      PotentialEmpire: ARRAY [1..8] OF Empire;
      NoOfEmps: Word;
      Emp: Empire;
      Ch: Char;

   BEGIN
   InitializeMenu(Menu);
   NoOfEmps:=0;
   FOR Emp:=Empire1 TO Empire8 DO
      IF Emp IN Empires THEN
         BEGIN
         Inc(NoOfEmps);
         PotentialEmpire[NoOfEmps]:=Emp;
         AddMenuLine(Menu,EmpireName(Emp));
         END;

   DisplayMenu(Menu,10,8,LightGray,C.SYSDispSelect,45,10);

   REPEAT
      GetChoice(AnyKey,NoCaseDistinct,Ch);
      ActivateMenu(Menu,Ch);
   UNTIL (Ch=ReturnKey) OR (Ch=ESCKey);

   IF Ch=ReturnKey THEN
      Player:=PotentialEmpire[GetMenuSelect(Menu)]
   ELSE
      Player:=Indep;
   CleanUpMenu(Menu);
   END;  { ChoosePlayer }

PROCEDURE DeletePlayerEmpire(VAR GameLoaded,GameModified,Continue: Boolean);
   VAR
      Abort: Boolean;
      Wind: WindowHandle;
      Empires: EmpireSet;
      Emp: Empire;
      NoOfEmpires: Word;

   BEGIN

{$IFNDEF Demo}

   IF NOT GameLoaded THEN
      BEGIN
      Abort:=False;
      AttentionWindow('You must Load a game first.','',Abort);
      END
   ELSE
      BEGIN
      Empires:=[];
      NoOfEmpires:=0;
      FOR Emp:=Empire1 TO Empire8 DO
         IF (EmpireActive(Emp)) AND (EmpirePlayer(Emp)) THEN
            BEGIN
            Empires:=Empires+[Emp];
            Inc(NoOfEmpires);
            END;

      OpenWindow(1,2,80,23,ThinBRD,'',C.SYSDispWind,C.SYSWBorder,Wind);
      IF NoOfEmpires=1 THEN
         BEGIN
         Abort:=False;
         AttentionWindow('But that''s the last empire!','How can you play with no empires?',Abort)
         END
      ELSE
         BEGIN
         WriteString('Which empire do you want to delete?',1,2,C.SYSDispWind);
         ChoosePlayer(Empires,Emp);
         IF Emp<>Indep THEN
            BEGIN
            Abort:=True;
            ClrScr;
            AttentionWindow('Do you really want to delete the '+EmpireName(Emp)+' Empire?',
                            'Press <Esc> to cancel.',Abort);
            IF NOT Abort THEN
               BEGIN
               EmpiresToMove:=EmpiresToMove-[Emp];
               DestroyEmpire(Emp);
               GameModified:=True;
               IF AsyncTurns THEN
                  BEGIN
                  Player:=Emp;
                  Continue:=True;
                  END
               ELSE IF Player=Emp THEN
                  Continue:=True;
               END;
            END;
         END;
      CloseWindow;
      END;

{$ELSE}

   FeatureInActive;

{$ENDIF}

   END;  { DeletePlayerEmpire }

PROCEDURE AddPlayerEmpire(VAR GameLoaded,GameModified: Boolean);
   VAR
      Emp,NewEmp: Empire;
      Sex,Abort: Boolean;
      i: Word;
      WorldID: IDNumber;
      Wind: WindowHandle;
      EmpireName: EmpireNameArray;
      Name: String32;
      Password: String8;
      MaxTech,Tech: TechLevel;
      MaxTechnology,Technology: TechnologySet;
      XY: XYCoord;
      Loc: Location;

   PROCEDURE GetNewEmpire(VAR NewEmp: Empire);

      BEGIN
      NewEmp:=Empire1;
      WHILE (NewEmp<>Indep) AND (EmpireActive(NewEmp)) DO
         NewEmp:=Succ(NewEmp);
      END;  { GetNewEmpire }

   PROCEDURE GetNewCapital(VAR WorldID: IDNumber);
      VAR
         i: Word;

      BEGIN
      WorldID.ObjTyp:=Pln;
      FOR i:=1 TO NoOfPlanets DO
         BEGIN
         WorldID.Index:=i;
         IF (GetStatus(WorldID)=Indep) AND
            (GetTech(WorldID)>=BioTchLvl) AND
            (GetPopulation(WorldID)>2000) THEN
            BEGIN
            Exit;
            END;
         END;

      WorldID:=EmptyQuadrant;
      END;  { GetNewCapital }

   BEGIN

{$IFNDEF Demo}

   IF GameLoaded THEN
      BEGIN
      GetNewEmpire(NewEmp);
      GetNewCapital(WorldID);

      IF NewEmp=Indep THEN
         BEGIN
         Abort:=False;
         AttentionWindow('There are already eight empires in the galaxy.',
                         'Press any key to continue.',Abort);
         END
      ELSE IF SameID(WorldID,EmptyQuadrant) THEN
         BEGIN
         Abort:=False;
         AttentionWindow('A suitable world cannot be found for a capital.',
                         'Press any key to continue.',Abort);
         END
      ELSE
         BEGIN
         GameModified:=True;
         FillChar(EmpireName,SizeOf(EmpireName),0);
         Tech:=GetTech(WorldID);

         MaxTech:=PrimitLvl;
         MaxTechnology:=[];
         FOR Emp:=Empire1 TO Empire8 DO
            IF EmpireActive(Emp) THEN
               BEGIN
               { Alert others }
               IF Known(Emp,WorldID) THEN
                  BEGIN
                  Loc.ID:=WorldID;  Loc.XY:=Limbo;
                  AddNews(Emp,NewPlEmp,Loc,Ord(NewEmp),0,0);
                  END;

               GetEmpireTechnology(Emp,Tech,Technology);
               IF Tech>MaxTech THEN
                  BEGIN
                  MaxTech:=Tech;
                  MaxTechnology:=Technology;
                  END
               ELSE
                  MaxTechnology:=MaxTechnology+Technology;
               END;

         OpenWindow(1,2,80,23,ThinBRD,'',C.SYSDispWind,C.SYSWBorder,Wind);
         WriteString(' You are a dwarf among giants.  You have fewer ships, fewer people, and a',1,1,C.SYSDispWind);
         WriteString(' smaller industrial capacity than any other empire in the galaxy.  With one',1,2,C.SYSDispWind);
         WriteString(' world and your Imperial Starfleet you must carve an empire out of the scraps',1,3,C.SYSDispWind);
         WriteString(' and remnants of others.  But that is your greatest strength:  With luck, and',1,4,C.SYSDispWind);
         WriteString(' a great deal of diplomatic skill, you''ll perhaps be ignored for just long',1,5,C.SYSDispWind);
         WriteString(' enough...  By the time the giants realize their mistake, your flag will be',1,6,C.SYSDispWind);
         WriteString(' flying over a dozen worlds.',1,7,C.SYSDispWind);
         InputEmpireName(NewEmp,1,9,EmpireName,Name,Password,Sex);
         CreateEmpire(NewEmp,True,Sex,Name,Password,WorldID,MaxTech,MaxTechnology,0,[],Year);
         ClearKnownSet(NewEmp);
         SetStatus(WorldID,NewEmp);
         SetType(WorldID,CapTyp);
         SetTech(WorldID,MaxTech);
         GetCoord(WorldID,XY);
         Scout(NewEmp,XY);
         CloseWindow;
         END;
      END
   ELSE
      BEGIN
      Abort:=False;
      AttentionWindow('You must Load a game first.','',Abort);
      END;

{$ELSE}

   FeatureInActive;

{$ENDIF}

   END;  { AddPlayerEmpire }

PROCEDURE TogglePause(VAR Bar: MenuBar; VAR GameModified: Boolean);
   VAR
      Dummy: Boolean;
      Line: String;

   BEGIN
   Dummy:=False;
   PauseActive:=NOT PauseActive;
   IF PauseActive THEN
      Line:='Pause feature is now ACTIVE.'
   ELSE
      Line:='Pause feature is now INACTIVE.';

   AttentionWindow(Line,'Press any key to continue.',Dummy);
   UpdateMenuBar(Bar);
   GameModified:=True;
   END;  { TogglePause }

PROCEDURE ToggleTurnSync(VAR Bar: MenuBar; VAR GameModified: Boolean);
   VAR
      Dummy: Boolean;
      Line: String;

   BEGIN
   Dummy:=False;
   AsyncTurns:=NOT AsyncTurns;
   IF AsyncTurns THEN
      BEGIN
      Line:='Player turns are now NON-SEQUENTIAL';
      END
   ELSE
      BEGIN
      Line:='Player turns are now SEQUENTIAL';
      ResetEmpiresToMove;
      Player:=Empire1;
      END;

   AttentionWindow(Line,'Press any key to continue.',Dummy);
   UpdateMenuBar(Bar);
   GameModified:=True;
   END;  { ToggleTurnSync }

PROCEDURE ToggleAutoSave(VAR Bar: MenuBar; VAR GameModified: Boolean);
   VAR
      Dummy: Boolean;
      Line: String32;

   BEGIN
   Dummy:=False;
   AutoSave:=NOT AutoSave;
   IF AutoSave THEN
      Line:='Auto Backup is now ON.'
   ELSE
      Line:='Auto Backup is now OFF.';

   AttentionWindow(Line,'Press any key to continue.',Dummy);
   UpdateMenuBar(Bar);
   GameModified:=True;
   END;  { ToggleAutoSave }

PROCEDURE PrintGalacticMap(GameLoaded: Boolean);
   VAR
      x,y: Byte;
      Abort: Boolean;
      Wind: WindowHandle;
      Line: LineStr;
      Ch: Char;

   BEGIN
   IF (NOT GameLoaded) THEN
      BEGIN
      Abort:=False;
      AttentionWindow('You must Load a game first.','',Abort);
      END
   ELSE
      BEGIN
      Abort:=True;
      AttentionWindow('Please prepare the printer for output...',
                      'Press any key to start or <Esc> to cancel',Abort);
      IF NOT Abort THEN
         BEGIN
         OpenWindow(1,2,80,23,ThinBRD,'',C.SYSDispWind,C.SYSWBorder,Wind);
         WriteLn;
         WriteLn('Printing...');

         PRNReset;
         PrintLine('ANACREON: Galactic Map.');
         PrintLine('');
         PrintLine(Copy('       |    |    |    |    |    |    |    |    |    |',1,SizeOfGalaxy+3));
         FOR y:=1 TO SizeOfGalaxy DO
            BEGIN
            IF (y mod 5)=0 THEN
               Line:=' --'
            ELSE
               Line:='   ';

            FOR x:=1 TO SizeOfGalaxy DO
               BEGIN
               WITH Sector[x]^[y] DO
                  BEGIN
                  IF Obj.ObjTyp=Plsr THEN
                     Ch:=Chr(48+Obj.Index)
                  ELSE IF NebulaTypes(Special AND $0F)=Nebula THEN
                     Ch:='%'
                  ELSE IF NebulaTypes(Special AND $0F)=DarkNebula THEN
                     Ch:='#'
                  ELSE IF NebulaTypes(Special AND $0F)=DenseNebula THEN
                     Ch:='#'
                  ELSE IF Obj.ObjTyp=Pln THEN
                     Ch:='p'
                  ELSE
                     Ch:='.';
                  END;  { with scope }
               Line:=Line+Ch;
               END;  { loop }

            PrintLine(Line);
            END;  { loop }

         PRNFormFeed;
         CloseWindow;
         END;  { if }
      END;
   END;  { PrintGalacticMap }

PROCEDURE ChangeTimeLimit(VAR GameLoaded,GameModified: Boolean);
   VAR
      TimeStr: String8;
      Error,NewTime: Integer;
      Abort,Ok: Boolean;
      Wind: WindowHandle;

   BEGIN
   IF (NOT GameLoaded) THEN
      BEGIN
      Abort:=False;
      AttentionWindow('You must Load a game first.','',Abort);
      END
   ELSE
      BEGIN
      OpenWindow(15,10,60,8,ThinBRD,'Time Limit',C.CommWind,C.SYSWBorder,Wind);
      WriteString('This variable controls the amount of time that is added',1,1,C.CommWind);
      WriteString('to a player''s turn each year.',1,2,C.CommWind);
      REPEAT
         Str(TimePerTurn DIV 60,TimeStr);
         InputString('Time added per turn (minutes) : ',1,4,C.CommWind,6,TimeStr);
         Val(TimeStr,NewTime,Error);
         IF TimeStr=EscKey THEN
            BEGIN
            NewTime:=TimePerTurn DIV 60;
            Ok:=True;
            END
         ELSE IF (Error<>0) OR (NewTime<=0) THEN
            BEGIN
            WriteString('Please enter a positive integer.         ',3,5,C.CommWind);
            Ok:=False;
            END
         ELSE IF (NewTime>120) THEN
            BEGIN
            WriteString('Please limit yourself to 2 hours per turn!',3,5,C.CommWind);
            Ok:=False;
            END
         ELSE
            Ok:=True;
      UNTIL Ok;
      TimePerTurn:=NewTime*60;
      CloseWindow;
      GameModified:=True;
      END;
   END;  { ChangeTimeLimit }

PROCEDURE StartANewGame(VAR GameLoaded,GameModified: Boolean; VAR Bar: MenuBar);
   VAR
      Abort: Boolean;
      MainWindow: WindowHandle;

   BEGIN
   GameNotSaved(GameLoaded,GameModified);

   IF (NOT GameLoaded) OR (NOT GameModified) THEN
      BEGIN
      IF GameLoaded THEN
         CleanUpUniverse;

      OpenWindow(1,2,80,23,NoBRD,'',C.SYSWBorder,0,MainWindow);
      StartNewGame(Abort);
      IF NOT Abort THEN
         BEGIN
         GameLoaded:=True;
         GameModified:=True;
         ResetEmpiresToMove;
         AsyncTurns:=False;
         UpdateMenuBar(Bar);
         CurrentGame:='ANACREON.SAV';
         END
      ELSE
         BEGIN
         GameLoaded:=False;
         GameModified:=False;
         END;

      CloseWindow;
      END;
   END;  { StartANewGame }

PROCEDURE GetPlayerToMove(VAR Player: Empire);
   VAR
      Wind: WindowHandle;

   BEGIN
   OpenWindow(1,2,80,23,ThinBRD,'',C.SYSDispWind,C.SYSWBorder,Wind);
   WriteString('Who are you?',1,2,C.SYSDispWind);
   ChoosePlayer(EmpiresToMove,Player);
   CloseWindow;
   END;  { GetPlayerToMove }

PROCEDURE Directories;
   VAR
      Wind: WindowHandle;
      TempDir: LineStr;

   BEGIN
   OpenWindow(10,2,65,6,ThinBRD,'',LightGray,C.SYSWBorder,Wind);
   TempDir:=SceDirect;
   InputString('Scenario Directory: ',1,1,LightGray,40,TempDir);
   IF (TempDir<>EscKey) AND (TempDir<>SceDirect) THEN
      BEGIN
      SceDirect:=TempDir;
		AllUpCase(SceDirect);
      ConfigModified:=True;
      END;
   WriteString(SceDirect,21,1,LightGray);
   TempDir:=SavDirect;
   InputString('Save Directory: ',1,2,LightGray,40,TempDir);
   IF (TempDir<>EscKey) AND (TempDir<>SavDirect) THEN
      BEGIN
      SavDirect:=TempDir;
		AllUpCase(SavDirect);
      ConfigModified:=True;
      END;
   WriteString(SavDirect,17,2,LightGray);
   TempDir:=HlpDirect;
   InputString('Help Directory: ',1,3,LightGray,40,TempDir);
   IF (TempDir<>EscKey) AND (TempDir<>HlpDirect) THEN
      BEGIN
      HlpDirect:=TempDir;
		AllUpCase(HlpDirect);
      ConfigModified:=True;
      END;
   WriteString(HlpDirect,17,3,LightGray);
   CloseWindow;

	IF ConfigModified THEN
		BEGIN
		CloseHelpFile(HLP^);
		LoadHelpFile(HLP^);
		END;
   END;  { Directories }

PROCEDURE Prologue(VAR Player: Empire; VAR EndGame: Boolean);
   VAR
      Continue,ESCHit,Abort: Boolean;
      MainWindow: WindowHandle;
      Bar: MenuBar;
      Error,DelayLoop: Word;
      Star: StarArray;
      Ch: Char;
      Comm: Word;

   BEGIN
   InitializeMenuBar(Bar);
   AddBarItem(Bar,1,'ð','!',2,16);
   AddBarMenuItem(Bar,1,'About ANACREON','A',11);
   AddBarMenuItem(Bar,1,'DOS shell','D',13);

   AddBarItem(Bar,2,'Game','G',4,16);
   AddBarMenuItem(Bar,2,'Begin...','B',5);
   AddBarMenuItem(Bar,2,'New game','N',9);
   AddBarMenuItem(Bar,2,'Load game','L',10);
   AddBarMenuItem(Bar,2,'Save game','S',1);
   AddBarMenuItem(Bar,2,'Print map','P',3);
   AddBarMenuItem(Bar,2,'Quit','Q',4);

   AddBarItem(Bar,3,'Options','O',10,26);
   AddBarMenuItem(Bar,3,'Time limit','T',2);
   AddBarMenuItem(Bar,3,'New player empire','N',7);
   AddBarMenuItem(Bar,3,'Delete player empire','D',14);
   AddBarMenuItem(Bar,3,'Auto backup','A',6);
   AddBarMenuItem(Bar,3,'Pause','P',8);
   AddBarMenuItem(Bar,3,'Sequential play','S',12);

   AddBarItem(Bar,4,'Configure','C',19,24);
   AddBarMenuItem(Bar,4,'Mono/color','M',15);
   AddBarMenuItem(Bar,4,'Directories','D',16);
   AddBarMenuItem(Bar,4,'Save configuration','S',17);

   UpdateMenuBar(Bar);

   ESCHit:=True;

   OpenWindow(1,1,80,25,NoBRD,'',C.SYSWBorder,0,MainWindow);

   DelayLoop:=0;
   InitStarArray(Star);
   DisplayMenuBar(Bar);

   MainTitle;
   Continue:=False;
   Ch:=NoKey;
   REPEAT
      DisplayMenuBar(Bar);

      REPEAT
         Comm:=0;
         GetInkey(Ch);
         Ch:=UpCase(Ch);
         IF Ch<>NoKey THEN
            ActivateMenuBar(Bar,Ch,Comm);
         IF DelayLoop=0 THEN
            BEGIN
            UpdateStarArray(Star);
            DelayLoop:=1000;
            END
         ELSE
            Dec(DelayLoop);
      UNTIL Comm<>0;

      CASE Comm OF
         1: SaveTheGame(GameLoaded,GameModified);
         2: ChangeTimeLimit(GameLoaded,GameModified);
         3: PrintGalacticMap(GameLoaded);
         4: QuitGame(EndGame,GameLoaded,GameModified);
         5: BEGIN
            IF (NOT GameLoaded) THEN
               ContinueOldGame(Bar,GameLoaded,GameModified);

            IF GameLoaded THEN
               BEGIN
               IF AsyncTurns THEN
                  GetPlayerToMove(Player);

               ClrScr;
               IF Player<>Indep THEN
                  BEGIN
                  Continue:=True;
                  GameModified:=True;
                  END
               ELSE
                  MainTitle;
               END;
            END;
         6: ToggleAutoSave(Bar,GameModified);
         7: AddPlayerEmpire(GameLoaded,GameModified);
         8: TogglePause(Bar,GameModified);
         9: StartANewGame(GameLoaded,GameModified,Bar);
        10: ContinueOldGame(Bar,GameLoaded,GameModified);
        11: BEGIN
            AboutAnacreon;
            MainTitle;
            END;
        12: ToggleTurnSync(Bar,GameModified);
        13: DOSShell;
        14: DeletePlayerEmpire(GameLoaded,GameModified,Continue);
        15: BEGIN
            ToggleColorBW;
            UseColor:=NOT UseColor;
            ConfigModified:=True;
            UpdateMenuBar(Bar);
            MainTitle;
            END;
        16: Directories;
        17: SaveConfiguration;
      END;  { case }
       WriteHelpLine('');
      EmptyKeyBuffer;
   UNTIL Continue OR EndGame;

   CloseWindow;
   CleanUpMenuBar(Bar);
   END;  { Prologue }

END.
