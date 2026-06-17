# GenerateWeightsTool

## Purpose

`GenerateWeightsTool` owns the panel workflow for generating, normalizing, and clearing skinning weights.

## Source

- `Editor/SkinningModule/GenerateWeightsTool.cs`
- `Editor/Assets/SkinningModule/GenerateWeightsPanel.uxml`
- `Editor/SkinningModule/UI/GenerateWeightsPanel.cs`

## Entry Points

- `OnCreate()`
- `OnActivate()`
- `OnDeactivate()`
- panel callbacks:
  - `onGenerateWeights`
  - `onNormalizeWeights`
  - `onClearWeights`

## Inputs

- selected sprite or character mode sprite set;
- current vertex selection;
- associate-bones option;
- current mesh and skeleton data.

## Outputs

- generates weights for selected or relevant vertices;
- normalizes weights;
- clears weights;
- updates mesh data and preview state.

## Bone Selection Clearing

Auto Weights enables `SkeletonTool` bone unselection while its panel is active. `Esc` and primary empty click clear selected bones the same way Weight Slider and Weight Brush do. Right-click does not clear selected bones.

## Dependencies

- `MeshToolWrapper`
- `GenerateWeightsPanel`
- `SpriteMeshDataController`
- `BoundedBiharmonicWeightsGenerator`
- `AssociateBonesScope`

## Change Risks

- Weight commands should respect selection semantics.
- Character mode and sprite sheet mode differ in which sprites are affected.
- Clearing weights can affect Reset confirmation behavior because Reset checks whether weights currently sum above zero.
