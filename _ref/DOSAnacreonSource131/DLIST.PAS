(* DLIST.PAS -------------------------------------------------------------------

   algorithms:

      This unit links elements on a doubly-linked spine with the root in 
      ListRecord.

      ListRecord
      +--------+    +------+    +------+                 +------+
      | Head   |--->| Next |--->| Next |---> ... ... --->| Next |--->Nil
      +--------+    +------+    +------+                 +------+
               Nil<-| Prev |<---| Prev |<--- ... ... <---| Prev |
                    +------+    +------+                 +------+
                    | Elem |    | Elem |                 | Elem |
                    +------+    +------+                 +------+
                       |           |                        |
                       |           |                        |
                       |           |                        |
                       V           V                        V
                 +---------+    +---------+              +---------+ 
                 | Actual  |    | Actual  |              | Actual  |
                 | Element |    | Element |              | Element |
                 +---------+    +---------+              +---------+

----------------------------------------------------------------------------- *)

UNIT DList;

INTERFACE

PROCEDURE InitializeList(VAR List: ListStructure; ElementSize: Word);

IMPLEMENTATION

TYPE

   SpinePtr = ^SpineRecord;
   SpineRecord = RECORD
      Next: SpinePtr;
      Prev: SpinePtr;
      Element: Pointer;
   END;

   ListRecord = RECORD
      Head: SpinePtr;
      Size: Word;
   END;

PROCEDURE InitializeList(VAR List: ListStructure; ElementSize: Word);
   VAR
      L: ListRecord ABSOLUTE List;

   BEGIN
   WITH L DO
      BEGIN
      Head:=Nil;
      Size:=ElementSize;
      END;  { with scope }
   END;  { InitializeList }

PROCEDURE AddListElement(VAR List: ListStructure; ElmPtr: Pointer; Pos: 

END.
