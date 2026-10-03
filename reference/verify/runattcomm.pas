(* runattcomm.pas -------------------------------------------------------------
   Drives the real ATTCOMM.PAS GetGroups (Fleet Group Configuration) with a scripted key
   sequence, so LoadShips/ChangeGroupType/LoadTransports and GetGroups' own tail (compaction,
   then the auto-load pass) run exactly as in the game, nested procedures and all -- none of them
   can be called from outside GetGroups.

   A separate driver from runworld.pas, not a `case <domain>` there, for the same reason as
   runload.pas: AttComm's interface pulls in LoadSave/Scena/MapWind/..., and their unit
   initialization sections would run before every one of runworld's existing domains.

   Keys come from SWINDOWS.PAS's test hook (ScriptedActive/ScriptedKeys, see SWINDOWS.PAS.patch):
   GetCharacter, and so GetInputString (the "Add how many to this group:" prompt), read from the
   script instead of the keyboard. GetGroups is promoted to AttComm's INTERFACE by
   ATTCOMM.PAS.patch.

   CLI: `runattcomm case getgroups <tuple> <tuple> ...` (the first two words are ignored, so the
   C# side can call it the same way as every runworld domain). One output line per tuple.

   Tuple: fgt,hkr,jmp,jtn,pen,ssp,trn,men,nnj,k1,k2,... -- the fleet's ships (ShipTypes order) and
   its men/ninja cargo, then the key script as character ordinals (arrows are 200/208/203/205,
   Return 13, Esc 27; see EIO.PAS). The script must end in Esc.

   Output: `ngroups=<v>` then, for each returned group i, `g<i>_typ=<Ord>;g<i>_num=<v>;
   g<i>_gat=<v>;g<i>_gattyp=<Ord>` (Ord of AttackTypes, i.e. Types.PAS declaration order). ---- *)

PROGRAM RunAttComm;

USES Types, DataCnst, DataStrc, Galaxy, Int, Misc, PrimIntr, Environ, News, Mess, NPETypes, NPE,
     Orders, Fleet, Intrface, Strg, Attack, SWindows, LoadSave, AttComm;

CONST
   MaxFields = 128;

PROCEDURE RunCase(CONST arg: String);
   VAR
      vals: ARRAY[0..MaxFields-1] OF LongInt;
      n,i,startPos,code: Integer;
      tok: String;
      FltID: IDNumber;
      NoOfGp: Byte;
      Group: GroupArray;
      Quit: Boolean;

   BEGIN
   n:=0;
   startPos:=1;
   FOR i:=1 TO Length(arg)+1 DO
      IF (i>Length(arg)) OR (arg[i]=',') THEN
         BEGIN
         tok:=Copy(arg,startPos,i-startPos);
         Val(tok,vals[n],code);
         IF (code<>0) OR (n>=MaxFields) THEN
            BEGIN
            WriteLn(StdErr,'runattcomm: bad field "',tok,'" in "',arg,'"');
            Halt(1);
            END;
         Inc(n);
         startPos:=i+1;
         END;
   IF n<10 THEN
      BEGIN
      WriteLn(StdErr,'runattcomm: expected at least 10 fields, got ',n,' in "',arg,'"');
      Halt(1);
      END;

   New(Universe);
   FillChar(Universe^,SizeOf(Universe^),0);
   New(Universe^.Fleet[1]);
   FillChar(Universe^.Fleet[1]^,SizeOf(Universe^.Fleet[1]^),0);
   Universe^.Fleet[1]^.Ships[fgt]:=vals[0];
   Universe^.Fleet[1]^.Ships[hkr]:=vals[1];
   Universe^.Fleet[1]^.Ships[jmp]:=vals[2];
   Universe^.Fleet[1]^.Ships[jtn]:=vals[3];
   Universe^.Fleet[1]^.Ships[pen]:=vals[4];
   Universe^.Fleet[1]^.Ships[ssp]:=vals[5];
   Universe^.Fleet[1]^.Ships[trn]:=vals[6];
   Universe^.Fleet[1]^.Cargo[men]:=vals[7];
   Universe^.Fleet[1]^.Cargo[nnj]:=vals[8];
   SetOfActiveFleets:=[1];
   FltID.ObjTyp:=Flt;
   FltID.Index:=1;

   ScriptedKeys:='';
   FOR i:=9 TO n-1 DO
      ScriptedKeys:=ScriptedKeys+Chr(vals[i]);
   ScriptedPos:=0;
   ScriptedActive:=True;

   GetGroups(FltID,NoOfGp,Group,Quit);

   Write('ngroups=',NoOfGp);
   FOR i:=1 TO NoOfGp DO
      Write(';g',i,'_typ=',Ord(Group[i].Typ),';g',i,'_num=',Group[i].Num,
            ';g',i,'_gat=',Group[i].GAT,';g',i,'_gattyp=',Ord(Group[i].GATTyp));
   WriteLn;

   ScriptedActive:=False;
   Dispose(Universe^.Fleet[1]);
   Dispose(Universe);
   END;

VAR
   i: Integer;

BEGIN
IF (ParamCount<3) OR (ParamStr(1)<>'case') OR (ParamStr(2)<>'getgroups') THEN
   BEGIN
   WriteLn(StdErr,'usage: runattcomm case getgroups <tuple> [<tuple>...]');
   Halt(1);
   END;
FOR i:=3 TO ParamCount DO
   RunCase(ParamStr(i));
END.
