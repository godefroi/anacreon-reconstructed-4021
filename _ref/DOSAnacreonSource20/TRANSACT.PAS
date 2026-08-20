(* TRANSACT.PAS --------------------------------------------------------------

--------------------------------------------------------------------------- *)

UNIT Transact;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES
   Types;

PROCEDURE DefineTransaction(WorldID: IDNumber; Code: Pointer);
PROCEDURE InitializeTransactions;
PROCEDURE Transaction(WorldID,FltID: IDNumber; Emp: Empire);

IMPLEMENTATION

USES
   DataCnst,
   CdeTypes,
   Code;

TYPE
   TransactionRecord = RECORD
      TransCode: ActionArrayPtr;
      EmpireData: ARRAY [Empire] OF RegisterArray;
   END;
   TransactionPtr = ^TransactionRecord;

   TransactionSpine = ARRAY [1..MaxNoOfPlanets] OF TransactionPtr;

VAR
   Trans: TransactionSpine;

PROCEDURE InitializeTransactions;
   BEGIN
   FillChar(Trans,SizeOf(Trans),0);
   END;  { InitializeTransactions }

PROCEDURE Transaction(WorldID,FltID: IDNumber; Emp: Empire);
   VAR
      IP: Word;
      R: RegisterArray;

   BEGIN
   WITH Trans[WorldID.Index]^ DO
      BEGIN
      IP:=1;

      R[0].VType:=IDVRT;
      R[0].ID:=WorldID;

      R[1].VType:=EmpVRT;
      R[1].Emp:=Emp;

      R[2].VType:=IDVRT;
      R[2].ID:=FltID;

      CodeInterpreter(TransCode^,IP,EmpireData[Emp],R);
      END;  { with scope }
   END;  { Transaction }

PROCEDURE InitTransaction(WorldID: IDNumber; Emp: Empire);
   VAR
      IP: Word;
      R: RegisterArray;

   BEGIN
   WITH Trans[WorldID.Index]^ DO
      BEGIN
      IP:=1;

      R[0].VType:=IDVRT;
      R[0].ID:=WorldID;

      R[1].VType:=EmpVRT;
      R[1].Emp:=Indep;

      CodeInterpreter(TransCode^,IP,EmpireData[Emp],R);
      END;  { with scope }
   END;  { InitTransaction }

PROCEDURE DefineTransaction(WorldID: IDNumber; Code: Pointer);
   VAR
      Emp: Empire;

   BEGIN
   IF WorldID.Index>0 THEN
      BEGIN
      New(Trans[WorldID.Index]);
      WITH Trans[WorldID.Index]^ DO
         BEGIN
         TransCode:=Code;
         FillChar(EmpireData,SizeOf(EmpireData),0);
         END;

      FOR Emp:=Empire1 TO Empire8 DO
         InitTransaction(WorldID,Emp);
      END;
   END;  { DefineTransaction }

END.
