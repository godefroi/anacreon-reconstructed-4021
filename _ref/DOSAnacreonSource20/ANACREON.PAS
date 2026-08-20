(* ANACREON.PAS -------------------------------- TURBO Pascal 4.00 -------------

ANACREON Reconstruction 4021   v1.00

----------------------------------------------------------------------------- *)

PROGRAM Anacreon;

{$D+}

{$M 16384,100000,225000}

{$IFDEF Overlay}
{$F+}
{$ENDIF}

USES
   Strg,
   Overlay,
        OverInit,
        DOS2,
        System2,
   Crt,                                       { Standard CRT Library. }
   EIO,                                       { Extended IO Library. }
   WND,
   Types,
   DataStrc,                                  { Universe data structure. }
   Environ,
   PrimIntr,
   Intrface,
   News,
   TMA,                                       { Logo }
   Prolog,                                    { Prologue }
   NewGame,
   Update,
   LoadSave,
   Fleet,
   SBase,
   HlpWind,
   PlayTurn,                                  { PlayerTakesTurn }
   AttComm,
   FltComm,
   Design,
   ClsComm,
   MscComm,
   NPE;

{$IFDEF Overlay}
{$O PlayTurn}
{$O Prolog}
{$O TMA}
{$O Update}
{$O NewGame}
{$O AttComm}
{$O FltComm}
{$O Design}
{$O ClsComm}
{$O MscComm}
{$O NPE01}
{$O NPE02}
{$O NPE03}
{$O NPE04}
{$O NPEINTR}
{$ENDIF}

VAR
   ExitProgram,ExitGame: Boolean;
   Emp: Empire;

PROCEDURE InitializeOverlays(VAR Crash: Boolean);
   VAR
      Abort: Boolean;
      Line: LineStr;
      OvrBufferSize: LongInt;

   BEGIN
   {$IFDEF Overlay}
   IF NOT Crash THEN
      BEGIN
                { Overlays are initialized in OVERINIT. This function just checks
                  the error. }

      IF OvrResult<>0 THEN
         BEGIN
         CASE OvrResult OF
               ovrError: Line:='Overlay manager error.';
            ovrNotFound: Line:='Overlay file ANACREON.OVR not found.';
            ovrNoMemory: Line:='Not enough memory to load overlays.';
             ovrIOError: Line:='I/O ERROR loading ANACREON.OVR.';
            ELSE
               Line:='General error in overlay manager.';
            END;  { case }

         Abort:=False;
         AttentionWindow(Line,'(Unable to continue.)',Abort);
         Crash:=True
         END;
      END;
   {$ENDIF}
   END;  { InitializeOverlays }

PROCEDURE CheckMemory(VAR Crash: Boolean);
   VAR
      IgnoreWarning: Boolean;
      MemStr: String32;

   BEGIN
   IF NOT Crash THEN
      BEGIN
      IF MemAvail<32768 THEN
         BEGIN
         IgnoreWarning:=True;
         Str(MemAvail,MemStr);
         AttentionWindow('There are only '+MemStr+' bytes free!  Anacreon may crash.',
                         'Press <Esc> to ignore this warning.',IgnoreWarning);
         IF NOT IgnoreWarning THEN
            Crash:=True;
         END;
      END;
   END;  { CheckMemory }

PROCEDURE LoadConfigurationFile;
   BEGIN
   LoadConfiguration;

   IF (NOT UseColor) AND (Lo(LastMode)<>BW80) THEN
      ToggleColorBW
   ELSE IF UseColor AND (Lo(LastMode)=BW80) THEN
      ToggleColorBW;
   END;  { LoadConfigurationFile }

PROCEDURE Introduction;
   VAR
      Ch: Char;
      Abort,Crash: Boolean;
      Error: Word;
      MemStr: String8;

   { Introduction: MAIN PROCEDURE }
   BEGIN
   ClrScr;
   Crash:=False;
   InitializeOverlays(Crash);
   CheckMemory(Crash);

   IF NOT Crash THEN
      BEGIN
      LoadConfigurationFile;
      LoadHelpFile(HLP^);

      TMALogo;    { In TMA.PAS }
      Randomize;

      InitializeMainMenu;
      END
   ELSE
      BEGIN
      Halt(1);
      END;
   END;  { Introduction }

PROCEDURE CleanUp;
   BEGIN
   TextBackground(Black);
   TextColor(LightGray);
   Window(1,1,80,25);
   TextMode(StartUpMode);
   ClrScr;

   CloseHelpFile(HLP^);

   IF ConfigModified THEN
      SaveConfiguration;
   END;  { CleanUp }

PROCEDURE UpdateEmpireTimeLimit(Player: Empire);
   BEGIN
   WITH Universe^.EmpireData[Player] DO
      IF (1.0*TimeLeft+TimePerTurn) < MaxInt THEN
         TimeLeft:=TimeLeft+TimePerTurn;
   END;  { UpdateEmpireTimeLimit }

PROCEDURE SetUpTurn(Player: Empire);
   BEGIN
   {$IFDEF DebugNPE}
   IF NOT EmpirePlayer(Player) THEN
      BEGIN
      ImplementNPE(Player);
      END;
   {$ENDIF}

   ClearScoutSet(Player);
   ScoutFleets(Player);
   ScoutObjects(Player);
   UpdateProbes(Player);
   END;  { SetUpTurn }

FUNCTION NoMorePlayers: Boolean;
        VAR
                Emp: Empire;

        BEGIN
        Emp:=Empire1;

        WHILE (Emp<>Indep) AND ((NOT EmpireActive(Emp)) OR (NOT EmpirePlayer(Emp))) DO
                Inc (Emp);

        NoMorePlayers := (Emp = Indep);
        END;  { NoMorePlayers }

PROCEDURE UpdateTurn(VAR Player: Empire);
   BEGIN
   IF EmpireActive(Player) THEN
      BEGIN
      UpdateEmpireTimeLimit(Player);
      EraseNews(Player);          { clear news for next year. }
      IF NOT AsyncTurns THEN
         BEGIN
         UpdateAllFleets(Player,NextEmpire(Player));
         MovePlayerStarbases(NextEmpire(Player));
         END;
      END;

   EmpiresToMove:=EmpiresToMove-[Player];

   IF NOT AsyncTurns THEN
      BEGIN
      REPEAT
         Inc(Player);

         { Update the Universe }
         IF Player=Indep THEN
            BEGIN
            Player:=Empire1;
            ResetEmpiresToMove;
            UpdateUniverse;
            END;

         {$IFNDEF DebugNPE}
         IF EmpireActive(Player) AND (NOT EmpirePlayer(Player)) THEN
            BEGIN
            ClearScoutSet(Player);
            ScoutFleets(Player);
            ScoutObjects(Player);
            UpdateProbes(Player);
            ImplementNPE(Player);

            EraseNews(Player);
            UpdateAllFleets(Player,NextEmpire(Player));
            MovePlayerStarbases(NextEmpire(Player));
            END;
         {$ENDIF}

      {$IFDEF DebugNPE}
      UNTIL (EmpireActive(Player));
      {$ELSE}
      UNTIL (EmpireActive(Player) AND EmpirePlayer(Player));
      {$ENDIF}
      END
   ELSE
      BEGIN
      IF EmpiresToMove=[] THEN
         { ASSERT: All players done with turn }
         BEGIN
         ResetEmpiresToMove;

         { Non-player empires take their turn. }
         FOR Emp:=Empire1 TO Empire8 DO
            IF EmpireActive(Emp) AND (NOT EmpirePlayer(Emp)) THEN
               BEGIN
               ClearScoutSet(Player);
               ScoutFleets(Player);
               ScoutObjects(Player);
               UpdateProbes(Player);
               ImplementNPE(Emp);
               EraseNews(Emp);
               END;

         FOR Emp:=Empire1 TO Empire8 DO
            IF EmpireActive(Emp) THEN
               BEGIN
               UpdateAllFleets(Emp,Emp);
               MovePlayerStarbases(Emp);
               END;

         UpdateUniverse;
         END;

      Player:=Empire1;
      END;
   END;  { UpdateTurn }

PROCEDURE UpdateTurnAfterEmpireDestroyed(VAR Player: Empire);
   BEGIN
   IF NOT AsyncTurns THEN
      BEGIN
      UpdateAllFleets(Indep,NextEmpire(Player));
      MovePlayerStarbases(NextEmpire(Player));
      END;

   EmpiresToMove:=EmpiresToMove-[Player];

   IF NOT AsyncTurns THEN
      BEGIN
      REPEAT
         Inc(Player);

         { Update the Universe }
         IF Player=Indep THEN
            BEGIN
            Player:=Empire1;
            ResetEmpiresToMove;
            UpdateUniverse;
            END;

      {$IFDEF DebugNPE}
      UNTIL (EmpireActive(Player));
      {$ELSE}
      UNTIL (EmpireActive(Player) AND EmpirePlayer(Player));
      {$ENDIF}
      END
   ELSE
      BEGIN
      IF EmpiresToMove=[] THEN
         { ASSERT: All players done with turn }
         BEGIN
         ResetEmpiresToMove;

         { Non-player empires take their turn. }
         FOR Emp:=Empire1 TO Empire8 DO
            IF EmpireActive(Emp) AND (NOT EmpirePlayer(Emp)) THEN
               BEGIN
               ClearScoutSet(Player);
               ScoutFleets(Player);
               ScoutObjects(Player);
               UpdateProbes(Player);
               ImplementNPE(Emp);
               EraseNews(Emp);
               END;

         FOR Emp:=Empire1 TO Empire8 DO
            IF EmpireActive(Emp) THEN
               BEGIN
               UpdateAllFleets(Emp,Emp);
               MovePlayerStarbases(Emp);
               END;

         UpdateUniverse;
         END;

      Player:=Empire1;
      END;
   END;  { UpdateTurnAfterEmpireDestroyed }

PROCEDURE AutoBackup;
        VAR
                Error: Integer;
                Abort: Boolean;

        BEGIN
   IF AutoSave THEN
      BEGIN
      Error:=SaveGame(AddDefaultPath(SavDirect,FNameWithoutExt(CurrentGame)+'.BAK'));
      IF Error<>0 THEN
         BEGIN
         Abort:=False;
         AttentionWindow('AutoSave: Can''t save the game on default drive.',
                         '          Be sure to save the game manually.',Abort);
         END;
      END;
        END;  { AutoBackup }

{ Anacreon: MAIN PROGRAM }
BEGIN
Introduction;
ExitProgram:=False;
REPEAT
   Prologue(Player,ExitProgram);
   IF NOT ExitProgram THEN
      BEGIN
      REPEAT
         IF EmpirePlayer(Player) THEN     { I put in this conditional because if the first  }
           SetUpPlayer(Player,ExitGame)   { empire is a NPE it asks for the NPE's password. }
	 ELSE  { That is, it's a NPE's turn first }
           ExitGame:=False;
                        { If SetUpPlayer returns with Player not active, then the empire
                          was just destroyed. }
         IF (EmpireActive(Player)) AND (NOT ExitGame) THEN  { That is, player is active  }
            BEGIN                                           { and we aren't exiting game }
              IF NOT ReEnterGame THEN     
                SetUpTurn(Player)
              ELSE
                ReEnterGame:=False;
	      IF EmpirePlayer(Player) THEN  { This could only be false for a new game. }
                PlayerTakesTurn(Player,ExitGame);    { main routines }

              IF (NOT ExitGame) THEN
                UpdateTurn(Player)
              ELSE
                ReEnterGame:=TRUE;

              AutoBackup;
            END
         ELSE
            BEGIN
              IF (NOT EmpireActive(Player)) THEN
                BEGIN
                  IF (NoMorePlayers) THEN
                    BEGIN
                      ExitGame := TRUE;
                      DoNotSaveGame;
                    END
                  ELSE
                    UpdateTurnAfterEmpireDestroyed (Player);
                END
            END;
      UNTIL (AsyncTurns) OR (ExitGame);
      END;
UNTIL ExitProgram;

CleanUp;
END.  { Anacreon }
