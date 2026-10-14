# scripts/

*Last updated: 2026-10-14*

Dev / ops scripts for Wheelhouse. One per task; this is the index.

- `export-brand-logos.py` — reproduces the brand PNGs, review sheets and manifest in `product/brand/`.
- `export-app-icons.py` — derives the web app's wordmarks, favicons and touch icon from those PNGs.
- `export-icon-set.py` — copies four of those PNGs to `product/brand/icons/` under the wow-two product icons names.

The frontend deploy script lives with the workspace: `codebase/wheelhouse.frontend-services/scripts/deploy.mjs`.
