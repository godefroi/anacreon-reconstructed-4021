(* DESIGN.PAS ------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT Design;

{$IFDEF Overlay}
{$O+,F+}
{$ENDIF}

INTERFACE

USES Crt,                                       { Standard CRT Library. }
     Strg,                                      { String Library. }
     Int,                                       { Integer Library. }
     EIO,                                       { Extended IO Library. }
     Menu,
     TextStrc,
     Edit,
     Types,
     Galaxy,
     DataCnst,
     News,
     Mess,
     Misc,                                      { Miscellaneous procedures. }
     PrimIntr,                                  { Primitives. }
     Intrface,                                  { Interface. }
     Fleet,
     Attack,
     SWindows,                                  { Status windows. }
     MapWind,
     Display;                                   { Display procedures. }

PROCEDURE ChangeISSPCom(World: IDNumber);
PROCEDURE SellTechnology;
PROCEDURE SendMessageCommand;
PROCEDURE LaunchLAM(BaseID: IDNumber);
PROCEDURE DesignateCommand(World: IDNumber);
PROCEDURE GrantIndependenceCommand(WorldID: IDNumber);
PROCEDURE ReadMessageCommand;
PROCEDURE TerraformCommand(WorldID: IDNumber);

IMPLEMENTATION

USES
   Environ;

PROCEDURE LaunchLAM(BaseID: IDNumber);
   CONST
      OkRES       = 0;
      ExitRES     = 1;
      NoTargetRES = 2;

   VAR
      Target: IDNumber;
      TargetXY: XYCoord;
      BasN,TarN: String32;
      LAMStr,DestStr: String8;
      Defns: DefnsArray;
      LAMToUse: Resources;
      A: Integer;
      ShipsDest: ShipArray;
      DefnsDest: DefnsArray;
      ResI: ResourceTypes;
      NoOfLines: Byte;
      Result: Word;

   PROCEDURE GetTarget(BaseID: IDNumber; VAR Target: IDNumber; VAR Result: Word);
   { GetTarget: ----------------------------------------------------------------
      GetTarget asks the player to choose a target.  It makes a list of all
      the targets that can be attack by LAMs from BaseID and allows the 
      player to choose one.

      On exit, the DisplayScreen is active and cleared.
   --------------------------------------------------------------------------- }

      VAR
         Menu: MenuStructure;
         PotentialTarget: IDList;
         ListSize: Byte;
         BaseXY: XYCoord;
         Exit: Boolean;

      PROCEDURE CreateMenu(BaseXY: XYCoord; VAR Menu: MenuStructure;
                           VAR PotentialTarget: IDList; VAR ListSize: Byte);

         VAR
            i: Byte;
            ObjID: IDNumber;
            ObjXY: XYCoord;
            Emp: Empire;
            TargetFleets: FleetSet;

         PROCEDURE AddTarget(Target: IDNumber;
                             Emp: Empire;
                             VAR Menu: MenuStructure;
                             VAR PotentialTarget: IDList;
                             VAR ListSize: Byte);

            VAR
               Line: LineStr;
               XY: XYCoord;

            BEGIN
            ListSize:=ListSize+1;
            PotentialTarget[ListSize]:=Target;

            IF (Target.ObjTyp=Flt) AND (NOT Scouted(Player,Target)) THEN
               BEGIN
               GetCoord(Target,XY);
               GetCoordName(XY,Line);
               Line:='Enemy fleet at '+Line+'  (Unknown)';
               END
            ELSE
               BEGIN
               Line:=ObjectName(Player,Target,LongFormat);
               Line:=Line+'  ('+EmpireName(Emp)+')';
               END;
            AddMenuLine(Menu,Line);
            END;  { AddTarget }

         { CreateMenu: MAIN PROCEDURE }
         BEGIN
         InitializeMenu(Menu);
         ListSize:=0;

         ObjID.ObjTyp:=Flt;
         TargetFleets:=SetOfActiveFleets-SetOfFleetsOf[Player];
         FOR i:=1 TO MaxNoOfFleets DO
            IF i IN TargetFleets THEN
               BEGIN
               ObjID.Index:=i;
               GetCoord(ObjID,ObjXY);
               IF Known(Player,ObjID) AND (Distance(BaseXY,ObjXY)<=5) THEN
                  AddTarget(ObjID,GetStatus(ObjID),Menu,PotentialTarget,ListSize);
               END;

         ObjID.ObjTyp:=Pln;
         FOR i:=1 TO NoOfPlanets DO
            BEGIN
            ObjID.Index:=i;
            GetCoord(ObjID,ObjXY);
            Emp:=GetStatus(ObjID);
            IF Known(Player,ObjID) AND (Emp<>Player) AND (Distance(BaseXY,ObjXY)<=5) THEN
               AddTarget(ObjID,Emp,Menu,PotentialTarget,ListSize);
            END;

         ObjID.ObjTyp:=Base;
         FOR i:=1 TO MaxNoOfStarbases DO
            IF i IN SetOfActiveStarbases THEN
               BEGIN
               ObjID.Index:=i;
               GetCoord(ObjID,ObjXY);
               Emp:=GetStatus(ObjID);
               IF Known(Player,ObjID) AND (Emp<>Player) AND (Distance(BaseXY,ObjXY)<=5) THEN
                  AddTarget(ObjID,Emp,Menu,PotentialTarget,ListSize);
               END;
         END;  { CreateMenu }

      { GetTarget: MAIN PROCEDURE }
      BEGIN
      ClrDisplayScreen;
      WriteString('Launch LAMs:',1,1,C.SYSDispHigh);
      WriteString('What target shall we hit, '+MyLord(Player)+'?',2,3,C.SYSDispWind);
      GetCoord(BaseID,BaseXY);

      CreateMenu(BaseXY,Menu,PotentialTarget,ListSize);
      IF ListSize=0 THEN
         Result:=NoTargetRES
      ELSE
         BEGIN
         GetIDMenuChoice(Menu,PotentialTarget,Target,Exit);
         IF Exit THEN
            Result:=ExitRES
         ELSE
            Result:=OkRES;
         END;

      ClrDisplayScreen;
      END;  { GetTarget }


   { LaunchLAM: MAIN PROCEDURE }
   BEGIN
   GetDefns(BaseID,Defns);
   BasN:=ObjectName(Player,BaseID,LongFormat);

   GetTarget(BaseID,Target,Result);

   IF Result=OkRES THEN
      BEGIN
      GetCoord(Target,TargetXY);
      TarN:=ObjectName(Player,Target,LongFormat);
      Str(Defns[LAM],LAMStr);

      ClrDisplayScreen;
      WriteString('Launch LAMs:',1,1,C.SYSDispHigh);

      WriteString('LAMs at '+BasN+' targetted on '+TarN+'.',1,3,C.SYSDispWind);

      A:=0;
      REPEAT
         InputIntegerDisplayScreen('There are '+LAMStr+' LAMs at '+BasN+'.  Launch how many? ',1,4,A);
         WriteBlanks(78,1,5,C.SYSDispWind);
         IF A<0 THEN
            WriteString('You must use a positive number, '+MyLord(Player)+'!',3,5,C.SYSDispWind)
         ELSE IF A>Defns[LAM] THEN
            WriteString('There aren''t that many LAMs at '+BasN+', '+MyLord(Player)+'.',3,5,C.SYSDispWind);
      UNTIL (A>=0) AND (A<=Defns[LAM]);

      LAMToUse:=A;
      IF LAMToUse>0 THEN
         BEGIN
         LAMAttack(Player,LAMToUse,Target,ShipsDest,DefnsDest);
         Defns[LAM]:=Defns[LAM]-A;
         PutDefns(BaseID,Defns);
         END;

      NoOfLines:=0;
      IF Target.ObjTyp=Flt THEN
         BEGIN
         FOR ResI:=fgt TO trn DO
            IF ShipsDest[ResI]>0 THEN
               BEGIN
               Str(ShipsDest[ResI],DestStr);
               WriteString(DestStr+' '+ThingNames[ResI]+' were destroyed.',1,6+NoOfLines,C.SYSDispWind);
               NoOfLines:=NoOfLines+1;
               END;

         IF NoOfLines=0 THEN
            WriteString('No ships were destroyed.',1,6,C.SYSDispWind);

         UMFleets(TargetXY.x,TargetXY.y,Map^);
         END
      ELSE
         BEGIN
         FOR ResI:=LAM TO ion DO
            IF DefnsDest[ResI]>0 THEN
               BEGIN
               Str(DefnsDest[ResI],DestStr);
               WriteString(DestStr+' '+ThingNames[ResI]+' were destroyed.',1,6+NoOfLines,C.SYSDispWind);
               Inc(NoOfLines);
               END;

         IF NoOfLines=0 THEN
            WriteString('No defenses were destroyed.',1,6,C.SYSDispWind);
         END;
      WriteCommLine('');
      END
   ELSE IF Result=NoTargetRES THEN
      BEGIN
      WriteCommLine('No targets can be reached from '+BasN+', '+MyLord(Player)+'.');
      END
   ELSE
      BEGIN
      WriteCommLine('');
      ClrDisplayScreen;
      END;

   WriteErrorMessage('');
   END;  { LaunchLAM }

PROCEDURE ChangeISSPCom(World: IDNumber);
{ ChangeISSP:
   The ISSP command allows the emperor to change the Industrial Self-Sufficiency 
   Percentange for a given world.  This allows different worlds to have 
   different import/export requirement.  Thus ocean worlds could import metals 
   but export chemicals. 

   The procedure assumes that World is the ID of a world in the empire. }

   TYPE
      ISSPArray = ARRAY [0..3] OF Byte;

   VAR
      NewISSP: ISSPArray;

   PROCEDURE GetISSPArray(World: IDNumber; VAR OldISSP: ISSPArray);
      BEGIN
      OldISSP[0]:=GetISSP(World,CheInd);
      OldISSP[1]:=GetISSP(World,MinInd);
      OldISSP[2]:=GetISSP(World,SupInd);
      OldISSP[3]:=GetISSP(World,TriInd);
      END;  { GetISSPArray }

   PROCEDURE SetISSPArray(World: IDNumber; VAR NewISSP: ISSPArray);
      BEGIN
      SetISSP(World,CheInd,NewISSP[0]);
      SetISSP(World,MinInd,NewISSP[1]);
      SetISSP(World,SupInd,NewISSP[2]);
      SetISSP(World,TriInd,NewISSP[3]);
      END;  { SetISSPArray }

   PROCEDURE DisplayISSP(VAR NewISSP: ISSPArray);
      CONST
         { Coordinates to display table. }
         Dx = 2;
         Dy = 9;

      VAR
         Cursor,i: Byte;
         Ch: Char;

      FUNCTION GetISSPLine(i: Byte; ISSPInd: Byte): LineStr;
         CONST
            ISSPStr: ARRAY [0..MaxISSP] OF String50 =
               ( '  1%  (imports 99% of need)      ',
                 ' 10%  (imports 90% of need)      ',
                 ' 25%  (imports 75% of need)      ',
                 ' 50%  (imports 50% of need)      ',
                 ' 75%  (imports 25% of need)      ',
                 '100%  (no import/export)         ',
                 '150%  (exports 33% of production)',
                 '200%  (exports 50% of production)',
                 '300%  (exports 67% of production)',
                 '400%  (exports 75% of production)',
                 '500%  (exports 80% of production)' );

         BEGIN
         CASE i OF
            0 : GetISSPLine:='Chemical industry: '+ISSPStr[ISSPInd];
            1 : GetISSPLine:='Mining industry:   '+ISSPStr[ISSPInd];
            2 : GetISSPLine:='Supply industry:   '+ISSPStr[ISSPInd];
            3 : GetISSPLine:='Trillum industry:  '+ISSPStr[ISSPInd];
         END;  { case }
         END;  { GetISSPLine }

      BEGIN
      { Initialize }
      WriteHelpLine(':Move '#027#026':ChangeISSP Esc:Exit');
      Cursor:=0;
      WriteString(GetISSPLine(Cursor,NewISSP[Cursor]),Dx,Dy,C.SYSDispSelect);
      FOR i:=1 TO 3 DO
         WriteString(GetISSPLine(i,NewISSP[i]),Dx,Dy+i,C.SYSDispWind);

      REPEAT
         GetCharacter([EscKey,ReturnKey,LArrKey,RArrKey,UArrKey,PgUpKey,DArrKey,PgDnKey],NoCaseDistinct,Ch);
         CASE Ch OF
            LArrKey: BEGIN
               IF NewISSP[Cursor]>0 THEN 
                  BEGIN
                  NewISSP[Cursor]:=NewISSP[Cursor]-1;
                  WriteString(GetISSPLine(Cursor,NewISSP[Cursor]),Dx,Dy+Cursor,C.SYSDispSelect);
                  END;
               END;

            RArrKey: BEGIN
               IF NewISSP[Cursor]<MaxISSP THEN 
                  BEGIN
                  NewISSP[Cursor]:=NewISSP[Cursor]+1;
                  WriteString(GetISSPLine(Cursor,NewISSP[Cursor]),Dx,Dy+Cursor,C.SYSDispSelect);
                  END;
               END;

            UArrKey,
            PgUpKey: BEGIN
               WriteString(GetISSPLine(Cursor,NewISSP[Cursor]),Dx,Dy+Cursor,C.SYSDispWind);
               IF Cursor=0 THEN Cursor:=3 ELSE Cursor:=Cursor-1;
               WriteString(GetISSPLine(Cursor,NewISSP[Cursor]),Dx,Dy+Cursor,C.SYSDispSelect);
               END;

            DArrKey,
            PgDnKey: BEGIN
               WriteString(GetISSPLine(Cursor,NewISSP[Cursor]),Dx,Dy+Cursor,C.SYSDispWind);
               IF Cursor=3 THEN Cursor:=0 ELSE Cursor:=Cursor+1;
               WriteString(GetISSPLine(Cursor,NewISSP[Cursor]),Dx,Dy+Cursor,C.SYSDispSelect);
               END;
         END;  { case }
      UNTIL (Ch=EscKey) OR (Ch=ReturnKey);
      END;  { DisplayISSP }

   BEGIN
   ClrDisplayScreen;
   WriteString('ISSP:',1,1,C.SYSDispHigh);

   WriteString('The Industrial Self-Sufficiency Percentage of a given industry is defined',2,3,C.SYSDispWind);
   WriteString('for all raw material industries as the percent of total raw materials',2,4,C.SYSDispWind);
   WriteString('needed by a given world, that are actually produced on that world.  For',2,5,C.SYSDispWind);
   WriteString('example, if a world produces 50% of the metals that it needs, its ISSP for',2,6,C.SYSDispWind);
   WriteString('the mining industry is said to be 50.',2,7,C.SYSDispWind);

   GetISSPArray(World,NewISSP);
   DisplayISSP(NewISSP);
   SetISSPArray(World,NewISSP);

   ClrDisplayScreen;
   WriteCommLine('');
   WriteErrorMessage('');
   ActivateLatestStatusWindow;
   END;  { ChangeISSPCom }

PROCEDURE SellTechnology;
   VAR
      Emp: Empire;
      Abort: Boolean;
      Tech: TechLevel;
      TechSet: TechnologySet;
      NewTechnology: TechnologyTypes;
      Loc: Location;

   PROCEDURE GetEmpireToSell(VAR Emp: Empire; VAR Abort: Boolean);
      VAR
         PotentialEmpire: ARRAY [1..8] OF Empire;
         i,NoOfEmpires: Word;
         PlyTech,Tech: TechLevel;
         PlyTechSet,TechSet: TechnologySet;
         Ch: Char;
         Menu: MenuStructure;
         EmpI: Empire;

      BEGIN
      GetEmpireTechnology(Player,PlyTech,PlyTechSet);
      NoOfEmpires:=0;
      FOR EmpI:=Empire1 TO Empire8 DO
         IF EmpireActive(EmpI) AND (EmpI<>Player) THEN
            BEGIN
            GetEmpireTechnology(EmpI,Tech,TechSet);
            IF (PlyTechSet * (TechDev[Tech]-TechDev[Pred(Tech)]))<>[] THEN
               BEGIN
               Inc(NoOfEmpires);
               PotentialEmpire[NoOfEmpires]:=EmpI;
               END;
            END;

      IF NoOfEmpires=0 THEN
         BEGIN
         Emp:=Indep;
         Abort:=False;
         END
      ELSE
         BEGIN
         ClrDisplayScreen;
         WriteString('Trade Technology:',1,1,C.SYSDispHigh);
         InitializeMenu(Menu);
         FOR i:=1 TO NoOfEmpires DO
            AddMenuLine(Menu,EmpireName(PotentialEmpire[i]));

         WriteString('Which empire do you wish to transfer technology to?',1,4,C.SYSDispWind);
         DisplayMenu(Menu,5,12,LightGray,C.SYSDispSelect,45,7);
         WriteHelpLine(',:Move cursor <Enter>:Select <Esc>:Exit');
         REPEAT
            GetCharacter(AnyKey,NoCaseDistinct,Ch);
            ActivateMenu(Menu,Ch);
         UNTIL Ch IN [ESCKey,ReturnKey];

         IF Ch=ReturnKey THEN
            BEGIN
            Emp:=PotentialEmpire[GetMenuSelect(Menu)];
            Abort:=False;
            END
         ELSE
            Abort:=True;
         CleanUpMenu(Menu);
         ClrDisplayScreen;
         END;
      END;  { GetEmpireToSell }

   PROCEDURE GetTechnologyToSell(Emp: Empire;
                                 VAR NewTech: TechnologyTypes; 
                                 VAR Abort: Boolean);
      VAR
         PlyTech,Tech: TechLevel;
         PlyTechSet,TechSet: TechnologySet;
         PotentialTechnology: ARRAY [1..10] OF TechnologyTypes;
         i,NoOfTechs: Word;
         Ch: Char;
         Menu: MenuStructure;
         Line: LineStr;
         TchI: TechnologyTypes;
         PotentialTechnologies: TechnologySet;

      BEGIN
      ClrDisplayScreen;
      WriteString('Trade Technology: ',1,1,C.SYSDispHigh);
      GetEmpireTechnology(Player,PlyTech,PlyTechSet);
      GetEmpireTechnology(Emp,Tech,TechSet);
      PotentialTechnologies:=PlyTechSet * (TechDev[Tech]-TechDev[Pred(Tech)]);
      NoOfTechs:=0;
      FOR TchI:=LAM to ter DO
         BEGIN
         IF (TchI IN PotentialTechnologies) THEN
            BEGIN
            Inc(NoOfTechs);
            PotentialTechnology[NoOfTechs]:=TchI;
            END;
         END;

      InitializeMenu(Menu);
      FOR i:=1 TO NoOfTechs DO
         BEGIN
         Line:=TechnologyName[PotentialTechnology[i]]+' technology';
         Line[1]:=UpCase(Line[1]);
         AddMenuLine(Menu,Line);
         END;

      WriteString('Which technology do you wish to transfer?',1,4,C.SYSDispWind);
      DisplayMenu(Menu,5,12,LightGray,C.SYSDispSelect,45,7);
      WriteHelpLine(',:Move cursor <Enter>:Select <Esc>:Exit');
      REPEAT
         GetCharacter(AnyKey,NoCaseDistinct,Ch);
         ActivateMenu(Menu,Ch);
      UNTIL Ch IN [ESCKey,ReturnKey];

      IF Ch=ReturnKey THEN
         BEGIN
         NewTech:=PotentialTechnology[GetMenuSelect(Menu)];
         Abort:=False;
         END
      ELSE
         Abort:=True;
      CleanUpMenu(Menu);
      ClrDisplayScreen;
      END;  { GetTechnologyToSell }

   BEGIN
   WriteCommLine('');
   WriteErrorMessage('');
   GetEmpireToSell(Emp,Abort);
   IF NOT Abort THEN
      BEGIN
      IF Emp=Indep THEN
         BEGIN
         WriteCommLine('Unfortunately, '+MyLord(Player)
                      +', you have nothing that others would want.');
         ActivateLatestStatusWindow;
         END
      ELSE
         BEGIN
         GetTechnologyToSell(Emp,NewTechnology,Abort);
         IF NOT Abort THEN
            BEGIN
            GetEmpireTechnology(Emp,Tech,TechSet);
            TechSet:=TechSet+[NewTechnology];
            SetEmpireTechnology(Emp,Tech,TechSet);
            Loc.ID:=EmptyQuadrant;
            Loc.XY:=Limbo;
            AddNews(Emp,NSellTech,Loc,Ord(Player),Ord(NewTechnology),0);
            WriteCommLine('Transfer of '+TechnologyName[NewTechnology]+' technology to '+EmpireName(Emp)+' completed.');
            END;
         END;
      END
   ELSE
      WriteCommLine('');
   ActivateLatestStatusWindow;
   END;  { SellTechnology }

PROCEDURE GrantIndependenceCommand(WorldID: IDNumber);
   VAR
      Abort: Boolean;
      Loc: Location;
      WorldN: String32;
      Line: LineStr;
      XY: XYCoord;
      Emp: Empire;
      Ans: String8;

   PROCEDURE GetNewEmpire(WorldID: IDNumber;
                          VAR Emp: Empire;
                          VAR Abort: Boolean);
      VAR
         Fleets: FleetSet;
         WorldXY: XYCoord;
         PotentialEmpire: ARRAY [1..10] OF Empire;
         i,NoOfEmpires: Word;
         EmpiresAdded: EmpireSet;
         FltID: IDNumber;
         Menu: MenuStructure;
         Ch: Char;

      BEGIN
      GetCoord(WorldID,WorldXY);
      GetFleets(WorldXY,Fleets);
      NoOfEmpires:=1;
      PotentialEmpire[1]:=Indep;
      EmpiresAdded:=[Player];
      FltID.ObjTyp:=Flt;
      FOR i:=1 TO MaxNoOfFleets DO
         IF i IN Fleets THEN
            BEGIN
            FltID.Index:=i;
            Emp:=GetStatus(FltID);
            IF (NOT (Emp IN EmpiresAdded))
               AND Scouted(Player,FltID) THEN
               BEGIN
               Inc(NoOfEmpires);
               PotentialEmpire[NoOfEmpires]:=Emp;
               EmpiresAdded:=EmpiresAdded+[Emp];
               END;
            END;

      InitializeMenu(Menu);
      FOR i:=1 TO NoOfEmpires DO
         AddMenuLine(Menu,EmpireName(PotentialEmpire[i]));

      WriteString('Which empire do you wish to give this world to?',1,4,C.SYSDispWind);
      DisplayMenu(Menu,5,12,LightGray,C.SYSDispSelect,45,7);
      WriteHelpLine(',:Move cursor <Enter>:Select <Esc>:Exit');
      REPEAT
         GetCharacter(AnyKey,NoCaseDistinct,Ch);
         ActivateMenu(Menu,Ch);
      UNTIL Ch IN [ESCKey,ReturnKey];

      IF Ch=ReturnKey THEN
         BEGIN
         Emp:=PotentialEmpire[GetMenuSelect(Menu)];
         Abort:=False;
         END
      ELSE
         Abort:=True;
      CleanUpMenu(Menu);
      ClrDisplayScreen;
      END;  { GetNewEmpire }

   BEGIN
   WriteCommLine('');
   WriteErrorMessage('');
   ClrDisplayScreen;
   WriteString('Liberate:',1,1,C.SYSDispHigh);
   GetNewEmpire(WorldID,Emp,Abort);
   IF NOT Abort THEN
      BEGIN
      ClrDisplayScreen;
      WriteString('Liberate:',1,1,C.SYSDispHigh);
      Abort:=True;
      WorldN:=ObjectName(Player,WorldID,LongFormat);
      IF Emp=Indep THEN
         Line:='Do you really want to grant independence to '+WorldN+' (y/N) ? '
      ELSE
         Line:='Do you really want to give '+WorldN+' to '+EmpireName(Emp)+' (y/N) ? ';
      Ans:='';
      InputStrgDisplayScreen(Line,2,3,1,Ans);
      IF UpCase(Ans[1])='Y' THEN
         BEGIN
         IF (Emp=Indep) AND (WorldID.ObjTyp<>Base) THEN
            DesignateWorld(WorldID,IndTyp)
         ELSE
            DesignateWorld(WorldID,GetType(WorldID));
         InitializeISSP(WorldID);
         SetStatus(WorldID,Emp);
         GetCoord(WorldID,XY);
         UMSector(XY.x,XY.y,Map^);
         Loc.ID:=WorldID;
         Loc.XY:=Limbo;
         AddNews(Emp,GInd,Loc,Ord(Player),0,0);
         IF Emp=Indep THEN
            WriteCommLine(WorldN+' is now independent.')
         ELSE
            WriteCommLine(WorldN+' is now part of the empire of '+EmpireName(Emp)+'.');
         END;
      END;

   ClrDisplayScreen;
   ActivateLatestStatusWindow;
   END;  { GrantIndependenceCommand }

PROCEDURE DesignateCommand(World: IDNumber);
{ DesignateCommand:
   DesignateCommand takes a world ID and a new type.  It redesignates the
   world to be the given new type.  The capital cannot be redesignated, but
   if another world is designated as a capital, the capital is automatically
   assigned as a base planet.

   Globals:
      Player, Cap, CapitalX, CapitalY.

   Interface:
      SetUpSector, GetCoord, DesignateWorld, GetEfficiency.

   I/O:
      ClrDisplayScreen, WriteDisplayScreen, InputStrgDisplayScreen.         }

   CONST
      TypeN: ARRAY [WorldTypes] OF String32 =
         ( 'an agricultural world',
           'an ambrosia world',
           'a base planet',
           'a specialized base planet',
           'the capital of the empire',
           'a chemical factory world',
           'an independent world',
           'a jumpship complex',
           'a specialized jumpship complex',
           'a metal-mining world',
           'a ninja world',
           'an outpost',
           'a mining world',
           'a specialized mining world',
           'a starship complex',
           'a specialized starship complex',
           'a warpship complex',
           'a specialized warpship complex',
           'a research university world',
           'a terraforming world',
           'a trillum-mining world' );

   VAR
      Exit,Ok: Boolean;
      Ans: String[1];
      XY: XYCoord;
      WorldN: String32;
      EffStr: String8;
      CapID: IDNumber;
      CapXY: XYCoord;
      Typ: WorldTypes;
      Cls: WorldClass;

   PROCEDURE GetDesignation(WorldID,CapID: IDNumber; VAR NewTyp: WorldTypes;
                            VAR Exit: Boolean);
      VAR
         Menu: MenuStructure;
         PotentialType: ARRAY [1..30] OF WorldTypes;
         NoOfTypes: Word;
         Ch: Char;
         TypI: WorldTypes;
         Tech: TechLevel;
         Cls: WorldClass;
         Line: LineStr;

      BEGIN
      InitializeMenu(Menu);
      NoOfTypes:=0;
      Tech:=GetTech(WorldID);
      Cls:=GetClass(WorldID);

      FOR TypI:=AgrTyp TO TriTyp DO
         BEGIN
         IF (MinTechForType[TypI]<=Tech)
            AND NOT (TypI IN [OutTyp,BseSTyp,JmpSTyp,StrSTyp,TrnSTyp,RawSTyp,TerTyp])
            AND ((TypI<>AmbTyp) OR (Cls IN [AmbCls,ParCls])) THEN
            BEGIN
            Inc(NoOfTypes);
            PotentialType[NoOfTypes]:=TypI;
            Line:=TypeName[TypI];
            Line[1]:=UpCase(Line[1]);
            AdjustString(Line,30);
            IF TypI=RsrTyp THEN
               Line:=Line+'(research)'
            ELSE IF TypI=RawTyp THEN
               Line:=Line+'raw material mining'
            ELSE IF TypI=CapTyp THEN
               Line:=Line+'administration'
            ELSE
               Line:=Line+IndusNames[PrincipalIndustry[TypI]];
            AddMenuLine(Menu,Line);
            END;
         END;

      WriteString('World Type                    Main Industry',15,7,C.SYSDispHigh);
      WriteHelpLine(',:Move cursor <Enter>:Select <Esc>:Exit');
      DisplayMenu(Menu,15,12,LightGray,C.SYSDispSelect,60,10);

      REPEAT
         GetCharacter(AnyKey,NoCaseDistinct,Ch);
         ActivateMenu(Menu,Ch);
      UNTIL Ch IN [ESCKey,ReturnKey];

      IF Ch=ReturnKey THEN
         BEGIN
         NewTyp:=PotentialType[GetMenuSelect(Menu)];
         Exit:=False;
         END
      ELSE
         Exit:=True;

      CleanUpMenu(Menu);
      ClrDisplayScreen;
      END;  { GetDesignation }

   { DesignateCommand: MAIN PROCEDURE }
   BEGIN
   WorldN:=ObjectName(Player,World,LongFormat);
   GetCapital(Player,CapID);
   GetCoord(CapID,CapXY);
   Cls:=GetClass(World);

   REPEAT
      Ok:=True;
      ClrDisplayScreen;
      WriteString('Designate:',1,1,C.SYSDispHigh);
      WriteString(MyLord(Player)+', '+WorldN+' is currently '+TypeN[GetType(World)]+'.',2,3,C.SYSDispWind);
      WriteString('What shall its new designation be?',2,4,C.SYSDispWind);
      GetDesignation(World,CapID,Typ,Exit);

      Ans:='';
      IF NOT Exit THEN
         BEGIN
         WriteString('Designate:',1,1,C.SYSDispHigh);
         IF Typ=CapTyp THEN
            BEGIN
            WriteString(MyLord(Player)+', changing the capital will result in short-term loss of '
                        ,2,3,C.SYSDispWind);
            WriteString('efficiency and increased unrest among the people of the empire.',2,4,C.SYSDispWind);
            InputStrgDisplayScreen('Are you sure about this order (y/N) ? ',2,5,1,Ans);
            IF UpCase(Ans[1])<>'Y' THEN
               Ok:=False;
            END
         ELSE IF (World.ObjTyp=Base) AND (GetBaseType(World)=cmp)
                 AND (NOT (Typ IN [BseTyp,JmpTyp,StrTyp,TrnTyp,CapTyp,NnjTyp])) THEN
            BEGIN
            WriteString('But '+MyLord(Player)+', an industrial complex would be wasted on such a trivial',2,3,C.SYSDispWind);
            WriteString('designation.',2,4,C.SYSDispWind);
            InputStrgDisplayScreen('Are you sure that you want to continue (y/N) ? ',2,5,1,Ans);
            IF UpCase(Ans[1])<>'Y' THEN
               Ok:=False;
            END
         ELSE IF (Typ=RsrTyp) AND (GetTech(World)<GetTech(CapID)) THEN
            BEGIN
            WriteString('But '+MyLord(Player)+', '+WorldN+' is not yet as advanced as the capital.',2,3,C.SYSDispWind);
            WriteString('As a university world it wouldn''t be of much use.',2,4,C.SYSDispWind);
            InputStrgDisplayScreen('Are you sure that you want to continue (y/N) ? ',2,5,1,Ans);
            IF UpCase(Ans[1])<>'Y' THEN
               Ok:=False;
            END
         ELSE IF (Typ IN [MinTyp,RawTyp,TriTyp]) 
                 AND (Cls IN [GsGCls,IceCls,OcnCls,PsnCls]) THEN
            BEGIN
            WriteString(MyLord(Player)+', the environment of '+WorldN+' is not really suited to',2,3,C.SYSDispWind);
            WriteString('large scale mining operations.',2,4,C.SYSDispWind);
            InputStrgDisplayScreen('Are you sure about this command (y/N) ? ',2,5,1,Ans);
            IF UpCase(Ans[1])<>'Y' THEN
               Ok:=False;
            END
         ELSE IF (Typ=AgrTyp) 
                 AND (Cls IN [ArdCls,ArtCls,BarCls,DrtCls,IceCls,PsnCls,UndCls,VlcCls]) THEN
            BEGIN
            WriteString('I hope you will reconsider, '+MyLord(Player)+', '+WorldN+' would not be',2,3,C.SYSDispWind);
            WriteString('an ideal agricultural world.',2,4,C.SYSDispWind);
            InputStrgDisplayScreen('Are you sure about this command (y/N) ? ',2,5,1,Ans);
            IF UpCase(Ans[1])<>'Y' THEN
               Ok:=False;
            END;
         END;
   UNTIL Ok;

   IF NOT Exit THEN
      BEGIN
      IF Typ<>GetType(World) THEN
         DesignateWorld(World,Typ);
      Str(GetEfficiency(World):3,EffStr);

      ClrDisplayScreen;
      WriteString('Designate:',1,1,C.SYSDispHigh);

      WriteString(WorldN+' has been designated as '+TypeN[Typ]+'.',2,3,C.SYSDispWind);
      WriteString('All industries are being re-distributed.  New efficiency: '+EffStr+'%',2,4,C.SYSDispWind);

      GetCoord(World,XY);
      UMSector(XY.x,XY.y,Map^);
      UMSector(CapXY.x,CapXY.y,Map^);
      IF Typ=CapTyp THEN
        InitScanWindowMap(MAP^,Player);
      WriteCommLine('');
      WriteErrorMessage('');
      END
   ELSE
      BEGIN
      ClrDisplayScreen;
      WriteCommLine('');
      WriteErrorMessage('');
      ActivateLatestStatusWindow;
      END;
   END;  { DesignateCommand }

PROCEDURE TerraformCommand(WorldID: IDNumber);

   VAR
      Exit,Ok: Boolean;
      Ans: String[1];
      XY: XYCoord;
      WorldN: String32;
      EffStr: String8;
      Typ: WorldTypes;
      Cls: WorldClass;

   CONST
     ClassN: ARRAY [WorldClass] OF String16 =
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



   PROCEDURE GetNewClass(WorldID: IDNumber; VAR NewCls: WorldClass;
                         VAR Exit: Boolean);
      VAR
         Menu: MenuStructure;
         PotentialClass: ARRAY [1..10] OF WorldClass;
         NoOfClasses, I: Word;
         Ch: Char;
         ClassI: WorldClass;
         Line: LineStr;

      BEGIN
      InitializeMenu(Menu);
      NoOfClasses:=0;
      Cls:=GetClass(WorldID);

      FOR I:=1 TO 10 DO
         BEGIN
         ClassI:=TerraformPotentialClasses[Cls,I];
         IF (ClassI<>Cls) THEN
            BEGIN
              Inc(NoOfClasses);
              PotentialClass[NoOfClasses]:=ClassI;
              Line:=ClassN[ClassI];
              AdjustString(Line,30);
              AddMenuLine(Menu,Line);
            END;
         END;
     
      WriteString('Available Classes',15,9,C.SYSDispHigh);
      WriteHelpLine(',:Move cursor <Enter>:Select <Esc>:Exit');
      DisplayMenu(Menu,15,14,LightGray,C.SYSDispSelect,40,8);

      REPEAT
         GetCharacter(AnyKey,NoCaseDistinct,Ch);
         ActivateMenu(Menu,Ch);
      UNTIL Ch IN [ESCKey,ReturnKey];

      IF (Ch=ReturnKey) AND (NoOfClasses>0) THEN
         BEGIN
         NewCls:=PotentialClass[GetMenuSelect(Menu)];
         Exit:=False;
         END
      ELSE
         Exit:=True;

      CleanUpMenu(Menu);
      ClrDisplayScreen;
      END;  { GetNewClass }

   { TerraformCommand: MAIN PROCEDURE }
   BEGIN
   WorldN:=ObjectName(Player,WorldID,LongFormat);
   Cls:=GetClass(WorldID);

   REPEAT
      Ok:=True;
      ClrDisplayScreen;
      WriteString('Terraform:',1,1,C.SYSDispHigh);
      WriteString(MyLord(Player)+', '+WorldN+' is ready for your orders.',2,3,C.SYSDispWind);
      WriteString('Current class: '+ClassN[Cls],2,4,C.SYSDispWind);
      WriteString('To what class shall we terraform it?',2,6,C.SYSDispWind);
      GetNewClass(WorldID,Cls,Exit);

      Ans:='';
      IF NOT Exit THEN
         BEGIN
           WriteString('Terraform:',1,1,C.SYSDispHigh);
           WriteString(MyLord(Player)+', terraforming will not be complete for several',2,3,C.SYSDispWind);
           WriteString('years and industry will stop completely during the process.',2,4,C.SYSDispWind);
           WriteString('Additionally, our dramatic changes to the biosphere could backfire,',2,5,C.SYSDispWind);
           WriteString('leaving the planet a desolate wasteland.',2,6,C.SYSDispWind);
           WriteString('Billions of people will likely emigrate from the planet.',2,7,C.SYSDispWind);
           WriteString('I would ask you to consider the lives you will be displacing.',2,8,C.SYSDispWind);
           InputStrgDisplayScreen('Are you sure about this command (y/N) ? ',2,10,1,Ans);
           IF UpCase(Ans[1])<>'Y' THEN
             Ok:=False;
         END;
   UNTIL Ok;

   IF NOT Exit THEN
      BEGIN
      IF Cls<>GetClass(WorldID) THEN
        BEGIN
          TerraformWorld(WorldID,Cls);
        END;

      ClrDisplayScreen;
      WriteString('Terraform:',1,1,C.SYSDispHigh);

      WriteString('All industries on '+WorldN+' are being dismantled and the',2,3,C.SYSDispWind);
      WriteString('process of terraforming has begun.',2,4,C.SYSDispWind);
      WriteString('Target class: '+ClassN[Cls]+'.',2,6,C.SYSDispWind);

      GetCoord(WorldID,XY);
      UMSector(XY.x,XY.y,Map^);
      WriteCommLine('');
      WriteErrorMessage('');
      END
   ELSE
      BEGIN
      ClrDisplayScreen;
      WriteCommLine('');
      WriteErrorMessage('');
      ActivateLatestStatusWindow;
      END;
   END; { TerraformCommand }


PROCEDURE SendMessageCommand;

   VAR
      Emp: Empire;
      Line: LineStr;
      Ans: String8;
      Ch: Char;
      NoOfLines,NoOfEmpires: Word;
      Empires: EmpireSet;
      Abort: Boolean;
      Edit: EditStructure;

   PROCEDURE GetEmpires(Player: Empire; VAR Empires: EmpireSet; 
                        VAR NoOfEmpires: Word; VAR Abort: Boolean);
      VAR
         Emp: Empire;
         Menu: MenuStructure;
         PotentialEmpire: ARRAY [1..10] OF Empire;
         i: Word;
         Ch: Char;

      BEGIN
      InitializeMenu(Menu);
      NoOfEmpires:=0;
      FOR Emp:=Empire1 TO Empire8 DO
         IF (EmpireActive(Emp)) THEN
            BEGIN
            Inc(NoOfEmpires);
            PotentialEmpire[NoOfEmpires]:=Emp;
            AddMenuLine(Menu,EmpireName(Emp));
            END;

      IF NoOfEmpires>0 THEN
         BEGIN
         DisplayMenu(Menu,5,10,LightGray,C.SYSDispSelect,45,10);
         WriteHelpLine(',:Move cursor <Space>:Toggle empire <Enter>:End selection <Esc>:Exit');
         Empires:=[];
         REPEAT
            GetCharacter([PgUpKey,PgDnKey,UArrKey,DArrKey,ReturnKey,' ',ESCKey],NoCaseDistinct,Ch);
            CASE Ch OF
               ' ': BEGIN
                  i:=GetMenuSelect(Menu);
                  Emp:=PotentialEmpire[i];
                  IF Emp IN Empires THEN
                     BEGIN
                     Empires:=Empires-[Emp];
                     WriteString(' ',47,i,C.SYSDispHigh);
                     END
                  ELSE
                     BEGIN
                     Empires:=Empires+[Emp];
                     WriteString('',47,i,C.SYSDispHigh);
                     END;
                  END;
               ELSE
                  ActivateMenu(Menu,Ch);
            END;  { case }
         UNTIL Ch IN [ESCKey,ReturnKey];

         IF Ch=ReturnKey THEN
            BEGIN
            IF Empires=[] THEN
               BEGIN
               Empires:=[PotentialEmpire[GetMenuSelect(Menu)]];
               NoOfEmpires:=1;
               END;
            Abort:=False;
            END
         ELSE
            Abort:=True;

         CleanUpMenu(Menu);
         END;
      END;  { GetEmpires }

   { SendMessageCommand: MAIN PROCEDURE }
   BEGIN
   WriteCommLine('');
   WriteErrorMessage('');

   ClrDisplayScreen;
   WriteString('Send Message:',1,1,C.SYSDispHigh);
   WriteString('Which empire(s) do you wish to send a message to?',2,3,C.SYSDispWind);
   GetEmpires(Player,Empires,NoOfEmpires,Abort);
   IF NOT Abort THEN
      BEGIN
      ClrDisplayScreen;
      InitializeText(Edit.Txt);
      InitializeEdit(Edit,'Anacreon: Edit     <Esc>:End',1,4,80,21,C.SYSDispWind);
      REPEAT
         GetCharacter(AnyKey,CaseDistinct,Ch);
         EditText(Edit,Ch);
      UNTIL Ch=ESCKey;
      CleanUpEdit(Edit);

      ClrDisplayScreen;
      WriteString('Send Message:',1,1,C.SYSDispHigh);
      NoOfLines:=1;
      IF NoOfEmpires=1 THEN
         BEGIN
         FOR Emp:=Empire1 TO Empire8 DO
            IF Emp IN Empires THEN
               Line:=EmpireName(Emp);
         WriteString('Ready to send message to '+Line+'.',2,3,C.SYSDispWind);
         END
      ELSE
         BEGIN
         WriteString('Ready to send messages to:',2,3,C.SYSDispWind);
         FOR Emp:=Empire1 TO Empire8 DO
            IF Emp IN Empires THEN
               BEGIN
               WriteString(EmpireName(Emp),5,NoOfLines+3,C.SYSDispWind);
               Inc(NoOfLines);
               END;
         END;

      Ans:='';
      InputStrgDisplayScreen('Send message (Y/n) ? ',2,NoOfLines+3,1,Ans);
      IF UpCase(Ans[1])<>'N' THEN
         BEGIN
         SendMessage(Player,Empires,Edit.Txt);
         WriteCommLine('Message sent, '+MyLord(Player)+'.');
         END
      ELSE
         WriteCommLine('Message aborted, '+MyLord(Player)+'.');
      END;

   ClrDisplayScreen;
   ActivateLatestStatusWindow;
   END;  { SendMessageCommand }

PROCEDURE ReadMessageCommand;
   VAR
      Message: MessageArray;
      CurMess,NoOfMessages: Word;
      Ch: Char;

   PROCEDURE DisplayMessage(CurMes,NoOfMes: Word; Mes: MessageRecordPtr);
      VAR
         Strg1,Strg2: String16;

      BEGIN
      Str(CurMes,Strg1); { Current message number into Strg1 }
      Str(NoOfMes,Strg2); { Number of messages into Strg2 }
      IF NoOfMes>1 THEN
         Strg1:='('+Strg1+' of '+Strg2+') '
      ELSE
         Strg1:='';

      WITH Mes^ DO
         BEGIN
         IF Intercepted THEN
            WriteCommLine(Strg1+'Intercepted message from '+EmpireName(Sender)+'.')
         ELSE IF Sender<>Player THEN
            WriteCommLine(Strg1+'Message from '+EmpireName(Sender)+'.')
         ELSE
            WriteCommLine(Strg1+'Time capsule from the past.');
         ClrDisplayScreen;
         ViewText(MesText,1,19,C.SYSDispWind);
         END;
      END;  { DisplayMessage }

   BEGIN
   GetMessages(Player,NoOfMessages,Message);
   IF NoOfMessages=0 THEN
      BEGIN
      WriteCommLine('You have not received any messages this year, '+MyLord(Player)+'.');
      ActivateLatestStatusWindow;
      END
   ELSE
      BEGIN
      CurMess:=1;
      DisplayMessage(CurMess,NoOfMessages,Message[CurMess]);
      SetMessageRead(Player,Message[CurMess]);
      IF NoOfMessages>1 THEN
         BEGIN
         WriteHelpLine('<PgUp>:Next message <PgDn>:Previous message <Esc>:Exit');
         REPEAT
            GetCharacter([PgUpKey,PgDnKey,ESCKey],NoCaseDistinct,Ch);
            CASE Ch OF
               PgUpKey: BEGIN
                  IF (CurMess>1) THEN
                     BEGIN
                     Dec(CurMess);
                     DisplayMessage(CurMess,NoOfMessages,Message[CurMess]);
                     END;
                  END;
               PgDnKey: BEGIN
                  IF (CurMess<NoOfMessages) THEN
                     BEGIN
                     Inc(CurMess);
                     DisplayMessage(CurMess,NoOfMessages,Message[CurMess]);
                     SetMessageRead(Player,Message[CurMess]);
                     END;
                  END;
            END;  { case }
         UNTIL Ch=EscKey;
         END;
      END;
   END;  { ReadMessageCommand }

END.
