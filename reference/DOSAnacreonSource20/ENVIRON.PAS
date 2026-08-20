(* ENVIRON.PAS -----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT Environ;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

USES Crt,
     Strg,
     DOS2,
     EIO,
     WND,
     Types;

CONST
   HelpFilename = 'ANACREON.HLP';
   ConfFilename = 'ANACREON.CNF';

   CurrentGame: LineStr = 'ANACREON.SAV';
   ConfigModified: Boolean = False;
   HelpLoaded: Boolean = False;

   AutoSave: Boolean = True;
   PauseActive: Boolean = True;
   AsyncTurns: Boolean = False;
   ReEnterGame: Boolean = False;

VAR
   Year: Word;
   Player: Empire;
   EmpiresToMove: EmpireSet;
   ScenaFilename: String16;
   TimePerTurn: Word;

	HlpDirect: String64;
   SceDirect: String64;
   SavDirect: String64;
   UseColor: Boolean;

PROCEDURE FeatureInActive;
PROCEDURE LoadConfiguration;
FUNCTION LoadEnvironment(VAR SF: FILE): Word;
PROCEDURE ResetEmpiresToMove;
PROCEDURE SaveConfiguration;
FUNCTION SaveEnvironment(VAR SF: FILE): Word;

IMPLEMENTATION

USES
   PrimIntr;

PROCEDURE LoadConfiguration;
   VAR
      ConfigFile: FILE;
      Error: Word;
      Filename: LineStr;

   BEGIN
   Filename:=HomeDirectory+ConfFilename;
   Assign(ConfigFile,Filename);
   {$I-}
   Reset(ConfigFile,1);
   {$I+}

   Error:=IOResult;
   IF Error=0 THEN
      BEGIN
      {$I-}
      BlockRead(ConfigFile,SceDirect,64);
      BlockRead(ConfigFile,SavDirect,64);
		BlockRead(ConfigFile,HlpDirect,64);
      BlockRead(ConfigFile,UseColor,1);
      Close(ConfigFile);
      {$I+}
      Error:=IOResult;
      END;

   IF Error<>0 THEN
      BEGIN
      DOSErrorWindow(Error,Filename);
      SceDirect:='';
      SavDirect:='';
		HlpDirect:='';
      UseColor:=True;
      END;
   END;  { LoadConfiguration }

PROCEDURE SaveConfiguration;
   VAR
      ConfigFile: FILE;
      Error: Word;
      Filename: LineStr;

   BEGIN
   Filename:=HomeDirectory+ConfFilename;
   Assign(ConfigFile,Filename);
   {$I-}
   Rewrite(ConfigFile,1);
   {$I+}

   Error:=IOResult;
   IF Error=0 THEN
      BEGIN
      {$I-}
      BlockWrite(ConfigFile,SceDirect,64);
      BlockWrite(ConfigFile,SavDirect,64);
		BlockWrite(ConfigFile,HlpDirect,64);
      BlockWrite(ConfigFile,UseColor,1);
      Close(ConfigFile);
      {$I+}
      Error:=IOResult;
      END;

   IF Error<>0 THEN
      DOSErrorWindow(Error,Filename)
   ELSE
      ConfigModified:=False;
   END;  { SaveConfiguration }

FUNCTION LoadEnvironment(VAR SF: FILE): Word;
   VAR
      Error: Word;

   BEGIN
   Error:=ReadVariable(SF,Year,2);
   Error:=ReadVariable(SF,Player,1);
   Error:=ReadVariable(SF,EmpiresToMove,SizeOf(EmpiresToMove));
   Error:=ReadVariable(SF,ScenaFilename,SizeOf(ScenaFilename));
   Error:=ReadVariable(SF,TimePerTurn,2);
   Error:=ReadVariable(SF,AutoSave,1);
   Error:=ReadVariable(SF,AsyncTurns,1);
   Error:=ReadVariable(SF,PauseActive,1);
   Error:=ReadVariable(SF,ReEnterGame,1);

   LoadEnvironment:=Error;
   END;  { LoadEnvironment }

FUNCTION SaveEnvironment(VAR SF: FILE): Word;
   VAR
      Error: Word;

   BEGIN
   Error:=WriteVariable(SF,Year,2);
   Error:=WriteVariable(SF,Player,1);
   Error:=WriteVariable(SF,EmpiresToMove,SizeOf(EmpiresToMove));
   Error:=WriteVariable(SF,ScenaFilename,SizeOf(ScenaFilename));
   Error:=WriteVariable(SF,TimePerTurn,2);
   Error:=WriteVariable(SF,AutoSave,1);
   Error:=WriteVariable(SF,AsyncTurns,1);
   Error:=WriteVariable(SF,PauseActive,1);
   Error:=WriteVariable(SF,ReEnterGame,1);

   SaveEnvironment:=Error;
   END;  { SaveEnvironment }

PROCEDURE ResetEmpiresToMove;
   VAR
      Emp: Empire;

   BEGIN
   EmpiresToMove:=[];
   FOR Emp:=Empire1 TO Empire8 DO
      IF EmpireActive(Emp) AND (EmpirePlayer(Emp)) THEN
         EmpiresToMove:=EmpiresToMove+[Emp];
   END;  { ResetEmpiresToMove }

PROCEDURE FeatureInActive;
   VAR
      Wind: WindowHandle;
      Ch: Char;

   BEGIN
   OpenWindow(15,6,50,12,ThinBRD,'',LightGray,C.SYSWBorder,Wind);
   WriteString('That command is not active in this demo',2,2,LightGray);
   WriteString('version. If you want to purchase the full',2,3,LightGray);
   WriteString('program, write to:',2,4,LightGray);

   WriteString('TMA',17,6,LightGray);
   WriteString('15 Whittier Rd.',17,7,LightGray);
   WriteString('Natick, MA 01760',17,8,LightGray);
   GetChoice(AnyKey,NoCaseDistinct,Ch);
   CloseWindow;
   END;  { FeatureInActive }

END.
