(* COLORS.INC ------------------------------------------------------------------

   Color Table.

   Written for use with TURBO Pascal 4.0 by George P. Moromisato.
   Copyright (c) 1987 by Thinking Machine Associates.

----------------------------------------------------------------------------- *)

TYPE

   ColorRecord = RECORD
      SYSWBorder: Byte;          { Window border color. }
      SYSTBorder: Byte;          { Window text border color. }
      SYSHelpLine: Byte;         { Help line color. }
      SYSAttnWind: Byte;         { Attention window color. }
      SYSMenuBar: Byte;          { Menu bar color. }
      SYSMenu: Byte;             { Menu color. }
      SYSDispWind: Byte;         { Display window color. }
      SYSDispSelect: Byte;       { Display window selector color. }
      SYSDispHigh: Byte;         { Display window highlight color. }

      PlayerColor: Byte;
      PlayerFleetColor: Byte;
      MapBackground: Byte;
      EnemyColor: Byte;
      UnscoutedColor: Byte;
      NebulaColor: Byte;
      GridColor: Byte;

      TitleLine: Byte;
      Title1: Byte;              { Title page color 1 }
      Title2: Byte;              { Title page color 2 }

      CommWind: Byte;            { Command line window }
      StatWind1: Byte;
      StatWind2: Byte;
      StatWind3: Byte;
      HelpWind: Byte;
      AttackWind: Byte;
      GroupWind: Byte;
      EnemyWind: Byte;
      AboutWind: Byte;

      AttWind: Byte;             { Battle display window }
      AttWindLow: Byte;          { Battle display (low) }
      AttLRWind: Byte;           { Battle LR scan window }
      AttMesWind: Byte;          { Battle message window }
   END;  { ColorRecord }

CONST
   ColorScrColor: ColorRecord =
      ( SYSWBorder: 7;
        SYSTBorder: 71;
        SYSHelpLine: 4;
        SYSAttnWind: 7;
        SYSMenuBar: 79;
        SYSMenu: 7;
        SYSDispWind: 23;
        SYSDispSelect: 112;
        SYSDispHigh: 31;

        PlayerColor: 15;
        PlayerFleetColor: 15;
        MapBackground: 7;
        EnemyColor: 7;
        UnscoutedColor: 4;
        NebulaColor: 13;
        GridColor: 7;

        TitleLine: 79;
        Title1: 4;
        Title2: 15;

        CommWind: 15;
        StatWind1: 116;
        StatWind2: 4;
        StatWind3: 7;
        HelpWind: 27;
        AttackWind: 15;
        GroupWind: 23;
        EnemyWind: 4;
        AboutWind: 7;

        AttWind: 15;
        AttWindLow: 7;
        AttLRWind: 23;
        AttMesWind: 15
      );

   BWScrColor: ColorRecord =
      ( SYSWBorder: 7;
        SYSTBorder: 112;
        SYSHelpLine: 7;
        SYSAttnWind: 7;
        SYSMenuBar: 15;
        SYSMenu: 7;
        SYSDispWind: 7;
        SYSDispSelect: 112;
        SYSDispHigh: 15;

        PlayerColor: 112;
        PlayerFleetColor: 15;
        MapBackground: 7;
        EnemyColor: 7;
        UnscoutedColor: 7;
        NebulaColor: 7;
        GridColor: 7;

        TitleLine: 112;
        Title1: 7;
        Title2: 15;

        CommWind: 15;
        StatWind1: 15;
        StatWind2: 15;
        StatWind3: 15;
        HelpWind: 7;
        AttackWind: 15;
        GroupWind: 15;
        EnemyWind: 7;
        AboutWind: 7;

        AttWind: 15;
        AttWindLow: 7;
        AttLRWind: 112;
        AttMesWind: 7
      );

   MonoScrColor: ColorRecord =
      ( SYSWBorder: 7;
        SYSTBorder: 112;
        SYSHelpLine: 7;
        SYSAttnWind: 7;
        SYSMenuBar: 9;
        SYSMenu: 7;
        SYSDispWind: 7;
        SYSDispSelect: 112;
        SYSDispHigh: 15;

        PlayerColor: 112;
        PlayerFleetColor: 15;
        MapBackground: 7;
        EnemyColor: 7;
        UnscoutedColor: 7;
        NebulaColor: 7;
        GridColor: 7;

        TitleLine: 112;
        Title1: 7;
        Title2: 15;

        CommWind: 15;
        StatWind1: 15;
        StatWind2: 15;
        StatWind3: 15;
        HelpWind: 7;
        AttackWind: 15;
        GroupWind: 15;
        EnemyWind: 7;
        AboutWind: 7;

        AttWind: 15;
        AttWindLow: 7;
        AttLRWind: 112;
        AttMesWind: 7
      );
