# Co-op multiplayer (2 to 4 players)

Built 2026-11-04 at the owner's request ("plan and work on the full system for multiplayer, double checking for bugs or
errors, just for co-op 2-4 players"). This replaces the earlier plan; the code is in `Assets/Backrooms/Scripts/Runtime/Net`.

## Playing with friends (Hamachi, a LAN, or any VPN)

1. Everyone joins the same Hamachi network (or Radmin VPN, ZeroTier, Tailscale, or the same home network). Each player
   gets a private address: Hamachi's start with 25.
2. The host picks **MULTIPLAYER > HOST A WORLD** and one of their three worlds (an empty slot makes a new one). The page
   shows their addresses (the Hamachi one first) and the port (7777, changeable).
3. The first time, Windows asks whether the game may use the network: allow it for private and public networks
   (Hamachi's network often counts as public). If friends cannot connect, allow the game (or TCP and UDP port 7777) in
   Windows Defender Firewall.
4. Each friend picks **MULTIPLAYER > JOIN A GAME**, types the host's address (25.x.x.x, or `25.x.x.x:7777`) and joins.
   Set your name on the MULTIPLAYER page first; it is shown over your head and the host keeps your things under it.
5. Escape opens the menu as usual, but the world does not pause (the others are still playing). The host's menu shows
   the addresses again and **SAVE AND END THE GAME**; a friend's shows **LEAVE THE GAME**. Tab shows who is here and
   their ping.

The host's world is the world, saved in the host's slot with everything everyone did in it. A friend keeps nothing in
their own saves, but the host keeps what each friend was carrying and where they were (by name); joining again under the
same name puts them back there with their things. Everyone must run the same version of the game: a different version is
refused, and the two games check after joining that they built the same building.

## What is shared

| What | How |
| --- | --- |
| The building | The seed, the host's world-generation settings (F5) and custom rooms are sent on joining; every game builds the same building from them. A fingerprint of the first chunks is compared. |
| What had changed | A copy of the host's save made at that moment (moved furniture, broken walls, nails, lights, breakers, doors, homes, loot taken), its paint and its chalk and tape. |
| The players | Each game sends its player 20 times a second: where, the view, the stance, sitting, the hand, what is held and where. Others are drawn with the full body (their shirt in their colour, a name over the head), a tenth of a second behind, smoothed between updates; feet step by IK, the camcorder held to the eye, the right hand where theirs is. Footsteps are heard from their feet. |
| Items | Every item has an id (each player gives out ids from their own range). Picking up, pocketing, putting in a backpack, wearing it, a cassette into a boombox, dropping and throwing are told as they happen; the game that let go of a thing moves it until it settles and says where it came to rest. Two players reaching for the same thing: the host says who was first, the other's hand lets go. Paint left, a lamp's switch, a bag open or shut go with the item. Loot taken from a room becomes the same item in every game. |
| Furniture and carts | Whoever touches a piece moves it (their game simulates it, everyone else's follows it kinematically) and says where it came to rest; the host's record keeps it. A piece someone else is moving cannot be grabbed until it settles. An office chair moves with whoever sits on it. |
| Doors | Swung by whoever has the handle (the angle streams), left at an angle (kept in the save), bolted. |
| Lights, breakers, homes | Wall switches, breakers, rooms claimed as home, lights the dark puts out, the shadow's flicker. |
| Buttons | The elevator's buttons, the drinks machine, the boombox's keys: a press in one game is a press in all. |
| Paint | What is painted goes as the changed part of each paint canvas, merged by taking the larger (two players spraying one wall both keep their paint). Drips run in the painter's game. The friend's can shows its jet, haze and hiss. |
| Chalk, tape, scrapes | Each stroke and strip as drawn. |
| Walls and nails | A sledgehammer blow goes with its random seed: every game breaks the same pieces (their dust and fallen bits made locally). Nails driven in and pulled. |
| The dark | Its furniture moves happen in the host's game only, out of everyone's sight and away from everyone; the shadow and the tension are each player's own (your own scares), but what they do to the building (a room going dark, a door shutting) is shared and kept out of every player's view. |

## How it works

- **Connection** (`NetTransport.cs`): plain sockets polled once a frame, no package. TCP carries everything that must
  arrive, in order (framed by length); UDP carries the frequent updates once both ends have heard each other's
  datagrams, falling back to TCP if a firewall blocks UDP. The host listens on one port for both. 15 s of silence ends a
  connection; the host says goodbye before closing. This is how Minecraft and Terraria (TCP) and most co-op games (UDP
  for movement) are built; Unity's Netcode for GameObjects was set aside because its core is networked prefabs and this
  world is built from code (every object would have gone through custom messages anyway, as Valheim's own system does),
  and it would have meant installing a package.
- **Session** (`NetSession.cs`): hosting, joining, the handshake (version, a free player slot, a unique name), the copy
  of the world for a joiner, the players' list, notices (who joined and left), Tab.
- **World** (`NetWorld*.cs`): players, items, furniture, doors and the building's changes. Authority as in Valheim:
  each game simulates what its player touches; the host keeps the record and settles who has what. Hooks in the game
  (`NetHooks.cs`) say when something changed here; changes from the network are applied with the hooks quiet, so nothing
  echoes back.
- **Saves**: the host's save gains `guests` (each friend's position and carried things). A joined world is never written
  on the friend's machine.
- **Bandwidth**: about 2-3 KB/s per player while moving; joining sends the world once (a few KB to a few hundred KB
  with a lot of paint).

## Tested (2026-11-04)

On one PC with the editor hosting World 3 and one or two built copies joining over the loopback, driven by the test
remote control (below): joining and the world check; seeing each other walk, crouch, sit on a sofa; picking up, holding,
spraying (paint and the jet seen by the others), throwing (landing in the same place), pocketing (gone from the others'
world), leaving and coming back (put back where they left, with the chalk back in the pocket); loot (a found backpack)
taken; two players grabbing one can at once (the host's player got it, the other's hand let go); a light switch from
either side; a door opened by its handle; a box knocked off a stack and one carried; the host quitting (everyone told
"THE HOST QUIT"); a failed join and a taken port. EditMode tests: `Editor/Tests/NetTests.cs` (messages, the world copy, a
real connection on this machine with both channels and the goodbye).

**Not tested**: over real Hamachi between two PCs with real lag and packet loss (only the loopback on one machine).

## Known limits

- A lamp's cord: where its plug is plugged in is shared; the cord's hang is each game's own.
- The elevator: its buttons are shared and each game runs its own car, so a rider can be a moment out of step.
- A tape's position in a boombox can drift a little between games (its keys are shared, not every second of play).
- Voice: none built (use Discord); later, in-game voice that fades with distance.
- Over the internet without a VPN: the host must forward the port; Unity Relay or Steam's networking would remove that.
- Joining when someone is spraying finishes any drips running at that moment.

## Testing it (developers)

A copy started with `-netdebug -netjoin 127.0.0.1:7777 -netname SAM` joins at once and obeys the host's
`Backrooms.Net.NetDebug.Send(peer, "command")` (from the editor): `pos`, `tp x y z yaw`, `walk x z seconds`,
`look yaw pitch`, `lookat x y z`, `items`, `pick [id]`, `drop`, `throw speed`, `pocket i`, `hold`, `unhold`,
`use seconds`, `e`, `sit`, `press`, `prop HEXID`, `door KEY`, `light cx cy cz room`, `paint`, `shot path`, `leave`,
`quit`. Replies appear in the host's console as `[NetDebug] NAME: ...`. Without `-netdebug` a copy ignores them.

## Sources

- Valheim's networking: each object simulated by one peer, relayed through the host; ownership to whoever is near or
  using it (Edgegap's Valheim backend deep dive; the SpreadTheLoad mod's notes).
- Physics authority: non-owners keep the body kinematic and follow; ownership changes on touch (coherence's physics and
  authority-transfer tutorial; Unity Netcode's NetworkRigidbody notes; Microsoft Mesh physics).
- Snapshot interpolation: render remote entities about 100 ms (two send intervals at 20 Hz) behind, extrapolate briefly
  when starved (geckos.io snapshot-interpolation docs; Gabriel Gambetta's entity interpolation).
- Netcode for GameObjects 2.x for Unity 6 (its repository and release notes), considered and set aside as above.
