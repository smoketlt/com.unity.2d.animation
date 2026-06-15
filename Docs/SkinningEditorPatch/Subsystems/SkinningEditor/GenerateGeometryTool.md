# GenerateGeometryTool

## Purpose

`GenerateGeometryTool` owns automatic mesh outline generation, triangulation, optional subdivision, and optional weight generation.

## Source

- `Editor/SkinningModule/GenerateGeometryTool.cs`
- `Editor/Assets/SkinningModule/GenerateGeometryPanel.uxml`
- `Editor/SkinningModule/UI/GenerateGeometryPanel.cs`

## Entry Points

- `OnCreate()`
- `OnActivate()`
- `OnDeactivate()`
- panel callbacks:
  - `onAutoGenerateGeometry`
  - `onAutoGenerateGeometryAll`

## Inputs

- selected sprite or visible sprite list;
- outline detail;
- alpha tolerance;
- subdivision setting;
- generate-weights toggle;
- texture data provider.

## Outputs

- creates outline geometry from alpha;
- triangulates mesh data;
- optionally subdivides geometry;
- optionally generates weights;
- updates mesh preview behavior when weight generation is enabled.

## Pipeline

1. Generate outline from alpha into vertices and edges.
2. Triangulate base mesh, optionally through jobs.
3. Fallback to synchronous triangulation if needed.
4. Subdivide if requested.
5. Generate weights if requested.
6. Dispose native arrays and clear progress UI.

## Dependencies

- `MeshToolWrapper`
- `GenerateGeometryPanel`
- `SpriteMeshDataController`
- `OutlineGenerator`
- `Triangulator`
- `BoundedBiharmonicWeightsGenerator`

## Change Risks

- Native arrays must be disposed.
- Job result counts control how mesh data is copied back.
- Generate-weights state affects preview behavior.
- Geometry generation can overwrite custom geometry and weights.
