# FiveM SimRig FFB Bridge (v1.0.0)

A full-featured **DirectInput & Force Feedback (FFB) bridge** for **FiveM (GTA V)**. Enables Direct Drive wheelbases (tested on Moza SW20 V3), multi-USB pedals, analog handbrakes, and H-pattern / sequential shifters with real-time telemetry-driven Force Feedback and an in-game `F10` tuning menu.

## Features
- **True DirectInput Force Feedback:** Centering resistance, cornering G-force feedback, road bumps, tire slip vibration, and adjustable Softlock end-stops.
- **Multi-Device USB Support:** Combine wheel, throttle, brake, clutch, and analog handbrake across separate USB controllers simultaneously.
- **Transmission Modes:** H-Pattern (1–6 + R + clutch-driven neutral), Sequential (SQ), and Automatic.
- **In-Game `F10` Menu & HUD:** Adjust FFB gain, centering, min-force, road effects, steering angle, and gearbox mode on the fly; live bottom-right HUD shows current gear and pedal inputs.

## Architecture
```text
[DirectInput USB Devices] (Wheel, Pedals, Handbrake, Shifter)
        │  SharpDX (1000 Hz poll + DirectInput FFB)
        ▼
[SimRigBridge.exe] (.NET 8 Windows Forms + Local WebSocket Server ws://127.0.0.1:8765)
        ▲
        │  JSON Input & Telemetry Stream
        ▼
[FiveM NUI (html/index.html)] ◄──► [FiveM Client (client.lua)]
```

## Installation

### 1. FiveM Server (`fivem-ffb-bridge`)
1. Download `fivem-ffb-bridge.zip` from [Releases](../../releases) and extract it to your server's `resources/[standalone]/` folder.
2. Add to your `server.cfg`:
   ```cfg
   ensure fivem-ffb-bridge
   ```

### 2. Windows Client (`SimRigBridge.exe`)
1. Download `SimRigBridge.exe` from [Releases](../../releases) into its own folder.
2. **Recommended ([HidHide](https://github.com/nefarius/HidHide)):** Install HidHide (`https://github.com/nefarius/HidHide`), hide your wheel, pedals, and handbrake from FiveM on the *Devices* tab, and whitelist `SimRigBridge.exe` on the *Applications* tab so GTA V does not steal FFB exclusive access.
3. Run `SimRigBridge.exe`, select your FFB wheelbase (e.g., `SW20 V3`), bind your axes/buttons, and keep **Steer Gamma** at **`100%`**.
4. Launch FiveM and press **`F10`** in-game to open the FFB & gearbox settings menu.
5. **Important (FFB Direction):** Depending on your wheelbase brand (e.g., Moza), you may need to enable **Invert FFB** in the `F10` menu (or Windows app) if the wheel pulls into turns instead of self-centering.

## Looking for Contributors / Help Wanted
Contributions, PRs, and feedback from FiveM and sim-racing developers are very welcome! Current areas for improvement:
- **GTA V Steering Filter Optimization:** Minimizing virtual pad steering smoothing lag (`SetControlNormal`) on 900° Direct Drive wheels without causing low-speed over-rotation.
- **DirectInput FFB Tuning:** Refining `ConstantForce` + `Spring` + `Damper` profiles across different Direct Drive wheelbases (Moza, Simagic, Fanatec, Logitech).
- **Per-Vehicle FFB Profiles:** Saving separate FFB gain and steering angle presets per vehicle class or model.
