# F481 - Cancelación segura de solicitudes pendientes EAN

- Original Admin requester may cancel only a request whose latest immutable event is Solicitada (version 1). No other Admin may cancel another request.
- PostgreSQL advisory lock shares request key with approval; a cancellation and approval race can never both commit.
- Cancellation appends Rechazada version 2, repeats return the same durable state, no EAN or WMS mutation. A late approval is rejected.
- No deletion of audit history; executed rotations are irreversible by this endpoint. Any business recovery requires a separately approved new EAN under non-reuse policy.
- Admin HTTP endpoint and screen are behind the same two OFF-by-default barcode flags. Production cutover stays blocked under F477, and no cloud migrations were run.
- PG tests, U2 explicit action role matrix and frontend build prove behavior in development only. F398 hardware and #65 coordinated release still open.