UNIT Strg;

INTERFACE

USES Dos;

CONST
   IgnoreCase = False;
   CaseSignificant = True;

TYPE

   { String definitions. }
   String6 = STRING [6];
   String8 = STRING [8];
   String16 = STRING [16];
   String24 = STRING [24];
   String32 = STRING [32];
   String50 = STRING [50];
   String64 = STRING [64];
   LineStr = STRING [80];
   String128 = STRING [128];
   MaxStr = STRING [255];

PROCEDURE AdjustString(VAR Word: MaxStr; Len: Byte);
PROCEDURE AllUpCase(VAR Strg: MaxStr);
FUNCTION Int2Str(Num: LongInt): STRING;
PROCEDURE Seconds2Str(Time: LongInt; VAR Strg: String16);
FUNCTION DateString(Date: DateTime): String16;
FUNCTION Noun(Line: LineStr): LineStr;
FUNCTION OrdinalString(Num: Word): LineStr;
PROCEDURE StringReplace(VAR Line: STRING; Find,Replace: STRING);
FUNCTION UpCaseStr(Strg: STRING): STRING;

(* -------------------------------------------------------------------------- *)

IMPLEMENTATION

FUNCTION Int2Str(Num: LongInt): STRING;
   VAR
      Temp: STRING;

   BEGIN
   Str(Num,Temp);
   Int2Str:=Temp;
   END;  { Int2Str }

FUNCTION OrdinalString(Num: Word): LineStr;
   VAR
      Ultim,Penultim: Word;

   BEGIN
   Ultim:=Num MOD 10;
   Penultim:=(Num MOD 100) DIV 10;

   IF (Penultim=1) OR (Ultim>3) OR (Ultim=0) THEN
      OrdinalString:='th'
   ELSE IF Ultim=3 THEN
      OrdinalString:='rd'
   ELSE IF Ultim=2 THEN
      OrdinalString:='nd'
   ELSE IF Ultim=1 THEN
      OrdinalString:='st'
   ELSE
      OrdinalString:='th';
   END;  { OrdinalString }

FUNCTION DateString(Date: DateTime): String16;
   VAR
      TempStr1,TempStr2: String16;

   BEGIN
   TempStr2:='';
   Str(Date.Month,TempStr1);
   IF Length(TempStr1)=1 THEN
      TempStr1:='0'+TempStr1;
   TempStr2:=TempStr1;
   Str(Date.Day,TempStr1);
   IF Length(TempStr1)=1 THEN
      TempStr1:='0'+TempStr1;
   TempStr2:=TempStr2+'-'+TempStr1;
   Str(Date.Year,TempStr1);
   DateString:=TempStr2+'-'+TempStr1;
   END;  { DateString }

FUNCTION Noun(Line: LineStr): LineStr;
   BEGIN
   IF UpCase(Line[1]) IN ['A','E','I','O','U','Y'] THEN
      Noun:='an '+Line
   ELSE
      Noun:='a '+Line;
   END;  { Noun }

PROCEDURE AllUpCase{VAR Strg: MaxStr};
{ AllUpCase:
   This procedure converts all the characters in 'Strg' to upper-case. }

   BEGIN
   Inline
      ($C4/$BE/Strg/             {      LES     DI,Strg[BP]             }
       $26/$8A/$0D/              {      MOV     CL,ES:[DI]              }
       $FE/$C1/                  {      INC     CL                      }
       $FE/$C9/                  { L1:  DEC     CL                      }
       $74/$13/                  {      JZ      L2                      }
       $47/                      {      INC     DI                      }
       $26/$80/$3D/$61/          {      CMP     ES:BYTE PTR [DI],'a'    }
       $72/$F5/                  {      JB      L1                      }
       $26/$80/$3D/$7A/          {      CMP     ES:BYTE PTR [DI],'z'    }
       $77/$EF/                  {      JA      L1                      }
       $26/$80/$2D/$20/          {      SUB     ES:BYTE PTR [DI],20H    }
       $EB/$E9);                 {      JMP     SHORT L1                }
                                 { L2:                                  }
   END;  { AllUpCase }

PROCEDURE AdjustString{VAR Word: MaxStr; Len: Byte};
{ AdjustString:
   This procedure adjusts 'Word' until it is of length 'Len'
   if Word is too long, it truncates, and if Word is too short it
   adds spaces at the end until it is of length Len. }

   VAR
      Temp: MaxStr;

   BEGIN
   Temp:=Word;
   FillChar(Word,Len+1,32);
   Word:=Temp;
   Word[0]:=Chr(Len);
   END;  { AdjustString }

PROCEDURE Seconds2Str{Time: LongInt; VAR Strg: String16};
{ Seconds2Str:
   This procedure formats a time given in seconds (by AbsoluteTime) into
   hours:minutes:seconds.  Format will be:  'hh:mm:ss'.  If hours is 0, then
   format will be 'mm:ss' only. }

   VAR
      Hours,Minutes,Seconds: LongInt;
      temp: String16;

   BEGIN
   Hours:=(Abs(Time) DIV 3600);
   Minutes:=(Abs(Time) DIV 60) MOD 60;
   Seconds:=Abs(Time) MOD 60;

   Str(Seconds,temp);
   IF Seconds<10 THEN
      temp:='0'+temp;
   Strg:=temp;

   Str(Minutes,temp);
   IF Minutes<10 THEN
      temp:='0'+temp;
   Strg:=temp+':'+Strg;

   IF Hours>0 THEN
      BEGIN
      Str(Hours,temp);
      Strg:=temp+':'+Strg;
      END;

   IF Time<0 THEN
      Strg:='-'+Strg;
   END;  { Seconds2Str }

PROCEDURE StringReplace(VAR Line: STRING; Find,Replace: STRING);
  var
    sp1 : integer;
    sp2 : integer;
    sp3 : integer;
    sp4 : integer;
    sp5 : integer;
    atemp,btemp,ctemp: STRING;

  begin
    sp1 := length(Line);
    sp2 := length(Find);
    sp3 := length(Replace);
    while pos(Find,Line) <> 0 do
    begin
      sp4 := pos(Find,Line);
      sp5 := sp1 - (sp4 + sp2) + 1;
         atemp := copy(Line,1,sp4-1);
         btemp := copy(Line,sp4+sp2,sp5);
         ctemp := atemp + Replace + btemp;
         Line := ctemp;
   end;
end;

FUNCTION UpCaseStr(Strg: STRING): STRING;
        BEGIN
        AllUpCase(Strg);
        UpCaseStr:=Strg;
        END;  { UpCaseStr }

END.
