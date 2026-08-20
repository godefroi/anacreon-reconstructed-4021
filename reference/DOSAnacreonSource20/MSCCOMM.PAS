(* MSCCOMM.PAS -----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT MscComm;

{$IFDEF Overlay}
{$O+,F+}
{$ENDIF}

INTERFACE

USES CRT,
     Strg,
     EIO,
     Menu,
     Types;

PROCEDURE SelfDestructCommand;
PROCEDURE DefenseCommand;
(*
PROCEDURE HolocaustCommand;
*)
(*ARTIFACT
PROCEDURE ArtifactCommand;
PROCEDURE TransactionCommand(WorldID: IDNumber);
*)

IMPLEMENTATION

USES
   Environ,
   Galaxy,
   DataStrc,
   DataCnst,
   PrimIntr,
   Intrface,
   SBase,
   SWindows,
   MapWind,
   Display,
(*ARTIFACT
   Artifact,
   Transact,
*)
   Attack;

(*ARTIFACT

PROCEDURE GetFleetMenu(WorldID: IDNumber; VAR FltID: IDNumber;
                       VAR Exit: Boolean);

   VAR
      Menu: MenuStructure;
      PotentialGround: IDList;
      ListSize: Byte;
      XY: XYCoord;

   procedure CreateMenu(Emp: Empire; XY: XYCoord;
                        VAR Menu: MenuStructure;
                        VAR PotentialGround: IDList;
                        VAR ListSize: Byte);
      var
         Fleets: FleetSet;
         FltID: IDNumber;
         i: Word;

      PROCEDURE AddFleet(FltID: IDNumber;
                         Emp: Empire;
                         VAR Menu: MenuStructure;
                         VAR PotentialGround: IDList;
                         VAR ListSize: Byte);

         VAR
            Line: LineStr;

         BEGIN
         Inc(ListSize);
         PotentialGround[ListSize]:=FltID;

         Line:=ObjectName(Player,FltID,LongFormat);
         Line:=Line+'  ('+EmpireName(Emp)+')';
         AddMenuLine(Menu,Line);
         END;  { AddFleet }

      BEGIN
      InitializeMenu(Menu);
      ListSize:=0;

      FltID.ObjTyp:=Flt;
      GetFleets(XY,Fleets);
      Fleets:=Fleets*SetOfFleetsOf[Emp];

      { add fleets to menu }
      FOR i:=1 TO MaxNoOfFleets DO
         IF i IN Fleets THEN
            BEGIN
            FltID.Index:=i;
            AddFleet(FltID,Emp,Menu,PotentialGround,ListSize);
            END;
      end;  { CreateMenu }

   { GetFleetMenu: MAIN PROCEDURE }
   BEGIN
   GetCoord(WorldID,XY);
   CreateMenu(Player,XY,Menu,PotentialGround,ListSize);
   IF ListSize>0 THEN
      BEGIN
      ClrDisplayScreen;
      WriteString('Transaction:',1,1,C.SYSDispHigh);
      WriteString('Which fleet do you want to use to transact?',2,3,C.SYSDispWind);
      GetIDMenuChoice(Menu,PotentialGround,FltID,Exit);
      ClrDisplayScreen;
      END
   ELSE
      FltID:=EmptyQuadrant;
   END;  { GetFleetMenu }

PROCEDURE TransactionCommand(WorldID: IDNumber);
   VAR
      FltID: IDNumber;
      Abort: Boolean;

   BEGIN
   GetFleetMenu(WorldID,FltID,Abort);
   IF FltID.ObjTyp=Void THEN
      BEGIN
      WriteCommLine(MyLord(Player)+', we do not have a fleet in that sector.');
      END
   ELSE IF Abort THEN
      BEGIN
      WriteCommLine('Transaction aborted.');
      END
   ELSE
      BEGIN
      ClrDisplayScreen;
      Transaction(WorldID,FltID,Player);
      WriteCommLine('Transaction with '+ObjectName(Player,WorldID,LongFormat)+' completed.');
      END;
   ActivateLatestStatusWindow;
   END;  { TransactionCommand }

*)

(*ARIFACT
PROCEDURE ArtifactCommand;
   TYPE
      ArtifactIDList = ARRAY [1..MaxNoOfArtifacts] OF IDNumber;

   VAR
      Menu: MenuStructure;
      NoOfArtifacts: Word;
      ArtID: IDNumber;
      ArtifactList: ArtifactIDList;
      Ch: Char;
      Done: Boolean;

   PROCEDURE ConstructArtifactList(VAR Menu: MenuStructure;
                                   VAR NoOfArtifacts: Word;
                                   VAR ArtifactList: ArtifactIDList);
      VAR
         ArtID: IDNumber;
         Line: LineStr;

      BEGIN
      InitializeMenu(Menu);
      NoOfArtifacts:=0;

      ArtID:=FirstArtifact;
      NextArtifact(ArtID);
      WHILE ArtID.ObjTyp<>Void DO
         BEGIN
         IF ArtifactKnown(Player,ArtID) THEN
            BEGIN
            Inc(NoOfArtifacts);
            Line:=ArtifactName(ArtID);
            AddMenuLine(Menu,Line);
            ArtifactList[NoOfArtifacts]:=ArtID;
            END;
         NextArtifact(ArtID);
         END;
      END;  { ConstructArtifactList }

   { ArtifactCommand: MAIN PROCEDURE }
   BEGIN
   Done:=False;
   REPEAT
      ConstructArtifactList(Menu,NoOfArtifacts,ArtifactList);
      IF NoOfArtifacts>0 THEN
         BEGIN
         DisplayMenu(Menu,1,4,LightGray,C.SYSDispSelect,80,21);
         WriteHelpLine(',:Move cursor <Enter>:Examine <A>:Activate <Esc>:Exit');

         REPEAT
            GetCharacter(AnyKey,NoCaseDistinct,Ch);
            IF NOT (Ch IN ['A',ESCKey]) THEN
               ActivateMenu(Menu,Ch);
         UNTIL Ch IN [ESCKey,'A'];
         ArtID:=ArtifactList[GetMenuSelect(Menu)];
         CleanUpMenu(Menu);
         ClrDisplayScreen;

         CASE Ch OF
            'A': BEGIN
               IF ArtifactSituation(ArtID,ActiveSIT) THEN
                  ActiveSituation(ArtID,Player);
               END;
            ESCKey: Done:=True;
            END;  { case }
         END
      ELSE
         BEGIN
         WriteCommLine('I am not aware of any artifacts in our possession, '+MyLord(Player)+'.');
         Done:=True;
         ActivateLatestStatusWindow;
         END;
   UNTIL Done;
   WriteErrorMessage('');
   END;  { ArtifactCommand }
*)

PROCEDURE DefenseCommand;
   VAR
      ShpI: ShipTypes;
      OrbI: ShellPos;
      Def: DefenseRecord;
      Ch: Char;
      Error: Boolean;
      ErrorString: String64;

   PROCEDURE Normalize(VAR Defenses: DefenseDistributionArray);
      VAR
         ShpI: ShipTypes;
         OrbI: ShellPos;
         Total: Integer;

      BEGIN
      FOR ShpI:=fgt TO trn DO
         BEGIN

	 (* First, clean any ships off the ground that shouldn't be there.*)
	 IF NOT (ShpI IN [fgt,trn,jtn]) THEN
           BEGIN
            Defenses[SbOrb,ShpI]:=Defenses[SbOrb,ShpI]+Defenses[Grnd,ShpI];
	    Defenses[Grnd,ShpI]:=0;
           END;

         (* Now, count the total ships for all orbits *)
         Total:=0;
         FOR OrbI:=DpSpc TO Grnd DO
            Inc(Total,Defenses[OrbI,ShpI]);

	 (* Now do some checking on that total: *)

	 (* If there's less than 100%, dump the rest in Suborbit *)
         IF Total<100 THEN
            Defenses[SbOrb,ShpI]:=Defenses[SbOrb,ShpI]+(100-Total);
	 
	 (* If there's more than 100%, we need to do several steps: *)
         IF Total>100 THEN
            BEGIN             (* First, even out the numbers *)
            For OrbI:=DpSpc TO Grnd Do
               Defenses[OrbI,ShpI]:=Trunc((Defenses[OrbI,ShpI]/Total)*100);
            Total:=0;           (* Now re-add them, for checking. *)
            FOR OrbI:=DpSpc TO Grnd DO
               Inc(Total,Defenses[OrbI,ShpI]);
           IF Total<100 THEN    (* And now dump extras into sub-orbit again. *)
              Defenses[SbOrb,ShpI]:=Defenses[SbOrb,ShpI]+100-Total;
            END;
         END;
      END;  { Normalize }

   PROCEDURE CheckForIllegalAmounts(Defenses: DefenseDistributionArray;
                                    VAR ErrorBool: Boolean;
                                    VAR ErrorStr: String64);
      VAR
         ShpI: ShipTypes;
         OrbI: ShellPos;
         Total: Integer;

      BEGIN
      Error:=False;
      FOR ShpI:=fgt TO trn DO
         BEGIN
         Total:=0;
         FOR OrbI:=DpSpc TO Grnd DO
            Inc(Total,Defenses[OrbI,ShpI]);

         IF Total<>100 THEN
            BEGIN
	       ErrorBool:=True;
               ErrorStr:='Total for each ship type must be 100 .. Normalizing';
            END;
         IF (Defenses[Grnd,ShpI]<>0) AND NOT (ShpI IN [fgt,trn,jtn]) THEN
            BEGIN
               ErrorBool:=True;
               ErrorStr:='Only fgt, trn and jtn can be on the ground .. Normalizing';
            END;
         END;
      END;  { CheckForIllegalAmounts }

   PROCEDURE ChangePercent(VAR Defenses: DefenseDistributionArray; OrbI: ShellPos;
                           ShpI: ShipTypes; Ch: Char);
      VAR
         Error,NewPercent: Integer;
         Line: String8;

      BEGIN
      IF Ch=ReturnKey THEN
         Line:=''
      ELSE
         Line:=Ch;

      REPEAT
         Error:=0;
         InputStrgDisplayScreen('New setting: ',2,18,8,Line);
         IF (Line=EscKey) OR (Line='') THEN
            NewPercent:=Defenses[OrbI,ShpI]
         ELSE
            Val(Line,NewPercent,Error);

      UNTIL Error=0;
      WriteBlanks(50,2,18,C.SYSDispWind);

      IF (NewPercent>100) OR (NewPercent<0) THEN
         NewPercent:=0;
      Defenses[OrbI,ShpI]:=NewPercent;
      END;  { ChangePercent }

   PROCEDURE UpdateLine(VAR Defenses: DefenseDistributionArray; ShpI: ShipTypes;
                        DrawCursor: Boolean; Cursor: ShellPos);
      VAR
         OrbI: ShellPos;
         Temp: String8;
         Line,Column,Color: Word;
         Total: Integer;

      BEGIN
      Column:=23;
      Line:=4+(Ord(ShpI)-Ord(fgt));
      Total:=0;
      FOR OrbI:=DpSpc TO Grnd DO
         BEGIN
         IF (OrbI=Cursor) AND DrawCursor THEN
            Color:=C.SYSDispSelect
         ELSE
            Color:=C.SYSDispWind;
         Str(Defenses[OrbI,ShpI]:3,Temp);
         Inc(Total,Defenses[OrbI,ShpI]);
         WriteString(Temp,Column,Line,Color);
         Inc(Column,10);
         END;

      Str(Total:3,Temp);
      WriteString(Temp,Column,Line,C.SYSDispWind);
      END;  { UpdateLine }

   { DefenseCommand: MAIN PROCEDURE }
   BEGIN
   GetDefenseSettings(Player,Def);

   ClrDisplayScreen;
   WriteString('Defenses:',1,1,C.SYSDispHigh);
   WriteString('DeepSp   HighOrb    Orbit     SubOrb    Ground   (Total)',22,3,C.SYSDispWind);
   FOR ShpI:=fgt TO trn DO
      WriteString(ThingNames[ShpI]+':',2,Ord(ShpI)-Ord(fgt)+4,C.SYSDispWind);

   FOR ShpI:=fgt TO trn DO
      UpdateLine(Def.ShellDefDist,ShpI,False,DpSpc);
   UpdateLine(Def.ShellDefDist,fgt,True,DpSpc);

   OrbI:=DpSpc;
   ShpI:=fgt;
   Error:=False;
   

   REPEAT
      GetCharacter(AnyKey,NoCaseDistinct,Ch);
      CASE Ch OF
         UArrKey: BEGIN
            UpdateLine(Def.ShellDefDist,ShpI,False,OrbI);
            IF ShpI=fgt THEN
               ShpI:=trn
            ELSE
               Dec(ShpI);
            UpdateLine(Def.ShellDefDist,ShpI,True,OrbI);
            END;
         DArrKey: BEGIN
            UpdateLine(Def.ShellDefDist,ShpI,False,OrbI);
            IF ShpI=trn THEN
               ShpI:=fgt
            ELSE
               Inc(ShpI);
            UpdateLine(Def.ShellDefDist,ShpI,True,OrbI);
            END;
         LArrKey: BEGIN
            IF OrbI=DpSpc THEN
               OrbI:=Grnd
            ELSE
               Dec(OrbI);
            UpdateLine(Def.ShellDefDist,ShpI,True,OrbI);
            END;
         RArrKey: BEGIN
            IF OrbI=Grnd THEN
               OrbI:=DpSpc
            ELSE
               Inc(OrbI);
            UpdateLine(Def.ShellDefDist,ShpI,True,OrbI);
            END;
         '+','-',
         ReturnKey,
         '0'..'9': BEGIN
            ChangePercent(Def.ShellDefDist,OrbI,ShpI,Ch);
            UpdateLine(Def.ShellDefDist,ShpI,True,OrbI);
            END;
	 EscKey: BEGIN
            CheckForIllegalAmounts(Def.ShellDefDist,Error,ErrorString);
            IF Error THEN
              WriteString(ErrorString,10,13,C.SYSDispWind);
            Normalize(Def.ShellDefDist);
            FOR ShpI:=fgt TO trn DO
              UpdateLine(Def.ShellDefDist,ShpI,False,DpSpc);
            UpdateLine(Def.ShellDefDist,fgt,True,DpSpc);
            ShpI:=fgt;
	    OrbI:=DpSpc
            END;
       END; {Case}
       IF NOT (Ch=EscKey) Then
         WriteString('                                                           ',10,13,C.SYSDispWind);
           
   UNTIL (Ch=EscKey) AND NOT (Error);

   SetDefenseSettings(Player,Def);
   ClrDisplayScreen;
   ActivateLatestStatusWindow;
   END;  { DefenseCommand }

PROCEDURE SelfDestructCommand;
   VAR
      ObjID: IDNumber;
      XY: XYCoord;
      Abort: Boolean;
      BaseN: String32;
      Pop: Population;
      PopN: String16;
      Ans: String8;

   PROCEDURE GetBaseToDestroy(Player: Empire; VAR ObjID: IDNumber; VAR Abort: Boolean);
      VAR
         Parm: String32;
         Error: Boolean;

      BEGIN
      REPEAT
         Error:=False;
         InputParameter('What object do you wish to destroy? ',Parm);
         IF Parm<>'' THEN
            BEGIN
            InterpretObj(Parm,ObjID,Error);
            IF NOT Error THEN
               BEGIN
               IF GetStatus(ObjID)<>Player THEN
                  BEGIN
                  Parm[1]:=UpCase(Parm[1]);
                  WriteErrorMessage('"'+Parm+'" is not a part of '+EmpireName(Player)+', '+MyLord(Player)+'.');
                  Error:=True;
                  END
               ELSE IF ((ObjID.ObjTyp<>Base) OR (GetBaseType(ObjID)=cmp)) 
                       AND (ObjID.ObjTyp<>Gate) THEN
                  BEGIN
                  WriteErrorMessage(MyLord(Player)+', only bases, and stargates can be destroyed.');
                  Error:=True;
                  END;
               END;
            END;
      UNTIL (NOT Error);

      IF Parm='' THEN
         Abort:=True
      ELSE
         Abort:=False;

      WriteCommLine('');
      WriteErrorMessage('');
      END;  { GetBaseToDestroy }

   { SelfDestructCommand: MAIN PROCEDURE }
   BEGIN
   GetBaseToDestroy(Player,ObjID,Abort);
   IF NOT Abort THEN
      BEGIN
      BaseN:=ObjectName(Player,ObjID,LongFormat);
      Pop:=GetPopulation(ObjID);
      Str(Pop*10,PopN);

      ClrDisplayScreen;
      WriteString('Self-Destruct:',1,1,C.SYSDispHigh);

      BaseN[1]:=UpCase(BaseN[1]);
      IF ObjID.ObjTyp=Base THEN
         BEGIN
         WriteString(BaseN+' reports: Destruct sequence activated...',2,3,C.SYSDispWind);
         WriteString('Are you sure about this, '+MyLord(Player)+'?  Destruction of the base will',2,4,C.SYSDispWind);
         WriteString('result in the deaths of '+PopN+' million people and will destroy all ships',2,5,C.SYSDispWind);
         WriteString('in the sector.',2,6,C.SYSDispWind);
         END
      ELSE
         BEGIN
         WriteString('Atomic charges set on '+BaseN+'...',2,3,C.SYSDispWind);
         WriteString('Are you sure about this, '+MyLord(Player)+'?  It took us many years to build',2,4,C.SYSDispWind);
         WriteString('this structure.',2,5,C.SYSDispWind);
         END;

      Ans:='';
      InputStrgDisplayScreen('Give destruct confirmation (y/N) ? ',2,7,1,Ans);
      IF UpCase(Ans[1])='Y' THEN
         BEGIN
         GetCoord(ObjID,XY);
         SelfDestructObject(ObjID);
         Map^.Buffer[XY.y,XY.x].WorldChar:=' ';
         UMSector(XY.x,XY.y,Map^);
         UMFleets(XY.x,XY.y,Map^);
         WriteCommLine(BaseN+' has been destroyed, '+MyLord(Player)+'.');
         END
      ELSE
         WriteCommLine('Self-destruct aborted, '+MyLord(Player)+'.');

      ClrDisplayScreen;
      END;
   ActivateLatestStatusWindow;
   END;  { SelfDestructCommand }

(*

PROCEDURE HolocaustCommand;
   VAR
      FltN,WorldN,DeathN: String32;
      WorldID,FltID: IDNumber;
      Abort,Revert: Boolean;
      Eff: Index;
      Deaths: Population;
      Ans: String8;
      Losses: ShipArray;
      IndusDest: IndusArray;
      Result: HoloResultTypes;

   PROCEDURE GetFleetToUse(Player: Empire; VAR FltID,WorldID: IDNumber;
                           VAR Eff: Index; VAR Abort: Boolean);
      VAR
         Parm: String32;
         Error: Boolean;
         XY: XYCoord;

      BEGIN
      REPEAT
         Error:=False;
         InputParameter('Give holocaust instructions to which fleet? ',Parm);
         IF Parm<>'' THEN
            BEGIN
            InterpretObj(Parm,FltID,Error);
            IF NOT Error THEN
               BEGIN
               GetCoord(FltID,XY);
               GetObject(XY,WorldID);
               Eff:=HolocaustEffectiveness(FltID,WorldID);
               IF GetStatus(FltID)<>Player THEN
                  BEGIN
                  Parm[1]:=UpCase(Parm[1]);
                  WriteErrorMessage('"'+Parm+'" is an enemy fleet, '+MyLord(Player)+'.');
                  Error:=True;
                  END
               ELSE IF GetStatus(WorldID)=Player THEN
                  BEGIN
                  WriteErrorMessage('You cannot holocaust your own territory, '+MyLord(Player)+'.');
                  Error:=True;
                  END
               ELSE IF NOT (WorldID.ObjTyp IN [Pln,Base]) THEN
                  BEGIN
                  WriteErrorMessage('There is no world in that sector to holocaust, '+MyLord(Player)+'.');
                  Error:=True;
                  END
               ELSE IF (Eff<=0) THEN
                  BEGIN
                  WriteErrorMessage('There are too many defenses on the world, '+MyLord(Player)+'.');
                  Error:=True;
                  END
               END;
            END;
      UNTIL NOT Error;

      IF Parm='' THEN
         Abort:=True
      ELSE
         Abort:=False;

      WriteCommLine('');
      WriteErrorMessage('');
      END;  { GetFleetToUse }

   { HolocaustCommand: MAIN PROCEDURE }
   BEGIN
   GetFleetToUse(Player,FltID,WorldID,Eff,Abort);
   IF NOT Abort THEN
      BEGIN
      FltN:=ObjectName(Player,FltID,LongFormat);
      WorldN:=ObjectName(Player,WorldID,LongFormat);

      Deaths:=Round((GetPopulation(WorldID)/1000)*Eff);
      IF Deaths<100 THEN
         BEGIN
         Str(Deaths*10,DeathN);
         DeathN:=DeathN+' million';
         END
      ELSE
         BEGIN
         Str(Deaths/100:4:1,DeathN);
         DeathN:=DeathN+' billion';
         END;

      ClrDisplayScreen;
      WriteString('Holocaust:',1,1,C.SYSDispHigh);

      WriteString(FltN+' on hyperbolic approach to '+WorldN+', standing by...',2,3,C.SYSDispWind);
      WriteString('All planetary bombardment weapons ready, nuclear warheads armed.',2,4,C.SYSDispWind);
      WriteString('Estimated effectiveness: '+DeathN+' casualties.',2,5,C.SYSDispWind);
      
      WriteString(MyLord(Player)+', I must remind you that the people of the empire will not',2,7,C.SYSDispWind);
      WriteString('support this extreme action.',2,8,C.SYSDispWind);
      Ans:='';
      InputStrgDisplayScreen('Do you wish to give destruct confirmation (y/N) ? ',2,10,1,Ans);
      IF UpCase(Ans[1])='Y' THEN
         BEGIN
         ClrDisplayScreen;
         WriteString('Holocaust:',1,1,C.SYSDispHigh);

         HolocaustWorld(Player,Eff,WorldID,FltID,Losses,IndusDest,Deaths,Revert,Result);
         IF Result=WorldSurrendersHRT THEN
            BEGIN
            WriteString(MyLord(Player)+', representatives of the world have surrendered unconditionally',2,3,C.SYSDispWind);
            WriteString('after seeing a display of the empire''s power.',2,4,C.SYSDispWind);
            END
         ELSE
            BEGIN
            WriteString(WorldN+' has been attacked, '+MyLord(Player)+'.',2,3,C.SYSDispWind);
            END;
         END
      ELSE
         BEGIN
         WriteCommLine('Holocaust aborted, '+MyLord(Player)+'.');
         ClrDisplayScreen;
         END;
      END;
   END;  { HolocaustCommand }

*)

END.
