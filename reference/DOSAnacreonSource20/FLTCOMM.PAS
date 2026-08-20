(* FLTCOMM.PAS -----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT FltComm;

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
     TextStrc,
     Edit,
     Types,
     Galaxy,
     DataStrc,                                  { Universe data structure. }
     DataCnst,
     Misc,                                      { Miscellaneous procedures. }
     PrimIntr,                                  { Primitives. }
     Intrface,                                  { Interface. }
     News,
     Fleet,
     Orders,
     SWindows,                                  { Status windows. }
     MapWind,
     Display;                                   { Display procedures. }

PROCEDURE LaunchFleetCommand(FleetName: String16;
                             LaunchPt: IDNumber; Destination: XYCoord);
PROCEDURE AbortFleetCommand(FltID: IDNumber);
PROCEDURE ChangeDestinationCommand(FltID: IDNumber; NewDestination: XYCoord);
PROCEDURE TransferFleetCommand(FltID: IDNumber);
PROCEDURE RefuelFleetCommand(FltID: IDNumber);
PROCEDURE LaunchProbeCommand(Coord: XYCoord);
PROCEDURE MineSweeperCommand(FltID: IDNumber);
PROCEDURE FleetOrdersCommand(FltID: IDNumber);
PROCEDURE FleetCancelOrdersCommand(FltID: IDNumber);

IMPLEMENTATION

USES
   Environ;

PROCEDURE GetGround(FltID: IDNumber; VAR Ground: IDNumber;
                    PlayerOnly,IncludeFleet: Boolean;
                    ComName: LineStr;
                    PromptLine: LineStr;
                    VAR Exit: Boolean);
{ I/O PROCEDURE: GetGround: }

   VAR
      Menu: MenuStructure;
      PotentialGround: IDList;
      ListSize: Byte;
      XY: XYCoord;

   procedure CreateMenu(FltID: IDNumber; XY: XYCoord;
                        VAR Menu: MenuStructure;
                        VAR PotentialGround: IDList;
                        VAR ListSize: Byte);
      var
         Fleets: FleetSet;
         Flt2,Obj2: IDNumber;
         Emp: Empire;
         i: Byte;

      PROCEDURE AddGround(Ground: IDNumber;
                          Emp: Empire;
                          VAR Menu: MenuStructure;
                          VAR PotentialGround: IDList;
                          VAR ListSize: Byte);

         VAR
            Line: LineStr;

         BEGIN
         ListSize:=ListSize+1;
         PotentialGround[ListSize]:=Ground;

         Line:=ObjectName(Player,Ground,LongFormat);
         Line:=Line+'  ('+EmpireName(Emp)+')';
         AddMenuLine(Menu,Line);
         END;  { AddGround }

      BEGIN
      InitializeMenu(Menu);
      ListSize:=0;

      Flt2.ObjTyp:=Flt;
      GetFleets(XY,Fleets);

      IF NOT IncludeFleet THEN
         Fleets:=Fleets-[FltID.Index];       { make sure given fleet is }
                                             { not chosen. }
      { add fleets to menu }
      FOR i:=1 TO MaxNoOfFleets DO
         IF i IN Fleets THEN
            BEGIN
            Flt2.Index:=i;
            Emp:=GetStatus(Flt2);
            IF ((NOT PlayerOnly) OR (Emp=Player))
               AND (Scouted(Player,Flt2)) THEN
               AddGround(Flt2,Emp,Menu,PotentialGround,ListSize);
            END;

      { add object }
      GetObject(XY,Obj2);
      IF (Obj2.ObjTyp IN [Pln,Base]) THEN
         BEGIN
         Emp:=GetStatus(Obj2);
         IF (Emp=Player) OR (NOT PlayerOnly) THEN
            AddGround(Obj2,Emp,Menu,PotentialGround,ListSize);
         END;
      end;  { CreateMenu }

   { GetGround: MAIN PROCEDURE }
   BEGIN
   ClrDisplayScreen;
   WriteString(ComName,1,1,C.SYSDispHigh);
   GetCoord(FltID,XY); 
   CreateMenu(FltID,XY,Menu,PotentialGround,ListSize);
   WriteString(PromptLine,2,3,C.SYSDispWind);
   GetIDMenuChoice(Menu,PotentialGround,Ground,Exit);
   ClrDisplayScreen;
   END;  { GetGround }

PROCEDURE InputNewDistribution(GrndID: IDNumber;
                               GroundStatus: Empire;
                               VAR GrnSh: ShipArray;
                               VAR GrnCr: CargoArray;
                               VAR FltSh: ShipArray;
                               VAR FltCr: CargoArray);
{ I/O PROCEDURE: InputNewDistribution: }

   CONST
      DataX = 1;
      DataY = 5;

   VAR
      Comm: Char;
      EverythingOk: Boolean;
      ThgI,Point: ResourceTypes;
      DataOffset: Integer;
      temp: String32;

      ResTrans: ARRAY [fgt..tri] OF Integer;
      Loc: Location;

   PROCEDURE WriteError(Line: String64);
      BEGIN
      WriteBlanks(50,1,16,C.SYSDispWind);
      WriteString(Line,1,16,C.SYSDispWind);
      END;  { WriteError }

   PROCEDURE UpdateDisplay;
      VAR
         temp: String16;
         Line1,Line2: LineStr;
         ThgI: ResourceTypes;
         FltCargoSpace,GrnCargoSpace: Integer;

      BEGIN
      Line1:='';
      Line2:='';

      FOR ThgI:=fgt TO trn DO
         BEGIN
         Str(FltSh[ThgI]:5,temp);
         Line1:=Line1+temp;

         IF GroundStatus=Player THEN
            Str(GrnSh[ThgI]:5,temp)
         ELSE
            temp:=' ????';
         Line2:=Line2+temp;
         END;
      FOR ThgI:=men TO tri DO
         BEGIN
         Str(FltCr[ThgI]:5,temp);
         Line1:=Line1+temp;

         IF GroundStatus=Player THEN
            Str(GrnCr[ThgI]:5,temp)
         ELSE
            temp:=' ????';
         Line2:=Line2+temp;
         END;

      FltCargoSpace:=FleetCargoSpace(FltSh,FltCr);
      IF (GrndID.ObjTyp=Flt) AND (GroundStatus=Player) THEN
         GrnCargoSpace:=FleetCargoSpace(GrnSh,GrnCr)
      ELSE
         GrnCargoSpace:=0;

      Str(FltCargoSpace:4,temp);
      Line1:=Line1+' '+temp+' ';
      Str(GrnCargoSpace:4,temp);
      Line2:=Line2+' '+temp+' ';

      WriteString(Line1,DataX,DataY+1,C.SYSDispWind);
      WriteString(Line2,DataX,DataY+2,C.SYSDispWind);
      END;  { UpdateDisplay }

   PROCEDURE EraseOldPointer;
   { EraseOldPointer: WARNING! MACHINE SPECIFIC!
      This procedure will un-highlight the field that Point currently holds.  
      E.g. if Point=trn, then the trn columns are un-highlighted. }

      VAR
         StartAddr,x,y: Integer;

      BEGIN
      StartAddr:=DataOffset+(Ord(Point)-Ord(fgt))*10;
      FOR y:=0 TO 2 DO
         FOR x:=0 TO 4 DO
            Mem[ScrSeg:StartAddr+y*160+x*2+1]:=C.SYSDispWind;

      WriteError('');
      END;  { EraseOldPointer }

   PROCEDURE UpdatePointer;
   { UpdatePointer: WARNING! MACHINE SPECIFIC!
      This procedure will highlight the field that Point currently holds.  
      See EraseOldPointer. }

      VAR
         StartAddr,x,y: Integer;

      BEGIN
      StartAddr:=DataOffset+(Ord(Point)-Ord(fgt))*10;
      FOR y:=0 TO 2 DO
         for x:=0 to 4 do
            Mem[ScrSeg:StartAddr+y*160+x*2+1]:=C.SYSDispSelect;
      END;  { UpdatePointer }

   PROCEDURE PointerLeft;
      BEGIN
      EraseOldPointer;
      IF Point>fgt THEN
         Point:=Pred(Point)
      ELSE
         Point:=tri;
      UpdatePointer;
      END;  { PointerLeft }

   PROCEDURE PointerRight;
      BEGIN
      EraseOldPointer;
      IF Point<tri THEN
         Point:=Succ(Point)
      ELSE
         Point:=fgt;
      UpdatePointer;
      END;  { PointerRight }

   function GetCurrentValue(ThgI: ResourceTypes; Sh: ShipArray;
                            Cr: CargoArray): Resources;
      begin
      case ThgI of
         fgt..trn : GetCurrentValue:=Sh[ThgI];
         men..tri : GetCurrentValue:=Cr[ThgI];
      end;
      end;  { GetCurrentValue }

   PROCEDURE SetCurrentValue(ThgI: ResourceTypes; var Sh: ShipArray;
                             var Cr: CargoArray; Value: Resources);
      begin
      case ThgI of
         fgt..trn : Sh[ThgI]:=Value;
         men..tri : Cr[ThgI]:=Value;
      end;  { case }
      end;  { SetCurrentValue }

   PROCEDURE GetChange(Ch: Char);
      var
         Trns,Error: Integer;
         Line: LineStr;
         Ok: Boolean;
         AmountOnGrn,AmountInFlt: Integer;

      { GetChange: MAIN PROCEDURE }
      BEGIN
      IF Ch=ReturnKey THEN
         Line:=''
      ELSE
         Line:=Ch;

      REPEAT
         AmountOnGrn:=GetCurrentValue(Point,GrnSh,GrnCr);
         AmountInFlt:=GetCurrentValue(Point,FltSh,FltCr);
         Error:=0;

         InputStrgDisplayScreen('How many '+ThingNames[Point]+' to transfer : ',1,14,5,Line);
         WriteError('');  { Clear error message }
         IF (Line=EscKey) OR (Line='') THEN
            Trns:=0
         ELSE
            Val(Line,Trns,Error);

         Ok:=True;
         IF (Error<>0) OR (Abs(Trns)>9999) THEN
            BEGIN
            WriteError('Please, '+MyLord(Player)+', numbers from -9999 to 9999.');
            Ok:=False;
            END
         ELSE IF Trns<=0 THEN
            BEGIN
            IF Abs(Trns)>AmountInFlt THEN
               BEGIN
               WriteError('Not enough '+ThingNames[Point]+' in fleet.');
               Ok:=False;
               END;
            END
         ELSE
            BEGIN
            IF GroundStatus<>Player THEN
               BEGIN
               WriteError('This is not '+EmpireName(Player)+' territory.');
               Ok:=False;
               END
            ELSE IF Trns>AmountOnGrn THEN
               BEGIN
               WriteError('Not enough '+ThingNames[Point]+' on ground.');
               Ok:=False;
               END;
            END;  { if }
         Line:='';
      UNTIL Ok;

      SetCurrentValue(Point,FltSh,FltCr,ThgLmt(AmountInFlt+Trns));
      SetCurrentValue(Point,GrnSh,GrnCr,ThgLmt(AmountOnGrn-Trns));
      ResTrans[Point]:=ResTrans[Point]+Trns;

      WriteBlanks(51,1,14,C.SYSDispWind);
      UpdateDisplay;
      UpdatePointer;
      END;  { GetChange }

   PROCEDURE FillFleet;
      VAR
         CargoSpaceAvail: LongInt;
         AmountToMove,AmountOnGrn,AmountInFlt: Resources;

      BEGIN
      CargoSpaceAvail:=FleetCargoSpace(FltSh,FltCr);
      AmountToMove:=0;
      AmountOnGrn:=GetCurrentValue(Point,GrnSh,GrnCr);
      AmountInFlt:=GetCurrentValue(Point,FltSh,FltCr);

      { The amount of resources moved to the fleet vary depending on the type 
        of resource. For non-transport ships, all resources are transferred.
        For transport ships, if there are enough transports to carry all 
        current resources, then all ships are transferred, else, only enough 
        are transferred to carry the resources. For cargo, if transports are 
        full, all resources are transferred, else, only enough to fill the 
        transports. }

     IF GroundStatus<>Player THEN 
         WriteError('This is not '+EmpireName(Player)+' territory.')
      ELSE IF (Point IN [fgt..jmp,pen,ssp]) THEN
         AmountToMove:=AmountOnGrn
      ELSE IF (Point IN [jtn,trn]) THEN
         BEGIN
         IF CargoSpaceAvail>=0 THEN
            AmountToMove:=AmountOnGrn
         ELSE
            AmountToMove:=LesserInt(AmountOnGrn,Round(-CargoSpaceAvail/TrnAdj[Point]));
         END
      ELSE IF (CargoSpaceAvail>0) THEN
         AmountToMove:=LesserInt(CargoSpaceAvail*CargoSpace[Point],AmountOnGrn)
      ELSE
         AmountToMove:=AmountOnGrn;

      IF AmountInFlt+AmountToMove>9999 THEN
         AmountToMove:=9999-AmountInFlt;

      SetCurrentValue(Point,FltSh,FltCr,ThgLmt(AmountInFlt+AmountToMove));
      SetCurrentValue(Point,GrnSh,GrnCr,ThgLmt(AmountOnGrn-AmountToMove));
      ResTrans[Point]:=ResTrans[Point]+AmountToMove;

      UpdateDisplay;
      UpdatePointer;
      END;  { FillFleet }

   PROCEDURE EmptyFleet;
      VAR
         CargoSpaceAvail: LongInt;
         AmountToMove,AmountOnGrn,AmountInFlt: Resources;

      BEGIN
      CargoSpaceAvail:=FleetCargoSpace(GrnSh,GrnCr);
      AmountOnGrn:=GetCurrentValue(Point,GrnSh,GrnCr);
      AmountInFlt:=GetCurrentValue(Point,FltSh,FltCr);
      AmountToMove:=0;

      IF (Point IN [fgt..trn]) OR (GrndID.ObjTyp<>Flt) THEN
         AmountToMove:=AmountInFlt
      ELSE IF (CargoSpaceAvail>0) THEN
         AmountToMove:=LesserInt(CargoSpaceAvail*CargoSpace[Point],AmountInFlt);

      IF AmountOnGrn+AmountToMove>9999 THEN
         AmountToMove:=9999-AmountOnGrn;

      SetCurrentValue(Point,FltSh,FltCr,ThgLmt(AmountInFlt-AmountToMove));
      SetCurrentValue(Point,GrnSh,GrnCr,ThgLmt(AmountOnGrn+AmountToMove));
      ResTrans[Point]:=ResTrans[Point]+AmountToMove;

      UpdateDisplay;
      UpdatePointer;
      END;  { EmptyFleet }

   { InputNewDistribution: MAIN PROCEDURE }
   BEGIN
   WriteString('  fgt   hk  jmp  jtn  pen  str  trn  men ninj  amb  che  met  sup  tri  Cargo',DataX,DataY,C.SYSDispWind);
   DataOffset:=((Hi(WindMin)+(DataY-1))*160)+((Lo(WindMin)+(DataX)+1)*2)-2;

   WriteString('<RETURN> to select ship/cargo to change.',1,9,C.SYSDispWind);
   WriteString('<Esc> to end transfer.',1,10,C.SYSDispWind);
   WriteString('   +### to transfer from ground to fleet.',1,11,C.SYSDispWind);
   WriteString('   -### to transfer from fleet to ground.',1,12,C.SYSDispWind);

   WriteString('Transport Capacity:',52,9,C.SYSDispWind);
   FOR ThgI:=men TO tri DO
      BEGIN
      Str(CargoSpace[ThgI]:3,temp);
      temp:=temp+' '+ThingNames[ThgI];
      WriteString(temp,52,10+Ord(ThgI)-Ord(Men),C.SYSDispWind);
      END;
   FillChar(ResTrans,SizeOf(ResTrans),0);

   Point:=fgt;
   UpdateDisplay;
   UpdatePointer;
   REPEAT
      EverythingOk:=True;
      REPEAT
         GetCharacter(AnyKey,NoCaseDistinct,Comm);
         CASE Comm OF
            LArrKey : PointerLeft;
            RArrKey : PointerRight;
            UArrKey : FillFleet;
            DArrKey : EmptyFleet;
            '0'..'9',
            '+','-',
            ReturnKey : GetChange(Comm);           
         END;  { case }
      UNTIL (Comm='X') OR (Comm=ESCKey);

      IF FleetCargoSpace(FltSh,FltCr)<0 THEN
         BEGIN
         EverythingOk:=False;
         WriteError('There aren''t enough transports in the fleet.');
         END
      ELSE IF (GrndID.ObjTyp=Flt) AND (FleetCargoSpace(GrnSh,GrnCr)<0) THEN
         BEGIN
         IF (GroundStatus<>Player) THEN
            BalanceFleet(GrnSh,GrnCr)
         ELSE
            BEGIN
            EverythingOk:=False;
            WriteError('There aren''t enough transports left in the fleet.');
            END;
         END;

   UNTIL EverythingOk;

   IF GroundStatus<>Player THEN
      { Send message if player left stuff. }
      BEGIN
      ThgI:=fgt;
      WHILE (ThgI<>SRM) AND (ResTrans[ThgI]>=0) DO
         Inc(ThgI);

      IF ThgI<>SRM THEN
         BEGIN
         Loc.ID:=GrndID;  Loc.XY:=Limbo;
         AddNews(GroundStatus,TrnsShp,Loc,Ord(Player),0,0);
         FOR ThgI:=fgt TO tri DO
            AddNews(GroundStatus,Trns2,Loc,Ord(-ResTrans[ThgI]),Ord(ThgI),0);
         END;
      END;
   END;  { InputNewDistribution }

{ MAIN ROUTINES }
PROCEDURE LaunchFleetCommand(FleetName: String16;
                             LaunchPt: IDNumber; Destination: XYCoord);
   VAR
      FltSh,GrnSh: ShipArray;
      FltCr,GrnCr: CargoArray;
      GroundStatus: Empire;
      GrnName: String32;
      LaunchXY: XYCoord;
      Loc: Location;
      FltID: IDNumber;
      NamePtr: NameRecordPtr;
		Error: Boolean;

   { LaunchFleetCommand: MAIN PROCEDURE }
   BEGIN
   WriteCommLine('');
   WriteErrorMessage('');

   FillChar(FltSh,SizeOf(FltSh),0);
   FillChar(FltCr,SizeOf(FltCr),0);

   GetShips(LaunchPt,GrnSh);
   GetCargo(LaunchPt,GrnCr);
   GroundStatus:=GetStatus(LaunchPt);

   FleetName[1]:=UpCase(FleetName[1]);
   GrnName:=ObjectName(Player,LaunchPt,LongFormat);

   ClrDisplayScreen;
   WriteString(FleetName+' ready to be deployed from '+GrnName+'.',1,1,C.SYSDispWind);
   InputNewDistribution(LaunchPt,GroundStatus,GrnSh,GrnCr,FltSh,FltCr);

   IF NOT NoShips(FltSh) THEN
      BEGIN
      DeployFleet(Player,LaunchPt,FltSh,FltCr,Destination,FltID);
      IF NOT SameID(FltID,EmptyQuadrant) THEN
         BEGIN
         GetCoord(LaunchPt,LaunchXY);
         UMFleets(LaunchXY.x,LaunchXY.y,Map^);

         Loc.ID:=FltID;  Loc.XY:=Limbo;
			AddName(Player,Loc,FleetName,Error);

         WriteCommLine(FleetName+' deployed, '+MyLord(Player)+'.');
         END
      ELSE
         WriteCommLine('(ERROR: Discrepancy in SetOfActiveFleets.)');

      WriteErrorMessage('');
      END;

   ClrDisplayScreen;
   ActivateLatestStatusWindow;
   END;  { LaunchFleetCommand }

PROCEDURE AbortFleetCommand(FltID: IDNumber);
   VAR
      Ground: IDNumber;
      GrnName,FltName: String32;
      GroundXY: XYCoord;
      Exit,Ok: Boolean;
      ShI: ResourceTypes;
      Ans: String8;
      GrnShp,FltShp: ShipArray;

   { AbortFleetCommand: MAIN PROCEDURE }
   BEGIN
   WriteCommLine('');
   WriteErrorMessage('');

   FltName:=ObjectName(Player,FltID,ShortFormat);
   GetGround(FltID,Ground,False,False,'Abort Fleet:',
             'What do you wish to join the fleet with, '+MyLord(Player)+'?',Exit);  { get non-empire ground }

   IF NOT Exit THEN
      BEGIN
      GrnName:=ObjectName(Player,Ground,LongFormat);
      Ok:=True;

      IF GetStatus(Ground)<>Player THEN
         BEGIN
         WriteString('Abort Fleet:',1,1,C.SYSDispHigh);
         WriteString(MyLord(Player)+', '+GrnName+' is not part of '+EmpireName(Player)+'.',2,3,C.SYSDispWind);
         InputStrgDisplayScreen('Are you sure you want to abort the fleet (y/N) ? ',2,4,1,Ans);
         IF UpCase(Ans[1])<>'Y' THEN
            Ok:=False;
         ClrDisplayScreen;
         END;

      GetShips(Ground,GrnShp);
      GetShips(FltID,FltShp);
      ShI:=trn;
      WHILE (ShI>=fgt) AND (GrnShp[ShI]+FltShp[ShI]<=9999) DO
         Dec(ShI);

      IF ShI>=fgt THEN
         BEGIN
         WriteString('Abort Fleet:',1,1,C.SYSDispHigh);
         WriteString(MyLord(Player)+', an object cannot hold so many ships--some will be lost.',2,3,C.SYSDispWind);
         InputStrgDisplayScreen('Are you sure you want to abort the fleet (y/N) ? ',2,4,1,Ans);
         IF UpCase(Ans[1])<>'Y' THEN
            Ok:=False;
         ClrDisplayScreen;
         END;

      IF Ok THEN
         BEGIN
         AbortFleet(FltID,Ground,True);
         DestroyFleet(FltID);

         IF Ground.ObjTyp=Flt THEN
            WriteCommLine(FltName+' has been joined with '+GrnName+', '+MyLord(Player)+'.')
         ELSE
            WriteCommLine(FltName+' has been aborted to '+GrnName+', '+MyLord(Player)+'.');

         GetCoord(Ground,GroundXY);
         UMFleets(GroundXY.x,GroundXY.y,Map^);
         END;
      END;
   ActivateLatestStatusWindow;
   END;  { AbortFleetCommand }

PROCEDURE ChangeDestinationCommand(FltID: IDNumber; NewDestination: XYCoord);
   VAR
      FltName: String32;
      NewDestName: String32;
      TempXY: Location;

   BEGIN
   SetFleetDestination(FltID,NewDestination);

   FltName:=ObjectName(Player,FltID,ShortFormat);
   TempXY.ID:=EmptyQuadrant;  TempXY.XY:=NewDestination;
   GetName(Player,TempXY,ShortFormat,NewDestName);

   WriteCommLine('New destination for '+FltName+': '+NewDestName);
   WriteErrorMessage('');
   ActivateLatestStatusWindow;
   END;  { ChangeDestination }

PROCEDURE TransferFleetCommand(FltID: IDNumber);
   VAR
      Ground: IDNumber;
      GroundXY: XYCoord;
      FltSh,GrnSh: ShipArray;
      FltCr,GrnCr: CargoArray;
      FltName,GrnName: String32;
      GroundStatus,Emp: Empire;
      Exit: Boolean;

   { TransferFleetCommand: MAIN PROCEDURE }
   BEGIN
   WriteCommLine('');
   WriteErrorMessage('');

   GetGround(FltID,Ground,False,False,'Transfer Fleet:',
             'What shall I use as the target of the transfer, '+MyLord(Player)+'?',Exit);
   IF NOT Exit THEN
      BEGIN
      FltName:=ObjectName(Player,FltID,ShortFormat);
      GrnName:=ObjectName(Player,Ground,LongFormat);

      WriteString(FltName+' ready for transfer with '+GrnName,1,1,C.SYSDispWind);

      GetShips(FltID,FltSh);
      GetShips(Ground,GrnSh);
      GetCargo(FltID,FltCr);
      GetCargo(Ground,GrnCr);

      GroundStatus:=GetStatus(Ground);
      Emp:=GetStatus(FltID);

      InputNewDistribution(Ground,GroundStatus,GrnSh,GrnCr,FltSh,FltCr);

      ChangeCompositionOfFleet(FltID,Ground,FltSh,FltCr,GrnSh,GrnCr);

      { if ground does not belong to empire, report it. }
      IF GroundStatus<>Emp THEN
         BEGIN
         END;

      GetCoord(Ground,GroundXY);
      UMFleets(GroundXY.x,GroundXY.y,Map^);

      ClrDisplayScreen;
      WriteCommLine('Transfer from '+FltName+' to '+GrnName+' completed.');
      WriteErrorMessage('');
      END;
   ActivateLatestStatusWindow;
   END;  { TransferFleet }

PROCEDURE RefuelFleetCommand(FltID: IDNumber);
   var
      Ground: IDNumber;
      Exit: Boolean;
      Trillum,TonsOnGround,TonsNeeded,MaxTri: Resources;
      FltFuel,MaxFuel: Real;
      FltSh: ShipArray;
      FltN,GrnN: String32;

   PROCEDURE GetTrillumToUse(MaxTri: Resources; VAR Trillum: Resources;
                             VAR Exit: Boolean);
      VAR
         MaxTriN: String8;
         Ok: Boolean;
         TriInp: Integer;

      BEGIN
      Str(MaxTri,MaxTriN);
      REPEAT
         Ok:=True;
         InputIntegerDisplayScreen('Tons of trillum (max: '+MaxTriN+' tons ): ',1,4,TriInp);
         IF TriInp=0 THEN
            BEGIN
            TriInp:=MaxTri;
            WriteString('Maximum used as default.',3,5,C.SYSDispWind);
            END
         ELSE IF TriInp>MaxTri THEN
            BEGIN
            WriteString('The maximum amount allowable is '+MaxTriN+' tons, '
                        +MyLord(Player)+'.',3,5,C.SYSDispWind);
            Ok:=False;
            END
         ELSE IF TriInp<0 THEN
            BEGIN
            WriteString('That is a most bizarre request, '+MyLord(Player)+'.           ',3,5,C.SYSDispWind);
            Ok:=False;
            END;
      UNTIL Ok;

      Trillum:=TriInp;
      Exit:=False;
      END;  { GetTrillumToUse }

   { RefuelFleetCommand: MAIN PROCEDURE }
   begin
   { can only refuel at empire worlds }
   GetGround(FltID,Ground,True,True,'Refuel Fleet:',
             'Where shall I refuel from, '+MyLord(Player)+'?',Exit);
   if not Exit then
      begin
      TonsOnGround:=GetTrillum(Ground);
      GetShips(FltID,FltSh);
      FltFuel:=GetFleetFuel(FltID);
      MaxFuel:=FuelCapacity(FltSh);
      TonsNeeded:=Trunc((MaxFuel-FltFuel)/FuelPerTon)+1;
      MaxTri:=LesserInt(TonsNeeded,TonsOnGround);

      FltN:=ObjectName(Player,FltID,ShortFormat);
      GrnN:=ObjectName(Player,Ground,LongFormat);

      WriteString('Refuel Fleet:',1,1,C.SYSDispHigh);
      IF SameID(FltID,Ground) THEN
         WriteString(FltN+' ready to refuel with trillum in cargo.',1,3,C.SYSDispWind)
      ELSE
         WriteString(FltN+' ready to refuel with trillum from '+GrnN+'.',1,3,C.SYSDispWind);

      GetTrillumToUse(MaxTri,Trillum,Exit);
      IF NOT Exit THEN
         RefuelFleet(FltID,Ground,Trillum)
      ELSE
         ClrDisplayScreen;
      end;

   WriteCommLine('');
   WriteErrorMessage('');
   ActivateLatestStatusWindow;
   end;  { RefuelFleetCommand }

PROCEDURE LaunchProbeCommand(Coord: XYCoord);
   var
      PNum: Word;
      Loc: Location;
      LocN,PNumN: String32;

   { LaunchProbeCommand: MAIN PROCEDURE }
   BEGIN
   GetProbe(Player,PNum);
   IF PNum=0 THEN
      BEGIN
      WriteCommLine('I''m sorry, '+MyLord(Player)+' there are no more probes available.');
      WriteErrorMessage('');
      END
   ELSE
      BEGIN
      LaunchProbe(Player,PNum,Coord);

      Str((11-PNum),PNumN);
      Loc.ID:=EmptyQuadrant;  Loc.XY:=Coord;
      GetName(Player,Loc,LongFormat,LocN);
      WriteCommLine('Probe #'+PNumN+' launched to '+LocN);
      WriteErrorMessage('');
      END;
   ActivateLatestStatusWindow;
   END;  { LaunchProbeCommand }

PROCEDURE MineSweeperCommand(FltID: IDNumber);
   VAR
      XY: XYCoord;
      Loc: Location;
      Emp: Empire;

   BEGIN
   GetCoord(FltID,XY);
   Emp:=EnemyMine(XY);
   IF Emp=Indep THEN
      WriteCommLine('No SRMs found, '+MyLord(Player)+'.')
   ELSE
      BEGIN
      Loc.ID:=EmptyQuadrant;
      Loc.XY:=XY;
      IF Emp<>Player THEN
         AddNews(EnemyMine(XY),SRMClear,Loc,Integer(Player),0,0);
      PutMine(XY,Indep);
		ClrMineScout(XY);

      UMSector(XY.x,XY.y,Map^);
      WriteCommLine('Mine sweeping completed, '+MyLord(Player)+'.');
      END;
   ActivateLatestStatusWindow;
   END;  { MineSweeperCommand }

PROCEDURE SourceError(Error,LineNo: Word; VAR Abort: Boolean);
   VAR
      LineNumStr: String16;
      Line: LineStr;

   BEGIN
   Str(LineNo,LineNumStr);
   CASE Error OF
       BadCommandOER: Line:='Unknown command in line '+LineNumStr;
          BadDestOER: Line:='Unknown destination in line '+LineNumStr;
      BadResourceOER: Line:='Unknown resource in line '+LineNumStr;
      BadTransferOER: Line:='Bad transfer value in line '+LineNumStr;
      END;

   Abort:=True;
   AttentionWindow(Line,'Press <Esc> to abort command.',Abort);
   END;  { SourceError }

PROCEDURE EditSource(VAR EDT: EditStructure);
   VAR
      Ch: Char;

   BEGIN
   REPEAT
      GetCharacter(AnyKey,CaseDistinct,Ch);
      EditText(EDT,Ch);
   UNTIL Ch=EscKey;
   END;  { EditSource }

PROCEDURE FleetOrdersCommand(FltID: IDNumber);
   VAR
      EDT: EditStructure;
      Error,LineNo,Com: Word;
      Code,OldCode: OrderStructure;
      Source: TextStructure;
      Ok,Abort: Boolean;
      FltN: String32;

   BEGIN
   WriteCommLine('');
   WriteErrorMessage('');

   Com:=FleetNextStatement(FltID);
   InitializeText(Source);
   InitializeOrders(Code);
   IF Com<>0 THEN
      { ASSERT: Fleet already has orders }
      BEGIN
      GetFleetCode(FltID,OldCode);
      DeCompileOrders(Player,Source,OldCode);
      END
   ELSE
		BEGIN
      Com:=1;
	   InitializeOrders(OldCode);
		END;

   Ok:=False;
   Abort:=False;

   EDT.Txt:=Source;
   InitializeEdit(EDT,'Anacreon: Orders     <Esc>:End',1,4,80,21,C.SYSDispWind);

   REPEAT
      EditSource(EDT);
      Source:=EDT.Txt;

      DisposeOrders(Code);
      InitializeOrders(Code);
      CompileOrders(Player,Source,Code,Error,LineNo);
      IF Error<>NoOER THEN
         SourceError(Error,LineNo,Abort)
      ELSE
         Ok:=True;
   UNTIL Ok OR Abort;
   CleanUpEdit(EDT);

   IF (NOT Abort) THEN
      BEGIN
      DisposeOrders(OldCode);
      FltN:=ObjectName(Player,FltID,LongFormat);
      IF NumberOfCommands(Code)=0 THEN
         BEGIN
         DisposeOrders(Code);
         SetFleetNextStatement(FltID,0);
         WriteCommLine('All orders to '+FltN+' cancelled, '+MyLord(Player)+'.');
         END
      ELSE
         BEGIN
         SetFleetNextStatement(FltID,Com);
         WriteCommLine('Orders to '+FltN+' completed, '+MyLord(Player)+'.');
         END;
      SetFleetCode(FltID,Code);
      END
   ELSE
      BEGIN
      DisposeOrders(Code);
      END;

   DisposeText(Source);
   ActivateLatestStatusWindow;
   END;  { FleetOrdersCommand }

PROCEDURE FleetCancelOrdersCommand(FltID: IDNumber);
   VAR
      Code: OrderStructure;
      FltN: String32;

   BEGIN
   WriteCommLine('');
   WriteErrorMessage('');
   GetFleetCode(FltID,Code);
   DisposeOrders(Code);
   SetFleetNextStatement(FltID,0);
   FltN:=ObjectName(Player,FltID,LongFormat);
   WriteCommLine('All orders to '+FltN+' cancelled, '+MyLord(Player)+'.');
   ActivateLatestStatusWindow;
   END;  { FleetCancelOrdersCommand }
   
END.
