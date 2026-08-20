PROGRAM Compile1;

USES
   Strg;

CONST
   Bit: ARRAY [0..7] OF Word =
      ( 1,2,4,8,16,32,64,128 );

VAR
   Line: LineStr;
   Input: TEXT;
   i,j,BitValue: Word;

BEGIN
Assign(Input,'TEST.TXT');
Reset(Input);
REPEAT
   ReadLn(Input,Line);
   IF Line='BEGIN' THEN
      BEGIN
      REPEAT
         ReadLn(Input,Line);
         IF Line<>'END' THEN
            BEGIN
            AdjustString(Line,80);
            FOR i:=0 TO 9 DO
               BEGIN
               BitValue:=0;
               FOR j:=0 TO 7 DO
                  BEGIN
                  IF Line[i*8+j+1]<>' ' THEN
                     BEGIN
                     BitValue:=BitValue+Bit[j];
                     END;
                  END;
               Write(BitValue:6,',');
               END;
            WriteLn;
            END;
      UNTIL Line='END';
      WriteLn;
      WriteLn;
      END;
UNTIL Line='ALLEND';
Close(Input);
END.
