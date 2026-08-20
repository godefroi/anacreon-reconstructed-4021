(* QSORT.PAS -------------------------------------------------------------------

----------------------------------------------------------------------------- *)

UNIT QSort;

INTERFACE

PROCEDURE QuickSortA(VAR ArrayToSort; Size,First,Last: Word);
PROCEDURE QuickSortD(VAR ArrayToSort; Size,First,Last: Word);

IMPLEMENTATION

PROCEDURE QuickSortA(VAR ArrayToSort; Size,First,Last: Word);
{ QuickSortD -------------------------------------------------------------------
   This procedure will sort an array in ascending order based on the first word
   of each element of the array. The array passed to this routine must have an
   even number of bytes per element, and must have the first word in the element
   be a integer to use as a sort key. The size passed must be the size of each
   element in bytes.  The First and Last variables are the first and last elements
   in the array to be sorted (starting with 1 as the first element.)
------------------------------------------------------------------------------ }
   TYPE
      GenericArray = ARRAY [0..32766] OF Word;

   VAR
      A: GenericArray ABSOLUTE ArrayToSort;

   PROCEDURE SortA(First,Last: Word);
      VAR
         i,j: Integer;
         x,y: ARRAY [1..100] OF Word;

      BEGIN
      i:=First; j:=Last;
      Move(A[((First+Last) DIV 2)*Size],x,Size*2);
      REPEAT
         WHILE A[i*Size]<x[1] DO Inc(i);
         WHILE x[1]<A[j*Size] DO Dec(j);
         IF i<=j THEN
            BEGIN
            Move(A[i*Size],y,Size*2);
            Move(A[j*Size],A[i*Size],Size*2);
            Move(y,A[j*Size],Size*2);
            Inc(i);
            Dec(j);
            END;
      UNTIL i>j;
      IF First<j THEN SortA(First,j);
      IF i<Last THEN SortA(i,Last);
      END;  { SortD }

   BEGIN
   Size:=Size DIV 2;
   SortA(First-1,Last-1);
   END;  { QuickSortA }

PROCEDURE QuickSortD(VAR ArrayToSort; Size,First,Last: Word);
{ QuickSortD -------------------------------------------------------------------
   This procedure will sort an array in descending order based on the first word
   of each element of the array. The array passed to this routine must have an
   even number of bytes per element, and must have the first word in the element
   be a integer to use as a sort key. The size passed must be the size of each
   element in bytes.  The First and Last variables are the first and last elements
   in the array to be sorted (starting with 1 as the first element.)
------------------------------------------------------------------------------ }
   TYPE
      GenericArray = ARRAY [0..32766] OF Word;

   VAR
      A: GenericArray ABSOLUTE ArrayToSort;

   PROCEDURE SortD(First,Last: Word);
      VAR
         i,j: Integer;
         x,y: ARRAY [1..100] OF Word;

      BEGIN
      i:=First; j:=Last;
      Move(A[((First+Last) DIV 2)*Size],x,Size*2);
      REPEAT
         WHILE A[i*Size]>x[1] DO Inc(i);
         WHILE x[1]>A[j*Size] DO Dec(j);
         IF i<=j THEN
            BEGIN
            Move(A[i*Size],y,Size*2);
            Move(A[j*Size],A[i*Size],Size*2);
            Move(y,A[j*Size],Size*2);
            Inc(i);
            Dec(j);
            END;
      UNTIL i>j;
      IF First<j THEN SortD(First,j);
      IF i<Last THEN SortD(i,Last);
      END;  { SortD }

   BEGIN
   Size:=Size DIV 2;
   SortD(First-1,Last-1);
   END;  { QuickSortD }

END.
