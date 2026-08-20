(* MESS.PAS --------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT Mess;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Strg,                                      { String Library. }
     Int,
     EIO,                                       { Extended IO Library. }
     TextStrc,
     Types,
     Galaxy,
     News;

TYPE
   MessageRecordPtr = ^MessageRecord;
   MessageRecord = RECORD
      Sender: Empire;                  { empire who sent message }
      Recipient: ScoutSet;             { emperors to whom it is addressed }
      ReadBy: ScoutSet;                { emperors who have read it }
      Read: Boolean;                   { true if it has been read }
      Intercepted: Boolean;            { true if this is an intercepted message }
      MesText: TextStructure;

      Next: MessageRecordPtr;
      Prev: MessageRecordPtr;
   END;  { MessageRecord }
 
   MessageArray = ARRAY [1..20] OF MessageRecordPtr;

VAR
   MessageList: MessageRecordPtr;

PROCEDURE DeleteAllMessages;
PROCEDURE DeleteReadMessages;
PROCEDURE GetMessages(Emp: Empire; VAR NoOfMessages: Word;
                      VAR Messages: MessageArray);
FUNCTION LoadMessageData(VAR SF: FILE): Word;
FUNCTION SaveMessageData(VAR SF: FILE): Word;
PROCEDURE SendMessage(Emp: Empire; Empires: EmpireSet; 
                      VAR MessageText: TextStructure);
PROCEDURE SetMessageRead(Emp: Empire; Mess: MessageRecordPtr);

IMPLEMENTATION

USES
   Misc,
   PrimIntr,
   Dos2;

PROCEDURE GetMessages(Emp: Empire; VAR NoOfMessages: Word;
                      VAR Messages: MessageArray);
   VAR
      NextMess: MessageRecordPtr;

   BEGIN
   NextMess:=MessageList;
   NoOfMessages:=0;
   FillChar(Messages,SizeOf(Messages),0);
   WHILE NextMess<>Nil DO
      BEGIN
      IF Emp IN NextMess^.Recipient THEN
         BEGIN
         Inc(NoOfMessages);
         Messages[NoOfMessages]:=NextMess;
         END;
      NextMess:=NextMess^.Next;
      END;
   END;  { GetMessages }

PROCEDURE DeleteMessage(Mess: MessageRecordPtr);
   BEGIN
   WITH Mess^ DO
      BEGIN
      DisposeText(MesText);
      IF Next<>Nil THEN
         Next^.Prev:=Prev;

      IF Prev<>Nil THEN
         Prev^.Next:=Next
      ELSE
         MessageList:=Next;
      END;
   Dispose(Mess);
   END;  { DeleteMessage }

PROCEDURE DeleteReadMessages;
   VAR
      MesToDelete,NextMes: MessageRecordPtr;

   BEGIN
   NextMes:=MessageList;

   WHILE NextMes<>Nil DO
      IF NextMes^.Read THEN
         BEGIN
         MesToDelete:=NextMes;
         NextMes:=NextMes^.Next;
         DeleteMessage(MesToDelete);
         END
      ELSE
         NextMes:=NextMes^.Next;
   END;  { DeleteReadMessages }

PROCEDURE DeleteAllMessages;
   VAR
      MesToDelete,NextMes: MessageRecordPtr;

   BEGIN
   NextMes:=MessageList;

   WHILE NextMes<>Nil DO
      BEGIN
      MesToDelete:=NextMes;
      NextMes:=NextMes^.Next;
      DeleteMessage(MesToDelete);
      END
   END;  { DeleteAllMessages }

PROCEDURE NewMessage(VAR NewMess: MessageRecordPtr);
   BEGIN
   New(NewMess);
   WITH NewMess^ DO
      BEGIN
      Prev:=Nil;
      Next:=MessageList;
      END;  { with scope }

   IF MessageList<>Nil THEN
      MessageList^.Prev:=NewMess;

   MessageList:=NewMess;
   END;  { NewMessage }

PROCEDURE InterceptMessage(Emp,Interceptor: Empire; Obj: IDNumber;
                           VAR MessageText: TextStructure);
   VAR
      GarbledText: TextStructure;
      GarbLine,CurLine: LineRecordPtr;
      NewMess: MessageRecordPtr;
      i,j,StartGarb,MaxLen: Word;
      Temp: LineStr;
      Loc: Location;

   BEGIN
   InitializeText(GarbledText);
   CurLine:=MessageText.FirstLine;
   GarbLine:=GarbledText.FirstLine;
   GarbLine^.Line:=CurLine^.Line;
   CurLine:=CurLine^.Next;
   WHILE CurLine<>Nil DO
      BEGIN
      InsertLine(MessageText,GarbLine);
      GarbLine:=GarbLine^.Next;
      Temp:=CurLine^.Line;
      IF Length(Temp)>5 THEN
         FOR i:=1 TO Rnd(0,7) DO
            BEGIN
            StartGarb:=Rnd(1,Length(Temp)-5);
            MaxLen:=LesserInt(1+Length(Temp)-StartGarb,10);
            FOR j:=StartGarb TO StartGarb+Rnd(1,MaxLen) DO
               Temp[j]:='.';
            END;
      GarbLine^.Line:=Temp;
      CurLine:=CurLine^.Next;
      END;

   NewMessage(NewMess);
   WITH NewMess^ DO
      BEGIN
      Sender:=Emp;
      Recipient:=[Interceptor];
      ReadBy:=[];
      Read:=False;
      Intercepted:=True;
      MesText:=GarbledText;
      END;

   Loc.ID:=Obj;  Loc.XY:=Limbo;
   AddNews(Interceptor,MessI,Loc,Integer(Emp),0,0);
   END;  { InterceptMessage }

PROCEDURE SendMessage(Emp: Empire; Empires: EmpireSet; 
                      VAR MessageText: TextStructure);
   VAR
      NewMess: MessageRecordPtr;
      TargetEmp,EmpI: Empire;
      Loc: Location;
      Obj,CapID: IDNumber;
      SendXY,IntXY: XYCoord;
      i,j,NoOfIntercepts,ChanceToIntercept: Word;

   BEGIN
   NewMessage(NewMess);
   WITH NewMess^ DO
      BEGIN
      Sender:=Emp;
      Recipient:=Empires;
      ReadBy:=[];
      Read:=False;
      Intercepted:=False;
      MesText:=MessageText;
      END;  { NewMess }

   FOR EmpI:=Empire1 TO Empire8 DO
      IF EmpI IN Empires THEN
         TargetEmp:=EmpI;

   Obj.ObjTyp:=Pln;
   GetCapital(TargetEmp,CapID);
   GetCoord(CapID,SendXY);
   NoOfIntercepts:=0;
   Loc.ID:=EmptyQuadrant;  Loc.XY:=Limbo;
   FOR EmpI:=Empire1 TO Empire8 DO
      IF EmpI IN Empires THEN
         BEGIN
         AddNews(EmpI,MessR,Loc,Integer(Emp),0,0);
         END
      ELSE IF (EmpI<>Emp) AND (NoOfIntercepts<=5) AND (Empires<>[Emp]) THEN
         BEGIN
         FOR j:=1 TO NoOfPlanets DO
            IF j IN SetOfPlanetsOf[EmpI] THEN
               BEGIN
               Obj.Index:=j;
               GetCoord(Obj,IntXY);
               ChanceToIntercept:=Round((1/(Sqr(Distance(SendXY,IntXY))))*150);
               IF Rnd(1,100)<=ChanceToIntercept THEN
                  BEGIN
                  InterceptMessage(Emp,EmpI,Obj,MessageText);
                  Inc(NoOfIntercepts);
                  END;
               END;
         END;
   END;  { SendMessage }

PROCEDURE SetMessageRead(Emp: Empire; Mess: MessageRecordPtr);
   BEGIN
   WITH Mess^ DO
      BEGIN
      ReadBy:=ReadBy+[Emp];
      IF Recipient<=ReadBy THEN
         Read:=True;
      END;
   END;  { SetMessageRead }

FUNCTION LoadMessageData(VAR SF: FILE): Word;
{ LoadMessageData: -------------------------------------------------------------
   This procedure loads previously saved messages.  It expects the given file
   to be opened and correctly positioned.  The data is read in the following
   format:

         No. Of Messages (Byte)
         1st Message Record (MessageRecord)
            No. Of Lines (Byte)
            Line 1 (LineStr)
            Line 2 (LineStr)
                .
                .
                .
         2nd Message Record (MessageRecord)
                .
                .
                .
                .
                .

------------------------------------------------------------------------------ }
   VAR
      NoOfLines,NoOfMessages: Byte;
      Error,i,j: Word;
      NextMess: MessageRecordPtr;
      TempMess: MessageRecord;
      CurLine: LineRecordPtr;

   BEGIN
   Error:=ReadVariable(SF,NoOfMessages,1);
   i:=NoOfMessages;
   WHILE (i>0) AND (Error=0) DO
      BEGIN
      NewMessage(NextMess);
      Error:=ReadVariable(SF,TempMess,SizeOf(TempMess));
      NextMess^.Sender:=TempMess.Sender;
      NextMess^.Recipient:=TempMess.Recipient;
      NextMess^.Read:=TempMess.Read;
      NextMess^.Intercepted:=TempMess.Intercepted;
      InitializeText(NextMess^.MesText);

      Error:=ReadVariable(SF,NoOfLines,1);

      CurLine:=NextMess^.MesText.FirstLine;
      IF NoOfLines>0 THEN
         BEGIN
         Error:=ReadVariable(SF,CurLine^.Line,SizeOf(CurLine^.Line));

         FOR j:=2 TO NoOfLines DO
            BEGIN
            InsertLine(NextMess^.MesText,CurLine);
            CurLine:=CurLine^.Next;
            Error:=ReadVariable(SF,CurLine^.Line,SizeOf(CurLine^.Line));
            END;
         END;

		Dec(i);
      END;  { while }

   { ASSERT: no more messages to save OR error condition }
   IF Error<>0 THEN
      DeleteAllMessages;

   LoadMessageData:=Error;
   END;  { LoadMessageData }

FUNCTION SaveMessageData(VAR SF: FILE): Word;
{ SaveMessageData: -------------------------------------------------------------
   This procedure saves messages.  It expects the given file to be opened and
   correctly positioned.
------------------------------------------------------------------------------ }
   VAR
      NoOfLines,NoOfMessages: Byte;
      Error,i,j: Word;
      NextMess: MessageRecordPtr;
      CurLine: LineRecordPtr;

   BEGIN
   NoOfMessages:=0;
   NextMess:=MessageList;
   WHILE NextMess<>Nil DO
      BEGIN
      Inc(NoOfMessages);
      NextMess:=NextMess^.Next;
      END;

   Error:=WriteVariable(SF,NoOfMessages,1);
   NextMess:=MessageList;

   WHILE (NextMess<>Nil) AND (Error=0) DO
      BEGIN
      Error:=WriteVariable(SF,NextMess^,SizeOf(NextMess^));

      CurLine:=NextMess^.MesText.FirstLine;
      NoOfLines:=0;
      WHILE CurLine<>Nil DO
         BEGIN
         Inc(NoOfLines);
         CurLine:=CurLine^.Next;
         END;

      Error:=WriteVariable(SF,NoOfLines,1);

      CurLine:=NextMess^.MesText.FirstLine;
      FOR j:=1 TO NoOfLines DO
         BEGIN
         Error:=WriteVariable(SF,CurLine^.Line,SizeOf(CurLine^.Line));
         CurLine:=CurLine^.Next;
         END;

      NextMess:=NextMess^.Next;
      END;  { while }

   SaveMessageData:=Error;
   END;  { SaveMessageData }

BEGIN
MessageList:=Nil;
END.
