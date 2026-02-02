# Implementation

## Digging (SDFs)

### Overview

The excavation system uses a **Signed Distance Field (SDF)** to represent the terrain surface. Player carving is stored in a **3D texture** (baked volume) that records how much material has been removed at each point.

**Core composition rule:**
```
Scene(p) = max(Base(p), -Carve(p))
```
- `Base(p)` — the original, unexcavated ground SDF (e.g., a flat plane or gentle terrain)
- `Carve(p)` — the accumulated excavation depth, sampled from the 3D texture
- Positive = outside surface, Negative = inside solid

---

### 3D Carve Volume

#### Configuration

```csharp
[System.Serializable]
public class CarveVolumeSettings
{
    public Vector3 worldOrigin = Vector3.zero;      // Corner of the excavation bounds
    public Vector3 worldSize = new Vector3(10, 5, 10); // 10m x 5m x 10m
    public float voxelSize = 0.05f;                 // 5 cm resolution (adjustable)
}
```

Derived values:
- `resolution = ceil(worldSize / voxelSize)` → e.g., 200 × 100 × 200 at 5 cm
- Memory: 1 byte per voxel → ~4 MB at this size

#### Texture Format

| Option | Precision | Notes |
|--------|-----------|-------|
| `R8_UNorm` | 256 depth levels | Lightweight; map 0–1 to max carve depth (e.g., 2 m) |
| `R16_UNorm` | 65 k levels | Higher precision if needed |
| `R32_SFloat` | Full float | Overkill for most cases |

Start with **R8** (1 byte); upgrade if banding artifacts appear.

#### Coordinate Mapping

```csharp
// World → texture UV (0–1)
Vector3 WorldToUV(Vector3 worldPos, CarveVolumeSettings s)
{
    return (worldPos - s.worldOrigin) / s.worldSize;
}

// UV → voxel index
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
   - If `d < 0` (inside sphere), update the carve texture:
     ```
     carve[voxel] = max(carve[voxel], -d)
     ```
     This records the deepest penetration.

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
float EvaluateScene(Vector3 p, CarveVolumeSettings settings, Texture3D carveVolume)
{
    float baseSDF = EvaluateBase(p);               // e.g., -p.y for flat ground at y=0
    
    Vector3 uv = WorldToUV(p, settings);
    if (OutOfBounds(uv)) return baseSDF;           // Outside excavation zone
    
    float carveDepth = SampleCarveVolume(carveVolume, uv) * maxCarveDepth;
    return Mathf.Max(baseSDF, -carveDepth);
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

At 5 cm / R8, uncompressed is ~4 MB; compresses well when mostly unexcavated.

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

public abstract class LayerGeometry
{
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
    naturalCite // Oldest
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
| Carve volume | 3D texture (R8), ~4 MB | Per dig stroke |
| Base SDF | Analytical function | Static |
| Material layers | Analytical functions | Static (authored) |
| Surface render | Raymarch shader | Per frame |
| Collision | SDF samples / sphere-trace | Per physics tick |

