# Name ornament: trinity knot with a fading line

Approved 2026-10-25 (Jatify detail pass).

- `trinity-knot-line.svg`: the knot (46 x 24 view box), Jatify green `#22d67e`, one band with a gap wherever it passes under
  itself (no border), a tip pointing away from the name, a 1.1 px line stub out of that tip at y = 12. Used on both sides of
  creature nameplates, the boss name and panel headings (the right-hand copy mirrored); the line beyond the stub fades out.
- `triquetra.py`: builds the knot's centre line from the reference triquetra (three circular arcs), finds its three crossings
  and alternates over/under along the band. The SVG is drawn from it: the band, masked where the strand on top crosses
  (cut 3.4 units along it, band + 2 x 0.7 wide), and the top strands redrawn over the cut.
- In game: `assets/icons/knot.png` is this SVG in white at 4x (184 x 96), tinted by the image colour (green on nameplates and
  headings, red on the boss bar): `sed s/#22d67e/#ffffff/ trinity-knot-line.svg | rsvg-convert -w 184 -h 96 -o ../icons/knot.png`.
  The fading line beyond the stub is `Kit.FadeLine()` (a generated 64 x 1 ramp); `Kit.Ornament` puts the two together.

- `shield-knot.svg` (approved 2026-10-26): the shield knot for the inventory's section divider — a ring split by a cross with
  an inner woven square, thick band, sharp tips at the ring's slits, over/under at its four crossings.
- `eternity-knot.svg`: the blocky eternity knot proposed for the top-left and bottom-right window corners, the panel edges
  fading out of it (in the preview, not yet approved).
