# Wheelhouse brand assets

*Last updated: 2026-10-14*

> Selected **Soft folds** identity: a rounded paper boat replacing the initial W.

## Usage

- default: compact wordmark without a flag or attribution
- optional endorsement: neutral `by` plus the approved WoW2 Panels symbol and name, beneath the lettering
- navy parent artwork accompanies black Wheelhouse; white parent artwork accompanies white and mint Wheelhouse
- standalone mark: compact navigation, app lists and icon contexts
- black: light surfaces; white or mint: dark surfaces
- lower triangle: gray in every variant
- fold gaps and letter counters: transparent negative space
- app icon: opaque green tile; transparency outside its rounded corners
- PNG files retain native raster resolution; larger or vector exports are not included
- layout clear space: at least one-quarter boat content height; 37 pixels at native export size
- export padding is separate from layout clear space
- reviewed minimums and failed targets are recorded in the size review below

Boat separator bodies measure `11.3–11.9px` perpendicular to their facing edges: `3.9–4.1%` of the `292px` visible width.
That already fits the [family separator rule](../../../../../conventions/design/identity/logo-system.md#internal-separators).
The boat geometry, gray fold and seven unendorsed exports remain unchanged.

---

## Exports

All ten files are cropped RGBA PNGs, with eight pixels of transparent safety padding.
The app icon uses a square canvas and centers the original tile without stretching it.
The approved final set is saved with two review sheets and a [complete manifest](manifest.json).
The manifest records every asset's dimensions and SHA-256, source hashes, exact parent provenance and composition.

| Asset | Size | Files |
|---|---|---|
| Default wordmark | 1173 × 163 | [Black](soft-folds/wheelhouse-wordmark.png) · [White](soft-folds/wheelhouse-wordmark-white.png) · [Mint](soft-folds/wheelhouse-wordmark-mint.png) |
| Wordmark with attribution | 1173 × 209 | [Black](soft-folds/wheelhouse-wordmark-by-wow2.png) · [White](soft-folds/wheelhouse-wordmark-by-wow2-white.png) · [Mint](soft-folds/wheelhouse-wordmark-by-wow2-mint.png) |
| Standalone boat | 308 × 163 | [Black](soft-folds/wheelhouse-mark.png) · [White](soft-folds/wheelhouse-mark-white.png) · [Mint](soft-folds/wheelhouse-mark-mint.png) |
| App icon | 312 × 312 | [Green tile](soft-folds/wheelhouse-app-icon.png) |

![Export preview on light and dark surfaces](preview.png)

---

## Size review

The [review sheet](size-review.png) renders one image pixel per CSS pixel at 100% zoom.
Black artwork is reviewed on white; white and mint primary lettering are reviewed on dark surfaces.
The enlarged symbol samples use nearest-neighbor pixels to expose closed fold gaps.

| Composition | Reviewed minimum | Reviewed targets and result |
|---|---|---|
| Primary | `24px` total image height | `24`, `32`, `40px`: lettering and boat remain readable |
| Standalone boat | `20 × 20px` square slot | `16px`: folds merge; `20`, `24`, `32px`: four facets remain distinct |
| Endorsed | `128px` total image height; `718px` width | `96`, `112px`: below parent rules; `128`, `144`, native `209px`: pass |
| App tile | `64 × 64px` | Readable square avatar; platform-specific masks remain untested |

- symbol geometry check: connected alpha ≥ `128` regions, each at least two pixels
- at `16px`, the four boat facets merge into one such region; the source artwork is unchanged
- at `128px`, parent visible-symbol width is `31px`; equivalent padded parent-primary height is about `24.1px`
- both parent values meet the `24px` minimums in the [WoW2 guide](../../../../../docs/brand/wow2/brand.md)
- `112px` retains sufficient symbol width but fails the parent-primary height rule; `96px` fails both
- use the unendorsed identity below the endorsed minimum; do not enlarge only the parent inside the saved composition
- these are reviewed 1× raster minimums, not user-recognition testing or platform-installation certification
- review captions use Pillow's bundled font; neither sheet modifies the ten artwork PNGs

---

## Source and processing

- [Selected artwork](source/soft-folds-selection.png): the approved comparison board's upper row, cropped without resampling
- [Attribution lettering](source/by-wow2-attribution.png): only its original `by` letters are used
- [Approved parent inputs and hashes](source/wow2/provenance.json): vendored copies of the canonical WoW2 Panels primary assets
- [Navy parent](source/wow2/wow2-primary-navy.png) and [white parent](source/wow2/wow2-primary-white.png): copied byte-for-byte
- exports use direct pixel processing of these sources; no generated redraws are included
- opaque source pixels retain their original colors in the black and green exports
- antialiased boundary pixels are unmatted against the original white page
- standalone boats use the same pixels as the default wordmark
- neutral `by`: native `38 × 31`, extracted from `(0, 0, 52, 37)` of the original `160 × 37` source
- parent inputs: `864 × 192`; alpha content `(8, 8, 856, 184)` is scaled to `173 × 36`
- endorsement begins at `x = 311`, beneath the first h; vertical gap `10` pixels; `by`-to-parent gap `12` pixels
- parent assets retain their aspect ratio, lettering and internal spacing through a single proportional raster resize
- exporter verifies parent SHA-256 hashes before writing outputs; no old `wow2` lettering is composed
- white and mint variants share the black export's exact alpha channel
- white foreground: `#ffffff`; mint foreground: `#a6d4bd`; gray lower facet retained
- mint applies only to Wheelhouse; its neutral `by` and parent artwork remain white
- all exported PNGs were checked for transparent margins and intact opaque interiors
- `preview.png` and `size-review.png` are presentation sheets, not transparent logo assets

These files are the saved brand assets. Application integration is separate.

---

## Application

The [app export script](../../engineering/scripts/export-app-icons.py) derives the web app's files from the exports
above, after checking each against the manifest; an isolated replay matches byte for byte.

| Placement | Source | App file |
|---|---|---|
| Top bar (26px), sign-in (40px), first-load splash | Default wordmark, black and white | `src/presentation/shell/assets/wordmark-{dark,light}-ink.png` (96px high, 64-colour palette) |
| Browser tab, light and dark schemes | Standalone boat, black and white | `public/favicon.png`, `public/favicon-dark.png` (64px square) |
| Home-screen icon | App tile, green extended to a full-bleed square | `public/apple-touch-icon.png` (180px) |

- the header and sign-in show the wordmark without endorsement; the image carries the accessible name
- the 16px tab slot sits below the reviewed `20px` symbol minimum; a micro symbol would need design review

---

## Icon set

The [icon export script](../../engineering/scripts/export-icon-set.py) copies four approved exports byte for byte,
after checking each against the manifest, into [`icons/`](icons) under the
[product icons](../../../../../conventions/design/identity/product-icons.md) names. The console and any other
Wheelhouse consumer resolve a product's icon from these paths.

| Icon file | Approved export |
|---|---|
| `icons/short-colored-light.png` | Standalone boat, black |
| `icons/short-colored-dark.png` | Standalone boat, white |
| `icons/full-colored-light.png` | Default wordmark, black |
| `icons/full-colored-dark.png` | Default wordmark, white |

- `colored` is the approved artwork: ink plus the gray lower facet, which every approved variant keeps
- `monochrome` variants are not shipped: a one-ink export recolors the gray fold, which needs a brand decision
- a `monochrome` request falls back to the `colored` file of the same theme
- mint, attributed wordmarks and the app tile have no slot in the icon matrix and stay brand exports
- Wheelhouse has one service, so it carries no `services/` folder; its service shows these icons
- the web app's tab and touch icons remain the separate `public/` files in [Application](#application)

---

## Continuation

- [Identity adoption handoff](handoff/handoff.md): adopted convention, exact parent provenance and remaining work
- [Exploration archive](exploration/exploration.md): previous directions and complete comparison boards
- [Export script](../../engineering/scripts/export-brand-logos.py): reproduces all ten PNGs, both review sheets and the manifest
- [Icon export script](../../engineering/scripts/export-icon-set.py): reproduces the four `icons/` files from the manifest-checked exports
