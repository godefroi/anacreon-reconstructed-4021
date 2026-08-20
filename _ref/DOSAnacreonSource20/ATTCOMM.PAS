(* ATTCOMM.PAS -----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT AttComm;

{$IFDEF Overlay}
{$O+,F+}
{$ENDIF}

INTERFACE

USES Crt,                                       { Standard CRT Library. }
     Strg,                                      { String Library. }
     Int,                                       { Integer Library. }
     EIO,                                       { Extended IO Library. }
     WND,
     Menu,
     Types,
     Galaxy,
     DataCnst,
     News,
     Misc,                                      { Miscellaneous procedures. }
     PrimIntr,                                  { Primitives. }
     Intrface,                                  { Interface. }
     Fleet,
     SWindows,                                  { Status windows. }
     MapWind,
     Display,                                   { Display procedures. }
     Attack,
     Scena,
     LoadSave;

PROCEDURE AttackCommand(FltID: IDNumber);
PROCEDURE AutoAttackCommand(FltID: IDNumber);

IMPLEMENTATION

USES
   Environ,
	AttNPE;

CONST

   { The following constants are used in the attack displays. }
   EdgeOfScreen = 968;
   TopCornerLoc = 160;

   ATSymb: array[AttackTypes] of Char =
      '-LDGIFHJTPSRMN';

   TypN: array[AttackTypes] of String8 =
      ( '','','','','','fgt sq','hk    ','jmpshp','jmptrn',
        'pentr ','strshp','trnspt','GAT   ','ninja ' );

   PosN: array[ShellPos] of String8 =
      ( 'DSp','Hi ','Std','Sub','Sur' );

   OrbLoc: array[ShellPos] of Integer =
      ( 990,1024,1054,1078,1100 );

   EnemyOffset: array[1..12] of Integer =
      ( 4,6,166,-154,8,168,-152,-312,328,10,170,-150 );
   PlayerOffset: array[1..10] of Integer =
      ( -2,-4,-164,156,-6,154,-166,-326,314,-8 );

VAR
   AttackWindow,EnemyWindow,GroupWindow: WindowHandle;

PROCEDURE AttReport(Line: LineStr);
   VAR
      x: Byte;
      OldLine: LineStr;
      PosI: ShellPos;

   BEGIN
   ActivateWindow(AttackWindow);
   GotoXY(1,2);   ClrEOL;
   Mem[ScrSeg:OrbLoc[DpSpc]-4*160]:=250;
   Mem[ScrSeg:OrbLoc[HiOrb]-4*160+2]:=250;
   Mem[ScrSeg:OrbLoc[Orbit]-4*160+4]:=250;
   Mem[ScrSeg:OrbLoc[Grnd]-4*160]:=250;

   x:=40-(Length(Line) div 2);
   GotoXY(x,2);   Write(Line);

   if Line<>'' then
      Delay(1000);
   end;  { AttReport }

PROCEDURE WarpIn(VAR Gp: GroupArray; G: Byte; NewPos: ShellPos);
   VAR
      Str,Fin,j,TimeDelay,temp,Dist: Integer;

   BEGIN
   IF NewPos=DpSpc THEN
      BEGIN
      Str:=968;
      TimeDelay:=15;
      END
   ELSE
      BEGIN
      Str:=OrbLoc[Pred(NewPos)];
      TimeDelay:=40;
      END;

   Str:=Str+PlayerOffset[G];
   Fin:=OrbLoc[NewPos]+PlayerOffset[G];
   Dist:=(Fin-Str) div 2;

   WITH Gp[G] DO
      BEGIN
      Mem[ScrSeg:Str]:=32;
      FOR j:=0 TO Dist DO
         BEGIN
         temp:=Mem[ScrSeg:Str+2*j];
         Mem[ScrSeg:Str+2*j]:=16;
         Delay(TimeDelay);
         Mem[ScrSeg:Str+2*j]:=temp;
         END;
      IF NewPos<>Grnd THEN
         Mem[ScrSeg:Fin]:=16;
      END;  { with scope }
   END;  { WarpIn }

PROCEDURE WarpOut(VAR Gp: GroupArray; G: Byte; NewPos: ShellPos);
   VAR
      Str,Fin,j,temp,Dist: Integer;

   BEGIN
   Str:=OrbLoc[Succ(NewPos)]+PlayerOffset[G];
   Fin:=OrbLoc[NewPos]+PlayerOffset[G];
   Dist:=(Str-Fin) DIV 2;

   WITH Gp[G] DO
      BEGIN
      Mem[ScrSeg:Str]:=32;
      FOR j:=0 TO Dist DO
         BEGIN
         temp:=Mem[ScrSeg:Str-2*j];
         Mem[ScrSeg:Str-2*j]:=16;
         Delay(40);
         Mem[ScrSeg:Str-2*j]:=temp;
         END;
      Mem[ScrSeg:Fin]:=16;
      END;  { with scope }
   END;  { WarpOut }

PROCEDURE EnemyStatus(En: EnemyArray);
   VAR
      Total: ARRAY [ShellPos] OF LongInt;

   PROCEDURE UpdateEnemyWindow;
      VAR
         ShpI: AttackTypes;
         PosI: ShellPos;
			Line: LineStr;
			Temp: STRING [8];

      BEGIN
      ActivateWindow(EnemyWindow);
		FillChar(Total,SizeOf(Total),0);

      FOR ShpI:=fgt TO trn DO
         BEGIN
			Line:='';
         FOR PosI:=DpSpc TO Grnd DO
            BEGIN
				Str(En[PosI][ShpI]:4,Temp);
				Line:=Line+Temp;
            IF PosI<>Grnd THEN
					Line:=Line+' ';

            Inc (Total[PosI], En[PosI][ShpI]);
            END;

			WriteString(Line,24,Ord(ShpI)-Ord(fgt)+2,C.EnemyWind);
         END;

      GotoXY(9,10);   Write(En[SbOrb][GDM]:4);
      GotoXY(23,10);  Write(En[Orbit][def]:4);
      GotoXY(37,10);  Write(En[SbOrb][ion]:4);
      GotoXY(9,11);   Write(En[Grnd][men]:4);
      GotoXY(23,11);  Write(En[Grnd][nnj]:4);
      GotoXY(37,11);  Write(En[SbOrb][LAM]:4);
      end;  { UpdateEnemyWindow }

   procedure DrawEnemyShips;
      var
         PosI: ShellPos;
         NoOfShapes,i: Byte;

      begin
      ActivateWindow(AttackWindow);

      for PosI:=DpSpc to SbOrb do
         begin
         for i:=1 to 12 do
            Mem[ScrSeg:OrbLoc[PosI]+EnemyOffset[i]]:=32;

         IF Total[PosI]>0 THEN
            BEGIN
            NoOfShapes:=Round(Total[PosI]/500)+1;
            IF NoOfShapes>12 THEN
               NoOfShapes:=12;

            FOR i:=1 TO NoOfShapes DO
               Mem[ScrSeg:OrbLoc[PosI]+EnemyOffset[i]]:=17;
            END;

         end;
      end;  { DrawEnemyShips }

   { EnemyStatus: MAIN PROCEDURE }
   BEGIN
   UpdateEnemyWindow;
   DrawEnemyShips;
   END;  { EnemyStatus }

PROCEDURE Engage(NoOfGroups: Byte;
                 VAR Gp: GroupArray;
                 VAR En: EnemyArray;
                 CombatData: CombatDataRecord;
                 VAR Casualties,Killed: AttackArray;
                 VAR Result: AttackResultTypes);

   VAR
      Comm: Char;
      Ok,EndBattle: Boolean;
      Details: DetailArray;

   procedure Menu(var Comm: Char);
      begin
      ActivateWindow(GroupWindow);   ClrScr;
      Writeln;
      Writeln('   <E>ngage');
      Writeln('   <M>ove');
      Writeln('   <G>roup status');
      Writeln('   <T>arget');
      Writeln('   <D>etails');
      Writeln('   <R>etreat');
      Writeln;
      Write('Command Û ');
      GetCharacter(['E','M','D','R','T','G','X'],NoCaseDistinct,Comm);
      END;  { Menu }

   PROCEDURE GroupEngage(NoOfGroups: Word;
                         VAR Gp: GroupArray;
                         VAR En: EnemyArray;
                         CombatData: CombatDataRecord;
                         VAR Details: DetailArray;
                         VAR Killed,Casualties: AttackArray;
                         VAR EndBattle: Boolean;
                         VAR Result: AttackResultTypes);
      VAR
         P: ShellPos;
         GroupsDestroyed: GroupSet;

      PROCEDURE DrawGroupShips(P: ShellPos);
         VAR
            i: Integer;

         BEGIN
         ActivateWindow(AttackWindow);   GotoXY(1,1);
         FOR i:=1 TO NoOfGroups DO
            with Gp[i] do
               begin
               if (Sta<>GDst) and (P=Pos) and (P<>Grnd) then
                  begin
                  Mem[ScrSeg:OrbLoc[P]+PlayerOffset[i]]:=16;
                  end
               else
                  if P<>Grnd then
                     begin
                     Mem[ScrSeg:OrbLoc[P]+PlayerOffset[i]]:=32;
                     end;
               end;  { with scope and loop }
         end;  { DrawGroupShips }

      PROCEDURE AdvanceGroupsSFX;
         VAR
            i: Word;

         BEGIN
         FOR i:=1 TO NoOfGroups DO
            WITH Gp[i] DO
               IF Sta=GAdvc THEN
                  WarpIn(Gp,i,Succ(Pos))
               ELSE IF Sta=GRtrt THEN
                  WarpOut(Gp,i,Pred(Pos));
         END;

      PROCEDURE GroupsDestroyedSFX(DeadGroups: GroupSet; Pos: ShellPos);
         VAR
            temp1: String8;
            Loc,i,j: Word;

         BEGIN
         ActivateWindow(AttackWindow);
         GotoXY(1,1);

         FOR i:=1 TO MaxNoOfGroups DO
            IF i IN DeadGroups THEN
               BEGIN
               { message to screen }
               Str(i:2,temp1);
               AttReport('Group '+temp1+' destroyed.');

               { special effects }
               Loc:=OrbLoc[Pos]+PlayerOffset[i];
               IF Pos<>Grnd THEN
                  FOR j:=1 TO 10 DO
                     BEGIN
                     Mem[ScrSeg:Loc]:=16;
                     Delay((11-j)*10);
                     Mem[ScrSeg:Loc]:=32;
                     Delay((11-j)*10);
                     END;

               AttReport('');
               END;
         END;  { GroupsDestroyedSFX }

      { GroupEngage: MAIN PROCEDURE }
      BEGIN
      FillChar(Details,SizeOf(Details),0);
      FOR P:=DpSPc TO Grnd DO
         BEGIN
         Battle(NoOfGroups,Gp,GroupsDestroyed,En,P,CombatData,Details,Casualties,Killed);
         GroupsDestroyedSFX(GroupsDestroyed,P);
         DrawGroupShips(P);
         END;
      AdvanceGroupsSFX;
      AdvanceGroups(NoOfGroups,Gp);
      EnemyStatus(En);

      IF AllGroupsDestroyed(NoOfGroups,Gp) THEN
         BEGIN
         AttReport('ALL GROUPS DESTROYED');
         EndBattle:=True;
         Result:=AttDestroyedART;
         END
      ELSE IF Result<>AttRetreatsART THEN
         IF EnemySurrenders(NoOfGroups,Gp,En,Casualties,Killed,CombatData) THEN
            BEGIN
            AttReport('THE ENEMY HAS SURRENDERED');
            EndBattle:=True;
            Result:=DefConqueredART;
            END;
      END;  { GroupEngage }

   PROCEDURE GroupMove(DTyp: ObjectTypes;
                       NoOfGroups: Byte;
                       VAR Gp: GroupArray;
                       VAR Ok: Boolean);

      LABEL
         ExitLoop;

      VAR
         i: Integer;
         Inp: Char;
         Advance,Retreat: Boolean;
         Prompt: String16;
         Legal: CharSet;

      PROCEDURE CancelAdvance(NoOfGroups: Byte;
                              VAR Gp: GroupArray);
         VAR
            i: Integer;

         BEGIN
         FOR i:=1 TO NoOfGroups DO
            WITH Gp[i] DO
               IF (Sta=GAdvc) OR (Sta=GRtrt) THEN
                  Sta:=GReady;
         END;  { CancelAdvance }

      { GroupMove: MAIN PROCEDURE }
      BEGIN
      ActivateWindow(GroupWindow);
      ClrScr;
      Ok:=False;

      FOR i:=1 TO NoOfGroups DO
         WITH Gp[i] DO
            BEGIN
            IF Sta<>GDst THEN
               BEGIN
               Prompt:='S';
               Legal:=['S',EscKey];
               IF ((Pos<>Grnd) AND ((DTyp<>Flt) OR (Pos<>Orbit)))
                  AND ((Pos<>SbOrb) OR (Typ IN [fgt,trn,jtn])) THEN
                  BEGIN
                  Advance:=True;
                  Prompt:=Prompt+'/A';
                  Legal:=Legal+['A'];
                  END
               ELSE
                  Advance:=False;

               IF ((Pos=Grnd) AND (Typ<>fgt)) OR (Pos=DpSpc) THEN
                  Retreat:=False
               ELSE
                  BEGIN
                  Retreat:=True;
                  Prompt:=Prompt+'/R';
                  Legal:=Legal+['R'];
                  END;
               Prompt:=Prompt+'/Esc';

               IF Advance OR Retreat THEN
                  BEGIN
                  Writeln(i:2,': ',Num:4,' ',TypN[Typ],' O:',PosN[Pos],' (',ATSymb[Trg],')');
                  Write('  Move ('+Prompt+') Û ');
                  GetCharacter(Legal,NoCaseDistinct,Inp);

                  WriteLn(Inp);
                  IF Inp='A' THEN
                     BEGIN
                     Sta:=GAdvc;
                     Ok:=True;
                     END
                  ELSE IF Inp='R' THEN
                     BEGIN
                     Sta:=GRtrt;
                     Ok:=True;
                     END
                  ELSE IF Inp='S' THEN
                     Sta:=GReady
                  ELSE IF Inp=ESCKey then
                     BEGIN
                     Ok:=False;
                     GOTO ExitLoop;
                     END;
                  END;
               END;
            END;  { with scope and loop }

      { if advance is cancelled in the middle, loop ends here. }
      ExitLoop:

      IF Ok THEN
         BEGIN
         ClrScr;
         Writeln('Designated groups ready');
         Writeln('for maneuver, Your Highness.');
         Writeln;
         Write('Maneuver (y/N) Û ');
         GetCharacter(['Y','N'],NoCaseDistinct,Inp);

         WriteLn(Inp);
         IF Inp<>'Y' THEN
            BEGIN
            CancelAdvance(NoOfGroups,Gp);
            Ok:=False;
            END;
         END
      ELSE
         CancelAdvance(NoOfGroups,Gp);
      END;  { GroupMove }

   PROCEDURE GroupRetreat(NoOfGroups: Byte;
                          VAR Gp: GroupArray;
                          VAR En: EnemyArray;
                          CombatData: CombatDataRecord;
                          VAR Details: DetailArray;
                          VAR Killed,Casualties: AttackArray;
                          VAR EndBattle: Boolean;
                          VAR Result: AttackResultTypes);
      var
         Ans: Char;
         i,j: Integer;
         temp,Start,Finish,Distance: Integer;

      begin
      ActivateWindow(GroupWindow);   ClrScr;
      Write('Are you sure (y/N) Û ');
      GetCharacter(['Y','N'],NoCaseDistinct,Ans);

      if Ans='Y' then
         begin
         AttReport('ALL GROUPS RETREATING');
         Result:=AttRetreatsART;
         GroupEngage(NoOfGroups,Gp,En,CombatData,Details,Killed,Casualties,EndBattle,Result);

         for i:=1 to NoOfGroups do
            with Gp[i] do
               if Sta<>GDst then
                  begin
                  Start:=OrbLoc[Pos]+PlayerOffset[i];
                  Finish:=EdgeOfScreen+PlayerOffset[i];
                  Distance:=(Start-Finish) div 2;
                  IF Pos<>Grnd THEN
                     Mem[ScrSeg:Start]:=32;
                  for j:=0 to Distance do
                     begin
                     temp:=Mem[ScrSeg:Start-2*j];
                     Mem[ScrSeg:Start-2*j]:=16;
                     Delay(15);
                     Mem[ScrSeg:Start-2*j]:=temp;
                     end;
                  end;

         EndBattle:=True;
         end;
      end;  { GroupRetreat }

   PROCEDURE GroupTarget(NoOfGroups: Byte;
                         VAR Gp: GroupArray);
      VAR
         i: Integer;
         NewTrg: Char;
         TrgI: AttackTypes;
         Abort: Boolean;

      BEGIN
      ActivateWindow(GroupWindow);  ClrScr;
      i:=1;
      Abort:=False;
      WHILE (i<=NoOfGroups) AND (NOT Abort) DO
         WITH Gp[i] DO
            BEGIN
            IF Sta<>GDst THEN
               BEGIN
               Writeln(i:2,': ',Num:4,' ',TypN[Typ],' O:',PosN[Pos],' (',ATSymb[Trg],')');
               Write('   New target Û ');
               GetCharacter([EscKey,ReturnKey,'-','L','G','D','I','S','P','H','J','F','T','R','M','N'],
                            NoCaseDistinct,NewTrg);

               IF NewTrg=ReturnKey THEN
                  WriteLn(ATSymb[Trg])
               ELSE IF NewTrg=EscKey THEN
                  Abort:=True
               ELSE
                  BEGIN
                  WriteLn(NewTrg);
                  FOR TrgI:=NoRes TO nnj DO
                     IF NewTrg=ATSymb[TrgI] THEN
                        Trg:=TrgI;
                  END
               END;  { if }

            Inc(i);
            END;  { with scope and loop }
      END;  { GroupTarget }

   procedure GroupStatus(NoOfGroups: Byte;
                         var Gp: GroupArray);
      var
         a: Integer;
         Ch: Char;

      begin
      ActivateWindow(GroupWindow);
      ClrScr;
      for a:=1 to NoOfGroups do
         with Gp[a] do
            begin
            Write(a:2,': ',Num:4,' ',TypN[Typ],' O:',PosN[Pos],' (',ATSymb[Trg],') ');
            if Sta=GDst then
               Writeln('Dst')
            else
               begin
               if Typ in [Trn,JTn] then
                  Writeln(GAT:4)
               else if (Typ=hkr) AND (Flg=False) then
                  Writeln('Clk')
               ELSE
                  Writeln;
               end;
            end;
      GetCharacter(AnyKey,NoCaseDistinct,Ch);
      end;  { GroupStatus }

   procedure AttackDetails(NoOfGroups: Byte;
                           var Gp: GroupArray;
                           var Details: DetailArray);
      var
         DetailWindow: Byte;
         i,y: Byte;
         ShpI: AttackTypes;

      BEGIN
      OpenWindow(1,1,80,13,TopAndBottomBRD,'Engage results: ships destroyed by enemy',
                 C.AttackWind,C.SYSWBorder,DetailWindow);
      GotoXY(23,1);
      FOR i:=1 TO NoOfGroups DO
         Write(i:5);

      y:=2;
      FOR ShpI:=LAM TO nnj DO
         IF Details[ShpI,0]<>0 THEN
            BEGIN
            GotoXY(1,y);
            Write(ThingNames[ShpI]:20,': ');
            FOR i:=1 TO NoOfGroups DO
               Write(Details[ShpI,i]:5);
            y:=y+1;
            END;

      GroupStatus(NoOfGroups,Gp);
      ActivateWindow(DetailWindow);
      CloseWindow;
      ActivateWindow(EnemyWindow);
      ActivateWindow(AttackWindow);
      END;  { AttackDetails }

   { Engage: MAIN PROCEDURE }
   BEGIN
   FillChar(Details,SizeOf(Details),0);
   FillChar(Killed,SizeOf(Killed),0);
   FillChar(Casualties,SizeOf(Casualties),0);
   EndBattle:=False;

   REPEAT
      Menu(Comm);
      CASE Comm OF
         'E' : GroupEngage(NoOfGroups,Gp,En,CombatData,Details,Killed,Casualties,EndBattle,Result);
         'D' : AttackDetails(NoOfGroups,Gp,Details);
         'M' : BEGIN
               GroupMove(CombatData.DTyp,NoOfGroups,Gp,Ok);
               IF Ok THEN
                  GroupEngage(NoOfGroups,Gp,En,CombatData,Details,Killed,Casualties,EndBattle,Result);
               END;
         'R' : GroupRetreat(NoOfGroups,Gp,En,CombatData,Details,Killed,Casualties,EndBattle,Result);
         'T' : GroupTarget(NoOfGroups,Gp);
         'G' : BEGIN
               ActivateWindow(AttackWindow);
               ActivateWindow(EnemyWindow);
               GroupStatus(NoOfGroups,Gp);
               END;
      END;  { case }
   UNTIL EndBattle;
   END;  { Engage }

{ Interface Routines ------------------------------------------------------- }

PROCEDURE GetTarget(FltID: IDNumber;
                    VAR Target: IDNumber;
                    VAR Exit: Boolean);
{ GetTarget:
   Given the fleet that will attack in FltID, this procedure will return the 
   ID of the target in that sector.  If there are more than one target,
   i.e. several fleets, the procedure will ask the player which fleet he 
   wants to attack.  If the player terminates here, then 'Exit' will be 
   true. 

   FltID    same as FltID for PROCEDURE Attack

   Target   must be either Con,Pln,Base,Gate, or Flt
            must be a same XY as FltID
            must NOT belong to 'Player' }

   VAR
      TargetXY: XYCoord;
      FltN: String32;

      Menu: MenuStructure;
      PotentialTarget: IDList;
      ListSize: Byte;

   PROCEDURE CreateMenu(TargetXY: XYCoord;
                        VAR Menu: MenuStructure;
                        VAR PotentialTarget: IDList;
                        VAR ListSize: Byte);
      VAR
         FleetsAtXY: FleetSet;
         Target: IDNumber;
         Status: Empire;
         i: Byte;

      PROCEDURE AddTarget(Target: IDNumber;
                          Emp: Empire;
                          VAR Menu: MenuStructure;
                          VAR PotentialTarget: IDList;
                          VAR ListSize: Byte);

         VAR
            Line: LineStr;

         BEGIN
         ListSize:=ListSize+1;
         PotentialTarget[ListSize]:=Target;

         Line:=ObjectName(Player,Target,LongFormat)+'  ('+EmpireName(Emp)+')';
         AddMenuLine(Menu,Line);
         END;  { AddTarget }

      BEGIN
      InitializeMenu(Menu);
      ListSize:=0;

      Target.ObjTyp:=Flt;
      GetFleets(TargetXY,FleetsAtXY);

      { add enemy fleets to menu }
      FOR i:=1 TO MaxNoOfFleets DO
         IF i IN FleetsAtXY THEN
            BEGIN
            Target.Index:=i;
            Status:=GetStatus(Target);
            IF (Status<>Player) AND Scouted(Player,Target) THEN
               AddTarget(Target,Status,Menu,PotentialTarget,ListSize);
            END;

      { if no fleets, add object. }
      IF ListSize=0 THEN
         BEGIN
         GetObject(TargetXY,Target);
         Status:=GetStatus(Target);
         IF (Status<>Player) THEN
            AddTarget(Target,Status,Menu,PotentialTarget,ListSize);
         END;
      END;  { CreateMenu }

   { GetTarget: MAIN PROCEDURE }
   BEGIN
   ClrDisplayScreen;
   FltN:=ObjectName(Player,FltID,LongFormat);
   WriteString('Attack:',1,1,C.SYSDispHigh);
   WriteString(FltN+' awaiting targetting instructions, '+MyLord(Player)+'.',2,3,C.SYSDispWind);

   GetCoord(FltID,TargetXY);
   CreateMenu(TargetXY,Menu,PotentialTarget,ListSize);
   GetIDMenuChoice(Menu,PotentialTarget,Target,Exit);
   ClrDisplayScreen;
   END;  { GetTarget }

PROCEDURE AttackCommand(FltID: IDNumber);
{ Attack:
   This procedure handles the attack command.  On entry, 'FltID' is the ID
   number of the fleet that is doing the attacking.  The fleet must be a 
   valid fleet and must belong to the proper empire.

   FltID:   must be a fleet
            must be active
            must belong to 'Player'
            target must exist at coordinates (either enemy obj or enemy fleet)
   }

   VAR
      Target: IDNumber;
      TargetName,FleetName: String32;
      CombatData: CombatDataRecord;
      Killed,Casualties: AttackArray;
      Result: AttackResultTypes;
      Exit: Boolean;
      NoOfGroups: Byte;
      Gp: GroupArray;
      En: EnemyArray;
      EnemyEmp: Empire;
      HKSurprise: Boolean;
      Ans: String8;

   { PRIMITIVES }

   { MAIN PROCEDURES }
	PROCEDURE TakeOverConOrGate(ID: IDNumber);
   	VAR
      	NameStr: String32;
      	Emp: Empire;
      	XY: XYCoord;

   	BEGIN
   	ClrDisplayScreen;
   	WriteString('Attack:',1,1,C.SYSDispHigh);

   	GetCoord(ID,XY);
   	NameStr:=ObjectName(Player,ID,LongFormat);

   	DestroyConstructionOrGate(Player,HKSurprise,ID);
   	WriteString(NameStr+' has been destroyed.',2,3,C.SYSDispWind);
   	UMSector(XY.x,XY.y,Map^);
   	END;  { TakeOverConOrGate }

   PROCEDURE GetGroups(FltID: IDNumber; VAR NoOfGp: Byte;
                       VAR Group: GroupArray; VAR Exit: Boolean);
   { GetGroups:
      This procedure will allow the player to split FltID into different groups.
      On return the groups will be in Gp.  If Exit is true, then the player 
      decided to end the session in the middle. }

      VAR
         Sh: ShipArray;
         Cr: CargoArray;
         CG: Word;                        { Current Group }
         Ch: Char;
         i,Trans: Word;
         Gp: GroupArray;
         NoOfGroups: Word;
         CurTyp: AttackTypes;

      PROCEDURE UpdateGroupDisplay(VAR Gp: GroupArray; CG: Word; Col: Word);
         VAR
            Line: LineStr;
            Temp: String8;

         BEGIN
         WITH Gp[CG] DO
            BEGIN
            Str(CG,Temp);
            Line:=Temp+': ';
            Str(Num:4,Temp);
            Line:=Line+Temp+'  '+ThingNames[Typ];
            Line[10]:=UpCase(Line[10]);
            AdjustString(Line,30);

            IF GAT>0 THEN
               BEGIN
               Str(GAT,Temp);
               Line:=Line+'('+Temp+' '+ThingNames[GATTyp]+')';
               END;
            END;

         AdjustString(Line,50);
         WriteString(Line,2,7+CG,Col);
         END;  { UpdateGroupDisplay }

      PROCEDURE UpdateDisplay(Sh: ShipArray; Cr: CargoArray; Typ: ShipTypes);
         VAR
            temp: String16;
            ThgI: ResourceTypes;
            Line: LineStr;

         BEGIN
         Line:='';
         FOR ThgI:=fgt TO trn DO
            BEGIN
            Str(Sh[ThgI]:5,temp);
            Line:=Line+temp;
            END;
         Line:=Line+'          ';

         FOR ThgI:=men TO nnj DO
            BEGIN
            Str(Cr[ThgI]:5,temp);
            Line:=Line+temp;
            END;

         WriteString(Line,1,6,C.SYSDispWind);
         Str(Sh[Typ]:4,temp);
         WriteString(temp,2+(Ord(Typ)-Ord(fgt))*5,6,C.SYSDispSelect);
         END;  { UpdateDisplay }

      PROCEDURE ChangeGroupType(Sh: ShipArray; Cr: CargoArray; Ch: Char; VAR Typ: AttackTypes);
         VAR
            OldTyp: ShipTypes;

         BEGIN
         OldTyp:=Typ;
         IF Ch=RArrKey THEN
            BEGIN
            IF Typ=trn THEN
               Typ:=fgt
            ELSE
               Typ:=Succ(Typ);
            END
         ELSE
            BEGIN
            IF Typ=fgt THEN
               Typ:=trn
            ELSE
               Typ:=Pred(Typ);
            END;

         UpdateDisplay(Sh,Cr,Typ);
         END;  { ChangeGroupType }

      PROCEDURE LoadShips(VAR Sh: ShipArray; VAR Cr: CargoArray;
                          VAR Gp: GroupArray; VAR CG: Word;
                          CurTyp: AttackTypes; Trans: Integer);
         VAR
            PrevTyp: AttackTypes;

         BEGIN
         WITH Gp[CG] DO
            BEGIN
            IF Typ<>CurTyp THEN
               BEGIN
               IF Num>0 THEN
                  BEGIN
                  Inc(Sh[Typ],Num);
                  Num:=0;
                  IF GAT>0 THEN
                     BEGIN
                     Inc(Cr[GATTyp],GAT);
                     GAT:=0;
                     END;
                  END;
               Typ:=CurTyp;
               END;

            IF Trans>0 THEN
               Trans:=LesserInt(Trans,Sh[Typ])
            ELSE
               Trans:=-LesserInt(-Trans,Num);

            Sh[Typ]:=Sh[Typ]-Trans;
            Num:=Num+Trans;

            IF GAT>0 THEN
               BEGIN
               Cr[GATTyp]:=Cr[GATTyp]+GAT;
               GAT:=0;
               END;

            PrevTyp:=Typ;
            END;  { with scope }

         IF CG<NoOfGroups THEN
            BEGIN
            UpdateGroupDisplay(Gp,CG,C.SYSDispWind);
            Inc(CG);
            END;

         UpdateDisplay(Sh,Cr,CurTyp);
         UpdateGroupDisplay(Gp,CG,C.SYSDispSelect);
         END;  { LoadShips }

      PROCEDURE ChangeGroupNumber(VAR Sh: ShipArray; VAR Cr: CargoArray;
                                  VAR Gp: GroupArray; VAR CG: Word;
                                  CurTyp: AttackTypes; Ch: Char);
         VAR
            Error,Trans: Integer;
            Line: String8;

         BEGIN
         IF Ch=ReturnKey THEN
            Line:=''
         ELSE
            Line:=Ch;

         REPEAT
            Error:=0;
            InputStrgDisplayScreen('Add how many to this group: ',2,18,8,Line);
            IF (Line=EscKey) THEN
               Trans:=0
            ELSE IF (Line='') THEN
               Trans:=Sh[Gp[CG].Typ]
            ELSE
               Val(Line,Trans,Error);
            Line:='';
         UNTIL Error=0;
         WriteBlanks(50,2,18,C.SYSDispWind);

         IF Trans<>0 THEN
            LoadShips(Sh,Cr,Gp,CG,CurTyp,Trans);
         END;  { ChangeGroupNumber }

      PROCEDURE LoadTransports(VAR Sh: ShipArray; VAR Cr: CargoArray; 
                               VAR Gp: GroupArray; CG: Word; Ch: Char);
         VAR
            TypeToLoad: AttackTypes;
            NoToLoad: Word;

         BEGIN
         IF Ch='N' THEN
            TypeToLoad:=nnj
         ELSE
            TypeToLoad:=men;

         WITH Gp[CG] DO
            BEGIN
            IF GAT>0 THEN
               Cr[GATTyp]:=Cr[GATTyp]+GAT;

            GAT:=LesserInt(Cr[TypeToLoad],ThgLmt(Round(TrnAdj[Typ]*Num*CargoSpace[TypeToLoad])));
            GATTyp:=TypeToLoad;
            Cr[GATTyp]:=Cr[GATTyp]-GAT;
            END;

         UpdateGroupDisplay(Gp,CG,C.SYSDispSelect);
         UpdateDisplay(Sh,Cr,Gp[CG].Typ);
         END;  { LoadTransports }

      { GetGroups: MAIN PROCEDURE }
      BEGIN
      ClrDisplayScreen;
      WriteString('Attack:',1,1,C.SYSDispHigh);
      WriteString('Fleet configuration:',2,3,C.SYSDispWind);
      WriteString('  fgt   hk  jmp  jtn  pen  str  trn            men ninj',1,5,C.SYSDispWind);
      WriteHelpLine(':Select Group '#27#26':Select Type <M>:Load Men <N>:Load Ninja Esc:Exit');

      FillChar(Gp,SizeOf(Gp),0);
      FillChar(Group,SizeOf(Group),0);

      GetShips(FltID,Sh);
      GetCargo(FltID,Cr);
      CG:=1;
      CurTyp:=fgt;
      NoOfGroups:=MaxNoOfGroups;
      FOR i:=1 TO NoOfGroups DO
         BEGIN
         Gp[i].Typ:=CurTyp;
         UpdateGroupDisplay(Gp,i,C.SYSDispWind);
         END;

      UpdateDisplay(Sh,Cr,CurTyp);
      UpdateGroupDisplay(Gp,1,C.SYSDispSelect);

      REPEAT
         GetCharacter(AnyKey,NoCaseDistinct,Ch);
         CASE Ch OF
            UArrKey: IF CG>1 THEN
               BEGIN
               UpdateGroupDisplay(Gp,CG,C.SYSDispWind);
               Dec(CG);
               UpdateGroupDisplay(Gp,CG,C.SYSDispSelect);
               UpdateDisplay(Sh,Cr,Gp[CG].Typ);
               CurTyp:=Gp[CG].Typ;
               END;
            DArrKey: IF CG<NoOfGroups THEN
               BEGIN
               UpdateGroupDisplay(Gp,CG,C.SYSDispWind);
               Inc(CG);
               UpdateGroupDisplay(Gp,CG,C.SYSDispSelect);
               UpdateDisplay(Sh,Cr,Gp[CG].Typ);
               CurTyp:=Gp[CG].Typ;
               END;

            '+','-',
            ReturnKey,
            '0'..'9': ChangeGroupNumber(Sh,Cr,Gp,CG,CurTyp,Ch);

            ' ': LoadShips(Sh,Cr,Gp,CG,CurTyp,Sh[CurTyp]);

            LArrKey,
            RArrKey: ChangeGroupType(Sh,Cr,Ch,CurTyp);

            'N','M': LoadTransports(Sh,Cr,Gp,CG,Ch);
         END;  { case }
      UNTIL (Ch=ESCKey);

      Exit:=False;

      NoOfGp:=0;
      FOR i:=1 TO MaxNoOfGroups DO
         IF (Gp[i].Num>0) AND (NOT (Gp[i].Typ IN [trn,jtn])) THEN
            BEGIN
            Inc(NoOfGp);
            Group[NoOfGp]:=Gp[i];
            END;

      FOR i:=1 TO MaxNoOfGroups DO
         IF (Gp[i].Num>0) AND (Gp[i].Typ IN [trn,jtn]) THEN
            BEGIN
            Inc(NoOfGp);
            Group[NoOfGp]:=Gp[i];
            END;

      FOR i:=1 TO NoOfGp DO
         WITH Group[i] DO
            BEGIN
            IF (Cr[men]>0) AND (Typ IN [trn,jtn]) AND (GAT=0) THEN
               BEGIN
               Trans:=LesserInt(Cr[men],Round(CargoSpace[men]*TrnAdj[typ]*Num));
               GAT:=Trans;
               GATTyp:=men;
               Dec(Cr[men],Trans);
               END
            ELSE IF (Cr[nnj]>0) AND (Typ IN [trn,jtn]) AND (GAT=0) THEN
               BEGIN
               Trans:=LesserInt(Cr[nnj],Round(CargoSpace[nnj]*TrnAdj[typ]*Num));
               GAT:=Trans;
               GATTyp:=nnj;
               Dec(Cr[nnj],Trans);
               END;
            END;
      END;  { GetGroups }

   PROCEDURE DrawScreen(Target: IDNumber);

      VAR
         Obj: ObjectTypes;
         i: Integer;

      PROCEDURE InitializeEnemyWindow;
         VAR
            AttI: AttackTypes;


         BEGIN
         ActivateWindow(EnemyWindow);
         Write('                       Deep/High/Orbt/SubO/Grnd');
         FOR AttI:=fgt TO trn DO
            Writeln('<',ATSymb[AttI],'> ',ThingNames[AttI]);
         Writeln;
         Writeln('<G> GDM 0000  <D> def 0000  <I> ion 0000');
         Write(  '<M> men 0000  <N> nnj 0000  <L> LAM 0000');
         END;  { InitializeEnemyWindow }

      PROCEDURE DrawObject(Target: IDNumber);

         BEGIN
         IF Target.ObjTyp=Pln THEN
            BEGIN
            WriteString(    'ÜÜÜ',70,4,C.AttackWind);
            WriteString( 'ÜÛÛÛÛÛÛÛÜ',67,5,C.AttackWind);
            WriteString('ÞÛÛÛÛÛÛÛÛÛÝ',66,6,C.AttackWind);
            WriteString( 'ßÛÛÛÛÛÛÛß',67,7,C.AttackWind);
            WriteString(    'ßßß',70,8,C.AttackWind);
            END
         ELSE IF Target.ObjTyp=Base THEN
            BEGIN
            CASE GetBaseType(Target) OF
               out: BEGIN
                  WriteString(    'þÂß',71,5,C.AttackWind);
                  WriteString( 'þÛþ²Û²þÛþ',67,6,C.AttackWind);
                  WriteString(    ' Ýß',70,7,C.AttackWind);
                  WriteString(     '³',71,8,C.AttackWind);
                  END;
               cmp: BEGIN
                  WriteString(    'ÜÜÜ',70,4,C.AttackWind);
                  WriteString( 'ÜÛ²ß ß±²Ü',67,5,C.AttackWind);
                  WriteString('ÞÛ²Ý   Þ²ÛÝ',66,6,C.AttackWind);
                  WriteString( 'ß±²Ü Ü²Ûß',67,7,C.AttackWind);
                  WriteString(    'ßßß',70,8,C.AttackWind);
                  END;
               frt,cmm: BEGIN
                  WriteString('    ÞÜ     ',66,4,C.AttackWind);
                  WriteString('  ÞÜÛÛÜþÝ ³',66,5,C.AttackWind);
                  WriteString('ÞÜÛ²ÛßÛÛÛÜÝ',66,6,C.AttackWind);
                  WriteString(' ß²±ÅþÅ±Ûß ',66,7,C.AttackWind);
                  WriteString('    ÝßÞ    ',66,8,C.AttackWind);
                  END;
            END;  { case }
            END;
         END;  { DrawObject }

      PROCEDURE DrawGrid;
         VAR
            PosI: ShellPos;
            j: Integer;

         CONST
            Disp: array[HiOrb..Orbit] of array[1..11] of Byte =
               (  ( 4,2,2,0,0,0,0,0,2,2,4 ),
                  ( 10,4,2,2,0,0,0,2,2,4,10 )  );

            Disp2: array[1..11] of Byte =
               ( 0,0,10,14,16,16,16,14,10,0,0 );

         BEGIN
         FOR j:=-5 TO 5 DO
            Mem[ScrSeg:OrbLoc[DpSpc]+j*160]:=250;

         FOR PosI:=HiOrb TO Orbit DO
            BEGIN
            FOR j:=-5 TO 5 DO
               Mem[ScrSeg:OrbLoc[PosI]+j*160+Disp[PosI][j+6]]:=250;
            END;

         FOR j:=-4 TO 4 DO
            BEGIN
            Mem[ScrSeg:OrbLoc[Grnd]+j*160+Disp2[j+6]]:=250;
            Mem[ScrSeg:OrbLoc[Grnd]+j*160-Disp2[j+6]]:=250;
            END;

         WriteString('Range to attack defenses:                     Ã Def ´  Ã Ion/GDM ´',1,1,C.AttackWind);
         WriteString('Range of defenses:         ÃÄÄ Defense Sat.  ÄÁ GDM ´    Ã Ion ´',1,11,C.AttackWind);
         END;  { DrawGrid }

      PROCEDURE DrawStars;
         VAR
            i: Byte;

         BEGIN
         FOR i:=1 TO 100 DO
            Mem[ScrSeg:TopCornerLoc+Rnd(1,720)*2]:=250;
         END;  { DrawStars }

      { DrawScreen: MAIN PROCEDURE }
      BEGIN
      OpenWindow(1,1,80,13,TopAndBottomBRD,'',C.AttackWind,C.SYSWBorder,AttackWindow);
      OpenWindow(1,14,33,12,ThinBRD,'',C.GroupWind,C.SYSWBorder,GroupWindow);
      OpenWindow(34,14,47,12,NoBRD,'',C.EnemyWind,C.SYSWBorder,EnemyWindow);

      InitializeEnemyWindow;

      Obj:=Target.ObjTyp;

      ActivateWindow(AttackWindow);
      IF Obj IN [Con..Gate] THEN
         DrawGrid
      ELSE
         DrawStars;

      DrawObject(Target);

      EnemyStatus(En);
      CASE Rnd(1,3) OF
         1 : AttReport('Fleet entering real space...');
         2 : AttReport('Fleet now coming out of hyperspace...');
         3 : AttReport('Fleet in combat status...');
      END;  { case }

      FOR i:=1 TO NoOfGroups DO
         WarpIn(Gp,i,DpSpc);

      AttReport('');
      END;  { DrawScreen }

   PROCEDURE EraseScreen;
      BEGIN
      ActivateWindow(GroupWindow);
      ActivateWindow(AttackWindow);
      ActivateWindow(EnemyWindow);
      CloseWindow;
      CloseWindow;
      CloseWindow;

      ClrDisplayScreen;
      END;  { EraseScreen }

   PROCEDURE CleanUp(Result: AttackResultTypes;
                     FltID,Target: IDNumber;
                     VAR Casualties,Killed: AttackArray);
      VAR
         Capture: Boolean;
         XY: XYCoord;
         Booty: PlanetSet;

      PROCEDURE BattleLost(FltID,Target: IDNumber);
      { BattleLost:
         The player is told that the battle was lost.  The news is updated.
         Revolution is added. }

         var
            Emp: Empire;
            XY: XYCoord;

         procedure Message1;
            var
               MaxRev,i: Byte;
               MaxRevID: IDNumber;
               ObjID: IDNumber;
               World: String32;

            begin
            WriteString('I''m sorry, '+MyLord(Player)+', the entire attack force has been destroyed.',1,3,C.SYSDispWind);
            WriteString('I hope I do not have to remind you about the repercussion that this',1,4,C.SYSDispWind);
            WriteString('loss will have.  Cetain factions within the Empire are already counting',1,5,C.SYSDispWind);
            WriteString('on fear to incite rebellion.',1,6,C.SYSDispWind);

            ObjID.ObjTyp:=Pln;
            MaxRev:=0;  MaxRevID:=EmptyQuadrant;
            FOR i:=1 TO NoOfPlanets DO
               IF i IN SetOfPlanetsOf[Player] THEN
                  BEGIN
                  ObjID.Index:=i;
                  IF GetRevIndex(ObjID)>MaxRev THEN
                     BEGIN
                     MaxRevID:=ObjID;
                     MaxRev:=GetRevIndex(MaxRevID);
                     END;
                  END;
            ObjID:=MaxRevID;
            World:=ObjectName(Player,ObjID,ShortFormat);

            IF (MaxRev>20) AND (World<>'') THEN
               BEGIN
               WriteString('Do not forget that '+World+' is quickly growing doubtful of the Empire''s',1,7,C.SYSDispWind);
               WriteString('ability to defend itself.  ',1,8,C.SYSDispWind);
               END;
            END;  { Message1 }

         procedure Message2;
            begin
            WriteString(MyLord(Player)+', I''m sorry to report that the entire attack force was lost',1,3,C.SYSDispWind);
            WriteString('in the battle.  At the risk of offending Your Highness, I would like to',1,4,C.SYSDispWind);
            WriteString('point out that an option to retreat was open at all times.  Although',1,5,C.SYSDispWind);
            WriteString('sacrifice is something that all your troops know, it is often best to',1,6,C.SYSDispWind);
            WriteString('allow them the luxury of living to fight another day.',1,7,C.SYSDispWind);
            end;  { Message2 }

         PROCEDURE Message3;
            VAR
               E: Empire;

            BEGIN
            REPEAT
               E:=Empire(Rnd(Ord(Empire1),Ord(Empire8)));
            UNTIL (E<>Player) AND EmpireActive(E);
            WriteString(MyLord(Player)+', the entire attack force was destroyed in battle.',1,3,C.SYSDispWind);
            WriteString('Although I certainly do not question the orders and decision of Your',1,4,C.SYSDispWind);
            WriteString('Highness, I should like to mention that this defeat will not go',1,5,C.SYSDispWind);
            WriteString('unnoticed in the Galaxy.  Already '+EmpireName(E)+' is starting to',1,6,C.SYSDispWind);
            WriteString('believe that this Empire would not be an overly costly target.',1,7,C.SYSDispWind);
            END;  { Message3 }

         procedure Message4;
            begin
            WriteString(MyLord(Player)+', the attack force has been totally destroyed by the enemy.',1,3,C.SYSDispWind);
            case Rnd(1,3) of
               1 : WriteString('You must be careful, Your Highness, or greater battles will be lost.',1,4,C.SYSDispWind);
               2 : WriteString('Do not think that this defeat will go unnoticed in the Galaxy.',1,4,C.SYSDispWind);
               3 : WriteString('You must be careful, other star systems grow suspicious of your defenses.',1,4,C.SYSDispWind);
            end;  { case }
            end;  { Message4 }

         { MAIN PROCEDURE: BattleLost }
         BEGIN
         Emp:=GetStatus(Target);
         IF Emp=Indep THEN
            BEGIN
            CASE Rnd(1,12) OF
               1 : Message1;
               2 : Message2;
               3..12 : Message4;
            END;  { case }
            END
         ELSE
            BEGIN
            CASE Rnd(1,4) OF
               1 : Message1;
               2 : Message2;
               3 : Message3;
               4 : Message4;
            END;  { case }
            END;
         END;  { BattleLost }

      PROCEDURE Retreated(FltID,Target: IDNumber);
      { Retreated:
         The player is told that the fleet retreated.  The news is updated.
         Revolution is added, though not as much as if the ships had been
         destroyed. }

         VAR
            Emp: Empire;

         { MAIN PROCEDURE: Retreated }
         BEGIN
         WriteString('The attacking force has retreated, '+MyLord(Player)+'.',1,3,C.SYSDispWind);
         END;  { Retreated }

      PROCEDURE AskToCapture(VAR Sh: ShipArray; VAR Capture: Boolean);
         VAR
            ShpI: ShipTypes;
            Lines: Word;
            Ans,NumStr: String8;

         BEGIN
         ClrDisplayScreen;
         Lines:=0;
         WriteString('Attack:',1,1,C.SYSDispHigh);

         WriteString('You have captured:',2,3,C.SYSDispWind);
         FOR ShpI:=fgt TO trn DO
            IF Sh[ShpI]>0 THEN
               BEGIN
               Str(Sh[ShpI],NumStr);
               WriteString(NumStr+' '+ThingNames[ShpI],4,Lines+4,C.SYSDispWind);
               Inc(Lines);
               END;

         CASE Rnd(1,3) OF
            1: BEGIN
               WriteString('The commander of the enemy fleet begs Your Majesty to spare his life.',2,Lines+5,C.SYSDispWind);
               Inc(Lines);
               END;
            2: BEGIN
               WriteString('Message from enemy commander: "...'+MyLord(Player)+', please allow me the honor',
                           2,Lines+5,C.SYSDispWind);
               WriteString('of death in battle.  Destroy this fleet..."',2,Lines+6,C.SYSDispWind);
               Inc(Lines,2);
               END;
            3: BEGIN
               WriteString('Death in space is the only honorable way for a vanquished enemy.',2,Lines+5,C.SYSDispWind);
               Inc(Lines);
               END;
         END;  { case }

         Ans:='';
         InputStrgDisplayScreen('Do you wish to destroy the enemy fleet (y/N) ? ',2,Lines+5,1,Ans);
         IF UpCase(Ans[1])='Y' THEN
            Capture:=False
         ELSE
            Capture:=True;

         ClrDisplayScreen;
         END;  { AskToCapture }

      PROCEDURE EnemyConquered(FltID,Target: IDNumber; VAR Capture: Boolean);
      { EnemyConquered:
         The enemy is conquered.  If it is a planet or base, it is declared to
         be part of the empire.  If it is a fleet, some of the ships are
         impressed into the conquering fleet.  The news is updated.  If the
         target was not independent then revolution index is added to the enemy.
         The planet is conquered and its stats are adjusted. }

         var
            Sh,Sh2: ShipArray;
            Cr: CargoArray;
            YearN: String16;
            DummyEmp,OldEmp: Empire;
            RevChange: Integer;
            XY: XYCoord;
            EmpN: String32;
            Message: Boolean;
            

         procedure Message1;
            begin
            IF Empress(Player) THEN
               WriteString('In the name of Her Imperial Majesty, Lady of '+EmpN+', I hereby declare',1,3,C.SYSDispWind)
            ELSE
               WriteString('In the name of His Imperial Majesty, Lord of '+EmpN+', I hereby declare',1,3,C.SYSDispWind);

            WriteString('this '+ObjName[Target.ObjTyp]+' to be under the sovereign jurisdiction of the',1,4,C.SYSDispWind); 
            WriteString(EmpN+' Empire.',1,5,C.SYSDispWind);
            end;  { Message1 }

         PROCEDURE Message2;
            VAR
               FltN,TargetN: String32;

            BEGIN
            TargetN:=ObjectName(Player,Target,LongFormat);
            FltN:=ObjectName(Player,FltID,LongFormat);

            WriteString('Congratulations '+MyLord(Player)+', '+FltN+' has succeeded in its attack against',1,3,C.SYSDispWind);
            WriteString(TargetN+'.  No doubt some of your enemies will in the future ',1,4,C.SYSDispWind);
            WriteString('be more careful when challenging this empire.',1,5,C.SYSDispWind);
            END;  { Message2 }

         procedure Message3;
            begin
            WriteString('Congratulations on your victory, '+MyLord(Player)+', but remember that not',1,3,C.SYSDispWind);
            WriteString('all battles will be this easy.',1,4,C.SYSDispWind);
            end;  { Message3 }

         { EnemyConquered: MAIN PROCEDURE }
         BEGIN
         OldEmp:=GetStatus(Target);
         FillChar(Cr,SizeOf(Cr),0);
         Str(Year:4,YearN);
         EmpN:=EmpireName(Player);
	 DummyEmp:=Indep;

         IF (Target.ObjTyp=Flt) THEN
            BEGIN
            GetShips(Target,Sh2);
            IF NOT NoShips(Sh2) THEN
               BEGIN
               AskToCapture(Sh2,Capture);
               ClrDisplayScreen;
               WriteString('Attack:',1,1,C.SYSDispHigh);
               END;
            END;

         { ending message }
         DisplayBackground(DummyEmp,Target,1,3,C.SYSDispWind,True,Message);
         IF NOT Message THEN
            BEGIN
            IF GetType(Target)<>CapTyp THEN
               BEGIN
               CASE Rnd(1,3) OF
                  1 : Message1;
                  2 : Message2;
                  3 : Message3;
               END;  { case }
               END
            ELSE
               BEGIN
               Message1;
               END;
            END;
         END;  { EnemyConquered }

      PROCEDURE UpdateMap;
         VAR
            XY: XYCoord;
            ObjID: IDNumber;

         BEGIN
         ObjID.ObjTyp:=Pln;
         FOR ObjID.Index:=1 TO NoOfPlanets DO
            IF Known(Player,ObjID) THEN
               BEGIN
               GetCoord(ObjID,XY);
               UMSector(XY.x,XY.y,Map^);
               END;
         END;  { UpdateMap }

      PROCEDURE OldShipsFound(Target: IDNumber);
         VAR
            PosY: Word;
            ShStr: String8;
            ShipsFound,Ships: ShipArray;
            ShI: ShipTypes;
            Tech: TechLevel;
            OldShips: Boolean;
            Ch: Char;

         BEGIN
         IF (Target.ObjTyp=Pln) AND (GetStatus(Target)=Indep) THEN
            BEGIN
            OldShips:=False;
            FillChar(ShipsFound,SizeOf(ShipsFound),0);
            GetShips(Target,Ships);
            Tech:=GetTech(Target);
            FOR ShI:=fgt TO trn DO
               IF (NOT (ShI IN TechDev[Tech])) AND (Ships[ShI]>0) THEN
                  BEGIN
                  ShipsFound[ShI]:=Ships[ShI];
                  OldShips:=True;
                  END;

            IF OldShips THEN
               BEGIN
               WriteString(MyLord(Player)+', we have found the following ships in orbit: ',1,3,C.SYSDispWind);
               PosY:=5;
               FOR ShI:=fgt TO trn DO
                  IF ShipsFound[ShI]>0 THEN
                     BEGIN
                     Str(ShipsFound[ShI],ShStr);
                     WriteString(ShStr+' '+ThingNames[ShI],5,PosY,C.SYSDispWind);
                     Inc(PosY);
                     END;
               WriteString('Press any key to continue...',2,19,C.SYSDispWind);
               GetCharacter(AnyKey,NoCaseDistinct,Ch);
               ClrDisplayScreen;
               WriteString('Attack:',1,1,C.SYSDispHigh);
               END;
            END;
         END;  { OldShipsFound }

      PROCEDURE EmpireConquestReport(VAR Booty: PlanetSet);
         VAR
            Ch: Char;
            PosX,PosY,i: Word;
            WorldName: String32;
            XY: XYCoord;
            ObjID: IDNumber;

         BEGIN
         WriteString('Press any key to continue...',2,19,C.SYSDispWind);
         GetCharacter(AnyKey,NoCaseDistinct,Ch);
         ClrDisplayScreen;
         WriteString('Attack:',1,1,C.SYSDispHigh);

         WriteString(MyLord(Player)+', the following worlds have joined our empire:',2,3,C.SYSDispWind);
         PosX:=5;
         PosY:=5;
         ObjID.ObjTyp:=Pln;
         FOR i:=1 TO NoOfPlanets DO
            IF i IN Booty THEN
               BEGIN
               ObjID.Index:=i;
               WorldName:=ObjectName(Player,ObjID,LongFormat);
               WriteString(WorldName,PosX,PosY,C.SYSDispWind);
               Inc(PosY);
               IF PosY>19 THEN
                  BEGIN
                  PosY:=5;
                  Inc(PosX,32);
                  END;

               GetCoord(ObjID,XY);
               UMSector(XY.x,XY.y,Map^);
               END;
         END;  { EmpireConquestReport }

      { MAIN PROCEDURE: CleanUp }
      BEGIN
      ClrDisplayScreen;
      WriteString('Attack:',1,1,C.SYSDispHigh);

      GetCoord(FltID,XY);
      RestoreCombatant(FltID,Casualties);
      RestoreCombatant(Target,Killed);
      CASE Result OF
         AttDestroyedART: BattleLost(FltID,Target);
         AttRetreatsART: Retreated(FltID,Target);
         DefConqueredART: BEGIN
            OldShipsFound(Target);
            EnemyConquered(FltID,Target,Capture);
            IF GetType(Target)=CapTyp THEN
               UpdateMap;
            END;
      END;  { case }
      ResolveAttack(Result,FltID,Target,HKSurprise,Capture,Casualties,Killed,Booty);
      IF Booty<>[] THEN
         EmpireConquestReport(Booty);

      UMSector(XY.x,XY.y,Map^);
      UMFleets(XY.x,XY.y,Map^);
      END;  { CleanUp }

   { Attack: MAIN PROCEDURE }
   BEGIN
   Exit:=False;
   GetTarget(FltID,Target,Exit);

   IF NOT Exit THEN
      BEGIN
      HKSurprise:=ForcesUnknown(FltID,Target);

      IF Target.ObjTyp IN [Pln,Base,Flt] THEN
         BEGIN
         WriteString('Attack:',1,1,C.SYSDispHigh);

         FleetName:=ObjectName(Player,FltID,LongFormat);
         TargetName:=ObjectName(Player,Target,LongFormat);
         WriteString(FleetName+' ready to attack '+TargetName+'.',2,3,C.SYSDispWind);

         Ans:='';
         InputStrgDisplayScreen('Standard battle configuration (Y/n) ? ',2,5,1,Ans);

         ClrDisplayScreen;
         IF Ans<>EscKey THEN
            BEGIN
            IF UpCase(Ans[1])='N' THEN
               GetGroups(FltID,NoOfGroups,Gp,Exit)
            ELSE
               DefaultDistribution(FltID,NoOfGroups,Gp);

            ClrDisplayScreen;
            IF NoOfGroups>0 THEN
               BEGIN
               CalculateCombatData(Player,FltID,Target,CombatData);
               GetEnemy(Target,En);

               DrawScreen(Target);
               Engage(NoOfGroups,Gp,En,CombatData,Casualties,Killed,Result);
               EraseScreen;
               WriteHelpLine('F1:Help F3:Status F5:Fleets F7:News F8:Empire F9:Names F10:Map');

               CleanUp(Result,FltID,Target,Casualties,Killed);
               END;
            END;
         END
      ELSE IF Target.ObjTyp IN [Con,Gate] THEN
         TakeOverConOrGate(Target);
      END;
   WriteCommLine('');
   WriteErrorMessage('');
   END;  { Attack }

PROCEDURE AutoAttackCommand(FltID: IDNumber);
   VAR
      TargetID: IDNumber;
      TargetName,FleetName: String32;
      CombatData: CombatDataRecord;
		XY: XYCoord;
      Result: AttackResultTypes;
      Exit: Boolean;
      EnemyEmp: Empire;
      Ans: String8;
		OldShips, NewShips: ShipArray;

	PROCEDURE ResultMessage (TargetID: IDNumber; Result: AttackResultTypes);
		VAR
			EmpN: LineStr;

		BEGIN
		CASE Result OF
			AttDestroyedART: BEGIN
            WriteString('I''m sorry, '+MyLord(Player)+', the entire attack force has been destroyed.',2,3,C.SYSDispWind);
				CASE Rnd(1,2) OF
					1: WriteString('Perhaps if you had been there to direct the attack...',2,4,C.SYSDispWind);
					2: WriteString('The fleet commander fought bravely and did not surrender.',2,4,C.SYSDispWind);
				END;  { case }
				END;
			AttRetreatsART: BEGIN
            WriteString('I''m sorry, '+MyLord(Player)+', the fleet was forced to retreat.',2,3,C.SYSDispWind);
				END;
			DefConqueredART: BEGIN
				IF TargetID.ObjTyp=Flt THEN
					BEGIN
	            WriteString('The enemy fleet has been destroyed, '+MyLord(Player)+'.',2,3,C.SYSDispWind);
					END
				ELSE
					BEGIN
					EmpN:=EmpireName(Player);
      			IF Empress(Player) THEN
         			WriteString('In the name of Her Imperial Majesty, Lady of '+EmpN+', I hereby declare',2,3,C.SYSDispWind)
      			ELSE
         			WriteString('In the name of His Imperial Majesty, Lord of '+EmpN+', I hereby declare',2,3,C.SYSDispWind);

      			WriteString('this '+ObjName[TargetID.ObjTyp]+' to be under the sovereign jurisdiction of the',2,4,C.SYSDispWind); 
      			WriteString(EmpN+' Empire.',2,5,C.SYSDispWind);
					END;
				END;
		END;  { case }
		END;  { ResultMessage }

	PROCEDURE CasualtyReport (OldShips, NewShips: ShipArray);
		VAR
			ShpI: ResourceTypes;
			Temp: LineStr;

		BEGIN
		WriteString ('Casualties:',2,7,C.SYSDispWind);

		FOR ShpI:=fgt TO trn DO
			BEGIN
			WriteString (ThingNames[ShpI],5,9+Ord(ShpI)-Ord(fgt),C.SYSDispWind);
			Str (GreaterInt (0, OldShips[ShpI] - NewShips[ShpI]), Temp);
			WriteString (Temp, 28, 9+Ord(ShpI)-Ord(fgt),C.SYSDispWind);
			END;
		END;  { CasualtyReport }

	{ AutoAttackCommand }
	BEGIN
   Exit:=False;
   GetTarget(FltID,TargetID,Exit);

   IF NOT Exit THEN
      BEGIN
      WriteString('Auto Attack:',1,1,C.SYSDispHigh);

      FleetName:=ObjectName(Player,FltID,LongFormat);
      TargetName:=ObjectName(Player,TargetID,LongFormat);
      WriteString(FleetName+' ready to attack '+TargetName+'.',2,3,C.SYSDispWind);

      Ans:='';
      InputStrgDisplayScreen('Give confirmation order (y/N) ? ',2,5,1,Ans);

      ClrDisplayScreen;
		WriteString('Auto Attack:',1,1,C.SYSDispHigh);

      IF (UpCase(Ans[1])='Y') THEN
			BEGIN
			IF TargetID.ObjTyp IN [Pln,Base,Flt] THEN
            BEGIN
				GetShips (FltID, OldShips) ;
				NPEAttack (FltID, TargetID, ConquerAIT, 0, Result) ;
				GetShips (FltID, NewShips) ;

				ResultMessage (TargetID, Result) ;
				CasualtyReport (OldShips, NewShips) ;
            END
	      ELSE IF TargetID.ObjTyp IN [Con,Gate] THEN
				BEGIN
   			WriteString(TargetName+' has been destroyed.',2,3,C.SYSDispWind);
				END;

			GetCoord (FltID, XY) ;
			UMSector (XY.x, XY.y, Map^) ;
	      UMFleets (XY.x, XY.y, Map^) ;
         END;
      END;
   WriteCommLine('');
   WriteErrorMessage('');
	END;  { AutoAttackCommand }

END.
