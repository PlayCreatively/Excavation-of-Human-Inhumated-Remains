# Quick Setup Guide - Excavation System

This guide will get you up and running with a basic excavation scene in under 10 minutes.

## Prerequisites

- Unity 2021.3+ with HDRP
- New Input System package installed

## Step 1: Let Unity Compile (2 minutes)

After implementation, Unity needs to compile all the new scripts. You should see:
- ~14 C# scripts compiling
- 3 compute shaders
- 1 HLSL shader + 1 include file

**Wait for compilation to finish** before proceeding. Check the bottom-right of Unity for the spinning icon to disappear.

## Step 2: Create Volume Settings (1 minute)

1. In Project window, right-click → `Create > Excavation > Volume Settings`
2. Name it: `TestVolumeSettings`
3. Configure:
   ```
   World Origin: (0, 0, 0)
   World Size: (5, 3, 5)     // 5m x 3m x 5m
   Voxel Size: 0.1           // 10cm resolution (coarse for testing)
   Texture Format: R16_SFloat
   Max Raymarch Steps: 128
   ```

## Step 3: Create Material Layers (2 minutes)

### Topsoil Layer
1. Right-click → `Create > Excavation > Material Layer`
2. Name: `Topsoil`
3. Configure:
   - Layer Name: "Topsoil"
   - Base Colour: Brown (RGB: 0.4, 0.3, 0.2)
   - Hardness: 4
4. Click "Add Depth Band" button in geometry section
5. Set:
   - Operation: Inside
   - Top Y: 0
   - Bottom Y: -0.3

### Subsoil Layer
1. Create another Material Layer
2. Name: `Subsoil`
3. Configure:
   - Layer Name: "Subsoil"
   - Base Colour: Orange-brown (RGB: 0.6, 0.4, 0.2)
   - Hardness: 7
4. Add Depth Band:
   - Top Y: -0.3
   - Bottom Y: -1.5

## Step 4: Create Dig Brush (1 minute)

1. Right-click → `Create > Excavation > Dig Brush Preset`
2. Name: `Trowel`
3. Configure:
   ```
   Radius: 0.05              // 5cm brush
   Dig Speed: 1.0
   Falloff Curve: (default linear is fine)
   ```

## Step 5: Set Up Scene (4 minutes)

### Create Excavation Manager
1. Create empty GameObject: "ExcavationManager"
2. Add Component → `Excavation Manager`
3. Assign:
   - Settings: `TestVolumeSettings`
   - Carve Shader: Drag `Resources/Shaders/CarveVolume`
   - MIP Gen Shader: Drag `Resources/Shaders/GenerateMips`
   
   💡 **Or use the "Auto-Load Shaders" button in the Inspector!**

### Create Stratigraphy Evaluator
1. Create empty GameObject: "StratigraphyEvaluator"
2. Add Component → `Stratigraphy Evaluator`
3. Set:
   - Layers (Size: 2)
     - Element 0: `Topsoil`
     - Element 1: `Subsoil`
   - Default Substrate: `Subsoil` (or create a "Bedrock" layer)
   - Base Terrain Y: 0

### Create Excavation Renderer
1. Create empty GameObject: "ExcavationRenderer"
2. Add Component → `Excavation Renderer`
3. Assign:
   - Excavation Manager: (drag the manager GameObject)
   - Stratigraphy: (drag the evaluator GameObject)
4. **Create Material:**
   - Right-click in Project → `Create > Material`
   - Name: `ExcavationMaterial`
   - Shader: Select `Excavation/ExcavationRaymarch`
5. Assign material to "Raymarch Material" field

### Add Dig Tool to Camera
1. Select your Main Camera (or player controller)
2. Add Component → `Dig Tool`
3. Assign:
   - Excavation Manager: (drag)
   - Stratigraphy: (drag)
   - Current Brush: `Trowel`
4. **Create Tool Tip:**
   - Create child GameObject under Camera: "ToolTip"
   - Position it slightly in front of camera (e.g., Z = 1)
5. Drag ToolTip into the "Tool Tip" field
6. (Optional) Add AudioSource component for dig sounds

## Step 6: Test! (30 seconds)

1. **Enter Play Mode**
2. **Look at the excavation volume** - you should see a brown cube rendered
3. **Hold Left Mouse Button** (or configure Input Action) to dig
4. **Move the camera** while digging to carve into the terrain

### Expected Results:
- ✅ Brown terrain cube appears
- ✅ Clicking creates holes in the terrain
- ✅ Gizmo shows brush sphere at tool tip
- ✅ Console shows no errors

## Troubleshooting

### Nothing Renders
- Check ExcavationManager inspector - look for memory stats
- Verify shaders are assigned (auto-load button helps)
- Check Console for shader compilation errors

### Can't Dig
- Ensure Tool Tip is positioned in front of camera
- Check that all references on DigTool are set
- Try moving closer to the terrain surface
- Check Console for "Tool tip not assigned" warning

### Weird Artifacts
- Increase Voxel Size to 0.2 for faster testing
- Lower Max Raymarch Steps if performance is poor
- Check that layers have valid geometry

### Compilation Errors
- Ensure Unity has Input System package
- Check that HDRP is properly configured
- Verify assembly definitions reference correct packages

## Next Steps

Once basic excavation works:

1. **Tune Performance:**
   - Reduce Voxel Size to 0.05m for better quality
   - Adjust Max Steps based on your GPU

2. **Add More Layers:**
   - Create a pit feature using Cut Geometry
   - Add a noisy layer for natural variation

3. **Improve Visuals:**
   - Add textures to material layers
   - Enable self-shadows in material
   - Adjust lighting

4. **Set Up Input:**
   - Create proper Input Action for digging
   - Configure controller haptics strength

5. **Add Content:**
   - Create multiple brush presets
   - Build a layer library
   - Make different sized excavation volumes

## Common Settings

### High Quality (Desktop)
```
Voxel Size: 0.03m
Max Steps: 256
Resolution: ~167×100×167 = 2.8M voxels (~11 MB)
```

### Medium Quality (Balanced)
```
Voxel Size: 0.05m
Max Steps: 128
Resolution: 100×60×100 = 600K voxels (~2.4 MB)
```

### Low Quality (Testing/VR)
```
Voxel Size: 0.1m
Max Steps: 64
Resolution: 50×30×50 = 75K voxels (~300 KB)
```

## Tips

- **Use Gizmos:** Enable gizmos to visualize volume bounds and layer geometry
- **Test Small:** Start with 2×2×2m volumes and large voxels
- **Layer Order Matters:** Youngest layers at top of list
- **Save Often:** Use the save/load buttons in ExcavationManager during play mode

---

**You're ready to dig! 🏺⛏️**

For detailed documentation, see `README.md` in the Scripts/Excavation folder.
