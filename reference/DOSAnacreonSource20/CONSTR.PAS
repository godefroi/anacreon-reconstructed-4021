(* CONSTR.PAS ------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT Constr;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Crt,                                       { Standard CRT Library. }
     Strg,                                      { String Library. }
     Int,                                       { Integer Library. }
     EIO,                                       { Extended IO Library. }
     Menu,
     Types,
     Galaxy,
     DataStrc,
     DataCnst,
     Misc,                                      { Miscellaneous procedures. }
     PrimIntr,                                  { Primitives. }
     Intrface,                                  { Interface. }
     SWindows,                                  { Status windows. }
     MapWind,
     Display,                                   { Display procedures. }
     WND;

PROCEDURE AbortConstructionCommand(ConID: IDNumber);
PROCEDURE ConstructCommand(XY:XYCoord);
PROCEDURE ConstrStatusCommand;
PROCEDURE WarpLinkFrequencyCommand;

IMPLEMENTATION

USES
   Environ;

CONST
   ConsName: ARRAY [ConstrTypes] OF STRING [24] =
      ( { SRM }               'SRM field',
        { cmm }               'command base',
        { frt }               'fortress',
        { cmp }               'industrial complex',
        { out }               'outpost',
        { gte }               'stargate',
        { lnk }               'warp link',
        { dis }               'jumpspace disrupter' );

PROCEDURE AbortConstructionCommand(ConID: IDNumber);
   VAR
      Ans: String8;
      ConName: String32;
      ConXY: XYCoord;

   BEGIN
   WriteErrorMessage('');
   WriteCommLine('');
   ClrDisplayScreen;
   WriteString('Abort Construction:',1,1,C.SYSDispHigh);
   ConName:=ObjectName(Player,ConID,LongFormat);
   Ans:='';
   InputStrgDisplayScreen('Are you sure you want to abort '+ConName+' (y/N) ? ',2,3,1,Ans);
   IF UpCase(Ans[1])='Y' THEN
      BEGIN
      ConName[1]:=UpCase(ConName[1]);
      WriteCommLine(ConName+' aborted, '+MyLord(Player)+'.');
      GetCoord(ConID,ConXY);
      DestroyConstruction(ConID);
      UMSector(ConXY.x,ConXY.y,Map^);
      END;

   ClrDisplayScreen;
   END;  { AbortConstructionCommand }

PROCEDURE InputConstrType(Player: Empire; VAR ConsType: ConstrTypes; VAR Abort: Boolean);
   VAR
      Tech: TechLevel;
      Technology: TechnologySet;
      Menu: MenuStructure;
      ConI: ConstrTypes;
      PotentialConstr: ARRAY [1..10] OF ConstrTypes;
      NoOfItems: Word;
      Ch: Char;
      Line: String32;

   BEGIN
   GetEmpireTechnology(Player,Tech,Technology);
   InitializeMenu(Menu);
   NoOfItems:=0;
   FOR ConI:=SRM TO dis DO
      IF ConI IN Technology THEN
         BEGIN
         Inc(NoOfItems);
         PotentialConstr[NoOfItems]:=ConI;
         Line:=ConsName[ConI];
         Line[1]:=UpCase(Line[1]);
         AddMenuLine(Menu,Line);
         END;

   IF NoOfItems>0 THEN
      BEGIN
      ClrDisplayScreen;
      WriteString('Construction:',1,1,C.SYSDispHigh);
      WriteString('What kind of construction do you wish to start?',1,4,C.SYSDispWind);
      DisplayMenu(Menu,5,12,LightGray,C.SYSDispSelect,45,9);
      WriteHelpLine(',:Move cursor <Enter>:Select <Esc>:Exit');

      REPEAT
         GetCharacter(AnyKey,NoCaseDistinct,Ch);
         ActivateMenu(Menu,Ch);
      UNTIL Ch IN [ESCKey,ReturnKey];

      IF Ch=ReturnKey THEN
         BEGIN
         ConsType:=PotentialConstr[GetMenuSelect(Menu)];
         Abort:=False;
         END
      ELSE
         Abort:=True;

      CleanUpMenu(Menu);
      ClrDisplayScreen;
      END
   ELSE
      BEGIN
      WriteCommLine(MyLord(Player)+', you don''t have the technology to build anything!');
      Abort:=True;
      END;
   END;  { InputConstrType }

PROCEDURE WarpLinkFreqList(Player: Empire; VAR ID: IDNumber);
	VAR
		Menu: MenuStructure;
		PotentialID: IDList;
		ListSize: Word;
		Exit: Boolean;
		OldModalInput: Boolean;

   procedure CreateMenu(VAR Menu: MenuStructure;
                        VAR PotentialID: IDList;
                        VAR ListSize: Word);
      VAR
         Obj: IDNumber;
         Emp: Empire;
         i: Word;

      PROCEDURE AddID(ID: IDNumber;
                      Emp: Empire;
                      VAR Menu: MenuStructure;
                      VAR PotentialID: IDList;
                      VAR ListSize: Word);

         VAR
            Line: LineStr;

         BEGIN
	 Inc(ListSize);
         PotentialID[ListSize]:=ID;

         Line:=ObjectName(Player,ID,LongFormat)+' ('+EmpireName(Emp)+')';
         AdjustString(Line,32);
         Line:=Line+'  '+Int2Str(GetWarpLinkFreq(Player,ID));
         AddMenuLine(Menu, Line);
         END;  { AddID }

      BEGIN
      InitializeMenu(Menu);
      ListSize:=0;

      { add object }
      Obj.ObjTyp:=Gate;   
      FOR i:=1 TO MaxNoOfStargates DO      
       BEGIN
       Obj.Index:=i;
       IF Known (Player, Obj) THEN
         BEGIN
         Emp:=GetStatus(Obj);
  	      AddID (Obj, Emp, Menu, PotentialID, ListSize);
         END;
       END;

      END;  { CreateMenu }

	BEGIN
	CreateMenu (Menu, PotentialID, ListSize);
	IF (ListSize > 0) THEN
		BEGIN
		OldModalInput:=ModalInput;
		ModalInput:=True;
		GetIDMenuChoice (Menu, PotentialID, ID, Exit);
		ModalInput:=OldModalInput;
		IF Exit THEN
  		  ID:=EmptyQuadrant;
		END
	ELSE
		BEGIN		
		  ID:=EmptyQuadrant;
		  WriteCommLine('There are no known stargates in the galaxy, '+MyLord(Player));
		END
	END;  { WarpLinkFreqList }

PROCEDURE WarpLinkFrequencyCommand;
 
  VAR 
    ID: IDNumber;
    Str,Response: String32;
    NewFreq: Integer;
    Loc: Location;
    Window: Byte;
    Error: Integer;
    Abort,OK: Boolean;

  BEGIN
    WarpLinkFreqList(Player, ID);
    IF NOT SameID(ID,EmptyQuadrant) THEN
      BEGIN 
        WriteCommLine('');
        WriteErrorMessage('');
        ClrDisplayScreen;
        Loc.ID:=ID;
        GetName(Player,Loc,Longformat,Str);
        WriteLn('');
        WriteLn(' Our current frequency for '+Str+' is '+Int2Str(GetWarpLinkFreq(Player,ID)));
        REPEAT 
          InputString('Please enter the new frequency : ',2,3,C.SYSDispWind,4,Response);
          Writeln('');
          Val(Response,NewFreq,Error); 
          OK:=True;
          IF ((Error <> 0) OR (NewFreq < 0) OR (NewFreq > 9999)) AND NOT (Response=EscKey) THEN
            BEGIN
            Writeln('Frequency must be between 0 and 9999.');
            OK:=False;
            END
          UNTIL (OK=True);
        IF NOT (Response=EscKey) THEN
          SetWarpLinkFreq(Player,ID,NewFreq);
        WriteLn('');
        WriteLn(' Our warp link frequency for '+Str+' has been set to '+Int2Str(GetWarpLinkFreq(Player,ID)));
        IF (GetStatus(ID)=Player) THEN
          BEGIN
   	    WriteLn(' Please be careful how you divulge this frequency, as other empires');
	    WriteLn(' who know or guess it will have access to our device.');
          END
        ELSE
          BEGIN
            WriteLn(' If this is not the same frequency that '+EmpireName(GetStatus(ID))+' has');
            WriteLn(' configured, we will still not be able to use this device.');
          END         
      END
  END;

PROCEDURE GetConstrXY(Player: Empire; VAR XY: XYCoord; VAR Abort: Boolean);
   VAR
      Error: Boolean;
      Parm: String32;
      ObjID: IDNumber;

   BEGIN
   REPEAT
      Error:=False;
      InputParameter('Where do you wish to begin the construction? ',Parm);
      IF Parm<>'' THEN
         BEGIN
         InterpretXY(Parm,XY,Error);
         IF NOT Error THEN
            BEGIN
            GetObject(XY,ObjID);
            IF NOT SameID(ObjID,EmptyQuadrant) THEN
               BEGIN
               WriteErrorMessage(MyLord(Player)+', that sector is already occupied.');
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
   END;  { GetConstrXY }

PROCEDURE ConstructCommand(XY: XYCoord);
{ ConstructCommand:
   This command takes a type of construction and an set of XY coordinates as 
   input.  If the construction type is one that is within the capabilities of 
   the empire, then the procedure will call Construction to start a 
   construction site.  If the technology is not yet available, then it will 
   return a message.

   Globals:
      Player, ConsName, YearsToBuild, ConsCargoNeeded, ThingNames.

   Interface:
      GetEmpireTechnology, Construction, GetCoordName.

   I/O: 
      ClrDisplayScreen, WriteDisplayScreen.                                 }


   VAR
      TimeN,IntN,CoordN: String16;
      ThgI: CargoTypes;
      LineNo,Time: Integer;
      Tech: TechLevel;
      Technology: TechnologySet;
      Abort,Error: Boolean;
      ConsType: ConstrTypes;
      Loc: Location;
      ConNamePtr: NameRecordPtr;
      ConName: String16;
      ConID: IDNumber;

   { ConstructCommand: MAIN PROCEDURE }
   BEGIN
   InputConstrType(Player,ConsType,Abort);
   IF NOT Abort THEN
      BEGIN
      ClrDisplayScreen;
      WriteString('Construction:',1,1,C.SYSDispHigh);

      Construction(Player,ConsType,XY,ConID);
      GetCoordName(XY,CoordN);
      Time:=YearsToBuild[ConsType];
      Str(Time,TimeN);

      WriteString('Starting construction of '+Noun(ConsName[ConsType])+' at '+CoordN+'.',2,3,C.SYSDispWind);

      WriteString('Construction will take approximately '+TimeN+' years to finish and will',2,5,C.SYSDispWind);
      WriteString('require the following quantities of raw material:',2,6,C.SYSDispWind);
      LineNo:=8;
      FOR ThgI:=che TO tri DO
         IF ThgI<>sup THEN
            BEGIN
            Str(ConsCargoNeeded[ConsType,ThgI],IntN);
            WriteString(IntN+' '+ThingNames[ThgI]+' per year.',5,LineNo,C.SYSDispWind);
            Inc(LineNo);
            END;

      Loc.XY:=XY;  Loc.ID:=EmptyQuadrant;
      Location2Index(Player,Loc,ConNamePtr);
      IF ConNamePtr<>Nil THEN
         BEGIN
         GetDefinedName(Player,ConNamePtr,ConName,Loc);
         DeleteName(Player,ConName);
         Loc.XY:=Limbo;  Loc.ID:=ConID;
         AddName(Player,Loc,ConName,Error);
         END;

         UMSector(XY.x,XY.y,Map^);
         END;
   END;  { ConstructCommand }

PROCEDURE ConstrStatusCommand;
   VAR
      i,j: Word;
      CarI: CargoTypes;
      CargoNeeded: Integer;
      FltID,ConID: IDNumber;
      Line: LineStr;
      Temp,ConName: String32;
      NoOfLines: Word;
      TimeLeft: Word;
      FltCargo,CargoAvail: CargoArray;
      CargoFleets: FleetSet;
      ConXY: XYCoord;

   BEGIN
   ClrDisplayScreen;
   WriteString('Construction Status:',1,1,C.SYSDispHigh);

   NoOfLines:=0;
   ConID.ObjTyp:=Con;
   FOR i:=1 TO MaxNoOfConstrSites DO
      IF i IN SetOfConstructionSitesOf[Player] THEN
         BEGIN
         ConID.Index:=i;
         ConName:=ObjectName(Player,ConID,ShortFormat);
         AdjustString(ConName,10);
         Line:=ConName+ConsName[GetConstrType(ConID)];
         AdjustString(Line,32);
         TimeLeft:=GetConstrTimeLeft(ConID);
         Str(Year+TimeLeft,Temp);
         Line:=Line+Temp;
         AdjustString(Line,47);

         FltID.ObjTyp:=Flt;
         FillChar(CargoAvail,SizeOf(CargoAvail),0);
         GetCoord(ConID,ConXY);
         GetFleets(ConXY,CargoFleets);
         CargoFleets:=CargoFleets * SetOfFleetsOf[Player];
         FOR j:=1 TO MaxNoOfFleets DO
            IF j IN CargoFleets THEN
               BEGIN
               FltID.Index:=j;
               GetCargo(FltID,FltCargo);
               FOR CarI:=che TO tri DO                  
                  CargoAvail[CarI]:=ThgLmt(CargoAvail[CarI]+FltCargo[CarI]);
               END;
         FOR CarI:=che TO tri DO
            IF CarI<>sup THEN
               BEGIN
               CargoNeeded:=ConsCargoNeeded[GetConstrType(ConID)][CarI]-CargoAvail[CarI];
               Str(GreaterInt(0,CargoNeeded):5,Temp);
               Line:=Line+Temp;
               END;

         WriteString(Line,2,5+NoOfLines,C.SYSDispWind);
         Inc(NoOfLines);
         END;

   IF NoOfLines=0 THEN
      WriteString(MyLord(Player)+', there are no active construction sites.',2,3,C.SYSDispWind)
   ELSE
      BEGIN
      WriteString(                                'Date of         Materials Needed',34,3,C.SYSDispHigh);
      WriteString('Site      Type                Completion         che  met  tri',2,4,C.SYSDispHigh);
      END;
   END;  { ConstrStatusCommand }

END.  { Unit }
