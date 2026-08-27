(* PRINTER.PAS -- fullbuild lane's own stand-in for Turbo Pascal's Printer unit.

   Not a patch against any pristine file -- there is no PRINTER.PAS in
   reference/DOSAnacreonSource131; the real Printer unit is Borland's, binding
   the `Lst` text file to LPT1. fpc does ship a real Printer unit
   (units/i386-win32/rtl-extra/printer.ppu), but it isn't on the default unit
   search path, and even if it were, this lane doesn't want it: talking to a
   real printer port is meaningless on a modern OS and out of scope for a
   headless test harness (same reasoning as shims/CRT.PAS -- eliminate, don't
   reproduce).

   `Lst` is assigned to the NUL device so pristine code's Write(Lst,...)/
   WriteLn(Lst,...) calls stay valid Pascal (a real Text file, real IOResult
   semantics) but produce no output and touch nothing external. *)

UNIT Printer;

INTERFACE

VAR
   Lst: Text;

IMPLEMENTATION

BEGIN
Assign(Lst, 'NUL');
Rewrite(Lst);
END.
