[FREE / Open-Source] SimRig Bridge — Universal DirectInput FFB, Multi-USB Pedals, Button Boxes & Haptics (No ScriptHook!)
SimRig Bridge is a 100% free, open-source Force Feedback and multi-device sim racing bridge for FiveM. It works on any game build using only official FiveM natives—sv_scriptHookAllowed 0 stays enabled, and no game memory is touched.

Features:
• Universal FFB: Works with Logitech, Thrustmaster, Moza, Fanatec, Simucube, and Sim-plicity Direct Drive wheels via safe DirectInput Constant Force.
• Multi-USB Rig Support: Combine separate USB pedals (load-cell brake, clutch), analog handbrakes, shifters, and Arduino / Leo Bodnar button boxes directly into FiveM controls and commands.
• Haptics UDP (Port 20777): Built-in Forza Data Out telemetry stream for SimHub (pedal rumble motors & haptic seat pads).
• Zero Latency: Communicates locally via NUI (127.0.0.1:8989, 0 ms delay). Keyboard/gamepad players don't need to install anything.
Quick Setup:
• Server: Drop fivem-ffb-bridge into resources and add ensure fivem-ffb-bridge to server.cfg.
• Player: Run SimRigBridge.exe, select your wheel, click Connect FFB, and drive. (Check "Invert Force" for Logitech wheels.)

## Direct Drive FFB & HidHide (Important)
When FiveM starts, it takes control of all visible DirectInput game controllers and sends zero-force signals to them, which cancels out external Force Feedback.

**Solution (One-time setup):**
1. Download and install [HidHide](https://github.com/nefarius/hidhide) and reboot Windows.
2. Open **HidHide Configuration Client** -> go to the **Applications** tab -> add **`SimRigBridge.exe`** to the allowed list (`+`).
3. Go to the **Devices** tab -> check the box next to your steering wheel (e.g., **SW20 V3**) and enable **Enable device hiding**.
4. FiveM will no longer see the wheel directly or override its FFB, while `SimRigBridge.exe` continues to work normally.


