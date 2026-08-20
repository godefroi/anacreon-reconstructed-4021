(* ARTIFACT.PAS --------------------------------------------------------------

   Adding a Situation:
      1) Add the situation type to SituationTypes.  If necessary, modify
         LastSIT.
      2) Add keyword to SituationKeyword in NEWGAME.PAS
      3) Write a procedure that initializes registers and calls
         CodeInterpreter.

   Adding an Action (CODE.PAS):
      1) Add the action to ActionTypes.  If necessary, modify LastACT.
      2) Add keyword to ActionKeyword in NEWGAME.PAS
      3) Add number of parameters to ActionParms in NEWGAME.PAS
      4) Add new case and procedure to ExecuteAction

--------------------------------------------------------------------------- *)

UNIT Artifact;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES
   Strg,
   Int,
   Galaxy,
   Types,
   CdeTypes,
   DataStrc,
   Misc,
   Primintr,
   swindows,
   display;

CONST
   MaxNoOfArtifacts = 255;

TYPE
   SituationTypes = (
      NoSIT,
      ActiveSIT,                 { artifact is activated }
      CloseUpSIT,                { close up on world of artifact }
      UpdateSIT );               { computer update }
   SetOfSitu = SET OF SituationTypes;

   Attributes = ( MobileATT,
                  UsableByOwnATT,
                  UsableByOtherATT,
                  UniqueATT,
                  PieceOfATT,
                  HiddenToAllATT,
                  HiddenToOtherATT );
   SetOfAttr = SET OF Attributes;

CONST
   LastSIT = UpdateSIT;
   FirstArtifact: IDNumber = (ObjTyp: ArtOBJ; Index: 0);

VAR
   NoOfArtifactTypes: Word;
   LastArtifact: Word;

PROCEDURE ActiveSituation(ArtID: IDNumber; Activator: Empire);
PROCEDURE CloseUpSituation(ArtID: IDNumber; Player: Empire);

PROCEDURE AllocateArtifactTypes(NoOfTypes: Word);
FUNCTION ArtifactKnown(Emp: Empire; ArtID: IDNumber): Boolean;
PROCEDURE ArtifactLoc(ArtID: IDNumber; VAR Loc: Location);
FUNCTION ArtifactName(ArtID: IDNumber): LineStr;
FUNCTION ArtifactSituation(ArtID: IDNumber; Sit: SituationTypes): Boolean;
FUNCTION ArtifactStatus(ArtID: IDNumber): Empire;
PROCEDURE CreateArtifact(ArtID: IDNumber;
                         ArtType: Word;
                         ArtLoc: Location;
                         Globals: RegisterArray);
PROCEDURE DefineArtifact(ArtType: Word;
                         ArtName: String32;
                         ArtAttr: SetOfAttr;
                         ArtSize: Word;
                         ArtPieceOf: Byte;
                         ArtDesc: Word;
                         ArtSits: SetOfSitu;
                         ArtCode: ActionArrayPtr);
PROCEDURE InitializeArtifacts;
PROCEDURE NextArtifact(VAR ArtID: IDNumber);
PROCEDURE NextArtifactOn(VAR ArtID: IDNumber; ID: IDNumber);

IMPLEMENTATION

USES
   Crt,
   EIO,
   Menu,
   Wnd,
   TextStrc,
   Scena,
   Code;

TYPE
   ArtifactTypeRecord = RECORD
      Name: String32;                        { name of the artifact }
      Attr: SetOfAttr;                       { flags }
      Size: Word;                            { cargo space required }
      PieceOf: Byte;                         { type of complete artifact }
                                             {    or no of pieces        }
      Desc: Word;                            { description of artifact }

      Situations: SetOfSitu;
      Code: ActionArrayPtr;                  { executable code }
   END;
   ArtifactTypeArray = ARRAY [1..1000] OF ArtifactTypeRecord;
   ArtifactTypeArrayPtr = ^ArtifactTypeArray;

   ArtifactRecord = RECORD
      AType: Word;                           { index into type array }
      Loc: Location;                         { current location }
      V: RegisterArray;                      { general purpose variables }

      NextID: IDNumber;                      { next artifact }
   END;
   ArtifactPtr = ^ArtifactRecord;
   ArtifactSpine = ARRAY [1..MaxNoOfArtifacts] OF ArtifactPtr;

VAR
   Artfct: ArtifactSpine;
   ArtifactType: ArtifactTypeArrayPtr;

{ Primitives --------------------------------------------------------------- }

{ Interface ---------------------------------------------------------------- }

PROCEDURE AllocateArtifactTypes(NoOfTypes: Word);
   BEGIN
   GetMem(ArtifactType,NoOfTypes*SizeOf(ArtifactTypeRecord));
   END;  { AllocateArtifactTypes }

PROCEDURE DefineArtifact(ArtType: Word;
                         ArtName: String32;
                         ArtAttr: SetOfAttr;
                         ArtSize: Word;
                         ArtPieceOf: Byte;
                         ArtDesc: Word;
                         ArtSits: SetOfSitu;
                         ArtCode: ActionArrayPtr);
   BEGIN
   WITH ArtifactType^[ArtType] DO
      BEGIN
      Name:=ArtName;
      Desc:=ArtDesc;
      Attr:=ArtAttr;
      Size:=ArtSize;
      PieceOf:=ArtPieceOf;
      Desc:=ArtDesc;

      Situations:=ArtSits;
      Code:=ArtCode;
      END;  { with scope }
   END;  { DefineArtifact }

PROCEDURE CreateArtifact(ArtID: IDNumber;
                         ArtType: Word;
                         ArtLoc: Location;
                         Globals: RegisterArray);

   BEGIN
   New(Artfct[ArtID.Index]);
   WITH Artfct[ArtID.Index]^ DO
      BEGIN
      AType:=ArtType;
      Loc:=ArtLoc;
      V:=Globals;
      END;  { with scope }
   END;  { CreateArtifact }

PROCEDURE NextArtifactOn(VAR ArtID: IDNumber; ID: IDNumber);
   VAR
      Art: Word;

   BEGIN
   Art:=ArtID.Index;
   REPEAT
      Inc(Art);
   UNTIL ((Artfct[Art]<>Nil) AND (SameID(ID,Artfct[Art]^.Loc.ID)))
          OR (Art>LastArtifact);

   IF Art>LastArtifact THEN
      ArtID:=EmptyQuadrant
   ELSE
      ArtID.Index:=Art;
   END;  { NextArtifactOn }

PROCEDURE NextArtifact(VAR ArtID: IDNumber);
   VAR
      Art: Word;

   BEGIN
   Art:=ArtID.Index;

   REPEAT
      Inc(Art);
   UNTIL (Artfct[Art]<>Nil) OR (Art>LastArtifact);

   IF Art>LastArtifact THEN
      ArtID:=EmptyQuadrant
   ELSE
      ArtID.Index:=Art;
   END;  { NextArtifact }

PROCEDURE InitializeArtifacts;
   BEGIN
   FillChar(Artfct,SizeOf(Artfct),0);
   LastArtifact:=0;
   END;  { InitializeArtifacts }

FUNCTION ArtifactStatus(ArtID: IDNumber): Empire;
   BEGIN
   ArtifactStatus:=GetStatus(Artfct[ArtID.Index]^.Loc.ID);
   END;  { ArtifactStatus }

FUNCTION ArtifactName(ArtID: IDNumber): LineStr;
   BEGIN
   ArtifactName:=ArtifactType^[Artfct[ArtID.Index]^.AType].Name;
   END;  { ArtifactName }

PROCEDURE ArtifactLoc(ArtID: IDNumber; VAR Loc: Location);
   BEGIN
   Loc:=Artfct[ArtID.Index]^.Loc;
   END;  { ArtifactName }

FUNCTION ArtifactKnown(Emp: Empire; ArtID: IDNumber): Boolean;
   BEGIN
   WITH Artfct[ArtID.Index]^ DO
      BEGIN
      IF SameID(Loc.ID,EmptyQuadrant) THEN
         ArtifactKnown:=(Emp IN (Sector[Loc.XY.x]^[Loc.XY.y].Flts))
      ELSE
         ArtifactKnown:=Known(Emp,Loc.ID);
      END;
   END;  { ArtifactKnown }

FUNCTION ArtifactSituation(ArtID: IDNumber; Sit: SituationTypes): Boolean;
{ ArtifactSituation: ---------------------------------------------------------
   Check to see if the given artifact has code for the given situation.
   If it does, it returns the number of the situation. Otherwise it returns
   zero.
---------------------------------------------------------------------------- }
   VAR
      i: Word;

   BEGIN
   ArtifactSituation:=(Sit IN ArtifactType^[Artfct[ArtID.Index]^.AType].Situations);
   END;  { ArtifactSituation }

PROCEDURE ActiveSituation(ArtID: IDNumber; Activator: Empire);
{ ActiveSituation: -----------------------------------------------------------
   Activates the given artifact.
---------------------------------------------------------------------------- }
   VAR
      R: RegisterArray;
      IP: Word;

   BEGIN
   WITH Artfct[ArtID.Index]^ DO
      BEGIN
      R[0].VType:=ScalarVRT;
      R[0].Scalar:=Word(ActiveSIT);

      R[1].VType:=IDVRT;
      R[1].ID:=ArtID;

      R[2].VType:=EmpVRT;
      R[2].Emp:=Activator;

      IP:=1;
      CodeInterpreter(ArtifactType^[AType].Code^,IP,V,R);
      END;
   END;  { ActiveSituation }

PROCEDURE CloseUpSituation(ArtID: IDNumber; Player: Empire);
   VAR
      R: RegisterArray;
      IP: Word;

   BEGIN
   WITH Artfct[ArtID.Index]^ DO
      BEGIN
      R[0].VType:=ScalarVRT;
      R[0].Scalar:=Word(CloseUpSIT);

      R[1].VType:=IDVRT;
      R[1].ID:=ArtID;

      R[2].VType:=EmpVRT;
      R[2].Emp:=Player;

      IP:=1;
      CodeInterpreter(ArtifactType^[AType].Code^,IP,V,R);
      END;
   END;  { CloseUpSituation }

END.
