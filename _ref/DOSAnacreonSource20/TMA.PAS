(* TMA.PAS ---------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT TMA;

{$IFDEF Overlay}
{$F+}
{$O+}
{$ENDIF}

INTERFACE

USES Strg,
     CRT,
     EIO,
     WND;

CONST

{$IFDEF Demo}
   Version = '1.30d';
{$ELSE}
   Version = '2.0';
{$ENDIF}

   VersionNumber = 15;

PROCEDURE TMALogo;
PROCEDURE AboutAnacreon;

IMPLEMENTATION

CONST
   Logo: ARRAY [1..7] OF LineStr =
      ( ' €ﬂﬂﬂﬂﬂ€€ﬂﬂﬂﬂﬂ€ ﬂﬂ€€‹         €€ﬂﬂ         €        ',
        ' ﬂ     €€     ﬂ   €€€‹       €€€          €€€       ',
        '       €€         € €€‹     € €€         € ﬂ€€      ',
        '       €€         €  €€‹   €  €€        €   ﬂ€€     ',
        '       €€         €   €€‹ €   €€       €‹‹‹‹‹€€€    ',
        '       €€         €    €€€    €€      €       ﬂ€€   ',
        '     ‹‹€€‹‹     ‹‹€‹    €   ‹‹€€‹‹ ‹‹€‹      ‹‹€€€‹‹' );

PROCEDURE TMALogo;
{ TMALogo:
   This procedure clear the screen and displays the TMA logo. }

   VAR
      Len: Byte;
      i,j: Byte;
      Line: ARRAY [1..7] OF LineStr;
      Ch: Char;

   BEGIN
   ClrScr;
   SetCursor(NoCursor);

   Len:=Length(Logo[1]);
   FOR i:=1 TO 7 DO
      Line[i]:='';

   FOR i:=Len-1 DOWNTO 1 DO
      BEGIN
      FOR j:=1 TO 7 DO
         BEGIN
         Line[j]:=Logo[j][i]+Line[j];
         WriteString(Line[j],1,6+j,Red);
         END;
      END;

   FOR i:=1 TO 13 DO
      FOR j:=1 TO 7 DO
         WriteString(Logo[j],i,6+j,Red);

   WriteString('Presents',36,20,LightCyan);

   Wait(5,Ch);
   SetCursor(UnderlineCursor);
   ClrScr;
   END;  { TMALogo }

PROCEDURE AboutAnacreon;
   VAR
      Wind: WindowHandle;
      Ch: Char;

   BEGIN
   OpenWindow(1,2,80,23,NoBRD,'',C.AboutWind,7,Wind);
   WriteString('           ˛€€˛              ANACREON: Reconstruction 4021  ver '+Version,1,1,C.AboutWind);
   WriteString('            ﬁ›                     Released October 2, 2003',1,2,C.AboutWind);
   WriteString('            ±±                  ',1,3,C.AboutWind);
   WriteString('            ±±               After thirteen years, Anacreon has finally',1,4,C.AboutWind);
   WriteString('            ±±               been resurrected!  Thanks to George Moromisato',1,5,C.AboutWind);
   WriteString('      ﬁ    ‹±±‹    ›         for releasing the source code, and for his genius',1,6,C.AboutWind);
   WriteString('      ﬁﬂﬂ€€±≤€≤€€ﬂﬂ›         in creating the game in the first place.',1,7,C.AboutWind);
   WriteString('            ›€               ',1,8,C.AboutWind);
   WriteString('           ‹‹‹‹‹             See the changelog for version details, but most',1,9,C.AboutWind);
   WriteString('      ‹‹∞±±±±≤≤≤€€€‹‹        importantly, the following features have been',1,10,C.AboutWind);
   WriteString('   ∞∞∞∞∞∞∞±±±±±±≤≤€€€€€‹     added:',1,11,C.AboutWind);
   WriteString('  ∞∞∞∞∞∞∞±∞±±±±±±±≤≤€€€€‹    - Disrupters make your warp fleets go jumpspeed',1,12,C.AboutWind);
   WriteString(' ∞∞∞∞∞∞∞∞∞∞±∞±±±±≤±≤≤€€€€›   - Warp Link Frequencies allow you to share gates',1,13,C.AboutWind);
   WriteString(' ∞∞∞∞∞∞∞∞∞∞∞±±±±±±±≤±≤€€€€   - Fleets can now be ordered to do an SRMSweep',1,14,C.AboutWind);
   WriteString(' ﬁ∞∞∞∞∞∞∞∞∞∞±∞±±±±≤±≤≤€€€›   - Player empire order in scenarios can be random',1,15,C.AboutWind);
   WriteString('  ∞∞∞∞∞∞∞∞∞∞∞∞∞±±±±±≤≤€€ﬂ    - Scenario flavor text routines are more flexible',1,16,C.AboutWind);
   WriteString('   ∞∞∞∞∞∞∞∞∞∞∞±±±±≤≤≤€€ﬂ     - Added Terraforming as a gate-level technology',1,17,C.AboutWind);
   WriteString('      ﬂ∞∞∞∞∞∞∞∞±≤≤≤ﬂﬂ        - Fixed various bugs - see changlelog.txt',1,18,C.AboutWind);
   WriteString('           ﬂﬂﬂﬂ              ',1,19,C.AboutWind);
   WriteString('            ›€               Check out the Ardreil: Anacreon Reconstruction',1,20,C.AboutWind);
   WriteString('            ›€               project at sourceforge.net/projects/ardreil/ for',1,21,C.AboutWind);
   WriteString('            ›€               information on a network-aware Anacreon game',1,22,C.AboutWind);
   WriteString('            ›€                  Report bugs to Adam Luker (zot@aapc.com)',1,23,C.AboutWind);

   GetChoice(AnyKey,NoCaseDistinct,Ch);
   CloseWindow;
   OpenWindow(1,2,80,23,NoBRD,'',C.AboutWind,7,Wind);
   WriteString('',1,1,C.AboutWind);
   WriteString('',1,2,C.AboutWind);
   WriteString('  Legal stuff:',1,3,C.AboutWind);
   WriteString('',1,4,C.AboutWind);
   WriteString('  Based in part on Anacreon: Reconstruction 4021',1,5,C.AboutWind);
   WriteString('  copyright (c) 1988-2003 George Moromisato.',1,6,C.AboutWind);
   WriteString('  All rights reserved.',1,7,C.AboutWind);
   WriteString('',1,8,C.AboutWind);
   WriteString('  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"',1,9,C.AboutWind);
   WriteString('  AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE',1,10,C.AboutWind);
   WriteString('  IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE',1,11,C.AboutWind);
   WriteString('  ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE',1,12,C.AboutWind);
   WriteString('  LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR',1,13,C.AboutWind);
   WriteString('  CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF',1,14,C.AboutWind);
   WriteString('  SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS',1,15,C.AboutWind);
   WriteString('  INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN',1,16,C.AboutWind);
   WriteString('  CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)',1,17,C.AboutWind);
   WriteString('  ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE',1,18,C.AboutWind);
   WriteString('  POSSIBILITY OF SUCH DAMAGE.',1,19,C.AboutWind);
   WriteString('',1,20,C.AboutWind);
   WriteString('',1,21,C.AboutWind);
   WriteString('  (Of course, this game isn''t really just "based in part on" Anacreon. :)',1,22,C.AboutWind);
   WriteString('',1,23,C.AboutWind);

   GetChoice(AnyKey,NoCaseDistinct,Ch);
   CloseWindow;

   END;  { AboutAnacreon }

END.
