# Changelog

## 1.0.3
- Changed: reviving takes 2 seconds instead of 3.
- Changed: reviving costs 10% of your health (at least 10, so with 10 or less it kills you),
  and the revived player gets exactly the health you paid.

## 1.0.2
- Added: a green bar fills up under the crosshair while you hold H, so you can see how long until the revive happens.
- Added: sounds while charging, when the revive happens, and when you let go early. The volume can be set with `SoundVolume` in the config.

## 1.0.1
- Fixed: holding H while carrying a dead player's head did nothing.

## 1.0.0
- First release: hold H while carrying a dead player's head to revive them for 40 health.
  They come back with 10 health, or with 1 health if you had less than 40 (which kills you).
