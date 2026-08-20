(* SYSTEM2.PAS ----------------------------------------- TURBO Pascal 4.0 ------

   System Library   v1.0

   This UNIT contains miscellaneous procedures and functions that are used in
   every Pascal program.

----------------------------------------------------------------------------- *)

UNIT System2;

INTERFACE

USES Strg,DOS;

CONST
   SecondsPerDay = 86400;                       { Number of seconds in a day. }

TYPE
   MemoryArray = ARRAY [0..65534] OF Byte;
   MemoryArrayPtr = ^MemoryArray;

FUNCTION AbsoluteTime: LongInt;
FUNCTION FNameWithoutExt(Filename: String64): String64;
PROCEDURE AddDefaultExtension(VAR Filename: LineStr; Ext: String8);
PROCEDURE FillWord(VAR Dest; Count,Data: Word);
PROCEDURE ScanTextFile(VAR TextFile: TEXT; Strg: LineStr; CheckCase: Boolean);

IMPLEMENTATION

PROCEDURE AddDefaultExtension(VAR Filename: LineStr; Ext: String8);
   VAR
      DotPos: Word;

   BEGIN
   DotPos:=Pos('.',Filename);
   IF DotPos=0 THEN
      Filename:=Filename+'.'+Ext
   ELSE IF DotPos=Length(Filename) THEN
      Filename:=Copy(Filename,1,DotPos-1);
   END;  { AddDefaultExtension }

PROCEDURE ScanTextFile(VAR TextFile: TEXT; Strg: LineStr; CheckCase: Boolean);
{ ScanTextFile: ----------------------------------------------------------------
   This procedure scans an open text file for an occurrence of Strg. If the
   string is found, the file position is left at the next character (i.e. the
   first character not in the string).  If the string is not found (an error
   condition,) then the end of the file is reached.

   CheckCase can be IgnoreCase or CaseSignificant (defined in Strg).
------------------------------------------------------------------------------ }
   VAR
      Matching: Word;
      Found: Boolean;
      Buffer: ARRAY [0..80] OF Char;            { a circular buffer of 81 chrs }
      Pos,Head,Tail: Word;
      Ch: Char;

   PROCEDURE AddToBuffer(Ch: Char);
      BEGIN
      Buffer[Head]:=Ch;
      Head:=(Head+1) MOD 81;
      END;  { AddToBuffer }

   PROCEDURE DeleteFromBuffer;
      BEGIN
      Tail:=(Tail+1) MOD 81;
      END;  { DeleteFromBuffer }

   FUNCTION CharAt(Pos: Integer): Char;
      BEGIN      
      CharAt:=Buffer[(Tail+(Pos-1)) MOD 81];
      END;  { CharAt }

   { ScanTextFile: MAIN PROCEDURE }
   BEGIN
   Head:=0;
   Tail:=0;

   IF NOT CheckCase THEN
      AllUpCase(Strg);

   Pos:=0;
   Found:=True;
   WHILE (NOT EoF(TextFile)) AND (Pos<Length(Strg)) DO
      BEGIN
      Read(TextFile,Ch);
      IF NOT CheckCase THEN
         Ch:=UpCase(Ch);
      AddToBuffer(Ch);
      Inc(Pos);
      IF Ch<>Strg[Pos] THEN
         Found:=False;
      { INVARIANT: Pos = no. of characters read into buffer }
      END;
   { ASSERT: EoF OR Pos=Length(Strg) }

   IF (NOT EoF(TextFile)) AND (NOT Found) THEN
      BEGIN
      REPEAT
         { ASSERT: Buffer is full and does not match text }
         Read(TextFile,Ch);
         IF NOT CheckCase THEN
            Ch:=UpCase(Ch);
         DeleteFromBuffer;
         AddToBuffer(Ch);

         { check for match }
         Pos:=Length(Strg);
         WHILE (Pos>0) AND (Strg[Pos]=CharAt(Pos)) DO
            Dec(Pos);

      UNTIL (Pos=0) OR EoF(TextFile);
      END;
   END;  { ScanTextFile }

FUNCTION FNameWithoutExt(Filename: String64): String64;
   BEGIN
   IF Pos('.',Filename)=0 THEN
      FNameWithoutExt:=Filename
   ELSE
      FNameWithoutExt:=Copy(Filename,1,Pos('.',Filename)-1);
   END;  { FNameWithoutExt }

FUNCTION AbsoluteTime: LongInt;
{ AbsoluteTime: ----------------------------------------------------------------
   This routine returns the number of seconds elapsed since midnight.
------------------------------------------------------------------------------ }
   VAR
      Regs: Registers;

   BEGIN
   WITH Regs DO
      BEGIN
      AH:=$2C;

      Intr($21,Regs);
      { Returns:
           CH = Hours (0-23)
           CL = Minutes
           DH = Seconds
           DL = Hundreds of seconds }

      AbsoluteTime:=LongInt(3600)*CH + (60*CL) + DH;
      END;  { with scope }
   END;  { AbsoluteTime }

PROCEDURE FillWord(VAR Dest; Count,Data: Word);
{ FillWord: --------------------------------------------------------------------
   Fills the destination with the given number of Word.
   NOTE: You cannot use SizeOf(..) as Count because that gives the number
   of BYTES.  Use SizeOf(..) DIV 2.
------------------------------------------------------------------------------ }
   BEGIN
   INLINE(
      $C4/$BE/Dest/                       {  LES   DI,Dest[BP]  }
      $8B/$8E/Count/                      {  MOV   CX,Count[BP] }
      $8B/$86/Data/                       {  MOV   AX,Data[BP]  }
      $FC/                                {  CLD                }
      $F3/$AB);                           {  REP STOSW          }
   END;  { FillWord }

END.

