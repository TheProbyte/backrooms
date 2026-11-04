# Building the game

- **Make a build:** File > Build Profiles > Windows, or `BuildPipeline.BuildPlayer` to `Builds/StillRecording/StillRecording.exe`
  (the `Builds` folder is ignored by git). The first build after shader changes takes 10-30 min; later ones under a minute.
  Zip `Builds/StillRecording` without the `*_BurstDebugInformation_DoNotShip` folder to send it (about 110 MB).
- **What happens during a build** (Editor scripts, nothing is left changed afterwards):
  - `HdrGradingForBuild`: the URP assets are set to HDR colour grading for the length of the build. The camcorder look
    switches URP to HDR grading while it plays, and URP only builds the post-processing shaders its assets use: without
    this, a build's camcorder picture blew out (crushed shadows, yellow-orange highlights) while the editor looked right.
  - `ShipEditorSettings`: the settings tuned in the editor (world generation, sounds, audio, graphics, light look,
    camcorder) are copied into `Resources/ShippedSettings.json` for the build and removed after. A build takes each one
    the player has not saved yet (`ShippedSettings`), so it starts as the editor plays (same worlds from the same seeds).
  - `BackroomsLitVariantStripper`: drops Backrooms/Lit variants that never occur.
- **The build's own settings** are separate from the editor's (registry `HKCU\Software\DefaultCompany\My project`), but
  saves are shared on the same PC (`%USERPROFILE%\AppData\LocalLow\DefaultCompany\My project`).
- **Checking a build without playing it:** run it with `-probe [folder]` (`Debug/BuildProbe.cs`). It opens World 3 (slot 2,
  seed 553893173), takes a screenshot with each camcorder preset and once with the 3D view at full size, logs the exposure
  and white balance and whether the shipped settings arrived, then erases World 3 and quits. Add the component in Play Mode
  to compare with the editor. `-benchmark` measures frame times (Docs/Performance.md).
