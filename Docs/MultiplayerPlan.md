# Multiplayer plan: co-op with a friend (Hamachi or LAN first)

Written 2026-11-04 at the owner's request ("write out a plan for how you would make multiplayer work, maybe even just
with Hamachi and my friend for now ... a code and adding his Hamachi IP"). Nothing here is built yet.

## The goal

Two players (later up to four) in one world. One player **hosts** their saved world; the other **joins** by typing the
host's IP address (their Hamachi address, 25.x.x.x) and a port. Each player has their own camcorder, body and hands. The
world is the host's save: the friend keeps nothing when they leave.

## The technology

- **Netcode for GameObjects 2.x** (Unity's own networking, made for Unity 6 LTS) with **Unity Transport** (UDP, port
  7777). Its host mode is exactly the "one player hosts, the others join" model, and a client can connect straight to an
  IP and a port, which is what Hamachi needs. Later, the same code can use **Unity Relay** (join with a code, no VPN, no
  open ports) or **Steam's networking** (invite from the friends list) without changing the game's code.
- Alternatives considered: Mirror and FishNet (free, community-made, well proven), Photon Fusion (paid above a small
  size), Steam only (no IPs, but needs a Steam app ID first). Netcode for GameObjects wins on being Unity's own, current
  with Unity 6, and having Relay ready for later.

## How the world is shared

The world is generated from its seed, so both machines build the same building on their own: only the seed and the
**changes** go over the network. The host decides everything that happens in the world (it is the "authority"); a player
only decides their own movement, and what they are holding while they hold it.

| What | How it is shared |
| --- | --- |
| The world itself | The seed and the world settings, sent when you join. Each machine generates it. |
| Changes so far | A snapshot of the host's save (moved furniture, broken walls, paint, chalk and tape, lights switched off, breakers, doors, loot taken, every item) sent in pieces when you join, using the save format that already exists (WorldSaver). |
| Players | Each player's position, view, stance and what they hold (owner-driven, smoothed on the other screen). The other player is shown with the full body that already exists, walking by their speed, their camcorder in hand. |
| Items | Each item is a networked object made by the host. Picking one up hands it to that player ("ownership"); dropping or throwing hands it back. Its state (paint left, a lamp's switch, a tape's position) goes with it. Pockets and backpacks are which item is inside which. |
| Furniture and carts | The host moves them; when a player grabs one, it is handed to that player while held (as co-op physics games do: whoever touches it simulates it, the others see it smoothed). Sitting is a player state. |
| Doors, switches, breakers, the elevator, the vending machine, the boombox | A player asks ("press this button"), the host does it and tells everyone. |
| Paint, chalk, tape | The spray is sent as what the can did each moment (where the nozzle was, which way, its random seed); every machine traces the same rays and paints the same paint. Chalk and tape strokes are sent as their points. |
| Walls broken by the sledgehammer | The swing's hit and its seed; every machine breaks the same pieces. |
| The dark, the shadow, the tension | The host decides, taking every player's view into account ("nobody is looking"), and the moves are shared as furniture moves. |
| Sound | Each machine makes its own sounds from what happens. Voice chat: Discord at first; later voice in the game that fades with distance (Unity Vivox, Steam Voice or the Dissonance asset). |
| Saving | The host's save slot, as now. |

## What has to change in the game (the real work)

1. **The world streamer** loads chunks around one player; it must load around every player (the host's machine keeps
   both areas alive).
2. **The hand, the body, the pockets** (PropGrabber, FirstPersonBody) assume one local player. They stay local, but every
   change they make to the world goes through the network (ask the host, or own the object while holding it).
3. **Single "Instance" systems** (paint, marks, wall damage, the dark, the shadow, the light zones, the tension) must
   apply changes that come from the network the same way as local ones, and never act on their own on a client.
4. **Furniture physics**: hand-over of ownership when grabbed, frozen copies on the other machine until then.
5. **Determinism checks**: both machines must generate exactly the same world from the seed (same build, same settings;
   the tests can check the generated plans match).

## Steps

| Step | What works at the end | Rough time |
| --- | --- | --- |
| 0 | A MULTIPLAYER page: HOST (shows your IP addresses, the Hamachi one marked) and JOIN (type an IP and port). The package installed; the two games connect and say so. | a day or two |
| 1 | Two players walking in the same world: the seed shared, each other's bodies, chunks loaded round both. | a week |
| 2 | Doors, switches, lights, breakers, the elevator; items picked up, dropped, thrown, pocketed; the backpack. | 1-2 weeks |
| 3 | Furniture and carts handed to whoever holds them; sitting; paint, chalk, tape; the sledgehammer. | 1-2 weeks |
| 4 | Joining a world already played (the snapshot); saving; the dark and the shadow with two players. | a week |
| 5 | Polish: voice, disconnects and rejoining, testing over Hamachi with real lag; then Relay or Steam so no VPN is needed. | open |

A first playable "walk around together" is about a week of work; everything synced and solid is one to two months.

## Playing over Hamachi (what you and your friend do)

1. Both install Hamachi. One makes a network (a name and a password); the other joins it. Each now has a 25.x.x.x
   address.
2. The host starts the game and picks MULTIPLAYER > HOST. The page shows their addresses; the 25.x.x.x one is for the
   friend.
3. The first time, Windows asks whether the game may use the network: allow it (tick both private and public, because
   Hamachi's network often counts as public). If the friend cannot connect, add an inbound rule in Windows Firewall
   for the game (or UDP port 7777).
4. The friend picks MULTIPLAYER > JOIN, types the host's 25.x.x.x address and port 7777, and joins.
5. If Hamachi will not connect, Radmin VPN, ZeroTier or Tailscale work the same way (a private address to type in).

## Risks

- The hand and the furniture physics are the most delicate part: grabbing something the other player is holding, or
  two players pushing one couch, needs clear rules (the first to grab holds it; the second is refused until it is let go).
- The dark's "while you are away" needs a rule for two players (away from both).
- Bandwidth is small except the join snapshot (paint canvases are compressed; a big mural is a few hundred kilobytes once).
- The game's tone: alone is the brief. Co-op would be a separate mode, solo staying the default.

## Sources

- Netcode for GameObjects 2.x (Unity 6 LTS), its FAQ on hosting and connecting by IP, and Relay:
  docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.6, mp-docs.dl.it.unity3d.com/netcode/2.0.0/relay
- A Unity 6 / Netcode 2.x walk-through of playing with a friend through Relay and Lobby (uhiyama-lab.com).
- Hamachi and Unity: the firewall rule a Unity build or the editor needs for its UDP port (discussions.unity.com);
  players' reports of joining by the 25.x address, and Radmin VPN where Hamachi failed (forums).
- Co-op physics ownership: whoever touches a rigidbody simulates it (Microsoft Mesh's physics), and a request-and-grant
  hand-over (coherence's tutorial).
