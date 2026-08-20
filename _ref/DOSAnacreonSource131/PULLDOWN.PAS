(* PULLDOWN.PAS ---------------------------------------- TURBO Pascal 4.0 ------

   Pull-Down Menu Library   v1.0

   This UNIT implements a pull down menu interface that can be used with EIO 
   and WND to create a windowed program.

   A menu bar is a structure that defines a multiple-menu, top-line bar that 
   serves as a user interface.  A bar is compose of 1 to 10 menus, each with 
   a unique name and first letter.  Each menu can have any number of items, 
   each with a unique name and first letter.  (Item names cannot conflict 
   with menu names, although they can conflict with items in other menus.)
   The user can use the interface in two ways:

   1. The cursor keys allow the user to move a highlighted cursor from item to 
      item and from menu to menu.  Pressing <Return> selects a particular 
      item. 

   2. Pressing the first letter of any of the menus will pull down that menu.  
      Once in a menu, pressing the first letter for a particular item will 
      select it. 

   Any combination of the two methods can be used to select an item.

   USAGE:

   1. Declare a variable Bar of type MenuBar.

   2. Call InitializeMenuBar and pass Bar as a parameter. 

   3. For every menu, call AddBarItem with the appropriate parameters. Add the 
      menus in order, starting with the left-most. 

   4. For each menu, add all the items by calling AddBarMenuItem for each 
      item.  The activation key is the character that will select this item 
      when in the menu.  The return code is the number that you want the 
      system to return when the user selects this item. 

   5. Call DisplayMenuBar.

   6. Each time the user hits a key that is meant to go to the menu bar, call 
      ActivateMenuBar and pass it the menu bar (Bar) the character that the 
      user pressed, and a VAR variable of type Word.  The routine will return 
      the return code of the command that the user selected in the VAR 
      variable.

   INTERFACE:

   AddBarItem(VAR Bar: MenuBar; BarMenu: Word;          PROCEDURE
              NewLine: String16; NewKey: Char;
              NewMenuX: Word; NewMenuLen: Word)
      This routines adds a new menu to the bar.
         o Bar is a properly initialized variable of type MenuBar.
         o BarMenu is the number of the menu to add (starts with 1.)
         o NewLine is the title of the menu.
         o NewKey is the character key that will activate (pull down) the menu 
           (usually the first letter of the title.)
         o NewMenuX is the x coordinate at which the pull down menu should be 
           placed.  (The title is placed to the right of the last menu, 
           separated by 2 spaces.) 
         o NewMenuLen is the width of the pull down area, not including the 
           spaces for a border.

   AddBarMenuItem(VAR Bar: MenuBar; BarMenu: Word;      PROCEDURE
                  NewLine: String32; NewKey: Char; 
                  NewReturn: Word)
      AddBarMenuItem add a menu item to the given menu.
         o Bar is a properly initialized variable of type MenuBar.
         o BarMenu is the number of the menu to which the item should be 
           added.
         o NewLine is a string indicating the item to be added.
         o NewKey is the character that will activate this item, usually the 
           first letter of NewLine.
         o NewReturn is the code returned by ActivateMenuBar if this item is 
           selected.

   ActivateMenuBar(VAR Bar: MenuBar;                    PROCEDURE
                      Ch: Char; VAR Comm: Word)
      Once a menu has been created with AddBarItem and AddBarMenuItem, this 
      procedure will handle user input.  Every time that the user presses a 
      key that should be directed to the menu bar, this procedure should be 
      called.  If the user has made a selection, then Comm will return the 
      command code that the user has selected.  NOTE: Although other 
      procedures can be called while the user is making menu selections, the 
      programmer must make sure that a pulled-down menu is not overwritten.  
      The variable OnBar is False when the user has pulled down a menu.  It is 
      recommended that no other activities take place while OnBar is False, as 
      ActivateMenuBar expects its menu to be intact. 


   ChangeMenuLine(VAR Bar: MenuBar;                     PROCEDURE
                  BarMenu,Item: Word; 
                  NewLine: String32)
      Menu item lines can be changed with this procedure.  This capability is 
      useful is the programmer wants to display so information on the menu 
      bar.  For example, an entry in a menu might toggle a certain condition.  
      The menu line could reflect that condition by changing the line 
      everytime that the condition was toggled.

   CleanUpMenuBar(VAR Bar: MenuBar)                     PROCEDURE
      Since menu bars are kept on the heap, this routine should be called 
      after a procedure is done with a menu bar.  This will totally erase the 
      structure of the given menu bar, and return any memory used to the 
      system.

   DisplayMenuBar(VAR Bar: MenuBar)                     PROCEDURE
      This procedure will draw the bar (not the menus) at the top of the 
      screen.  This procedure can be called as needed to re-draw the bar.

   InitializeMenuBar(VAR Bar: MenuBar)                  PROCEDURE
      Before attempting to call any other routine, the programmer must 
      initialize a MenuBar type variable.

   OnBar: Boolean                                       VARIABLE
      OnBar is True when the menu bar cursor is on the bar, and False when it 
      is in a menu below.  See ActivateMenuBar.

----------------------------------------------------------------------------- *)

UNIT PullDown;

INTERFACE

USES Strg,
     CRT,
     EIO;

TYPE
   MenuItemPtr = ^MenuItem;
   MenuItem = RECORD
      Line: String32;
      Key: Char;
      Return: Word;
      Active: Boolean;
      Next: MenuItemPtr;
      Prev: MenuItemPtr;
   END;  { MenuItem }

   BarItem = RECORD
      Line: String16;
      Key: Char;
      Active: Boolean;
      Menu: MenuItemPtr;
      BarX: Word;
      MenuX: Word;
      MenuLen: Word;
      NoOfItems: Word;
   END;  { BarItem }
   MenuBar = ARRAY [1..10] OF BarItem;

VAR
   OnBar: Boolean;

PROCEDURE AddBarItem(VAR Bar: MenuBar; BarMenu: Word;
                     NewLine: String16; NewKey: Char;
                     NewMenuX: Word; NewMenuLen: Word);
PROCEDURE AddBarMenuItem(VAR Bar: MenuBar; BarMenu: Word;
                         NewLine: String32; NewKey: Char; NewReturn: Word);
PROCEDURE ActivateMenuBar(VAR Bar: MenuBar; Ch: Char; VAR Comm: Word);
PROCEDURE ChangeMenuLine(VAR Bar: MenuBar; BarMenu,Item: Word; NewLine: String32);
PROCEDURE CleanUpMenuBar(VAR Bar: MenuBar);
PROCEDURE ClearMenuBar(VAR Bar: MenuBar);
PROCEDURE DisplayMenuBar(VAR Bar: MenuBar);
PROCEDURE HighlightMenuBar(VAR Bar: MenuBar);
PROCEDURE InitializeMenuBar(VAR Bar: MenuBar);

IMPLEMENTATION

VAR
   BC: Word;
   MC: MenuItemPtr;
   MenuY: Word;
   PullDownArea: SaveAreaRecord;

PROCEDURE InitializeMenuBar(VAR Bar: MenuBar);
   BEGIN
   FillChar(Bar,SizeOf(Bar),0);
   END;  { InitializeMenuBar }

PROCEDURE AddBarItem(VAR Bar: MenuBar;
                     BarMenu: Word; NewLine: String16;
                     NewKey: Char; NewMenuX: Word; NewMenuLen: Word);
   BEGIN
   WITH Bar[BarMenu] DO
      BEGIN
      Line:=NewLine;
      Key:=NewKey;
      Active:=True;
      Menu:=Nil;
      IF BarMenu=1 THEN
         BarX:=1
      ELSE
         BarX:=Bar[BarMenu-1].BarX+Length(Bar[BarMenu-1].Line)+2;
      MenuX:=NewMenuX;
      MenuLen:=NewMenuLen;
      NoOfItems:=0;
      END;
   END;  { AddBarItem }

PROCEDURE AddBarMenuItem(VAR Bar: MenuBar;
                         BarMenu: Word; NewLine: String32;
                         NewKey: Char; NewReturn: Word);
   VAR
      NewMenu,NewPrev,NewNext: MenuItemPtr;

   BEGIN
   New(NewMenu);
   Inc(Bar[BarMenu].NoOfItems);
   IF Bar[BarMenu].Menu=Nil THEN
      BEGIN
      Bar[BarMenu].Menu:=NewMenu;
      NewPrev:=NewMenu;
      NewNext:=NewMenu;
      END
   ELSE
      BEGIN
      NewNext:=Bar[BarMenu].Menu;
      NewPrev:=NewNext^.Prev;
      NewPrev^.Next:=NewMenu;
      NewNext^.Prev:=NewMenu;
      END;

   AdjustString(NewLine,Bar[BarMenu].MenuLen);
   WITH NewMenu^ DO
      BEGIN
      Line:=NewLine;
      Key:=NewKey;
      Return:=NewReturn;
      Active:=True;
      Next:=NewNext;
      Prev:=NewPrev;
      END;  { with scope }
   END;  { AddBarMenuItem }

PROCEDURE ChangeMenuLine(VAR Bar: MenuBar; BarMenu,Item: Word; NewLine: String32);
   VAR
      ItemToChange: MenuItemPtr;
      i: Word;

   BEGIN
   ItemToChange:=Bar[BarMenu].Menu;
   FOR i:=1 TO Item-1 DO
      ItemToChange:=ItemToChange^.Next;

   AdjustString(NewLine,Bar[BarMenu].MenuLen);
   ItemToChange^.Line:=NewLine;
   END;  { ChangeMenuLine }

PROCEDURE DisplayMenuBar(VAR Bar: MenuBar);
   VAR
      Menu: LineStr;
      x,i,TempMin,TempMax: Word;

   BEGIN
   Menu:='';
   BC:=1;
   FOR i:=1 TO 10 DO
      IF Bar[i].Active THEN
         Menu:=Menu+Bar[i].Line+'  ';

   TempMin:=WindMin;
   TempMax:=WindMax;
   Window(1,1,80,25);
   WriteBlanks(80,1,1,C.SYSMenuBar);
   WriteString(Menu,1,1,C.SYSMenuBar);
   WriteString(Bar[BC].Line,Bar[BC].BarX,1,C.SYSDispSelect);
   Window(Lo(TempMin)+1,Hi(TempMin)+1,Lo(TempMax)+1,Hi(TempMax)+1);
   OnBar:=True;
   END;  { DisplayMenuBar }

PROCEDURE CleanUpMenuBar(VAR Bar: MenuBar);
   VAR
      i,j: Word;
      TMenu,DMenu: MenuItemPtr;

   BEGIN
   FOR i:=1 TO 10 DO
      WITH Bar[i] DO
         IF Active THEN
            BEGIN
            TMenu:=Menu^.Next;
            DMenu:=Menu;
            j:=NoOfItems;
            WHILE j>0 DO
               BEGIN
               Dispose(DMenu);
               Dec(j);
               DMenu:=TMenu;
               TMenu:=TMenu^.Next;
               END;
            END;
   END;  { CleanUpMenuBar }

PROCEDURE HighlightMenuBar(VAR Bar: MenuBar);
   BEGIN
   IF OnBar THEN
      WriteString(Bar[BC].Line,Bar[BC].BarX,1,C.SYSDispSelect);
   END;  { HighlightMenuBar }

PROCEDURE ClearMenuBar(VAR Bar: MenuBar);
   BEGIN
   IF OnBar THEN
      WriteString(Bar[BC].Line,Bar[BC].BarX,1,C.SYSMenuBar);
   END;  { ClearMenuBar }

PROCEDURE ActivateMenuBar(VAR Bar: MenuBar;
                          Ch: Char; VAR Comm: Word);
   LABEL
      ExitLoop;
   VAR
      i,TempMin,TempMax: Word;
      TMenu: MenuItemPtr;

   PROCEDURE PullDown(BC: Word);
      VAR
         MC: MenuItemPtr;
         x,y,MenuY: Word;
         BotStr: String32;

      BEGIN
      WITH Bar[BC] DO
         BEGIN
         SaveArea(MenuX-1,2,MenuLen+2,NoOfItems+1,PullDownArea);
         FOR y:=2 TO NoOfItems+1 DO
            BEGIN
            WriteString('³',MenuX-1,y,C.SYSWBorder);
            WriteString('³',MenuX+MenuLen,y,C.SYSWBorder);
            END;
         BotStr:='À';
         FOR x:=1 TO MenuLen DO
            BotStr:=BotStr+'Ä';
         BotStr:=BotStr+'Ù';
         WriteString(BotStr,MenuX-1,NoOfItems+2,C.SYSWBorder);
         MC:=Menu;
         WriteString(MC^.Line,MenuX,2,C.SYSDispSelect);
         MC:=MC^.Next;
         MenuY:=3;
         WHILE MC<>Menu DO
            BEGIN
            WriteString(MC^.Line,MenuX,MenuY,C.SYSMenu);
            Inc(MenuY);
            MC:=MC^.Next;
            END;
         END;  { with scope }
      END;  { PullDown }

   BEGIN
   TempMin:=WindMin;
   TempMax:=WindMax;
   Window(1,1,80,25);

   Ch:=Alt2Char(Ch);
   CASE Ch OF
      NoKey: BEGIN END;
      EscKey: BEGIN
         IF NOT OnBar THEN
            BEGIN
            RestoreArea(PullDownArea);
            OnBar:=True;
            END;
         Comm:=0;
         END;
      UArrKey: BEGIN
         IF NOT OnBar THEN
            BEGIN
            IF MC<>Bar[BC].Menu THEN
               BEGIN
               WriteString(MC^.Line,Bar[BC].MenuX,MenuY,C.SYSMenu);
               MC:=MC^.Prev;
               Dec(MenuY);
               WriteString(MC^.Line,Bar[BC].MenuX,MenuY,C.SYSDispSelect);
               END;
            END;
         Comm:=0;
         END;
      DArrKey: BEGIN
         IF NOT OnBar THEN
            BEGIN
            IF MC^.Next<>Bar[BC].Menu THEN
               BEGIN
               WriteString(MC^.Line,Bar[BC].MenuX,MenuY,C.SYSMenu);
               MC:=MC^.Next;
               Inc(MenuY);
               WriteString(MC^.Line,Bar[BC].MenuX,MenuY,C.SYSDispSelect);
               END;
            END
         ELSE
            BEGIN
            PullDown(BC);
            MC:=Bar[BC].Menu;
            OnBar:=False;
            MenuY:=2;
            END;
         Comm:=0;
         END;
      LArrKey: BEGIN
         IF (BC>1) THEN
            BEGIN
            IF NOT OnBar THEN
               BEGIN
               RestoreArea(PullDownArea);
               PullDown(BC-1);
               MC:=Bar[BC-1].Menu;
               MenuY:=2;
               END;

            WriteString(Bar[BC].Line,Bar[BC].BarX,1,C.SYSMenuBar);
            Dec(BC);
            WriteString(Bar[BC].Line,Bar[BC].BarX,1,C.SYSDispSelect);
            END;

         Comm:=0;
         END;
      RArrKey: BEGIN
         IF (BC<10) AND (Bar[BC+1].Active) THEN
            BEGIN
            IF NOT OnBar THEN
               BEGIN
               RestoreArea(PullDownArea);
               PullDown(BC+1);
               MC:=Bar[BC+1].Menu;
               MenuY:=2;
               END;

            WriteString(Bar[BC].Line,Bar[BC].BarX,1,C.SYSMenuBar);
            Inc(BC);
            WriteString(Bar[BC].Line,Bar[BC].BarX,1,C.SYSDispSelect);
            END;

         Comm:=0;
         END;
      ReturnKey: BEGIN
         IF OnBar THEN
            BEGIN
            PullDown(BC);
            MC:=Bar[BC].Menu;
            OnBar:=False;
            MenuY:=2;
            Comm:=0;
            END
         ELSE
            BEGIN
            Comm:=MC^.Return;
            RestoreArea(PullDownArea);
            OnBar:=True;
            END;
         END;
   ELSE
      { see if key pressed is a menu }
      FOR i:=1 TO 10 DO
         BEGIN
         IF Bar[i].Key=Ch THEN
            BEGIN
            IF NOT OnBar THEN
               RestoreArea(PullDownArea);
            WriteString(Bar[BC].Line,Bar[BC].BarX,1,C.SYSMenuBar);
            BC:=i;
            WriteString(Bar[BC].Line,Bar[BC].BarX,1,C.SYSDispSelect);
            PullDown(BC);
            MC:=Bar[BC].Menu;
            MenuY:=2;
            OnBar:=False;
            Comm:=0;
            GOTO ExitLoop;
            END;
         END;

      { see if key is a command }
      IF NOT OnBar THEN
         BEGIN
         TMenu:=Bar[BC].Menu;
         i:=Bar[BC].NoOfItems;
         WHILE (i>0) AND (TMenu^.Key<>Ch) DO
            BEGIN
            TMenu:=TMenu^.Next;
            Dec(i);
            END;

         IF i=0 THEN
            Comm:=0
         ELSE
            BEGIN
            Comm:=TMenu^.Return;
            OnBar:=True;
            RestoreArea(PullDownArea);
            END;
         END
      ELSE
         Comm:=0;

   END;  { case }

   ExitLoop:
   Window(Lo(TempMin)+1,Hi(TempMin)+1,Lo(TempMax)+1,Hi(TempMax)+1);
   END;  { ActivateMenuBar }

END.
