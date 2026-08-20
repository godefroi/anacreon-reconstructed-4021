(* ORDERS.PAS ------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT Orders;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES
   Strg,
   System2,
   TextStrc,
   Types,
   Galaxy,
   DataStrc,
   Misc,
   PrimIntr;

CONST
   { Order errors }
   NoOER          = 0;
   BadCommandOER  = 1;
   BadDestOER     = 2;
   BadResourceOER = 3;
   BadTransferOER = 4;

TYPE
   OrderStructure = RECORD
      Len: Word;
      Data: MemoryArrayPtr;
   END;

   CommandTypes =
      ( NoCOM,
        DestCOM,                       { set new destination }
        TransCOM,                      { transfer }
        RepeatCOM,                     { repeat orders }
        AbortCOM,                      { abort the fleet }
        SweepCOM,                      { sweep SRMs }
        StopCOM,                       { end orders }
        WaitCOM );                     { wait }

   CommandRecord = RECORD
      Typ: CommandTypes;
      CASE Integer OF
         0: (Loc: Location);
         1: (Res: ResourceTypes;
             Trns: Integer);
   END;

PROCEDURE AddOrders(VAR Code: OrderStructure; NewComm: CommandRecord);
PROCEDURE CompileOrders(Emp: Empire;
                        VAR Source: TextStructure; VAR Code: OrderStructure;
                        VAR Error,LineNo: Word);
PROCEDURE DeCompileOrders(Emp: Empire;
                          VAR Source: TextStructure;
                          VAR Code: OrderStructure);
PROCEDURE DisposeOrders(VAR Code: OrderStructure);
FUNCTION FleetNextStatement(FltID: IDNumber): Word;
PROCEDURE GetCommandRecord(VAR Code: OrderStructure; CNum: Word; VAR Comm: CommandRecord);
PROCEDURE GetFleetCode(FltID: IDNumber; VAR Code: OrderStructure);
PROCEDURE InitializeOrders(VAR Code: OrderStructure);
FUNCTION NumberOfCommands(VAR Code: OrderStructure): Word;
PROCEDURE SetFleetCode(FltID: IDNumber; VAR Code: OrderStructure);
PROCEDURE SetFleetNextStatement(FltID: IDNumber; Com: Word);

IMPLEMENTATION

CONST
   MaxNoOfParms = 4;
   MaxNoOfOrders = 100;

   ResourceName: ARRAY [fgt..tri] OF STRING [3] =
      ( 'FGT','HKR','JMP','JTN','PEN','STR','TRN',
        'MEN','NNJ','AMB','CHE','MET','SUP','TRI' );

TYPE
   OrdersArray = ARRAY [1..MaxNoOfOrders] OF CommandRecord;
   OrdersPtr = ^OrdersArray;

   ParmArray = ARRAY [1..MaxNoOfParms] OF String32;

FUNCTION NumberOfCommands(VAR Code: OrderStructure): Word;
   BEGIN
   NumberOfCommands:=Code.Len DIV SizeOf(CommandRecord);
   END;  { NumberOfCommands }

PROCEDURE GetCommandRecord(VAR Code: OrderStructure; CNum: Word; VAR Comm: CommandRecord);
   BEGIN
   Comm:=OrdersPtr(Code.Data)^[CNum];
   END;  { GetCommandRecord }

PROCEDURE InitializeOrders(VAR Code: OrderStructure);
   BEGIN
   WITH Code DO
      BEGIN
      Data:=Nil;
      Len:=0;
      END;
   END;  { InitializeOrders }

PROCEDURE AddOrders(VAR Code: OrderStructure; NewComm: CommandRecord);
   VAR
      NewData: MemoryArrayPtr;
      NewLen: Word;

   BEGIN
   WITH Code DO
      BEGIN
      NewLen:=Len+SizeOf(NewComm);
      GetMem(NewData,NewLen);

      IF Len<>0 THEN
         BEGIN
         Move(Data^,NewData^,Len);
         FreeMem(Data,Len);
         END;

      Move(NewComm,NewData^[Len],SizeOf(NewComm));
      Len:=NewLen;
      Data:=Pointer(NewData);
      END;  { with scope }
   END;  { AddOrders }

PROCEDURE SplitLine(Line: LineStr; VAR Parm: ParmArray);
   VAR
      PNum,i: Word;
      SkipBlanks: Boolean;

   BEGIN
   PNum:=1;
   Parm[1]:='';
   SkipBlanks:=True;
   FOR i:=1 TO Length(Line) DO
      BEGIN
      IF Line[i]=' ' THEN
         BEGIN
         IF NOT SkipBlanks THEN
            BEGIN
            IF PNum<MaxNoOfParms THEN
               BEGIN
               Inc(PNum);
               Parm[PNum]:='';
               END;
            SkipBlanks:=True;
            END;
         END
      ELSE
         BEGIN
         SkipBlanks:=False;
         Parm[PNum]:=Parm[PNum]+Line[i];
         END;
      END;
   END;  { SplitLine }

PROCEDURE GetResourceType(Line: LineStr; VAR Res: ResourceTypes; VAR Error: Word);

   BEGIN
   AllUpCase(Line);
   Line:=Copy(Line,1,3);

   Res:=fgt;
   WHILE (Res<=tri) AND (ResourceName[Res]<>Line) DO
      Inc(Res);

   IF Res>tri THEN
      Error:=BadResourceOER;
   END;  { GetResourceType }

PROCEDURE GetTransfer(Line: LineStr; VAR Trans: Integer; VAR Error: Word);
   VAR
      BadInt: Word;

   BEGIN
   Val(Line,Trans,BadInt);
   IF BadInt<>0 THEN
      Error:=BadTransferOER;
   END;  { GetTransfer }

PROCEDURE GetDestination(Line: LineStr; Emp: Empire; VAR Loc: Location; VAR Error: Word);
   BEGIN
   GetLocation(Emp,Line,Loc);
   IF SameID(Loc.ID,EmptyQuadrant) AND SameXY(Loc.XY,Limbo) THEN
      Error:=BadDestOER;
   END;  { GetDestination }
   
PROCEDURE ParseLine(Emp: Empire; Line: LineStr; VAR Comm: CommandRecord; VAR Error: Word);
{ ParseLine: -------------------------------------------------------------------
   Converts an input line (e.g. "TRANSFER 1000 metals") to a CommandRecord.
   Any errors are returned in Error.
------------------------------------------------------------------------------ }
   VAR
      Parm: ParmArray;

   BEGIN
   Error:=0;
   AllUpCase(Line);
   SplitLine(Line,Parm);
   Parm[1]:=Copy(Parm[1],1,4);
   IF Parm[1]='TRAN' THEN
      BEGIN
      Comm.Typ:=TransCOM;
      GetResourceType(Parm[3],Comm.Res,Error);
      GetTransfer(Parm[2],Comm.Trns,Error);
      END
(*
   ELSE IF Parm[1]='ABOR' THEN
      BEGIN
      Comm.Typ:=AbortCom;
      GetDestination(Parm[2],Emp,Comm.Loc,Error);
      END
*)
   ELSE IF Parm[1]='SRMS' THEN
      BEGIN
      Comm.Typ:=SweepCom;    
      END
   ELSE IF Parm[1]='DEST' THEN
      BEGIN
      Comm.Typ:=DestCOM;
      GetDestination(Parm[2],Emp,Comm.Loc,Error);
      END
   ELSE IF Parm[1]='REPE' THEN
      BEGIN
      Comm.Typ:=RepeatCOM;
      END
	ELSE IF Parm[1]='WAIT' THEN
		BEGIN
		Comm.Typ:=WaitCOM;
		END
   ELSE IF Parm[1]='' THEN
      BEGIN
      Comm.Typ:=NoCOM;
      END
   ELSE
      BEGIN
      Error:=BadCommandOER;
      Comm.Typ:=NoCOM;
      END;
   END;  { ParseLine }

PROCEDURE CompileOrders(Emp: Empire;
                        VAR Source: TextStructure; VAR Code: OrderStructure;
                        VAR Error,LineNo: Word);
{ CompileOrders: ---------------------------------------------------------------
   Converts from an input text to a compile orders. Code must be an initialized
   variable. Source must contain the source text. Error returns any error
   found, and LineNo returns the line number of the error.
------------------------------------------------------------------------------ }
   VAR
      NoOfLines,i: Word;
      CurrLine: LineRecordPtr;
      Line: LineStr;
      Comm: CommandRecord;

   BEGIN
   CurrLine:=TXTFirstLine(Source);
   NoOfLines:=TXTNumberOfLines(Source);
   i:=1;
   Error:=0;
   WHILE (i<=NoOfLines) AND (Error=NoOER) DO
      BEGIN
      Line:=TXTLine(CurrLine);
      ParseLine(Emp,Line,Comm,Error);
      IF Comm.Typ<>NoCOM THEN
         AddOrders(Code,Comm);

      CurrLine:=TXTNextLine(CurrLine);
      Inc(i);
      END;

   IF Error<>NoOER THEN
      LineNo:=i-1;
   END;  { CompileOrders }

PROCEDURE DeCompileOrders(Emp: Empire;
                          VAR Source: TextStructure;
                          VAR Code: OrderStructure);
{ DeCompileOrders: -------------------------------------------------------------
   Converts from OrderStructure to Source. Source must be an initialized
   variable. Code must contain the fleet orders.
------------------------------------------------------------------------------ }
   VAR
      Comm: CommandRecord;
      Line,Temp: LineStr;
      CurrLine: LineRecordPtr;
      i,NoOfOrders: Word;
      Orders: OrdersPtr;

   BEGIN
   CurrLine:=TXTFirstLine(Source);
   WITH Code DO
      BEGIN
      Orders:=OrdersPtr(Data);
      NoOfOrders:=Len DIV SizeOf(CommandRecord);
      END;

   FOR i:=1 TO NoOfOrders DO
      WITH Orders^[i] DO
         BEGIN
         CASE Typ OF
(*
            AbortCom: BEGIN
               Line:='ABORtTo'
               END;
*)
            SweepCom: BEGIN
               Line:='SRMSweep'
               END;      
            DestCOM: BEGIN
               Line:='DESTination ';
               GetName(Emp,Loc,ShortFormat,Temp);
               Line:=Line+Temp;
               END;
            TransCOM: BEGIN
               Line:='TRANsfer ';
               Str(Trns,Temp);
               Line:=Line+Temp+' '+ResourceName[Res];
               END;
            RepeatCOM: Line:='REPEat';
				WaitCOM: Line:='WAIT';
            END;  { case }
         TXTSetLine(CurrLine,Line);
         TXTSetParagraph(CurrLine);
         InsertLine(Source,CurrLine);
         CurrLine:=TXTNextLine(CurrLine);
         END;  { with scope and loop }
   END;  { DeCompileOrders }

FUNCTION FleetNextStatement(FltID: IDNumber): Word;
   BEGIN
   FleetNextStatement:=Universe^.Fleet[FltID.Index]^.NextOrder;
   END;  { FleetNextStatement }

PROCEDURE SetFleetNextStatement(FltID: IDNumber; Com: Word);
   BEGIN
   Universe^.Fleet[FltID.Index]^.NextOrder:=Com;
   END;  { SetFleetNextStatement }

PROCEDURE GetFleetCode(FltID: IDNumber; VAR Code: OrderStructure);
   BEGIN
   Code:=OrderStructure(Universe^.Fleet[FltID.Index]^.OrderData);
   END;  { GetFleetCode }

PROCEDURE SetFleetCode(FltID: IDNumber; VAR Code: OrderStructure);
   VAR
      NewCode: ^OrderStructure;

   BEGIN
   Move(Code,Universe^.Fleet[FltID.Index]^.OrderData,SizeOf(Code));
   END;  { SetFleetCode }

PROCEDURE DisposeOrders(VAR Code: OrderStructure);
   BEGIN
   WITH Code DO
      BEGIN
      IF Len<>0 THEN
			BEGIN
         FreeMem(Data,Len);
			END;

		Data:=Nil;
		Len:=0;
      END;
   END;  { DisposeOrders }

END.
