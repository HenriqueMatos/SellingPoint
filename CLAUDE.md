# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

`README.md` is the product-level document: what the app does, why the printing,
paper-saving and update decisions are what they are, and how the UI is meant to
look. Read it before changing behaviour in those areas. This file covers what a
new contributor cannot see from any single file.

## Commands

The .NET 10 SDK lives in `~/.dotnet` and is not on the default PATH. Export it
first in every shell:

```sh
export PATH="$HOME/.dotnet:$PATH"

dotnet build                                     # whole solution
dotnet test                                      # 308 tests, ~2 s
dotnet test --filter "FullyQualifiedName~MoneyTests"        # one class
dotnet test --filter "FullyQualifiedName~A_full_night_at_the_till"  # one test
dotnet run --project src/SellingPoint.App        # run the UI
```

Run the app against a scratch database and open straight onto a screen:

```sh
dotnet run --project src/SellingPoint.App -- --db=/tmp/scratch.db --tab=2
```

`--tab=` is `0` Venda, `1` Gestão, `2` Relatórios, `3` Definições.

Build the Windows executable (works from macOS and Linux — no Windows machine
needed):

```sh
dotnet publish src/SellingPoint.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish/win-x64
```

Release: `./release.sh 1.5.0 "notas"` raises `<Version>` in
`Directory.Build.props`, runs the tests, publishes the `.exe`, commits, tags and
creates the GitHub release the in-app updater reads. It refuses on a dirty tree
or an existing tag. Do not bump the version by hand — the tag, the release and
the property have to agree.

## Project layout and dependency direction

```
SellingPoint.Core      cart, money, tender, models, enums — no UI, no I/O
SellingPoint.Data      SQLite: schema.sql, Db, repositories        → Core
SellingPoint.Printing  slips, layout, ESC/POS, transports          → Core
SellingPoint.App       Avalonia UI + PrintService + updater  → Core, Data, Printing
```

Core has no dependencies at all. Nothing depends on App except the test project,
which references it for one class: `PrintService` is deliberately free of any
Avalonia reference so it can be tested headlessly. Keep it that way — it runs on
a background thread and raises a plain `Action` event; view models marshal onto
the UI thread themselves.

`AppServices` is the composition root: hand-wired, no container. It owns the
`Db`, every repository, the `TicketPrinter`, the `PrintService`, and the updater.
Settings are read through it (`BuildTicketOptions`, `BuildTransport`), so a
settings change calls `ReloadPrinter()` rather than rebuilding the graph.

## Data model and its invariants

Hierarchy: **event** (a festival) → **session** (one night, its own float and
count) → **sale** → **sale_line**. Only one session is open at a time, and the
open event falls out of that. `SalesRepository.OpenSession` enforces "no session
outside an event" itself rather than trusting callers.

- **Money is integer cents everywhere.** Never float, never decimal. The app runs
  in `InvariantGlobalization`, so `Money.Format`/`TryParseEuros` do the
  formatting by hand; do not reach for a culture.
- **Sale lines are snapshots.** `product_name`, `unit_price_cents`,
  `category_name`, `print_group` and `slip_mode` are copied onto the line at sale
  time. Never join `sale_line` to `product` to render a past sale or a report.
- **Enum members are stored by name** (`slip_mode`, `payment_method`,
  `TicketFontSize`, `PaperWidth`). Add members freely; renaming one silently
  drops the setting every existing till is on.
- Queued slips store **encoded bytes**, not the sale, so what comes out of the
  printer is what was queued regardless of settings changed in between.

### Schema changes

`schema.sql` is an embedded resource applied on **every** startup, so it must
stay additive — `CREATE ... IF NOT EXISTS` only, never a drop or a rename. That
covers new tables and indexes and nothing else.

A new column on an existing table needs a step in `Db.Migrate`. Follow the shape
that is there:

- Decide whether to run by **asking the database** (`pragma_table_info`), not by
  reading the `schema_version` stamp — the stamp is bookkeeping and can be wrong.
- Take a `Backup` before the first change.
- Do the work in one transaction and move the version inside it, so a failed step
  is retried next launch rather than half applied.
- Prefer SQL with no clock, so the result is the same however often it is read.
- `MigrationTests.cs` builds a real v1 database and upgrades it. Extend it.

## Printing

The path is: `TicketBuilder.Build` (sale → slips) → `SlipRenderer.Render` (slip →
text lines) → `EscPosEncoder.Encode` (lines → bytes) → `IPrintTransport.Send`.
`TicketPrinter.Compose` and `.Send` are separate on purpose so slips can be
queued.

**Nothing prints directly.** `PrintService` writes every slip to the `print_job`
table and a background worker drains it oldest-first, with backoff, status
queries, and COM-port relocation after every third consecutive failure. If you
add a path to paper, enqueue it — do not call the transport.

**Never store or hand-enter a column count.** Characters per line are derived by
`PaperFormat.Columns(paper, font)` from the printable dots and the cell size.
Line-level emphasis combines with the base size by `Math.Max`, never by
multiplying (`PaperFormat.EffectiveWidth`) — three places need that same answer:
the bytes, the preview panel and the paper estimate. `NoOverflowTests` walks all
192 paper × size × switch combinations and asserts no line exceeds the columns;
it is the guarantee, so any layout change must keep it green.

Status queries (`DLE EOT`) need to read as well as write, so they only exist on
serial and network. `WindowsRawTransport` and `FileTransport` are one-way.

## UI conventions

MVVM with CommunityToolkit.Mvvm. View models use the C# partial-property form —
`[ObservableProperty] public partial bool DeleteArmed { get; set; }` — and
`[RelayCommand]`. Views bind with compiled bindings (`x:DataType`) and are wired
as plain child controls in `MainWindow.axaml`, not through `DataTemplates`.

- **Each tab reloads on selection** (`MainWindowViewModel.OnSelectedTabChanged`).
  Prices and settings are edited on one tab and used on another.
- **Every colour and measurement is in `Styles/Tokens.axaml`.** Do not hard-code
  a colour or a touch-target size in a view.
- **Any control rendering a user-entered name sets `LineHeight` explicitly**
  (~1.75× the font size). Avalonia's default line box clips accents above cap
  height, so `Água` renders as `Agua`. See README for the full diagnosis.
- **Destructive actions ask first, in the same place they act.** Two-tap arm →
  confirm on one button (`CategoryDeleteArmed`, `CloseArmed`, `DiscardArmed`),
  with the label bound to the armed state. Deleting a festival is the exception:
  it uses two *separate* buttons and additionally refuses until the festival has
  been exported and has not grown since, because a double tap on a wet screen
  would otherwise cost a whole festival. `DestructiveActionTests.cs` covers this.
- Anything armed must be disarmable — the print worker's 3-second change event
  would otherwise cancel a question before it could be answered.

## Tests

xunit, `tests/SellingPoint.Tests`. `TempDb` gives a real SQLite file in the temp
directory (not `:memory:` — repositories open a connection per call).
**Test parallelization is disabled process-wide** in `Parallelism.cs`, because
`TempDb.Dispose` calls the global `SqliteConnection.ClearAllPools()`; the reasons
are written out there. Do not re-enable it.

`Using` directives for `Xunit`, `SellingPoint.Core` and `SellingPoint.Data` are
implicit via the csproj.

## Language

User-facing strings — UI labels, status messages, ticket text, exception messages
that reach the screen — are **Portuguese**. Code, identifiers, comments and test
names are **English**. The `%APPDATA%\SellingPoint\` data folder keeps the old
project name deliberately so renaming the program does not orphan existing
installs; do not "fix" it.
