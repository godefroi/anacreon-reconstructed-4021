(* DOS2.PAS -------------------------------------------- TURBO Pascal 4.0 ------

   DOS System Library   v1.0

   This UNIT contains high-level procedures and functions such as shell
   escapes.

----------------------------------------------------------------------------- *)

UNIT DOS2;

{$R-,S-,V-}

INTERFACE

USES Strg,
     Printer,
     CRT,
     DOS,
     EIO,
     WND,
     Menu;

CONST
   MaxNoOfDirEntries = 250;

TYPE
   PSPStructure = RECORD
      Int20H: Word;
      EndBlockSeg: Word;                           { End of alloc. block }
      R1: Byte;                                    { Reserved }
      LongCall: ARRAY [1..3] OF Byte;
      FuncDispSeg: Word;                           { Function dispatcher }
      ExitAddr: Pointer;                           { Exit addr (IP,CS) }
      CBrkAddr: Pointer;                           { Ctrl-C Addr (IP,CS) }
      HErrAddr: Pointer;                           { Hard error addr (IP,CS) }
      R2: ARRAY [1..22] OF Byte;                   { Reserved }
      EnvironSeg: Word;                            { Environment }
   END;

   FileLineFunction = FUNCTION(VAR Search: SearchRec; VAR Data): LineStr;

   DirMenuStructure = RECORD
      Path: LineStr;
      Mask: String16;
      DirX,DirY: Word;
      DirWidth,DirHeight: Word;
      DirCol,DirHCol: Word;
      FileLineFunc: FileLineFunction;
      FileLineData: Pointer;
      SelectionKeys: CharSet;

      Menu: MenuStructure;
      FilenameList: ARRAY [1..MaxNoOfDirEntries] OF String16;
   END;

VAR
   PSP: ^PSPStructure;

FUNCTION AddDefaultPath(Path,Filename: LineStr): LineStr;
PROCEDURE DOSSetDeviceBinaryMode(VAR F: File; VAR Error: Word);
PROCEDURE DOSShell;
PROCEDURE DOSCopyFile(Source: LineStr; VAR DestFile: FILE; VAR Error: Word);
PROCEDURE DOSCopyLine(Line: STRING; VAR DestFile: FILE; VAR Error: Word);
FUNCTION EnvironmentVar(EVar: LineStr): LineStr;
FUNCTION HomeDirectory: LineStr;
PROCEDURE PRNPrintLine(Line: LineStr; VAR Error: Word);
PROCEDURE PrintLine(Line: LineStr);
PROCEDURE PRNFormFeed;
PROCEDURE PRNReset;
PROCEDURE SelectFile(Path: LineStr; Mask: String16; VAR Filename: LineStr; VAR Abort: Boolean);

PROCEDURE DMSActivateDirMenu(VAR DMS: DirMenuStructure; Ch: Char; VAR FileSelected: Boolean);
PROCEDURE DMSCleanUp(VAR DMS: DirMenuStructure);
FUNCTION DMSDefaultFileLine(VAR Search: SearchRec; VAR Data): LineStr;
FUNCTION DMSDirMenuSelect(VAR DMS: DirMenuStructure): LineStr;
PROCEDURE DMSDisplayDirMenu(VAR DMS: DirMenuStructure);
PROCEDURE DMSInitialize(VAR DMS: DirMenuStructure; NewPath: LineStr; NewMask: String16);

FUNCTION ReadVariable(VAR AnyFile: FILE; VAR AnyVar; VarSize: Word): Word;
FUNCTION WriteVariable(VAR AnyFile: FILE; VAR AnyVar; VarSize: Word): Word;

IMPLEMENTATION

VAR
   PrinterOut: Boolean;

FUNCTION ReadVariable(VAR AnyFile: FILE; VAR AnyVar; VarSize: Word): Word;
   BEGIN
   {$I-}
   BlockRead(AnyFile,AnyVar,VarSize);
   {$I+}
   ReadVariable:=IOResult;
   END;  { ReadVariable }

FUNCTION WriteVariable(VAR AnyFile: FILE; VAR AnyVar; VarSize: Word): Word;
   BEGIN
   {$I-}
   BlockWrite(AnyFile,AnyVar,VarSize);
   {$I+}
   WriteVariable:=IOResult;
   END;  { WriteVariable }

FUNCTION AddDefaultPath(Path,Filename: LineStr): LineStr;

   BEGIN
   IF (Path='') OR (Pos(':',Filename)<>0) OR (Pos('\',Filename)<>0) THEN
      AddDefaultPath:=Filename
   ELSE IF (Path[Length(Path)]='\') THEN
      AddDefaultPath:=Path+Filename
   ELSE
      AddDefaultPath:=Path+'\'+Filename;
   END;  { AddDefaultPath }

PROCEDURE PRNPrintLine(Line: LineStr; VAR Error: Word);
   BEGIN
   IF Error=0 THEN
      BEGIN
      {$I-}
      Write(Lst,Line);
      {$I+}
      Error:=IOResult;
      END;
   END;  { PRNPrintLine }

PROCEDURE PRNReset;
   BEGIN
   PrinterOut:=False;
   END;  { ResetPrinter }

PROCEDURE PRNFormFeed;
   VAR
      Error: Word;

   BEGIN
   IF NOT PrinterOut THEN
      BEGIN
      {$I-}
      Write(Lst,#12);
      {$I+}
      Error:=IOResult;
      END;
   END;  { PRNFormFeed }

PROCEDURE PrintLine(Line: LineStr);
   VAR
      Count,Error: Integer;

   BEGIN
   IF NOT PrinterOut THEN
      BEGIN
      Count:=0;
      REPEAT
         {$I-}
         WriteLn(Lst,Line);
         {$I+}
         Error:=IOResult;
         IF Error<>0 THEN
            DOSErrorWindow(Error,'PRN');
         Inc(Count);
      UNTIL (Count=3) OR (Error=0);

      IF Count=3 THEN
         PrinterOut:=True;
      END;
   END;  { PrintLine }

FUNCTION HomeDirectory: LineStr;
{ HomeDirectory:
   Returns the directory in which the current program resides relative to the
   default directory. }
   TYPE
      EnvArray = ARRAY [0..32767] OF Char;

   VAR
      TempStr: LineStr;
      EnvPtr: ^EnvArray;
      i: Word;
      SingleZero,DoubleZero: Boolean;

   BEGIN
   IF Lo(DosVersion)>=3 THEN
      BEGIN
      EnvPtr:=Ptr(PSP^.EnvironSeg,0);
      DoubleZero:=False;
      SingleZero:=False;
      i:=0;
      REPEAT
         IF EnvPtr^[i]=Char(0) THEN
            BEGIN
            IF SingleZero THEN
               DoubleZero:=True
            ELSE
               SingleZero:=True;
            END
         ELSE
            SingleZero:=False;
         Inc(i);
      UNTIL DoubleZero;

      WHILE (EnvPtr^[i]<>Char(0)) DO
         Inc(i);

      Inc(i);
      TempStr:='';
      WHILE (EnvPtr^[i]<>Char(0)) DO
         BEGIN
         TempStr:=TempStr+EnvPtr^[i];
         Inc(i);
         END;

      i:=Length(TempStr);
      WHILE TempStr[i]<>'\' DO
         Dec(i);

      AllUpCase(TempStr);
      HomeDirectory:=Copy(TempStr,1,i);
      END
   ELSE
      BEGIN
      {$I-}
      GetDir(0,TempStr);
      {$I+}
      IF IOResult=0 THEN
         BEGIN
         AllUpCase(TempStr);
         HomeDirectory:=TempStr;
         END
      ELSE
         BEGIN
         HomeDirectory:='';
         END;
      END;
   END;  { HomeDirectory }

FUNCTION EnvironmentVar(EVar: LineStr): LineStr;
{ EnvironmentVar:
   Returns the value of the given environment variable or a null string if the
   variable is not found. }

   BEGIN
   EnvironmentVar:=GetEnv(EVar);
   END;  { EvironmentVar }

PROCEDURE DOSShell;
   VAR
      OldScr: ScrArray;
      OldWndX,OldWndY: Word;
      CommandStr: LineStr;

   BEGIN
   SaveScreen(OldScr);
   OldWndX:=WindMin;
   OldWndY:=WindMax;
   Window(1,1,80,25);
   ClrScr;

   CommandStr:=EnvironmentVar('COMSPEC');
   Exec(CommandStr,'');
   IF DosError<>0 THEN
      DOSErrorWindow(DosError,CommandStr);

   RestoreScreen(OldScr);
   WindMin:=OldWndX;
   WindMax:=OldWndY;
   END;  { DOSShell }

PROCEDURE DOSSetDeviceBinaryMode(VAR F: File; VAR Error: Word);
   VAR
      DestFile: FileRec ABSOLUTE F;
      R: Registers;
      DevInfo: Word;

   BEGIN
   IF Error=0 THEN
      BEGIN
      R.AH:=$44;                { function call $44: I/O Control }
      R.AL:=$00;                { function value $00: Get Device Info }
      R.BX:=DestFile.Handle;
      MSDos(R);
      DevInfo:=R.DX;

      IF (DevInfo AND 128)<>0 THEN
         BEGIN
         DevInfo:=Lo(DevInfo) OR 32;

         R.AH:=$44;
         R.AL:=$01;
         R.BX:=DestFile.Handle;
         R.DX:=DevInfo;
         MSDos(R);
         IF (R.Flags AND FCarry)<>0 THEN
            Error:=R.AX;
         END;
      END;
   END;  { DOSSetBinaryMode }

PROCEDURE DOSCopyLine(Line: STRING; VAR DestFile: FILE; VAR Error: Word);
   BEGIN
   {$I-}
   IF Error=0 THEN
      BEGIN
      BlockWrite(DestFile,Line[1],Length(Line));
      Error:=IOResult;
      END;
   {$I+}
   END;  { DOSCopyLine }

PROCEDURE DOSCopyFile(Source: LineStr; VAR DestFile: FILE; VAR Error: Word);
   CONST
      BufferSize = 32768;

   TYPE
      MemoryArray = ARRAY [0..BufferSize-1] OF Char;

   VAR
      SourceFile: FILE;
      Buffer: ^MemoryArray;
      NumWritten,NumRead,i: Word;

   BEGIN
   {$I-}
   IF Error=0 THEN
      BEGIN
      Assign(SourceFile,Source);
      Reset(SourceFile,1);
      Error:=IOResult;

      IF Error=0 THEN
         BEGIN
         GetMem(Buffer,BufferSize);
         REPEAT
            BlockRead(SourceFile,Buffer^,BufferSize,NumRead);
            Error:=IOResult;
            IF (Error=0) AND (NumRead<>0) THEN
               BEGIN
               BlockWrite(DestFile,Buffer^,NumRead,NumWritten);
               Error:=IOResult;
               END;
         UNTIL (Error<>0) OR (NumRead=0);
         FreeMem(Buffer,BufferSize);
         END;
      Close(SourceFile);
      END;
   {$I+}
   END;  { DOSCopyFile }

{$F+}
FUNCTION DMSDefaultFileLine(VAR Search: SearchRec; VAR Data): LineStr;
   VAR
      DotPos: Word;
      Line: LineStr;
      TempStr: String16;
      Date: DateTime;

   BEGIN
   DotPos:=Pos('.',Search.Name);
   IF DotPos<>0 THEN
      BEGIN
      Line:=Copy(Search.Name,1,DotPos-1);
      AdjustString(Line,9);
      Line:=Line+Copy(Search.Name,DotPos+1,16);
      END
   ELSE
      Line:=Copy(Search.Name,1,8);

   AdjustString(Line,15);
   Str(Search.Size:6,TempStr);
   Line:=Line+TempStr;

   AdjustString(Line,25);
   UnpackTime(Search.Time,Date);
   DMSDefaultFileLine:=Line+DateString(Date);
   END;  { DMSDefaultFileLine }
{$F-}

FUNCTION IsARootDir(Path: LineStr): Boolean;
   BEGIN
   IF (Length(Path)=3) THEN
      IsARootDir:=True
   ELSE
      IsARootDir:=False;
   END;  { IsARootDir }

PROCEDURE InitializeDirMenu(VAR DMS: DirMenuStructure; NewPath: LineStr; NewMask: String16);
   VAR
      Search: SearchRec;
      Filename: LineStr;
      NoOfFiles: Word;

   BEGIN
   WITH DMS DO
      BEGIN
      InitializeMenu(Menu);
      NoOfFiles:=0;
      Path:=FExpand(NewPath);
      IF Path[Length(Path)]<>'\' THEN
         Path:=Path+'\';
      Mask:=NewMask;

      IF IsARootDir(Path) THEN
         { ASSERT: path is root }
         BEGIN
         END
      ELSE
         BEGIN
         AddMenuLine(Menu,'<PARENT>');
         Inc(NoOfFiles);
         FilenameList[NoOfFiles]:='<PARENT>';
         END;

      FindFirst(Path+'*.*',AnyFile-VolumeID-SysFile,Search);
      WHILE (DosError=0) AND (NoOfFiles<MaxNoOfDirEntries) DO
         BEGIN
         Filename:=Search.Name;
         IF ((Search.Attr AND Directory)>0) AND (Filename[1]<>'.') THEN
            BEGIN
            Inc(NoOfFiles);
            Filename:='<'+Filename+'>';
            FilenameList[NoOfFiles]:=Filename;

            AddMenuLine(Menu,Filename);
            END;

         FindNext(Search);
         END;  { while }

      FindFirst(Path+Mask,AnyFile-VolumeID-SysFile-Directory,Search);
      WHILE (DosError=0) AND (NoOfFiles<MaxNoOfDirEntries) DO
         BEGIN
         Inc(NoOfFiles);
         Filename:=Search.Name;
         FilenameList[NoOfFiles]:=Filename;
         AddMenuLine(Menu,FileLineFunc(Search,FileLineData^));

         FindNext(Search);
         END;  { while }

      END;  { with scope }
   END;  { InitializeDirMenu }

PROCEDURE DMSInitialize(VAR DMS: DirMenuStructure; NewPath: LineStr; NewMask: String16);
   BEGIN
   WITH DMS DO
      BEGIN
      Path:=NewPath;
      Mask:=NewMask;

      DirX:=1;
      DirY:=2;
      DirWidth:=40;
      DirHeight:=23;
      DirCol:=LightGray;
      DirHCol:=C.SYSDispSelect;

      SelectionKeys:=[ReturnKey,' '];

      @FileLineFunc:=@DMSDefaultFileLine;
      FileLineData:=Nil;
      END;  { with scope }

   InitializeDirMenu(DMS,NewPath,NewMask);
   END;  { DMSInitialize }

FUNCTION DMSDirMenuSelect(VAR DMS: DirMenuStructure): LineStr;
   BEGIN
   WITH DMS DO
      BEGIN
      DMSDirMenuSelect:=Path+FilenameList[GetMenuSelect(Menu)];
      END;
   END;  { DMSDirMenuSelect }

PROCEDURE DMSActivateDirMenu(VAR DMS: DirMenuStructure; Ch: Char; VAR FileSelected: Boolean);
   VAR
      Filename: LineStr;
      PosX: Word;

   BEGIN
   FileSelected:=False;
   WITH DMS DO
      BEGIN
      IF Ch IN SelectionKeys THEN
         BEGIN
         Filename:=FilenameList[GetMenuSelect(Menu)];
         IF Filename='<PARENT>' THEN
            BEGIN
            PosX:=Length(Path)-1;
            WHILE Path[PosX]<>'\' DO
               Dec(PosX);

            Path:=Copy(Path,1,PosX);

            CleanUpMenu(Menu);
            InitializeDirMenu(DMS,Path,Mask);
            DisplayMenu(Menu,DirX,DirY,DirCol,DirHCol,DirWidth,DirHeight);
            END
         ELSE IF Filename[1]='<' THEN
            BEGIN
            Path:=Path+Copy(Filename,2,Length(Filename)-2)+'\';

            CleanUpMenu(Menu);
            InitializeDirMenu(DMS,Path,Mask);
            DisplayMenu(Menu,DirX,DirY,DirCol,DirHCol,DirWidth,DirHeight);
            END
         ELSE
            FileSelected:=True;
         END
      ELSE
         BEGIN
         ActivateMenu(Menu,Ch);
         END;
      END;  { with scope }
   END;  { DMSActivateDirMenu }

PROCEDURE DMSDisplayDirMenu(VAR DMS: DirMenuStructure);
   BEGIN
   WITH DMS DO
      BEGIN
      DisplayMenu(Menu,DirX,DirY,DirCol,DirHCol,DirWidth,DirHeight);
      END;
   END;  { DMSDisplayDirMenu }

PROCEDURE DMSCleanUp(VAR DMS: DirMenuStructure);
   BEGIN
   CleanUpMenu(DMS.Menu);
   END;  { DMSCleanUpDirMenu }

PROCEDURE SelectFile(Path: LineStr; Mask: String16; VAR Filename: LineStr; VAR Abort: Boolean);
{ SelectFile:
   Opens a window and lists the directory allowing the user to move through the
   structure. Returns the full path and filename of selected file. }

   VAR
      DMS: DirMenuStructure;
      FileSelected: Boolean;
      Ch: Char;

   { SelectFile: MAIN PROCEDURE }
   BEGIN
   DMSInitialize(DMS,Path,Mask);
   DMSDisplayDirMenu(DMS);

   REPEAT
      GetChoice(AnyKey,NoCaseDistinct,Ch);
      DMSActivateDirMenu(DMS,Ch,FileSelected);
   UNTIL (FileSelected) OR (Ch=EscKey);

   IF Ch=EscKey THEN
      BEGIN
      Abort:=True;
      Filename:='';
      END
   ELSE
      BEGIN
      Abort:=False;
      Filename:=DMSDirMenuSelect(DMS);
      END;

   DMSCleanUp(DMS);
   END;  { SelectFile }

BEGIN
PSP:=Ptr(PrefixSeg,0);
END.

