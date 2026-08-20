(* WNDTYPES.PAS ----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT WndTypes;

{$IFDEF Overlay}
{$F+}
{$ENDIF}

INTERFACE

CONST
   { Window commands }
   NoWCM             = 0;
   OpenWCM           = 1;
   ActivateWCM       = 2;
   CloseWCM          = 3;
   ScrllUpWCM        = 4;
   ScrllDownWCM      = 5;
   ScrllLeftWCM      = 6;
   ScrllRightWCM     = 7;
   CenterWCM         = 8;
   SelectWCM         = 9;
   PageUpWCM         = 10;
   PageDownWCM       = 11;
   PageLeftWCM       = 12;
   PageRightWCM      = 13;
   CursorDownWCM     = 14;
   CursorLeftWCM     = 15;
   CursorRightWCM    = 16;
   CursorUpWCM       = 17;
   EndWCM            = 18;
   DeActWCM          = 19;

TYPE
   InputCommandTypes = ( NoCommand,CommW,HelpW,ScanW,AllSW,MilSW,FltSW,
                         NewsW,EmpSW,ConsW,NameW,
                         OpenCom,CloseCom,ScrllUpCom,ScrllDownCom,
                         ScrllLeftCom,ScrllRightCom,SelectW,CentCom,
                         ScrllPUpCom,ScrllPDownCom,ScrllPLeftCom,ScrllPRightCom,
                         IndexCom );

IMPLEMENTATION

END.

