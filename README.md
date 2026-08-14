# SellingPoint

A till for village festivals, association parties and school events. Big-button
touch screen, your own categories and prices, and slips printed on a cheap
thermal printer for people to hand in at the bar or the kitchen.

Windows program, single file, no installer. Runs offline on one laptop.

## What it does

- **Sell** — tap categories and products, take cash with change calculation or
  record a card payment, print.
- **Print by group** — every category has a *print group*. By default each
  category has its own, so one order prints one ticket per category: the customer
  hands the drinks slip to the bar, the food slip to the kitchen and the dessert
  slip to the dessert stand, and nothing arrives at the wrong counter. Every slip
  carries the same ticket number, so they stay recognisably one order.
  Combining is the opt-in: give two categories the same group name in Gestão and
  they share a slip.
- **Senhas or lists** — per category: `3x Cerveja` as one line, or three separate
  slips for the bar to collect, one per drink.
- **Change everything** — categories, colours, products, prices, order, stock.
  All in the app, no config files. Products are listed one category at a time,
  so you open Bebidas, see the six drinks you have, and add another without
  wading through everything else.
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
dotnet test                                      # 168 tests
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

## Fullscreen

The app starts fullscreen. On Windows that covers the taskbar and removes the
title bar, so there is nothing outside the app to tap by accident — no minimise,
no close, no Start menu.

Getting out: **F11**, or the **Sair do ecrã inteiro** button at the top right.
The button exists because Windows shows *nothing* in fullscreen — unlike macOS,
there is no reveal-on-hover title bar, so without it nobody at the event could
find a way out. Alt+F4 still closes the app either way.

Note the platform difference when developing: macOS maps fullscreen to a *native*
fullscreen Space, so it slides onto its own desktop and the menu bar reappears if
you push the cursor to the top. Windows does neither.

## Setting up the printer

Settings → **Ligação**:

| Choice | Destination | For |
|---|---|---|
| Ficheiros | a folder | No printer. Each slip is written as readable text — how the app is developed on a Mac |
| Rede | `192.168.1.50` or `192.168.1.50:9100` | Ethernet and WiFi printers |
| Porta série | `COM3` | USB printers that install as a virtual COM port |
| Impressora do Windows | the printer's exact name | USB printers that only appear as a print queue |

Pick the connection and the list below it fills with what the machine actually
has — the printers Windows knows about, or the COM ports — and tapping one fills
in the destination. The exact name never has to be typed.

Then press **Teste de impressão**. Check the accent line: if `áéíóú ãõ çÇ` comes
out as line-drawing characters, change the code page.

On a serial connection, **Procurar** goes further and asks each port which one
answers like a printer, so the port never has to be looked up in the Gestor de
Dispositivos. Note that COM1 and COM2 are usually the motherboard's own legacy
ports; a USB printer that presents as a COM port lands on COM3 or higher, so
finding only those two means the printer is a Windows print queue instead.

The Windows print-queue path is one-way, so on it the app can print but cannot
read status — no *sem papel* or *tampa aberta*. Serial and network can do both.

## When the printer stops mid-event

USB thermal printers on Windows fail in a specific way: the device briefly
re-enumerates — a knocked cable, a power dip, a different USB socket — and
Windows hands it back on a **different COM number**. The app is still pointed at
COM3 while the printer is now COM7. That is what the trip into Device Manager is
usually fixing.

The app handles it in four layers, so a dead printer costs a delay rather than a
ticket:

1. **Nothing is printed directly.** Every slip is encoded and put in a queue in
   the database first. Sell as normal while the printer is down; the queue drains
   itself, oldest first, the moment one answers. It survives the app being closed
   and reopened.
2. **The port is found again.** After three failures in a row the app scans the
   COM ports, adopts whichever one answers like a printer, and saves it. COM3
   becoming COM7 becomes invisible. Other ports are only ever probed *after* the
   configured one has failed, so a working till never writes to a port belonging
   to something else.
3. **The printer is asked what is wrong.** ESC/POS real-time status
   (`DLE EOT`) distinguishes *sem papel* from *tampa aberta* from *não
   respondeu* — the difference between a ten-second fix and ten minutes of
   guessing. Replies are validated against the fixed bit pattern every status
   byte carries, so another device on that port cannot be mistaken for a healthy
   printer. Printers that ignore the command are treated as *unknown* and printed
   to anyway.
4. **A light on the till.** A coloured chip in the top bar shows the state and how
   many slips are waiting. Tapping it opens the diagnostics without leaving the
   sales screen: what is wrong, what to do about it, what is queued, which ports
   have something on them, and buttons for retry, re-scan, pause and test.

Status queries need to read as well as write, so they work on **serial and
network only** — the Windows print-queue path is one-way, and the file transport
has nothing to ask.

**Ethernet retires this whole problem.** There is no port enumeration on the
network path, so nothing can be renumbered. If the printer has an Ethernet jack,
moving to it is a settings change: pick *Rede*, enter the address.

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
  Styles/Tokens.axaml        every colour and measurement, in one file
  Styles/Controls.axaml      shared control styles
tests/SellingPoint.Tests/    every rule above, headless
```

## Look and feel

The interface is built for one situation: a volunteer working a touch screen, at
night, outdoors, with a queue in front of them.

- **Surfaces are a warm near-black, not a cold one.** Colour on top then reads as
  lit rather than printed on slate, and each step up the scale reads as "closer
  to the front".
- **Every product button is a gradient built from its category's colour.** The
  organizer picks one hex in Gestão and everything else is derived, so there is
  never a second colour to keep in step with the first. Flat fills read as paper;
  a top-lit gradient reads as a button.
- **Money is amber, everywhere.** The total, the change, the confirm button. The
  number the whole queue is looking at is never the same colour as anything else
  on the screen.
- **Everything tappable is at least 56px, spaced at least 8px.** 56 rather than
  the 44px web baseline because a till is worked standing, at speed, without
  looking down. Product buttons are 208x136. A thumb that hits "remove" instead
  of "minus" costs a real argument at the counter.
- **Every button has a pressed state.** On a touch screen there is no hover, so
  without one a tap that did nothing and a tap that worked look identical.
- **Colour carries meaning consistently.** Cash is green, card is blue, a printer
  problem is red — in the buttons, the totals and the status light alike.
- **Destructive actions are quieter than primary ones.** Apagar is outlined
  rather than filled: findable, clearly marked, not competing with Guardar.
- **Every colour and size lives in `Styles/Tokens.axaml`.** Changing the accent
  or the touch-target size is one edit, not a search across views.

All foreground/background pairs clear WCAG AA (4.5:1); muted text on a panel is
7:1.

### The accent problem

Avalonia's default line box clips the accent above cap height, so `Água` renders
as `Agua` and `SESSÃO` as `SESSAO`. Lowercase `é`/`ã` survive because their
accents fit inside the ascender, and `Ç` survives because its cedilla hangs below
the baseline — which is what makes the bug easy to miss.

It is not the font and not the font size: a probe showed 17px and 46px both
clipping, and `LineSpacing` at any value not helping. Only an explicit
`LineHeight` fixes it, at roughly 1.75x the font size.

So every place that renders a **user-entered name** — product buttons, category
chips, cart rows, report rows, the admin lists — sets `LineHeight` explicitly.
Fixed UI labels are written in sentence case instead, which sidesteps it.

This was observed on macOS and could not be verified on Windows. The explicit
line heights are harmless either way.

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
