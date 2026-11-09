# Overflow: the game design (working title)

The one place that says what the game is. Started 2026-11-08 from everything decided so far; the owner and Claude keep
editing it until the owner agrees with all of it, and after that it is the reference for anything new.

**How to read it**
- Each part says where it stands: **[built]** is in the game, **[built, not checked]** is in but not played through,
  **[planned]** is agreed but not made, **[draft]** is a proposal still to agree, **[open]** is a question.
- When the design changes, this doc changes first (or with the work). The older plan docs keep the detail and the
  history of how things were made (see "Other docs" at the end).
- The game was "Still Recording" until 2026-11; it is now **Overflow** (also a working title).

---

## 1. The game in a paragraph

You work for Overflow, a storage company with a strange product: the world is full, and behind it there is more world,
so Overflow rents that space out. Every day you wake on a cot in the company's empty, automated office (the hub), pick
up your work orders, punch in and ride the service lift down to your floor of rented space in the Backrooms. There you
go through the seams (taped-off doorways where the walls are thin) into ordinary real-world places, carry the clients'
boxes back and store them on your floor, or take them out again. Be back before closing or you black out. What you earn
pays for improvements that make the work faster and open more of the building, step by step, like Satisfactory's
milestones. You see it all through the company's work camcorder. Alone or with up to three friends.

## 2. What it should feel like (pillars)

1. **Through the camcorder.** You are the camera: a 90s work camcorder with its own picture, sound, date stamp and
   handheld motion. "If it isn't on tape, it didn't happen." [built]
2. **Everything is physical.** Boxes, carts, sofas and doors are pushed, carried and stacked by hand, with weight and
   sound. No floating inventory grid: two hands, four pockets, a backpack, carts. [built]
3. **Build up a place and keep it.** Your floor, your storage, your chutes and shelves stay. Progress is something you
   can walk around in, like a Satisfactory factory. A room you set up properly stays safe; upkeep comes back only when
   something new happens, never as a daily chore (section 8). [built in part]
4. **The working day.** A shift with a clock gives every day a goal and a little pressure: plan what you can do before
   closing. [built]
5. **Uncanny against ordinary.** The Backrooms are yellow, humming, empty and endless; the real-world places through
   the seams are clean, cool, daylit and full of normal life sounds. The contrast is the point. [built]
6. **Eerie, not a horror game (for now).** No monsters in the current scope. The danger is the building: getting lost,
   the dark, closing time. [built]
7. **No survival meters.** No hunger, no thirst, no battery to keep charging, no comfort meter. [built]

## 3. The world and the story

- **The company.** Overflow was set up in the 60s, done up in the bright 70s look (amber, orange, gold, avocado on dark
  brown), and by the 90s it runs itself: a PA voice, a board, a pay window, a punch clock. Nobody else works there.
- **The company's voice** is the man in the orientation tape: a character the owner is making, a male voice. To the
  players he sounds like a boss giving them work. They do not realise it is all old and pre-recorded, and that they are
  working for someone who probably no longer exists. His voice gives the orders and the PA lines. [planned]
- Its
  slogans: WORK HARD. WORK FAST. BE BACK BY CLOSING. / THERE'S ALWAYS MORE ROOM. / IF IT ISN'T ON TAPE, IT DIDN'T HAPPEN.
- **The time.** The camcorder's date starts on MAR 11 1996 and moves on a day for every working day.
- **The orientation tape.** A training video plays in the hub's training room when everyone sits down; it explains the
  job (the seams, the chutes, storage, closing time). Its cut is being made separately. [planned] Until it is in, a
  stand-in plays: about 20 seconds of title cards on the TV when you sit down, and watching it to the end counts for
  the first work order. [built 2026-11-08] The real tape must also be
  updated for the storage rules in section 8: boxes go on shelves, shelves must be lit, the dark takes what is left out,
  your lamps stay on overnight, how a shelving kit is set up, the daily storage charge clients pay from day one, and the
  power bill (played for laughs). [planned]
- **The mystery.** The manager's office is locked; its calendar stopped in March 1979 and his chair faces the wall.
  "What are we even working for?" What is behind it, and whether the game has an ending, is [open] (see section 14).
- **The clients** (all invented, never real brands), each with its own tape colour on its boxes:

| Client | Place behind its seam | Tape |
|---|---|---|
| TALLMORE WHOLESALE | a warehouse: racking, a roller door, a pallet jack | blue |
| QUILLBY PRINT & COPY | a print shop: a press, paper stacks, a guillotine | red |
| PAPERLARK OFFICE SUPPLY | an office supply shop: shelving full of stock, a counter and register | green |
| BELLWEATHER HOME FURNISHINGS | a furniture showroom: sofas, lamps, a SALE banner | plum |

## 4. The look and sound

- **The Overflow look:** chunky, rounded, low-poly shapes in a few flat colours, painted with big soft patches and
  baked dirt, lit by the game's real lights, seen through the camcorder. References: Lethal Company, PEAK, RV There Yet?
  The full rules are in `Docs/StyleSheet.md`. [built]
- **The hub's style is the house style:** seventies office furniture, fat cushions, walnut plinths, chunky tubes. All of
  Level 0's furniture is remade in it (sofas, chairs, desks, lamps, tables, clocks, shelving, partitions). [built]
- **The Classic look** (the earlier realistic one) is still a setting; the hub is always in the Overflow look. [built]
- **Sound:** the building's hum and air, fluorescent buzz, footsteps by floor, sound that goes round corners through
  doorways, muffled places behind the seams, a radio and traffic inside them, the camcorder's microphone on everything.
  Crackly muzak and the PA voice in the hub. [built; muzak and PA voice planned]

## 5. The places

### The hub (where every day starts) [built]
A one-floor 70s company office, about 22 x 22 m, the same in every world, safe but eerie.
- **First-aid room:** four cots (one per player). You arrive on a conveyor through a return chute behind your cot and
  wake there: on loading, on a new day, and after blacking out.
- **Locker room:** avocado lockers and a bench (decoration for now; storage in them is [open]).
- **Lobby:** the jobs board, the pay window, the improvements board, the OVERFLOW sign, reception with its lamp, the 70s
  sofas, the manager's locked office and his window. A **supply counter** for kits and supplies is agreed [planned],
  and an upgrade station later [planned].
- **Vending corner:** drinks machines, a coffee machine OUT OF ORDER.
- **Training room:** rows of bolted-down chairs and a TV on a cart; the orientation tape plays when everyone sits.
- **Lift hall:** the punch clock and time cards, a YOU ARE HERE map, the service lift.

### The service lift [built]
A freight cab with a folding gate, a 10-second ride between the hub and your floor, with an odd Severance-style moment
halfway (the view stretches and warbles). Anything in the cab, carts included, rides with you. In co-op whoever is in the
cab rides together.

### Your floor (one per world, kept for good) [built]
A floor of the Backrooms that Overflow leases, made from the world's seed: Level 0's offices, halls and rooms, with the
lift in the middle. Its edges are sealed (chained fire doors, bricked-up doorways, plastic sheeting, NOT LEASED signs).
- **The core:** 4 x 4 districts, about 230 m across. Leased from the start.
- **Four wings** (north, east, south, west), about 230 x 115 m each, behind plywood hoarding (NOT LEASED) until you
  lease them. Each brings more seams and more floor for shelves. [built] Whether wings stay in the game is [open]: the
  owner is not sold on them yet. **The main level (the core) is the focus for now**; wings are talked about later.
- **Storage:** today, set rooms with a STORAGE sign, lit and lined with empty shelving [built]. Agreed change: there are
  no special storage rooms. Storage is **wherever you put shelves** (bought or found and fixed up) anywhere on your
  leased floor. One room near the lift starts with one working shelf and one working light; the rest of the floor is
  bare, often dark, with a few broken old shelves to find. [planned]
- **Further out:** past a leased wing, a door to the **Poolrooms** once that is leased (the Poolrooms and its water park
  are already built as a place). More levels after that are [open].

### The seams [built]
Door-sized noclip doorways outlined in silver duct tape, with Overflow's printed label (OVERFLOW / the place / SEAM -
KEEP CLEAR) and the muffled sound of the place behind. Walk through and you are in a real-world room (about 8 x 10 m)
built from real surfaces (block or drywall walls, polished concrete or vinyl, LED lights and daylight). The client's
boxes wait on a pallet under an OVERFLOW PICKUP sign; deliveries go in a taped DELIVERIES bay. At closing the seams shut:
anyone inside is trapped and blacks out.

**Finding them** (agreed 2026-11-08: "how you supposed to find what the first contract asks for?"):
- **The first seam is close and taped.** One WAREHOUSE seam (TALLMORE's) is always in the district beside the lift's,
  on the wall nearest the lift (about 13 m away in a straight line on the test floor; the nearest WAREHOUSE was 58 m
  before). The company's blue floor tape runs from the lift's door to it, with chevrons pointing the way and a bar at the
  end, as warehouses and hospitals tape their floors (Dead Space's locator line is the same idea). TALLMORE's boxes always
  come from this seam. [built]
- **Every other seam is found by exploring**: the maze is the point, and later your own tape and chalk mark the way.
- **Ideas for later, not agreed:** seams loud enough to follow by ear (left out for now, the owner's choice); a seam
  finder for the camcorder from the supply counter (like Satisfactory's V scanner: static when you face a seam).

### Free roam (the endless Backrooms) [built, set aside]
The original endless world still exists in the code. It is not part of the main game for now; whether it comes back
as a mode is [open].

## 6. A day

1. **Wake** on your cot in the hub (8:30 on the camcorder's clock).
2. **Read the board** and take the day's work (see section 7). Buy what you need at the supply counter (as much as
   you can pay for) [planned]; today it is one improvement a night at the improvements board [built].
3. **Punch in** and **ride the lift** down. The shift starts at your first step out on the floor.
4. **Work** from 9 to 5 on the camcorder's clock: 15 real minutes (overtime makes it longer, to 6, 7 or 8 PM).
   **Clock out** at the punch clock's CLOCK OUT button whenever you like to end the day early (agreed 2026-11-08: never
   a reason to wait for closing). [built]
5. **Closing:** at 5 the bell rings and the floor's lights go out. A minute later (more with backup lights) anyone still
   down there drops what they carry, blacks out and wakes on their cot. Work not finished is lost.
6. **Ride up and get paid** at the pay window for what is finished. Rent comes in for every box kept safe, for the
   part of the shift you were clocked in (clock out at noon: about half a day's rent). [built]
7. **Night:** collecting the pay ends the day: a new date, a new board, and the counter to spend at. [built; the
   counter planned]

## 7. Work: from random jobs to a story of work orders

### Today [built]
The board posts three random jobs a day from the leased parts' seams and storage rooms. Take any of them before the
shift:
- **STORE:** carry a client's boxes from their place into your storage.
- **SHIP:** carry boxes from one client's place to another client's DELIVERIES bay.
- **RETURN:** take a client's stored boxes back out to them.
Pay grows with the number of boxes and the distance. Each row on the board has the client's colour.
Agreed: these random jobs are **dropped for now** when the work orders come in; side jobs may come back later. [planned]

### The plan: work orders like Satisfactory's milestones [built, not checked in play: 2026-11-08]
Satisfactory starts with a short tutorial of upgrades that must be done in order (Tier 0). After that, each tier is a set
of milestones done in any order; finishing them unlocks new things to build. Every few tiers, a big delivery to the Space
Elevator opens the next ones ([wiki: Tier 0](https://satisfactory.wiki.gg/wiki/Tier_0),
[Milestones](https://satisfactory.wiki.gg/wiki/Milestones)). Overflow's version:

- **The board is a notice, not a menu** (agreed 2026-11-08): nothing to pick. It shows the current work orders and how
  far along each is; each day an order's boxes go out to its seam by themselves. [built]
- **The orders follow you on the camcorder's screen** (agreed 2026-11-08: "instead of forgetting and having to go back
  to hub"), pinned in the top right as Satisfactory pins its current milestone. It shows the tier and how many of its
  orders are done; each open order with its pay, what to do, its progress and today's boxes, naming the seam they wait
  at; what the tier unlocks when it is done; and the next step. **J** hides it, and hidden it still pops up for a few
  seconds when an order moves on (as Avowed's "show on update" tracker does). After the gate it lists the day's jobs.
  [built]
- **The board shows company work orders**, a fixed sequence, the same in every world (the floor's layout is still
  random). An order can take several days; its progress is kept.
- **Finishing orders unlocks things** at the supply counter and the improvements board. Unlocked is not free: you
  still pay for them.
- **Each order pays when it is done**, on top of the daily rent, so a slow order never leaves you with nothing coming in.
- **Each tier's orders teach what it unlocks next** (keeping 20 boxes safe pushes you to lamps before the work lamp
  opens; the generator arrives just before the conveyor that needs it).
- **Big gates** (like the Space Elevator): a large "quarterly shipment" order opens the next part of the building.
- **A voice for the company:** the orientation tape's man (section 3) gives each order on the PA and in memos on the
  board, moving the story along between orders.

**The sequence on the main floor** (agreed; amounts and pay are tuned in play):

| Stage | Orders | Unlocks |
|---|---|---|
| **Onboarding** (in order, each as soon as the last is done) | 1. Watch the tape and clock in. 2. Follow the blue tape to the WAREHOUSE seam and store 5 TALLMORE boxes on the starter shelf. 3. Find the QUILLBY seam and bring back its sample box. 4. Take 3 stored boxes back to TALLMORE. | the supply counter (shelving kits, desk lamps, spare tubes), the platform truck |
| **Tier 1: Getting set up** (any order) | Put up 2 shelving kits. Keep 20 boxes safe overnight. Ship 10 boxes between two clients. | work lamp, intake chute kit, overtime 1 |
| **Tier 2: More clients** | Store for PAPERLARK. A fragile load for QUILLBY. An audit for TALLMORE. | taller shelving, a small generator, the repair kit |
| **Tier 3: Running the floor** | Keep 50 boxes safe overnight. Set up a chute and a shelf row by one seam. | conveyor, floodlight, rent review 1 |
| **Gate: First quarter's shipment** | Keep 80 boxes safe at once. | pallet racking, then whatever comes after the main floor |
| **After the main floor** | [open]: wings, the Poolrooms, later levels (the wings are undecided). | [open] |

### Kinds of order (current and ideas)
- Store, ship, return [built]; furnish a room [planned, from the earlier plan].
- Agreed [planned]: **fragile** boxes (break if dropped or thrown), **heavy** loads (need a truck or pallet jack),
  **mixed** loads (each client's boxes kept apart), **audits** (a client wants particular boxes back on short notice),
  **find a lost box** (one the dark moved, section 8).
- Ideas [open]: get a room's lights or power back on, clear a room for storage, put up shelving, move a big piece of
  furniture, a timed rush order.

## 8. Storage: shelves, light and the dark [built, not checked in play, except as noted in section 15]

Agreed 2026-11-08. This is the core of what you are paid for: boxes kept safe on your floor.

### What counts as stored
- A box is **stored** only while it sits **on a shelf in a lit spot**. Only stored boxes pay rent each day.
- A box on the floor, fallen off, moved away or left in the dark pays **nothing** until it is back on a lit shelf. That
  lost rent is the whole cost: no extra fine.
- **There are no special storage rooms.** Any shelf on your leased floor counts, bought or found, wherever it stands.
  Where you put your storage is your choice: near the lift for short walks, or near a seam.

### Light at night
- At closing the building's **ceiling lights go out** for the night. **Your own lamps stay on** (outlets are always on),
  so what keeps boxes safe overnight is the light you brought. Ceiling lights matter during the shift, so you can see.
- **Light is measured at each shelf.** Every light that reaches a shelf adds up, so aiming a lamp at it counts, and it
  works the same in a closed room or an open hall. Once a shelf is lit to the top level, nothing on it is taken.
- **You always know what happened:** the board or the pay window shows boxes safe, at risk (in the dark) and lost
  overnight, so a loss is never a surprise.

### The start
- One room near the lift has **one working shelf, a desk lamp and an outlet beside it**. You plug the lamp in and aim
  it at the shelf. That is enough for the first onboarding orders and the first pay. Every other shelf is bought or
  found.
- **Shelf size:** a starter shelf holds **8 to 12 boxes**; bigger shelving may come later.

### Shelves are bought, not crafted
- Decided: **no crafting.** Buying materials and making the same shelf adds steps without adding anything, and pulls you
  away from the real work (moving boxes). Satisfactory's crafting works because building factories is its whole game.
- You buy a **shelving kit** at the hub's **supply counter** (as many as you can pay for): a crate, like the chute kit.
  Carry it down to where you want it.
- **Placing it:** while you hold the kit, a see-through outline of the shelf shows where it will go (like Satisfactory's
  build gun). It snaps flat against walls and turns red where it does not fit. One key turns it, a click places it (keys
  chosen so they do not clash with the current controls; the game's builder is reused where it can be). A placed shelf
  can be packed back into its crate to move it.
- **Found shelves:** broken old shelving turns up out on the floor. Fix it up with hammer and nails (later the repair
  kit) and it is yours for almost nothing. This is what exploring pays.

### The dark takes boxes
- At night, a box that is not in good light gets moved. **The darker it is, the farther it goes:**

| Light where the box was | What happens to it overnight |
|---|---|
| Well lit | Safe |
| Dim | Falls off the shelf, or turns up a few metres away on the floor |
| Dark | Ends up in another room nearby |
| Pitch black, or dark for several nights | Ends up in another part of the floor; very rarely gone for good |

- Moved boxes stop paying until you find them and shelve them again (and can become a "find a lost box" order).
- **Seeing how lit a shelf is:** look at a shelf and hold your gaze on it, and the camcorder's **LOW LIGHT** warning
  fades in on screen if that shelf is not lit enough, as real camcorders warned. It shows only for shelves, not
  anywhere dim. No extra meter on screen.

### Light and power (all the core floor asks of you)
1. **Light** keeps the boxes safe, measured at each shelf (above).
2. **Doors are left out for now.** They do not affect light or boxes; the door kit idea is kept for later if it is
   needed.
3. **Power** runs what you add. Agreed so far:
   - Power never cuts out at random; every cut has a cause you can find and fix.
   - **No breaker panels to hunt for** or circuits to work out.
   - **Wall outlets are always on and low power:** enough for small things like a desk lamp, through an extension cord
     if needed.
   - **Big machines** (a conveyor, later others) **need a generator**, not an outlet. They have a chunky yellow
     industrial plug that only fits a generator, so the rule shows on the plug itself.
   - **Generators** are bought at the counter and set down like a shelf. Each runs a set amount (for example four
     conveyor sections); a dial on its front shows the load. Plug in too much and it stalls with a clunk: unplug
     something or get a bigger one. That is the only cause of a power cut. They thump and rattle, loud against the hum.
   - **The ceiling lights use no power.** They belong to the building: on during the shift unless a tube is dead, off
     at night. You fix them or add lamps; you never power them. Lamps you place use an outlet.
   - **A power bill:** each generator adds a small charge to your day, taken off your pay, so the machines have to earn
     more than they cost. Better generators, unlocked by work orders, cost less to run for each machine. No fuel to
     carry. Tried this way for now; Satisfactory's fuel-first ladder is the fallback if it feels too easy.

### A room set up properly stays safe
- Like a Satisfactory factory that runs once it is built right: shelves with full light and steady power need no more
  attention.
- Upkeep comes back only when **something new happens**, and always for a reason: a new shelf, a new place, something
  you plugged in or moved. No random events.
- **Repair:** at the start, hammer and nails plus spare tubes you buy. Later a **repair kit** is unlocked: it fixes
  lights, broken shelves and fittings.

### Each new place brings one new problem
So the game does not just keep adding layers to the same chain:

| Place | Its problem | Its fixes |
|---|---|---|
| The core | The dark | Lights and power (above) |
| The wings ([open]: whether wings stay) | Distance: far from the lift | Conveyors, roller track, a pallet jack: making routes efficient |
| The Poolrooms | Damp: wet cardboard is ruined | Sealed doors, shelves raised off the floor, a dehumidifier |
| Later levels | That level's own weirdness, e.g. halls that rearrange overnight | e.g. signs and tape lines that hold the route |

Variety also comes from the clients' loads (fragile, heavy, mixed, audits: section 7), and tidy storage (each client's
boxes together) makes audits quick.

## 9. Money

| What | How it works now | |
|---|---|---|
| Pay | Each finished job pays about $30 plus an amount a box, more the farther it goes, rounded to $5 | [built] |
| Rent | $2 a day for every box kept in storage, +$1 a box per rent review | [built] |
| Rent, agreed change | Only boxes on a lit shelf pay (section 8). Clients pay this daily charge from day one | [planned] |
| Power bill | Each generator takes a small charge off the day's pay (section 8) | [planned] |
| Spending | The improvements board only, one a night | [built] |
| Spending, agreed change | The supply counter, as much as you can pay for; later an upgrade station (section 10) | [planned] |
| The pay window | Pays out at the end of the day; the board shows IN HAND $ | [built] |

[open] Whether money is spent on anything else (supplies, furniture for your own break room), and whether there is a
quota you must meet.

## 10. Improvements (what you can buy)

**Today** [built]: everything is bought at the improvements board in the lobby (NEXT and ORDER), never during the
shift, at most one a night.

**Agreed change** [planned]:
- **The one-a-night limit goes.**
- **The supply counter** in the hub sells things you carry down and use: kits and supplies, as many as you can pay for.
- **An upgrade station**, later, sells permanent upgrades that last for good (longer shifts, better rent, leases).
  Until it exists, those stay on the improvements board.
- In the work-order plan (section 7), finishing orders unlocks things at both; unlocked is not free.

| What | Price now | What it does | Sold at | |
|---|---|---|---|---|
| Intake chute kit | $150 | A crate by the lift; set it down in front of a seam and a chute is fitted there for good: it pulls the client's boxes through by itself | counter | [built] |
| Platform truck | $120 (two at most) | A flat trolley that boxes ride on | counter | [built] |
| Overtime | $200 / 300 / 400 | The shift runs to 6, 7, then 8 PM | upgrade station | [built] |
| Backup lights | $150 / 250 | One more minute after closing each | upgrade station | [built] |
| Rent review | $250 / 400 | +$1 rent a box a day each | upgrade station | [built] |
| Lease a wing | $400, +$200 per wing owned | The wing's hoarding comes down; its seams join the board | upgrade station | [built] |
| Poolrooms access | $900 (needs a wing) | A door to the Poolrooms opens past a leased wing | upgrade station | [built] |
| Shelving kit | [open] | A crate; set up a shelf where you want it (section 8) | counter | [planned] |
| Door kit | [open] | Snaps into an empty doorway. Left out for now: doors do not affect light or boxes | counter | [open, later] |
| Spare tubes | [open] | Replace dead or blown ceiling tubes | counter | [planned] |
| Repair kit | [open], unlocked by an order | Fixes lights, broken shelves and fittings | counter | [planned] |
| Power items | [open] | Depends on how power works (section 14, question 12) | counter | [open] |
| Wing and Poolrooms fixes | [open] | Conveyors, roller track, pallet jack; raised shelves, dehumidifier (section 8) | counter | [planned] |

### Progress on the main floor: three ladders [planned]
Agreed 2026-11-08 ("sounds right for now"). Progress is climbing these: each step makes the same job faster or safer,
is unlocked by a work order and bought at the supply counter. The floor slowly turns from a dark office with one lamp
into a lit storage operation you built yourself. Which order unlocks which step is still to work out.

| Ladder | Steps |
|---|---|
| **Light** | Desk lamp on an outlet → work lamp on a stand (brighter, covers two shelves) → floodlight on a generator (lights a whole row) |
| **Storage** | Starter shelf (8 to 12 boxes) → taller shelving → pallet racking |
| **Moving boxes** | By hand → platform truck → intake chute at a seam → conveyor from the seam to your shelves, run by a generator |

**More ideas, Satisfactory-style "make your output better"** [open]:
- Chute upgrades: faster, or straight onto a truck or pallet.
- A roller track as a cheaper, unpowered step before conveyors.
- A pallet jack for whole pallets; a bigger lift cab.
- Lights and signs you put up yourself to mark routes (to make the walk faster and safer).
- A label printer or scanner that shows what is stored where.

## 11. Danger

- **Closing time** is the main one: lights out, then blackout and lost work. [built]
- **Low activity:** after closing, and in the dark generally, the building's tension rises; things in dark rooms move
  while you are away (a chair dragged, a box shifted), never what you set up near your own light. [built]
- **The dark takes boxes:** at night, boxes not in good light are moved, farther the darker it is, and stop paying
  until shelved again (section 8). An overloaded generator stalls what it runs. [planned]
- **Damp** in the Poolrooms ruins boxes left low or behind open doors (section 8). [planned]
- **Getting lost:** the floor is big and copies itself; chalk, tape and spray paint are your map. [built]
- **Seams shutting** with you inside. [built, not checked]
- **Creatures:** none for now. A later plan may add them. [open]

## 12. What you have (tools and moving things)

All [built], from the earlier milestones (`Docs/Interaction.md`):
- **The camcorder** in one hand, its zoom and its F5 menu.
- **The free hand** picks things up; **four pockets** (keys 1-4); **a backpack** (B) with room inside.
- **Furniture:** grab it at a point and push, pull and turn it; sit on chairs and sofas.
- **Carts:** utility cart, janitor's cart, platform trolley; what is on them rides along.
- **Boxes:** cardboard boxes in sizes, the clients' boxes with their tape.
- **Marking the way:** chalk, blue painter's tape, red spray paint.
- **Power and light:** a desk lamp, an extension cord, live wall outlets, breakers.
- **Fixing things:** hammer and nails (board up a doorway), breaking walls.
- Agreed additions [planned]: the **shelving kit** with its see-through placing outline, hammer and nails also fixing
  found shelves, **spare tubes**, the **repair kit**, and the camcorder's **LOW LIGHT** warning (section 8).
- **Doors:** opened by hand, shoved, closed.
- **Noclip doors and crawl holes** that lead farther than the wall could hold.

### Carrying boxes (agreed 2026-11-08: "the main next thing you do is pick up boxes and move them ... must feel satisfying")
Today [built]: hold the left button on a light box and both hands carry it low in front of you; mouse up and down lifts
it; let go and it drops (and it drops if a wall holds it back); one at a time; right click flings it. The owner, playing
it: too slow reaching down or walking over to a box, it covers the camera a bit too much, dropping and throwing do not
work well, right click "flings it weird and spins it". The agreed way [built 2026-11-08, checked in play by script]:
- **Click to pick up, click to put down** (no holding the button). A quick bend and lift, about a third of a second; a
  box up to about 2 m away is taken with the body leaning in, not walking over; one on a high shelf is reached up for
  and slid off.
- **Carried low at the hips**, covering less of the picture; more boxes block a little more. A slight bob as you walk and
  a little lag when you turn. **Light, normal and heavy boxes**; heavy ones slow you. A wall stops a box but never knocks
  it out of your hands.
- **A few in your arms:** click another box while carrying to put it on top, up to about 20 kg (three light, two normal
  or one heavy). Hold the click on a stack to take its top few at once. Turn too fast and the top ones fall off.
- **Putting down:** a see-through outline shows where it goes where you look (a shelf, the floor, another box), R turns
  it a quarter turn, a click puts it there. **Q** drops it at your feet; **hold Q** for a gentle toss, further the longer
  you hold. Right click does nothing to a carried box (no more flinging). On shelves and the floor it sits where it is
  put, loose (no slots): a stack can topple.
- **Satisfying:** a small dip of the view lifting something heavy; a box lands with a soft settle (a tiny squash, a puff
  of dust) and a thud; a clean click when the outline sits flush with a shelf edge or another box; a cardboard rustle
  picking up, a scrape dragging. No grunts. What a box is shows only on the box itself (its label, its tape, FRAGILE).
- **Your body is always shown** (the full body is the default: the Classic style is being rolled out of the game).
- **Box shapes by client:** the WAREHOUSE plain medium boxes; the PRINT SHOP small heavy boxes of paper; OFFICE SUPPLY
  small and medium; the FURNITURE STORE long flat flat-pack boxes.
- **On the truck and carts** boxes sit fairly tight; a hard knock can still spill them.
- **Later:** two-person boxes in co-op (the furniture store's biggest; once co-op shares the storage work), and the
  carrying-gear ladder: hand truck (a dolly for 4-5 boxes), platform truck, pallet jack and pallets, conveyors.
- **As built:** boxes are taken from 2.4 m; up to 20 kg and 0.75 m of boxes in the arms (the bottom one carried lower as
  the stack grows, so the top stays under the eye; four small boxes stood up to the eye at first); while you carry a box
  the camcorder on its strap looks where you look, from nearer the eye (on the chest it followed a quarter of the look,
  and the box filled the picture). The shapes: WAREHOUSE 0.5 x 0.36 x 0.4 m, 8 kg; OFFICE SUPPLY small 0.34 x 0.24 x
  0.28 m, 5 kg, or medium 0.46 x 0.32 x 0.36 m, 7.5 kg (by the day); PRINT SHOP 0.43 x 0.27 x 0.3 m, 12 kg; FURNITURE
  STORE 1.0 x 0.12 x 0.45 m, 13 kg. Boxes saved before keep the old box. Each batch has its own pile at the seam.

## 13. Co-op

Two to four players [built]: one hosts their world, the others join by its address (Hamachi, a LAN or any VPN). Players
see each other's bodies and hands; items, furniture, carts, doors, lights, paint and marks are shared; the host's save
keeps what each friend carried. Proximity voice chat moves the suit's mouth. Everyone wakes on their own cot and rides the
lift together. [Not checked with the working day and contracts yet.]

## 14. Still to decide [open]

1. **Side jobs:** dropped for now; do any come back later, and as what?
2. **Doors:** left out for now. Do they come back later, and what would they do that is fun and feels like progress?
3. **Light levels:** how many steps between dark and fully safe, and how much light a lamp gives (tuned in play).
4. **Order kinds:** which of the remaining ideas in section 7 to use.
5. **Losing:** is losing the day's work at blackout enough, or should blacking out cost money or a stored box? (Still
   to figure out.)
6. **The end:** does the game have an ending (the manager's office, a last level), or does it go on?
7. **Past the Poolrooms:** what comes next, and what work is there?
8. **Creatures:** never, later, or only after closing?
9. **The tape check:** should the pay window check your recording (REC) to pay?
10. **Free roam:** bring the endless Backrooms back as a mode or leave it out?
11. **Lockers:** decoration, or personal storage? (The owner is thinking about it.)
12. **The wings:** keep them or not, and what they are for. Talked about after the main level.
13. **Prices** for the supply counter's kits and supplies, and the power bill's size.

## 15. Where things stand (2026-11-08)

- **In and checked in play:** the hub and waking, the lift, the floor with its edges and wings, the taped seams and the
  four real-world rooms, the board's store jobs, closing and blackout, leasing a wing, the improvements board, the chute
  kit, the trucks, the camcorder's date, the Poolrooms door appearing.
- **In, not played through yet:** a full Ship and a full Return delivery, walking through the Poolrooms door, the duct
  tape's final colour, the new desks and the last remade furniture in game, co-op with the working day.
- **Built 2026-11-08, checked in play by script (not played by hand yet):** the starter shelf and its desk lamp by the
  lift (the lamp plugs itself in; the shelf measures safe), knocked-over shelves in the old storerooms, light measured
  at each shelf from your own lamps, the LOW LIGHT warning when you look at a shelf, Store work counting boxes on
  shelves, rent only for boxes on safe shelves, the night moving boxes out of the dark (pitch black sent five boxes
  60-120 m away), the morning report, the work orders' first days (clock in, first storage, paid on completion),
  outlets always live, a generator and a conveyor carrying a box, the power bill.
- **Also checked in play by script (2026-11-08, second pass):** the supply counter (every item bought and delivered by
  the lift), the tiers unlocking the counter's goods and the improvements in order, the see-through outline (green, red
  where something is in the way, R turns it) and putting a shelf and a generator up from their crates, packing a shelf
  back into its crate with G, standing a knocked-over shelf up with 4 nails, plugging machines in by clicking them, the
  generator stalling over its load and starting again, a floodlight lighting a shelf 3 m off to safe, the work lamp
  plugging in, a fragile box breaking when dropped, an audit lost at noon, the stand-in tape, the notice board, and
  shelves and machines coming back after saving and loading (they were being dropped on loading: fixed).
- **Also checked in play by script (2026-11-08, third pass):** the work orders on the camcorder's screen in the hub and
  on the floor, J hiding them, and the hidden tracker popping up when an order moved on.
- **Checked in play by script (2026-11-08, fourth pass):** the first seam beside the lift's district and the blue tape to
  it (about 20 m of tape), FIRST STORAGE's boxes waiting there; the hub's sounds turned down to about the floor's level
  (they were 10-18 times as loud), the training room TV's hiss and the lift's fan now following AMBIENCE.
- **Not checked yet:** tall shelving and pallet racking put up in the game (their models now load), carrying a crate by hand with the
  mouse and putting it up with a real right click, a full day played by hand.
- **Modelled 2026-11-08 (Tools/Blender/build_machines.py, checked in play by script):** the generator (a tubular roll
  cage on two wheels, the amber engine, a cream tank, its dial with the game's needle and the yellow socket), the
  conveyor section (avocado side frames, the dark belt over end drums, four braced legs, the motor under the discharge
  end), the floodlight (a tripod, a telescoping pole, one amber head in a yoke behind a wire guard, the game's lens lit
  when it is on) and the work lamp (a heavy foot, a pole held like the floor lamp's, a small amber work-light head
  throwing a wide beam ahead). Tall shelving and pallet racking had been showing as plain shelves (their models were
  imported unreadable): fixed. Random side jobs come back after the gate.
- **Not made yet:** spare tubes and the repair kit (the counter does not sell them yet), heavy and mixed loads, the
  "find a lost box" order, co-op for the new storage work (placing, the night and machines are not shared yet), the
  orientation tape in the training room and its update for all of this, the PA voice and the muzak, furnish jobs,
  anything past the main floor.

## Other docs

- `Docs/HubPlan.md`: the hub, room by room, and how it was built.
- `Docs/ContractFloorsPlan.md`: the floor, seams, contracts, the working day and improvements, in detail.
- `Docs/StyleSheet.md` and `Docs/StyleRollout.md`: the Overflow look.
- `Docs/Interaction.md`: hands, pockets, carrying, furniture, items.
- `Docs/Poolrooms.md`: the Poolrooms and the water park.
- `Docs/MultiplayerPlan.md`: co-op.
- `Docs/Camcorder.md`, `Docs/Audio.md`: the camera and the sound.
- `Docs/ProjectBrief.md`: the owner's first brief (2026-10-20). Its feel (the camcorder, physical things, no meters, the
  building as the danger) still holds; its story (falling in and trying to escape) is replaced by the job at Overflow.
- `Docs/WORKLOG.md`: the history of everything built.
