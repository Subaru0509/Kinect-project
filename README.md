# Kinect Window Cleaner Simulation

A body-tracking simulation game built with C#, WinForms, and the Azure Kinect DK SDK. Players simulate a high-rise window cleaner using physical arm movements and depth (Z-axis) to wash windows on a skyscraper.

## 🎮 Gameplay Mechanics
- **Z-Axis Depth Sensing**: Players must extend their hands forward into the "effective glass distance" (within 100cm from the camera) to trigger interactions. A red crosshair indicates the hand is hovering; touching the glass is purely based on real-world depth.
- **Dual-Hand Coordination**:
  - **Left Hand (Detergent / Blue Crosshair)**: Softens stubborn dirt (`Dirty`, brown) into a foamy state (`Foamed`, light blue). It cannot wipe the window completely clean.
  - **Right Hand (Wiper / Yellow Crosshair)**: Wipes away the foamed areas completely (`Clean`, transparent), revealing the real camera feed underneath. It has no effect on raw stubborn dirt.
- **Immersive Gondola Drop (No UI)**: Abandoned traditional progress bars. When a window pane is 95% clean, a "gondola drop" animation triggers—the building grid shifts upwards, and a new dirty row appears at the bottom, simulating continuous downward descent.
- **Edge Wall Protection**: The far left and right edges of the screen are rendered as solid concrete walls. This cleverly avoids relying on the Kinect's edge field-of-view, where skeletal tracking tends to jitter or fail.

---

## 🤖 Architecture & AI Context (For Future AI Assistants)

> **Note to future AI Assistants (e.g., GitHub Copilot)**: 
> This project was specifically architected to overcome severe memory leak issues and performance bottlenecks commonly found in rapid Kinect + WinForms prototyping. Please adhere strictly to the following core architectural decisions when continuing development on another machine:

1. **Zero-RAM Leak Grid Rendering (`BuildingGrid.cs`)**:
   - **CRITICAL: DO NOT use per-frame Bitmap manipulation or alpha-channel masking for the dirt.** 
   - Initially, the project suffered from out-of-memory (OOM) crashes due to generating high-res Bitmaps every frame. This was refactored into a logical 2D enum grid (`CellType[,] grid`).
   - Dirt and foam are rendered using simple GDI+ `FillRectangle` calls with semi-transparent brushes directly in the `PictureBox_Paint` event. This keeps memory usage flat.
2. **High-Performance Asynchronous Capture**:
   - **DO NOT use WinForms `Timer` for Kinect frame polling.**
   - Kinect data capture and body tracking run in an isolated `Task.Run` while-loop (`KinectTrackingLoop`), preventing UI thread blocking and ensuring a smooth 30 FPS.
3. **Strict Thread Safety and Object Disposal**:
   - Every `Capture`, `Image`, and `Frame` from the Kinect SDK is strictly wrapped in `using` blocks.
   - The RGB camera Bitmap is safely passed to the UI thread using a `lock (frameLock)` to prevent `InvalidOperationException` during cross-thread rendering.

## 🚀 Setup & Build
1. Install **Azure Kinect Sensor SDK (v1.4+)** and **Azure Kinect Body Tracking SDK (v1.1+)**.
2. Open `Kinect_Project.sln` in Visual Studio 2022.
3. Restore NuGet packages (handles the large `.onnx` models automatically, which are ignored via `.gitignore`).
4. Connect the Azure Kinect device, compile, and run.