(* NAME.PAS --------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT Names;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Crt,                                       { Standard CRT Library. }
     Strg,                                      { String Library. }
     DOS2,
     Int,                                       { Integer Library. }
     EIO,                                       { Extended IO Library. }
     WND,
     Types,
     Galaxy,
     DataStrc,                                  { Universe data structure. }
     DataCnst,
     Misc,                                      { Miscellaneous procedures. }
     PrimIntr,                                  { Primitives. }
     Intrface,                                  { Interface. }
     SWindows,                                  { Status windows. }
     MapWind,
     Display;                                   { Display procedures. }

PROCEDURE AddNameCommand(Obj: IDNumber;
                         Coord: XYCoord;
                         NameVar: String32);
PROCEDURE DeleteNameCommand(NameVar: String32);
PROCEDURE StatusHardcopy;

IMPLEMENTATION

USES
   Environ;

PROCEDURE AddNameCommand(Obj: IDNumber;
                         Coord: XYCoord;
                         NameVar: String32);
   var
      Loc: Location;
      Error: Boolean;

   { AddNameCommand: MAIN PROCEDURE }
   BEGIN
   Loc.ID:=Obj;  Loc.XY:=Coord;
   { capitalize first letter }
   NameVar[1]:=UpCase(NameVar[1]);

   AddName(Player,Loc,NameVar,Error);

   if not Error then
      begin
      WriteCommLine('"'+NameVar+'" has been added to the list, '+MyLord(Player)+'.');
      WriteErrorMessage('');
      UMAddName(Map^,NameVar,Loc.XY);
      end
   else
      begin
      WriteErrorMessage('Unfortunately, '+MyLord(Player)+', there isn''t room to define another name.');
      end;
   ActivateLatestStatusWindow;
   end;  { AddName }

PROCEDURE DeleteNameCommand(NameVar: String32);

   { DeleteNameCommand: MAIN PROCEDURE }
   BEGIN
   DeleteName(Player,NameVar);

   WriteCommLine('"'+NameVar+'" has been deleted from the list, '+MyLord(Player)+'.');
   ActivateLatestStatusWindow;
   END;  { DeleteNameCommand }

PROCEDURE StatusHardcopy;
   VAR
      Obj: IDNumber;
      i: Byte;
      Ch: Char;
      CapID: IDNumber;
      Abort: Boolean;
      YearStr: String8;

   PROCEDURE PrintWorldStatus(Player: Empire; WorldID: IDNumber);
      VAR
         Loc: Location;
         NameStr,EmpStr,temp1: String8;
         Emp: Empire;
         Class: WorldClass;
         Typ: WorldTypes;
         Tl: TechLevel;
         Pop: Population;
         Eff: Index;
         Sh: ShipArray;
         Cr: CargoArray;
         Df: DefnsArray;
         Line: LineStr;
         ResI: ResourceTypes;

      PROCEDURE GetResourceEntry(Player,Emp: Empire; Res: ResourceTypes;
                                 Amt: Resources; VAR ResStr: String8);
         BEGIN
         IF Emp<>Player THEN
            BEGIN
            IF Res IN [amb..tri] THEN
               ResStr:='--'
            ELSE IF Amt=0 THEN
               ResStr:='no'
            ELSE IF Amt>9500 THEN
               ResStr:='y+'
            ELSE 
               BEGIN
               Str(Round(Amt/1000):1,ResStr);
               ResStr:='y'+ResStr;
               END;
            END
         ELSE
            BEGIN
            IF Round(Amt/100)>99 THEN
               ResStr:='++'
            ELSE
               Str(Round(Amt/100):2,ResStr);
            END;
         END;  { GetResourceEntry }

      BEGIN
      Loc.ID:=WorldID;  Loc.XY:=Limbo;
      GetName(Player,Loc,ShortFormat,NameStr);
      AdjustString(NameStr,8);

      Class:=GetClass(WorldID);
      Typ:=GetType(WorldID);
      Tl:=GetTech(WorldID);
      Emp:=GetStatus(WorldID);
      Eff:=GetEfficiency(WorldID);
      Pop:=GetPopulation(WorldID);
      GetShipsKnown(Player,WorldID,Sh);
      GetCargo(WorldID,Cr);
      GetDefns(WorldID,Df);
      EmpStr:=EmpireName(Emp);
      AdjustString(EmpStr,3);

      Line:=NameStr+' '+EmpStr+' ';
      Line:=Line+ClassStr[Class]+' ';
      Line:=Line+TypeStr[Typ]+' ';
      Line:=Line+TechStr[Tl]+' ';
      Str(Pop/100:4:1,temp1);
      Line:=Line+temp1+' ';

      FOR ResI:=amb TO tri DO
         BEGIN
         GetResourceEntry(Player,Emp,ResI,Cr[ResI],temp1);
         Line:=Line+temp1+' ';
         END;

      GetResourceEntry(Player,Emp,men,Cr[men],temp1);
      Line:=Line+temp1+' ';
      GetResourceEntry(Player,Emp,nnj,Cr[nnj],temp1);
      Line:=Line+temp1+' ';

      FOR ResI:=fgt TO trn DO
         BEGIN
         GetResourceEntry(Player,Emp,ResI,Sh[ResI],temp1);
         Line:=Line+temp1+' ';
         END;

      FOR ResI:=LAM TO ion DO
         BEGIN
         GetResourceEntry(Player,Emp,ResI,Df[ResI],temp1);
         Line:=Line+temp1;
         IF ResI<>ion THEN
            Line:=Line+' ';
         END;

      PrintLine(Line);
      END;  { PrintStatus }

   { StatusHardcopy: MAIN PROCEDURE }
   BEGIN
   Abort:=True;
   AttentionWindow('Please prepare the printer for output...',
                   'Press any key to start or <Esc> to cancel.',Abort);
   IF NOT Abort THEN
      BEGIN
      WriteCommLine('Printing...');
      PRNReset;
      GetCapital(Player,CapID);

      Str(Year,YearStr);
      PrintLine(EmpireName(Player)+' status: '+YearStr);
      PrintLine('');
      PrintLine('PlntName Sta C T Tl  Pop am ch mt sp tr MN NJ FT HK JM JT PN ST TN lm df gd in');
      PrintLine('------------------------------------------------------------------------------');
      PrintWorldStatus(Player,CapID);

      Obj.ObjTyp:=Pln;
      FOR i:=1 TO NoOfPlanets DO
         IF i IN SetOfPlanetsOf[Player] THEN
            BEGIN
            Obj.Index:=i;
            IF NOT SameID(Obj,CapID) THEN
               PrintWorldStatus(Player,Obj);
            END;

      Obj.ObjTyp:=Base;
      FOR i:=1 TO MaxNoOfStarbases DO
         IF i IN SetOfStarbasesOf[Player] THEN
            BEGIN
            Obj.Index:=i;
            IF NOT SameID(Obj,CapID) THEN
               PrintWorldStatus(Player,Obj);
            END;

      Obj.ObjTyp:=Pln;
      FOR i:=1 TO NoOfPlanets DO
         IF NOT (i IN SetOfPlanetsOf[Player]) THEN
            BEGIN
            Obj.Index:=i;
            IF Scouted(Player,Obj) THEN
               PrintWorldStatus(Player,Obj);
            END;

      Obj.ObjTyp:=Base;
      FOR i:=1 TO MaxNoOfStarbases DO
         IF (i IN SetOfActiveStarbases) 
            AND (NOT (i IN SetOfStarbasesOf[Player])) THEN
            BEGIN
            Obj.Index:=i;
            IF Scouted(Player,Obj) THEN
               PrintWorldStatus(Player,Obj);
            END;

      PRNFormFeed;
      END;

   WriteCommLine('');
   END;  { StatusHarcopy }

END.  { Unit }
