# Overflow: the game design (working title)

The one place that says what the game is **now**. The owner and Claude keep it up to date: when the design or the
scope changes, this doc changes first (or with the work). How the game used to work, and why it changed, is in
`Docs/DesignHistory.md`; how things were made is in the other docs (see the end).

**How to read it**
- Each part says where it stands: **[built]** is in the game, **[planned]** is agreed but not made, **[draft]** is a
  proposal still to agree, **[open]** is a question.
- **Checked by script** means a test drove it in Play; **played by hand** means someone played it with mouse and keys.
  Only playing by hand says whether something feels good.
- Numbers are where things stand now, to be tuned in play.
- The game was "Still Recording" until 2026-11; it is now **Overflow** (also a working title). Overflow is already a
  Japanese game studio's name and part of a Steam game's title, so the game's own name is still to be picked; the
  company in the game stays Overflow.

---

## 1. The game in a paragraph

You work for Overflow, a storage company with a strange product: the world is full, and behind it there is more world,
so Overflow rents that space out. You start in the company's empty, automated office (the hub), get your gear at the
supply counter and ride the service lift down to your floor of rented space in the Backrooms. Out in its halls you find
seams (taped-off doorways where the walls are thin) into ordinary real-world places, and each seam is a client with
business to offer. Take a batch of their boxes, carry it through, keep it safe on lit shelves for the time agreed, then
take it back through the same seam when it is due. A returned batch frees the shelves for more. What you earn buys the
equipment to handle more business at once, step by step, like Satisfactory's milestones: a hand truck, trucks, chutes,
conveyors, and later powered racks that store and release batches for you. You see it all through the company's work
camcorder. Alone or with up to three friends.

## 2. What it should feel like (pillars)

1. **Through the camcorder.** You are the camera: a 90s work camcorder with its own picture, sound, date stamp and
   handheld motion. "If it isn't on tape, it didn't happen." [built]
2. **Everything is physical.** Boxes, carts, sofas and doors are pushed, carried and stacked by hand, with weight and
   sound. No floating inventory grid: two hands, four pockets, a backpack, carts. [built]
3. **Build up a place and keep it.** Your floor, your storage, your chutes and shelves stay. Progress is something you
   can walk around in, like a Satisfactory factory. A room set up properly stays safe; upkeep comes back only when
   something new happens, never as a chore. [built in part]
4. **Business on a schedule.** Every batch has a plain clock: how long until it is due back, and how long you have to
   return it. The pressure comes from batches that overlap (some coming in, some stored, some going back), not from a
   closing bell. [built]
5. **Uncanny against ordinary.** The Backrooms are yellow, humming, empty and endless; the real-world places through
   the seams are clean, cool, daylit and full of normal life sounds. The contrast is the point. [built]
6. **Eerie, not a horror game (for now).** No monsters in the current scope: the old atmosphere director (the shadow
   that followed you, its power dips and flickers) is switched off in code (TensionDirector.Enabled, 2026-11-09). The danger is the building: getting lost
   and the dark. [built]
7. **No survival meters.** No hunger, no thirst, no battery to keep charging, no comfort meter, no sanity meter. [built]
8. **A floor that runs.** The goal is a dependable operation you build and then extend, until it runs while you explore.
   An upgrade gives real relief: the game never quietly adds work to a route just to use up the time it saved. [planned]

## 3. The world and the story

- **The company.** Overflow was set up in the 60s, done up in the bright 70s look (amber, orange, gold, avocado on dark
  brown), and by the 90s it runs itself: a PA voice, a board, a pay window, an old punch clock. Nobody else works there.
- **The company's voice** is the man in the orientation tape: a character the owner is making, a male voice. To the
  players he sounds like a boss giving them work. They do not realise it is all old and pre-recorded, and that they are
  working for someone who probably no longer exists. His voice gives the orders and the PA lines. [planned]
- **Its slogans:** WORK HARD. WORK FAST. GET IT BACK ON TIME. / THERE'S ALWAYS MORE ROOM. / IF IT ISN'T ON TAPE, IT
  DIDN'T HAPPEN. [built on the tape; signs to update]
- **Time behind the seams:** time runs differently on the other side. A client pays for weeks of storage that pass in
  minutes on your floor. The tape says so in a line; in the game you see a plain countdown on each batch. The
  camcorder's date follows the clients' side: it starts on MAR 11 1996 and moves on about a day for each minute of
  business time, while the time of day runs as a normal clock. [built]
- **The orientation tape.** A training video plays in the hub's training room when everyone sits down. Its cut is being
  made separately [planned]; until it is in, a stand-in of title cards plays on the TV (about 28 s), and watching it to
  the end is the first work order [built]. The cards: find a seam, a client is behind it; bring their boxes in and shelve
  them in the light; a third is paid when they are safe; time runs fast behind the seams; take them back when they are
  due, the rest is paid then; keep your shelves lit, the dark scuffs the rest; the power bill is yours; get it back on
  time. The real tape must say the same, plus how a shelving kit is set up. [planned]
- **The mystery.** The manager's office is locked; its calendar stopped in March 1979 and his chair faces the wall.
  "What are we even working for?" What is behind it, and whether the game has an ending, is [open].
- **The clients** (all invented, never real brands), each with its own tape colour on its boxes:

| Client | Place behind its seam | Tape | Boxes |
|---|---|---|---|
| TALLMORE WHOLESALE | a warehouse: racking, a roller door, a pallet jack | blue | plain medium, 0.5 x 0.36 x 0.4 m, 8 kg |
| QUILLBY PRINT & COPY | a print shop: a press, paper stacks, a guillotine | red | small and heavy (paper), 12 kg |
| PAPERLARK OFFICE SUPPLY | an office supply shop: shelving full of stock, a counter and register | green | small (5 kg) or medium (7.5 kg) |
| BELLWEATHER HOME FURNISHINGS | a furniture showroom: sofas, lamps, a SALE banner | plum | long flat-packs, 1.0 x 0.12 x 0.45 m, 13 kg |

## 4. The look and sound

- **The Overflow look:** chunky, rounded, low-poly shapes in a few flat colours, painted with big soft patches and
  baked dirt, lit by the game's real lights, seen through the camcorder. References: Lethal Company, PEAK, RV There Yet?
  The full rules are in `Docs/StyleSheet.md`. [built]
- **The hub's style is the house style:** seventies office furniture, fat cushions, walnut plinths, chunky tubes. All of
  Level 0's furniture is remade in it. [built]
- **One look.** The Overflow look is the game's look. (The earlier realistic "Classic" look was a setting until
  2026-11-09, when the owner retired it: `DesignHistory.md`.) [built; its unused textures still to come out of the build]
- **The walls are painted like the hub's** (the owner, 2026-11-09: "i like the hub walls"): Level 0's mustard wallpaper
  is a printed pattern, a half-drop repeat of lighter stripes with chevrons and diamonds in slightly off-register ink, a
  faint dotted ground between; the homes' blue-sky paper is painted cumulus in three flat values, and their orange-peel
  drywall is bold soft bumps in big patches. The wallpaper's contrast is carried by its dark inks (the under-print, the
  pin lines, the stripe's tone), so the print still reads under the bright panels. [built]
- **Sound:** the building's hum and air, fluorescent buzz, footsteps by floor, sound that goes round corners through
  doorways, muffled places behind the seams, a radio and traffic inside them, the camcorder's microphone on everything.
  Crackly muzak and the PA voice in the hub. [built; muzak and PA voice planned] Nothing clicks or scrapes as a place
  loads: lamps that start plugged in and plugs put back from the save go in silently, and furniture settling after it
  appears is not heard. [built 2026-11-09]
- **On-screen notices** (the camcorder's titles, centre-low) wrap onto two lines and shrink to fit inside the picture;
  never cut off at the sides. [built 2026-11-09]
- **The interface's two typefaces** (the owner, 2026-11-10), shipped with the game so every PC shows the same letters:
  **Pixelify Sans** for the camcorder's own marks (REC, the counter, the date, notices, LOW LIGHT, the F5 menu, the
  tape's blue screen) and **Courier Prime**, a typewriter face, for the company's paper (the menus, settings, the pause
  menu, the side panels, prompts, the pay slip, the co-op list). The 5 x 7 pixel font stays on the boards and screens
  in the world. Every screen takes its colours from one table of names (chocolate, amber, cream, tan, the stripe's
  orange, gold and avocado, good, warning, bad). [built 2026-11-10]
- **The developer's tools are not the player's** (the owner, 2026-11-10: "build mode and dev is for me technically,
  not future players"): L (items, PROGRESSION), K (enemies), V (flying), F2 (item tuning) and build mode (REFERENCE
  ROOM, BUILD A SECTION) exist only in the editor, a development build, or with `-dev` on the command line; there they
  can be turned off in Settings (DEVELOPER TOOLS), and a small DEV in the viewfinder shows they are on. [built
  2026-11-10]
- **What is on screen while you work** (Docs/UiPlan.md pass 1): in the black bars beside the 4:3 picture, your batches
  as cards on the left (the client's colour and mark, how many, what to do now, the time left as a draining bar) and
  the work on the right (the next step, this stage's orders as tick rows with filling bars); hints as key caps and
  verbs; news that changes the plan as a title in the middle (queued, never replacing each other), the rest as small
  lines at the lower left; in co-op a player list with where everyone is, and talk bars by a name while that friend
  speaks. The side panels show whether or not the viewfinder marks are on. [built 2026-11-10]

## 5. The places

### The hub [built]
A one-floor 70s company office, about 22 x 22 m, the same in every world, safe but eerie. **Business time stops while
everyone is in it** (and behind the seams: section 6).
- **First-aid room:** four cots (one per player). You arrive on a conveyor through a return chute behind your cot and
  wake there when the world loads, lying on your back. **Getting up is one move seen through your own eyes** (about
  three seconds): you sit up, the view coming down off the ceiling; slide down the mattress to its foot, your legs
  going over the end; lean forward and stand. Friends see you lying on the belt and the cot, and getting up.
  [built 2026-11-09]
- **Locker room:** avocado lockers and a bench (decoration for now; storage in them is [open]).
- **Lobby:** the work board, the pay window (PAY SLIPS), the supply counter, the improvements board, the OVERFLOW sign,
  reception with its lamp, the 70s sofas, the manager's locked office and his window. An upgrade station may later take
  over the improvements board's permanent upgrades. [planned]
- **Vending corner:** drinks machines, a coffee machine OUT OF ORDER.
- **Training room:** rows of bolted-down chairs and a TV on a cart; the orientation tape plays when everyone sits.
- **Lift hall:** the old punch clock and its time cards (decoration), a YOU ARE HERE map, the service lift.

### The service lift [built]
A freight cab with a folding gate, a 10-second ride between the hub and your floor, with an odd Severance-style moment
halfway (the view stretches and warbles). Anything in the cab, carts included, rides with you. In co-op whoever is in
the cab rides together. It goes down whenever you like.

### Your floor (one per world, kept for good) [built]
A floor of the Backrooms that Overflow leases, made from the world's seed: Level 0's offices, halls and rooms, with the
lift in the middle. Its edges are sealed (chained fire doors, bricked-up doorways, plastic sheeting, NOT LEASED signs).
- **The core:** 4 x 4 districts, about 230 m across. Leased from the start. **The focus for now.**
- **Four wings** (north, east, south, west), about 230 x 115 m each, behind plywood hoarding until you lease them. Each
  brings more seams and more floor for shelves. [built] Whether wings stay in the game is [open].
- **Storage is wherever you put shelves** (bought, or found and fixed up), anywhere on your leased floor. One room by the
  lift starts with one working shelf and a desk lamp; a few broken old shelves wait to be found. [built]
- **Lit rooms are finds.** About half the rooms have every ceiling tube working; a big, well-lit room is a good place for
  storage (section 8).
- **No boxes of the floor's own.** Every box in the game is a client's (or a kit you bought): the generated levels have
  no loose boxes, and their racks and pallets stand empty, ready for work. Rooms you build yourself keep the boxes you
  put in them. (2026-11-09, the owner: "there should no longer be random boxes".) [built]
- **Further out:** past a leased wing, a door to the **Poolrooms** once that is leased (the Poolrooms and its water park
  are built as a place). More levels after that are [open].

### The seams [built]
Door-sized noclip doorways outlined in silver duct tape, with Overflow's printed label (OVERFLOW / the place / SEAM -
KEEP CLEAR) and the muffled sound of the place behind. Walk through and you are in a real-world room (about 8 x 10 m)
built from real surfaces (block or drywall walls, polished concrete or vinyl, LED lights and daylight). The client's
boxes wait on a pallet under an OVERFLOW PICKUP sign; returned boxes go back in the same room (the taped DELIVERIES bay
is the natural spot). Every seam of a client leads to the same place. Each room has the client's own **staff door** beside
the bay: shut and locked to you (it rattles), the shop's light under it; it is how their people come and go.
**A seam shows what is behind it** (2026-11-09): the other side's cool light leaks round the tape's edges and across the
carpet in front, with a faint blue cast on the walls near it, and a stiff flag of the client's tape stands out from the
wall above head height, so a seam can be picked out from the far end of a corridor. From the client's room the way
back leaks the Backrooms' yellow. Beside the OVERFLOW PICKUP sign is a small **receipt printer** (section 6). [built
2026-11-09]
- **Nobody is ever seen, but people come and go.** A new lot is brought in only while nobody is in the room, and
  returned boxes (paid the moment the last is back) stay where you left them until you have gone, then they are taken
  in; signed-for boxes can't be picked up again. Nothing appears or vanishes in front of you. [built 2026-11-09]
- **The way through** is the camcorder's: as you walk into the tape the picture washes to white, a moment of soft pale
  snow, a flash, and the other side comes up out of white as the exposure settles, with no sound at all (the owner's
  reference clip and "should be none really", 2026-11-09; it replaced a loud rolling, tearing glitch with a crackle).
  About a second, you keep control, and a trip soon after the last is quicker still. However you come at the tape
  (slowly, backwards, sideways, stopping in it, stepping straight back), you go through: never into the gap behind the
  wall, and never through the far room's floor before it is solid. [built]
- **Back and forth is instant.** While you are in a client's room, the floor round the seam you came through stays
  loaded (a second streaming source, as Unreal's World Partition keeps one at a teleport's far end), and while you are
  near a seam its rooms stay loaded; nothing is built again on a trip either way. [built 2026-11-09]
- **The first seam is close and taped.** One WAREHOUSE seam (TALLMORE's) is always in the district beside the lift's, on
  the wall nearest the lift. The company's blue floor tape runs from the lift's door to it, with chevrons pointing the
  way (as warehouses and hospitals tape their floors; Dead Space's locator line is the same idea). [built]
- **Every other seam is found by exploring**: the maze is the point, and your own tape and chalk mark the way. Finding a
  seam never starts a clock (section 7). [built]
- **The seam's card:** a clipboard on the wall beside the tape says what the client offers (section 7). [built]
- **Which clients a floor has is random:** a core can have only two or three kinds of seam. Whether every floor should
  have all four is [open].
- **Seams in different kinds of places** [planned, still to test]: some off a cramped corridor with room for one shelf,
  some off a big room with outlets and space to grow, so the layout choice is real (section 8).
- **Ideas, not agreed:** seams loud enough to follow by ear (left out, the owner's choice); a seam finder for the
  camcorder (like Satisfactory's scanner).

### Free roam (the endless Backrooms) [built, set aside]
The original endless world still exists in the code. Whether it comes back as a mode is [open].

## 6. The work loop

**Find a seam → take a lot of boxes → carry them in → store them safely → their time passes while you do other things →
carry them back through the same seam → paid, and the shelves are free again.** [built 2026-11-09]

- Boxes stay real things you carry and stack. Nothing turns into a number in a menu.
- **Batches overlap**: a new lot is behind the seam 20 seconds of business time after you take the last (and once
  nobody is in the room to see it brought), so two or
  three batches of one client can be going at once, plus other clients'. That overlap is the game: choosing what to
  bring in, arranging space, preparing returns, deciding whether there is time to explore.
- **Progress is how much business your floor can handle at once**, not how many boxes pile up. A returned batch leaves
  the game (the client has it back), which also keeps the floor from filling with thousands of boxes.

### Business time [built]
- Storage time passes **while you play**: while you work, build, explore and handle other batches. It runs while anyone
  is on the floor, and **stops while everyone is off it**: in the hub, or behind a seam in a client's real-world room
  (in co-op, only when all of you are off the floor). The real world is not the Backrooms: storing happens on the floor.
  (The seams' rooms stopped the clock from 2026-11-09, the owner.) Nothing moves on while the game is closed. Saving
  and loading keep every clock where it was.
- There is no shift, no closing time, no blackout and no night. The hub is for planning, shopping, gearing up and the
  atmosphere.
- Not used, and not approved: a sanity meter, monsters on a timer.

### Getting paid is a moment [built 2026-11-09]
The instant a payment lands, it happens on things, never as floating text:
- The camcorder's **IN HAND** figure counts up (bottom right of the picture), a tick a step and a till's bell at the
  end; longer and brighter for a bigger payment.
- **Stored:** the batch's shelf tickets (section 8) are stamped STORED with a thunk.
- **Returned:** a receipt prints from the small printer beside the OVERFLOW PICKUP sign (the client, RETURNED or
  SETTLED, how many boxes, ON TIME or LATE, the pay) and tears off.

### The trip slip [built]
Each time you come back up into the hub with something earned, a pay slip prints on the camcorder's screen: each batch
stored or returned (late, settled, scuffed), the power, THIS TRIP and IN HAND. PAY SLIPS at the pay window shows the last
one again.

### Filling the time [planned]
While batches wait out their time: take another lot, build and light more storage, explore for seams and finds (section
7), pack an order (section 7), set up a chute or a conveyor. Early batches are short (3 to 5 minutes) because early on
there is little else to do; later ones are longer and more of them overlap. You should never stand about waiting to be
allowed to do something.

## 7. Work: seams, batches and the company's milestones

### Seams are business, like resource nodes [built]
- Each client's seam is like a Satisfactory node: **always there, never runs dry**, used whenever you like. There is no
  accepting step: a lot of boxes waits behind the seam, and **picking them up makes it your batch**.
- **Lots differ like node purity** (numbers now): TALLMORE 4-6 boxes for 4 minutes, QUILLBY 5+ for 5, PAPERLARK 7+ for 5,
  BELLWEATHER 3+ for 7; a box more and half a minute longer each stage; half as many boxes again at a seam with a chute.
  The very first lot is 4 boxes for 3 minutes.
- **Some work needs equipment** before its lot goes out, and the card says so: QUILLBY's heavy paper NEEDS A HAND TRUCK,
  BELLWEATHER's flat-packs NEED A PLATFORM TRUCK. Later: the Poolrooms' seams need damp-proof storage. [planned]
- **The card** (a clipboard beside the seam): the client and place, the lot waiting (boxes, storage time and client
  days, what it pays in and back, FRAGILE), what it needs or that the client is on hold, and your batches of theirs
  (COMING IN, STORED 2:10, DUE 1:40, LATE!).
- **What keeps it in check:** your shelf space, having to bring every batch back, and an overdue batch putting its client
  on hold.

### A batch, start to finish [built]
| Step | What happens |
|---|---|
| **Waiting** | The lot is on the pallet behind the seam |
| **Coming in** | You took a box: it is your batch. No deadline to bring it in |
| **Stored** | Every box safe on a lit shelf: the **intake fee** (a third) is paid and the countdown starts |
| **Due back** | Time's up: the **collection window** opens (half the storage time, at least 2 minutes) |
| **Returned** | Every box back behind the seam: the **completion pay** (the rest), less lateness and scuffs; the boxes are taken away once you have left the room |
| **Late** | Past the window: a warning, the completion pay slides to half over 5 minutes, and the client is on hold (an untouched lot of theirs goes back; nothing new until yours is in) |
| **Settled** | Once the late pay is at half, the batch closes with whatever is back: paid for that share, the missing boxes charged as lost, and the client takes work again |

- **Each payment is made once.** Taking boxes down and putting them back earns nothing again; leaving goods on your
  shelves for ever earns nothing more.
- **Every batch has an ending**, failure included: a fragile box that breaks leaves its batch ($15 off), and a box lost
  for good is settled as above.
- **Early clients are forgiving.**
- **Rush lots** [built 2026-11-09]: about one lot in four, once two batches are done, is offered as a **RUSH** (red
  on the seam's card and in the list): stored half as long, paid half as much again. It is an offer, never an event:
  leave it and nothing happens; after four minutes of business time untaken, the client sends an ordinary lot
  instead. (`Contracts.RushOneIn`: 0 switches them off.)

### Goods being handled, goods left out [built]
- **Stored safely:** on a shelf lit enough (section 8).
- **Being handled:** in someone's arms, on the hand truck, on a cart or truck, or with someone within 8 m: ordinary work,
  no clock and no warning.
- **Left out:** set down somewhere unsafe (off a shelf, in the dark) with nobody near. After 10 seconds a warning; after
  45 seconds the box is **scuffed** (dark scrapes on it, 5% off its batch's completion pay).
- Only stored batches count. A batch coming in or going back is never at risk.

### Packing and dispatch [planned: one job to prototype]
Warehouse work that fits Overflow better than a factory: sorting, packing and sending out. One example job:
1. PAPERLARK sends stock (notebooks, pencils) through its warehouse seam.
2. You bring it in and shelve it.
3. An order asks for packs: e.g. 4 school packs, each 2 notebooks and 3 pencils.
4. You put the goods into shipping boxes (the client sends flat ones with the stock, so there is no packaging to buy).
5. The packs go out through the seam the order names: the same one, or the client's other seam (a shop). A client with
   two seams gives a route across the floor a business reason.
- Shelves get a second use: stock waiting to be packed.
- [draft] A packing table: fold a flat box on it, drop the goods in until the order's slip ticks off, tape it with a
  click, and a label prints with the seam it goes to. Fulfilment games (Fulfillment Center Simulator, Ship, Inc.) are
  built on this pick, pack and ship loop.
- One job first; more only if it is fun. No manufacturing chain is planned.

### What exploring finds [planned as a direction]
Besides new clients' seams: maintenance passages that shorten a useful route, broken shelving or equipment worth
repairing, lit rooms with space and power, strange places and company records, furnishings for your own workspace. The
tracker remembers the seams you have found; none of it is ever urgent.

### Kinds of work
- **Store and return** (a batch) [built]; **fragile** loads (break if dropped or thrown hard) [built]; **packing** [planned].
- Agreed [planned]: **heavy** loads (need a truck or pallet jack), **mixed** loads (each client's boxes kept apart),
  **audits** (a client wants part of a batch back early, on a short countdown).
- Ideas [open]: get a room's lights or power back on, clear a room for storage, move a big piece of furniture.

### The company's milestones [built 2026-11-09]
The Satisfactory shape: onboarding in order, then tiers of orders done in any order, then a big gate. Each order pays
when it is done, and finishing a stage unlocks things at the supply counter and the improvements board (unlocked is not
free). The goals are broad, about your operation, rarely one exact client.

| Stage | Orders | Unlocks |
|---|---|---|
| **Onboarding** (in order) | Watch the tape ($20). First storage: TALLMORE's first lot on the lit starter shelf ($60). Find another seam ($40; another of TALLMORE's counts on a floor with few clients). Back on time: return TALLMORE's batch ($60). | the supply counter, the hand truck |
| **Tier 1: Getting set up** | Put up 2 shelving kits ($60). 20 boxes stored safely at once ($150). Complete 4 batches ($120). | platform truck, work lamp, intake chute |
| **Tier 2: More clients** | Three clients: a batch returned to each (or to every client on your floor, $120). A fragile QUILLBY load ($140). On time: 3 batches inside their window ($130). | tall shelving, generator, repair kit |
| **Tier 3: Running the floor** | 50 boxes stored safely at once ($300). A chute and 3 shelves within 12 m of its seam ($200). | conveyor, floodlight, better rates |
| **Gate: the first quarter** | Complete 20 batches ($600). | pallet racking, better rates, the wings and the Poolrooms |
| **After the main floor** | [open]: the wings, the Poolrooms, later levels. | [open] |

The work is pinned beside the picture, kept short like a game's pinned objective (2026-11-09, the owner: the old one
was "just words"). The picture is 4:3, so a 16:9 screen has a black bar each side and a 32:9 screen has nearly two
thirds of itself in them; the list used to sit over the picture instead (it covered a client's own sign). Now
(2026-11-09) it lives **in the bars, as the camcorder's side panels**:
- **Right bar:** the **one next step** on top; the stage's orders as a checklist (`[X]` done, `[ ]` open, a count like
  12/20 where there is one) and one line of what finishing them unlocks.
- **Left bar:** the date, IN HAND, whether business time is running, and your batches, each with its client's colour,
  what to do with it now and how long is left; clients needing equipment or on hold.
- Inside the picture only the camcorder's own marks stay: REC, the counter, the date, LOW LIGHT, notices, and the IN
  HAND figure for a few seconds when money comes in.
- **J** hides and shows the panels. On a window with no bars (narrow, or 4:3 off) the picture carries only the one
  next step; J there goes round: next step, the whole list, hidden. Hidden, it still pops up for a few seconds when
  something moves on. [built]

### The work board [built]
In the lobby: WORK BOARD, the date, and whether business time is running; your batches a line each in the client's
colour, the client and box count on the left (TALLMORE X4) and on the right what to do with it now, in plain words:
READY TO PICK UP, SHELVE 2 MORE, STORED 3:12, TAKE BACK 1:40, LATE - TAKE BACK (due back first); along the foot STORED
SAFE (against the order's target), AT RISK, DUE BACK and IN HAND; and one green line saying what to do next. [built]

## 8. Storage: shelves, light and the dark

The core of what you are paid for: boxes kept safe on your floor.

### Shelf tickets: the state on the shelf [built 2026-11-09]
Every standing shelf has a ticket holder on its front and one small lamp, so a storage room can be read without the
list (as Hardspace: Shipbreaker puts its interface on the things themselves):
- **The lamp**, read from across a room: **green**, its boxes are safe (dim green: empty and lit, ready); **amber**,
  the shelf is not lit enough; **red and blinking**, boxes on it are due back (fast when late).
- **The ticket**, read up close: the client's colour and name, how many of the batch's boxes are on this shelf, and
  STORED 2:10, TAKE BACK 1:40, LATE or COMING IN (LOW LIGHT in red when it is not lit enough; EMPTY - LIT - READY when
  free).
- The camcorder's LOW LIGHT warning stays as the second cue.

**A client's mark on its boxes** [built 2026-11-09]: besides their tape's colour, every box carries its client's
pictogram, big, on both large faces: TALLMORE three bars rising, QUILLBY a diamond, PAPERLARK a chevron, BELLWEATHER a
bell. Readable without colour. (The tape itself was drawn magenta on any box that had been saved, or sent to a
friend, since the day it was added: it lost its material. Fixed the same day.)

### What counts as stored [built]
- A box is **stored safely** only while it sits **on a shelf lit enough** (the top light level). A batch is stored once
  all its boxes are.
- **There are no special storage rooms.** Any shelf on your leased floor counts, bought or found, wherever it stands.
- **Storing beside a seam is fine.** A shelf right by a seam is a good, efficient answer, not a loophole; the game does
  not force long routes to give conveyors a job. The layout makes the choice interesting: a cramped corridor by one seam
  fits a single shelf, a big room farther off has outlets and room to grow. Neither is meant to always win.

### Light [built]
- **Any light reaching a shelf counts**, ceiling tubes included, all added up and measured at each shelf (two points on
  each deck). A room with every tube working lights a shelf fully; a room with some tubes half; a dark room nothing.
- **The ceiling lights stay as they are**: working tubes stay on, dead ones stay dead until you fix them (spare tubes,
  the repair kit). **No tube failures** are added to make lamps necessary (decided 2026-11-09).
- **Lamps let you use dark rooms** and choose better places: aim a desk lamp, a work lamp or a floodlight at a shelf.
- **Seeing how lit a shelf is:** hold your gaze on a shelf and the camcorder's **LOW LIGHT** warning fades in if it is
  not lit enough, as real camcorders warned. No extra meter.
- **A room set up properly stays safe**, like a Satisfactory factory that runs once it is built right. Upkeep comes back
  only when something new happens, always for a reason. No random events.

### The start [built]
One room by the lift has **one working shelf, a desk lamp plugged into the outlet beside it**, enough for onboarding.
Every other shelf is bought or found. A starter shelf holds 8 to 12 boxes.

### Shelves are bought, not crafted [built]
- **No crafting**: making the same shelf from materials adds steps without adding anything.
- A **shelving kit** from the supply counter is a crate you carry down. Holding it, a see-through outline shows where
  the shelf goes (green, red where it does not fit; R turns it); a click puts it up. G packs it back into its crate.
- **Found shelves:** knocked-over old shelving lies in the floor's old storerooms. Stand it up with hammer and nails (4
  nails) and it is yours.

### The dark nudges what is left out [planned; slice 3]
In the office Backrooms, a box left unsafe and unattended may, after its warning, shift, slide off a shelf or move a
short way while nobody is in that area, leaving scrape marks so it is found nearby. Never gone for good. It follows the
rules the building already uses on furniture in dark rooms (DarkChanges [built]: only out of sight, at least 11 m away,
never near a lit lamp of yours, after a room has been dark for 20 s). These are deliberate events, easy to tell apart
from physics. Today a box left out is scuffed (section 7) and nothing moves it.

### Light and power
- **Doors are left out for now.** They do not affect light or boxes.
- **Wall outlets are always on and low power**: enough for a desk lamp, through an extension cord if needed. [built]
- **Big machines** (conveyors, floodlights) **need a generator**: their chunky yellow industrial plug only fits one.
  [built]
- **Generators** are bought at the counter and set down like a shelf. Each runs a set load (four conveyor sections; a
  floodlight counts two); a dial shows it. Plug in too much and it stalls with a clunk: **overload is the only thing
  that stops a generator.** [built]
- **The ceiling lights use no power.** They belong to the building. [built]
- **The power bill:** $12 a day's worth for each generator, charged by the minute while business time runs and taken
  from what you have. With nothing in hand it is **owed** and taken from the next payment; nothing ever switches off for
  it. Better generators may cost less to run. [built]

### Each new place brings one new problem [planned]
| Place | Its problem | Its fixes |
|---|---|---|
| The core | The dark | Lights and power (above) |
| The wings ([open]: whether wings stay) | Distance: far from the lift | Conveyors, roller track, a pallet jack |
| The Poolrooms | Damp, not darkness: wet cardboard is ruined | Raised shelves, dry rooms, sealed doors, a dehumidifier |
| Later levels | That level's own weirdness, e.g. halls that rearrange | e.g. signs and tape lines that hold the route |

## 9. Money

- **Each batch has a stated total, paid twice** [built]: the **intake fee** (a third) when every box is safe on a shelf,
  and the **completion pay** (the rest) when it is back, less lateness and scuffs (each part once).
- **Prices** [built, to tune]: about $10 a box for TALLMORE ($14 QUILLBY, $9 PAPERLARK, $22 BELLWEATHER), more for
  longer storage (by its square root) and for seams farther from the lift, rounded to $5; BETTER RATES add 15% each.
  TALLMORE's first lot: $10 in, $25 back.
- **Lateness and scuffs:** full pay through the window, sliding to half over 5 minutes after it; 5% off for each
  scuffed box; never below 30% in all. Settled batches pay for the share that came back.
- **Work orders** pay when they are done ($20 to $600). [built]
- **The power bill** is charged by the minute and owed when you have nothing (section 8). [built]
- **You can always recover from a mistake:** TALLMORE's taped seam always has a small lot, and nothing switches off for
  lack of money. [built]
- **Spending:** the supply counter (as much as you can pay for) and the improvements board (permanent upgrades). [built]
- [open] Whether money is spent on anything else (furniture for your own workspace, which exploring can also turn up).
  Not approved: a mandatory quota, a client reputation system.

## 10. Improvements (what you can buy)

Two places in the lobby [built]: the **supply counter** sells things you carry down and use, as many as you can pay for
(they wait by the lift on your floor); the **improvements board** sells permanent upgrades. Finishing work orders unlocks
what each sells; unlocked is not free. Both list **everything** from the start: what is locked is greyed, with which
stage opens it in place of its price (AFTER ONBOARDING, AFTER TIER 2) and, picked, "FINISH TIER 2'S WORK ORDERS (J)",
as Satisfactory shows each milestone's unlocks. [built 2026-11-09] (An upgrade station may later take the board's
place. [planned])

| What | Price | What it does | Sold at | |
|---|---|---|---|---|
| Shelving kit | $60 | A crate; put up a shelf for 8 to 12 boxes anywhere | counter | [built] |
| Desk lamp | $25 | Plugs into any outlet; aim it at a shelf | counter | [built] |
| Extension cord | $20 | Three sockets on a long lead | counter | [built] |
| Hammer and nails | $30 | Stand up knocked-over shelves | counter | [built] |
| Spare tubes | $15 | Put dead ceiling tubes back | counter | [built] |
| **Hand truck** | $40 | 4 or 5 boxes on wheels (section 12) | counter | [built] |
| Platform truck | $120 (two at most) | A flat trolley that boxes ride on | counter | [built] |
| Work lamp | $90 | Brighter, lights two shelves | counter | [built] |
| Intake chute kit | $150 | Set it down at a seam: the chute pulls that client's lot through by itself; bigger lots there | counter | [built] |
| Tall shelving kit | $110 | A deck more | counter | [built] |
| Generator | $250 | Runs big machines; on the power bill | counter | [built] |
| Repair kit | $80 | Fixes dead tubes and broken shelves | counter | [built] |
| Conveyor section | $70 | Plugs into a generator; carries boxes | counter | [built] |
| Floodlight | $200 | On a generator; lights a row | counter | [built] |
| Pallet racking kit | $220 | Big loads, three decks | counter | [built] |
| Better rates | $250 / $400 | New batches pay 15% more each | board | [built] |
| Lease a wing | $400, +$200 per wing owned | The wing's hoarding comes down; its seams join your floor | board | [built] |
| Poolrooms access | $900 (needs a wing) | A door to the Poolrooms past a leased wing | board | [built] |
| Powered rack | [open] | Stores and releases batches by itself (below) | counter | [planned] |
| Door kit | [open] | Snaps into an empty doorway; doors do not affect light or boxes yet | counter | [open] |

### Progress on the main floor: three ladders
Each step makes the same job faster or safer, is unlocked by a work order and bought at the counter. The floor slowly
turns from a dark office with one lamp into a lit storage operation you built yourself.

| Ladder | Steps |
|---|---|
| **Light** | Desk lamp on an outlet → work lamp (two shelves) → floodlight on a generator (a whole row) |
| **Storage** | Starter shelf → tall shelving → pallet racking → powered rack (stores and releases by itself) |
| **Moving boxes** | By hand → hand truck → platform truck → intake chute at a seam → conveyor, run by a generator |

### Toward a floor that runs by itself [planned, later]
- **The powered rack.** A conveyor only moves boxes; something has to put them away and pick out the right batch to go
  back. A powered rack has an intake and an output; its lift visibly carries each box into a slot (like a real vertical
  lift module, which brings the tray asked for to an opening). Ordinary shelving stays manual. First a button on the
  rack releases a batch that is due; later release is automatic. Satisfactory's containers just pass items through,
  last in first out; a rack here keeps each batch together, which is the new part.
- **A standing arrangement with a client.** With a chute at their seam and room in a rack, their lots come in by
  themselves while there is capacity: a set number of active batches or reserved slots; a full rack pauses intake;
  outgoing batches clear space before the next delivery. Then an established route runs while you explore.
- [open] rack size, how it picks a batch, how conveyors join it, blocked outputs.

**More ideas** [open]: faster chutes, or chutes straight onto a truck; a roller track as a cheaper, unpowered step
before conveyors; a pallet jack; a bigger lift cab; your own lights and signs to mark routes; a label printer showing what
is stored where.

## 11. Danger

- **Getting lost:** the floor is big and copies itself; chalk, tape and spray paint are your map. [built]
- **The dark:** things in dark rooms move while you are away (a chair dragged), never what you set up near your own
  light [built]; boxes left out are scuffed [built], and nudged a short way [planned].
- **Late returns** cost part of the completion pay and put the client on hold. [built]
- **An overloaded generator** stalls what it runs. [built]
- **Damp** in the Poolrooms ruins boxes left low. [planned]
- **Creatures:** none for now. [open] Not approved: a sanity meter, monsters that come after a set time.

## 12. What you have (tools and moving things)

All [built] (`Docs/Interaction.md`):
- **The camcorder** in one hand, its zoom and its F5 menu (quick travel: Places > OVERFLOW).
- **The free hand** picks things up; **four pockets** (keys 1-4); **a backpack** (B) with room inside.
- **Furniture:** grab it at a point and push, pull and turn it; sit on chairs and sofas.
- **Carts:** utility cart, janitor's cart, platform truck; what is on them rides along.
- **Marking the way:** chalk, blue painter's tape, red spray paint.
- **Power and light:** desk lamp, work lamp, extension cord, live wall outlets.
- **Fixing things:** hammer and nails, spare tubes, the repair kit, breaking walls.
- **Doors:** opened by hand, shoved, closed. **Noclip doors and crawl holes** lead farther than the wall could hold.

### Carrying boxes [built]
The owner, 2026-11-08: "the main next thing you do is pick up boxes and move them ... must feel satisfying".
- **Click to pick up, click to put down.** A quick bend and lift, about a third of a second, from up to 2.4 m away
  without walking over; one on a high shelf is reached up for. Taking a box takes what is stacked on it.
- **Carried low at the hips**, with a slight bob as you walk and a little lag when you turn. Heavy boxes slow you. A wall
  stops a box but never knocks it out of your hands.
- **A few in your arms:** hold the click on another box and it goes on top, up to 20 kg and 0.75 m (two warehouse boxes,
  one of QUILLBY's). Turn too fast and the top one falls off.
- **Putting down:** a tap of the click puts the top box where a thin amber rectangle shows (a shelf's deck, a box's top,
  the floor close by), lined up and flush when close; with none, it goes down in front of you. **Hold the click** away
  from a box and it winds up a throw; thrown at a shelf it slides on square. **A hold that starts on a box only ever
  takes it**: it never turns into a throw if your aim slips. Q does the same as the click; R turns the box.
- **Satisfying:** a dip of the view lifting something heavy; a soft settle (a tiny squash, a puff of dust) and a thud; a
  click when the outline sits flush; cardboard rustles. What a box is shows only on the box itself.
- **Stored goods stay put.** A box on a shelf takes a real hit to move (over 2.2 m/s: a thrown box, a truck driven into
  it), and taking or knocking one box no longer disturbs the boxes beside it (only what is stacked on it comes along).
  Saving and loading keep everything where it was.
- **Your body is always shown.**

### The hand truck [built]
The first carrying upgrade (as in Big Ambitions, where a hand truck takes four things to the one you carry by hand).
Click its handles to take it; boxes you click go onto its nose plate, a tall stack against its frame, up to 70 kg and
1.45 m (four warehouse boxes, five of QUILLBY's). They go down the same way, a tap onto the outline. The stack rides
0.7 m out so you see over it, and never slides off a turn. Q with nothing on it parks it standing where you are. Not
shared with co-op friends yet.

### Later [planned]
Two-person boxes in co-op (the furniture store's biggest); boxes sitting tighter on carts and trucks; a pallet jack and
pallets.

## 13. Co-op

Two to four players [built]: one hosts their world, the others join by its address (Hamachi, a LAN or any VPN). Players
see each other's bodies and hands; items, furniture, carts, doors, lights, paint and marks are shared; the host's save
keeps what each friend carried. Proximity voice chat moves the suit's mouth. Business time stops only when everyone is
off the floor (in the hub or behind a seam).
- **How a friend moves** (2026-11-09, the owner: "improve animations ... crouching, crawling, visuals for multiplayer
  other players, laying down when getting out of the medbay"). All worked out in code from what their game sends:
  walking, the hips sway over the standing foot and turn with the stride while the chest turns the other way, the feet
  roll from heel to toe, and the body leans into speeding up, slowing and turning; standing, they breathe and shift
  their weight, and their feet stay planted, stepping round when they turn on the spot; a real crouch (hips back and
  down, knees out); crawling on hands and knees in a diagonal gait, shins along the floor; knees tucked in the air; one
  leg out in front sliding; lying flat on the belt and the cot, and sitting up from it. [built; seen with a stand-in
  player on one PC, not yet with a friend]
- **What you see of yourself** [built 2026-11-09]: crawling, your free hand works the floor ahead (in the suit's sleeve
  and glove), planted while you move over it and reaching forward in turn; the camera rocks onto each hand-fall.
  Crouch-walking has its own deeper, slower bob. Getting down onto your hands tips the camera forward, getting up tips
  it back.
- **The work is shared** (2026-11-09, moved ahead of exploration and packing at the owner's word: "you can move coop
  up"). One job for the crew:
  - **The host's game runs the work** (lots, countdowns, payments, work orders, the power bill) and sends it to
    everyone as it changes; a joined game shows the same batches, money, tickets, board, cards and side panels, and
    gets the STORED stamp, the receipt and the count-up at the same instant.
  - **One wallet, anyone may spend:** ordering at the counter or the improvements board from a joined game asks the
    host, which pays and delivers (everyone is told who ordered what).
  - **Anyone may build:** a shelf or machine put up or packed away by anyone counts for the crew; the light measured
    at a shelf by whoever is near it counts, however far off the host is.
  - **Lots grow with the crew:** half as many boxes again for each extra player, and the pay follows the count.
  - **Finding a seam finds it for everyone.** The trip slip prints for each player as they come up; the host's coming
    up closes the trip.
  - **Checked** with two copies of the game on one PC: a friend's game stored a lot and took it back, and both games
    agreed on the money and the batches at every step. **Not yet tried with a friend over the internet.**
- **Not shared yet:** the hand truck's place, a machine's plug, a throw caught in the air, two-person loads. [planned]

## 14. Still to decide [open]

1. **Amounts:** storage times, prices, equipment costs, the late slope, the power bill (all tuned in play).
2. **Clients per floor:** should every floor's core have all four clients' seams?
3. **Packing:** how it is done by hand, and whether supplies (boxes, tape) are ever bought.
4. **Powered racks and standing arrangements:** sizes, routing, limits.
5. **The dark's nudges:** thresholds and timing (scuffs are built).
6. **Co-op:** (decided 2026-11-09: one wallet anyone may spend from; lots half as big again per extra player. To
   tune in play.)
7. **Doors:** do they come back, and what would they do that is fun and feels like progress?
8. **Order kinds:** which ideas in section 7 to use.
9. **The end:** does the game have an ending (the manager's office, a last level), or does it go on?
10. **The wings:** keep them or not, and what they are for.
11. **Past the Poolrooms:** what comes next, and what work is there?
12. **Creatures:** never, or later? (Not on a timer, not with a sanity meter.)
13. **The tape check:** should pay depend on your recording (REC)?
14. **Free roam:** bring the endless Backrooms back as a mode or leave it out?
15. **Lockers:** decoration, or personal storage?

## 15. Where things stand (2026-11-09)

- **Built, checked in play by script, not yet played by hand:** the batches (lots behind every seam, storing, the
  countdown, due back, late and on hold, settling with boxes missing), the two payments, business time stopping in the
  hub, scuffs on boxes left out (and none while being handled or with someone near), the power bill owed instead of
  switching anything off, the trip slip, the seam cards, the board and tracker, the new work orders, the hand truck,
  steadier shelves, hold-to-take, ceiling light counting at shelves, older saves moving over (a copy of World 1), saving
  and loading mid-countdown, and a build smoke test (a new world, the lift down and up twice, no errors).
- **After the owner's first hand test (2026-11-09):** the board and tracker reworded and shortened, locked things shown
  on both shop boards with what opens them, a developer PROGRESSION panel in the L menu (skip each stage, money, business
  time, as Satisfactory's Creative Mode) and a WORK section there (every kit, the hand truck, the work lamp), whole stacks and the hand truck's load carried through seams, thrown boxes
  going through seams, the monster and its power dips switched off, the hub sofas' flicker fixed, the hub's loud rooms
  turned down. After the second: lots brought in and returned boxes taken away only while nobody is in the room, a
  locked staff door in each real-world room, and the soft white-out way through a seam. After the third: notices that
  fit the screen, no clicks as places load, seam trips with nothing rebuilt, business time stopped behind the seams,
  no generated boxes, and the painted wallpapers.
- **Built earlier and checked:** the hub, the lift, the floor and its wings, the taped first seam and its tape, the four
  real-world rooms, the supply counter and kits, shelves (put up, packed away, knocked-over ones stood up), lamps,
  generators, conveyors, floodlights and their models, the LOW LIGHT warning, fragile boxes, carrying boxes.
- **Not checked yet:** anything of the batches played by hand; boxes on a platform truck or cart counting as handled;
  the hand truck's look while walking; tall shelving and pallet racking put up in play; co-op with any of the work.
- **Built on 2026-11-09 from the improvement plan** (`ImprovementPlan.md`, which lists what was measured): shelf
  tickets and lamps, the pay moments (count-up, stamp, receipt printer), the work list in the side bars, seams that
  leak light and carry the client's flag, the bolder wallpaper print, the clients' marks on boxes, the animations (a
  friend's walk, crouch, crawl, lying and getting up; your own crawl hand, crouch and crawl camera, getting up off
  the cot), the shared work in co-op, rush lots, a notice in the hub for each stage done, a calendar in each
  client's room, the Classic look removed, pipeline states made ahead of time, the benchmark's work-floor spots.
  Also found and fixed: a stocked storage room ran at 19 fps on a card without ray tracing (now 73).
  **Seen in the editor and in the builds' tests; not yet played by hand.**
- **Not made yet:** the dark's nudges, packing, powered racks and standing arrangements, heavy and mixed loads, audits,
  the real orientation tape and its PA voice, muzak, anything past the main floor; in co-op, the hand truck's place, a
  machine's plug, catching a thrown box and two-person loads; the survey marks for walls worth knocking through.

## 16. Building it: the slices

Each slice ends with **playing it by hand**: a script passing is not proof that it is fun. The order (2026-11-09, after
the owner's review): overlapping batches → every batch able to end → a hand truck worth having → exploration → one
packing job → powered racks.

1. **Slice 1: the batch loop** [built]: one taped seam's lots overlapping, the countdown, both payments, lateness, hold
   and settling, scuffs, the slip, the cards, the hand truck. **Play by hand for:** does carrying feel good; is a batch's
   time clear at a glance; is "safe" easy to read; can you tell what you were paid and why; with two or three batches
   overlapping, do you think "I want to improve this place" rather than "I'm waiting for a timer"; does the hand truck
   cut the trips enough to notice (count the trips for one batch with and without it: TALLMORE's 4-box lots are 2 by
   hand and 1 with it; QUILLBY's 5 heavy boxes are 5 by hand and 1 with it).
2. **Slice 2: exploration**: a second client found and marked, finds in the maze (passages, shelves to repair, lit
   rooms), the dark's nudges.
3. **Slice 3: one packing job**: PAPERLARK's notebooks and pencils in, four school packs out through a seam.
4. **Slice 4: the powered rack**: release by button, then automatic release, then standing arrangements.
5. **Then:** the real orientation tape. (Co-op for the work was moved up and built on 2026-11-09: section 13. New
   systems are built shared from the start.)

## Other docs

- `Docs/UiPlan.md`: every screen, panel and prompt reviewed (2026-11-09), and a plan in passes to give them one look,
  split the developer tools from the player's game, and reach small screens. Passes 0 and 1 built 2026-11-10 (the
  two typefaces and the colour names, the developer switch, `-ui-shots`; the side panels, prompts, notices, co-op
  marks); passes 2 to 5 not built. Decided 2026-11-10: the
  developer tools and build mode are for the owner only (off in the player's game); no controller yet; other languages
  later; a title screen with the game's name, the name itself still open (Overflow is taken by a Japanese studio).
  The game's two typefaces: Pixelify Sans for the camcorder's marks, Courier Prime for the company's paper (menus,
  side panels, prompts, slips); the 5 x 7 pixel font stays on the screens in the world.
- `Docs/ImprovementPlan.md`: a review of the game against this outline (2026-11-09) and a plan in passes: fun, the
  look, efficiency. Not built; items move into this doc as they are agreed.
- `Docs/DesignHistory.md`: how the game used to work (the working day, rent, the night) and what replaced it.
- `Docs/HubPlan.md`: the hub, room by room, and how it was built.
- `Docs/ContractFloorsPlan.md`: the floor and the seams in detail (its working day is history now).
- `Docs/StyleSheet.md` and `Docs/StyleRollout.md`: the Overflow look.
- `Docs/Interaction.md`: hands, pockets, carrying, furniture, items.
- `Docs/Poolrooms.md`: the Poolrooms and the water park.
- `Docs/MultiplayerPlan.md`: co-op.
- `Docs/Camcorder.md`, `Docs/Audio.md`: the camera and the sound.
- `Docs/ProjectBrief.md`: the owner's first brief (2026-10-20). Its feel still holds; its story (falling in and trying
  to escape) is replaced by the job at Overflow.
- `Docs/WORKLOG.md`: the history of everything built.
