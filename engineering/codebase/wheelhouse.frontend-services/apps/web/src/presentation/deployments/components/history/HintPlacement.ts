/** A box on screen, in viewport pixels. */
export interface HintRect {
  readonly left: number;
  readonly top: number;
  readonly width: number;
  readonly height: number;
}

/** Where a hint lands: its top-left corner in viewport pixels, and the side of its anchor it took. */
export interface HintPosition {
  readonly left: number;
  readonly top: number;
  readonly side: "top" | "bottom";
}

/**
 * Collision rules for a hint drawn in the top layer beside an anchor: above it when there is room, else below; centred
 * on it, but slid sideways to stay `margin` inside the viewport, so it is never clipped at a panel's or a screen's edge.
 */
export const HintPlacement = {
  place(anchor: HintRect, hint: Pick<HintRect, "width" | "height">, viewport: Pick<HintRect, "width" | "height">, gap = 6, margin = 8): HintPosition {
    const above = anchor.top - gap - hint.height;
    const below = anchor.top + anchor.height + gap;
    const side = above >= margin || below + hint.height > viewport.height - margin ? "top" : "bottom";
    const top = side === "top" ? Math.max(margin, above) : below;
    const centred = anchor.left + anchor.width / 2 - hint.width / 2;
    const left = Math.min(Math.max(margin, centred), Math.max(margin, viewport.width - margin - hint.width));
    return { left, top, side };
  },
} as const;
