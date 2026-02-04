# Excavation System - Changelog

## [1.0.0] - 2026-02-03

### 🎉 Initial Implementation

Complete implementation of SDF-based archaeological excavation system for Unity HDRP based on technical specifications.

### ✨ Features Added

#### Core Systems
- **ExcavationManager**: 3D volume management with compute shader carving
- **StratigraphyEvaluator**: Analytical layer system with Harris Matrix ordering
- **ExcavationRenderer**: Hierarchical raymarching renderer for HDRP
- **DigTool**: Player excavation tool with input handling and feedback

#### ScriptableObject Assets
- **ExcavationVolumeSettings**: Configurable volume presets
- **MaterialLayer**: Stratigraphic layer definitions with visual properties
- **DigBrushPreset**: Tool presets with customizable parameters

#### Layer Geometry Types
- **DepthBandGeometry**: Horizontal layers
- **NoisyDepthBandGeometry**: Perlin noise undulating layers
- **CutGeometry**: Cylindrical cuts for pits and postholes
- **EllipsoidGeometry**: Burial mounds and rounded features

#### Shaders
- **ExcavationRaymarch.shader**: HDRP raymarch shader with:
  - Hierarchical raymarching with adaptive MIP levels
  - Conservative MIP sampling for safe skip distances
  - Soft shadow raymarching (optional)
  - Proper depth writing for scene integration
  - Material layer evaluation
  - Triplanar texture support (framework)
  
- **Compute Shaders**:
  - `InitializeVolume.compute`: Volume initialization
  - `CarveVolume.compute`: SDF boolean carving operations
  - `GenerateMips.compute`: Conservative MIP map generation

#### Utilities
- **SDFUtility**: Common SDF operations (Union, Subtract, Intersect, SmoothMin)
- **BrushStroke**: Carving operation data structure
- **SurfaceHit**: Raymarching result structure

#### Editor Tools
- **MaterialLayerEditor**: Custom inspector with geometry quick-add buttons
- **StratigraphyEvaluatorEditor**: Visual layer stack preview and SDF testing
- **ExcavationManagerEditor**: Runtime controls, volume stats, save/load UI
- **ExcavationSetupWizard**: Automated scene setup tool (Tools → Excavation → Setup Wizard)

#### Features
- ✅ Real-time SDF-based terrain carving
- ✅ Multi-layer stratigraphy with analytical geometry
- ✅ Hierarchical raymarching for performance
- ✅ Material hardness affecting dig speed
- ✅ Audio feedback based on material type
- ✅ Controller haptic feedback
- ✅ Volume serialization with GZip compression
- ✅ Debug gizmo visualization
- ✅ Configurable raymarch quality settings

### 📚 Documentation
- Comprehensive README with architecture overview
- Quick Setup Guide (10-minute start)
- Implementation Summary with statistics
- Inline XML documentation on all public APIs
- Troubleshooting section
- Performance tuning guidelines

### 🎨 Example Content
- Example layer assets (Topsoil, Subsoil)
- Example volume settings preset
- Example brush preset (Trowel)

### 🔧 Technical Details
- **Architecture**: Modular system with ScriptableObject-based configuration
- **Rendering**: HDRP forward pass with depth writing
- **Performance**: Conservative MIP maps enable efficient large empty space traversal
- **Memory**: Configurable resolution (75K to 5M+ voxels supported)
- **Serialization**: GZip compression for save files
- **Input**: New Input System with mouse fallback

### 📊 Statistics
- **Total Files**: 28
- **C# Code**: ~2,000 lines
- **Shaders**: ~600 lines HLSL
- **Documentation**: ~800 lines
- **Assembly Definitions**: 2 (Runtime + Editor)

### Known Limitations
- Texture sampling in C# uses placeholder (CPU collision via analytical SDF instead)
- Layer data transfer limited to 8 layers via fixed arrays (extensible to structured buffers)
- HDRP lighting uses simplified directional light (full lighting integration pending)
- Triplanar texturing framework in place but not fully wired up

### Platform Support
- Unity 2021.3+
- HDRP 12.0+
- New Input System 1.0+
- Desktop (tested)
- VR-ready architecture (controller support implemented)

### Dependencies
- Unity.InputSystem
- Unity.RenderPipelines.Core.Runtime
- Unity.RenderPipelines.HighDefinition.Runtime

---

## Future Enhancements (Roadmap)

### Version 1.1
- [ ] Full triplanar texturing with normal maps
- [ ] HDRP full lighting integration
- [ ] Structured buffer for unlimited layers
- [ ] Async GPU readback for C# texture sampling
- [ ] Performance profiling and optimization pass

### Version 1.2
- [ ] Player grounding physics integration
- [ ] Advanced shadow techniques (ambient occlusion)
- [ ] Custom erosion/weathering brush types
- [ ] Layer transition blending zones

### Version 2.0
- [ ] Marching Cubes mesh export
- [ ] Physics collider generation
- [ ] Multi-volume support
- [ ] Networked collaborative digging
- [ ] Artifact placement system

---

## Acknowledgments

Based on:
- Technical specification in `Documents/Implementation.md`
- Claybook's hierarchical raymarching technique
- Inigo Quilez's SDF operations
- Harris Matrix stratigraphic principles

Implemented for: Archaeological excavation simulation and training
