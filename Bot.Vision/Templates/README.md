# Vision Templates

Place gameplay template images in this folder:

- `map_button.png`
- `resource_tile.png`
- `gather_button.png`
- `lowest_tier_button.png`
- `deploy_button.png`

Army indicator template (needed for OCR-based max army limit control):

- `army_indicator_icon.png`

Other templates used by flow:

- `resource_stone.png`
- `resource_wood.png`
- `resource_ore.png`
- `resource_food.png`
- `resource_rune.png`
- `transfer_button.png` (tile popup button)
- `occupy_button.png` (tile popup button)
- `popup_close.png` (top-right popup close)

Notes:
- Use clean emulator screenshots for crops.
- Keep template scale close to runtime resolution.
- Re-capture templates after UI skin/theme changes.
- OCR runtime requires `eng.traineddata` in `Bot.Vision/Tessdata` or set `BOT_TESSDATA_PATH`.
