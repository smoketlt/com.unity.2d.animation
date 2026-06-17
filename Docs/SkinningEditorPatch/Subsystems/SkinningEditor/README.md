# Skinning Editor Subsystem

This subsystem covers the Unity Sprite Editor Skinning module and this fork's custom geometry-editing workflow.

## Scope

- module host: `SkinningModule`
- cache and tool registry: `SkinningCache`
- geometry toolbar: `MeshToolbar`
- mesh tool routing: `MeshToolWrapper`, `MeshTool`
- geometry interaction: `SpriteMeshView`, `SpriteMeshController`
- mesh data mutation: `SpriteMeshDataController`, `MeshCache`, `BaseSpriteMeshData`
- copy/paste geometry: `CopyTool`
- custom input state: `SkinningEditorInput`
- skeleton bone creation and drawing: `SkeletonView`, `SkeletonController`, `BoneDrawingUtility`

## Reading Order

1. [../../Architecture/SkinningEditorArchitecture](../../Architecture/SkinningEditorArchitecture.md)
2. [SkinningModule](SkinningModule.md)
3. [MeshToolbar](MeshToolbar.md)
4. [GeometryEditing](GeometryEditing.md)
5. [MeshDataAndWeights](MeshDataAndWeights.md)
6. [SkeletonBoneEditing](SkeletonBoneEditing.md) when Create Bone gestures or bone drawing are involved
7. [AltInputModeSwitching](AltInputModeSwitching.md) when Alt or mode swap behavior is involved
8. [ResetGeometry](ResetGeometry.md) when Reset or weight confirmation is involved
9. [CopyPasteGeometry](CopyPasteGeometry.md) when clipboard workflows are involved
10. [GenerateGeometryTool](GenerateGeometryTool.md) or [GenerateWeightsTool](GenerateWeightsTool.md) when generated mesh/weights are involved
11. [BoneInfluence](BoneInfluence.md) when changing assigned bone/sprite influence behavior or auto weights after assignment
12. [WeightPainterTool](WeightPainterTool.md) when changing Weight Slider/Brush mode selection, brush settings, or weight inspector panel behavior
13. [VisibilityTool](VisibilityTool.md) when changing the Visibility popup, opacity sliders, or visibility lists

## Entity Pages

- [SkinningModule](SkinningModule.md)
- [SkinningCache](SkinningCache.md)
- [MeshToolbar](MeshToolbar.md)
- [MeshToolWrapper](MeshToolWrapper.md)
- [SpriteMeshView](SpriteMeshView.md)
- [SpriteMeshController](SpriteMeshController.md)
- [SpriteMeshDataController](SpriteMeshDataController.md)
- [MeshDataAndWeights](MeshDataAndWeights.md)
- [SkeletonBoneEditing](SkeletonBoneEditing.md)
- [AltInputModeSwitching](AltInputModeSwitching.md)
- [ResetGeometry](ResetGeometry.md)
- [CopyTool](CopyTool.md)
- [CopyPasteGeometry](CopyPasteGeometry.md)
- [GenerateGeometryTool](GenerateGeometryTool.md)
- [GenerateWeightsTool](GenerateWeightsTool.md)
- [BoneInfluence](BoneInfluence.md)
- [WeightPainterTool](WeightPainterTool.md)
- [VisibilityTool](VisibilityTool.md)

## Current User-Facing Geometry Semantics

- `Modify`: edit existing vertices/edges.
- `Create`: create vertices and drag from vertices to create edges.
- `New`: clears the current mesh and enters open mesh hull creation mode.
- `Reset`: reset current sprite mesh to a four-corner rectangle.
- `Generate`: open auto geometry generation.
- Holding `Alt` in `Modify` temporarily behaves like `Create`.
- Holding `Alt` in `Create` temporarily behaves like `Modify`.
