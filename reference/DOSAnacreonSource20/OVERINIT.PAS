UNIT OVERINIT;

INTERFACE

IMPLEMENTATION

USES
	Overlay;

CONST
	BufferSize = 40000;

BEGIN
OvrInit('ANACREON.OVR');
OvrSetBuf(BufferSize);
OvrSetRetry(BufferSize DIV 3);
END.
