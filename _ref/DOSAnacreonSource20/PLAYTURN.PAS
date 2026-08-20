(* PLAYTURN.PAS ----------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT PlayTurn;

{$IFDEF Overlay}
{$F+}
{$O+}
{$ENDIF}

INTERFACE

USES Crt,                                       { Standard CRT Library. }
     Strg,                                      { String Library. }
     Int,                                       { Integer Library. }
     System2,
     DOS2,
     EIO,                                       { Extended IO Library. }
     WND,
     Environ,
     PullDown,
     Types,
     Galaxy,
     DataStrc,                                  { Universe data structure. }
     DataCnst,
     Misc,                                      { Miscellaneous. }
     PrimIntr,                                  { Primitives. }
     SWindows,                                  { Status windows. }
     Display,                                   { Display screen. }
     Design,
     ClsComm,
     FltComm,
     Names,
     AttComm,
     MscComm,
     Orders,
     TMA,
     Constr;

PROCEDURE InitializeMainMenu;
PROCEDURE PlayerTakesTurn(Player: Empire; VAR ExitGame: Boolean);

IMPLEMENTATION

TYPE
   AllCommands = ( NullCom,               { no command        }
                   ErrorCom,              { command error     }
                   EndCom,                { END               }
                   ProdInfoCom,           { ABDicate          }
                   AttackCom,             { ATTack            }
                   CAbortCom,             { STOp construction }
                   CAddCom,               { CONstruct         }
                   ShellCom,              { SHELL             }
                   DesignateCom,          { DESignate         }
                   FLaunchCom,            { LAUnch fleet      }
                   FAbortCom,             { ABOrt fleet       }
                   FDestCom,              { DEStination       }
                   FTransCom,             { TRAnsfer fleet    }
                   FFuelCom,              { REFuel fleet      }
                   GrantIndepCom,         { INDependence      }
                   AboutCom,              { AboutAnacreon     }
                   LAMCom,                { LAUNCH missile    }
                   MReadCom,              { REAd messages     }
                   MSendCom,              { SENd messages     }
                   NAddCom,               { NAMe              }
                   NDelCom,               { DELete name       }
                   ProbeCom,              { PRObe             }
                   SelfSufCom,            { SELf sufficiency  }
                   TerraCom,              { Terraform         }
                   HrdCopyCom,            { PRINT             }
                   InfoCom,               { INFO              }
                   STechCom,              { TECHnology trade  }
                   ConStaCom,             { Build Status      }
                   SRMSweepCom,           { Mine sweeping     }
                   SelfDestCom,           { Self Destruct     }
                   DefnsCom,              { Defenses          }
                   HoloCom,               { Holocaust World   }
                   PauseCom,              { Pause Game        }
                   OrderCom,              { Fleet Orders      }
                   CancelOrdCom,          { Fleet Cancel      }
                   ArtfctCom,             { Empire Artifacts  }
                   TransCom,              { World Transact    }
		   AutoAttackCom,	  { Auto Attack       }
		   WarpLinkFreqCom,	  { Warp Link Freq    }

                   XXXCom );              { XXX               }

   ParameterTypes = ( NoParm,AllCoordParm,IDParm,IDParm2,XYParm,NameParm,
                      NewNameParm,TypParm,ConsParm,SwitchParm,EmpSetParm );

   Errors = ( NoError,NotAFlt,NotPartOfEmp,InUse,NotInUse,
              NotEnoughTri,NotMoving,NoPlaceToTra,NotScouted,NotKnown,
              NoTri,NoPlaceToRef,NotAWorld,NotConsSite,NoTarget, NotEmpty,

              NotEnoughTech,IsACap,CapDes,TerDes,OutDes,TerTech,TerCap,TerWrongCls,
              TerNotWarp,NoLAMs,NoLAMTargets,
              NameNotInUse,IllName,NameTooLong,DuplicateName,DupFltName,
              NoFltAtSite,NoRoomToBuild,NoStr1,NoOrders,NoFltToTrans,

              EmptQuad,NotName,NoObj,UndefCoord,
              IllSymb,IllCons,IllSwitch,IllEmpSet );

   SetOfErrors = SET OF Errors;

   ParmDataRecord = RECORD
      ParmTyp: ParameterTypes;
      Question: Byte;
      ErrorCond: SetOfErrors;
   END;

   ParameterDataRecord = RECORD
      NoOfParm: Byte;
      ParmData: ARRAY [1..3] OF ParmDataRecord;
   END;

   CommandDataRecord = record
      CommandStr: String8;
      Command: AllCommands;
   END;

   ParmArray = ARRAY [1..MaxNoOfParameters] OF String32;

CONST
   NoOfCommands = 25;
   NoOfQuestions = 27;

   QuestionArray: ARRAY [1..NoOfQuestions] OF String50 =
      ( 'Which fleet shall we use to attack? ',                 { 1 }
        'Which construction site do you wish to stop? ',        { 2 }
        'Sweep SRM field with which fleet? ',                   { 3 }
        'Production information for which world? ',             { 4 }
        'Which world do you wish to designate? ',               { 5 }
        'Which fleet do you want to order? ',                   { 6 }
        'What name shall we use for this fleet? ',              { 7 }
        'Where shall we deploy the fleet from? ',               { 8 }
        'What shall its destination be? ',                      { 9 }
        'Which fleet do you wish to abort/join? ',              { 10 }
        'Change the destination of which fleet/starbase? ',     { 11 }
        'What shall its new destination be? ',                  { 12 }
        'Which fleet do you wish to transfer? ',                { 13 }
        'Which fleet do you wish to refuel? ',                  { 14 }
        'Which world shall you grant independence to? ',        { 15 }
        'Which world do you wish to destroy? ',                 { 16 }
        'Cancel orders for which fleet? ',                      { 17 }
        'What do you wish to name? ',                           { 18 }
        'What shall its new name be? ',                         { 19 }
        'Which name do you wish to delete? ',                   { 20 }
        'Where shall we send a probe? ',                        { 21 }
        'Change ISSP of which world? ',                         { 22 }
        'Transact with which world? ',                          { 23 }
        'Which world do you wish to terraform? ',               { 24 }
        'Where shall we launch the LAMs from? ',                { 25 }
        'Close up on which world? ',                            { 26 }
        'Where shall we begin construction? '                   { 27 }
        );

   ParameterData: array[AllCommands] of ParameterDataRecord =
   ( ( NoOfParm: 0;  { Null }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { Error }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { End }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { ProdInfoCom }
       ParmData: ( ( ParmTyp: IDParm;
                     Question:  4;
                     ErrorCond: [NotPartOfEmp,NotAWorld] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Attack }
       ParmData: ( ( ParmTyp: IDParm;
                     Question:  1;
                     ErrorCond: [NotAFlt,NotPartOfEmp,NotInUse,NoTarget] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Abort Construction }
       ParmData: ( ( ParmTyp: IDParm;
                     Question:  2;
                     ErrorCond: [NotPartOfEmp,NotConsSite] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Add Construction }
       ParmData: ( ( ParmTyp: XYParm; 
                     Question:  27; 
                     ErrorCond: [NotEmpty] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { Shell }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Designate }
       ParmData: ( ( ParmTyp: IDParm;
                     Question:  5;
                     ErrorCond: [NotPartOfEmp,NotInUse,NotAWorld,CapDes,TerDes,OutDes] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 3;  { Deploy Fleet }
       ParmData: ( ( ParmTyp: NewNameParm;
                     Question:  7;
                     ErrorCond: [DupFltName] ),
                   ( ParmTyp: IDParm2;
                     Question:  8;
                     ErrorCond: [NotPartOfEmp,NotInUse] ),
                   ( ParmTyp: XYParm;
                     Question:  9;
                     ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Abort Fleet }
       ParmData: ( ( ParmTyp: IDParm;
                     Question: 10;
                     ErrorCond: [NotPartOfEmp,NotInUse,NotAFlt,NoPlaceToTra] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 2;  { Change Destination }
       ParmData: ( ( ParmTyp: IDParm;
                     Question: 11;
                     ErrorCond: [NotPartOfEmp,NotInUse,NotMoving] ),
                   ( ParmTyp: XYParm;
                     Question: 12;
                     ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Fleet Transfer }
       ParmData: ( ( ParmTyp: IDParm;
                     Question: 13;
                     ErrorCond: [NotAFlt,NotPartOfEmp,NotInUse,NoPlaceToTra] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Fleet Refuel }
       ParmData: ( ( ParmTyp: IDParm;
                     Question: 14;
                     ErrorCond: [NotAFlt,NotPartOfEmp,NotInUse,NoPlaceToRef,NoTri] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Grant Independence }
       ParmData: ( ( ParmTyp: IDParm;
                     Question: 15;
                     ErrorCond: [NotPartOfEmp,NotInUse,NotAWorld,IsACap] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { AboutAnacreon }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { LAMCom }
       ParmData: ( ( ParmTyp: IDParm;
                     Question: 25;
                     ErrorCond: [NotPartOfEmp,NotAWorld,NoLAMs] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { Read Message }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { Send Message }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 2;  { Add Name }
       ParmData: ( ( ParmTyp: AllCoordParm;
                     Question: 18;
                     ErrorCond: [] ),
                   ( ParmTyp: NewNameParm;
                     Question: 19;
                     ErrorCond: [DuplicateName] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Delete Name }
       ParmData: ( ( ParmTyp: NameParm;
                     Question: 20;
                     ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Probe }
       ParmData: ( ( ParmTyp: XYParm;
                     Question: 21;
                     ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Set ISSP }
       ParmData: ( ( ParmTyp: IDParm;
                     Question: 22;
                     ErrorCond: [NotPartOfEmp,NotAWorld] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Terraform }
       ParmData: ( ( ParmTyp: IDParm;
                     Question: 24;
                     ErrorCond: [NotPartOfEmp,NotInUse,NotAWorld,TerTech,TerCap,TerWrongCls,TerNotWarp] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { Print }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Close Up }
       ParmData: ( ( ParmTyp: IDParm; 
                     Question:  26; 
                     ErrorCond: [NotKnown] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { Tech Trade }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { Construction Status }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Mine sweeper }
       ParmData: ( ( ParmTyp: IDParm;
                     Question: 3;
                     ErrorCond: [NotPartOfEmp,NotInUse,NotAFlt,NoStr1] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { Self Destruct }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { DefnsCom }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { HoloCom }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { PauseCom }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Fleet Orders }
       ParmData: ( ( ParmTyp: IDParm;
                     Question: 6;
                     ErrorCond: [NotPartOfEmp,NotInUse,NotAFlt] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Cancel Orders }
       ParmData: ( ( ParmTyp: IDParm;
                     Question: 17;
                     ErrorCond: [NotPartOfEmp,NotInUse,NotAFlt,NoOrders] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { Empire Artifacts }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { World Transact }
       ParmData: ( ( ParmTyp: IDParm;
                     Question: 23;
                     ErrorCond: [NotAWorld,NoFltToTrans] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 1;  { Attack }
       ParmData: ( ( ParmTyp: IDParm;
                     Question:  1;
                     ErrorCond: [NotAFlt,NotPartOfEmp,NotInUse,NoTarget] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { Warp Link Freq }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ),

     ( NoOfParm: 0;  { Exit }
       ParmData: ( ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ),
                   ( ParmTyp: NoParm; Question:  0; ErrorCond: [] ) ) ) );

VAR
   Bar: MenuBar;

PROCEDURE GetParameters(VAR Comm: AllCommands; Parm: ParmArray;
                        VAR Obj1: IDNumber;
                        VAR Obj2: IDNumber;
                        VAR Coord: XYCoord;
                        VAR NewName: String16;
                        VAR Typ: WorldTypes;
                        VAR Cons: ConstrTypes;
                        VAR Setting: Boolean;
                        VAR EmpSet: EmpireSet);
{ GetParameters:
   This procedure will do two things.  First it will call determine which
   command was input and then it will input all the parameters for the given
   command. If parameters are not given then it will ask for them until they 
   are entered correctly or player exits (default).  If player exits in the 
   middle of input, Comm will be set to Null. } 

   label
      ExitLoop;

   VAR
      i: Integer;
      Error: Errors;

   PROCEDURE InterpretParameter(ParmTyp: ParameterTypes; Parm: String32;
                                VAR Obj1,Obj2: IDNumber;
                                VAR Coord: XYCoord;
                                VAR NewName: String16;
                                VAR Typ: WorldTypes;
                                VAR Cons: ConstrTypes;
                                VAR Setting: Boolean;
                                VAR EmpSet: EmpireSet;
                                VAR Error: Errors);
   { InterpretParameter:
      Depending on the type of parameter, this procedure will convert the
      string to one (or more) of the variables.  If there is an error
      in the conversion, then it will return it in Error. }

      VAR
         Loc: Location;
         Err: Boolean;

      PROCEDURE InterpretLocation(Parm: String32; VAR Loc: Location;
                                  VAR Error: Errors);
         BEGIN
         GetLocation(Player,Parm,Loc);
         IF SameID(Loc.ID,EmptyQuadrant) AND SameXY(Loc.XY,Limbo) THEN
            Error:=UndefCoord
			ELSE IF (Loc.ID.ObjTyp<>Void) AND (NOT Known(Player,Loc.ID)) THEN
				Error:=UndefCoord;
         END;  { InterpretLocation }

      PROCEDURE InterpretCoord(Parm: String32; VAR Coord: XYCoord;
                               VAR Error: Errors);
         VAR
            Loc: Location;

         BEGIN
         Coord:=Limbo;
         GetLocation(Player,Parm,Loc);
         IF NOT SameID(Loc.ID,EmptyQuadrant) THEN
            GetCoord(Loc.ID,Coord)
         ELSE
            IF NOT SameXY(Loc.XY,Limbo) THEN
               Coord:=Loc.XY
            ELSE
               Error:=UndefCoord;
         END;  { InterpretCoord }

      PROCEDURE InterpretNameID(Player: Empire; Parm: String32;
                                VAR NewName: String16; VAR Error: Errors);
         VAR
            NamePtr: NameRecordPtr;

         BEGIN
         Name2Index(Player,Parm,NamePtr);
         IF NamePtr=Nil THEN
            Error:=NotName
         ELSE
            NewName:=Parm;
         END;  { InterpretNameID }

      PROCEDURE InterpretNewName(Player: Empire; Parm: String32;
                                 VAR NewName: String16; VAR Error: Errors);
         BEGIN
         IF Length(Parm)>8 THEN
            Error:=NameTooLong
         ELSE IF (Parm='') OR (Pos(',',Parm)<>0) THEN
            Error:=IllName
         ELSE
            NewName:=Parm;
         END;  { InterpretNewName }

      { InterpretParameter: MAIN PROCEDURE }
      BEGIN
      Error:=NoError;

      CASE ParmTyp OF
         NoParm: BEGIN END;
         AllCoordParm: BEGIN
            InterpretLocation(Parm,Loc,Error);
            Obj1:=Loc.ID;
            Coord:=Loc.XY;
            END;
         IDParm: BEGIN
            InterpretObj(Parm,Obj1,Err);
            IF Err THEN
               Error:=NoObj;
            END;
         IDParm2: BEGIN
            InterpretObj(Parm,Obj2,Err);
            IF Err THEN
               Error:=NoObj;
            END;
         XYParm: InterpretCoord(Parm,Coord,Error);
         NameParm: InterpretNameID(Player,Parm,NewName,Error);
         NewNameParm: InterpretNewName(Player,Parm,NewName,Error);
      END;  { case }
      END;  { InterpretParameter }

   PROCEDURE CheckForErrors(ParmTyp: ParameterTypes;
                            ErrorCond: SetOfErrors;
                            Obj1,Obj2: IDNumber;
                            Coord: XYCoord;
                            NewName: String16;
                            Cons: ConstrTypes;
                            Setting: Boolean;
                            EmpSet: EmpireSet;
                            VAR Error: Errors);

      PROCEDURE CheckForObjectErrors(Obj: IDNumber; ErrorCond: SetOfErrors;
                                     VAR Error: Errors);
         LABEL
            NoErrorFound;

         VAR
            Status: Empire;
            Trillum: Resources;
            i,j: Byte;
            Obj2: IDNumber;
            XY1,XY2: XYCoord;
            SetOfFleets: FleetSet;
            Defns: DefnsArray;
            Ships: ShipArray;
            Tech: TechLevel;
            Techs: Technologyset;

         BEGIN
	 IF NotKnown IN ErrorCond THEN
	    BEGIN
	    IF ((Obj.ObjTyp = Flt) AND (NOT Obj.Index IN SetOfActiveFleets))
		OR (NOT (Obj.ObjTyp IN [Pln,Flt,Base]))
		OR ((NOT Known (Player,Obj)) AND (NOT Scouted (Player,Obj))) THEN
		BEGIN
		Error:=NotKnown;
		Exit;
		END;
	    END;

         IF InUse IN ErrorCond THEN
            BEGIN
            IF Obj.ObjTyp=Flt THEN
               IF (Obj.Index in SetOfActiveFleets) THEN
                  BEGIN
                  Error:=InUse;
                  Exit;
                  END;
            END;

         IF NotInUse IN ErrorCond THEN
            BEGIN
            IF Obj.ObjTyp=Flt THEN
               IF NOT (Obj.Index IN SetOfActiveFleets) THEN
                  BEGIN
                  Error:=NotInUse;
                  Exit;
                  END;
            END;

         IF NotMoving IN ErrorCond THEN
            BEGIN
            IF NOT (Obj.ObjTyp IN [Flt,Base]) THEN
               BEGIN
               Error:=NotMoving;
               Exit;
               END;
            END;

         IF NotAFlt IN ErrorCond THEN
            BEGIN
            IF Obj.ObjTyp<>Flt THEN
               BEGIN
               Error:=NotAFlt;
               Exit;
               END;
            END;

         IF NotAWorld IN ErrorCond THEN
            BEGIN
            IF NOT (Obj.ObjTyp IN [Pln,Base]) THEN
               BEGIN
               Error:=NotAWorld;
               Exit;
               END;
            END;

         IF NoFltToTrans IN ErrorCond THEN
            BEGIN
            GetCoord(Obj,XY1);
            GetFleets(XY1,SetOfFleets);
            SetOfFleets:=SetOfFleets*SetOfFleetsOf[Player];
            IF SetOfFleets=[] THEN
               BEGIN
               Error:=NoFltToTrans;
               Exit;
               END
            END;

         IF NotConsSite IN ErrorCond THEN
            BEGIN
            IF NOT (Obj.ObjTyp in [Con]) THEN
               BEGIN
               Error:=NotConsSite;
               Exit;
               END;
            END;

         IF NotScouted IN ErrorCond THEN
            BEGIN
            IF NOT Scouted(Player,Obj) THEN
               BEGIN
               Error:=NotScouted;
               Exit;
               END;
            END;

         IF NotPartOfEmp IN ErrorCond THEN
            BEGIN
            Status:=GetStatus(Obj);
            IF Status<>Player THEN
               BEGIN
               Error:=NotPartOfEmp;
               Exit;
               END;
            END;

         IF NoStr1 IN ErrorCond THEN
            BEGIN
            GetShips(Obj,Ships);
            IF Ships[ssp]<100 THEN
               BEGIN
               Error:=NoStr1;
               Exit;
               END;
            END;

         IF NoPlaceToTra IN ErrorCond THEN
            BEGIN
            GetCoord(Obj,XY1);
            GetObject(XY1,Obj2);
            IF NOT (Obj2.ObjTyp IN [Pln,Base]) THEN
               BEGIN
               GetFleets(XY1,SetOfFleets);
               SetOfFleets:=SetOfFleets-[Obj.Index];
               Obj2.ObjTyp:=Flt;
               Obj2.Index:=MaxNoOfFleets;

               WHILE (Obj2.Index>0) 
                     AND ((NOT (Obj2.Index IN SetOfFleets)) OR (NOT (Scouted(Player,Obj2)))) DO
                  Dec(Obj2.Index);

               IF Obj2.Index=0 THEN
                  BEGIN
                  Error:=NoPlaceToTra;
                  Exit;
                  END;
               END;
            END;

         IF NoPlaceToRef IN ErrorCond THEN
            BEGIN
            GetCoord(Obj,XY1);
            GetObject(XY1,Obj2);
            Status:=GetStatus(Obj2);
            Trillum:=GetTrillum(Obj2);
            IF (NOT (Obj2.ObjTyp IN [Pln,Base])) OR 
               (Status<>Player) OR 
               (Trillum=0) THEN
               BEGIN
               GetFleets(XY1,SetOfFleets);
               Obj2.ObjTyp:=Flt;
               FOR i:=1 TO MaxNoOfFleets DO
                  IF i IN SetOfFleets THEN
                     BEGIN
                     Obj2.Index:=i;
                     Status:=GetStatus(Obj2);
                     Trillum:=GetTrillum(Obj2);
                     IF (Status=Player) AND (Trillum>0) THEN
                        GOTO NoErrorFound;
                     END;
               Error:=NoPlaceToRef;
               Exit;

               NoErrorFound:
               END;
            END;

         IF NotEnoughTri IN ErrorCond THEN
            BEGIN
            Trillum:=GetTrillum(Obj);
            IF Trillum<TriToLaunch THEN
               BEGIN
               Error:=NotEnoughTri;
               Exit;
               END;
            END;

         IF NoTarget IN ErrorCond THEN
            BEGIN
            GetCoord(Obj,XY1);
            GetObject(XY1,Obj2);
            Status:=GetStatus(Obj2);
            IF (Status=Player) OR SameID(Obj2,EmptyQuadrant) THEN
               BEGIN
               GetFleets(XY1,SetOfFleets);
               SetOfFleets:=SetOfFleets-SetOfFleetsOf[Player];
               Obj2.ObjTyp:=Flt;
               Obj2.Index:=MaxNoOfFleets;
               WHILE (Obj2.Index>0) AND ((NOT (Obj2.Index IN SetOfFleets)) OR (NOT Scouted(Player,Obj2))) DO
                  Dec(Obj2.Index);

               { ASSERT: (Obj.Index=0) U (EnemyFleet О Scouted) }
               IF Obj2.Index=0 THEN
                  BEGIN
                  Error:=NoTarget;
                  Exit;
                  END;
               END;
            END;

         IF NoLAMs IN ErrorCond THEN
            BEGIN
            GetDefns(Obj,Defns);
            IF Defns[LAM]=0 THEN
               BEGIN
               Error:=NoLAMs;
               Exit;
               END;
            END;

         IF IsACap IN ErrorCond THEN
            BEGIN
            IF GetType(Obj)=CapTyp THEN
               BEGIN
               Error:=IsACap;
               Exit;
               END;
            END;

         IF CapDes IN ErrorCond THEN
            BEGIN
            IF GetType(Obj)=CapTyp THEN
               BEGIN
               Error:=CapDes;
               Exit;
               END;
            END;

         IF TerDes IN ErrorCond THEN
            BEGIN
            IF GetType(Obj)=TerTyp THEN
               BEGIN
               Error:=TerDes;
               Exit;
               END;
            END;

         IF OutDes IN ErrorCond THEN
            BEGIN
            IF (Obj.ObjTyp=Base) AND (GetBaseType(Obj)<>cmp) THEN
               BEGIN
               Error:=OutDes;
               Exit;
               END;
            END;

         IF TerTech IN ErrorCond THEN
            BEGIN
            GetEmpireTechnology(Player,Tech,Techs);
            IF NOT (ter IN Techs) THEN
               BEGIN
               Error:=TerTech;
               Exit;
               END;
            END;

         IF TerNotWarp IN ErrorCond THEN
            BEGIN
            IF (GetTech(Obj) < WrpTchLvl) THEN
               BEGIN
               Error:=TerNotWarp;
               Exit;
               END;
            END;

         IF TerCap IN ErrorCond THEN
            BEGIN
            IF GetType(Obj)=CapTyp THEN
               BEGIN
               Error:=TerCap;
               Exit;
               END;
            END;

         IF TerWrongCls IN ErrorCond THEN
            BEGIN
            IF GetClass(Obj) IN [ArtCls,TerCls] THEN
               BEGIN
               Error:=TerWrongCls;
               Exit;
               END;
            END;

         IF NoOrders IN ErrorCond THEN
            BEGIN
            IF (Obj.ObjTyp=Flt) AND (FleetNextStatement(Obj)=0) THEN
               BEGIN
               Error:=NoOrders;
               Exit;
               END;
            END;
         END;  { CheckForObjectErrors }

      procedure CheckForNewNameErrors(NewName: String16;
                                      ErrorCond: SetOfErrors;
                                      VAR Error: Errors);
         VAR
            Index: NameRecordPtr;
            Dummy: String32;
            Loc: Location;

         begin
         if DuplicateName in ErrorCond then
            begin
            Name2Index(Player,NewName,Index);
            if Index<>Nil then
               Error:=DuplicateName;
            end;
         IF DupFltName IN ErrorCond THEN
            BEGIN
            Name2Index(Player,NewName,Index);
            IF Index<>Nil THEN
               { ASSERT: Name is already defined. }
               BEGIN
               GetDefinedName(Player,Index,Dummy,Loc);
               IF (Loc.ID.ObjTyp<>Flt) AND (Loc.ID.ObjTyp<>DestFlt) THEN
                  { ASSERT: Name used for something else. }
                  Error:=DupFltName
               ELSE IF (Loc.ID.ObjTyp<>DestFlt) AND (Loc.ID.Index IN SetOfActiveFleets) THEN
                  { ASSERT: Name used for another fleet. }
                  Error:=DupFltName
               ELSE
                  { ASSERT: Name used by an inactive fleet. }
                  DeleteName(Player,NewName);
               END;
            END;
         end;  { CheckForNewNameErrors }

      procedure CheckForXYErrors(XY:  XYCoord;
                                      ErrorCond: SetOfErrors;
                                      VAR Error: Errors);
         VAR
            ObjID: IDNumber ;

         begin

         IF NotEmpty IN ErrorCond THEN
            BEGIN
              GetObject(XY,ObjID);
              IF NOT SameID(ObjID,EmptyQuadrant) THEN
                BEGIN
                Error:=NotEmpty;
                Exit;
                END;
            END;

         end;  { CheckForXYErrors }

      { CheckForErrors: MAIN PROCEDURE }
      begin
      if Error=NoError then
         begin
         case ParmTyp of
             IDParm : CheckForObjectErrors(Obj1,ErrorCond,Error);
            IDParm2 : CheckForObjectErrors(Obj2,ErrorCond,Error);
        NewNameParm : CheckForNewNameErrors(NewName,ErrorCond,Error);
             XYParm : CheckForXYErrors(Coord,ErrorCond,Error);
         end;  { case }
         end;  { if }
      end;  { CheckForErrors }

   PROCEDURE WriteError(Error: Errors; Parm: String32);
      VAR
         Address: String16;
         Line: LineStr;

      BEGIN
      Address:=MyLord(Player);
      Line:='';
      CASE Error OF
         NoError      : Line:='';
         NotAFlt      : Line:='* is not a fleet, @.';
         NotPartOfEmp : Line:='@, * is not part of '+EmpireName(Player);
         InUse        : Line:='* has already been deployed, @.';
         NotInUse     : Line:='@, * has not yet been deployed.';
         NotEnoughTri : Line:='There isn''t enough trillum on * to launch a fleet, @.';
         NotMoving    : Line:='What an idea!  Unfortunately, @, * is immobile.';
         NoPlaceToTra : Line:='There is no place to transfer to, @.';
         NoTri        : Line:='There is no trillum on *, @.';
         NoPlaceToRef : Line:='There is no place to refuel a fleet in this sector, @.';
         NotAWorld    : Line:='* is not a world, @.';
         NotConsSite  : Line:='* is not a construction site, @.';
         NoTarget     : Line:='* has no targets, @.';
	 NotEmpty     : Line:='* is not empty, @.';
	 NotKnown,
         NotScouted   : Line:='There''s no information available on *, @.';

         IllName      : Line:='I can not use "*" as a name, @.';
         DuplicateName: Line:='"*" is already defined, @.';
         DupFltName   : Line:='"*" is already defined, @.';
         NameTooLong  : Line:='That name is too long, please restrict yourself to 8 characters, @.';
         IsACap       : Line:='Not your capital, @.  If you wish for a coup d''grace, try abdicating.';
         CapDes       : Line:='@ is surely joking!  * is your capital!';
         TerDes       : Line:='@, * cannot be designated, for it is being terraformed.';
         OutDes       : Line:='@, * cannot be designated, for it has no industrial capacity.';
         TerTech      : Line:='We have not yet developed Terraforming technology, @.';
	 TerCap	      : Line:='That would be far too great a disruption to administration, @!';
	 TerWrongCls  : Line:='That class of planet is not suitable for terraforming, @.'; 
	 TerNotWarp   : Line:='@, a planet must be warp-level or above to be terraformed.'; 

         EmptQuad     : Line:='That is an empty sector, @.';
         NotName      : Line:='I have not heard of "*", @.';
         UndefCoord   : Line:='Those coordinates are undefined, @.';
         NoLAMs       : Line:='There are no LAMs at *, @.';
         NoStr1       : Line:='You need at least 100 starships to sweep an SRM field, @.';
         NoOrders     : Line:='* has no orders, @.';
         NoFltToTrans : Line:='@, we need to have a fleet in the sector before we can transact.';
      END;  { case }

      IF Line<>'' THEN
         BEGIN
         StringReplace(Line,'@',Address);
         StringReplace(Line,'*',Parm);
         WriteErrorMessage(Line);
         END;
      END;  { WriteError }

   { GetParameters: MAIN PROCEDURE }
   BEGIN
   { Initialize parameters }
   Obj1:=EmptyQuadrant;
   Obj2:=EmptyQuadrant;
   Coord:=Limbo;
   NewName:='';
   Typ:=IndTyp;
   Cons:=out;
   Setting:=True;
   EmpSet:=[];
	InCommand:=True;

   with ParameterData[Comm] do
      begin
      for i:=1 to NoOfParm do
         with ParmData[i] do
            begin
            IF Parm[i]='' THEN
               BEGIN
               REPEAT
                  InputParameter(QuestionArray[Question],Parm[i]);
                  WriteErrorMessage('');  { erase any old errors }

                  IF Parm[i]='' THEN
                     BEGIN
                     Comm:=NullCom;
                     WriteCommLine('');
                     GOTO ExitLoop;
                     END;

                  InterpretParameter(ParmTyp,Parm[i],Obj1,Obj2,Coord,
                                     NewName,Typ,Cons,Setting,EmpSet,Error);
                  CheckForErrors(ParmTyp,ErrorCond,Obj1,Obj2,Coord,NewName,Cons,
                                 Setting,EmpSet,Error);
                  IF Error<>NoError THEN
                     BEGIN
                     WriteError(Error,Parm[i]);
                     END;
               UNTIL Error=NoError;
               end
            else
               begin
               InterpretParameter(ParmTyp,Parm[i],Obj1,Obj2,Coord,
                                  NewName,Typ,Cons,Setting,EmpSet,Error);
               CheckForErrors(ParmTyp,ErrorCond,Obj1,Obj2,Coord,NewName,Cons,
                              Setting,EmpSet,Error);
               if Error<>NoError then
                  begin
                  WriteError(Error,Parm[i]);
                  Comm:=NullCom;
                  goto ExitLoop;
                  end;  { if }
               end;  { if }
            end;  { loop }
      end;  { with scope }

   ExitLoop:

	InCommand:=False;
   end;  { GetParameters }

PROCEDURE TrapCommandErrors(VAR Comm: AllCommands);
{ TrapCommandErrors: -----------------------------------------------------------
   This procedure is called right after a command is selected.  If a command
   is illegal for any reason, i.e too many fleets in space, no more probes,
   then this procedure will output an error message and set Comm to NullCom.
------------------------------------------------------------------------------ }
   VAR
      i,NoOfFleets: Word;

   BEGIN
   CASE Comm OF
      FLaunchCom: BEGIN
         i:=MaxNoOfFleets;
         NoOfFleets:=0;
         WHILE (i>0) AND (NoOfFleets<NoOfFleetsPerEmpire) DO
            BEGIN
            IF i IN SetOfFleetsOf[Player] THEN
               Inc(NoOfFleets);
            Dec(i);
            END;

         IF NoOfFleets>=NoOfFleetsPerEmpire THEN
            BEGIN
            WriteCommLine('I''m sorry, '+MyLord(Player)+', there are too many fleets in space already.');
            Comm:=NullCom;
            END;
         END;
   END;  { case }
   END;  { TrapCommandErrors }

PROCEDURE GetCommand(VAR Command: AllCommands; VAR Parm: ParmArray);
{ GetCommand:
   This procedure inputs a command string.  Each word is placed in one
   element of Parm.  }

   VAR
      ComStr: LineStr;
      Ch: Char;
      Comm: Word;

   { GetCommand: MAIN PROCEDURE }
   BEGIN
   ActivateWindow(CommandWindow);
   DisplayMenuBar(Bar);
   REPEAT
      IF OnBar THEN
         GetCharacter(AnyKey,NoCaseDistinct,Ch)
      ELSE
         BEGIN
         SuppressWindows:=True;
         GetCharacter(AnyKey,NoCaseDistinct,Ch);
         SuppressWindows:=False;
         END;

      ActivateMenuBar(Bar,Ch,Comm);
   UNTIL Comm<>0;

   Command:=AllCommands(Comm);
   FillChar(Parm,SizeOf(Parm),0);

{$IFDEF Demo}
   IF Command IN [CAbortCom,CAddCom,GrantIndepCom,LAMCom,MReadCom,MSendCom,
                  STechCom,ConStaCom,SRMSweepCom,SelfDestCom,DefnsCom,
                  SelfSufCom,ProdInfoCom,HoloCom,OrderCom,CancelOrdCom,
                  ArtfctCom,TransCom,AutoAttackCom] THEN
      BEGIN
      FeatureInActive;
      Command:=NullCom;
      END;
{$ENDIF}
   END;  { GetCommand }

PROCEDURE ShellCommand;
   VAR
      PausedTimeMark: LongInt;

   BEGIN
   StopClock(PausedTimeMark);
   DOSShell;
   StartClock(PausedTimeMark);
   END;  { ShellCommand }

PROCEDURE PlayerTakesTurn(Player: Empire; VAR ExitGame: Boolean);
   VAR
      Abort,EndTurn,TimeUp: Boolean;
      Parm: ParmArray;
      Comm: AllCommands;

      Loc: XYCoord;
      Obj1,Obj2: IDNumber;
      NameI: Byte;
      NewName: String16;
      Typ: WorldTypes;
      Cons: ConstrTypes;
      Setting: Boolean;
      EmpSet: EmpireSet;
      i: Byte;
      EmpN: String32;

   { PlayerTakesTurn: MAIN PROCEDURE }
   BEGIN
   ExitGame:=False;
   InitializeWindows(Player);
   DrawScreen(Year);
   EmpN:=EmpireName(Player);

   EndTurn:=False;  TimeUp:=False;
   TimeMark:=AbsoluteTime;
   ElapsedTime:=0;
   WriteMainHelpLine;

	ModalInput := False;
	InCommand := False;
	InProcess := False;

   REPEAT
      GetCommand(Comm,Parm);
      WriteErrorMessage('');   { erase old error messages }
      TrapCommandErrors(Comm);

      GetParameters(Comm,Parm,Obj1,Obj2,Loc,NewName,Typ,Cons,Setting,EmpSet);

		InProcess := True;
      CASE Comm OF
	              NullCom: ActivateLatestStatusWindow;
	{ Info } 
	             AboutCom: AboutAnacreon;
	             ShellCom: ShellCommand;
	{ Game }
	             PauseCom: PauseCommand;
	           HrdCopyCom: StatusHardcopy;
	               EndCom: EndTurn:=True;
	               XXXCom: ExitGame:=True;
	{ Empire }              
	             MSendCom: SendMessageCommand;
	             MReadCom: ReadMessageCommand;
	             STechCom: SellTechnology;
	      WarpLinkFreqCom: WarpLinkFrequencyCommand;

	{ Worlds }
	              InfoCom: CloseUpCom(Obj1);
	         DesignateCom: DesignateCommand(Obj1);
	          ProdInfoCom: ProductionCom(Obj1);
	           SelfSufCom: ChangeISSPCom(Obj1);
	              NAddCom: AddNameCommand(Obj1,Loc,NewName);
	              NDelCom: DeleteNameCommand(NewName);
	        GrantIndepCom: GrantIndependenceCommand(Obj1);
	          SelfDestCom: SelfDestructCommand;
		     TerraCom: TerraFormCommand(Obj1);
	{ Fleet }
	           FLaunchCom: LaunchFleetCommand(NewName,Obj2,Loc);
	             FDestCom: ChangeDestinationCommand(Obj1,Loc);
	            FTransCom: TransferFleetCommand(Obj1);
	            FAbortCom: AbortFleetCommand(Obj1);
	             FFuelCom: RefuelFleetCommand(Obj1);
	          SRMSweepCom: MineSweeperCommand(Obj1);
	             OrderCom: FleetOrdersCommand(Obj1);
	         CancelOrdCom: FleetCancelOrdersCommand(Obj1);
	             ProbeCom: LaunchProbeCommand(Loc);
	{ Build }
	            ConStaCom: ConstrStatusCommand;
	              CAddCom: ConstructCommand(Loc);
	            CAbortCom: AbortConstructionCommand(Obj1);
	{ Ministry of War }
	            AttackCom: AttackCommand(Obj1);
	        AutoAttackCom: AutoAttackCommand(Obj1);
	               LAMCom: LaunchLAM(Obj1);
	             DefnsCom: DefenseCommand;

(*ARTIFACTS
             TransCom: TransactionCommand(Obj1);
            ArtfctCom: ArtifactCommand;
              HoloCom: HolocaustCommand;
*)

     END;  { case }
      WriteMainHelpLine;

      IF Comm=EndCom THEN
         BEGIN
         Abort:=True;
         AttentionWindow('Are you ready to end your turn?',
                         'Press <Esc> to continue playing.',Abort);
         IF Abort THEN
            BEGIN
            Comm:=NullCom;
            EndTurn:=False;
            END;
         END
      ELSE IF Comm=XXXCom THEN
         BEGIN
         Abort:=True;
         AttentionWindow('Are you sure you want to exit?',
                         'Press <Esc> to continue playing.',Abort);
         IF Abort THEN
            BEGIN
            Comm:=NullCom;
            ExitGame:=False;
            END;
         END
      ELSE IF (SecondsLeft<=0) THEN
         BEGIN
         Abort:=False;
         AttentionWindow('I''m sorry, '+MyLord(Player)+', your time is up.',
                         'Press any key to end your turn.',Abort);
         TimeUp:=True;
         END;

		InProcess := False;

   UNTIL TimeUp OR EndTurn OR ExitGame;

   ElapsedTime:=AbsoluteTime-TimeMark;
   IF ElapsedTime<0 THEN
      ElapsedTime:=ElapsedTime+(24*60*60);
   SecondsLeft:=Round(GetTimeLeft(Player)-ElapsedTime);
   SetTimeLeft(Player,SecondsLeft);

   DeleteAllFleetDestNames(Player);

   CloseAllWindows;
   CloseWindow;
   CloseWindow;
   END;  { PlayerTakesTurn }

PROCEDURE InitializeMainMenu;
   BEGIN
   InitializeMenuBar(Bar);
   AddBarItem(Bar,1,'П','!',2,16);
   AddBarMenuItem(Bar,1,'About ANACREON','A',Word(AboutCom));
   AddBarMenuItem(Bar,1,'DOS shell','D',Word(ShellCom));

   AddBarItem(Bar,2,'Game','G',4,20);
                        {12345678901234567890}
   AddBarMenuItem(Bar,2,'Pause game    Alt-F1','P',Word(PauseCOM));
   AddBarMenuItem(Bar,2,'Status hardcopy','S',Word(HrdCopyCom));
   AddBarMenuItem(Bar,2,'Next turn','N',Word(EndCom));
   AddBarMenuItem(Bar,2,'Quit','Q',Word(XXXCom));

   AddBarItem(Bar,3,'Empire','E',10,20);
   AddBarMenuItem(Bar,3,'Send message','S',Word(MSendCom));
   AddBarMenuItem(Bar,3,'Read messages','R',Word(MReadCom));
   AddBarMenuItem(Bar,3,'Trade technology','T',Word(STechCom));
   AddBarMenuItem(Bar,3,'Link frequencies','L',Word(WarpLinkFreqCom));
   (*
   AddBarMenuItem(Bar,3,'Artifacts','A',Word(ArtfctCom));
   *)

   AddBarItem(Bar,4,'Worlds','W',18,20);
                        {12345678901234567890}
   AddBarMenuItem(Bar,4,'Close up         ды','C',Word(InfoCom));
   AddBarMenuItem(Bar,4,'Designate      Alt-D','D',Word(DesignateCom));
   AddBarMenuItem(Bar,4,'Production     Alt-P','P',Word(ProdInfoCom));
   AddBarMenuItem(Bar,4,'ISSP','I',Word(SelfSufCom));
   AddBarMenuItem(Bar,4,'Name           Alt-N','N',Word(NAddCom));
   AddBarMenuItem(Bar,4,'Remove name','R',Word(NDelCom));
   AddBarMenuItem(Bar,4,'Liberate','L',Word(GrantIndepCom));
   AddBarMenuItem(Bar,4,'Self-destruct','S',Word(SelfDestCom));
   AddBarMenuItem(Bar,4,'Terraform','T',Word(TerraCom));
   (*
   AddBarMenuItem(Bar,4,'Transact       Alt-T','T',Word(TransCom));
   *)

   AddBarItem(Bar,5,'Fleet','F',26,24);
                        {123456789012345678901234}
   AddBarMenuItem(Bar,5,'Deploy','D',Word(FLaunchCom));
   AddBarMenuItem(Bar,5,'Change destination','C',Word(FDestCom));
   AddBarMenuItem(Bar,5,'Transfer','T',Word(FTransCom));
   AddBarMenuItem(Bar,5,'Abort/join','A',Word(FAbortCom));
   AddBarMenuItem(Bar,5,'Refuel','R',Word(FFuelCom));
   AddBarMenuItem(Bar,5,'SRM sweep','S',Word(SRMSweepCom));
   AddBarMenuItem(Bar,5,'Orders','O',Word(OrderCom));
   AddBarMenuItem(Bar,5,'caNcel orders','N',Word(CancelOrdCom));
   AddBarMenuItem(Bar,5,'Probe              Alt-Z','P',Word(ProbeCom));

   AddBarItem(Bar,6,'Build','B',33,24);
   AddBarMenuItem(Bar,6,'Site status','S',Word(ConStaCom));
   AddBarMenuItem(Bar,6,'New construction site','N',Word(CAddCom));
   AddBarMenuItem(Bar,6,'Abort construction','A',Word(CAbortCom));

   AddBarItem(Bar,7,'Ministry of War','M',36,20);
   AddBarMenuItem(Bar,7,'Attack','A',Word(AttackCom));
	AddBarMenuItem(Bar,7,'auTo attack','T',Word(AutoAttackCom));
   AddBarMenuItem(Bar,7,'Launch LAMs','L',Word(LAMCom));
   (*
   AddBarMenuItem(Bar,7,'Holocaust world','H',Word(HoloCom));
   *)
   AddBarMenuItem(Bar,7,'Defenses','D',Word(DefnsCom));
   END;  { InitializeMainMenu }

END.
