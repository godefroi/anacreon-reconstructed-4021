(* HLPWIND.PAS -----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT HlpWind;

{$IFDEF Overlay}
{$F+,O+}
{$ENDIF}

INTERFACE

USES
   Strg,
   CRT,
   EIO,
   WND,
   DOS2,
   Menu,
   Environ,
   WndTypes;

TYPE
   HelpPage = ARRAY [1..19] OF LineStr;

   HelpStructurePtr = ^HelpStructure;
   HelpStructure = RECORD
      PageNo: Word;
      Handle: WindowHandle;
      Col: Word;
      HelpFile: FILE OF HelpPage;
      HelpLoaded: Boolean;
   END;

VAR
   HLP: HelpStructurePtr;

PROCEDURE CloseHelpFile(VAR HLP: HelpStructure);
PROCEDURE HelpWindow(VAR HLP: HelpStructure; Comm: Word);
PROCEDURE InitializeHelpWindow(VAR HLP: HelpStructure);
PROCEDURE LoadHelpFile(VAR HLP: HelpStructure);

IMPLEMENTATION

CONST
   IndexPageNo: ARRAY [1..15] OF Byte =
      ( 11, 10,  6,  12, 2,
        13,  7,  7,  6,  5,
         2,  8,  9,  4,  3 );

   InitX = 1;
   InitY = 4;
   InitWidth = 80;
   InitHeight = 21;
   InitTitle = 'ANACREON: Help';

PROCEDURE LoadHelpFile(VAR HLP: HelpStructure);
   VAR
      Error: Word;
      Abort: Boolean;
      Filename: LineStr;

   BEGIN
   WITH HLP DO
      BEGIN
      Filename:=AddDefaultPath(HlpDirect,HelpFilename);
      Assign(HelpFile,Filename);
      {$I-}
      Reset(HelpFile);
      {$I+}
      Error:=IOResult;
      IF Error<>0 THEN
         BEGIN
         Abort:=False;
         AttentionWindow('Can''t find "'+Filename+'".','Press any key to continue.',Abort);
         HelpLoaded:=False;
         END
      ELSE
         HelpLoaded:=True;
      END;  { with scope }
   END;  { LoadHelpFile }
																				
PROCEDURE CloseHelpFile(VAR HLP: HelpStructure);
   BEGIN
   WITH HLP DO
      BEGIN
      IF HelpLoaded THEN
         Close(HelpFile);
      END;
   END;  { CloseHelpFile }

PROCEDURE DisplayPage(VAR HLP: HelpStructure);
   VAR
      PageToDisplay: HelpPage;
      i: Byte;

   BEGIN
   WITH HLP DO
      BEGIN
      ClrScr;
      IF HelpLoaded THEN
         BEGIN
         Seek(HelpFile,PageNo);
         Read(HelpFile,PageToDisplay);

         FOR i:=1 TO 19 DO
            WriteString(PageToDisplay[i],1,i,Col);
         END
      ELSE
         BEGIN
         WriteString('ANACREON Help file not available.',2,2,Col);

         WriteString('<F1>    Help Window',5,4,Col);
         WriteString('<F2>    Return to Menu',5,5,Col);
         WriteString('<F3>    Planetary Status Window',5,6,Col);
         WriteString('<F4>    Military Status Window',5,7,Col);
         WriteString('<F5>    Fleet Status Window',5,8,Col);
         WriteString('<F7>    News Window',5,9,Col);
         WriteString('<F8>    Empire Status Window',5,10,Col);
         WriteString('<F9>    Names Window',5,11,Col);
         WriteString('<F10>   Map Window',5,12,Col);
         END;
      END;
   END;  { DisplayPage }

PROCEDURE GetIndexEntry(VAR PageNo: Word);
   VAR
      Menu: MenuStructure;
      Ch: Char;

   BEGIN
   InitializeMenu(Menu);
   AddMenuLine(Menu,'Combat............................11');
   AddMenuLine(Menu,'Construction......................10');
   AddMenuLine(Menu,'Defenses...........................6');
   AddMenuLine(Menu,'Fleet Orders......................12');
   AddMenuLine(Menu,'Function Keys......................2');

   AddMenuLine(Menu,'ISSP..............................13');
   AddMenuLine(Menu,'Materials..........................7');
   AddMenuLine(Menu,'Raw Materials......................7');
   AddMenuLine(Menu,'Ships..............................6');
   AddMenuLine(Menu,'Technology Levels..................5');

   AddMenuLine(Menu,'Windows............................2');
   AddMenuLine(Menu,'   Map.............................8');
   AddMenuLine(Menu,'   World Status....................9');
   AddMenuLine(Menu,'World Classes......................4');
   AddMenuLine(Menu,'World Types........................3');

   DisplayMenu(Menu,35,2,LightGray,C.SYSDispSelect,40,17);
   WriteHelpLine(',:Move cursor <Enter>:Select <Esc>:Exit');

   REPEAT
      GetChoice(AnyKey,NoCaseDistinct,Ch);
      ActivateMenu(Menu,Ch);
   UNTIL Ch IN [ESCKey,ReturnKey];

   IF Ch=ReturnKey THEN
      PageNo:=IndexPageNo[GetMenuSelect(Menu)];

   CleanUpMenu(Menu);
   END;  { GetIndexEntry }

PROCEDURE OpenHelpWindow(VAR HLP: HelpStructure);
   BEGIN
   WITH HLP DO
      BEGIN
      IF Handle=0 THEN
         BEGIN
         OpenWindow(InitX,InitY,InitWidth,InitHeight,ThinBRD,InitTitle,Col,LightGray,Handle);
         DisplayPage(HLP);
         END
      ELSE
         ActivateWindow(Handle);
      END;  { with scope }
   END;  { OpenHelpWindow }

PROCEDURE CloseHelpWindow(VAR HLP: HelpStructure);
   BEGIN
   WITH HLP DO
      BEGIN
      IF Handle<>0 THEN
         BEGIN
         ActivateWindow(Handle);
         CloseWindow;
         Handle:=0;
         END;
      END;  { with scope }
   END;  { CloseHelpWindow }

PROCEDURE PageUp(VAR HLP: HelpStructure);
   BEGIN
   WITH HLP DO
      BEGIN
      IF HelpLoaded AND (PageNo<(FileSize(HelpFile)-1)) THEN
         BEGIN
         Inc(PageNo);
         DisplayPage(HLP);
         END;
      END;  { with scope }
   END;  { PageUp }

PROCEDURE PageDown(VAR HLP: HelpStructure);
   BEGIN
   WITH HLP DO
      BEGIN
      IF HelpLoaded AND (PageNo>1) THEN
         BEGIN
         Dec(PageNo);
         DisplayPage(HLP);
         END;
      END;  { with scope }
   END;  { PageDown }

PROCEDURE HelpIndex(VAR HLP: HelpStructure);
   BEGIN
   WITH HLP DO
      BEGIN
      IF HelpLoaded THEN
         BEGIN
         GetIndexEntry(PageNo);
         DisplayPage(HLP);
         END;
      END;
   END;  { HelpIndex }

PROCEDURE HelpWindow(VAR HLP: HelpStructure; Comm: Word);

   { HelpWindow: MAIN PROCEDURE }
   BEGIN
   CASE Comm OF
              CloseWCM: CloseHelpWindow(HLP);
               OpenWCM: OpenHelpWindow(HLP);

             PageUpWCM,
            ScrllUpWCM,
           CursorUpWCM: PageDown(HLP);

           PageDownWCM,
          ScrllDownWCM,
         CursorDownWCM: PageUp(HLP);
             CenterWCM,
                EndWCM: HelpIndex(HLP);
   END;  { case }
   END;  { HelpWindow }

PROCEDURE InitializeHelpWindow(VAR HLP: HelpStructure);
   BEGIN
   WITH HLP DO
      BEGIN
      Handle:=0;
      PageNo:=1;
      Col:=C.HelpWind;
      END;
   END;  { InitializeHelpWindow }

BEGIN
New(HLP);
END.
