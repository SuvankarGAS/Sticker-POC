# Doodle Sticker (Built-in Render Pipeline)

Peelable doodle stickers for world-space sprites and uGUI. Needs Unity 2021.3 or newer.

## Install

1. Copy the `DoodleSticker` folder anywhere under `Assets/`.
2. Delete the old `DoodleStickerSpriteBinder.cs` and `DoodleStickerCurlController.cs`. They are replaced by `DoodleSticker`.
3. Create materials with **Assets > Create > Doodle Stickers > Sticker Material** and **UI Sticker Material**.

## World sticker

- **GameObject > 2D Object > Doodle Sticker**, then assign a Sprite and the Sticker Material.
- Pointer input needs an **EventSystem** in the scene. A **Doodle Sticker Raycaster** is added to the main camera automatically. To turn that off, untick *Add Raycaster To Main Camera*.
- **Dynamic batching:** stickers that share a material and texture batch together. Enable **Dynamic Batching** in Player Settings.

## UI sticker

- **GameObject > UI > Doodle Sticker**, then assign a Sprite and the UI Sticker Material.
- Works with Mask, RectMask2D, and every Canvas render mode. The required canvas shader channels are enabled automatically.
- **Performance:** each drag frame rebuilds that canvas. Put busy stickers on their own nested Canvas.

## Sprite requirements

- In the Sprite Atlas, turn off **Allow Rotation** and **Tight Packing**. The inspector warns if either is on.
- 9-slice borders are ignored.
- **Hit testing** uses the sprite's Physics Shape (Sprite Editor > Custom Physics Shape). It falls back to the sprite mesh or the rect.

## Posing and scripting

- **Pose without Play mode:** drag the blue (grab) and red (drag) handles in the Scene view, or use the Initial Pose buttons.
- **Control from code:** `Flatten()`, `ResetToInitialPose()`, and `Peel` (state, lifted fraction, events).
- **Changing the template material at runtime:** call `DoodleStickerMaterialCache.RefreshFromTemplates()` afterwards.
