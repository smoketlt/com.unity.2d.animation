# Tool Panel Layout

## Scope

The bottom overlay settings panels can be moved by their title bars and resized in both dimensions by dragging the visible lower-right corner grip. This covers Weight Brush, Weight Slider, Bone Inspector, Generate Geometry, Generate Weights, Paste, Pivot, Bone Influence, Sprite Influence, and the Position/Rotation/Scale Constraints panels.

The Visibility popup retains its separate right-overlay layout and existing resizer. The Animation Preview timeline remains a fixed bottom strip.

## Source

- `Editor/LayoutOverlay/OverlayPanelLayout.cs`: layout restoration, bounds, minimum size, and preference storage.
- `Editor/LayoutOverlay/LayoutOverlayUtility.cs`: shared handles and panel registration.
- `Editor/LayoutOverlay/Manipulators/OverlayPanelDragger.cs`: pointer capture for moving and resizing, including saving on capture loss.
- `Editor/LayoutOverlay/LayoutOverlay.cs`: bottom panel registration.
- `Editor/Assets/LayoutOverlay/LayoutOverlayStyle.uss`: handles and flexible popup content.
- `Tests/Editor/OverlayPanelLayoutTests.cs`: EditMode layout regressions.

## Persistence And Mode Identity

Customized position and size are stored in `EditorPrefs` under `Unity.2D.Animation.SkinningPanel.<Application.dataPath>.<layoutKey>`. Settings belong to the current user and project, survive panel hiding, Sprite Editor recreation, assembly reload, and Unity restart, and do not change imported sprite data or create an undo operation.

Most panels use their element type and stable UXML name as their identity. The two Influence tools explicitly use separate `BoneInfluence` and `SpriteInfluence` keys because they instantiate the same window type. The Constraints tools use a separate key for each constraint type. Weight Painter shares one panel instance, so activation switches its layout identity to `WeightPainter.Brush` or `WeightPainter.Slider`; the previous mode is saved before the new one is restored. Localized titles are never used as keys.

Tools call `SaveDraggableOverlayPanel` before hiding. They no longer reset positioning or dimensions. Layout is also saved on pointer release, pointer capture loss, and detachment. Invalid saved data is ignored. Panels with no saved layout retain the original bottom-center placement.

## Sizing And Bounds

Restoration waits for valid visible panel and parent geometry. The initial natural size is the minimum for compact forms. Weight Painter uses a 300-by-220 minimum, and Influence panels use a 300-by-160 minimum so their scrollable lists can shrink. Popup backgrounds follow the resized root. Weight rows use the available inspector height instead of a fixed five-row budget, and their sliders expand horizontally. Influence and Constraints lists expand with available height.

Moving and restoring clamp the panel inside the current overlay. Resizing keeps its upper-left corner fixed and limits growth to the remaining viewport space. If the viewport is smaller than the minimum, the visible panel shrinks to fit. Automatically fitting a smaller viewport does not overwrite the preferred saved rectangle, so enlarging the viewport restores it. A subsequent deliberate move or resize saves the newly chosen rectangle.

## Verification

Run EditMode tests filtered to `UnityEditor.U2D.Animation.Tests.OverlayPanelLayoutTests`. The tests cover default placement, hide/show and panel recreation, independent keys on a shared panel, temporary viewport shrinkage, minimum sizes and bounds, resize-pointer capture loss, and invalid stored settings.

Manual verification in the Skinning Editor should cover dragging and resizing every listed panel, switching Brush/Slider and both Influence modes, closing/reopening the Sprite Editor, restarting Unity, and shrinking/enlarging the editor viewport. Check list growth and scrolling as well as form controls, drag cursors, and the lower-right grip. These UI Toolkit APIs are shared by Unity 6000.0 and 6000.5 or newer; no new version-specific API branch is required.

Verification on 2026-10-05: all eight layout tests passed in Unity 6000.0.81f1 and 6000.5.7f1 in isolated batch-mode projects with graphics enabled, using the actual layout controller, pointer manipulator, test source, and changed stylesheets from this checkout. The complete editor assembly also compiled against the current Unity 6000.0 project references; its existing Constraints `onSelectionChange` deprecation warning remains. The 6000.5 test host used cached Test Framework 1.7.0 because 1.6.0 cannot compile against the newer TreeView APIs. This verifies layout behavior and stylesheet imports, but does not replace visual/input checks of every real Skinning Editor panel or an actual Unity restart.
