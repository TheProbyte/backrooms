# Brief for ChatGPT (or any other assistant) while Claude is out of usage

Paste the whole "Message to send" section below as the first message. Attach nothing else: the repository has the rest.
Give ChatGPT access to the repo `theprobyte/backrooms`, branch `worldgen-v2` (or paste the files it asks for).

## What to ask it to work on (safe, self-contained, easy to check)

Good jobs, in order of value:

1. **More props for rooms.** Racks and wall clocks in build mode (they need wall placement), more prop kinds.
   Files: `Scripts/Runtime/CustomRooms/BuilderApp.Tools.cs` (`DefaultSize`, `PlaceProp`), `BuilderApp.UI.cs` (prop palette),
   `Scripts/Runtime/CustomRooms/CustomRoomStamp.cs` (`Decorate`), `Scripts/Runtime/Generation/OfficeChunkBuilder.cs` (`BuildProps`).
2. **Show custom rooms in F5 > PLACES** ("MY ROOMS": jump to the nearest placed custom room, like the atrium button).
   Files: `Scripts/Runtime/Camcorder/CamcorderVisualMenu.Locations.cs`.
3. **Wall and floor material choices per area** in build mode (needs a new field on `RoomSettings`, a new field on `Room`,
   and the chunk builder choosing the material). Keep the old two finishes as defaults.
4. **Nicer editor UI**: tool icons, tooltips, a help overlay, copy/paste of areas. Files: `BuilderApp.UI.cs`, `ImUi.cs`.
5. **Write more edit-mode tests** for build mode helpers (`RoomVariant.Resize`, `CustomRoomStamp` turns).
   File: `Scripts/Editor/Tests/CustomRoomTests.cs`.

Do **not** ask it to touch: the camcorder shader (`CamcorderVideo.shader`), the light grid (`LightGrid*`, `Shaders/`), the audio system,
or the world generator's core (`DistrictGenerator`, `WorldGenerator`) without telling you first. Those are tuned and fragile.

## Message to send

> You are helping on a Unity 6 (URP) first-person "Backrooms" game. Repository: theprobyte/backrooms, branch worldgen-v2.
> Read these first, in this order: `Docs/WORKLOG.md`, `Docs/BuildMode.md`, `Docs/ReferenceMatch.md`, `Docs/WorldGeneration.md`.
>
> Rules:
> 1. **Keep a log.** Before you finish any piece of work, add an entry at the top of `Docs/WORKLOG.md` in the format given
>    there: date, "ChatGPT", what changed, why, how you checked it, open problems. Another assistant (Claude Code) reads
>    that file next, and only that file tells it what you did. Commit the log entry together with the change.
> 2. Work on a new branch off `worldgen-v2` named `chatgpt/<short-topic>`. Never push to `main` or `worldgen-v2` directly.
>    Make small commits with clear messages. Do not open a pull request unless I ask.
> 3. Change only what the task needs. Do not reformat files, rename things, or "clean up" other code. Do not edit
>    `ProjectSettings/`, `*.meta` files by hand, or any `Materials/M_*.mat` file.
> 4. Unity compiles all of `Assets/Backrooms/Scripts` together (no assembly definitions). Runtime code must not use `UnityEditor`.
>    Code that the world generator calls runs on worker threads: no Unity API calls there (no `Application`, `GameObject`, `Random`, `Debug` is fine).
> 5. If you cannot run Unity, say so plainly in the log and in your reply. Do not claim something works because it compiles
>    in your head. List exactly what I should test by hand.
> 6. If a task is unclear or touches something risky, ask me before changing it.
>
> Task: <paste ONE job from the list above>

## When Claude is back

Tell Claude: "ChatGPT worked on branch `<name>`; read `Docs/WORKLOG.md` and review its changes." It will read the log, diff the
branch, run the tests (`Backrooms.Tests`, edit mode) and play the scenes to check.
