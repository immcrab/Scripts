# Scripts — in-game teleprompter for Gorilla Tag

A floating teleprompter that only you can see inside your headset. Paste a script into a text file, open the menu with **B**, press **Play**, and read while you record.

Everything runs locally. The mod has no networking and no RPCs, and it changes nothing for other players. It adds no colliders, so it works the same in normal and modded lobbies.

## Install

1. Install BepInEx 5 for Gorilla Tag (5.4.23.x was used for testing). Utilla is **not** required.
2. Copy `Scripts.dll` to `Gorilla Tag/BepInEx/plugins/Scripts/Scripts.dll`.
3. Start the game once. The mod creates `Gorilla Tag/BepInEx/config/Scripts/` and puts `Example Script.txt` in it.
4. **Where your scripts go:** open the menu (B), go to **CHOOSE SCRIPT**, and press **OPEN SCRIPTS FOLDER**. The folder opens on your PC desktop. Put one `.txt` file in it per script, for example `Intro.txt` or `Tutorial video.txt`. You can keep as many as you like. UTF-8 is recommended, and blank lines are kept.
5. Back in the game, press **REFRESH**, then tap a script in the list to choose it.

To build from source:

```bash
dotnet build -c Release -p:Deploy=true
```

This build references the game's DLLs from `D:\SteamLibrary\steamapps\common\Gorilla Tag`. To use a different install, add `-p:GamePath="X:\path\to\Gorilla Tag"`. With `Deploy=true`, the build copies the DLL into `BepInEx\plugins\Scripts`.

## Controls

| Input | Action |
|---|---|
| **B** (right controller) | Open or close the settings menu. Closing the menu does not stop playback. |
| **F6** (keyboard) | Same as B, for testing on desktop |
| Index finger on a menu button | Press it (the controller vibrates on each press) |

The teleprompter panel is **hidden every time the game starts**. Pressing **PLAY** or **SHOW** brings it up. The menu opens 0.6 m in front of you; change this with `[Input] MenuDistance` in the config (0.3–1.2 m).

The menu has two tabs.

**CONTROLS**
- **PLAY / PAUSE**: Play starts or resumes scrolling, and shows the panel. Pause keeps the current position. Pressing Play after the end starts again from the top.
- **RESTART**: Goes back to the top and waits for Play.
- **SHOW / HIDE**: Fades the panel in or out. Playback keeps running while the panel is hidden.
- **RELOAD**: Reads the current file from disk again and keeps your place in it.
- **Speed** (lines per second), **Text Size**, **Distance**, **Opacity**: Use − and +.
- **Follow: HEAD / BODY**
  - Head: the panel follows your view smoothly. Pitch is limited to ±45°.
  - Body: the panel stays in front of your body. It turns to face you again only when you look more than about 40° away, so you can glance around while you talk.
- **Rec sync ON/OFF**: See "Recording sync" below.
- **On camera HIDDEN / VISIBLE**: HIDDEN is the default. The panels show only in your headset. VISIBLE lets the desktop view, the in-game LCK camera and other cameras show them too (the button turns orange as a reminder). This is saved as `[Recording] ShowOnCameras` in the config.
- The bottom line shows which script is loaded.

**CHOOSE SCRIPT**
- Shows the folder path (`Gorilla Tag/BepInEx/config/Scripts`).
- **OPEN SCRIPTS FOLDER**: Opens that folder in Windows Explorer on your PC desktop.
- **REFRESH**: Scans the folder again. The list also refreshes each time you open the menu.
- Script list: tap a name to load it. The current script is highlighted green and marked with `>`. Five scripts fit per page; use **< >** to change page.

The status pill at the top shows PLAYING / PAUSED / END / READY with a percentage, and adds "HIDDEN" while the panel is hidden. The panel itself has a progress bar, a status line, the time remaining, and green reading-line cues.

When the script reaches the end it stops and shows **END**. It never loops on its own.

All settings save to `BepInEx/config/com.scripts.gorillatag.teleprompter.cfg` as soon as you change them. That file also has extra options: follow smoothing, vertical offset, panel width and height, reading-line position, menu distance, the menu button (B/A/Y/X) and the keyboard key.

**Button conflict:** your **Rigdoll** mod also uses B on the right controller. If you have both installed, one press toggles both. To avoid that, change `[Input] MenuButton` in the config (for example to `RightPrimary` for A, or `LeftSecondary` for Y).

## Which cameras can see the panel

This table describes the default setting (**On camera: HIDDEN**). When you switch to **VISIBLE**, the mod stops hiding the panels and moves them to the Default layer, so every camera draws them.

Gorilla Tag uses the Universal Render Pipeline (URP). Before each camera draws, the mod turns its own renderers off for every camera except the headset eye camera, `GTFirstPersonCamera.camera`. This is the same per-camera method the game's built-in LIV camera uses (`LckHideObjectFromCamera`). The panel is also placed on the game's `LCKHide` layer whenever the headset camera draws that layer.

| Camera / output | Panel visible? |
|---|---|
| Your headset view | **Yes** |
| Desktop window (third-person "shoulder" camera), including OBS capturing that window | No |
| Built-in LIV/LCK tablet camera: recording, streaming, selfie, photos | No |
| In-game mirrors and other render-texture cameras | No |
| Cameras added by other mods (normal Unity cameras) | No |
| **SteamVR "Display VR View", Oculus/Meta mirror, Quest casting, Quest's own recording, OBS capturing a headset mirror** | **Yes. This cannot be avoided.** |

These last outputs copy the finished headset image. Anything you see in the headset appears in them too. To record without the panel, record from the desktop window, the in-game LCK camera, or a mod camera.

## Recording sync

The game includes the LIV LCK camera, and it raises events when recording starts, pauses, resumes and stops (`LckService.OnRecordingStarted` and the others). When **Rec sync** is ON:

- When recording starts, the script restarts from the top and plays.
- When recording pauses or stops, the script pauses.
- When recording resumes, the script plays again.

The mod only listens to these events. It never starts or stops a recording itself. No reliable recording event was found for other recorders (OBS, SteamVR, Quest), so for those use the manual Play button.

## Needs testing in VR

The mod compiles against the installed game assemblies, but it has **not been run in a headset yet**. Please check:

1. The log shows `Scripts UI ready.` and `Camera hider installed. Headset camera: <name>`. The camera name should not be "not found yet".
2. The panel appears in the headset and does **not** appear in the desktop window or in the LCK camera preview or recording.
3. Which font was chosen (logged as `UI font:`). Check that the text is sharp and readable at 1.2 m. If the chosen font looks stylized, report its name so the preference list can be adjusted.
4. Text fades at the top and bottom edges, with no full-brightness flash when you show the panel or reload.
5. Button sizes and the press feel on Quest (Link/Air Link) and on Index. Check that sweeping a hand across the menu does not trigger buttons.
6. B opens and closes the menu on Quest controllers and Index controllers. Vive wands have no B button, so use the keyboard key or set another button.
7. The panel draws on top of the world (by design, so walls never hide your script). Check this is comfortable. The menu does **not** draw on top of the world, so your fingers show in front of it.
8. Rec sync with the in-game LCK camera: start, pause, resume and stop a recording.
9. Very long scripts (10,000+ characters) scroll smoothly. Text is split into chunks of about 2,500 characters.
10. Body mode feels right. Gorilla Tag's body collider turns with your head, so Body mode uses "lazy" re-centering rather than true torso tracking.
