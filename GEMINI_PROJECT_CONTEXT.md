# Project Context & AI System Prompt: Backrooms No Exit

> **How to use this with Gemini:**
> 1. In **Gemini Web** (gemini.google.com), **Gemini Advanced**, or **Google AI Studio**:
>    - Create a new chat.
>    - Paste this entire file into your first message, or upload this file as an attachment / System Instruction in Google AI Studio.
> 2. You can then ask questions, request new features, ask for bug fixes, performance optimizations, or shader/script implementations, and Gemini will have full context of your exact engine version, packages, and architecture!

---

## 1. Project Overview

- **Project Name:** Backrooms No Exit
- **Company Name:** the356company
- **Package Name (Application ID):** `com.the356company.BackroomsNoExit`
- **Engine Version:** **Unity 6 (6000.6.6f1)**
- **Render Pipeline:** **Universal Render Pipeline (URP 17.6.0)**
- **Target Platforms:** **Mobile (Android primary, touch controls)** and **PC (standalone/editor testing)**
- **Genre:** First-person liminal horror / survival game set in the Backrooms (Level 0 aesthetics: yellow wallpaper, fluorescent hum, procedural hallways, wandering entities).

---

## 2. Technical Stack & Key Packages

- **Engine:** Unity 6000.6.6f1
- **Rendering:** Universal Render Pipeline (`com.unity.render-pipelines.universal` 17.6.0)
  - Mobile & PC URP render assets (`Assets/_Project/Settings`)
  - Volume profiles with Neutral Tonemapping, Warm Linear Fog, Post Exposure, Bloom, Film Grain, Vignette
- **Camera:** Cinemachine 3 (`Unity.Cinemachine` 6.6.0)
  - *Note:* In Unity 6, Cinemachine uses `using Unity.Cinemachine;` (not `Cinemachine`) and components like `CinemachineBasicMultiChannelPerlin`.
- **Navigation & AI:** AI Navigation package (`Unity.AI.Navigation` 2.0.16)
  - Uses `NavMeshSurface` runtime baking and `NavMeshAgent`.
- **Input System:** New Input System (`com.unity.inputsystem` 1.20.1)
  - Supports Touchscreen virtual controls, keyboard/mouse, and gamepads.
- **UI Framework:** UI Toolkit (`UnityEngine.UIElements`, `com.unity.ugui` 2.6.0)
  - UXML templates, USS stylesheets, and programmatic UI construction for menus.
- **Model Import:** Needle UnityGLTF (`org.khronos.unitygltf` 2.23.0) for importing `.glb`/`.gltf` character models (e.g., Pirate Clark).

---

## 3. Directory & Architecture Structure

```
Assets/
├── _Project/
│   ├── Art/
│   │   ├── Characters/
│   │   │   ├── Bacteria/             # Classic Backrooms Bacteria wire monster
│   │   │   └── Clark/                # "Pirate clark norpa" glTF entity
│   │   ├── Environment/              # Floor, wall, ceiling textures & materials
│   │   └── UI/                       # UI textures, icons
│   ├── Audio/
│   │   └── Resources/SFX/            # Ambient audio loops, droning hums, stingers
│   ├── Scenes/
│   │   ├── Menu.unity                # Liminal CRT/Camcorder VHS styled main menu
│   │   └── MainLevel.unity           # Procedural level with monster AI & gameplay
│   ├── Scripts/
│   │   ├── GameSettings.cs           # Static settings singleton (PlayerPrefs backed)
│   │   ├── Player/
│   │   │   └── Flashlight.cs         # Player camera spot light with Perlin flicker
│   │   ├── Enemies/
│   │   │   ├── VillainAI.cs          # NavMesh state machine (Wander/Chase/Teleport)
│   │   │   └── VillainSightShake.cs  # Cinemachine camera shake on player visual contact
│   │   ├── Level/
│   │   │   ├── BackroomsRoomGenerator.cs # Procedural 2D grid dungeon & NavMesh generator
│   │   │   ├── BackroomsAtmosphere.cs    # Runtime URP volume, warm fog & post-processing
│   │   │   ├── BackroomsAmbience.cs      # Procedural SFX shuffling & audio playback
│   │   │   └── FlickeringLight.cs        # Unstable fluorescent tube light & hum
│   │   ├── UI/
│   │   │   ├── MainMenuController.cs # UI Toolkit main menu with liminal text effects
│   │   │   ├── PauseMenuController.cs# Mobile-friendly pause menu (resume, sound, exit)
│   │   │   └── MenuCameraHUD.cs      # Camcorder REC HUD overlay
│   │   └── Editor/
│   │       ├── MenuSceneSetup.cs     # Automated scene bootstrapping
│   │       ├── MenuDiagnostics.cs    # Scene verification tool
│   │       ├── MobileAudioImportUtility.cs # Auto-configures audio compression for mobile
│   │       ├── MenuBacteriaAnimationFix.cs
│   │       └── PirateClarkSetup.cs   # Wires glTF model to Mecanim AnimatorController
│   ├── Settings/                     # URP config, Quality levels, Volume Profiles
│   └── UI/                           # UXML / USS templates (RobotoMono font)
└── Starter Assets/                   # Unity FirstPersonController modified for mobile
    ├── Runtime/
    │   ├── FirstPersonController/
    │   ├── InputSystem/
    │   └── Mobile/                   # TouchscreenInput, VirtualInput UI widgets
```

---

## 4. Deep Dive into Core Systems

### 4.1. Procedural Generation (`BackroomsRoomGenerator.cs`)
- Generates a Backrooms layout on a configurable 2D grid (`gridWidth` x `gridHeight`, default 50x50 cells, `tileSize` = 2.5m, `wallHeight` = 3m).
- Randomly carves rectangular rooms (with chances for rare large halls) and connects them via a minimum spanning tree / pathfinding network plus extra loop corridors (`extraConnections`) and maze branches (`deadEndBranches`).
- Generates structural pillars in rooms, ceiling light panels (`lightEvery`), fluorescent point lights (capped via `maxPanelLights` for mobile GPU performance), and scatters environmental props (`decorationPrefabs`, wall posters).
- Dynamically attaches and bakes a `NavMeshSurface` at runtime so enemies can navigate the newly generated maze immediately.
- Automatically places the Player and the Villain at distant spawn points.

### 4.2. Enemy AI & Horror Mechanics (`VillainAI.cs`, `VillainSightShake.cs`)
- **NavMesh State Machine:** Wander & Chase states.
- **Vision Cone & Raycasts:** Checks distance (`viewDistance`), field-of-view angle (`viewAngle`), eye height, and raycasts against obstacles to detect the player. If sight is lost, continues chasing for `loseSightTime` seconds before resuming wandering.
- **Dynamic Animation Sync:** Adjusts agent speed and animator parameters (`Speed`, `IsChasing`, `Attack1`, `Attack2`) matching authored clip speeds.
- **Anti-Lost Repositioning (Smart Teleport):** To keep tension high in a massive procedural maze, if the monster drifts further than `maxDistanceFromPlayer` (e.g. 40m), it attempts to teleport into a ring (`teleportRange` 15–30m) around the player.
  - Crucially uses `avoidVisibleSpots`: ensures the teleport target is outside the player's camera frustum/sight cone so the monster never pops into view.
- **Camera Trauma / Sight Shake:** `VillainSightShake` queries `CinemachineBasicMultiChannelPerlin` on the player's virtual camera. When the monster spots the player, camera shake intensifies proportionally to distance.

### 4.3. Atmosphere, Lighting & Post-Processing (`BackroomsAtmosphere.cs`)
- Backrooms Level 0 aesthetic recreated programmatically without hardcoded scene assets.
- **URP Volume Profiles:**
  - `TonemappingMode.Neutral` (keeps fluorescent yellows from turning reddish/orange).
  - Warm distance fog (`fogColor`, linear 6m to 40m) hiding hallway ends.
  - Warm color filter, post-exposure adjustment tied to user brightness settings, subtle film grain, bloom, and chromatic aberration.
- **Fluorescent Lights (`FlickeringLight.cs`):** Simulates faulty ceiling tubes with random micro-flickering, sudden dips, and synced audio hum.

### 4.4. Audio System (`BackroomsAmbience.cs`)
- Plays ambient droning loops and eerie SFX from `Assets/_Project/Audio/Resources/SFX`.
- Shuffles tracks with random silence gaps (`minGap`, `maxGap`) to maintain psychological horror.
- Listens to volume changes triggered by `GameSettings`.

### 4.5. Player Controls & Mobile Input
- Built on Unity Starter Assets First Person Controller with touch input integration:
  - Touchscreen look zone and virtual on-screen joystick for movement.
  - `Flashlight.cs`: Attached to player camera with Perlin noise intensity flicker, toggleable via mobile UI button or desktop `F` key.
  - Sensitivity and controls adjustable from settings and stored in `PlayerPrefs`.

### 4.6. Settings & UI (`GameSettings.cs`, `MainMenuController.cs`, `PauseMenuController.cs`)
- Built with **UI Toolkit** (`UIDocument`, `.uxml`, `.uss`).
- Centralized `GameSettings` class managing:
  - `MasterVolume`, `MusicVolume`
  - `Sensitivity` (read by `FirstPersonController`)
  - `Brightness` (dynamically modulates URP Post Exposure in EV)
  - `QualityLevel` (URP render quality presets)
- Settings auto-load on boot (`RuntimeInitializeOnLoadMethod`) and re-apply on every scene change (`sceneLoaded`).
- Menus include liminal typewriter text effects, CRT scanline overlay, and mobile top-right pause button toggle.

---

## 5. Coding Guidelines for this Project (For Gemini)

When Gemini writes or reviews code for this project, it must adhere to the following conventions:

1. **Unity Version:** Unity 6 (`6000.6.6f1`).
   - Use `Object.FindFirstObjectByType<T>()` / `Object.FindAnyObjectByType<T>()` instead of obsolete `FindObjectOfType<T>()`.
   - Use `Unity.Cinemachine` namespace for Cinemachine 3 (not the legacy `Cinemachine` namespace).
   - Use `Unity.AI.Navigation` for NavMesh runtime baking (`NavMeshSurface`).
2. **Input System:** Always use the new Input System (`UnityEngine.InputSystem`), e.g., `Keyboard.current`, `Gamepad.current`, or `PlayerInput` / `StarterAssetsInputs`. Never use legacy `Input.GetKey`.
3. **Rendering:** Universal Render Pipeline (URP).
   - Manipulate post-processing by creating/editing runtime `VolumeProfile` components (`profile.TryGet<T>(out var comp)`).
4. **Mobile Performance First:**
   - Keep draw calls and real-time shadows low.
   - Limit real-time point lights; use vertex/unlit or simple lit shaders where possible.
   - Cache raycast hit buffers (`RaycastNonAlloc` or array sorting without heap allocations in hot loops).
5. **Code Style:** Clean C# with serialized fields (`[SerializeField]`), explicit comments, null-checks, and `#if UNITY_EDITOR` guards for editor-only utilities.

---

## 6. How Gemini Should Assist You

Gemini is primed as your senior Unity 6 & mobile game developer specializing in horror gameplay, procedural generation, URP shader optimization, and UI Toolkit. You can ask:
- *"How can I add an inventory system for keys and almond water that integrates with PauseMenuController?"*
- *"Optimize BackroomsRoomGenerator mesh generation using Mesh.CombineMeshes to reduce draw calls on Android."*
- *"Create a sound-detection mechanic for VillainAI so running alerts the monster."*
- *"Add a VHS tape collectible system with custom UI Toolkit popups."*
