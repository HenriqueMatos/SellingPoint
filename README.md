# SellingPoint

A till for village festivals, association parties and school events. Big-button
touch screen, your own categories and prices, and slips printed on a cheap
thermal printer for people to hand in at the bar or the kitchen.

Windows program, single file, no installer. Runs offline on one laptop.

## What it does

- **Sell** — tap categories and products, take cash with change calculation or
  record a card payment, print.
- **Print by group** — every category has a *print group*. Categories sharing a
  group print together on one slip; different groups get their own slip. Drinks
  and desserts on `Bar`, food on `Cozinha` gives exactly two slips per order.
- **Senhas or lists** — per category: `3x Cerveja` as one line, or three separate
  slips for the bar to collect, one per drink.
- **Change everything** — categories, colours, products, prices, order, stock.
  All in the app, no config files.
- **Stock** — count down as things sell, warn or block at zero, log restocks.
- **Close the night** — takings split cash vs card, per product, per category,
  expected cash against what was counted, stock left. CSV export, printed
  closing summary, automatic database backup.

## Running it

**On Windows** — copy `SellingPoint.exe` onto the machine and double-click it.
No .NET install, no admin rights, nothing else to copy. Data lives in
`%APPDATA%\SellingPoint\`.

**From source** — needs the .NET 10 SDK:

```sh
dotnet run --project src/SellingPoint.App        # run
dotnet test                                      # 119 tests
```

Build the Windows executable — this works from macOS and Linux too, no Windows
machine needed:

```sh
dotnet publish src/SellingPoint.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish/win-x64
```

Two arguments help during development and when talking someone through a
problem: `--db=<path>` uses a different database, `--tab=<0-3>` opens straight
onto a screen.

## Setting up the printer

Settings → **Ligação**:

| Choice | Destination | For |
|---|---|---|
| Ficheiros | a folder | No printer. Each slip is written as readable text — how the app is developed on a Mac |
| Rede | `192.168.1.50` or `192.168.1.50:9100` | Ethernet and WiFi printers |
| Porta série | `COM3` | USB printers that install as a virtual COM port |
| Impressora do Windows | the printer's exact name | USB printers that only appear as a print queue |

Then press **Teste de impressão**. Check the accent line: if `áéíóú ãõ çÇ` comes
out as line-drawing characters, change the code page.

**Code page.** The default is **858**, the only widely supported thermal code
page carrying both the Portuguese accents and the euro sign. CP860 is nominally
"the Portuguese one" but predates the euro — on it a price prints as `1,50 E`.
If a printer supports neither, tick *retirar acentos*.

## Layout

```
src/SellingPoint.Core/       cart, money, stock rules — no UI, no I/O
src/SellingPoint.Data/       SQLite schema and repositories
src/SellingPoint.Printing/   ticket building, ESC/POS, transports
src/SellingPoint.App/        Avalonia UI
tests/SellingPoint.Tests/    every rule above, headless
```

Everything except the UI project runs and is tested on macOS and Linux, which is
what makes developing this on a Mac and shipping it to Windows practical.

## Two decisions worth knowing about

**Money is integer cents everywhere.** Never a float, never a decimal. Formatting
is done by hand rather than through a culture, because the app runs in
globalization-invariant mode and because the ticket, the screen and the CSV must
agree exactly.

**Sale lines are snapshots.** The product name, unit price, category and print
settings are copied onto the line when the sale is made. Raise the beer price at
23:00 and the 22:00 ticket, its reprint, and last night's report all still show
what was actually charged.

## Not included

Post-sale voids, discounts, ticket redemption, several tills sharing one price
list, operator logins, card terminal integration, prepaid wristbands. Each is
additive; none needs the above rebuilt.

**Portugal.** This issues internal vouchers and an operational sales report, not
*faturas*. Software issuing fiscal invoices to the public must be AT-certified.
Worth confirming with whoever handles the event's accounting.
