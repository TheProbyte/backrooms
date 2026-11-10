# UI plan: every screen, panel and prompt (2026-11-09)

A review of everything the player reads or clicks, with what to improve and in what order. Where an item changes the
design, `GameDesign.md` changes with the work.

**Where it stands (2026-11-10): every pass is built (0 to 5)**: U1 the kit, U3 the picture run, U12 the developer
switch; U4 the side panels, U5 prompts, U6 notices, U7 co-op; U8 the title screen, U9 one menu language, U10 settings,
U11 worlds and friends; U13 the player's F5, U14 the pixel-screen kit, U15 the boards; U17 small screens, U18 words in
one place; U19 sounds and motion. Details, and what each left undone, under each item. U16 (a controller) is not wanted
yet. Pictures: `Docs/screenshots/ui/pass0/` to `pass5/`. Not yet played by hand.

**How to read it**
- Every item says what was found, what to do, how big it is (**S** under a session, **M** a session or two, **L**
  several) and how to tell it worked.
- **Seen** means I looked at it in the game today (pictures in `Docs/screenshots/ui/`) or read it in the code.
  **Judgement** means my reading of how similar games and the platform rules do it.
- The outline's rules still hold: the camcorder is the frame, state belongs on things where it can, no meters, no
  floating markers over the world.

## What was looked at

Every piece of UI in the code, and six screens in play (main menu, settings, graphics page, pause menu, the F5 camcorder
menu, the L menu).

| What the player sees | Made with | Where | Font |
|---|---|---|---|
| Main menu, save slots, multiplayer page | UI Toolkit | `Menu/MainMenu.cs`, `MainMenu.uss` | a monospace taken from the PC at run time |
| Settings (6 pages), YOUR LOOK | UI Toolkit | `Menu/SettingsMenu.cs`, `LookMenu.cs` | the same |
| F5 camcorder menu (8 tabs, minimap) | UI Toolkit | `Camcorder/CamcorderVisualMenu*.cs`, `CamcorderMenu.uss` | the same |
| Item tuning (F2) | UI Toolkit | `Interaction/Items/ItemTuningMenu.cs` | the same |
| Viewfinder marks: REC, counter, date, battery, LOW LIGHT, notices, IN HAND, pay slip | uGUI | `Camcorder/CamcorderOSD.cs` | Unity's built-in Arial |
| Side panels (work, batches) | uGUI | `CamcorderOSD.cs`, `Contracts.Tracker.cs` | Arial |
| Pause menu (Esc) | IMGUI | `CustomRooms/PauseMenu.cs`, `ImUi.cs` | Arial |
| Hints, pocket strip, focus marks, placing marks | IMGUI | `Interaction/PropGrabber.*.cs` | Arial |
| Co-op: player list (Tab), join and leave notes, name tags | IMGUI | `Net/NetSession.cs`, `NetWorld.cs` | Arial |
| L menu (items, furniture, progression), enemies menu | IMGUI | `Items/ItemsMenu.cs`, `Atmosphere/EnemiesMenu.cs` | Arial |
| Build mode (the room editor) | IMGUI | `CustomRooms/BuilderApp.UI.cs` | Arial |
| Tape transition between scenes | IMGUI | `Menu/TapeTransition.cs` | Consolas, if the PC has it |
| In the world: work board, supply counter, improvements board, seam cards, shelf tickets, receipts, calendar, labels, the training TV | pixel font on textures | `Interaction/Contracts*.cs`, `SeamCard.cs`, `Storage/*.cs`, `HubTvScreen.cs` | the game's own 5 x 7 pixel font |

Numbers that matter (seen in the code):
- **Four UI technologies** and about 6,000 lines of UI code. Three of them draw text the player reads during play.
- **No font ships with the game.** The menus build theirs from whatever monospace the PC has (Consolas, Lucida
  Console, Courier New); everything else is Unity's built-in Arial. The look differs from PC to PC, and on a Steam Deck
  none of those three is installed.
- **No controller support at all** (the word "gamepad" does not appear in the code). Menus are keyboard, the pause menu
  and L menu are mouse.
- **Smallest text:** side panels 13 px, F5 tabs 11 px, F5 hints 12 px, all at a 1080-line screen. On a Steam Deck's
  1280 x 800 that is 8 to 10 px for the whole font, under Valve's floor of 9 px for a single character's height.
- 29 different notices and 12 hand hints are written as text in the code, in capitals, in English only.

## The short version

1. **There is no single look.** The main menu is a chocolate panel with a cursor list in a monospace; the pause menu is
   a centred stack of Arial buttons; the L menu is a third layout; the side panels a fourth. All four are titled
   MENU or nothing. A player meets three different-looking menus in their first minute.
2. **The player's game and the developer's tools are the same game.** L spawns any item, skips tiers and adds $1,000.
   Esc offers REFERENCE ROOM and BUILD A SECTION. F5 is a tuning bench of 8 tabs and dozens of sliders titled MENU.
   None of it is marked as not for players, and all of it is one key away.
3. **The new side panels work but were never designed.** They are plain text in the bars. On a 16:9 screen each bar is
   240 wide, so the text wraps hard and drops to 13 px. The F5 menu draws over the left one (seen today: both showing
   at once).
4. **Text is fragile.** No font of the game's own, no text-size setting, sizes below what a small screen can show, and
   long labels cut off (F5 presets read DEFAUL T, FOUND FOOTAG E; the L menu cuts "Cassette (lunch break m").
5. **Only a keyboard and mouse can play.** For a co-op game meant for friends, a controller and a Steam Deck are the
   two most likely things a friend turns up with.

---

## 1. The foundation (do first: everything else builds on it)

### U1. One kit: fonts, colours, sizes (M)
- **Found:** no shipped font; colours defined three times (`MainMenu.uss`, `CamcorderMenu.uss`, `ImUi.cs`, plus
  literals in `CamcorderOSD.cs`); `ImUi.cs` still carries the Classic look's grey palette, which can no longer be
  chosen.
- **Do:**
  - Ship **two typefaces** with open licences: one for the **camcorder's own display** (REC, counter, date, notices:
    a VCR-style face) and one for **the company's paper and panels** (menus, side panels, slips: a typewriter or
    grotesque face that matches the hub's printed signs). The pixel font stays for screens in the world.
  - **Chosen (the owner, 2026-11-10):** **Pixelify Sans** for the camcorder (from sample C) and **Courier Prime** for
    the paper (from sample A), both under the SIL Open Font License. Samples:
    https://claude.ai/artifact/8Ges6gAKhAx5sSCk8raMaS
  - One table of **colour names** (chocolate, amber, cream, avocado, the four client colours, good, warning, bad) that
    every system reads, and one **size scale** (five steps) with a floor.
  - Delete the grey palette.
- **Worked when:** a screenshot of any screen uses only the two faces and the named colours, on a PC with none of
  Consolas, Lucida Console or Courier New installed.
- **Built (2026-11-10):**
  - `UI/UiKit.cs`: the two faces (uGUI, IMGUI and UI Toolkit; Courier Prime's real bold wired into the UI Toolkit
    asset), the colour names, the size scale (17, 22, 28, 36, 56 px of a 1080-line screen; 17 is the floor, 12.6 px at
    1280 x 800). `Resources/Fonts/`: the four font files and both licences (SIL OFL 1.1). Pixelify Sans is the
    variable font cut into fixed Regular and Bold with fontTools: Unity reads no variable fonts.
  - `Resources/UI/Kit.uss`: the same colours as named variables; `MainMenu.uss` and `CamcorderMenu.uss` rewritten on
    them with the Overflow look as the only look (the Classic navy rules and the `.mm-ov` / `.cc-ov` switch are gone).
    `ImUi.cs` lost the grey palette. The main menu's smallest text is now 17 px (was 13).
  - Where each face goes: the camcorder's marks, notices, F5 and the tape's blue screen in Pixelify; the menus,
    settings, YOUR LOOK, the pause menu, build mode, the L and K menus, F2, the side panels, the in-picture work list,
    the pay slip, hints, pockets, the co-op list and name tags in Courier Prime.
  - `UiKitTests`: the fonts and licences ship, Kit.uss equals UiKit.cs, no literal colour left in the menus' sheets,
    the size floor holds.
  - **Not yet:** sizes elsewhere are unchanged (F5 still goes down to 11 px, the side panels to 13: passes 1 and 3).
    The colours of the boards and paper in the world are their own (U14).

### U2. Fewer technologies (L, spread over the passes below)
- **Found:** Unity's own guidance for 6.3 still names uGUI as the recommended runtime system and UI Toolkit as the
  alternative under active development. This game already has its five menu screens in UI Toolkit and its viewfinder
  in uGUI (it has to be: it is drawn to a texture and curved by the camcorder pass).
- **Do:** keep those two and **take IMGUI out of the player's game**:
  - menus and panels: UI Toolkit (the pause menu, the player list and the side panels move to it);
  - marks inside the picture: uGUI, as now (hints, pockets and focus marks move to it);
  - IMGUI stays only in developer tools and build mode.
- **Why:** one theme file can then style every menu, and one place handles scaling, focus and a controller.
- **Worked when:** with developer tools off, no `OnGUI` runs during play.

### U3. A picture of every screen, taken by the game (S)
- **Found:** today's six pictures took a dozen hand-driven steps, and opening Settings by script showed the main menu
  through it (to check by hand: it may only happen that way).
- **Do:** a `-ui-shots` run, like the benchmark's: opens each screen and state in turn at five window shapes (16:9,
  16:10 at 1280 x 800, 21:9, 32:9, 4:3 bars off) and saves a picture of each.
- **Why first:** every later item is judged by eye. This makes "did it get better, did anything break" a folder of
  pictures to flip through, before and after.
- **Worked when:** one command produces the full set in under two minutes.
- **Built (2026-11-10):** `Debug/UiShots.cs`. `StillRecording.exe -ui-shots <folder> -dev` takes 15 screens (title,
  world select, multiplayer, settings and four of its pages, YOUR LOOK; in a world of its own: the hub, a notice, a pay
  slip, the pause menu, F5, the L menu) at five shapes (16:9, 16:10 at 1280 x 800, 21:9, 32:9, 16:9 with the 4:3
  bars off), 75 pictures and an `index.html` side by side, in 44 to 59 s. It shows the viewfinder marks for itself
  and puts the player's camcorder settings back before closing F5 and before it quits. Without `-dev` it skips the L
  menu (70 pictures). Run it on a build: the editor's game view cannot change size.
- **Seen in the first runs:** settings no longer shows the main menu through it (the old picture was a script
  artefact). At 16:10 the bars are too narrow for the side panels and the work list moves into the picture, top
  right; at 32:9 the side panels' text is far too small (both for U4 and U17). **The owner's build had the viewfinder
  marks turned off** (F5 > camera; saved), which also hides the side panels; this was a saved setting, not a bug.

---

## 2. In play (what is on screen while you work)

### U4. The side panels, designed (M)
- **Found (seen):** plain text; a batch is two lines of words; the bar on a 16:9 screen is too narrow for sentences.
- **Do:** draw them as the camcorder's side display, in the kit:
  - **Left, your batches:** one card each: the client's colour and pictogram (the same four marks as on the boxes), the
    count, and a bar that drains with the time left; green stored, amber take back, red late. IN HAND large at the top.
  - **Right, the work:** the next step as one short line with its key or place; the stage's orders as rows with a
    filling bar instead of "12/20".
  - **Narrow bars (16:9):** icons and numbers only, words on the wide layouts. No text under the size floor.
  - The F5 menu and the panels never draw over each other.
- **Worked when:** on a 16:9 screen, what is due next can be read from the left bar at a glance, with no wrapped
  sentence anywhere.
- **Built (2026-11-10):** `UI/SidePanels.cs` draws them in uGUI from `Contracts.FillSide` (data, no rich text).
  Left: the date, IN HAND large, business time; a card per batch (the client's colour stripe and pictogram from
  `UiKit.Mark`, the same quads as the boxes; name and count; PICK UP, SHELVE 3, STORED, TAKE BACK or LATE in the
  kit's tan, amber, green or red; the time left; a bar that drains, or fills as boxes go up). Right: NEXT and the step;
  the stage with its count; each order as a tick row with a filling bar where it has a target; UNLOCKS; a J key cap.
  16:10 bars (110 to 200 wide) get compact cards on the left only, the work in the picture as before. The panel under
  F5 (left) or F2 (right) hides while that menu is open. **Changed:** the panels no longer depend on the viewfinder
  marks being on (they were off by default, which hid the work for most players). Seen in the picture run at all five
  shapes, with stored, due and late batches put into its throwaway world.

### U5. Hints become prompts (M)
- **Found:** hints are a line of 17 px Arial centred over the bottom of the picture, with the key written in the
  sentence ("HOLD G TO PACK IT BACK INTO ITS CRATE", "RIGHT CLICK: PUT UP THE SHELF HERE · R TURNS IT"). 12 of them,
  each worded by hand.
- **Do:** one prompt component: a **key cap** and a **verb** ("[G] hold  PACK AWAY"), up to three side by side, in the
  camcorder face, always in the same place. The key cap is drawn from the binding, so rebinding a key or picking up a
  controller changes it. The first time a prompt appears it stays longer; after that it is brief.
- **Why:** it is the game's most-read text, and the part a controller needs most.
- **Worked when:** every hint in the game goes through it, and changing a key in Settings changes what it shows.
- **Built (2026-11-10):** `UI/Prompts.cs`. Every hint line still comes from where it is written; `Prompts.Parse` reads
  it into prompts (CLICK and RIGHT CLICK as the mouse with that button filled, HOLD and LET GO as a gold tag, MOUSE as
  the mouse, single keys, 1-4, W/A/S/D as a key cap; anything else a plain note), drawn as cap and verb in Courier
  Prime over the bottom of the picture. Key labels come from one table (`KeyLabel`): no rebinding exists yet, so that
  table is where it will go. Tests cover every hint form in the game. **Not done:** "longer the first time" (hints
  show while you aim at something, so they already stay as long as needed).

### U6. Notices queue instead of replacing each other (S)
- **Found:** one notice shows at a time, in the middle of the picture, for four seconds; a second one replaces the
  first. With co-op the host's notices now arrive on everyone's screen, so they collide more.
- **Do:** a short queue (three at most, oldest dropped), two weights: **titles** in the middle for what changes your
  plan (DUE BACK, LATE, NEW CLIENT FOUND) and **lines** at the lower left for the rest (ORDERED, 2 MORE). A friend's
  name in their colour when it is about them.
- **Worked when:** storing a batch while a friend buys something shows both.
- **Built (2026-11-10):** `CamcorderOSD.Title` (news that changes the plan: everything through `Contracts.Tell`, a
  co-op host's notices, FRAGILE BOX BROKEN, ORIENTATION DONE, HOME) queues three at most, each shown in turn (3 s while
  others wait); `CamcorderOSD.Notice` is now a line at the picture's lower left, three at once, the same line again
  refreshed instead of stacked. Seen in the picture run: a title and two lines together. **Not done:** a friend's name
  in their colour (notices do not say who caused them yet).

### U7. Co-op on screen (S to M)
- **Found:** the Tab list is a navy box (the old look) in Arial; name tags are 15 px labels; nothing shows who is
  talking.
- **Do:** the list in the kit, with each player's colour, where they are (HUB, FLOOR, a client's room) and their
  ping; a small mark by a name tag while that friend speaks; the join address on the pause menu kept, with a COPY
  button.
- **Worked when:** with two players, you can tell who is speaking and where they are without asking.
- **Built (2026-11-10):** the Tab list in the kit (a chocolate panel with the amber frame and stripe; each player's
  colour chip, name, where they are: HUB, FLOOR or a client's room, from `NetWorld.PlaceOf`; ping, red over 150 ms);
  three talk bars beside a name in the list and on the name tag while that player speaks (`NetWorld.TalkOf`, the
  voice level the mouths already use); COPY beside each join address on the pause menu. Seen in a two-copy loopback
  game (`-net-host -work-test -coop-shots <folder>`): the list and the tag. **Not seen:** the talk bars (no microphone
  in the test).

---

## 3. The menus

### U8. A title screen (S to M)
- **Found (seen):** the first thing the game shows is a panel titled MENU over a tilted, blurred shot of carpet. The
  game's name is nowhere. The viewfinder date reads OCT 19 1998 here and MAR 1996 in play. The panel sits half on the
  black bar and half on the picture.
- **Do:** the name, in the company's own lettering; the panel inside the picture or inside the bar, not across the
  join; a composed backdrop (the hub's lobby on standby, the lift hall); the build's version small in a corner; the
  date matching the world's.
- **Decided (the owner, 2026-11-10):** yes to a title screen with the game's name. **The name is open:** Overflow is
  already taken (a Japanese game studio of that name, and a Steam game called Overflow: Number Crunch), so the title
  screen shows OVERFLOW as a stand-in until the owner picks a name. The company in the game stays Overflow.
- **Worked when:** a picture of the title screen could be the store page's first image.
- **Built (2026-11-10):** the name (`MainMenu.GameName`, OVERFLOW for now) large in the company amber with the stripe
  and STORAGE & RETURNS · SINCE 1961 under it; the menu under that, both placed inside the picture whatever the bars
  (`UiKit.BarShare`); BUILD 0.1.0 small in the corner; the camcorder's date MAR. 11 1996 (the world's first morning)
  instead of OCT 19 1998; the side panels kept off it. **Kept:** the backdrop is still the camcorder on its side on the
  carpet (a composed hub shot is not done).

### U9. One menu language (M)
- **Found (seen):** main menu and settings are a keyboard cursor list; pause is mouse buttons in another font;
  both are titled MENU.
- **Do:** the pause menu rebuilt as the main menu is (same panel, same list, same keys, mouse as well), titled PAUSED.
  Every menu works with keyboard, mouse and controller alike. Every destructive choice (erase a world, quit to menu
  while hosting) asks once, the same way.
- **Worked when:** the pause menu and the main menu look like the same game in a side-by-side picture.
- **Built (2026-11-10):** the pause menu is UI Toolkit with the main menu's panel, list, cursor and keys (arrows, W S,
  Enter; the mouse as well), titled PAUSED, inside the picture; co-op lines and each address with COPY; QUIT and SAVE
  AND END THE GAME (hosting) ask once (a second press within four seconds), as ERASE on a world does. Every page of the
  main menu now has its own heading (WORLDS, PLAY WITH FRIENDS, HOST A WORLD, JOIN A GAME), never MENU. **Not done:**
  the developer menus (L, K, F2, build mode) stay IMGUI: they are not the player's.

### U10. Settings that read as settings (M)
- **Found (seen):** values sit wherever the label ends (QUALITY ◄ ULTRA ►, RESOLUTION ◄ 100 % ►), so nothing lines
  up; sliders are numbers only; the six pages are full-size list items.
- **Do:** two columns (label left, value right, aligned), a bar under number values, the description of the selected
  line in one fixed place, the pages as tabs along the top. New lines the review calls for:
  - **TEXT SIZE** (three steps) and **PANEL SIZE**;
  - **HOLD OR TOGGLE** for crouch, crawl and carrying;
  - **FLASHING** (the red lamps and blinking notices can be made steady);
  - **SUBTITLES**, ready for the PA voice and the orientation tape;
  - **DEVELOPER TOOLS** (U12).
- **Worked when:** every value on a page starts at the same column, and text size changes every screen at once.
- **Built (2026-11-10):** `Menu/SettingsMenu.cs` rewritten: the pages are tabs (SOUND, CONTROLS, COMFORT,
  ACCESSIBILITY, GRAPHICS, SCREEN, KEYS; Q and E or Tab, or a click); label and value in two columns, ◄ ► clickable,
  a bar under amounts; the chosen line's description in one place. New: **TEXT SIZE** (normal, large 115 %, larger
  130 %: the menus through a run-time copy of the panel settings, `UiKit.Panel`, the IMGUI screens, the side panels and
  the prompts; the camcorder's own marks keep their size), **CROUCH** and **CRAWL** each PRESS or HOLD, **FLASHING**
  (STEADY: REC, notices going, LOW LIGHT and the shelf lamps' red stay lit), **SUBTITLES** (on by default;
  `CamcorderOSD.Subtitle` is ready, nothing speaks yet), DEVELOPER TOOLS on the KEYS page, which lists every key in two
  columns. **Not done:** PANEL SIZE (TEXT SIZE covers the panels); HOLD OR TOGGLE for carrying (taking is already a
  click, and pushing a hold).

### U11. Worlds and friends (S each)
- **Do:** save slots as cards with what a player wants to know (the day, money in hand, stage, time played, last
  played); the multiplayer page with the address field, recent hosts, and a plain reason when a join fails.
- **Built (2026-11-10):** each world a card: its own date on the camcorder, money in hand, its stage, time played,
  last played (the seed only with the developer tools on). JOIN A GAME keeps the last three hosts as one-press entries,
  and a failed join says what to try (check the address and port, that the host's world is open, the same VPN).

---

## 4. Player and developer, pulled apart

### U12. Developer tools behind a switch (S)
- **Found (seen):** the L menu says "Developer menu" in its own text and offers SKIP TO TIER 3, UNLOCK EVERYTHING and
  +$1000; the pause menu offers REFERENCE ROOM and BUILD A SECTION; V flies through walls; F2 tunes items.
- **Do:** one switch, off by default (a line at the foot of Settings, and `-dev` on the command line). Off: L, V, F2
  and the two pause entries do nothing, and KEYS does not list them. On: a small DEV mark in the viewfinder.
- **Decided (the owner, 2026-11-10):** build mode and every developer tool are for the owner, not players. Build mode
  (REFERENCE ROOM, BUILD A SECTION) goes behind the same switch.
- **Worked when:** with the switch off, a player pressing every key cannot spawn, skip or fly.
- **Built (2026-11-10):** `UI/DevTools.cs`. The tools exist only in the editor, a development build, or with `-dev`;
  there they start on, and Settings has a DEVELOPER TOOLS line to turn them off (saved). L, K, V, F2, REFERENCE ROOM
  and BUILD A SECTION answer only while on; a DEV mark sits under SP in the viewfinder. F5 and F6 stay for players
  until the player's F5 (U13). Checked: the release build run without `-dev` has no DEV mark, no developer entries in
  the pause menu and no DEVELOPER TOOLS line. The keys themselves were checked in the code, not pressed in the build.

### U13. The camcorder's menu becomes a camcorder's menu (M)
- **Found (seen):** F5 opens CAMERA, PLAYER, WORLD, AUDIO, SOUND, DISPLAY, LIGHT, PLACES with dozens of tuning
  sliders, preset buttons whose labels do not fit, and 11 px tabs.
- **Do:** split it. **For the player:** what a camcorder has: picture preset (four, with names that fit), date and
  counter on or off, the side panels, the minimap, and quick travel. **For the developer** (behind U12): everything
  else, as it is now.
- **Worked when:** the player's F5 fits on one page with nothing cut off, and nothing in it can break the picture.
- **Built (2026-11-10):** `Camcorder/CamcorderPlayerMenu.cs`, a Handycam's own short menu (MENU · CAMERA SET, as a
  90s camcorder's PROGRAM AE, DISPLAY and 16:9 WIDE): PROGRAM AE (STANDARD, VHS, HI8, MINIDV: the picture presets,
  keeping the player's marks, shape and sound), DISPLAY on or off, PICTURE 4:3 or 16:9 WIDE, WORK PANELS, MAP; in the
  camcorder face, inside the picture. F5 opens it; the tuning bench (`CamcorderVisualMenu`) is the developer's, a
  DEVELOPER BENCH line at its foot while the developer tools are on (and Settings' CAMCORDER MENU (ADVANCED) likewise).
  **Not done:** quick travel (moving yourself round the floor is a developer's tool here).

---

## 5. Screens in the world

### U14. One kit for the pixel screens (M)
- **Found:** eight systems each lay out their own pixel screen by hand, with their own colours and margins. They are
  the most in-keeping UI in the game and the best-liked direction (state on things).
- **Do:** a small shared layout for them (header band, rows, chips, a big number, a status lamp), the client's
  pictogram wherever a client's colour is used, and one check: **each screen readable through the camcorder at the
  distance it is used** (a ticket at 1.5 m, a board at 3 m, a seam card at 2 m), measured in the picture's pixels.
- **Worked when:** a picture of each from its using distance can be read without stepping closer.
- **Built (2026-11-10):** `Generation/PixelScreen.cs`: the rule (a letter must cover 7.5 of the camcorder's 480
  recorded lines over its 54.5° view, at the distance the thing is used; Xbox asks 26 px of body height for flat UI and
  nothing agreed exists for text in the world, so it is measured in the picture) and the shared parts (stripe, header,
  bar, text cut to fit or to two lines, a client's pictogram). Measured before: the hub boards' 1-dot text 5.2 lines from
  3 m, the seam card 5.4 from 2 m, the shelf ticket 6.9 from 1.3 m, the receipt 6.7 from 0.8 m, the calendar's month 4
  from 2 m. Fixed: the boards draw nothing under 2 dots; the seam card 0.44 to 0.62 m wide, the ticket 0.24 to 0.27 m
  (wider would hang over the boxes below), the receipt 10.5 to 13 cm, the calendar 0.2 to 0.3 m. The training TV
  (33 cm) cannot be read from a seat: the tape's words now also come as subtitles. `UiPass3Tests` checks each screen
  against the rule and that the boards draw no 1-dot text. **Seen** in the picture run: the three hub boards from 3 m.
  **Not seen:** the seam card, ticket, receipt and calendar at their new sizes (they are on the floor; the run stays in
  the hub).

### U15. The work board and the two shop boards (M)
- **Do, with U14:** the supply counter and improvements board show price, what you have, and what is locked and why
  as rows (now: one item at a time, stepped through with a button); the work board gains the same batch cards as the
  left panel, so the hub and the camcorder say the same thing the same way.
- **Built (2026-11-10):** the work board's rows are the side panel's cards in pixels (the client's colour and mark,
  whose and how many, what to do and how long, a bar that drains or fills); its header's second line, the four numbers'
  labels (SAFE, AT RISK, DUE, IN HAND) and the next step at 2 dots, the step cut at a word. The shop boards (they were
  already rows) show six, with what is locked as AFTER ... at 2 dots and the picked one's note on two lines at 2 dots.
  **Not done:** "what you have" (how many of a thing you own) on the supply counter.

---

## 6. Reach: controller, small screens, other languages

### U16. A controller (L)
- **Found:** none. The game uses the new Input System already, read key by key.
- **Do:** actions instead of keys (move, look, take, use, crouch, crawl, menu), bound to keyboard, mouse and gamepad;
  every menu navigable with a stick; prompts (U5) showing the right glyphs; the camcorder's two-handed actions given
  sensible buttons.
- **Decided (the owner, 2026-11-10): not yet.** Out of the passes for now. Pass 1's prompts (U5) still draw keys from
  one table, so glyphs can be added later without touching every hint.

### U17. Small screens (S, after U1 and U4)
- **Do:** a floor on text of 12 px at 1280 x 800 (Valve asks for 9 at least and recommends 12), checked in the
  pictures from U3; the side-panel layout for 16:10.
- **Built (2026-11-10):** the floor is the kit's Small, 17 px of a 1080-line screen (12.6 at 1280 x 800); everything
  the player reads in the menus, the player's F5, the side panels and the prompts is at or over it, checked by
  `UiPass4Tests` (the stylesheets' sizes and the panels' and prompts' own). On 16:10 the left bar holds compact batch
  cards (pass 1) and the work list in the picture now keeps to the next step and the orders (it used to pop up the
  batches too, a block of small text over the picture). Seen in the picture run at 1280 x 800 on every screen.
  **Not covered:** the developer's screens (the F5 bench's 11 to 14 px, the L and K menus, build mode).

### U18. Words in one place (M)
- **Found:** every string is written where it is used.
- **Do:** move player-facing text into one table, keyed, so wording can be edited in one file and another language is
  possible later. No translation now.
- **Decided (the owner, 2026-11-10): languages later.** This stays "one file for wording" (still worth it for the 29
  notices); no translation work until asked.
- **Built (2026-11-10):** `Resources/Text/Words.csv`, 95 keys: every title and line (notices), every note (the hands
  could not), every hint and the names of things the hints use, and the co-op join and leave notes; read through
  `UI/Words.Get(key, values)` ({0}, {1} for the values). The file is in the layout Unity's Localization package imports
  as a string table (Key, Id, English(en)), so a language would be a column. A missing key shows as [key], never blank.
  `UiPass4Tests` checks that every key the code uses is in the file. **Not moved yet:** the menus' and settings' own
  words, the boards' and cards' words, the tape's cards, the work orders' titles.

---

## 7. Feel

### U19. One set of interface sounds, and how panels move (S)
- **Found:** beeps are made in several places with different tones (the boards, the vending machine, the till); the
  menus I opened today appeared and vanished without a transition.
- **Do:** five sounds in the camcorder's voice (move, select, back, refuse, confirm) used everywhere; panels slide or
  wipe in 120 ms like a camcorder's menu; REDUCE MOTION turns that off.
- **Built (2026-11-10):** `UI/UiSounds.cs`, five sounds made in code in the camcorder's beep (a small square wave, its
  top rolled off): a move the lightest tick, a select rising, back falling, a refusal a low buzz twice, a confirm a
  brighter rising chirp. The main menu, settings, YOUR LOOK, the pause menu and the player's F5 use them, and the hub's
  boards play confirm and refuse from their own speaker in the world. `UiKit.Enter` and Kit.uss's `.kit-enter`: the
  main menu (and the name), settings, YOUR LOOK, the pause menu and F5 come in over 120 ms from 14 px to the left,
  see-through to solid (USS transitions). COMFORT has MENU MOTION (SLIDE or STILL); REDUCE MOTION sets STILL. Seen: a
  picture three frames into the pause menu's slide. `UiPass5Tests`. **Not done:** the side panels, notices and pay slip
  keep their own motion (they fade, blink and slide already); the developer's screens keep their sounds.

---

## The order

Each pass ends with the pictures from U3 taken again and the owner looking at them.

| Pass | What | Items | Done when |
|---|---|---|---|
| **0. Ground** (built 2026-11-10) | The picture run, the kit, the developer switch | U3, U1, U12 | Before-pictures exist; two shipped fonts; a player cannot reach a developer tool |
| **1. In play** (built 2026-11-10) | Side panels, prompts, notices, co-op marks | U4, U5, U6, U7 | "What is due next" reads at a glance on 16:9; every hint is a prompt |
| **2. Menus** (built 2026-11-10) | Title, one menu language, settings, worlds and friends | U8, U9, U10, U11 | Main and pause menus look like one game; text size is a setting |
| **3. Camcorder and world** (built 2026-11-10) | The player's F5, the pixel-screen kit, the boards | U13, U14, U15 | Nothing cut off in F5; every world screen readable at its distance |
| **4. Reach** (built 2026-11-10) | Small screens, words in one place | U17, U18 (U16 not yet) | 12 px floor at 1280 x 800; wording in one file |
| **5. Feel** (built 2026-11-10) | Sounds and motion | U19 | One set of sounds everywhere |

IMGUI leaves the player's game as its screens are rebuilt (U2): the pause menu in pass 2, hints and pockets in pass 1,
the co-op list in pass 1.

## For the owner to decide

Answered 2026-11-10:
- **Developer tools and build mode** (U12): for the owner only. Off by default in the player's game.
- **A controller** (U16): not yet.
- **Another language** (U18): later.
- **A title screen** (U8): yes. The name itself is open (Overflow is taken).
- **The two typefaces** (U1): Pixelify Sans for the camcorder, Courier Prime for the paper (a mix of samples C and A:
  https://claude.ai/artifact/8Ges6gAKhAx5sSCk8raMaS).

Still open:
1. **The game's name.**

## Small things seen today (to fix on the way, whatever the order)

- ~~The F5 menu and the left side panel draw over each other~~ (the panel hides under F5, pass 1).
- F5 preset buttons cut their labels (DEFAUL T, FOUND FOOTAG E, REFERE NCE MATCH, LOW LIGHT GAIN clipped): still so on
  the developer's bench; the player's F5 (pass 3) has none.
- The L menu cuts "Cassette (lunch break mix)".
- ~~The title screen's date is OCT 19 1998; the game's is MAR 1996~~ (pass 2).
- ~~Settings opened by script showed the main menu through it~~ (not so: the picture run shows settings alone).
- ~~Every menu is titled MENU~~ (pass 2).
- ~~`ImUi.cs` keeps the retired Classic palette~~ (gone, pass 0).

## Sources

- Unity's comparison of its UI systems (6.3): https://docs.unity.com/en-us/engine/6000.3/manual/uitoolkits/ui-system-compare
- Valve's Steam Deck compatibility checklist (text 9 px at least at 1280 x 800, 12 recommended; controller glyphs):
  https://partner.steamgames.com/doc/steamdeck/compat
- Xbox Accessibility Guidelines 101 (text display), 102 (contrast), 104 (subtitles):
  https://learn.microsoft.com/en-us/gaming/accessibility/guidelines ,
  https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/102
- What Lethal Company's interface leaves out: https://indieklem.substack.com/p/11-whats-behind-the-interface-of
- Hardspace: Shipbreaker's diegetic interface (GDC 2021): https://gdconf.com/news/go-behind-fully-diegetic-ui-hardspace-shipbreaker-gdc-2021
