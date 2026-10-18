import type { CalendarCell } from "@/domain/deployments";

/**
 * The calendar's fill per tone and step (0 for no rollout, then 1, 2–3, 4–6 and 7+), shared by the calendar and its
 * key so the two never drift.
 */
export const CellFills: Readonly<Record<CalendarCell["tone"], readonly string[]>> = {
  empty: ["bg-muted", "bg-muted", "bg-muted", "bg-muted", "bg-muted"],
  success: ["bg-muted", "bg-success/30", "bg-success/55", "bg-success/80", "bg-success"],
  danger: ["bg-muted", "bg-destructive/30", "bg-destructive/55", "bg-destructive/80", "bg-destructive"],
  warning: ["bg-muted", "bg-warning/35", "bg-warning/60", "bg-warning/85", "bg-warning"],
};
