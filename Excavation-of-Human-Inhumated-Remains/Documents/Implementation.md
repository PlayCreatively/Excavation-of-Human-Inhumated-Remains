# Implementation

## Digging (SDFs)

### Overview

The excavation system uses a **Signed Distance Field (SDF)** to represent the terrain surface. The 3D texture stores the **actual distance** to the nearest excavated surface in world units.

### Key Concept: Texture-Based SDF

Each voxel stores a **Signed Distance**:
- **Negative (< 0):** Inside the "Carve Volume" (Air/Excavated).
- **Positive (> 0):** Outside the "Carve Volume" (Solid Ground).
- **Zero (0):** The exact surface boundary.

**The "Blindness" Problem:**
A raw texture is blind in empty space. If a voxel says "distance = -5.0", it knows you are 5m inside the void, but it doesn't know *direction*. This prevents standard Sphere Tracing from working efficiently over long distances.

**The Solution: Hierarchical Raymarching (The "Claybook" Trick)**
To skip empty space efficiently, we generate **MIP Maps** of the 3D texture using a **conservative** downsampling rule.
- **Mip 0:** High-res exact distance.
- **Mip 1:** "Conservative" distance (guarantees the wall is *at least* this far).
- **Mip 2:** Even coarser conservative distance.

This allows the raymarcher to take massive leaps through empty air using low-res MIPs, and only switch to high-res data when close to the surface.

---

### 3D Carve Mask

#### Configuration

```csharp
[System.Serializable]
public class CarveVolumeSettings
{
    public Vector3 worldOrigin = Vector3.zero;
    public Vector3 worldSize = new Vector3(10, 5, 10);
    public float voxelSize = 0.05f;      // 5 cm resolution
}
```

#### Texture Format

| Option | Precision | Use Case |
|--------|-----------|----------|
| `R16_SFloat` | 16-bit Float | **Recommended.** High precision, native negative values. |
| `R8_SNorm` | 8-bit Signed | Low memory (~4MB). Range -1 to +1. Requires remapping distances to fit range. |

**Initialization:** The texture should be initialized to a large **positive** value (e.g., `9999.0`), representing that no holes exist yet (everything is "far outside" the void).

---

### Carving (Boolean Subtraction)

When the player digs, we simply combine the existing void shape with the new brush shape using a **Union** operation on the void (which effectively subtracts from the ground).

**Logic:** `Void = Union(OldVoid, NewBrush)`  
In SDF terms (where negative = inside void): `min(OldSDF, BrushSDF)`

```csharp
// 1. Calculate the Brush SDF
// Negative = Inside brush (Air)
// Positive = Outside brush
float brushSDF = distance(voxelPos, brushCenter) - brushRadius;

// 2. Combine with existing mask
// We take the minimum because we want the union of all "negative" zones (holes)
float oldSDF = carveMask[voxel];
carveMask[voxel] = Mathf.Min(oldSDF, brushSDF);
```

**Digging Speed / Softness:**
Instead of setting the value instantly, you can subtract over time to simulate "melting" or scraping:
```csharp
carveMask[voxel] = Mathf.Min(carveMask[voxel], brushSDF - (digSpeed * deltaTime));
```

---

### Hierarchical Raymarching

#### 1. Conservative MIP Generation

When updating the texture, we must update the MIP chain. The downsampling rule is crucial: **"Never lie about safety."**

For a parent voxel to be "safe," it must report the minimum distance found in its children, minus a correction factor to account for the spatial difference.

```csharp
// Mip Generation Logic (Compute Shader)
float minChild = Min(c0, c1, c2, c3, c4, c5, c6, c7);

// Correction: Distance from parent center to child center
// (Half the diagonal of the parent voxel)
float correction = (parentVoxelSize * sqrt(3)) * 0.5;

parentSDF = minChild - correction;
```
*Result: A low-res voxel might say "Wall is 2m away" when it's actually 2.5m away. This is safe. It will never say "2m" if the wall is 1m away.*

#### 2. The Raymarching Loop

We assume two samplers:
- `SamplerPoint`: For Mip levels > 0 (Safety).
- `SamplerLinear`: For Mip level 0 (Visual smoothness).

```hlsl
float t = 0;
int mip = MAX_MIP; 

for(int i=0; i<MAX_STEPS; i++)
{
    float3 p = rayOrigin + rayDir * t;
    
    // 1. Sample Texture at current MIP
    // Use Point Sampling for Mips > 0 to preserve conservative bounds!
    float d = Texture.SampleLevel(SamplerPoint, uv, mip).r;
    
    // 2. The "Safety Brake"
    // If distance is smaller than the current voxel size, this MIP is too coarse.
    // We risk clipping. Drop down a level and retry WITHOUT marching.
    float voxelSize = GetVoxelSize(mip);
    if(d < voxelSize * 1.5 && mip > 0)
    {
        mip--;
        continue; // Retry logic with higher res
    }
    
    // 3. Surface Hit
    // Use a small threshold. If at Mip 0, we can use Linear Sampling for exact surface.
    if(d < 0.001) break;
    
    // 4. March
    // Nudge to resolve boundary ambiguity when using coarse MIPs
    float nudge = (mip > 0) ? (voxelSize * 0.25) : 0.0;
    t += d + nudge;
}
```

---

### Core Composition Rule

With the texture now storing **Void SDF** (Negative = Air, Positive = Solid Ground), the composition logic remains:

```csharp
// Base Terrain: Negative = Solid
// Carve Mask: Negative = Air (Inside Void)

// We want: Solid where (Base is Solid) AND (Carve Mask is NOT Air)
// Boolean Subtraction: Intersection(Base, -CarveMask)

float sceneSDF = max(BaseTerrain(p), -CarveMask(p));
```

- If `CarveMask` is -5 (Inside hole) → `-(-5) = +5`. `max(Base, +5)` = **+5 (Air)**.
- If `CarveMask` is +99 (Far from hole) → `-99`. `max(Base, -99)` = **Base (Solid)**.

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
