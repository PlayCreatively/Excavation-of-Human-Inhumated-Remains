# Excavation System Implementation

## Overview

This is a complete implementation of an SDF-based archaeological excavation system for Unity HDRP, based on the technical specifications in `Documents/Implementation.md`.

## Core Features

### ✅ Implemented

1. **SDF-Based Carving System**
   - 3D texture-based signed distance field
   - Conservative MIP map generation for hierarchical raymarching
   - Compute shader-based brush carving with boolean operations

2. **Analytical Stratigraphy**
   - Material layers defined by signed distance functions
   - Multiple geometry types: DepthBand, NoisyDepthBand, Cut, Ellipsoid
   - Harris Matrix ordering (youngest to oldest)
   - Layer operations: Inside, Union, Subtract

3. **Hierarchical Raymarching**
   - Multi-level raymarching with adaptive resolution
   - Conservative MIP sampling for safe skip distances
   - Depth writing for proper scene integration

4. **Tool System**
   - Configurable dig brushes with radius, speed, falloff
   - Material hardness affects dig speed
   - Audio feedback based on material
   - Controller haptic feedback
   - Visual gizmo for brush preview

5. **Serialization**
   - Save/load carve volume with GZip compression
   - Preserves excavation state between sessions

## Project Structure

```
Assets/
├── Scripts/
│   └── Excavation/
│       ├── Core/
│       │   ├── ExcavationManager.cs       # Manages 3D volume texture and carving
│       │   ├── BrushStroke.cs             # Carving operation data
│       │   ├── SurfaceHit.cs              # Raymarching result
│       │   └── SDFUtility.cs              # SDF math utilities
│       ├── ScriptableObjects/
│       │   ├── ExcavationVolumeSettings.cs # Volume configuration
│       │   ├── MaterialLayer.cs            # Stratigraphic layer definition
│       │   └── DigBrushPreset.cs          # Tool presets
│       ├── Stratigraphy/
│       │   ├── LayerGeometryData.cs       # Base geometry class
│       │   ├── DepthBandGeometry.cs       # Horizontal layers
│       │   ├── NoisyDepthBandGeometry.cs  # Undulating layers
│       │   ├── CutGeometry.cs             # Pits/postholes
│       │   ├── EllipsoidGeometry.cs       # Burial mounds
│       │   └── StratigraphyEvaluator.cs   # Layer evaluation system
│       ├── Rendering/
│       │   └── ExcavationRenderer.cs      # Raymarch rendering
│       └── Tools/
│           └── DigTool.cs                 # Player digging tool
├── Shaders/
│   ├── ExcavationRaymarch.shader          # Main HDRP raymarch shader
│   └── SDFCommon.hlsl                     # Shared SDF functions
└── Resources/
    └── Shaders/
        ├── InitializeVolume.compute       # Volume initialization
        ├── CarveVolume.compute            # Brush carving
        └── GenerateMips.compute           # Conservative MIP generation
```

## Quick Start

### 1. Create Volume Settings

1. Right-click in Project → `Create > Excavation > Volume Settings`
2. Configure:
   - **World Origin**: Bottom-left-front corner of excavation area
   - **World Size**: Dimensions in meters (e.g., 5×3×5m)
   - **Voxel Size**: Resolution (0.05 = 5cm, lower = higher quality)

### 2. Create Material Layers

1. Right-click in Project → `Create > Excavation > Material Layer`
2. Set:
   - **Layer Name**: Archaeological context (e.g., "Topsoil")
   - **Base Colour**: Visual color
   - **Hardness**: 0-10 (affects dig speed)
3. Add geometry:
   - Click "+" on Geometry Data
   - Choose type (DepthBand, NoisyDepthBand, Cut, Ellipsoid)
   - Configure parameters

**Example Layers:**
- **Topsoil**: DepthBand, top=0, bottom=-0.3, brown
- **Subsoil**: DepthBand, top=-0.3, bottom=-0.6, orange-brown
- **Pit Fill**: CutGeometry, center=(2, 0, 2), radius=1, depth=1.5

### 3. Create Dig Brush

1. Right-click in Project → `Create > Excavation > Dig Brush Preset`
2. Configure:
   - **Radius**: Brush size in meters
   - **Dig Speed**: Units per second
   - **Falloff Curve**: Edge softness

### 4. Set Up Scene

1. Create empty GameObject → Add `ExcavationManager`
   - Assign Volume Settings
   - Assign compute shaders from Resources/Shaders/

2. Create empty GameObject → Add `StratigraphyEvaluator`
   - Add Material Layers (ordered youngest to oldest)
   - Set default substrate layer

3. Create empty GameObject → Add `ExcavationRenderer`
   - Assign ExcavationManager
   - Assign StratigraphyEvaluator
   - Create material using `ExcavationRaymarch` shader
   - Assign to Raymarch Material field

4. Add `DigTool` to player/camera
   - Assign ExcavationManager
   - Assign StratigraphyEvaluator
   - Assign Dig Brush Preset
   - Create child object for Tool Tip transform

### 5. Configure Input

The DigTool uses the new Input System:
1. Create an Input Action for digging (e.g., "Dig" button/trigger)
2. Assign to DigTool's "Dig Action" field

Alternatively, it falls back to Mouse Button 0.

## Performance Tuning

### Resolution vs Quality

| Voxel Size | Resolution (5×3×5m) | Memory  | Quality |
|------------|---------------------|---------|---------|
| 0.1m       | 50×30×50 (75K)      | ~300KB  | Low     |
| 0.05m      | 100×60×100 (600K)   | ~2.4MB  | Medium  |
| 0.025m     | 200×120×200 (4.8M)  | ~19MB   | High    |

### Optimization Tips

- **Start coarse**: Use 0.1m voxels for testing, refine later
- **Limit raymarch steps**: 64-128 is usually sufficient
- **MIP levels**: More levels = better performance in large empty spaces
- **Brush optimization**: Only update affected voxel regions

## Known Limitations

### Current Version

- **Texture reading in C#** is not fully implemented (affects tool collision)
  - Workaround: Use analytical SDF for collision (already does this)
- **Layer data to shader** supports up to 8 layers
  - Workaround: Use hierarchical layers or structured buffers
- **No Marching Cubes** mesh generation (deferred feature)

### HDRP Integration

The shader currently uses a simplified lighting model. To integrate with HDRP's full lighting:
- Replace hard-coded light direction with `_DirectionalLightDatas`
- Add support for spot/point lights
- Integrate with HDRP's shadow system

## Extending the System

### Adding New Layer Geometries

1. Create new class inheriting from `LayerGeometryData`
2. Implement `SDF(Vector3 worldPos)` method
3. Mark as `[System.Serializable]`
4. Add corresponding HLSL function to shader (if using GPU evaluation)

Example:
```csharp
[System.Serializable]
public class BoxGeometry : LayerGeometryData
{
    public Vector3 center;
    public Vector3 size;
    
    public override float SDF(Vector3 p)
    {
        Vector3 d = Abs(p - center) - size * 0.5f;
        return Min(Max(d.x, Max(d.y, d.z)), 0f) + 
               Length(Max(d, Vector3.zero));
    }
}
```

### Custom Brush Shapes

Modify `CarveVolume.compute`:
- Change `brushSDF` calculation for different shapes
- Add shape parameters as uniforms
- Update `BrushStroke` struct with shape data

## Troubleshooting

**Volume not rendering:**
- Check ExcavationManager has initialized (look for log message)
- Verify all compute shaders are assigned
- Check ExcavationRenderer has valid material

**No digging happening:**
- Ensure DigTool references are set
- Check input action is configured
- Verify tool tip is near surface
- Look for console warnings

**Poor performance:**
- Reduce voxel count (increase voxel size)
- Lower max raymarch steps
- Disable self-shadows
- Reduce MIP levels

**Artifacts in rendering:**
- Increase surface threshold
- Adjust MIP generation correction factor
- Check for NaN values in SDF functions

## Future Enhancements

See `task.md` for remaining tasks:
- Triplanar texture mapping
- Advanced shadow techniques
- Physics integration (player grounding)
- Custom editor tools
- Performance profiling and optimization

## Credits

Implementation based on:
- Claybook's hierarchical raymarching technique
- SDF boolean operations (Inigo Quilez)
- Archaeological Harris Matrix principles
