# The style lab

A small room for settling the game's new look before it goes everywhere: `Assets/Backrooms/Scenes/StyleLab.unity`.
Started 2026-11-05. References: Lethal Company, PEAK, RV There Yet?, The Big Walk: chunky low-poly characters and
props in flat colours, real lighting and fog kept for the mood, and a stylising pass over the picture.

## What is in it

| Thing | Where |
|---|---|
| The room: 8 x 8 m, 2.7 m high, suspended ceiling with four light panels, a doorway onto a 6 m corridor whose light is dead | `Models/StyleLab/StyleLab_Room.fbx` |
| The player: a yellow hazmat suit, dark visor, respirator, air tank, black gloves, boots and taped cuffs. Every limb on its own joint (`Pelvis/Chest/ShoulderR/ElbowR/WristR`...) so it can be posed | `Models/StyleLab/Hazmat.fbx` |
| The work camcorder from the orientation tape: chocolate body, the triple stripe, ribbed lens and hood, carry handle, the microphone clamped on its post | `Models/StyleLab/Camcorder.fbx` |
| A platform cart with a U handle | `Models/StyleLab/PlatformCart.fbx` |
| Flat-colour URP Lit materials, one per colour | `Materials/StyleLab/SL_*.mat` |
| The look: `StyleLabLook` on `StyleLabCamera` | `Scripts/Runtime/StyleLab/StyleLabLook.cs`, `Shaders/StyleLab.shader` |
| Post (tonemapping, a little bloom and vignette) | `Settings/StyleLab_Volume.asset` |

The models are made by `Tools/Blender/style_lab_models.py` (run `F:\Blender\blender.exe -b -P Tools/Blender/style_lab_models.py`;
the .blend files land in `Tools/Blender/StyleLab/`). Change a number there and re-run to reshape anything; Unity
re-imports the FBX and keeps the materials.

## The suit versions (2026-11-06)

Three proofs of concept for the player, all in the company orange-yellow (`SL_SuitAmber`, #F0A21E) with the company
stripe (orange, gold, avocado on the camcorder's dark brown), made by `Tools/Blender/style_lab_characters.py`
(`F:\Blender\blender.exe -b -P Tools/Blender/style_lab_characters.py`). They keep the first Hazmat's joints and add a
ball at every bending joint so nothing splits when posed.

| Version | Shape |
|---|---|
| `HazmatSoft` | rounded and friendly: one soft body, a big round hood with a wide visor, mitten hands, a band round the chest |
| `HazmatCrew` | a worker's proportions, faceted and flat shaded, a baggy suit, a square visor, stripes on the chest and arms, reflective shin tape |
| `HazmatStout` | short and chunky like a toy: a big head with a round visor, a big backpack, big gloves and boots, the stripe down the chest and round the cuffs |

They stand in the scene's `CharacterLineup` group (inactive; turn it on and enable its `LineupCamera` for the front
shot). Pictures: `Docs/screenshots/stylelab/suit_versions_front.jpg`. The lab through the game's own camcorder picture:
`lab_camcorder_look.jpg` (and `lab_camcorder_plus_outlines.jpg`: the outline pass under the camcorder misaligns, because
the camcorder's lens distortion runs before it; the outlines would have to run before URP's post-processing to stack).

### Round two: softer versions of Stout

The owner leaned toward Stout but less blocky ("not super realistic, not Minecraft"). Three more, made by
`Tools/Blender/style_lab_characters2.py`: rounded pillow shapes (superellipsoids), tapered limbs, fabric folds at the
elbows and knees, the stripe wrapped onto the body's own surface.

| Version | Shape |
|---|---|
| `HazmatBun` | an egg-shaped body, a big round head with a wide wrap-round visor, a chin filter, a rounded backpack, the stripe round the chest and cuffs |
| `HazmatHood` | a loose baggy suit: a hood over a rounded face plate, one big filter and drawstrings, flared sleeves and trousers rolled at the boots, a stripe badge and stripes on both arms |
| `HazmatScout` | the shortest: a big head with a tall oval visor, a headlamp and aerial, the stripe as a sash, belt pouches, knee pads, twin tanks |

In the scene's `CharacterLineup2` group (inactive, with `LineupCamera2`). Pictures: `suit_versions2_front.jpg`,
`suit_versions2_three_quarter.jpg` and `suit_versions2_camcorder.jpg` (through the game's camcorder picture).

### Round three: the enclosed suit, with a face behind the glass

After the owner's reference photo of a gas-tight suit: `HazmatSuit`, made by `Tools/Blender/style_lab_characters3.py`.
One big hood with a large rounded window (a pocket is cut back behind the face, so the hood is hollow there), curved
glass (`SL_VisorGlass`: URP Lit, transparent, premultiplied so the shine stays, sky reflections off) and a rubber frame;
the breathing set inside a hump on the back with the company's round badge; the diagonal zip from the left shoulder to
the right hip with the stripe beside it; an exhale valve; glove rings; the legs coming down over the boots.

Three faces sit under the Neck joint; show one: `Face_Plain` (big eyes, brows, a worried mouth), `Face_FullMask` (a
respirator mask with its own lens and filter) and `Face_HalfMask` (nose and mouth covered, eyes and brows showing). The
skin and hair materials glow a little (emission 12-17 % of their colour) and the face pieces neither cast nor receive
shadows (set on the lineup instances only so far), or the hood's shadow leaves the face black.

In the scene's `CharacterLineup3` group (inactive, with `LineupCamera3`). Pictures: `suit_enclosed_camcorder.jpg`,
`suit_enclosed_faces.jpg`, `suit_enclosed_front.jpg`.

### Round four: less toy-like (`HazmatSuit2`)

The owner's notes on round three: less toy-like, the diagonal stripe looks odd, and the face should be the player's
choice. `Tools/Blender/style_lab_characters4.py` (reusing round three's window code) makes it about six heads tall
instead of three: a smaller hood, window and face, a longer body and limbs, narrower hips, a smaller hump; the face is
plainer (smaller eyes, no blush). The company stripe is now a band round the chest and both upper arms (like the
reflective tape on real protective suits); the zip stays diagonal, as in the photo, as a plain dark line.

`HazmatAppearance` (`Scripts/Runtime/StyleLab/HazmatAppearance.cs`) on the suit picks the face (Plain, FullMask,
HalfMask) and the skin tone (from its list of skin materials), and keeps the face out of the hood's shadow. It works
in the editor; `Set(face, tone)` is what a character menu would call.

In the scene's `CharacterLineup4` group (inactive, with `LineupCamera4`). Pictures: `suit2_camcorder.jpg`,
`suit2_faces.jpg`, `suit2_three_quarter.jpg`.

Note: after re-importing a suit's FBX with `ImportAssetOptions.ForceUpdate`, the lineup instances lost their positions
and names (all at the origin); re-place them after a re-import.

### Round five: shorter, new faces, masks as a separate choice (`HazmatSuit3`)

Round four read a little tall and its faces needed work. `Tools/Blender/style_lab_characters5.py` makes it about five
heads tall (1.72 m, between rounds three and four) and gives it new faces, still simple, where the eyes and brows carry
the expression and a few details make each one a person:

| Face | Look |
|---|---|
| `Face_Worker` | tired: half-closed eyes with a lash line, flat brows, a moustache, short hair |
| `Face_Rookie` | nervous: open eyes with a shine, raised brows, freckles, a side-swept fringe |
| `Face_Veteran` | steady: square glasses, thick brows, a firm mouth, a receding hairline |

Masks are their own choice over any face: `Mask_Half` (a half mask with two filters) and `Mask_Full` (a rubber-framed
lens over the eyes, a chin cup and a front filter, so the eyes still show). `HazmatAppearance` now takes the face, the
mask and the skin tone; the round-four lineup no longer uses it (its suit has the old face names).

Half-closed eyes are made by flattening the eye and adding a dark lash line. A separate lid shape was tried first, but
the outline pass drew it as a pale ball over the eye.

In the scene's `CharacterLineup5` group (inactive, with `LineupCamera5`). Pictures: `suit3_camcorder.jpg`,
`suit3_faces.jpg`, `suit3_three_quarter.jpg`.

### The Worker's painted face

The owner picked the Worker and asked for its eyes and mouth to be 2D, keeping the nose, brows and moustache 3D (no
expressions yet). `Tools/Blender/face_textures.py` paints `Textures/StyleLab/T_Face_Worker.png`: heavy-lidded eyes
with a shine and a faint bag under each, and a short mouth with a slight downward curve. Everything else is
transparent, so it works on any skin tone. The picture covers a 0.12 m square of the face seen from the front.
`face_decal` in `style_lab_characters5.py` makes `FacePaint`, a thin copy of the front of the head lifted 0.8 mm off
it, with UVs projected straight from the front over that same square, so the features are not stretched round the
head. Its material `SL_FacePaint_Worker` is URP Lit, transparent (queue 2990, under the visor glass at 3000), with the
texture as emission at 25 % so the shine stays bright inside the hood.

To change the features, edit and run `face_textures.py`; Unity re-imports the picture. Expressions later would be more
pictures (a sheet) swapped on the same decal.

Pictures: `worker_2d_faces.jpg` (three skin tones and both masks), `worker_2d_side.jpg`, `worker_2d_camcorder.jpg`.

### Round six: details and face choices (`HazmatSuit4`)

The owner kept the round-five suit and the Worker's painted face and asked for suit items 1-5 and face items 1-4 of a
list of improvements. `Tools/Blender/style_lab_characters6.py` builds it (about 1.5 minutes, most of it the bake);
`Tools/Blender/face_textures.py` paints the 2D textures.

Suit
- **Seams instead of the ring folds**: thin raised tape a shade darker than the suit, down the sides and over the
  shoulders, round the hips, along the outside of the legs and arms, round the armholes and over the top of the hood.
- **Fabric, creases, wear and dirt** are baked into one picture, `T_Suit_Albedo.png` (2048), over UVs shared by every
  suit, seam and rubber part (material `SL_SuitBaked`, plain URP Lit). The bake is Cycles from a procedural material:
  a faint weave and mottling, soft creases at the elbow and knee end of each limb piece, darker where parts meet
  (ambient occlusion), grime rising from the boots, scuffs where things rub (gloves, knees, boots) and dust on what
  faces up. Change the numbers in `bake_suit` and rebuild. Forking the project's 600-line lit shader for the suit was
  the other way; baking keeps the suit on the stock shader.
- **Gloves**: a gauntlet, the palm, a separate index finger, the other three together and a thumb. **Boots**: an upper,
  a thick sole with tread, a toe cap.
- **Name tape and crew number**: a cream tape on the right chest, the number on the back hump. The text is built in
  Unity by `HazmatAppearance` from markers in the model (`NameTapeAnchor/Right/Up`, `NumberAnchor/Right/Up`, set in
  the surface's own tangent frame) as small meshes from the letter atlas `T_LabelFont.png` (DejaVu Sans Mono, a free
  font that ships with Blender), bent to the body's curve. Material `SL_LabelInk` (alpha clipped).

Face (all the player's choice, on `HazmatAppearance`)
- **Painted eyes and mouth**: three sets in `T_FacePaint_Atlas.png` (Tired, Wide, Calm), each with a blink frame. The
  component picks the cell through a property block on `_BaseMap_ST` and blinks every 2.5-6 s in Play mode (now and
  then twice).
- **Painted shading** on every face (`T_FaceShade.png`): warm cheeks, a shadow under the brows, the nose and the lip.
- **Parts**: Nose Round/Long/Button, Brows Flat/Worried/Thick, Hair Short/Fringe/Receding/Buzz, Facial hair
  None/Moustache/Stubble (painted, `T_FaceStubble.png`)/Beard, Glasses, Mask None/Half/Full, four hair colours
  (`SL_Hair`, `SL_HairBlack`, `SL_HairGinger`, `SL_HairGrey`) and three skin tones, now warmer (the lightest read grey).
- Painted layers draw in this order: shading (queue 2988), stubble (2989), features (2990), then the visor glass (3000).

In the scene's `CharacterLineup6` group (inactive, with `LineupCamera6`): five crew, MORALES, OKAFOR, BECK, TANAKA and
IVERS, each with different choices. Pictures: `suit4_faces.jpg`, `suit4_details.jpg`, `suit4_camcorder.jpg`,
`suit4_three_quarter.jpg`.

### Round seven: one-piece body, smooth gloves and boots (`HazmatSuit5`)

The owner's notes on round six, made by `Tools/Blender/style_lab_characters7.py` (it reuses round six's seams, bake,
text markers and face parts):
- **Hands**: each glove is one smooth piece. The parts (gauntlet, palm, three fingers together, index, thumb) are fused
  with a voxel remesh, smoothed and decimated (`fuse`). Metaballs were tried first and were too fiddly to size.
- **Trousers to boots**: each leg ends in a hem gathered by an elastic band round a smooth boot shaft (the boot fused
  the same way), not a cylinder clipped over the boot.
- **Trousers to shirt, and the tummy**: the body is one smooth piece lofted through cross-sections from the crotch to
  the shoulders (`loft`), tapering into the thighs, which start inside it with a rounded top (higher-resolution so the
  join is a clean curve). At normal distance it reads as one coverall; in extreme close-up the baked shading along
  the join is a little ragged.
- **Stripe** only round the left upper arm.
- **Zip** straight up and down, a little left of centre, with a storm flap beside it and a pull at the top.
- **No valve** on the chest.

In the scene's `CharacterLineup7` group (inactive, with `LineupCamera7`), the same five crew. Pictures:
`suit5_camcorder.jpg`, `suit5_details.jpg`, `suit5_three_quarter.jpg`.

### Round eight: hood into the shoulders, sleeve stripe (`HazmatSuit6`)

The owner's notes on round seven, made by `Tools/Blender/style_lab_characters8.py`:
- **Bigger hands**: the fused gloves 1.3 times round seven's size.
- **Legs to body**: a seam tape (`thigh_seam`) laid exactly along where each thigh enters the body. For each line up
  the thigh's surface, a binary search finds where it goes inside the body.
- **Hood**: the collar ring that read like a scarf is gone. The hood is one smooth loft from inside the shoulders,
  through the neck, to the dome, so it rises out of the body like the reference photo's suit.
- **Stripe**: down the outside of the left sleeve from the shoulder seam to the glove ring (`strip_along`, cut from each
  arm piece), turned 35 degrees toward the front so it shows from the front too. Read as the "bottom just cuts off"
  note: the armband ended abruptly mid-arm.
- **Soles**: rounded corners and edges (a superellipsoid) instead of a box.

In the scene's `CharacterLineup8` group (inactive, with `LineupCamera8`). Pictures: `suit6_camcorder.jpg`,
`suit6_details.jpg`, `suit6_three_quarter.jpg`.

### Round nine: a real zip, shaped limbs, posed (`HazmatSuit7`)

The owner's notes on round eight, made by `Tools/Blender/style_lab_characters9.py`:
- **Zip**: a dark fabric tape from the collar down to the crotch, metal teeth alternating either side (`boxes_mesh`, one
  mesh), a slider with a pull tab hanging from it near the top and a stop at the bottom, beside the storm flap.
- **Limbs shaped, not cylinders** (`limb_loft`, built along each piece's own Z so the bake's creases still work): a
  deltoid cap at each shoulder, the upper arm narrowing to the elbow, the forearm fuller then flaring to the cuff, the
  thigh fuller at the top, a shaped calf. Each limb end is matched to its joint ball, so bent elbows and knees stay
  continuous. The arm and leg seams are cut from the new surfaces.
- **Posed**: in `CharacterLineup9` (inactive, with `LineupCamera9`) MORALES has an arm raised with the elbow bent,
  OKAFOR is mid-step, BECK crouches, TANAKA reaches up and IVERS stands for comparison. The poses were set by rotating
  the joints about world axes: +X swings a limb forward, -X bends a knee back.
- The name text's bend was eased (radius 0.4 m): at 0.15 m the last letter sank under the tape.

The suit is still rigid parts on joints. In a crouch the thigh seam turns with the thigh, away from the body; a skinned
rig would fix that when the suit goes into the game.

Pictures: `suit7_poses.jpg`, `suit7_front.jpg`, `suit7_details.jpg`, `suit7_camcorder.jpg`.

### Round ten: the suit rigged and in the game (`PlayerHazmat`)

`Tools/Blender/style_lab_rig.py` builds round nine's suit and rigs it on a skeleton with the **same bone names and
hierarchy as the game's player body** (MPFB's game-engine skeleton: Root, pelvis, spine_01..03, neck_01, head,
clavicle/upperarm/lowerarm/hand, the fingers, thigh/calf/foot/ball), placed at the suit's own joints. The game's body
code finds bones by name and measures the rig at start, so it drives the suit as it drove the MakeHuman body.
- **Skinning**: every suit part is weighted to the bones nearest it among those that move it (its own and its
  neighbours, inverse distance to the sixth power, four at most), so elbows, knees, hips, shoulders and the neck bend
  as one surface. Heavy parts are trimmed first (boots and gloves 13 000 to 2 000 triangles each): 93 000 to 47 000
  triangles for the whole suit.
- **Split like the player body**: `Suit.Head` (hood, window), `Suit.ArmL`, `Suit.ArmR`, `Suit.Torso`, `Suit` (legs),
  so first person draws the head and trunk into the shadows only. The face choices are separate pieces on the head
  bone (`Nose_Round.Head`, `Mask_Full.Head`, `Paint_Features.Head`...), the name and number markers on `spine_03`.
- **In the game**: `Resources/Player/Hazmat/PlayerHazmatSuit.prefab` (the model with `HazmatAppearance`).
  `FirstPersonBody.useHazmatSuit` (on) loads it in place of the MakeHuman body, its materials moved onto Backrooms/Lit so
  the light grid lights it, and `HazmatAppearance.firstPerson` hides the face and name from inside the hood.
  `RemotePlayer` loads it for co-op players: their name on the tape, their number on the back, a face picked from their
  peer number (until a look is chosen and sent), scaled 1.044 so the eyes and camcorder are at 1.58 m. Turn
  `useHazmatSuit` off to go back to the MakeHuman body.
- Checked in World 3 with `BodyShots` (walking, backing up, sidestepping, crouch-walking, getting up from a crawl) and
  `HandShots` (holding items), with the Body setting shown for the test only (the owner plays with Body: None).
  Known: held items sit partly inside the glove (the grips were tuned for the smaller human hand).
- **Fabric and visor**: a normal map baked from the same weave and creases (`T_Suit_Normal.png`, used at 0.3 strength;
  full strength looked like corduroy), and the visor glass has UVs and `T_VisorFog.png`: breath fog at the bottom, a few
  runs, a glare streak.
- **Faces**: the atlas has four frames per set (open, blink, talk, scared). `HazmatAppearance.talk` (0..1) flaps the
  mouth (for voice chat, which the game does not have yet); `scared` or the building's tension past
  `scaredFromTension` (0.75) shows the frightened face.

**What it costs** (editor, about 1080p, `StyleLabFrameProbe`): four rigged suits on screen about 0.5-0.7 ms of GPU time
together; the lab's stylise pass (bands and outlines) about 0.9 ms of GPU time.

## Unity's audio assert flood (UUM-146734)

On 2026-11-06 the editor log filled with "Access version should be odd when acquiring lock" (4.7 million lines, up to
19,000 a second). The native stack trace (`Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.Full)`)
showed it inside Unity's own audio engine: `audio::DualThreadManager::ControlUpdate` from `AudioManager::Update`. It is
Unity bug UUM-146734 (it loops until the editor runs out of memory). Unity fixed it in 6000.3.22f1, 6000.5.8f1,
6000.6.0b8 and 6000.7.0a4, but in no 6000.4 build; this project is on 6000.4.5f1. Game code does not cause it; it
started here right after leaving Play mode.

- Restarting Unity clears it.
- `Scripts/Editor/AudioAssertWatchdog.cs` watches for it. When it starts, the watchdog restarts the audio engine
  (`AudioSettings.Reset`, which cut it from about 19,000 a second to about 70) and warns once, in the console and on
  the Game and Scene views, to restart Unity.
- The lasting fix is upgrading the editor to 6000.5.8f1 or later.

## Tuning

Select `StyleLabCamera` and move the sliders on **Style Lab Look**; the Game view updates without pressing Play.

| Slider | What it does |
|---|---|
| Pixel Size | 1 = full resolution; 3-4 = the chunky low-resolution look |
| Bands, Band Blend, Dither | brightness cut into steps (Lethal Company's posterising), how much of it shows, and a dither across the steps |
| Outline, Width, Depth / Normal / Contrast sensitivity, Fade | the dark outlines: from jumps in depth, changes in surface direction and in brightness; fading out with distance |
| Saturation, Contrast, Tint, Shadow Tint | the grade |
| Debug View | 1 shows depth, 2 the normals (to check the outline inputs) |

The lights (`Lights/PanelLight`), fog and ambient colour (Lighting window) and the materials' colours are the other
main levers. The lab camera uses the plain renderer (`Portal_Renderer`), so none of the camcorder's own processing
applies here.

## Next

The look is settled (the camcorder look over it, painted textures); taking it across the game is planned and tracked in
`Docs/StyleRollout.md`, by the rules in `Docs/StyleSheet.md`.
