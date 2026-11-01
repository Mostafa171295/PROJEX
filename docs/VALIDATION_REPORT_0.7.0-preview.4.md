# Validation Report — 0.7.0-preview.4

- Source changes inspected: Desktop API adapter, connection-window ownership, live schedule editor.
- New behavior: duration editing in working days through the existing authenticated PATCH route, optimistic concurrency using `ExpectedRowVersion`, server reload after save.
- No schema migration added.
- No new API route added.
- Build/tests/integration tests: NOT RUN in this environment. Do not treat this preview as production-validated.
- Known scope gap: no graphical drag-and-drop date editing, relationship editor, or undo/redo yet.
