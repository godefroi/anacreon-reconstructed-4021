(* CODE.PAS ------------------------------------------------------------------

--------------------------------------------------------------------------- *)

UNIT Code;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES
   Strg,
   Types,
   Galaxy,
   CdeTypes;

PROCEDURE CodeInterpreter(VAR Code: ActionArray;
                          VAR IP: Word;
                          VAR Global: RegisterArray;
                          VAR Register: RegisterArray);
PROCEDURE GetParameter(Parm: Byte; VAR Value: VariableRecord;
                       I: VariableRecord; VAR R,V: RegisterArray);
PROCEDURE SetParameter(Parm: Byte; NewValue: VariableRecord;
                       VAR R,V: RegisterArray);

IMPLEMENTATION

USES
   CRT,
   EIO,
   Int,
   Menu,
   Wnd,
   Misc,
   Environ,
   PrimIntr,
   Intrface,
   TextStrc,
   Artifact,
   SWindows,
   Scena;

TYPE
   ComparisonTypes = ( NoCMP, EqualCMP, GreaterCMP, LesserCMP );

   GenericMenuLine = RECORD
      CASE Integer OF
         1: (ID: IDNumber);
         2: (Line: Word);
   END;

VAR
   TempMenu: MenuStructure;
   TempList: ARRAY [1..100] OF GenericMenuLine;
   TempNoOfLines: Word;
   TempMenuType: Word;

(* Primitives ============================================================= *)

FUNCTION CompareVariables(First,Second: VariableRecord): ComparisonTypes;
   BEGIN
   CASE First.VType OF
      BoolVRT: BEGIN
         IF First.Bool=Second.Bool THEN
            CompareVariables:=EqualCMP
         ELSE
            CompareVariables:=GreaterCMP;
         END;   
      ScalarVRT: BEGIN
         IF First.Scalar=Second.Scalar THEN
            CompareVariables:=EqualCMP
         ELSE IF First.Scalar>Second.Scalar THEN
            CompareVariables:=GreaterCMP
         ELSE
            CompareVariables:=LesserCMP;
         END;
      EmpVRT: BEGIN
         IF First.Emp=Second.Emp THEN
            CompareVariables:=EqualCMP
         ELSE
            CompareVariables:=GreaterCMP;
         END;
      IDVRT: BEGIN
         IF SameID(First.ID,Second.ID) THEN
            CompareVariables:=EqualCMP
         ELSE
            CompareVariables:=GreaterCMP;
         END;
      XYVRT: BEGIN
         IF SameXY(First.XY,Second.XY) THEN
            CompareVariables:=EqualCMP
         ELSE
            CompareVariables:=GreaterCMP;
         END;
      END;  { case }
   END;  { CompareVariables }

FUNCTION VariableString(R: VariableRecord): LineStr;
   VAR
      Temp: LineStr;

   BEGIN
   CASE R.VType OF
      BoolVRT: BEGIN
         IF R.Bool THEN
            Temp:='TRUE'
         ELSE
            Temp:='FALSE';
         END;
      ScalarVRT: Str(R.Scalar,Temp);
      EmpVRT: Temp:=EmpireName(R.Emp);
      IDVRT: Temp:=ObjectName(Player,R.ID,LongFormat);
      XYVRT: GetCoordName(R.XY,Temp);
   ELSE
      Temp:='(undefined)';
      END;  { case }

   VariableString:=Temp;
   END;  { VariableString }

PROCEDURE SetParameter(Parm: Byte; NewValue: VariableRecord;
                       VAR R,V: RegisterArray);
   BEGIN
   CASE Parm OF
      0: BEGIN END;
      1..10: R[Parm-1]:=NewValue;
      11: BEGIN END;
      12..21: V[Parm-12]:=NewValue;
      END;
   END;  { SetParameter }

PROCEDURE GetParameter(Parm: Byte; VAR Value: VariableRecord;
                       I: VariableRecord; VAR R,V: RegisterArray);
   BEGIN
   CASE Parm OF
      0: FillChar(Value,SizeOf(Value),0);
      1..10: Value:=R[Parm-1];
      11: Value:=I;
      12..21: Value:=V[Parm-12];
      END;
   END;  { GetParameter }

PROCEDURE DisplayTextWindow(Txt: TextStructure; x,y,Col: Word);
   VAR
      Line: LineStr;
      CurLine: LineRecordPtr;
      i: Word;

   BEGIN
   CurLine:=TXTFirstLine(Txt);
   FOR i:=1 TO TXTNumberOfLines(Txt) DO
      BEGIN
      WriteString(TXTLine(CurLine),x,y,Col);
      Inc(y);
      CurLine:=TXTNextLine(CurLine);
      END;
   END;  { DisplayTextWindow }

(* ExecuteAction procedures ----------------------------------------------- *)

PROCEDURE PressAnyKey;
{ AnyKey: -----------------------------------------------------------------
Displays 'Press any key to continue...' at bottom of display screen
and waits for a key to be pressed.
------------------------------------------------------------------------- }
   VAR
      Ch: Char;

   BEGIN
   WriteString('Press any key to continue...',2,19,C.SYSDispWind);
   GetCharacter(AnyKey,NoCaseDistinct,Ch);
   END;  { PressAnyKey }

PROCEDURE Assign(VAR Action: ActionRecord;
                 VAR G: RegisterArray;
                 VAR R: RegisterArray);
{ Assign: -----------------------------------------------------------------
Parm1 := Parm2.
------------------------------------------------------------------------- }
   VAR
      Temp: VariableRecord;

   BEGIN
   WITH Action DO
      BEGIN
      GetParameter(Parm[2],Temp,Immediate,R,G);
      SetParameter(Parm[1],Temp,R,G);
      END;
   END;  { Assign }

PROCEDURE DebugDump(VAR R: RegisterArray);
   VAR
      Wind: WindowHandle;
      Ch: Char;
      i: Word;

   BEGIN
   OpenWindow(1,5,50,15,ThinBRD,'Debug: Registers',C.CommWind,C.SYSWBorder,Wind);
   FOR i:=0 TO 9 DO
      BEGIN
      WriteString('Register '+Char(48+i)+': ',1,1+i,C.SYSDispWind);
      WriteString(VariableString(R[i]),20,1+i,C.SYSDispWind);
      END;
   WriteString('Press any key to continue...',2,13,C.SYSDispWind);
   GetCharacter(AnyKey,NoCaseDistinct,Ch);
   CloseWindow;
   END;  { DebugDump }

PROCEDURE Display(VAR Action: ActionRecord;
                  VAR G: RegisterArray;
                  VAR R: RegisterArray);
{ Display: ----------------------------------------------------------------
Displays scenario text given in p1 at y-coord given by p2
------------------------------------------------------------------------- }
   VAR
      TextNum: VariableRecord;
      Txt: TextStructure;
      Ch: Char;
      YPos: VariableRecord;

   BEGIN
   WITH Action DO
      BEGIN
      GetParameter(Parm[1],TextNum,Immediate,R,G);
      GetParameter(Parm[2],YPos,Immediate,R,G);
      IF TextNum.VType=ScalarVRT THEN
         BEGIN
         InitializeText(Txt);
         LoadScenaText(TextNum.Scalar,Txt);
         DisplayTextWindow(Txt,1,YPos.Scalar,C.SYSDispWind);
         DisposeText(Txt);
         END;
      END;
   END;  { Display }

PROCEDURE GetArtifactCoord(VAR Action: ActionRecord;
                           VAR G: RegisterArray;
                           VAR R: RegisterArray);
{ GetArtifactCoord: -------------------------------------------------------
   Returns the coords of artifact p1 in p2.
------------------------------------------------------------------------- }
   VAR
      Temp: VariableRecord;
      Loc: Location;

   BEGIN
   WITH Action DO
      BEGIN
      GetParameter(Parm[1],Temp,Immediate,R,G);
      ArtifactLoc(Temp.ID,Loc);
      IF SameID(Loc.ID,EmptyQuadrant) THEN
         Temp.XY:=Loc.XY
      ELSE
         GetCoord(Loc.ID,Temp.XY);

      Temp.VType:=XYVRT;
      SetParameter(Parm[2],Temp,R,G);
      END;
   END;  { GetArtifactCoord }

PROCEDURE GetObjectPower(VAR Action: ActionRecord;
                         VAR G: RegisterArray;
                         VAR R: RegisterArray);
   VAR
      Defns: DefnsArray;
      Ships: ShipArray;
      Temp: VariableRecord;
      Power: LongInt;

   BEGIN
   WITH Action DO
      BEGIN
      GetParameter(Parm[1],Temp,Immediate,R,G);
      FillChar(Defns,SizeOf(Defns),0);
      GetShips(Temp.ID,Ships);
      Power:=MilitaryPower(Ships,Defns);
      IF Power=0 THEN
         Temp.Scalar:=0
      ELSE
         Temp.Scalar:=GreaterInt(1,IntLmt(Round(Power/100)));

      Temp.VType:=ScalarVRT;
      SetParameter(Parm[2],Temp,R,G);
      END;
   END;  { GetObjectPower }

PROCEDURE IsEqual(VAR Action: ActionRecord;
            VAR G: RegisterArray;
            VAR R: RegisterArray;
            Comparison: ComparisonTypes);
   VAR
      First,Second,Result: VariableRecord;
      Comp: ComparisonTypes;

   BEGIN
   WITH Action DO
      BEGIN
      GetParameter(Parm[1],First,Immediate,R,G);
      GetParameter(Parm[2],Second,Immediate,R,G);
      END;

   Comp:=CompareVariables(First,Second);
   Result.VType:=BoolVRT;
   Result.Bool:=(Comp=Comparison);

   WITH Action DO
      BEGIN
      SetParameter(Parm[3],Result,R,G);
      END;
   END;  { IsEqual }

PROCEDURE IsNotEqual(VAR Action: ActionRecord;
               VAR G: RegisterArray;
               VAR R: RegisterArray);
   VAR
      First,Second,Result: VariableRecord;
      Comp: ComparisonTypes;

   BEGIN
   WITH Action DO
      BEGIN
      GetParameter(Parm[1],First,Immediate,R,G);
      GetParameter(Parm[2],Second,Immediate,R,G);
      END;

   Comp:=CompareVariables(First,Second);
   Result.VType:=BoolVRT;
   Result.Bool:=(Comp<>EqualCMP);

   WITH Action DO
      BEGIN
      SetParameter(Parm[3],Result,R,G);
      END;
   END;  { IsNotEqual }

PROCEDURE MenuAddFleets(VAR Action: ActionRecord;
                  VAR G: RegisterArray;
                  VAR R: RegisterArray);
   VAR
      Temp: VariableRecord;
      FltXY,XY: XYCoord;
      Line: LineStr;
      Emp: Empire;
      i: Word;
      FleetsToCheck: FleetSet;
      FltID: IDNumber;


   BEGIN
   WITH Action DO
      BEGIN
      GetParameter(Parm[1],Temp,Immediate,R,G);
      XY:=Temp.XY;
      GetParameter(Parm[2],Temp,Immediate,R,G);
      Emp:=Temp.Emp;
      END;

   FltID.ObjTyp:=Flt;
   FleetsToCheck:=SetOfActiveFleets * SetOfFleetsOf[Emp];
   FOR i:=1 TO MaxNoOfFleets DO
   IF (i IN FleetsToCheck) THEN
      BEGIN
         FltID.Index:=i;
         GetCoord(FltID,FltXY);
         IF SameXY(FltXY,XY) THEN
            BEGIN
            Inc(TempNoOfLines);
            TempList[TempNoOfLines].ID:=FltID;
            Line:=ObjectName(Player,FltID,LongFormat);
            Line:=Line+'  ('+EmpireName(Emp)+')';
            AddMenuLine(TempMenu,Line);
            END;
         END;

   WITH Action DO
      BEGIN
      Temp.VType:=ScalarVRT;
      Temp.Scalar:=TempNoOfLines;
      SetParameter(Parm[3],Temp,R,G);
      END;
   END;  { MenuAddFleets }

PROCEDURE MenuDisplay(VAR Action: ActionRecord;
                     VAR G: RegisterArray;
                     VAR R: RegisterArray);
   VAR
      Temp: VariableRecord;
      Abort: Boolean;
      Ch: Char;

   BEGIN
   IF TempNoOfLines>0 THEN
      BEGIN
      DisplayMenu(TempMenu,5,12,LightGray,C.SYSDispSelect,45,7);
      REPEAT
         GetCharacter(AnyKey,NoCaseDistinct,Ch);
         ActivateMenu(TempMenu,Ch);
      UNTIL Ch IN [ESCKey,ReturnKey];
   
      IF Ch=ReturnKey THEN
         BEGIN
         Abort:=False;
         CASE TempMenuType OF
            1: BEGIN
               Temp.VType:=IDVRT;
               Temp.ID:=TempList[GetMenuSelect(TempMenu)].ID;
               END;
            2: BEGIN
               Temp.VType:=ScalarVRT;
               Temp.Scalar:=GetMenuSelect(TempMenu);
               END;
            END;  { case }
         END
      ELSE
         Abort:=True;

      CleanUpMenu(TempMenu);
      END
   ELSE
      Abort:=True;

   WITH Action DO
      BEGIN
      SetParameter(Parm[1],Temp,R,G);
      Temp.VType:=BoolVRT;
      Temp.Bool:=Abort;
      SetParameter(Parm[2],Temp,R,G);
      END;
   END;  { MenuDisplay }

PROCEDURE MenuInitialize(VAR Action: ActionRecord;
                        VAR G: RegisterArray;
                        VAR R: RegisterArray);
   VAR
      Temp: VariableRecord;

   BEGIN
   WITH Action DO
      BEGIN
      GetParameter(Parm[1],Temp,Immediate,R,G);
      TempNoOfLines:=0;
      TempMenuType:=Temp.Scalar;
      InitializeMenu(TempMenu);
      END;
   END;  { MenuInitialize }

(* Interface Procedures --------------------------------------------------- *)

PROCEDURE ExecuteAction(Action: ActionRecord;
                        VAR Global: RegisterArray;
                        VAR Register: RegisterArray);

   { BEGIN PROCEDURE: ExecuteAction }
   BEGIN
   CASE Action.AType OF
      AnyKeyACT: PressAnyKey;
      AssignACT: Assign(Action,Global,Register);
      ClsACT: ClrScr;
      DebugDumpACT: DebugDump(Register);
      DisplayACT: Display(Action,Global,Register);
      GetArtifactCoordACT: GetArtifactCoord(Action,Global,Register);
      GetObjectPowerACT: GetObjectPower(Action,Global,Register);
      IsEqualACT: IsEqual(Action,Global,Register,EqualCMP);
      IsGreaterACT: IsEqual(Action,Global,Register,GreaterCMP);
      IsLesserACT: IsEqual(Action,Global,Register,LesserCMP);
      IsNotEqualACT: IsNotEqual(Action,Global,Register);
      MenuAddFleetsACT: MenuAddFleets(Action,Global,Register);
      MenuDisplayACT: MenuDisplay(Action,Global,Register);
      MenuInitializeACT: MenuInitialize(Action,Global,Register);
      END;
   END;  { ExecuteAction }

PROCEDURE SkipToNextEnd(VAR Code: ActionArray; VAR IP: Word);
{ SkipToNextEnd: -------------------------------------------------------------
   Skips to next structured END in the code, ignoring all nested ENDs.
   Positions the IP to the instruction following the END.
---------------------------------------------------------------------------- }
   
   VAR
      Level: Word;

   BEGIN
   Level:=1;
   REPEAT
      CASE Code[IP].AType OF
         SwitchCODE,CaseCODE,IfCODE,ElseCODE,WhileCODE: Inc(Level);
         EndCODE: Dec(Level);
         END;
      Inc(IP);
   UNTIL (Code[IP-1].AType=EndCODE) AND (Level=0);
   END;  { SkipToNextEnd }

PROCEDURE CodeInterpreter(VAR Code: ActionArray;
                          VAR IP: Word;
                          VAR Global: RegisterArray;
                          VAR Register: RegisterArray);
{ CodeInterpreter: -----------------------------------------------------------
   Executes code given by Code and starts at instruction given by IP.
   Returns when END command is reached.
---------------------------------------------------------------------------- }
   VAR
      Guard,Value: VariableRecord;
      StartIP: Word;

   BEGIN
   WHILE Code[IP].AType<>EndCODE DO
      WITH Code[IP] DO
         BEGIN
         CASE AType OF
            SwitchCODE: BEGIN
               END;
            CaseCODE: BEGIN
               GetParameter(Parm[1],Guard,Immediate,Register,Global);
               GetParameter(Parm[2],Value,Immediate,Register,Global);
               IF Value.Scalar<=Guard.Scalar THEN
                  BEGIN
                  Inc(IP);
                  CodeInterpreter(Code,IP,Global,Register);
                  SkipToNextEnd(Code,IP);
                  Dec(IP);
                  END
               ELSE
                  BEGIN
                  Inc(IP);
                  SkipToNextEnd(Code,IP);
                  IF Code[IP].AType<>EndCODE THEN
                     Dec(IP);
                  END;
               END;
            WhileCODE: BEGIN
               GetParameter(Parm[1],Guard,Immediate,Register,Global);
               StartIP:=IP+1;
               WHILE Guard.Bool DO
                  BEGIN 
                  CodeInterpreter(Code,StartIP,Global,Register);
                  GetParameter(Parm[1],Guard,Immediate,Register,Global);
                  END;
               IP:=StartIP-1;
               END;
            IfCODE: BEGIN
               GetParameter(Parm[1],Guard,Immediate,Register,Global);
               IF Guard.Bool THEN
                  BEGIN
                  Inc(IP);
                  CodeInterpreter(Code,IP,Global,Register);
                  IF Code[IP].AType=ElseCODE THEN
                     BEGIN
                     Inc(IP);
                     SkipToNextEnd(Code,IP);
                     Dec(IP);
                     END;
                  END
               ELSE
                  BEGIN
                  Inc(IP);
                  SkipToNextEnd(Code,IP);
                  IF Code[IP].AType=ElseCODE THEN
                     BEGIN
                     Inc(IP);
                     CodeInterpreter(Code,IP,Global,Register);
                     END;
                  Dec(IP);
                  END;
               END;
            ELSE
               ExecuteAction(Code[IP],Global,Register);
            END;  { case }
         Inc(IP);
         END;

   Inc(IP);
   END;  { CodeInterpreter }

END.
