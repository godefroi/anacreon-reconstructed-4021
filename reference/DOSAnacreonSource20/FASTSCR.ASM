; Extended IO Primitives
;
; This subroutines are to be linked with EIO.PAS using TURBO Pascal 4.0.

DATA    SEGMENT WORD PUBLIC

        EXTRN   VirtualScreen: DWORD
        EXTRN   WindMin: WORD
        EXTRN   WindMax: WORD
        EXTRN   CheckSnow: BYTE

DATA    ENDS

CODE    SEGMENT BYTE PUBLIC
        ASSUME  CS:CODE, DS:DATA

        PUBLIC  ScrollDown
        PUBLIC  ScrollLeft
        PUBLIC  ScrollRight
        PUBLIC  ScrollUp
        PUBLIC  WriteBlanks
        PUBLIC  WriteString

; WriteString ----------------------------------------------------------------
;
; Parameters:
;       VAR S: STRING;
;       x: Word;
;       y: Word;
;       Col: Word;
;
; ----------------------------------------------------------------------------
WriteString     PROC FAR

WSString        EQU     DWORD PTR [BP+12];
WSX             EQU     WORD PTR [BP+10];
WSY             EQU     WORD PTR [BP+8];
WSCol           EQU     WORD PTR [BP+6];

        PUSH    BP              ;Save BP
        MOV     BP,SP           ;Set up stack frame
        PUSH    DS              ;Save DS.
                                ;   Wait until all globals are used before 
                                ;   changing the DS register.

        ; Get absolute Y in AL
        MOV     AX,WSY          ;Load AX with X coordinate
        DEC     AX              ;AX is 0..24
        MOV     BX,WindMin      ;BL = X coord, BH = Y.
        ADD     AL,BH           ;Add window offset to y.

        ; Multiply by 80
        MOV     CX,0004H        ;Prepare to multiply
        SHL     AX,CL           ; AX = X * 16
        MOV     BX,AX           ; BX = X * 16
        SHL     AX,1
        SHL     AX,1            ; AX = X * 64
        ADD     AX,BX           ; AX = X * 80

        ; Get absolute X in BX
        MOV     BX,WSX          ;Load BX with relative X
        DEC     BX              ; BX is 0..79
        MOV     CX,WindMin
        ADD     BL,CL           ;Add window offset to y

        ;Get Seg:Off
        ADD     AX,BX
        SHL     AX,1            ;AX is offset to screen
        LES     DI,VirtualScreen
        ADD     DI,AX           ;ES:DI is screen position

        MOV     AL,CheckSnow

        LDS     SI,WSString     ;DS:SI points to WSString[0];
        MOV     CL,[SI]         ;Get string length
        XOR     CH,CH           ;Clear high byte
        JCXZ    WSExit          ;If length=0 then exit
        INC     SI              ;First character of string
        MOV     DX,WSCol        ;Get attribute in AH
        MOV     AH,DL
        CLD
        RCR     AL,1            ;If CheckSnow is not true...
        JNC     WSMono          ;...then goto WSMono

        ;Check for snow...
        MOV     DX,03DAH
WSNext: LODSB                   ;Get next character
        MOV     BX,AX
        MOV     AH,09H
        CLI
WSH:    IN      AL,DX
        RCR     AL,1
        JC      WSH             ;Wait for horizontal retrace
WSV:    IN      AL,DX
        AND     AL,AH
        JZ      WSV             ;Wait for vertical retrace

        MOV     AX,BX
        STOSW                   ;Write character to screen
        STI
        LOOP    WSNext          ;Next character
        JMP     WSExit          ;Exit

        ;Monochrome
WSMono: LODSB
        STOSW
        LOOP WSMono

WSExit: POP     DS              ;Restore DS
        POP     BP              ;Restore BP
        RET     10              ;Remove parameters and return.

WriteString     ENDP

; WriteBlanks ----------------------------------------------------------------
;
; Parameters:
;       NoOfChrs: Word;
;       x: Word;
;       y: Word;
;       Col: Word;
;
; ----------------------------------------------------------------------------
WriteBlanks     PROC FAR

WBNoOfChrs      EQU     WORD PTR [BP+12];
WBX             EQU     WORD PTR [BP+10];
WBY             EQU     WORD PTR [BP+8];
WBCol           EQU     WORD PTR [BP+6];

        PUSH    BP              ;Save BP
        MOV     BP,SP           ;Set up stack frame
        PUSH    DS              ;Save DS.
                                ;   Wait until all globals are used before 
                                ;   changing the DS register.

        ; Get absolute Y in AL
        MOV     AX,WBY          ;Load AX with X coordinate
        DEC     AX              ;AX is 0..24
        MOV     BX,WindMin      ;BL = X coord, BH = Y.
        ADD     AL,BH           ;Add window offset to y.

        ; Multiply by 80
        MOV     CX,0004H        ;Prepare to multiply
        SHL     AX,CL           ; AX = X * 16
        MOV     BX,AX           ; BX = X * 16
        SHL     AX,1
        SHL     AX,1            ; AX = X * 64
        ADD     AX,BX           ; AX = X * 80

        ; Get absolute X in BX
        MOV     BX,WBX          ;Load BX with relative X
        DEC     BX              ; BX is 0..79
        MOV     CX,WindMin
        ADD     BL,CL           ;Add window offset to y

        ;Get Seg:Off
        ADD     AX,BX
        SHL     AX,1            ;AX is offset to screen
        LES     DI,VirtualScreen
        ADD     DI,AX           ;ES:DI is screen position

        MOV     AL,CheckSnow

        MOV     CX,WBNoOfChrs   ;Get string length
        JCXZ    WBExit          ;If length=0 then exit
        MOV     DX,WBCol        ;Get attribute in AH
        MOV     AH,DL

        CLD

        RCR     AL,1            ;If CheckSnow is not true...
        JNC     WBMono          ;...then goto WBMono

        ;Check for snow...
        MOV     DX,03DAH
        MOV     AL,32
WBNext: MOV     BX,AX           ;Save AX
        MOV     AH,09H
        CLI
WBH:    IN      AL,DX
        RCR     AL,1
        JC      WBH             ;Wait for horizontal retrace
WBV:    IN      AL,DX
        AND     AL,AH
        JZ      WBV             ;Wait for vertical retrace

        MOV     AX,BX
        STOSW                   ;Write character to screen
        STI
        LOOP    WBNext          ;Next character
        JMP     WBExit          ;Exit

        ;Monochrome
WBMono: MOV     AL,32           ;Blank in AL
WBN2:   STOSW
        LOOP WBN2  

WBExit: POP     DS              ;Restore DS
        POP     BP              ;Restore BP
        RET     8               ;Remove parameters and return.

WriteBlanks     ENDP

; ScrollUp -------------------------------------------------------------------
;
; Variables:
;       Height: WORD
;       Width: WORD
;
; ----------------------------------------------------------------------------
ScrollUp        PROC FAR

SUHeight        EQU     WORD PTR [BP-2];
SUWidth         EQU     WORD PTR [BP-4];

        PUSH    BP              ;Save BP
        MOV     BP,SP           ;Set up stack frame
        SUB     SP,4            ;4 Bytes of local space
        PUSH    DS              ;Save DS

        ;Initialize SUHeight and SUWidth
        MOV     DX,WindMin      ;DL = X Coordinate
        MOV     CX,WindMax      ;CL = Max X Coordinate        
        XOR     AX,AX           ;Clear AX
        MOV     AL,CL           ;AL = Max X Coordinate
        SUB     AL,DL
        INC     AX
        MOV     SUWidth,AX      ;Store width
        MOV     AL,CH           ;AL = Max Y Coordinate
        SUB     AL,DH
        INC     AX
        MOV     SUHeight,AX     ;Store height

        ;Y coordinate in AX
        MOV     AL,DH

        ;Multiply by 80.  DX = WindMin, AL = Y coord.
        MOV     CX,0004H        ;Prepare to shift 4 places
        SHL     AX,CL           ;AX = Y * 16
        MOV     BX,AX           ;BX = Y * 16
        SHL     AX,1            ;AX = Y * 32
        SHL     AX,1            ;AX = Y * 64
        ADD     AX,BX           ;AX = (Y*64) + (Y*16) = Y*80

        XOR     BX,BX           ;Clear BX
        MOV     BL,DL           ;X Coordinate in BL
        ADD     AX,BX           ;AX = Y*80 + X
        SHL     AX,1            ;Account for attribute
        MOV     DX,AX           ;Temporary store

        ;Get address of virtual screen in ES:DI
        LES     DI,VirtualScreen

        MOV     BL,CheckSnow    ;Get CheckSnow

        ;Point ES:DI to Line #1
        MOV     AX,DI
        ADD     AX,DX           ;AX = offset to window corner
        MOV     DI,AX           ;ES:DI points to window corner

        ;Point DS:SI to Line #2
        MOV     AX,ES
        MOV     DS,AX           ;set DS to same segment
        MOV     AX,DI
        ADD     AX,160          ;next line
        MOV     SI,AX           ;DS:SI points to line #2

        CLD                     ;set direction

        RCR     BL,1            ;if not CheckSnow then SUMono
        JNC     SUMono

;-------COLOR routine.  Use only when CheckSnow=True

        ;Turn screen off
        MOV     DX,03DAH        ;status port
SUW1:   IN      AL,DX           ;read port
        TEST    AL,08H          ;test BIT 3
        JZ SUW1
        MOV     AL,21H          ;color card status
        MOV     DL,0D8H         ;output port
        OUT     DX,AL

        ;Get window height in CX 0..24
        MOV     CX,SUHeight
        DEC     CX

SUNL:   PUSH    CX
        PUSH    SI
        PUSH    DI
        MOV     CX,SUWidth

SUNC:   LODSW
        STOSW                   ;move DS:SI to ES:DI
        LOOP    SUNC                  

        POP     DI
        POP     SI
        ADD     DI,160
        ADD     SI,160
        POP     CX
        LOOP    SUNL   

        ;Turn screen ON
        MOV     DX,03DAH        ;status port
SUW2:   IN      AL,DX           ;read port
        TEST    AL,08H          ;test BIT 3
        JZ      SUW2 
        MOV     AL,29H          ;color card status
        MOV     DL,0D8H         ;output port
        OUT     DX,AL

        JMP     SUExit

;-------MONO routine.
SUMono: MOV     CX,SUHeight
        DEC     CX
SUNL2:  PUSH    CX
        PUSH    SI
        PUSH    DI
        MOV     CX,SUWidth

SUNC2:  LODSW
        STOSW                   ;move DS:SI to ES:DI
        LOOP    SUNC2 

        POP     DI
        POP     SI
        ADD     DI,160
        ADD     SI,160
        POP     CX
        LOOP    SUNL2 

SUExit: POP     DS
        MOV     SP,BP           ;De-allocate local variables
        POP     BP
        RET

ScrollUp        ENDP

; ScrollDown -----------------------------------------------------------------
;
; Variables:
;       Height: WORD
;       Width: WORD
;
; ----------------------------------------------------------------------------
ScrollDown      PROC FAR

SDHeight        EQU     WORD PTR [BP-2];
SDWidth         EQU     WORD PTR [BP-4];

        PUSH    BP              ;Save BP
        MOV     BP,SP           ;Set up stack frame
        SUB     SP,4            ;4 Bytes of local space
        PUSH    DS              ;Save DS

        ;Initialize SDHeight and SDWidth
        MOV     DX,WindMin      ;DL = X Coordinate
        MOV     CX,WindMax      ;CL = Max X Coordinate        
        XOR     AX,AX           ;Clear AX
        MOV     AL,CL           ;AL = Max X Coordinate
        SUB     AL,DL
        INC     AX
        MOV     SDWidth,AX      ;Store width
        MOV     AL,CH           ;AL = Max Y Coordinate
        SUB     AL,DH
        INC     AX
        MOV     SDHeight,AX     ;Store height

        ;Y coordinate in AX
        MOV     AL,DH
        ADD     AX,SDHeight     ;Add Height
        DEC     AX              ;AX = (Y+Height) = last line of scroll window

        ;Multiply by 80.  DX = WindMin, AL = Y coord.
        MOV     CX,0004H        ;Prepare to shift 4 places
        SHL     AX,CL           ;AX = Y * 16
        MOV     BX,AX           ;BX = Y * 16
        SHL     AX,1            ;AX = Y * 32
        SHL     AX,1            ;AX = Y * 64
        ADD     AX,BX           ;AX = (Y*64) + (Y*16) = Y*80

        XOR     BX,BX           ;Clear BX
        MOV     BL,DL           ;X Coordinate in BL
        ADD     AX,BX           ;AX = Y*80 + X
        SHL     AX,1            ;Account for attribute
        MOV     DX,AX           ;Temporary store

        ;Get address of virtual screen in ES:DI
        LES     DI,VirtualScreen

        MOV     BL,CheckSnow    ;Get CheckSnow

        ;Point ES:DI to last line
        MOV     AX,DI
        ADD     AX,DX           ;AX = offset to lower left corner
        MOV     DI,AX           ;ES:DI points to window corner

        ;Point DS:SI to next to last line
        MOV     AX,ES
        MOV     DS,AX           ;set DS to same segment
        MOV     AX,DI
        SUB     AX,160          ;previous line
        MOV     SI,AX           ;DS:SI points to line #2

        CLD                     ;set direction

        RCR     BL,1            ;if not CheckSnow then SDMono
        JNC     SDMono

;-------COLOR routine.  Use only when CheckSnow=True

        ;Turn screen off
        MOV     DX,03DAH        ;status port
SDW1:   IN      AL,DX           ;read port
        TEST    AL,08H          ;test BIT 3
        JZ SDW1
        MOV     AL,21H          ;color card status
        MOV     DL,0D8H         ;output port
        OUT     DX,AL

        ;Get window height in CX 0..24
        MOV     CX,SDHeight
        DEC     CX

SDNL:   PUSH    CX
        PUSH    SI
        PUSH    DI
        MOV     CX,SDWidth

SDNC:   LODSW
        STOSW                   ;move DS:SI to ES:DI
        LOOP    SDNC                  

        POP     DI
        POP     SI
        SUB     DI,160          ;move up a line
        SUB     SI,160          ;move up a line
        POP     CX
        LOOP    SDNL   

        ;Turn screen ON
        MOV     DX,03DAH        ;status port
SDW2:   IN      AL,DX           ;read port
        TEST    AL,08H          ;test BIT 3
        JZ      SDW2 
        MOV     AL,29H          ;color card status
        MOV     DL,0D8H         ;output port
        OUT     DX,AL

        JMP     SDExit

;-------MONO routine.
SDMono: MOV     CX,SDHeight
        DEC     CX
SDNL2:  PUSH    CX
        PUSH    SI
        PUSH    DI
        MOV     CX,SDWidth

SDNC2:  LODSW
        STOSW                   ;move DS:SI to ES:DI
        LOOP    SDNC2 

        POP     DI
        POP     SI
        SUB     DI,160
        SUB     SI,160
        POP     CX
        LOOP    SDNL2 

SDExit: POP     DS
        MOV     SP,BP           ;De-allocate local variables
        POP     BP
        RET

ScrollDown      ENDP

; ScrollRight ----------------------------------------------------------------
;
; Variables:
;       Height: WORD
;       Width: WORD
;
; ----------------------------------------------------------------------------
ScrollRight     PROC FAR

SRHeight        EQU     WORD PTR [BP-2];
SRWidth         EQU     WORD PTR [BP-4];

        PUSH    BP              ;Save BP
        MOV     BP,SP           ;Set up stack frame
        SUB     SP,4            ;4 Bytes of local space
        PUSH    DS              ;Save DS

        ;Initialize SRHeight and SRWidth
        MOV     DX,WindMin      ;DL = X Coordinate
        MOV     CX,WindMax      ;CL = Max X Coordinate        
        XOR     AX,AX           ;Clear AX
        MOV     AL,CL           ;AL = Max X Coordinate
        SUB     AL,DL
        INC     AX
        MOV     SRWidth,AX      ;Store width
        MOV     AL,CH           ;AL = Max Y Coordinate
        SUB     AL,DH
        INC     AX
        MOV     SRHeight,AX     ;Store height

        ;Y coordinate in AX
        MOV     AL,DH

        ;Multiply by 80.  DX = WindMin, AL = Y coord.
        MOV     CX,0004H        ;Prepare to shift 4 places
        SHL     AX,CL           ;AX = Y * 16
        MOV     BX,AX           ;BX = Y * 16
        SHL     AX,1            ;AX = Y * 32
        SHL     AX,1            ;AX = Y * 64
        ADD     AX,BX           ;AX = (Y*64) + (Y*16) = Y*80

        XOR     BX,BX           ;Clear BX
        MOV     BL,DL           ;X Coordinate in BL
        ADD     AX,BX           ;AX = Y*80 + X
        SHL     AX,1            ;Account for attribute
        MOV     DX,AX           ;Temporary store

        ;Get address of virtual screen in ES:DI
        LES     DI,VirtualScreen

        MOV     BL,CheckSnow    ;Get CheckSnow

        ;Point ES:DI to last chr on Line #1
        MOV     AX,DI
        ADD     AX,DX           ;AX = offset to window corner
        ADD     AX,SRWidth      ;Add width...
        ADD     AX,SRWidth      ;...twice to accout for attributes.
        DEC     AX
        DEC     AX
        MOV     DI,AX           ;ES:DI points to last chr on line #1

        ;Point DS:SI to Line #1, 2nd to last character
        MOV     AX,ES
        MOV     DS,AX           ;set DS to same segment
        MOV     AX,DI
        SUB     AX,2            ;previous character
        MOV     SI,AX           ;DS:SI points to line #1, 2nd character.

        STD                     ;set direction

        RCR     BL,1            ;if not CheckSnow then SRMono
        JNC     SRMono

;-------COLOR routine.  Use only when CheckSnow=True

        ;Turn screen off
        MOV     DX,03DAH        ;status port
SRW1:   IN      AL,DX           ;read port
        TEST    AL,08H          ;test BIT 3
        JZ SRW1
        MOV     AL,21H          ;color card status
        MOV     DL,0D8H         ;output port
        OUT     DX,AL

        ;Get window height in CX 0..24
        MOV     CX,SRHeight

SRNL:   PUSH    CX
        PUSH    SI
        PUSH    DI
        MOV     CX,SRWidth
        DEC     CX

SRNC:   LODSW
        STOSW                   ;move DS:SI to ES:DI
        LOOP    SRNC                  

        POP     DI
        POP     SI
        ADD     DI,160
        ADD     SI,160
        POP     CX
        LOOP    SRNL   

        ;Turn screen ON
        MOV     DX,03DAH        ;status port
SRW2:   IN      AL,DX           ;read port
        TEST    AL,08H          ;test BIT 3
        JZ      SRW2 
        MOV     AL,29H          ;color card status
        MOV     DL,0D8H         ;output port
        OUT     DX,AL

        JMP     SRExit

;-------MONO routine.
SRMono: MOV     CX,SRHeight
SRNL2:  PUSH    CX
        PUSH    SI
        PUSH    DI
        MOV     CX,SRWidth
        DEC     CX

SRNC2:  LODSW
        STOSW                   ;move DS:SI to ES:DI
        LOOP    SRNC2 

        POP     DI
        POP     SI
        ADD     DI,160
        ADD     SI,160
        POP     CX
        LOOP    SRNL2 

SRExit: POP     DS
        MOV     SP,BP           ;De-allocate local variables
        POP     BP
        RET

ScrollRight     ENDP

; ScrollLeft ------------------------------------------------------------------
;
; Variables:
;       Height: WORD
;       Width: WORD
;
; ----------------------------------------------------------------------------
ScrollLeft      PROC FAR

SLHeight        EQU     WORD PTR [BP-2];
SLWidth         EQU     WORD PTR [BP-4];

        PUSH    BP              ;Save BP
        MOV     BP,SP           ;Set up stack frame
        SUB     SP,4            ;4 Bytes of local space
        PUSH    DS              ;Save DS

        ;Initialize SLHeight and SLWidth
        MOV     DX,WindMin      ;DL = X Coordinate
        MOV     CX,WindMax      ;CL = Max X Coordinate        
        XOR     AX,AX           ;Clear AX
        MOV     AL,CL           ;AL = Max X Coordinate
        SUB     AL,DL
        INC     AX
        MOV     SLWidth,AX      ;Store width
        MOV     AL,CH           ;AL = Max Y Coordinate
        SUB     AL,DH
        INC     AX
        MOV     SLHeight,AX     ;Store height

        ;Y coordinate in AX
        MOV     AL,DH

        ;Multiply by 80.  DX = WindMin, AL = Y coord.
        MOV     CX,0004H        ;Prepare to shift 4 places
        SHL     AX,CL           ;AX = Y * 16
        MOV     BX,AX           ;BX = Y * 16
        SHL     AX,1            ;AX = Y * 32
        SHL     AX,1            ;AX = Y * 64
        ADD     AX,BX           ;AX = (Y*64) + (Y*16) = Y*80

        XOR     BX,BX           ;Clear BX
        MOV     BL,DL           ;X Coordinate in BL
        ADD     AX,BX           ;AX = Y*80 + X
        SHL     AX,1            ;Account for attribute
        MOV     DX,AX           ;Temporary store

        ;Get address of virtual screen in ES:DI
        LES     DI,VirtualScreen

        MOV     BL,CheckSnow    ;Get CheckSnow

        ;Point ES:DI to Line #1
        MOV     AX,DI
        ADD     AX,DX           ;AX = offset to window corner
        MOV     DI,AX           ;ES:DI points to window corner

        ;Point DS:SI to Line #1, 2nd character
        MOV     AX,ES
        MOV     DS,AX           ;set DS to same segment
        MOV     AX,DI
        ADD     AX,2            ;next character
        MOV     SI,AX           ;DS:SI points to line #2

        CLD                     ;set direction

        RCR     BL,1            ;if not CheckSnow then SLMono
        JNC     SLMono

;-------COLOR routine.  Use only when CheckSnow=True

        ;Turn screen off
        MOV     DX,03DAH        ;status port
SLW1:   IN      AL,DX           ;read port
        TEST    AL,08H          ;test BIT 3
        JZ SLW1
        MOV     AL,21H          ;color card status
        MOV     DL,0D8H         ;output port
        OUT     DX,AL

        ;Get window height in CX 0..24
        MOV     CX,SLHeight

SLNL:   PUSH    CX
        PUSH    SI
        PUSH    DI
        MOV     CX,SLWidth
        DEC     CX

SLNC:   LODSW
        STOSW                   ;move DS:SI to ES:DI
        LOOP    SLNC                  

        POP     DI
        POP     SI
        ADD     DI,160
        ADD     SI,160
        POP     CX
        LOOP    SLNL   

        ;Turn screen ON
        MOV     DX,03DAH        ;status port
SLW2:   IN      AL,DX           ;read port
        TEST    AL,08H          ;test BIT 3
        JZ      SLW2 
        MOV     AL,29H          ;color card status
        MOV     DL,0D8H         ;output port
        OUT     DX,AL

        JMP     SLExit

;-------MONO routine.
SLMono: MOV     CX,SLHeight
SLNL2:  PUSH    CX
        PUSH    SI
        PUSH    DI
        MOV     CX,SLWidth
        DEC     CX

SLNC2:  LODSW
        STOSW                   ;move DS:SI to ES:DI
        LOOP    SLNC2 

        POP     DI
        POP     SI
        ADD     DI,160
        ADD     SI,160
        POP     CX
        LOOP    SLNL2 

SLExit: POP     DS
        MOV     SP,BP           ;De-allocate local variables
        POP     BP
        RET

ScrollLeft      ENDP

CODE    ENDS

        END

