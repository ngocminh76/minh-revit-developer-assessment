# Revit Developer Assessment

A Revit add-in built with **C# (.NET)** demonstrating structural beam adjustment automation and extensible ribbon UI architecture.

## Overview

This project implements a Revit external application that automatically adjusts structural beam endpoints based on their intersection context with columns, walls, and other beams. The add-in identifies different geometric scenarios and applies appropriate clearance gaps and void cuts.

## Features

### Adjust Beams
Automatically adjusts beam endpoint positions based on intersection targets:

| Case | Scenario | Behavior |
|------|----------|----------|
| 1 | Beam → Wall | Shorten beam to maintain clearance from wall face |
| 2 | Beam → Column | Shorten beam to maintain clearance from column face |
| 3 | Two collinear beams → Column | Shorten both beams equally, apply void cut at angled faces |
| 4 | Beam → Perpendicular beam | Apply slab-shaped void cut at intersection zone |
| 4b | Two collinear beams → Perpendicular beam | Shorten both beams, apply void cuts at junction |

### Configurable Parameters
- Wall clearance gap (mm)
- Column clearance gap (mm)  
- Inline beam half-gap at columns (mm)
- Perpendicular beam gap (mm)

## Architecture

```
RevitAssessment.sln
├── MyRevitAddin/                    # Main Revit add-in
│   ├── App.cs                       # Entry point (IExternalApplication)
│   ├── Infrastructure/
│   │   └── Ribbon/
│   │       ├── RibbonSetup.cs       # Declarative ribbon configuration
│   │       └── IconHelper.cs        # Runtime icon generation
│   ├── Features/
│   │   ├── Structural/
│   │   │   └── AdjustBeam/
│   │   │       ├── Commands/        # External commands
│   │   │       ├── Logic/           # Core algorithms
│   │   │       ├── Models/          # Data models
│   │   │       ├── ViewModels/      # MVVM view models
│   │   │       └── Views/           # WPF windows
│   │   └── Annotations/
│   │       └── BearingPlate/        # Assembly drawing generation
│   └── Core/                        # Shared utilities
│       ├── ElementProximityUtils.cs  # Spatial element queries
│       ├── SolidFaceUtils.cs        # Geometry face analysis
│       └── MathUtils.cs             # Vector math helpers
├── WPFUI/                          # Shared WPF components
└── Shared_Core/                    # Cross-platform shared code
```

### Design Patterns
- **MVVM** for WPF configuration window
- **Strategy pattern** in `TargetType` enum for endpoint computation per scenario
- **Declarative ribbon config** — adding a new command requires only a `ButtonInfo` entry

## Technical Highlights

### Geometric Analysis
- **Face-based gap computation**: Uses `PlanarFace` normals and origins to compute exact clearance distances, not bounding boxes
- **Web face detection**: Identifies beam web faces by filtering solid geometry faces by area and normal direction
- **Inline beam detection**: Determines collinear beam connections by comparing direction vectors and endpoint proximity
- **Perpendicular beam detection**: Identifies crossing beams using dot product angle thresholds

### Void Cut System
- **Half-space void** (`BeamEndCutVoid.rfa`): Dynamically creates a Generic Model void family for angled cuts at column junctions
- **Slab void** (`BeamPerpCutVoidSlab`): Creates a bounded slab-shaped void for cutting only the intersection zone of perpendicular beams
- Uses `InstanceVoidCutUtils.AddInstanceVoidCut()` for Revit solid-cut operations

### Multi-version Support
- Targets both **Revit 2024** (.NET Framework 4.8) and **Revit 2026** (.NET 8.0)
- Conditional compilation with `REVIT2024` / `REVIT2026` defines

## Build & Run

### Prerequisites
- Visual Studio 2022 or .NET SDK 8.0+
- Revit 2024 or 2026 installed

### Build
```bash
dotnet build RevitAssessment.sln -c Release
```

### Install
1. Copy the `.addin` manifest to:
   ```
   %APPDATA%\Autodesk\Revit\Addins\{version}\MyRevitAddin.addin
   ```
2. Update the `<Assembly>` path in the manifest to point to the built DLL
3. Launch Revit — the **Dev Assessment** tab will appear in the ribbon

### Usage
1. Open a Revit model with structural framing
2. Navigate to the **Dev Assessment** tab → **Structural** panel
3. Click **Adjust Beams**
4. Configure clearance gaps in the settings window
5. Select structural elements (beams, columns, walls)
6. Click OK to execute adjustments

## Technology Stack

- **Language**: C# 12
- **Frameworks**: .NET 8.0 / .NET Framework 4.8
- **UI**: WPF (MVVM pattern)
- **API**: Revit API 2024 / 2026
- **Build**: MSBuild / dotnet CLI
