(* NEWGAME.PAS -----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT NewGame;

{$IFDEF Overlay}
{$O+}
{$F+}
{$ENDIF}

INTERFACE

USES Crt,                                       { Standard CRT Library. }
     Dos,                                       { Standard DOS Library. }
     DOS2,
     Strg,                                      { String Library. }
     Int,                                       { Integer Library. }
     EIO,                                       { Extended IO Library. }
     WND,
     DFA,
     Menu,
     Types,
     Galaxy,
     DataCnst,
     Misc,                                      { Miscellaneous. }
     Primintr,                                  { Primitives procedures. }
     Intrface,                                  { Interface procedures. }
     Environ,
     LoadSave,
     NPETypes,
     NPE;

TYPE
   EmpireNameArray = ARRAY [Empire] OF String32;
   SexArray = ARRAY [Empire] OF Boolean;

PROCEDURE InputEmpireName(NewEmp: Empire; x,y: Byte;
                          VAR EmpireName: EmpireNameArray;
                          VAR Name: String32;
                          VAR Password: String8;
                          VAR IsAnEmpress: Boolean);
PROCEDURE StartNewGame(VAR Exit: Boolean);

IMPLEMENTATION


USES
   SWindows;
(*
   Artifact,
   Transact,
   CdeTypes,
   Code;
*)

CONST
   MaxNoOfScenarios = 20;

(* Pre-defined empire names ------------------------------------------------- *)
   NoOfRndNames = 59;
   RndEmpireName: ARRAY [1..NoOfRndNames] OF String16 =
      ( 'Aaraavon','Antramis','Azores',
        'Bok','Brekandi','Byzantium',
        'Cal''Dulmas','Cerberon','Chulron',
        'Dol Parem','Doramis','Drii',
        'Earon','Entares','Esperance',
        'Fahron','First Sun','Freberon',
        'Geldtried','Gen-Tarem','Ghaza',
        'Haar','Hasarem','Highguard','Horace',
        'Iileron','Illissia',
        'Jamin','Jasper','Jool Den',
        'Kandii','Kendrezani',
        'Lazarus','Lililth',
        'Moorline','Mu','Mutara',
        'Ny','N''zares',
        'Occem','Ovaris',
        'Palanhoth','Pell','Pharo',
        'Quezelquan',
        'Rho Kandii','Rosseri',
        'Sarlok','Sol-Terra',
        'Terminus','Terra','Trantor',
        'Ultarion',
        'Vex','Vlandis',
        'Whorl',
        'Xi',
        'Yew','Yolandis' );

   MaxNoOfXYPoints = 25;

(*
   SituationKeyword: ARRAY [SituationTypes] OF String16 = (
      '',
      'ACTIVE',
      'CLOSEUP',
      'UPDATE' );

   ActionKeyword: ARRAY [ActionTypes] OF String16 = (
      '',
      'CASE',
      'ELSE',
      'END',
      'IF',
      'SWITCH',
      'WHILE',

      'ANYKEY',
      'ASSIGN',
      'CLS',
      'CREATE',
      'DEBUGDUMP',
      'DESTRUCT',
      'DISPLAY',
      'GETARTIFACTCOORD',
      'GETOBJECTPOWER',
      'ISEQUAL',
      'ISGREATER',
      'ISLESSER',
      'ISNOTEQUAL',
      'MENUADDFLEETS',
      'MENUDISPLAY',
      'MENUINITIALIZE',
      'NULL',
      'WINGAME' );

   ActionParms: ARRAY [ActionTypes] OF Byte = (
      { NoACT }               0,
      { CASE }                2,
      { ELSE }                0,
      { END }                 0,
      { IF }                  1,
      { SWITCH }              0,
      { WHILE }               1,

      { AnyKeyACT }           0,
      { AssignACT }           2,
      { ClsACT }              0,
      { CreateACT }           2,
      { DebugDumpACT }        0,
      { DestructACT }         1,
      { DisplayACT }          2,
      { GetArtifactCoordACT } 2,
      { GetObjectPowerACT }   2,
      { IsEqualACT }          3,
      { IsGreaterACT }        3,
      { IsLesserACT }         3,
      { IsNotEqualACT }       3,
      { MenuAddFleetsACT }    3,
      { MenuDisplayACT }      2,
      { MenuInitializeACT }   1,
      { NullACT }             0,
      { WinGameACT }          1 );
*)

TYPE
   FilenameArray = ARRAY [1..MaxNoOfScenarios] OF String16;
   CapitalArray = ARRAY [Empire] OF IDNumber;
   ClassArray = ARRAY [1..100] OF WorldClass;
   TechArray = ARRAY [1..100] OF TechLevel;

   ZoneRecord = RECORD
      x1,y1,x2,y2: Byte;
   END;
   ZoneArray = ARRAY [1..20] OF ZoneRecord;

   XYPointRecord = RECORD
      Name: String8;
      XY: XYCoord;
   END;
   XYPointArray = ARRAY [1..MaxNoOfXYPoints] OF XYPointRecord;

VAR
   ScenarioError: Boolean;
	DebugScena: Boolean;
   ScenaVersion: Word;

PROCEDURE GetRandomEmpireName(VAR EmpireName: EmpireNameArray;
                              VAR NewName: String32);
{ GetRandomEmpireName:
   This procedure will return a random name from RndEmpireName that is
   not in EmpireName. }

   VAR
      Emp: Empire;
      Ok: Boolean;

   BEGIN
   REPEAT
      Ok:=True;

      NewName:=RndEmpireName[Rnd(1,NoOfRndNames)];
      FOR Emp:=Empire1 TO Empire8 DO
         IF EmpireName[Emp]=NewName THEN
            Ok:=False;

   UNTIL Ok;
   END;  { GetRandomEmpireName }

PROCEDURE ScenaError(Line: LineStr);
   BEGIN
   WriteLn(Line);
   ScenarioError:=True;
   END;  { ScenaError }

PROCEDURE SkipText(VAR SF: Text);
   VAR
      Line: LineStr;

   BEGIN
   REPEAT
      ReadLn(SF,Line);
      AllUpCase(Line);
   UNTIL (Pos('ENDTEXT',Line)<>0) OR (EoF(SF));
   END;  { SkipText }

FUNCTION NextInteger(VAR SF: Text): Integer;
   VAR
      Token: LineStr;
      Temp,Error: Integer;
      BadToken: Boolean;

   BEGIN
   Token:=DFA1NextToken(SF,BadToken);
   IF BadToken THEN
      ScenaError('ERROR: Bad token "'+Token+'"')
   ELSE
      BEGIN
      Val(Token,Temp,Error);
      IF Error=0 THEN
         NextInteger:=Temp
      ELSE
         ScenaError('ERROR: Illegal number format "'+Token+'"');
      END;
   END;  { NextInteger }

PROCEDURE GetRandomXY(VAR XY: XYCoord; x1,y1,x2,y2: Integer; CheckWorld: Boolean);
   VAR
      ObjectThere: IDNumber;
      Count: Word;

   BEGIN
   Count:=0;
   REPEAT
      XY.x:=Rnd(x1,x2);
      XY.y:=Rnd(y1,y2);
      GetObject(XY,ObjectThere);
      Inc(Count);
   UNTIL (NOT CheckWorld) OR (Count>100) 
         OR ((ObjectThere.ObjTyp=Void) 
         AND (EnemyMine(XY)=Indep)
         AND (GetNebula(XY)<>DenseNebula));

   IF Count>100 THEN
      BEGIN
      XY:=Limbo;
      ScenaError('ERROR: No room for random world in zone.');
      END;
   END;  { GetRandomXY }

PROCEDURE GetRandomRange(Line: String16; VAR Low,High: Integer);
   VAR
      Error,DashPos: Word;

   BEGIN
   Low:=0;
   High:=0;
   DashPos:=Pos('..',Line);
   IF DashPos=0 THEN
      BEGIN
      Val(Line,Low,Error);
      IF Error<>0 THEN
         Low:=0
      ELSE
         High:=Low;
      END
   ELSE
      BEGIN
      Val(Copy(Line,1,DashPos-1),Low,Error);
      IF Error=0 THEN
         BEGIN
         Val(Copy(Line,DashPos+2,10),High,Error);
         IF Error<>0 THEN
            BEGIN
            Low:=0;
            High:=0;
            Exit;
            END;
         END
      ELSE
         Low:=0;
      END;
   END;  { GetRandomRange }

PROCEDURE GetNextXY(VAR SF: Text; VAR XYPoint: XYPointArray; VAR Zone: ZoneArray;
                    CheckWorlds: Boolean; VAR RetXY: XYCoord);
   VAR
      Token: LineStr;
      i,CommaPos,ColonPos,Error,Z: Word;
      TempX1,TempY1,TempX2,TempY2: Integer;
      ObjectThere: IDNumber;
      Header: String8;
      BadToken: Boolean;

   PROCEDURE GetXY(Line: LineStr; VAR x,y: Integer);
      VAR
         Error: Word;
         CommaPos: Word;

      BEGIN
      IF Line='' THEN
         BEGIN
         x:=0;
         y:=0;
         END
      ELSE
         BEGIN
         CommaPos:=Pos(',',Line);
         Val(Copy(Line,1,CommaPos-1),x,Error);
         IF Error<>0 THEN
            BEGIN
            x:=0;
            y:=0;
            ScenaError('ERROR: Illegal coordinate "'+Line+'"');
            END
         ELSE
            BEGIN
            Val(Copy(Line,CommaPos+1,5),y,Error);
            IF Error<>0 THEN
               BEGIN
               x:=0;
               y:=0;
               ScenaError('ERROR: Illegal coordinate "'+Line+'"');
               END;
            END;
         END;
      END;  { GetXY }

   BEGIN
   Token:=DFA1NextToken(SF,BadToken);
   IF BadToken THEN
      ScenaError('ERROR: Bad token "'+Token+'"')
   ELSE
      BEGIN
      CommaPos:=Pos(',',Token);
      ColonPos:=Pos(':',Token);
      Header:=Copy(Token,1,2);
      Header[1]:=UpCase(Header[1]);
      IF Header='Z:' THEN
         BEGIN
         Val(Copy(Token,3,5),Z,Error);
         IF Error<>0 THEN
            BEGIN
            RetXY:=Limbo;
            ScenaError('ERROR: Illegal zone coordinate "'+Token+'"');
            END
         ELSE
            BEGIN
            WITH Zone[Z] DO
               GetRandomXY(RetXY,x1,y1,x2,y2,CheckWorlds);
            END;
         END
      ELSE IF Header='R:' THEN
         BEGIN
         GetRandomRange(Copy(Token,3,CommaPos-3),TempX1,TempX2);
         GetRandomRange(Copy(Token,CommaPos+1,10),TempY1,TempY2);
         IF InGalaxy(TempX1,TempY1) AND InGalaxy(TempX2,TempY2) THEN
            GetRandomXY(RetXY,TempX1,TempY1,TempX2,TempY2,CheckWorlds)
         ELSE
            BEGIN
            RetXY:=Limbo;
            ScenaError('ERROR: Illegal random coordinates "'+Token+'"');
            END
         END
      ELSE IF ColonPos<>0 THEN
         BEGIN
         GetRandomRange(Copy(Token,ColonPos+1,(CommaPos-ColonPos)-1),TempX1,TempX2);
         GetRandomRange(Copy(Token,CommaPos+1,10),TempY1,TempY2);
         Token:=Copy(Token,1,ColonPos-1);
         AllUpCase(Token);
         i:=MaxNoOfXYPoints;
         WHILE (i>0) AND (Token<>XYPoint[i].Name) DO
            Dec(i);

         IF i<>0 THEN
            BEGIN
            TempX1:=XYPoint[i].XY.x+TempX1;
            TempX2:=XYPoint[i].XY.x+TempX2;
            TempY1:=XYPoint[i].XY.y+TempY1;
            TempY2:=XYPoint[i].XY.y+TempY2;
            IF InGalaxy(TempX1,TempY1) AND InGalaxy(TempX2,TempY2) THEN
               GetRandomXY(RetXY,TempX1,TempY1,TempX2,TempY2,CheckWorlds)
            ELSE
               BEGIN
               RetXY:=Limbo;
               ScenaError('ERROR: Relative coordinates outside of galaxy.');
               END
            END
         ELSE
            BEGIN
            RetXY:=Limbo;
            ScenaError('ERROR: XYPoint not found "'+Token+'"');
            END;
         END
      ELSE IF CommaPos<>0 THEN
         BEGIN
         GetXY(Token,TempX1,TempY1);
         IF InGalaxy(TempX1,TempY1) THEN
            BEGIN
            RetXY.x:=TempX1;
            RetXY.y:=TempY1;
            END
         ELSE
            BEGIN
            RetXY:=Limbo;
            ScenaError('ERROR: Absolute coordinates outside of galaxy.');
            END;
         END
      ELSE
         BEGIN
         ScenaError('ERROR: Illegal coordinate "'+Token+'"');
         END;
      END;
   END;  { GetNextXY }

(*ARIFACT
PROCEDURE GetLocation(VAR Immediate: VariableRecord; VAR Loc: Location);
   BEGIN
   IF Immediate.VType=IDVRT THEN
      BEGIN
      Loc.ID:=Immediate.ID;
      GetCoord(Loc.ID,Loc.XY);
      END
   ELSE
      BEGIN
      Loc.ID:=EmptyQuadrant;
      Loc.XY:=Immediate.XY
      END;
   END;  { GetLocation }

PROCEDURE EvaluateImmediate(VAR Immediate: VariableRecord; Line: LineStr);
   VAR
      Temp,Delim,Error: Integer;

   BEGIN
   WITH Immediate DO
      BEGIN
      IF Copy(Line,1,4)='TRUE' THEN
         BEGIN
         VType:=BoolVRT;
         Bool:=True;
         END
      ELSE IF Copy(Line,1,5)='FALSE' THEN
         BEGIN
         VType:=BoolVRT;
         Bool:=False;
         END
      ELSE IF Copy(Line,1,6)='EMPIRE' THEN
         BEGIN
         VType:=EmpVRT;
         Val(Copy(Line,7,10),Temp,Error);
         Emp:=Empire(Temp);
         END
      ELSE IF Copy(Line,1,5)='INDEP' THEN
         BEGIN
         VType:=EmpVRT;
         Emp:=Indep;
         END
      ELSE IF Copy(Line,1,5)='WORLD' THEN
         BEGIN
         VType:=IDVRT;
         Val(Copy(Line,6,10),Temp,Error);
         ID.ObjTyp:=Pln;
         ID.Index:=Temp;
         END
      ELSE IF Copy(Line,1,5)='FLEET' THEN
         BEGIN
         VType:=IDVRT;
         Val(Copy(Line,6,10),Temp,Error);
         ID.ObjTyp:=Flt;
         ID.Index:=Temp;
         END
      ELSE IF Line[1]='I' THEN
         BEGIN
         VType:=IDVRT;
         Delete(Line,1,1);
         Delim:=Pos(':',Line);

         Val(Copy(Line,1,Delim-1),Temp,Error);
         ID.ObjTyp:=ObjectTypes(Temp);
         Val(Copy(Line,Delim+1,255),Temp,Error);
         ID.Index:=Temp;
         END
      ELSE IF Line[1]='X' THEN
         BEGIN
         VType:=XYVRT;
         Delete(Line,1,1);
         Delim:=Pos(',',Line);

         Val(Copy(Line,1,Delim-1),Temp,Error);
         XY.x:=Temp;
         Val(Copy(Line,Delim+1,255),Temp,Error);
         XY.y:=Temp;
         END
      ELSE
         BEGIN
         VType:=ScalarVRT;
         Val(Line,Scalar,Error);
         END;
      END;
   END;  { EvaluateImmediate }

PROCEDURE GetParameter(VAR Parm: Byte; VAR Immediate: VariableRecord; Line: LineStr);
   VAR
      Error,Value: Integer;

   BEGIN
   AllUpCase(Line);
   IF Line[1]='R' THEN
      BEGIN
      Delete(Line,1,1);
      Val(Line,Value,Error);
      Parm:=Value+1;
      END
   ELSE IF Line[1]='V' THEN
      BEGIN
      Delete(Line,1,1);
      Val(Line,Value,Error);
      Parm:=Value+12;
      END
   ELSE
      BEGIN
      EvaluateImmediate(Immediate,Line);
      Parm:=11;
      END;
   END;  { GetParameter }

PROCEDURE CodeCompiler(VAR SF: TEXT; VAR Code: ActionArrayPtr);
   VAR
      Temp: ActionArrayPtr;
      k,Level,Acts: Word;
      Line: LineStr;
      BadToken: Boolean;

   BEGIN
   GetMem(Temp,1000*SizeOf(ActionRecord));
   Level:=1;
   Acts:=0;

   REPEAT
      Inc(Acts);
      WITH Temp^[Acts] DO
         BEGIN
         Line:=DFA1NextToken(SF,BadToken);
         AllUpCase(Line);
         AType:=LastACT;
         WHILE (AType<>NoACT) AND (Line<>ActionKeyword[AType]) DO
            Dec(AType);

         IF AType=NoACT THEN
            ScenaError('ERROR: Unknown action "'+Line+'".');

         CASE AType OF
            SwitchCODE,CaseCODE,IfCODE,ElseCODE,WhileCODE: Inc(Level);
            EndCODE: Dec(Level);
         END;

         FOR k:=1 TO ActionParms[AType] DO
            BEGIN
            Line:=DFA1NextToken(SF,BadToken);
            GetParameter(Parm[k],Immediate,Line);
            END;
         END;  { with scope }
   UNTIL (Level=0) OR (ScenarioError);

   GetMem(Code,Acts*SizeOf(ActionRecord));
   Move(Temp^,Code^,Acts*SizeOf(ActionRecord));
   FreeMem(Temp,1000*SizeOf(ActionRecord));
   END;  { CodeCompiler }

PROCEDURE DefineNewArtifact(VAR SF: TEXT);
   VAR
      BadToken: Boolean;
      NoOfAttr: Byte;
      Line: LineStr;
      i,j: Word;

      ArtType,ArtSize,ArtPieceOf,ArtDesc: Word;
      ArtSits: SetOfSitu;
      ArtName: String32;
      ArtAttr: SetOfAttr;
      SType: SituationTypes;
      Code: ActionArrayPtr;

   BEGIN
   ArtType:=NextInteger(SF);
   ArtName:=DFA1NextToken(SF,BadToken);
   ArtDesc:=NextInteger(SF);

   NoOfAttr:=NextInteger(SF);
   ArtAttr:=[];

   ArtSize:=NextInteger(SF);
   ArtPieceOf:=NextInteger(SF);

   ArtSits:=[];
   CodeCompiler(SF,Code);

   DefineArtifact(ArtType,ArtName,ArtAttr,ArtSize,ArtPieceOf,ArtDesc,ArtSits,Code);
   END;  { DefineNewArtifact }

PROCEDURE CreateNewArtifact(VAR SF: TEXT);
   VAR
      i,Art: Word;
      BadToken: Boolean;
      Line: LineStr;
      Immediate: VariableRecord;
      ArtID: IDNumber;
      ArtType: Word;
      ArtLoc: Location;
      ArtGlobal: RegisterArray;

   BEGIN
   Art:=NextInteger(SF);
   IF Art>LastArtifact THEN
      LastArtifact:=Art;

   ArtID.ObjTyp:=ArtOBJ;
   ArtID.Index:=Art;

   ArtType:=NextInteger(SF);

   Line:=DFA1NextToken(SF,BadToken);
   EvaluateImmediate(Immediate,Line);
   GetLocation(Immediate,ArtLoc);

   FOR i:=0 TO 9 DO
      BEGIN
      Line:=DFA1NextToken(SF,BadToken);
      EvaluateImmediate(ArtGlobal[i],Line);
      END;

   CreateArtifact(ArtID,ArtType,ArtLoc,ArtGlobal);
   END;  { CreateNewArtifact }

PROCEDURE Artifacts(VAR SF: TEXT);
   VAR
      BadToken: Boolean;
      Comm: String32;

   BEGIN
   AllocateArtifactTypes(NextInteger(SF));
   REPEAT
      Comm:=DFA1NextToken(SF,BadToken);
      AllUpCase(Comm);
      IF Comm='DEFINEARTIFACT' THEN
         DefineNewArtifact(SF)
      ELSE IF Comm='CREATEARTIFACT' THEN
         CreateNewArtifact(SF)
   UNTIL (Comm='ENDARTIFACTS') OR (ScenarioError);
   END;  { Artifacts }

PROCEDURE DefineNewTransaction(VAR SF: TEXT);
   VAR
      Line: String32;
      BadToken: Boolean;
      Temp: VariableRecord;
      Code: ActionArrayPtr;

   BEGIN
   Line:=DFA1NextToken(SF,BadToken);
   AllUpCase(Line);
   EvaluateImmediate(Temp,Line);
   IF (Temp.VType=IDVRT) AND (Temp.ID.ObjTyp=Pln) THEN
      BEGIN
      CodeCompiler(SF,Code);
      DefineTransaction(Temp.ID,Code);
      END
   ELSE
      ScenaError('ERROR: World ID expected "'+Line+'"');
   END;  { DefineNewTransaction }

PROCEDURE Transactions(VAR SF: TEXT);
   VAR
      BadToken: Boolean;
      Comm: String32;

   BEGIN
   REPEAT
      Comm:=DFA1NextToken(SF,BadToken);
      AllUpCase(Comm);
      IF Comm='DEFINETRANSACTION' THEN
         DefineNewTransaction(SF)
      ELSE IF Comm='TEXT' THEN
         SkipText(SF);
   UNTIL (Comm='ENDTRANSACTIONS') OR (ScenarioError);
   END;  { Transactions }
*)

PROCEDURE SetTrillumReserves(VAR SF: TEXT; VAR TriRes: Word);
   BEGIN
   TriRes:=NextInteger(SF);
   IF (TriRes<0) OR (TriRes>100) THEN
      ScenaError('ERROR: Illegal trillum reserve setting.');
   END;  { SetTrillumReserves }

PROCEDURE LoadClassArray(VAR SF: TEXT; VAR CA: ClassArray);
{ LoadClassArray:
   This procedure sets up the class distribution table from the data given
   in the file 'SF'  The procedure assumes that the file is open and that
   it is at the first line of the class table. }
   VAR
      ClsI: WorldClass;
      i,Index,Prob: Word;

   BEGIN
	IF DebugScena THEN
		WriteLn('DEBUG: ClassTable');  

   Index:=1;
   FOR ClsI:=AmbCls TO VlcCls DO
    IF (ScenaVersion>13) OR (ClsI<>TerCls) THEN 
      BEGIN
      Prob:=NextInteger(SF);
      FOR i:=Index TO Index+Prob-1 DO
         CA[i]:=ClsI;
      Index:=Index+Prob;
      END;

   IF Index<>101 THEN
      ScenaError('ERROR: Class table probabilities do not add up to 100.');
   END;  { LoadClassArray }

PROCEDURE LoadTechArray(VAR SF: TEXT; VAR TA: TechArray);
{ LoadTechArray:
   This procedure sets up the tech distribution table from the data given
   in the file 'SF'  The procedure assumes that the file is open and that
   it is at the first line of the tech table. }
   VAR
      TchI: TechLevel;
      i,Index,Prob: Word;

   BEGIN
	IF DebugScena THEN
		WriteLn('DEBUG: TechTable');

   Index:=1;
   FOR TchI:=PreTchLvl TO GteTchLvl DO
      BEGIN
      Prob:=NextInteger(SF);
      FOR i:=Index TO Index+Prob-1 DO
         TA[i]:=TchI;
      Index:=Index+Prob;
      END;

   IF Index<>101 THEN
      ScenaError('ERROR: Tech table probabilities do not add up to 100.');
   END;  { LoadTechArray }

PROCEDURE DefineXYPoint(VAR SF: TEXT; VAR XYPoint: XYPointArray; VAR Zone: ZoneArray);
   VAR
      NewXYPoint: String8;
      NewXY: XYCoord;
      i: Word;
      BadToken: Boolean;
      Token: LineStr;

   BEGIN
   i:=MaxNoOfXYPoints;
   WHILE (i>0) AND (XYPoint[i].Name<>'') DO
      Dec(i);

   Token:=DFA1NextToken(SF,BadToken);
   IF BadToken THEN
      ScenaError('ERROR: Bad token "'+Token+'"')
   ELSE
      BEGIN
      NewXYPoint:=Token;
      GetNextXY(SF,XYPoint,Zone,False,NewXY);

      IF i=0 THEN
         ScenaError('ERROR: Too many XYPoints defined.')
      ELSE
         BEGIN
         AllUpCase(NewXYPoint);
         XYPoint[i].Name:=NewXYPoint;
         XYPoint[i].XY:=NewXY;

			IF DebugScena THEN
				WriteLn('DEBUG: DefineXY (',XYPoint[i].XY.x,',',XYPoint[i].XY.y,')');

         END;
      END;
   END;  { DefineXYPoint }

PROCEDURE DefineZone(VAR SF: TEXT; VAR XYPoint: XYPointArray; VAR Zone: ZoneArray);
   VAR
      ZNumber: Word;
      XY1,XY2: XYCoord;

   BEGIN
   ZNumber:=NextInteger(SF);
   GetNextXY(SF,XYPoint,Zone,False,XY1);
   GetNextXY(SF,XYPoint,Zone,False,XY2);
   WITH Zone[ZNumber] DO
      BEGIN
      x1:=XY1.x;
      y1:=XY1.y;
      x2:=XY2.x;
      y2:=XY2.y;

		IF DebugScena THEN
			WriteLn('DEBUG: DefineZone ',ZNumber,'  (',x1,',',y1,' ',x2,',',y2,')');
      END;
   END;  { DefineZone }

FUNCTION RandomTrillumReserves(Cls: WorldClass; RegionReserves: Word): Word;
   VAR
      Temp: Word;

   BEGIN
   Temp:=GreaterInt(Integer(RegionReserves)+Rnd(-25,25),0);
   RandomTrillumReserves:=Round((Temp)*(TriResByClass[Cls]/100)+Rnd(1,100));
   END;  { RandomTrillumReserves }

PROCEDURE RndShips(Nfgt,Nhkr,Njmp,Njtn,Npen,Nstr,Ntrn: Integer;
                   Tech: TechLevel; PerCentVar: Byte;
                   CheckTech: Boolean;
                   VAR Ships: ShipArray);
   VAR
      ShpI: ShipTypes;

   BEGIN
   Ships[fgt]:=ThgLmt(RndVar(Nfgt,PerCentVar));
   Ships[hkr]:=ThgLmt(RndVar(Nhkr,PerCentVar));
   Ships[jmp]:=ThgLmt(RndVar(Njmp,PerCentVar));
   Ships[jtn]:=ThgLmt(RndVar(Njtn,PerCentVar));
   Ships[pen]:=ThgLmt(RndVar(Npen,PerCentVar));
   Ships[ssp]:=ThgLmt(RndVar(Nstr,PerCentVar));
   Ships[trn]:=ThgLmt(RndVar(Ntrn,PerCentVar));

   IF CheckTech THEN
      FOR ShpI:=fgt TO trn DO
         IF NOT (ShpI IN TechDev[Tech]) THEN
            Ships[ShpI]:=0;
   END;  { RndShips }

PROCEDURE RndCargo(Nmen,Nnnj,Namb,Nche,Nmet,Nsup,Ntri: Integer;
                   Tech: TechLevel; PerCentVar: Byte;
                   CheckTech: Boolean;
                   VAR Cargo: CargoArray);
   VAR
      CarI: CargoTypes;

   BEGIN
   Cargo[men]:=ThgLmt(RndVar(Nmen,PerCentVar));
   Cargo[nnj]:=ThgLmt(RndVar(Nnnj,PerCentVar));
   Cargo[amb]:=ThgLmt(RndVar(Namb,PerCentVar));
   Cargo[che]:=ThgLmt(RndVar(Nche,PerCentVar));
   Cargo[met]:=ThgLmt(RndVar(Nmet,PerCentVar));
   Cargo[sup]:=ThgLmt(RndVar(Nsup,PerCentVar));
   Cargo[tri]:=ThgLmt(RndVar(Ntri,PerCentVar));

   IF CheckTech THEN
      FOR CarI:=men TO tri DO
         IF NOT (CarI IN TechDev[Tech]) THEN
            Cargo[CarI]:=0;
   END;  { RndCargo }

PROCEDURE RndDefns(NLAM,Ndef,NGDM,Nion: Integer;
                   Tech: TechLevel; PerCentVar: Byte;
                   CheckTech: Boolean;
                   VAR Defns: DefnsArray);
   VAR
      DefI: DefnsTypes;

   BEGIN
   Defns[LAM]:=ThgLmt(RndVar(NLAM,PerCentVar));
   Defns[def]:=ThgLmt(RndVar(Ndef,PerCentVar));
   Defns[GDM]:=ThgLmt(RndVar(NGDM,PerCentVar));
   Defns[ion]:=ThgLmt(RndVar(Nion,PerCentVar));

   IF CheckTech THEN
      FOR DefI:=LAM TO ion DO
         IF NOT (DefI IN TechDev[Tech]) THEN
            Defns[DefI]:=0;
   END;  { RndDefns }

PROCEDURE SetUpWorld(WorldID: IDNumber;
                     Cls: WorldClass;
                     Tech: TechLevel;
                     Typ: WorldTypes;
                     Emp: Empire;
                     Pop: Population;
                     Eff: Index;
                     Special: SetOfSpecialConditions;
                     Nfgt,Nhkr,Njmp,Njtn,Npen,Nstr,Ntrn: Integer;
                     Nmen,Nnnj,Namb,Nche,Nmet,Nsup,Ntri: Integer;
                     NLAM,Ndef,NGDM,Nion: Integer;
                     CheckTech: Boolean);
   VAR
      Indus: IndusArray;
      Ships: ShipArray;
      Cargo: CargoArray;
      Defns: DefnsArray;
      XY: XYCoord;

   BEGIN
   SetClass(WorldID,Cls);
   SetTech(WorldID,Tech);
   SetType(WorldID,Typ);
   SetStatus(WorldID,Emp);
   SetPopulation(WorldID,Pop);
   SetEfficiency(WorldID,Eff);
   SetSpecial(WorldID,Special);
   GetCoord(WorldID,XY);
   Scout(Emp,XY);
   GetOptimumIndus(WorldID,Indus);
   PutIndus(WorldID,Indus);
   RndShips(Nfgt,Nhkr,Njmp,Njtn,Npen,Nstr,Ntrn,Tech,20,CheckTech,Ships);
   PutShips(WorldID,Ships);
   RndCargo(Nmen,Nnnj,Namb,Nche,Nmet,Nsup,Ntri,Tech,20,CheckTech,Cargo);
   PutCargo(WorldID,Cargo);
   RndDefns(NLAM,Ndef,NGDM,Nion,Tech,20,CheckTech,Defns);
   PutDefns(WorldID,Defns);
   END;  { SetUpWorld }

PROCEDURE CreateRndPlanet(ID: IDNumber; Coord: XYCoord;
                          Class: WorldClass;  T: TechLevel);
{ CreateRndPlanet: }

   CONST
      RndMilTechAdj: ARRAY [TechLevel] OF Real =
         {   pt     p    pa     a    pw     w     j     b     s    pg     g }
         ( 0.01, 0.02, 0.04, 0.05, 0.10, 0.30, 0.35, 0.60, 0.75, 0.95, 1.00 );

   VAR
      MI: Integer;
      Eff: Index;
      Pop: Population;

   { CreateRndPlanet: MAIN PROCEDURE }
   BEGIN
   CreatePlanet(ID,Coord);
   IF (Class = TerCls) THEN
     SetTerraformTarget(ID, EthCls);
   Eff:=Rnd(40,60);
   Pop:=RndVar(Trunc((1+(Eff-50)/500)*BasePop[T]), 10);
   MI:=Rnd(1,33)+Rnd(1,34)+Rnd(1,33);
   MI:=Round(MI*RndMilTechAdj[T]);

   SetUpWorld(ID,Class,T,IndTyp,Indep,Pop,Eff,[],
              80*MI, 7*MI, 10*MI, 6*MI, 4*MI, MI, 30*MI,
              40*MI, 0, 0, 30*MI, 50*MI, 25*MI, 10*MI,
              0, 30*MI, 50*MI, 40*MI,True);
   IF (Class = TerCls) THEN
     SetTerraformTarget(ID, EthCls);
   END;  { CreateRndPlanet }

PROCEDURE Pause;
   VAR
   Ch: Char;

   BEGIN
   IF DebugScena THEN
     BEGIN
     WriteLn('Press any key ...');
     REPEAT UNTIL KeyPressed;
     END;
   END;

PROCEDURE CreateWorld(VAR SF: TEXT;
                      VAR FirstWorld: Word;
                      VAR NoOfPlayers: Empire;
                      VAR XYPoint: XYPointArray;
                      VAR Zone: ZoneArray);
   VAR
      n,x,y,C,Tl,T,E,Pp,Ef: Integer;
      ObjID: IDNumber;
      XY: XYCoord;
      NLAM,Ndef,NGDM,Nion,Nfgt,Nhkr,Njmp,Njtn,Npen,Nssp,Ntrn,
      Nmen,Nnnj,Namb,Nche,Nmet,Nsup,Ntri: Integer;
      Emp: Empire;
      Typ: WorldTypes;
      Reserves,TriRes: Word;

   BEGIN
   n:=NextInteger(SF);

	IF DebugScena THEN
		WriteLn('DEBUG: CreateWorld ',n);

   GetNextXY(SF,XYPoint,Zone,True,XY);
   C:=NextInteger(SF);
     IF (ScenaVersion < 14) AND (C>=20) THEN Inc(C); { Change the class from Terraforming }
   Tl:=NextInteger(SF);
   T:=NextInteger(SF);
   E:=NextInteger(SF);
   Pp:=NextInteger(SF);
   Ef:=NextInteger(SF);
   IF ScenaVersion>=12 THEN
      TriRes:=NextInteger(SF)
   ELSE
      TriRes:=100;
   NLAM:=NextInteger(SF);
   Ndef:=NextInteger(SF);
   NGDM:=NextInteger(SF);
   Nion:=NextInteger(SF);
   Nfgt:=NextInteger(SF);
   Nhkr:=NextInteger(SF);
   Njmp:=NextInteger(SF);
   Njtn:=NextInteger(SF);
   Npen:=NextInteger(SF);
   Nssp:=NextInteger(SF);
   Ntrn:=NextInteger(SF);
   Nmen:=NextInteger(SF);
   Nnnj:=NextInteger(SF);
   Namb:=NextInteger(SF);
   Nche:=NextInteger(SF);
   Nmet:=NextInteger(SF);
   Nsup:=NextInteger(SF);
   Ntri:=NextInteger(SF);

   ObjID.ObjTyp:=Pln;
   ObjID.Index:=FirstWorld;
   Inc(FirstWorld);
   Emp:=Empire(E);
   Typ:=WorldTypes(T);
   IF (Emp<>Indep) AND (NOT EmpireActive(Emp)) THEN
      BEGIN
      Emp:=Indep;
      Typ:=IndTyp;
      END;

   CreatePlanet(ObjID,XY);
   SetUpWorld(ObjID,WorldClass(C),TechLevel(Tl),Typ,Emp,RndVar(Pp,15),Ef,[],
              Nfgt,Nhkr,Njmp,Njtn,Npen,Nssp,Ntrn,
              Nmen,Nnnj,Namb,Nche,Nmet,Nsup,Ntri,
              NLAM,Ndef,NGDM,Nion,False);
   Reserves:=RandomTrillumReserves(WorldClass(C),TriRes);
   PutTrillumReserves(ObjID,Reserves);

   IF Typ=CapTyp THEN
      SetCapital(Emp,ObjID);
   END; { CreateWorld }

PROCEDURE CreateBase(VAR SF: TEXT;
                     VAR FirstBase: Word;
                     VAR NoOfPlayers: Empire;
                     VAR XYPoint: XYPointArray;
                     VAR Zone: ZoneArray);
   VAR
      n,x,y,C,Tl,T,E,Pp,Ef,STyp: Integer;
      ObjID: IDNumber;
      XY: XYCoord;
      NLAM,Ndef,NGDM,Nion,Nfgt,Nhkr,Njmp,Njtn,Npen,Nssp,Ntrn,
      Nmen,Nnnj,Namb,Nche,Nmet,Nsup,Ntri: Integer;
      Emp: Empire;
      Typ: WorldTypes;
		BaseTyp: StarbaseTypes;

   BEGIN
   n:=NextInteger(SF);

	IF DebugScena THEN
		WriteLn('DEBUG: CreateStarbase ',n);

   GetNextXY(SF,XYPoint,Zone,True,XY);
   STyp:=NextInteger(SF);
   Tl:=NextInteger(SF);
   T:=NextInteger(SF);
   E:=NextInteger(SF);
   Pp:=NextInteger(SF);
   Ef:=NextInteger(SF);
   NLAM:=NextInteger(SF);
   Ndef:=NextInteger(SF);
   NGDM:=NextInteger(SF);
   Nion:=NextInteger(SF);
   Nfgt:=NextInteger(SF);
   Nhkr:=NextInteger(SF);
   Njmp:=NextInteger(SF);
   Njtn:=NextInteger(SF);
   Npen:=NextInteger(SF);
   Nssp:=NextInteger(SF);
   Ntrn:=NextInteger(SF);
   Nmen:=NextInteger(SF);
   Nnnj:=NextInteger(SF);
   Namb:=NextInteger(SF);
   Nche:=NextInteger(SF);
   Nmet:=NextInteger(SF);
   Nsup:=NextInteger(SF);
   Ntri:=NextInteger(SF);

   ObjID.ObjTyp:=Base;
   ObjID.Index:=FirstBase;
   Emp:=Empire(E);
	BaseTyp:=StarbaseTypes(STyp);

	IF BaseTyp=out THEN
		Typ:=OutTyp
	ELSE IF BaseTyp IN [cmm,frt] THEN
		Typ:=BseTyp
	ELSE
		Typ:=WorldTypes(T);

   IF (Emp=Indep) OR EmpireActive(Emp) THEN
      BEGIN
      Inc(FirstBase);
      CreateStarbase(ObjID,Emp,XY,BaseTyp);
      SetUpWorld(ObjID,ArtCls,TechLevel(Tl),Typ,Emp,RndVar(Pp,15),Ef,[],
                 Nfgt,Nhkr,Njmp,Njtn,Npen,Nssp,Ntrn,
                 Nmen,Nnnj,Namb,Nche,Nmet,Nsup,Ntri,
                 NLAM,Ndef,NGDM,Nion,False);

      IF Typ=CapTyp THEN
         SetCapital(Emp,ObjID);
      END;
   END; { CreateBase }

PROCEDURE CreateGate(VAR SF: TEXT;
                     NoOfPlayers: Empire;
                     VAR XYPoint: XYPointArray;
                     VAR Zone: ZoneArray);
   VAR
      XY: XYCoord;
      GTyp: StargateTypes;
      Emp: Empire;
      ID: IDNumber;

   BEGIN
   GetNextXY(SF,XYPoint,Zone,True,XY);
   GTyp:=StargateTypes(NextInteger(SF));
   Emp:=Empire(NextInteger(SF));
   ID.ObjTyp:=Gate;
   ID.Index:=NextStargateSlot;
   IF ID.Index>0 THEN
      CreateStargate(ID,Emp,GTyp,XY)
   ELSE
      ScenaError('ERROR: Too many stargates created.');
   END;  { CreateGate }

PROCEDURE CreateRandomWorlds(VAR SF: TEXT;
                             VAR FirstWorld: Word;
                             VAR ClassTable: ClassArray;
                             VAR TechTable: TechArray;
                             TriRes: Word;
                             VAR Zone: ZoneArray);
   VAR
      Safety,i,NoOfWorlds: Word;
      ObjID: IDNumber;
      Coord: XYCoord;
      Class: WorldClass;
      Tech: TechLevel;
      Reserves,ZNumber: Word;

   BEGIN
   NoOfWorlds:=NextInteger(SF);
   ZNumber:=NextInteger(SF);

   ObjID.ObjTyp:=Pln;
   FOR i:=FirstWorld TO (FirstWorld+NoOfWorlds-1) DO
      BEGIN
      ObjID.Index:=i;
      WITH Zone[ZNumber] DO
         GetRandomXY(Coord,x1,y1,x2,y2,True);
      Safety:=0;
      REPEAT
         Class:=ClassTable[Rnd(1,100)];
         Tech:=TechTable[Rnd(1,100)];
         Inc(Safety);
      UNTIL (Safety>100) OR (Tech>=MinTechForClass[Class]);
      IF Safety>100 THEN
         BEGIN
         ScenaError('ERROR: Incompatible class and tech tables.');
         END;
      CreateRndPlanet(ObjID,Coord,Class,Tech);
      Reserves:=RandomTrillumReserves(Class,TriRes);
      PutTrillumReserves(ObjID,Reserves);
      GotoXY(1,WhereY);
      END;

   FirstWorld:=FirstWorld+NoOfWorlds;
   END;  { CreateRandomWorlds }

PROCEDURE ReadModifierList(VAR SF: TEXT; VAR Modifiers: SetOfEmpireModifiers);
   VAR
      i,NoOfModifiers: Word;
      Abort: Boolean;
      ModToken: String16;

   BEGIN
   Modifiers:=[];
   IF ScenaVersion>=12 THEN
      BEGIN
      NoOfModifiers:=NextInteger(SF);
      FOR i:=1 TO NoOfModifiers DO
         BEGIN
         ModToken:=DFA1NextToken(SF,Abort);
         AllUpCase(ModToken);
         IF ModToken='CENTRAL' THEN
            Modifiers:=Modifiers+[CentralEMD];
         END;
      END;
   END;  { ReadModifierList }

PROCEDURE CreatePlayerEmpire(VAR SF: TEXT; NoOfPlayers: Empire;
                             VAR EmpireName,Password: EmpireNameArray;
                             VAR Sex: SexArray;
			     VAR EmpsCreated: Word);
   VAR
      i,Pl,Tl,NoOfTechs,RevFactor: Integer;
      Emp: Empire;
      Tech: TechLevel;
      CapitalID: IDNumber;
      XY: XYCoord;
      KnownTechs: TechnologySet;
      Modifiers: SetOfEmpireModifiers;

   BEGIN
   Pl:=NextInteger(SF);
   RevFactor:=NextInteger(SF);
   Tl:=NextInteger(SF);
   Tech:=TechLevel(Tl);
   KnownTechs:=TechDev[Pred(Tech)];
   NoOfTechs:=NextInteger(SF);
   FOR i:=1 TO NoOfTechs DO
      KnownTechs:=KnownTechs+[TechnologyTypes(NextInteger(SF))];
   KnownTechs:=KnownTechs * TechDev[Tech];

   ReadModifierList(SF,Modifiers);

   Emp:=Empire(Pl);
   IF (EmpireName[Emp] <> '') THEN  { In other words, if the empire name has been filled in }
      BEGIN
      CreateEmpire(Emp,True,Sex[Emp],EmpireName[Emp],Password[Emp],
                   EmptyQuadrant,Tech,KnownTechs,RevFactor,Modifiers,Year);
      Inc(EmpsCreated);
      END;
   END;  { CreatePlayerEmpire }

PROCEDURE CreateNPEmpire(VAR SF: TEXT; VAR EmpireName: EmpireNameArray; VAR NextEmp: Word);
   VAR
      i,E,ET,Tl,NoOfTechs,RevFactor: Integer;
      Emp: Empire;
      Tech: TechLevel;
      CapitalID: IDNumber;
      XY: XYCoord;
      ETyp: NPEmpireTypes;
      Name: String32;
      BadToken: Boolean;
      KnownTechs: TechnologySet;
      Modifiers: SetOfEmpireModifiers;

   BEGIN
   E:=NextInteger(SF);
   ET:=NextInteger(SF);
   Name:=DFA1NextToken(SF,BadToken);
   RevFactor:=NextInteger(SF);
   Tl:=NextInteger(SF);
   Tech:=TechLevel(Tl);
   NoOfTechs:=NextInteger(SF);
   KnownTechs:=TechDev[Pred(Tech)];
   FOR i:=1 TO NoOfTechs DO
      KnownTechs:=KnownTechs+[TechnologyTypes(NextInteger(SF))];
   KnownTechs:=KnownTechs * TechDev[Tech];

   ReadModifierList(SF,Modifiers);

   Emp:=Empire(E);
   ETyp:=NPEmpireTypes(ET);

   IF NOT EmpireActive(Emp) THEN
      BEGIN
      IF Name='RndName' THEN
         GetRandomEmpireName(EmpireName,Name);

      CreateEmpire(Emp,False,Boolean(Rnd(0,1)),Name,'',
                   EmptyQuadrant,Tech,KnownTechs,RevFactor,Modifiers,Year);
      InitializeNPE(Emp,ETyp);
      Inc(NextEmp);
      END;
   END;  { CreateNPEmpire }

PROCEDURE NebulaeBand;
   VAR
      InitX,StartX,XDisp,x,y: Integer;
      Coord: XYCoord;

   BEGIN
   InitX:=Rnd(1,SizeOfGalaxy);
   { InitX is the x-coordinate on which the strip of nebula
     will start.  It will go from the top down and tilt either
     to the left or the right. }

   IF InitX<=(SizeOfGalaxy DIV 4) THEN
      XDisp:=Rnd(0,3)
   ELSE IF InitX>=(SizeOfGalaxy*3 DIV 4) THEN
      XDisp:=Rnd(-3,0)
   ELSE
      XDisp:=Rnd(-3,3);

   StartX:=InitX;
   FOR y:=1 TO SizeOfGalaxy DO
      BEGIN
      FOR x:=StartX-Rnd(1,5) TO StartX+Rnd(1,5) DO
         IF InGalaxy(x,y) THEN
            BEGIN
            Coord.x:=x;  Coord.y:=y;
            PutNebula(Coord,Nebula);
            END;

      StartX:=StartX+XDisp;
      END;  { loop }
   END;  { NebulaeBand }

PROCEDURE NebulaePatches(NoOfPatches: Word);
   VAR
      Patch: Byte;
      InitX,InitY,x,y: Integer;
      Coord: XYCoord;

   BEGIN
   FOR Patch:=1 TO NoOfPatches DO
      BEGIN
      InitX:=Rnd(1,SizeOfGalaxy);
      InitY:=Rnd(1,SizeOfGalaxy);
      FOR y:=InitY-Rnd(1,3) TO InitY+Rnd(1,3) DO
         FOR x:=InitX-Rnd(1,4-Abs(y-InitY)) TO
                InitX+Rnd(1,4-Abs(y-InitY)) DO
            IF InGalaxy(x,y) THEN
               BEGIN
               Coord.x:=x;  Coord.y:=y;
               PutNebula(Coord,Nebula);
               END;  { loop }
      END;  { loop }
   END;  { NebulaePatches }

PROCEDURE CreateNebula(VAR SF: TEXT; VAR XYPoint: XYPointArray; VAR Zone: ZoneArray);
   VAR
      XY,UpperLeft,LowerRight: XYCoord;
      NTyp,x,y: Word;

   BEGIN
   NTyp:=NextInteger(SF);
   GetNextXY(SF,XYPoint,Zone,False,UpperLeft);
   GetNextXY(SF,XYPoint,Zone,False,LowerRight);
   FOR x:=UpperLeft.x TO LowerRight.x DO
      FOR y:=UpperLeft.y TO LowerRight.y DO
         IF InGalaxy(x,y) THEN
            BEGIN
            XY.x:=x;  XY.y:=y;
            PutNebula(XY,NebulaTypes(NTyp));
            END;
   END;  { CreateNebula }

PROCEDURE CreateRandomNebula(VAR SF: TEXT);
   VAR
      Typ,Min,Max: Word;

   BEGIN
   Typ:=NextInteger(SF);
   Min:=NextInteger(SF);
   Max:=NextInteger(SF);
   CASE Typ OF
      1: BEGIN
         NebulaeBand;
         END;
      2: BEGIN
         NebulaePatches(Rnd(Min,Max));
         END;
   END;  { case }
   END;  { CreateRandomNebula }

PROCEDURE CreateSRMs(VAR SF: TEXT; VAR XYPoint: XYPointArray; VAR Zone: ZoneArray);
   VAR
      XY,UpperLeft,LowerRight: XYCoord;
      E,x,y: Word;
      ObjID: IDNumber;

   BEGIN
   E:=NextInteger(SF);
   GetNextXY(SF,XYPoint,Zone,False,UpperLeft);
   GetNextXY(SF,XYPoint,Zone,False,LowerRight);
   FOR x:=UpperLeft.x TO LowerRight.x DO
      FOR y:=UpperLeft.y TO LowerRight.y DO
         IF InGalaxy(x,y) THEN
            BEGIN
            XY.x:=x;  XY.y:=y;
            GetObject(XY,ObjID);
            IF ObjID.ObjTyp=Void THEN
               PutMine(XY,Empire(E));
            END;
   END;  { CreateSRMs }

PROCEDURE SkipDescriptions(VAR SF: TEXT);
   VAR
      Line: LineStr;

   BEGIN
   REPEAT
      ReadLn(SF,Line);
      AllUpCase(Line);
   UNTIL (Pos('ENDDESCRIPTION',Line)<>0) OR EoF(SF);

   IF EoF(SF) THEN
      BEGIN
      ScenaError('ERROR: EndDescription not found.');
      END;
   END;  { SkipDescriptions }

PROCEDURE ScenarioIntroduction(VAR SF: TEXT;
                               MinPlay,MaxPlay: Word;
                               VAR NoOfPlayers: Empire;
                               VAR InputPos: Word);
   VAR
      Line: ARRAY [1..25] OF LineStr;
      i,LineNo: Word;
      TempStr: LineStr;
      BadToken,EndText: Boolean;

	FUNCTION NoChoice: Empire;
		VAR
			NPStr: String8;
			Ch: Char;
			Line: LineStr;

		BEGIN
		Str(MinPlay,NPStr);

     	WriteHelpLine('<Esc>:Exit');
		Line:='This is a scenario for '+NPStr+' player';
		IF MinPlay>1 THEN
			Line:=Line+'s';

		WriteString(Line,1,LineNo,C.SYSDispWind);
		WriteString('Press any key to begin or <Esc> to Exit...',1,LineNo+1,C.SYSDispWind);
		GotoXY(43,LineNo+1);
		GetChoice(AnyKey,NoCaseDistinct,Ch);

		IF Ch=ESCKey THEN
			NoChoice:=Indep
		ELSE
			NoChoice:=Empire(MinPlay-1);

		Inc(LineNo);
		END;  { NoChoice }

	FUNCTION GetNoOfPlayers: Empire;
		VAR
			Ok: Boolean;
			NPStr,MinStr,MaxStr: String8;
			NoOfPlayers: Empire;
			BadStr,NPInt: Integer;

		BEGIN
   	REPEAT
      	Ok:=True;
      	WriteHelpLine('<Esc>:Exit');
      	NPStr:='';
      	Str(MinPlay,MinStr);
      	Str(MaxPlay,MaxStr);

      	InputString('How many players ('+MinStr+'-'+MaxStr+') ? ',1,LineNo,C.SYSDispWind,1,NPStr);
      	WriteBlanks(70,5,LineNo+1,C.SYSDispWind);

      	IF NPStr=ESCKey THEN
         	BEGIN
         	NoOfPlayers:=Indep;
         	END
      	ELSE
         	BEGIN
         	Val(NPStr,NPInt,BadStr);

         	IF BadStr<>0 THEN
            	BEGIN
            	WriteString('Please enter a valid number or <Esc> to exit.',5,LineNo+1,C.SYSDispWind);
            	Ok:=False;
            	END
         	ELSE IF (NPInt<MinPlay) OR (NPInt>MaxPlay) THEN
            	BEGIN
            	WriteString('Please enter a number between '+MinStr+' and '+MaxStr+' or <Esc> to exit.',
                        	5,LineNo+1,C.SYSDispWind);
            	Ok:=False;
            	END
         	ELSE
            	BEGIN
            	NoOfPlayers:=Empire((NPInt-1)+Ord(Empire1));
            	END;
         	END;
	   UNTIL Ok;

		GetNoOfPlayers:=NoOfPlayers;
		END;  { GetNoOfPlayers }

   PROCEDURE ReadPage(VAR EndText: Boolean);
      VAR
         BadToken: Boolean;
         TempStr: LineStr;

      BEGIN
      LineNo:=0;
      REPEAT
         Inc(LineNo);
         ReadLn(SF,Line[LineNo]);
      UNTIL (Pos('ENDTEXT',Line[LineNo])<>0) 
            OR (Pos('NEWPAGE',Line[LineNo])<>0);

      IF Pos('ENDTEXT',Line[LineNo])<>0 THEN
         EndText:=True
      ELSE
         EndText:=False;
      END;  { ReadPage }

   BEGIN
   REPEAT
      TempStr:=DFA1NextToken(SF,BadToken);
      AllUpCase(TempStr);
   UNTIL TempStr='BEGINTEXT';
   ReadLn(SF);

   REPEAT
      ClrScr;
      ReadPage(EndText);
      FOR i:=1 TO LineNo-1 DO
         WriteString(Line[i],1,i,C.SYSDispWind);
      IF NOT EndText THEN
         PressAnyKey(40,22,'Press any key to continue...');
   UNTIL EndText;
   Inc(LineNo);

   { Get number of players }
	IF MinPlay<MaxPlay THEN
		NoOfPlayers:=GetNoOfPlayers
	ELSE
		NoOfPlayers:=NoChoice;

   InputPos:=LineNo+2;
   END;  { ScenarioIntroduction }

PROCEDURE InputEmpireName(NewEmp: Empire; x,y: Byte;
                          VAR EmpireName: EmpireNameArray;
                          VAR Name: String32;
                          VAR Password: String8;
                          VAR IsAnEmpress: Boolean);

   VAR
      Emp: Empire;
      Prompt: String32;
      RndName: String32;
      ESCHit,Ok,PassOk: Boolean;
      PasW: Byte;
      temp: String8;

   PROCEDURE SuggestionsWindow(VAR SuggName: String32);
      VAR
         NameMenu: MenuStructure;
         i: Byte;
         Ok: Boolean;
         Ch: Char;

      BEGIN
      InitializeMenu(NameMenu);
      
      FOR i:=1 TO NoOfRndNames DO
         AddMenuLine(NameMenu,RndEmpireName[i]);

      DisplayMenu(NameMenu,40,1,C.CommWind,C.SYSDispSelect,20,20);
      WriteHelpLine(',:Move cursor <Enter>:Select <Esc>:Exit');

      REPEAT
         Ok:=False;
         GetChoice(AnyKey,NoCaseDistinct,Ch);
         CASE Ch OF
            ESCKey: BEGIN
               SuggName:='';
               Ok:=True;
               END;

            ReturnKey: BEGIN
               SuggName:=RndEmpireName[GetMenuSelect(NameMenu)];
               Ok:=True;
               END;

            ELSE
               ActivateMenu(NameMenu,Ch);
         END;  { case }
      UNTIL Ok;

      CleanUpMenu(NameMenu);
      END;  { SuggestionsWindow }

   { InputEmpireName: MAIN PROCEDURE }
   BEGIN
   Str(Ord(NewEmp)-Ord(Empire1)+1,Prompt);
   Prompt:='Name of player empire #'+Prompt+' : ';
   Name:='';
   Password:='';

   GetRandomEmpireName(EmpireName,RndName);

   REPEAT
      Ok:=True;
      WriteHelpLine('<Esc>:Suggestions  Default:"'+RndName+'"');
      InputString(Prompt,x,y,C.SYSDispWind,32,Name);
      IF Name=ESCKey THEN
         BEGIN
         SuggestionsWindow(Name);
         Ok:=False;
         END
      ELSE IF Name='' THEN
        BEGIN
        WriteString(Prompt+RndName,x,y,C.SYSDispWind);
        Name:=RndName;
        END;

      Name[1]:=UpCase(Name[1]);
   UNTIL Ok;

   { get sex }
   OpenWindow(20,12,50,7,ThinBRD,'',C.CommWind,C.SYSWBorder,PasW);
   temp:='';
   InputString('Are you male or female (m/f) ? ',1,1,C.CommWind,6,temp);
   IF UpCase(temp[1])='M' THEN
      IsAnEmpress:=False
   ELSE IF UpCase(temp[1])='F' THEN
      IsAnEmpress:=True
   ELSE
      IsAnEmpress:=Boolean(Rnd(0,1));
   CloseWindow;

   { get password }
   OpenWindow(20,12,50,7,ThinBRD,'Password',C.CommWind,C.SYSWBorder,PasW);
   WriteHelpLine('');
   REPEAT
      PassOk:=True;
      Write('Password : ');
      InputPassword(Password,ESCHit);
      IF ESCHit=True THEN
         Password:='';
      WriteLn;
      WriteLn('Please type in your password again to check.');
      Write('Password : ');
      InputPassword(temp,ESCHit);
      IF ESCHit=True THEN
         temp:='';
      WriteLn;
      IF temp<>Password THEN
         BEGIN
         WriteLn;
         WriteLn('Please enter your password again.');
         PassOk:=False;
         END;
   UNTIL PassOk;
   CloseWindow;
   END;  { InputEmpireName }

FUNCTION CheckSum(VAR TextFile: TEXT): Word;
   VAR
      Sum: Word;
      TempCh: Char;

   BEGIN
   Reset(TextFile);
   Sum:=0;
   REPEAT
      Read(TextFile,TempCh);
      Inc(Sum,Ord(TempCh));
   UNTIL EoF(TextFile);
   CheckSum:=Sum;
   Reset(TextFile);
   END;  { CheckSum }

PROCEDURE RandomizePlayers(Next,Max: Word; 
                           VAR EmpNames: EmpireNameArray;
                           VAR Passes: EmpireNameArray; 
                           VAR Sexes: SexArray);
   VAR
    i,j: Word;
    Order: Array[0..7] OF Word;  { This assumes a MaxNoOfEmpires of Empire8 }
    TmpNames,TmpPasses: EmpireNameArray;
    TmpSex: SexArray;
    OK: Boolean;
    
   BEGIN
     IF DebugScena THEN
       BEGIN
         WriteLn('DEBUG: RandomizePlayers');
         FOR i:=(Next) TO Max DO
           WriteLn(i,':  ',EmpNames[Empire(i)]);
       END;
     IF ((Next)<Max) THEN  { That is, more than 1 player empire has yet to be made }
       BEGIN
         Order[Next]:=Rnd((Next),Max);
         FOR i:=(Next+1) TO Max DO
           REPEAT
             OK:=True;
             Order[i]:=Rnd((Next),Max);
             FOR j:=(Next) TO (i-1) DO
               IF Order[j]=Order[i] THEN
                 OK:=False;
           UNTIL (OK = True);  { That is, repeat the randomization process for each i  }
                               { until it doesn't repeat any previous value in Order[] }
 	
         { Now, each number from Next to Max should have a unique place in   }
         { Order[(Next)..Max]. So now let's shuffle stuff according to that. }          
 	
         FOR i:=(Next) TO Max DO
           BEGIN
             TmpNames[Empire(i)]:=EmpNames[Empire(Order[i])];
             TmpPasses[Empire(i)]:=Passes[Empire(Order[i])];
             TmpSex[Empire(i)]:=Sexes[Empire(Order[i])];
           END;
         FOR i:=(Next) TO Max DO
  	   BEGIN
             EmpNames[Empire(i)]:=TmpNames[Empire(i)];
             Passes[Empire(i)]:=TmpPasses[Empire(i)];
             Sexes[Empire(i)]:=TmpSex[Empire(i)];
             IF DebugScena THEN
               WriteLn(i,':  ',EmpNames[Empire(i)]);
           END;
       END;
   END;
          
        

PROCEDURE LoadScenario(Filename: LineStr; VAR Abort: Boolean);
   VAR
      ScenaFile: TEXT;
      Title,Vers: String64;
      Line: LineStr;
      BadToken: Boolean;
      WindH: WindowHandle;
      Dummy,FirstYear,PosY,MinPlay,MaxPlay,SoG,NoP,Diff,MinLen,MaxLen: Word;

      FirstWorld,FirstBase,NextEmpToCreate: Word;
      Emp,NoOfPlayers: Empire;
      EmpireName,Password: EmpireNameArray;
      Sex: SexArray;
      NewName: String32;
      NewPass: String8;
      CapitalID: CapitalArray;
      ClassTable: ClassArray;
      TechTable: TechArray;
      Zone: ZoneArray;
      XYPoint: XYPointArray;
      Seed: Integer;
      NewSex: Boolean;
      TriRes,Sum: Word;

   BEGIN
   Assign(ScenaFile,Filename);

   {$IFDEF Demo}
   Sum:=CheckSum(ScenaFile);
   IF (Sum<>43171) AND (Sum<>6792) AND (Sum<>44304) THEN
      BEGIN
      Abort:=False;
      AttentionWindow('"'+Filename+'" is an illegal','scenario file',Abort);
      Exit;
      END;
   {$ENDIF}

   Reset(ScenaFile);
   ReadLn(ScenaFile,Vers);
   Val(Copy(Vers,10,2),ScenaVersion,Dummy);

   Title:=DFA1NextToken(ScenaFile,BadToken);
   Seed:=NextInteger(ScenaFile);
   MinPlay:=NextInteger(ScenaFile);
   MaxPlay:=NextInteger(ScenaFile);
   SoG:=NextInteger(ScenaFile);
   NoP:=NextInteger(ScenaFile);
   Diff:=NextInteger(ScenaFile);
   MinLen:=NextInteger(ScenaFile);
   MaxLen:=NextInteger(ScenaFile);
   FirstYear:=NextInteger(ScenaFile);
   OpenWindow(1,1,80,24,ThinBRD,Title,C.SYSDispWind,C.SYSWBorder,WindH);

   InitializeUniverse(FirstYear,SoG,NoP);
   ScenarioIntroduction(ScenaFile,MinPlay,MaxPlay,NoOfPlayers,PosY);
   ScenarioError:=False;
	DebugScena:=False;
   IF (NoOfPlayers<>Indep) THEN
      BEGIN
      FillChar(EmpireName,SizeOf(EmpireName),0);
      FillChar(Password,SizeOf(Password),0);
      FillChar(Zone,SizeOf(Zone),0);
      FillChar(XYPoint,SizeOf(XYPoint),0);
      FirstWorld:=1;
      FirstBase:=1;
      NextEmpToCreate:=0;
      FOR Emp:=Empire1 TO NoOfPlayers DO
         BEGIN
         InputEmpireName(Emp,1,PosY+Ord(Emp)-Ord(Empire1),EmpireName,NewName,NewPass,NewSex);
         EmpireName[Emp]:=NewName;
         Password[Emp]:=NewPass;
         Sex[Emp]:=NewSex;
         END;

      ClrScr;
      Writeln;
      WriteLn('Please wait while the universe is created...');
      WriteLn;
      Zone[1].x1:=1;
      Zone[1].y1:=1;
      Zone[1].x2:=SoG;
      Zone[1].y2:=SoG;

      TriRes:=100;

      IF Seed=0 THEN
         Randomize
      ELSE
         RandSeed:=Seed;

      REPEAT
         Line:=DFA1NextToken(ScenaFile,BadToken);
         IF BadToken THEN
            ScenaError('ERROR: Bad command token "'+Line+'"')
         ELSE
            BEGIN
            AllUpCase(Line);
				IF Line='DEBUGSCENARIO' THEN
					DebugScena:=True
            ELSE IF Line='BEGINDESCRIPTION' THEN
               SkipDescriptions(ScenaFile)
(*ARTIFACTS
            ELSE IF Line='BEGINARTIFACTS' THEN
               Artifacts(ScenaFile)
{
            ELSE IF Line='BEGINVICTORYCONDITIONS' THEN
               VictoryConditions(ScenaFile)
}
            ELSE IF Line='BEGINTRANSACTIONS' THEN
               Transactions(ScenaFile)
*)
            ELSE IF Line='CLASSTABLE' THEN
               LoadClassArray(ScenaFile,ClassTable)
            ELSE IF Line='CREATENEBULA' THEN
               CreateNebula(ScenaFile,XYPoint,Zone)
            ELSE IF Line='CREATERANDOMNEBULA' THEN
               CreateRandomNebula(ScenaFile)
            ELSE IF Line='RANDOMIZEPLAYERS' THEN
               RandomizePlayers(NextEmpToCreate,MaxPlay-1,EmpireName,Password,Sex)
            ELSE IF Line='CREATESRMS' THEN
               CreateSRMs(ScenaFile,XYPoint,Zone)
            ELSE IF Line='CREATEPLAYEREMPIRE' THEN
               CreatePlayerEmpire(ScenaFile,NoOfPlayers,EmpireName,Password,Sex,NextEmpToCreate)
            ELSE IF Line='CREATENPEMPIRE' THEN
               CreateNPEmpire(ScenaFile,EmpireName,NextEmpToCreate)
            ELSE IF Line='CREATERANDOMWORLDS' THEN
               CreateRandomWorlds(ScenaFile,FirstWorld,ClassTable,TechTable,TriRes,Zone)
            ELSE IF Line='CREATEWORLD' THEN
               CreateWorld(ScenaFile,FirstWorld,NoOfPlayers,XYPoint,Zone)
            ELSE IF Line='CREATESTARBASE' THEN
               CreateBase(ScenaFile,FirstBase,NoOfPlayers,XYPoint,Zone)
            ELSE IF Line='CREATESTARGATE' THEN
               CreateGate(ScenaFile,NoOfPlayers,XYPoint,Zone)
            ELSE IF Line='DEFINEZONE' THEN
               DefineZone(ScenaFile,XYPoint,Zone)
            ELSE IF Line='DEFINEXY' THEN
               DefineXYPoint(ScenaFile,XYPoint,Zone)
            ELSE IF Line='REPORT' THEN
               WriteLn(DFA1NextToken(ScenaFile,BadToken))
            ELSE IF Line='PAUSE' THEN
               Pause
            ELSE IF Line='TECHTABLE' THEN
               LoadTechArray(ScenaFile,TechTable)
            ELSE IF Line='SETTRILLUMRESERVES' THEN
               SetTrillumReserves(ScenaFile,TriRes)
            ELSE IF Line<>'ENDSCENARIO' THEN
               ScenaError('ERROR: Unknown command "'+Line+'"');
            END;
      UNTIL (Line='ENDSCENARIO') OR EoF(ScenaFile) OR ScenarioError;

      Player:=Empire1;
      Abort:=False;
      END
   ELSE
      Abort:=True;

   Close(ScenaFile);

   IF ScenarioError THEN
      BEGIN
      WriteLn;
      PressAnyKey(1,22,'Press any key to continue...');
      Abort:=True;
      END;

   CloseWindow;
   END;  { LoadScenario }

PROCEDURE GetScenarios(Path: String64;
                       VAR Menu: MenuStructure;
                       VAR Filename: FilenameArray;
                       VAR NoFiles: Boolean);
{ GetScenarios:
   This procedure will search the given directory path for *.SCN files.  Any
   that are ANACREON scenario files are added to the menu and the filename 
   array. }
   CONST
      DiffStr: ARRAY [0..3] OF String16 =
         ( 'Beginner      ',
           'Intermediate  ',
           'Advanced      ',
           'Expert        ' );

   VAR
      Search: String64;
      Attr: Word;
      S: SearchRec;
      Scena: TEXT;
      MinPlay,MaxPlay,Diff,MinLen,MaxLen,Dummy: Word;
      MinStr,MaxStr: String8;
      Ana,Temp,Title: String32;
      Line: LineStr;
      LineNo: Word;
      BadToken: Boolean;

   BEGIN
   Search:=AddDefaultPath(Path,'*.SCN');
   Attr:=Archive+ReadOnly;
   LineNo:=1;
   NoFiles:=True;

   FindFirst(Search,Attr,S);
   WHILE DosError=0 DO
      BEGIN
      Assign(Scena,AddDefaultPath(Path,S.Name));
      Reset(Scena);
      ReadLn(Scena,Ana);
      IF Copy(Ana,1,8)='ANACREON' THEN
         BEGIN
         NoFiles:=False;
         Title:=DFA1NextToken(Scena,BadToken);
         Dummy:=NextInteger(Scena);
         MinPlay:=NextInteger(Scena);
         MaxPlay:=NextInteger(Scena);
         Dummy:=NextInteger(Scena);
         Dummy:=NextInteger(Scena);
         Diff:=NextInteger(Scena);
         MinLen:=NextInteger(Scena);
         MaxLen:=NextInteger(Scena);
         Dummy:=NextInteger(Scena);
         Close(Scena);

         AdjustString(Title,23);
         Str(MinPlay,MinStr);
         Str(MaxPlay,MaxStr);
         IF MinPlay=MaxPlay THEN
            Temp:=MinStr+' Player'
         ELSE
            Temp:=MinStr+'-'+MaxStr+' Player';
         IF MaxPlay>1 THEN
            Temp:=Temp+'s';
         AdjustString(Temp,11);

         Line:=Title+' '+DiffStr[Diff]+Temp+' ';

         Str(MinLen,MinStr);
         Str(MaxLen,MaxStr);
         IF MaxLen=0 THEN
            Temp:=MinStr+'+ years'
         ELSE
            Temp:=MinStr+'-'+MaxStr+' years';

         Line:=Line+Temp;
         AddMenuLine(Menu,Line);
         Filename[LineNo]:=S.Name;
         Inc(LineNo);
         END;  { if }

      FindNext(S);
      END;
   END;  { GetScenarios }

PROCEDURE StartNewGame(VAR Exit: Boolean);
   VAR
      Menu: MenuStructure;
      ExitMenu,ExitScenario,ChoseScenario: Boolean;
      ScenarioToPlay: Byte;
      Ch: Char;
      Filename: FilenameArray;
      Abort,NoFiles: Boolean;

   { StartNewGame: MAIN PROCEDURE }
   BEGIN
   Exit:=False;
   REPEAT
      ClrScr;
      InitializeMenu(Menu);
      GetScenarios(SceDirect,Menu,Filename,NoFiles);
      IF NoFiles THEN
         BEGIN
         Abort:=False;
         AttentionWindow('There are no Anacreon scenario','files in "'+SceDirect+'".',Abort);
         Exit:=True;
         END
      ELSE
         BEGIN
         WriteString('Please select a scenario:',1,2,C.CommWind);
         WriteString('Name                 Difficulty      Players    Duration',7,5,C.CommWind);
         DisplayMenu(Menu,4,7,C.CommWind,C.SYSDispSelect,65,10);
         WriteHelpLine(',:Move cursor <Enter>:Select <Esc>:Exit');

         ExitMenu:=False;
         REPEAT
            GetChoice(AnyKey,NoCaseDistinct,Ch);
            CASE Ch OF
               ESCKey: BEGIN
                  ExitMenu:=True;
                  Exit:=True;
                  END;

               ReturnKey : BEGIN
                  ScenarioToPlay:=GetMenuSelect(Menu);
                  ExitMenu:=True;
                  END;

               ELSE
                  ActivateMenu(Menu,Ch);
            END;  { case }
         UNTIL ExitMenu;

         CleanUpMenu(Menu);
         WriteHelpLine('');
         END;

      ClrScr;

      IF NOT Exit THEN
         BEGIN
         ExitScenario:=False;
         LoadScenario(AddDefaultPath(SceDirect,Filename[ScenarioToPlay]),ExitScenario);
         ChoseScenario:=NOT ExitScenario;
         ScenaFilename:=Filename[ScenarioToPlay];
         END;
   UNTIL (ChoseScenario) OR (Exit);

   END;  { StartNewGame }

END.
