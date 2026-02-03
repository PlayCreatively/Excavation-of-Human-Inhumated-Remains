# Implementation

## Digging (SDFs)

### Overview

The excavation system uses a **Signed Distance Field (SDF)** to represent the terrain surface. Player carving is stored in a **3D texture** (baked volume) where each voxel holds a **density value** representing how solid that point is.

### Key Concept: Density & Hardware Filtering

Each voxel stores a value from 0 to 1:
- **0** = Fully excavated (air)
- **1** = Fully solid (untouched ground)
- **0.01–0.99** = Partial values at carved edges

**Why this works:** When sampling the 3D texture using normalized UV coordinates, the GPU's **trilinear filtering** automatically interpolates between neighbouring voxels. We define an **isosurface threshold** (typically 0.5)—the surface appears wherever the interpolated density crosses this threshold.

```
Voxel A: 1.0 (solid)    Voxel B: 0.2 (mostly carved)
         |——————————————————|
              ↑
         Isosurface at 0.5 appears here
         (closer to A, because B's value is lower)
```

This means a coarse voxel grid can produce **smooth, curved surfaces** with no extra code—exploiting what GPUs already do with texture filtering, but interpreting the result as geometry.

### Core Composition Rule

```
Scene(p) = max(Base(p), -CarveMask(p))
```
- `Base(p)` — the original, unexcavated ground SDF (e.g., `-p.y` for flat ground at y=0)
- `CarveMask(p)` — the excavation SDF sampled from the 3D texture (0 = untouched, positive = carved)
- **Positive** = outside surface (air), **Negative** = inside solid

---

### 3D Carve Mask

#### Configuration

```csharp
[System.Serializable]
public class CarveVolumeSettings
{
    public Vector3 worldOrigin = Vector3.zero;      // Corner of the excavation bounds
    public Vector3 worldSize = new Vector3(10, 5, 10); // 10m × 5m × 10m
    public float voxelSize = 0.05f;                 // 5 cm resolution (adjustable)
}
```

Derived values:
- `resolution = ceil(worldSize / voxelSize)` → e.g., 200 × 100 × 200 at 5 cm
- Memory: 1 byte per voxel → ~4 MB at this size

#### Texture Format

| Option | Precision | Notes |
|--------|-----------|-------|
| `R8_UNorm` | 256 levels | Lightweight; values 0–255 map to density 0.0–1.0 |
| `R16_UNorm` | 65k levels | Higher precision if needed |
| `R32_SFloat` | Full float | Overkill for most cases |

Start with **R8** (1 byte); upgrade if banding artifacts appear.

**Important:** Use `FilterMode.Trilinear` on the texture to enable hardware interpolation.

#### Coordinate Mapping

```csharp
// World → texture UV (0–1)
Vector3 WorldToUV(Vector3 worldPos, CarveVolumeSettings s)
{
    return (worldPos - s.worldOrigin) / s.worldSize;
}

// UV → voxel index (for direct writes)
Vector3Int UVToVoxel(Vector3 uv, Vector3Int resolution)
{
    return Vector3Int.FloorToInt(uv * resolution);
}
```

---

### Carving (Sphere Brush)

When the player digs at position `center` with tool radius `r`:

1. Compute the axis-aligned bounding box of the sphere in voxel space.
2. For each voxel in that box:
   - Compute world position of voxel centre.
   - Evaluate sphere SDF: `d = length(voxelPos - center) - r`
   - If `d < 0` (inside sphere), reduce the density:
     ```csharp
     // Convert penetration depth to a density reduction
     float penetration = -d;  // Positive value indicating how far inside
     float reduction = saturate(penetration / maxBrushPenetration);
     
     // Reduce density (0 = fully carved, 1 = solid)
     carveMask[voxel] = min(carveMask[voxel], 1.0 - reduction);
     ```

The deeper inside the brush, the lower the density becomes. Voxels at the brush edge get partial values, which the GPU interpolates into smooth surfaces.

#### Caveat: Carving in Air

The brush writes to **all** voxels it touches, including those above the base terrain (i.e., in air). This data is harmless in most cases — the scene evaluation uses `max(baseSDF, -carveMaskSDF)`, so carving air has no visible effect.

However, be aware of these edge cases:

- **Union layers added later:** If you carve in air, then later add a burial mound (Union operation) that occupies that space, the mound will have an unexpected hole from the earlier carve.
- **Wasted writes:** Large brushes mostly in air do unnecessary work. Negligible for small brushes.
- **Save file size:** Carved regions (non-255 values) compress less efficiently.

If these become problems, add a check before writing:

```csharp
if (EvaluateBase(voxelPos) < 0)  // Only carve inside solid ground
{
    carveMask[voxel] = min(carveMask[voxel], 1.0 - reduction);
}
```

For now, the simpler approach (carve everything, ignore the harmless data) is recommended.

#### GPU vs CPU

| Approach | When to use |
|----------|-------------|
| **Compute shader** | Large brushes, high frequency digging — update many voxels per frame efficiently |
| **CPU + `Texture3D.SetPixels`** | Simpler; fine for small brushes or low-frequency edits |

Recommended: implement CPU first, profile, then port to compute if needed.

---

### Evaluation at Runtime

To query the final surface at any point `p`:

```csharp
float EvaluateScene(Vector3 p, CarveVolumeSettings settings, Texture3D carveMask)
{
    float baseSDF = EvaluateBase(p);               // e.g., -p.y for flat ground at y=0
    
    Vector3 uv = WorldToUV(p, settings);
    if (OutOfBounds(uv)) return baseSDF;           // Outside excavation zone
    
    // Sample the carve mask (GPU interpolates automatically via trilinear filtering)
    // 1.0 = untouched solid, 0.0 = fully excavated
    float maskDensity = carveMask.SampleLevel(uv, 0);
    
    // Convert to an SDF: 0 when untouched, positive when carved
    float carveMaskSDF = (1.0 - maskDensity) * maxExcavationDepth;
    
    return Mathf.Max(baseSDF, -carveMaskSDF);
}
```

---

### Rendering

Render the SDF via **sphere-tracing (ray marching)** inside a bounding box proxy mesh.

1. Vertex shader outputs world-space ray origin & direction.
2. Fragment shader marches `Scene(p)` until `|d| < ε` or max steps exceeded.
3. Compute normal via central differences: `n = normalize(∇Scene(p))`.
4. Shade using material colour/texture (see Stratigraphy section).
5. **Write depth** to the depth buffer so other geometry occludes correctly.

Pipeline-agnostic notes:
- URP: custom render feature + shader with `ZWrite On`.
- Built-in: `Camera.AddCommandBuffer` or replacement shader.
- HDRP: custom pass.

#### Triplanar Texturing

Since the terrain is generated procedurally via SDF, there are no traditional UV coordinates. Instead, use **triplanar mapping** to project textures from world space:

```hlsl
float3 TriplanarSample(Texture2D tex, SamplerState samp, float3 worldPos, float3 normal, float sharpness)
{
    // Sample texture from each axis
    float3 xProj = tex.Sample(samp, worldPos.yz).rgb;
    float3 yProj = tex.Sample(samp, worldPos.xz).rgb;
    float3 zProj = tex.Sample(samp, worldPos.xy).rgb;
    
    // Blend weights based on normal direction
    float3 blend = pow(abs(normal), sharpness);
    blend /= (blend.x + blend.y + blend.z);  // Normalize
    
    return xProj * blend.x + yProj * blend.y + zProj * blend.z;
}
```

- `worldPos` — the hit point from raymarching
- `normal` — computed from SDF gradient
- `sharpness` — controls blend falloff (higher = sharper transitions, typically 4–16)

Scale `worldPos` before sampling to control texture tiling (e.g., `worldPos * 2.0` for 50cm repeats).

#### Self-Shadowing (Optional)

For more dramatic lighting, perform a **second raymarch from the surface toward the light** to check for occlusion:

```hlsl
bool _EnableSelfShadows;  // Toggle via material property

float ComputeShadow(float3 hitPoint, float3 normal, float3 lightDir)
{
    if (!_EnableSelfShadows)
        return 1.0;  // No shadow
    
    float3 rayOrigin = hitPoint + normal * 0.01;  // Offset to avoid self-intersection
    float t = 0.0;
    
    for (int i = 0; i < MAX_SHADOW_STEPS; i++)
    {
        float3 p = rayOrigin + lightDir * t;
        float d = EvaluateScene(p);
        
        if (d < EPSILON)
            return 0.0;  // In shadow
        
        t += d;
        
        if (t > MAX_SHADOW_DISTANCE)
            break;
    }
    
    return 1.0;  // Lit
}
```

For softer shadows, accumulate a penumbra factor instead of hard 0/1:

```hlsl
float ComputeSoftShadow(float3 hitPoint, float3 normal, float3 lightDir, float softness)
{
    float3 rayOrigin = hitPoint + normal * 0.01;
    float t = 0.0;
    float shadow = 1.0;
    
    for (int i = 0; i < MAX_SHADOW_STEPS; i++)
    {
        float3 p = rayOrigin + lightDir * t;
        float d = EvaluateScene(p);
        
        if (d < EPSILON)
            return 0.0;
        
        // Soft shadow: narrow misses darken more than wide misses
        shadow = min(shadow, softness * d / t);
        t += d;
        
        if (t > MAX_SHADOW_DISTANCE)
            break;
    }
    
    return saturate(shadow);
}
```

**Performance note:** Self-shadowing roughly doubles the raymarch cost. Keep it toggleable and consider:
- Lower step count for shadow rays
- Shorter max distance
- Only enabling for the main directional light

---

### Physics / Tool Collision

Avoid generating a `MeshCollider` every frame. Instead:

- **Tool contact:** Sphere-trace or sample `Scene(toolTip)` to detect when the tool hits dirt.
- **Player grounding:** Sample SDF at feet; if inside, push up.
- **Coarse proxy collider:** Optionally maintain a low-res mesh collider updated infrequently for other physics objects.

---

### Serialisation (Save/Load)

```csharp
byte[] SaveCarveVolume(Texture3D volume)
{
    byte[] raw = volume.GetPixelData<byte>(0).ToArray();
    return Compress(raw); // e.g., GZip, LZ4
}

void LoadCarveVolume(Texture3D volume, byte[] compressed)
{
    byte[] raw = Decompress(compressed);
    volume.SetPixelData(raw, 0);
    volume.Apply();
}
```

At R8, uncompressed is ~4 MB; compresses well when mostly unexcavated (lots of 255s; compressed using run-length encoding).

---

## Stratigraphy (Analytical Material Layers)

### Concept

Instead of storing material IDs per voxel, **derive the material from world position** using analytical functions. This is memory-free, infinitely precise, and easy to author procedurally.

```csharp
MaterialLayer EvaluateMaterial(Vector3 p)
{
    // Check layers from top (youngest) to bottom (oldest)
    foreach (var layer in layers) // ordered by Harris Matrix
    {
        if (layer.Contains(p))
            return layer;
    }
    return defaultSubstrate;
}
```

---

### Base Terrain Evaluation

The base terrain SDF is modified by any layers using `Union` or `Subtract` operations:

```csharp
float EvaluateBase(Vector3 p)
{
    float sdf = baseGround.SDF(p);  // e.g., -p.y for flat ground
    
    foreach (var layer in layers)
    {
        float layerSDF = layer.geometry.SDF(p);
        
        switch (layer.geometry.operation)
        {
            case LayerOperation.Union:
                sdf = Mathf.Min(sdf, layerSDF);    // Add material
                break;
            case LayerOperation.Subtract:
                sdf = Mathf.Max(sdf, -layerSDF);   // Remove material
                break;
            // LayerOperation.Inside doesn't modify base — handled in EvaluateMaterial
        }
    }
    
    return sdf;
}
```

Most layers use `Inside` (default) and only define material regions within the existing terrain. `Union` adds geometry (burial mounds), `Subtract` removes it (modern service trenches).

---

### Layer Definition

Each layer is defined by a **signed distance function** or a **depth band**:

```csharp
[System.Serializable]
public class MaterialLayer
{
    public string name;                // e.g., "Topsoil", "Clay deposit", "Pit fill 001"
    public Color baseColour;
    public Texture2D texture;          // Triplanar or world-projected
    public float hardness;             // Optional: affects dig speed / audio

    // Geometry — choose one or combine
    public LayerGeometry geometry;
}

public enum LayerOperation
{
    Inside,     // AND — only exists within base terrain (default)
    Union,      // OR  — adds to base terrain (mounds, spoil heaps)
    Subtract    // XOR — cuts through everything (modern trench, pipe)
}

public abstract class LayerGeometry
{
    public LayerOperation operation = LayerOperation.Inside;
    public abstract float SDF(Vector3 p);            // Negative = inside
    public bool Contains(Vector3 p) => SDF(p) < 0;
}
```

#### Example Geometries

**Depth band (horizontal layer):**
```csharp
public class DepthBandGeometry : LayerGeometry
{
    public float topY;      // e.g., 0.0
    public float bottomY;   // e.g., -0.3

    public override float SDF(Vector3 p)
    {
        float dTop = topY - p.y;      // Positive below top
        float dBot = p.y - bottomY;   // Positive above bottom
        return Mathf.Max(-dTop, -dBot); // Inside when both positive → SDF negative
    }
}
```

**Undulating surface (noise-based):**
```csharp
public class NoisyDepthBand : LayerGeometry
{
    public float baseTopY;
    public float baseBottomY;
    public float noiseAmplitude;
    public float noiseFrequency;

    public override float SDF(Vector3 p)
    {
        float offset = Mathf.PerlinNoise(p.x * noiseFrequency, p.z * noiseFrequency) * noiseAmplitude;
        float topY = baseTopY + offset;
        float bottomY = baseBottomY + offset;
        return Mathf.Max(-(topY - p.y), -(p.y - bottomY));
    }
}
```

**Cut feature (pit, ditch):**
```csharp
public class CutGeometry : LayerGeometry
{
    public Vector3 centre;
    public float radius;
    public float depth;

    public override float SDF(Vector3 p)
    {
        // Vertical cylinder capped at top surface
        float horizontal = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(centre.x, centre.z)) - radius;
        float vertical = Mathf.Max(p.y - centre.y, centre.y - depth - p.y);
        return Mathf.Max(horizontal, vertical);
    }
}
```

**Fill (material inside a cut):**

Fills reuse the cut's geometry but assign a different material. The ordering matters—evaluate the fill *before* the cut so the fill takes precedence inside.

---

### Layer Ordering (Harris Matrix)

Stratigraphic law: **later events are evaluated first**.

```csharp
List<MaterialLayer> layers = new List<MaterialLayer>
{
    fill_001,   // Most recent — a backfilled pit
    cut_001,    // The pit cut (void if reached, but fill covers it)
    topsoil,
    subsoil,
    natural     // Oldest
};
```

When evaluating, the first layer whose `Contains(p)` returns true wins.

---

### Blending / Transition Zones

For soft boundaries (bioturbation, gradual colour change):

```csharp
float t = Mathf.InverseLerp(transitionStart, transitionEnd, -layer.SDF(p));
Color blended = Color.Lerp(layerAbove.baseColour, layer.baseColour, t);
```

---

### Shader Integration

Pass the layer result to the raymarch shader:

1. After finding surface hit point `p`, call `EvaluateMaterial(p)` (CPU) or encode layer logic in shader (GPU).
2. Use returned colour/texture to shade.
3. For GPU: encode layer boundaries as uniforms or a small lookup texture; switch on layer ID.

---

## Summary

| Component | Storage | Update Frequency |
|-----------|---------|------------------|
| Carve mask | 3D texture (R8), ~4 MB | Per dig stroke |
| Base SDF | Analytical function | Static |
| Material layers | Analytical functions | Static (authored) |
| Surface render | Raymarch shader | Per frame |
| Collision | SDF samples / sphere-trace | Per physics tick |
