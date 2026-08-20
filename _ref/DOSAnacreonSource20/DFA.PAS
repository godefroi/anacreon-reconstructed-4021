(* DFA.PAS ---------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT DFA;

INTERFACE

USES Strg;

CONST
   EoFCh = #026;
   ReturnCh = #013;
   LineFeedCh = #010;
	TabCh = #09;

	WhiteSpace: SET OF Char = [EoFCh,ReturnCh,LineFeedCh,TabCh,' '];

FUNCTION DFA1NextToken(VAR Input: TEXT; VAR Error: Boolean): LineStr;
FUNCTION DFANextInteger(VAR Input: TEXT; VAR Error: Boolean): LongInt;

IMPLEMENTATION

CONST
   EoFMessage = 'ERROR: Unexpected end of file.';
   LBrkMessage = 'ERROR: Unexpected line break.';

FUNCTION DFA1NextToken(VAR Input: TEXT; VAR Error: Boolean): LineStr;
{ DFA1NextToken: ---------------------------------------------------------------
   This function returns the next token in the input file.  If there is an 
   error, then Error is true and an error message is return.  The machine
   recognizes token of the following syntax:

   - Tokens are strings of characters separated by spaces or line breaks.
   - A token can contain spaces if it is surrounded by quotes (").
   - If a token needs to uses the quote (") or backslash (\) character,
     the character should be preceeded by a backslash (\).
   - Anything after a semi-colon (;) until the line break is a comment.

   legal tokens:
      ThisIsAToken
      Token
      1234
      !@#$%^&*
      "This is a token"                      ( This is a token )
      "\"This is a token\""                  ( "This is a token" )
      "This is a backslash (\\)"             ( This is a backslash (\) )

------------------------------------------------------------------------------ }
   TYPE
      StateTypes = ( Start, Comment, Quote, Slash1, Slash2, Token1, Token2, Final );

   VAR
      Ch: Char;
      Token: LineStr;
      State: StateTypes;

   BEGIN
   Error:=False;
   Token:='';
   State:=Start;

   REPEAT
      Read(Input,Ch);
      CASE State OF
         Start: BEGIN
            IF Ch=';' THEN
               State:=Comment
            ELSE IF Ch='"' THEN
               State:=Quote
            ELSE IF Ch='\' THEN
               State:=Slash2
            ELSE IF Ch=EoFCh THEN
               BEGIN
               Error:=True;
               Token:=EoFMessage;
               END
            ELSE IF (NOT (Ch IN WhiteSpace)) THEN
               BEGIN
               State:=Token2;
               Token:=Token+Ch;
               END;
            END;
         Comment: BEGIN
            IF (Ch=ReturnCh) OR (Ch=LineFeedCh) THEN
               State:=Start
            ELSE IF Ch=EoFCh THEN
               BEGIN
               Error:=True;
               Token:=EoFMessage;
               END;
            END;
         Quote: BEGIN
            IF Ch='"' THEN
               State:=Start
            ELSE IF Ch='\' THEN
               State:=Slash1
            ELSE IF (Ch=EoFCh) OR (Ch=ReturnCh) OR (Ch=LineFeedCh) THEN
               BEGIN
               Error:=True;
               Token:=LBrkMessage;
               END
            ELSE
               BEGIN
               State:=Token1;
               Token:=Token+Ch;
               END;
            END;
         Slash1: BEGIN
            IF (Ch=EoFCh) OR (Ch=LineFeedCh) OR (Ch=ReturnCh) THEN
               BEGIN
               Error:=True;
               Token:=LBrkMessage;
               END
            ELSE
               BEGIN
               State:=Token1;
               Token:=Token+Ch;
               END;
            END;
         Slash2: BEGIN
            IF (Ch=EoFCh) OR (Ch=LineFeedCh) OR (Ch=ReturnCh) THEN
               BEGIN
               Error:=True;
               Token:=LBrkMessage;
               END
            ELSE
               BEGIN
               State:=Token2;
               Token:=Token+Ch;
               END;
            END;
         Token1: BEGIN
            IF (Ch='"') OR (Ch=LineFeedCh) OR (Ch=ReturnCh) OR (Ch=EoFCh) THEN
               State:=Final
            ELSE IF Ch='\' THEN
               State:=Slash1
            ELSE
               Token:=Token+Ch;
            END;
         Token2: BEGIN
            IF (Ch IN WhiteSpace) THEN
               State:=Final
            ELSE IF Ch='\' THEN
               State:=Slash2
            ELSE
               Token:=Token+Ch;
            END;
      END;  { case }
   UNTIL (State=Final) OR Error;

   DFA1NextToken:=Token;
   END;  { DFA1NextToken }

FUNCTION DFANextInteger(VAR Input: TEXT; VAR Error: Boolean): LongInt;
   VAR
      Line: LineStr;
      Value: LongInt;
      Error2: Word;

   BEGIN
   Line:=DFA1NextToken(Input,Error);
   Val(Line,Value,Error2);
   IF Error2=0 THEN
      DFANextInteger:=Value
   ELSE
      Error:=True;
   END;  { DFANextInteger }

END.
