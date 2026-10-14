# Skinning Module Assets

## Purpose

This page maps UXML/USS/resource files used by the Skinning Editor module.

## Primary Location

- `Editor/Assets/SkinningModule/**`

## Toolbar Assets

| Asset | Purpose |
| --- | --- |
| `MeshToolbar.uxml` | Geometry toolbar buttons |
| `MeshToolbarStyle.uss` | Geometry toolbar styling |
| `BoneToolbar.uxml` | Bone toolbar |
| `WeightToolbar.uxml` | Weight toolbar |
| `ConstraintsToolbar.uxml` | Runtime constraint toolbar |
| `PoseToolbar.uxml` | Pose toolbar |
| `RigToolbar.uxml` | Rig copy/paste toolbar |

## Geometry Toolbar Notes

`MeshToolbar.uxml` still uses the old UXML name `SplitEdge` for the Reset button. C# treats it as `k_ResetGeometryId`.

`MeshToolbar.uxml` also still uses the old UXML name `CreateEdge` for the `New` button. C# still routes it through `Tools.CreateEdge`, but the behavior is new mesh hull creation.

Do not rename the UXML element unless all C# lookups, USS selectors, and metadata references are checked.

## Panels

Geometry selected/hovered dots use a shared neutral procedural texture cached by `SpriteMeshView.Styles`; drawing tints it with the live [Uniskin preference colors](../Subsystems/SkinningEditor/GeometryEditing.md#uniskin-preferences). Normal dots still use `dotCyan.png`. Existing `dotYellow.png` is retained for other consumers; no new imported bitmap is required.

- `SceneMeshInspector.uss`: SpriteRenderer and Clipping Mask buttons reuse action heights, spacing and icon sizing. Background/state colors are left to each native Inspector host. Mask buttons intentionally retain their darker default appearance: attempts to force matching backgrounds and periodically clear inline colors were removed because they caused flicker. The mask editor does not write background colors or install pointer/focus color callbacks.

- `SceneEditClippingMaskWindow.uxml`: dedicated vector contour popup with Modify/Create/Delete, New/Reset and one Symmetry/Deformed/Triangles/Masked row. Clipping-specific toggle margins keep the row compact. Masked switches staged stencil preview without saving geometry. It reuses `SceneEditMeshWindow.uss` for consistent colors and sizing; no Copy/Paste/Generate/Trace elements are included. Its separate `SceneEditClippingMaskWindow` host shares the mesh editing engine. See [Masking](../Subsystems/SkinningEditor/Masking.md).

- `BoneIcons/*.png`: the user's 29 original transparent icon textures. `BoneIconPicker` presents 35 choices with four cardinal rotations each for Arrow and Direction, using `SceneEditBoneWindow.uss` for the compact seven-column popup matching Edit Bones dark colors and teal current tile. Preserve `boneIcon` and `boneIconImage` in the Edit Bones properties row. See [Bone Icons](../Subsystems/SkinningEditor/BoneIcons.md).

- `SceneMeshIcons/Copy.png` and `Paste.png`: user-supplied icons for whole-mesh clipboard commands in Scene Edit Mesh. `SceneEditMeshWindow.uxml` adds `copy`, `paste` and a separate `pasteSettings` preview panel with a mirror toggle, English Scene placement/Enter/Escape instructions, Cancel and Apply controls. Scene handles provide translation and corner rotation; numeric transform fields have been removed.

- `SceneEditMeshWindow.uxml` / `.uss`: floating **Edit Mesh** popup with a separate Symmetry row (New/Modify/Create/Delete) and Deformed, Triangles, Dim and Isolate toggles. Preserve `windowIcon`, `windowTitle`, `symmetry` and `deformed` for C# queries. The native title/header icon uses the existing `SceneMeshIcons/CreateMesh.png`; icon size accounts for editor DPI.
- `GenerateGeometryPanel.uxml` / `.uss`
- `GenerateWeightsPanel.uxml` / `.uss`
- `PastePanel.uxml` / `.uss`
- `WeightPainterPanel.uxml` / `.uss`
- `AnimationPreviewPanel.uss` styles the persistent bottom timeline created by `AnimationPreviewPanel.cs`.
- `VisibilityToolWindow.uxml`
- `SceneEditBoneWindow.uxml` / `.uss`: floating **Edit Bones** skeleton popup; shares the Edit Mesh stylesheet and header/height helper. The header contains the `windowIcon` Image, `windowTitle` Label and `closeWindow` Button. Preserve these names for C# queries. `SceneMeshIcons/Bone_Create.png`, `Bone_Split.png`, and `Bone_SetParent.png` are the user's supplied command icons.
- `SceneMeshIcons/EditBone.png`: user's supplied 16x16 icon for **Edit Bones** in the SpriteRenderer Inspector, the character root/bone GameObject Inspector header, the popup's native title content and its visible `windowIcon`. C# adjusts the header icon dimensions for editor DPI; skeleton-only entry displays **Edit Bones — Skeleton**.
- `InfluenceWindow.uxml`
- `SceneMeshInspector.uss` styles the renderer's Mesh/Bone/Weights buttons, the shared `UniskinRigInspectorControls` mode/bake block and `.uniskin-rig-action` buttons in `UniskinRigEditor`. These controls use UI Toolkit; no native UXML element names are replaced.
- `SceneMeshIcons/UniskinLogo.png`, `PsbLogo.png`, `BakeToPsb.png`: supplied rig mode/bake icons. `UniskinRigInspectorControls` uses centered Image/Label buttons, a flexible two-button mode row and a full-width Bake row. The character root header uses the same images/labels through the existing native IMGUI header extension. Preserve `uniskinUseRig`, `uniskinUsePsb` and `uniskinBakeRig`; `uniskinOpenSkinning` is removed from the renderer's rig block.
- Constraint settings are built in `Editor/SkinningModule/ConstraintsTool.cs` and hosted in the bottom overlay.

## Shaders

- `SkinningModule-GUITextureClip.shader` draws clipped textured editor meshes such as sprite mesh previews.
- `SkinningModule-BoneTexture.shader` draws bitmap bone body slices in both Skinning Editor and Scene view. It does not sample GUI clip masks, because Scene view bone gizmos should not be clipped by editor GUI textures.

## Overlay Placement

Skinning tool panels that are short inspector/control windows are hosted in `LayoutOverlay.bottomOverlay`, appear centered at the bottom by default, and can be moved by their title bars or resized with the lower-right grip. This includes Weight Painter, Bone Inspector, Generate Geometry, Generate Weights, Paste, Pivot, Influence, and Constraints panels. Each panel saves its customized position and size across tool changes and editor reopening; see [Tool Panel Layout](../Subsystems/SkinningEditor/PanelLayout.md). Shared popup backgrounds and content follow the root's resized dimensions, and scrollable weight/influence lists use the available height.

The persistent Animation Preview timeline is hosted in `LayoutOverlay.timelineOverlay`, an absolute 44-pixel strip at the bottom edge. `bottomOverlay` reserves 54 pixels beneath its draggable panels so they remain above the timeline.

The Visibility window stays in `LayoutOverlay.rightOverlay` because it is a tall resizable list window. Shared popup sizing and form styles in `Editor/Assets/LayoutOverlay/LayoutOverlayStyle.uss` must cover both right and bottom overlay selectors when a panel can live in either area.

## Weight Painter Panel Notes

The floating Scene Edit Weights host uses `SceneEditWeightsWindow.uxml` / `.uss`, shares the Edit Mesh base style, and uses `SceneWeightsPreview.shader` for alpha-aware textured weight preview. Its supplied Weights/Smooth/Bind/Auto/Prune/Swap/Weld/Delete icons live under `SceneMeshIcons`; Remove uses Delete. Existing icon GUIDs are retained when replacing image bytes. See [Scene Weight Editing](../Subsystems/SkinningEditor/SceneWeightEditing.md).

The Scene weight list flexes with its popup's height resize grip. Bone fills use the shared fixed weight palette, and swatches use `WeightInspector.LockTexture`. Scene Prune builds its UI with the same `SceneEditMeshWindow.uss`; the existing Sprite Editor Prune caller retains its utility window.

Scene Strength/Size/Feather are float `Slider` elements. The weights stylesheet fixes row heights and resets tracker offsets to keep fills inside their selected rows during resize. Slider drag containers use `align-self: stretch` and explicit 20 px tracker/container heights so absolutely positioned tracks/fills cannot collapse their intrinsic width. This applies to brush controls and dynamically created bone rows. Window layout and control values are saved by `SceneEditWeightsWindow` in per-project EditorPrefs.

`WeightPainterPanel.uxml` contains placeholder containers for the Mode and Bone popups. `WeightPainterPanel.GenerateFromUXML()` creates both popup controls in C#.

The Mode popup is intentionally a `PopupField<string>` backed by `WeightEditorMode` values. Avoid replacing it with `EnumField` unless Git-installed package behavior is re-tested in Unity 6000, because the enum dropdown can fail to open or select values when resolved from an internal editor package assembly.

## Change Risks

- UI Toolkit queries rely on stable names.
- Tooltips in UXML should match command behavior.
- Localized text generated through code should live in `TextContent.cs`.
