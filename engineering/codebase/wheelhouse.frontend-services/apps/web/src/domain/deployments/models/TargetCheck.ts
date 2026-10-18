import type { TargetState } from './TargetState';

/** One read-only readiness probe; `detail` is display-safe text. */
export interface TargetCheckItem {
  name: string;
  ok: boolean;
  detail: string;
}

/** A target's readiness, optionally against one release. */
export interface TargetCheck {
  targetId: string;
  ok: boolean;
  checks: TargetCheckItem[];
  state?: TargetState;
}
