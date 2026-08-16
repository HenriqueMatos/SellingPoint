-- Applied on every startup. Additive only: never drop or rename a column here,
-- add a new statement instead. schema_version exists so a future change that
-- genuinely cannot be expressed as CREATE IF NOT EXISTS has somewhere to hook in.

CREATE TABLE IF NOT EXISTS setting (
  key   TEXT PRIMARY KEY,
  value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS category (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  name        TEXT    NOT NULL,
  color       TEXT    NOT NULL DEFAULT '#3A7BD5',
  sort_order  INTEGER NOT NULL DEFAULT 0,
  print_group TEXT    NOT NULL DEFAULT 'Bar',
  slip_mode   TEXT    NOT NULL DEFAULT 'Grouped'
);

CREATE TABLE IF NOT EXISTS product (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  category_id INTEGER NOT NULL REFERENCES category(id) ON DELETE CASCADE,
  name        TEXT    NOT NULL,
  price_cents INTEGER NOT NULL,
  sort_order  INTEGER NOT NULL DEFAULT 0,
  is_active   INTEGER NOT NULL DEFAULT 1,
  track_stock INTEGER NOT NULL DEFAULT 0,
  stock_qty   INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_product_category ON product(category_id);

CREATE TABLE IF NOT EXISTS session (
  id                    INTEGER PRIMARY KEY AUTOINCREMENT,
  name                  TEXT    NOT NULL,
  opened_at             TEXT    NOT NULL,
  closed_at             TEXT,
  opening_float_cents   INTEGER NOT NULL DEFAULT 0,
  closing_counted_cents INTEGER
);

CREATE TABLE IF NOT EXISTS sale (
  id                  INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id          INTEGER NOT NULL REFERENCES session(id),
  ticket_number       INTEGER NOT NULL,
  created_at          TEXT    NOT NULL,
  total_cents         INTEGER NOT NULL,
  payment_method      TEXT    NOT NULL,
  cash_received_cents INTEGER NOT NULL DEFAULT 0,
  change_cents        INTEGER NOT NULL DEFAULT 0
);
-- Two tickets numbered #42 on the same night would be unresolvable at the bar.
CREATE UNIQUE INDEX IF NOT EXISTS ux_sale_ticket ON sale(session_id, ticket_number);

-- product_name, unit_price_cents, category_name, print_group and slip_mode are
-- snapshots taken at sale time. Never join to product to render a past sale.
CREATE TABLE IF NOT EXISTS sale_line (
  id               INTEGER PRIMARY KEY AUTOINCREMENT,
  sale_id          INTEGER NOT NULL REFERENCES sale(id) ON DELETE CASCADE,
  product_id       INTEGER NOT NULL,
  product_name     TEXT    NOT NULL,
  unit_price_cents INTEGER NOT NULL,
  category_name    TEXT    NOT NULL,
  print_group      TEXT    NOT NULL,
  slip_mode        TEXT    NOT NULL,
  qty              INTEGER NOT NULL,
  line_total_cents INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_sale_line_sale ON sale_line(sale_id);

-- Manual stock changes only (restocks, corrections). Sales are derivable from sale_line.
CREATE TABLE IF NOT EXISTS stock_adjustment (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  product_id INTEGER NOT NULL REFERENCES product(id) ON DELETE CASCADE,
  delta      INTEGER NOT NULL,
  reason     TEXT    NOT NULL DEFAULT '',
  created_at TEXT    NOT NULL,
  session_id INTEGER
);
CREATE INDEX IF NOT EXISTS ix_stock_adjustment_product ON stock_adjustment(product_id);

-- Slips waiting for a printer. The encoded bytes are stored rather than the sale,
-- so what was queued is exactly what eventually comes out, whatever anyone changes
-- in Definições in between.
CREATE TABLE IF NOT EXISTS print_job (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  sale_id     INTEGER,
  title       TEXT    NOT NULL,
  payload     BLOB    NOT NULL,
  preview     TEXT    NOT NULL DEFAULT '',
  created_at  TEXT    NOT NULL,
  attempts    INTEGER NOT NULL DEFAULT 0,
  last_error  TEXT,
  printed_at  TEXT
);
CREATE INDEX IF NOT EXISTS ix_print_job_pending ON print_job(printed_at, id);

-- Cash that entered or left the drawer without a sale. Negative is money taken
-- out - the run to the car at eleven with most of the night's takings.
--
-- Without somewhere to record it, that run destroys the one number that catches
-- an error or a theft: expected cash against counted cash. The only way out was
-- to close the session mid-evening and start another, which splits the night's
-- report in two.
CREATE TABLE IF NOT EXISTS cash_movement (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id INTEGER NOT NULL REFERENCES session(id),
  cents      INTEGER NOT NULL,
  reason     TEXT    NOT NULL DEFAULT '',
  created_at TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_cash_movement_session ON cash_movement(session_id);

INSERT OR IGNORE INTO setting(key, value) VALUES ('schema_version', '1');
