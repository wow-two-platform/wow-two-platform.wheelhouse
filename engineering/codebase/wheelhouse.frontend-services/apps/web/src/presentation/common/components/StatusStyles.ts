import type { Component } from "vue";
import {
  Ban,
  CircleCheck,
  CircleDashed,
  CircleHelp,
  CircleX,
  LoaderCircle,
  TriangleAlert,
  Unplug,
  Workflow,
} from "lucide-vue-next";

import { StatusTones, type StatusState, type StatusTone } from "@/domain/common";

/** Each state's icon: every state has its own shape, so colour is never the only signal. */
export const StatusIcons: Readonly<Record<StatusState, Component>> = {
  healthy: CircleCheck,
  succeeded: CircleCheck,
  ci: Workflow,
  deploying: LoaderCircle,
  failed: CircleX,
  degraded: TriangleAlert,
  interrupted: Unplug,
  refused: Ban,
  none: CircleDashed,
  unknown: CircleHelp,
};

/**
 * Each tone's outline, fill and text on a pill or a count tile. Calm stays neutral with only its icon green, so problems
 * stand out; `[data-detail]` is the muted text after the label, which takes the tone's colour on a loud pill.
 */
export const StatusToneClass: Readonly<Record<StatusTone, string>> = {
  calm: "border-border bg-card text-foreground [&_svg]:text-success",
  info: "border-info/40 bg-info-soft text-info-soft-foreground [&_svg]:text-info-soft-foreground [&_[data-detail]]:text-info-soft-foreground",
  warning:
    "border-warning/45 bg-warning-soft text-warning-soft-foreground [&_svg]:text-warning-soft-foreground [&_[data-detail]]:text-warning-soft-foreground",
  danger:
    "border-destructive/45 bg-destructive-soft text-destructive-soft-foreground [&_svg]:text-destructive-soft-foreground [&_[data-detail]]:text-destructive-soft-foreground",
  quiet: "border-border bg-transparent text-muted-foreground [&_svg]:text-muted-foreground",
  empty:
    "border-dashed border-border-strong bg-transparent text-subtle-foreground [&_svg]:text-subtle-foreground [&_[data-env]]:font-medium",
};

/** A state's classes: its tone's, except CI, which is calm with a muted icon since nothing here deployed it. */
export function statusClass(state: StatusState): string {
  return state === "ci"
    ? "border-border bg-card text-foreground [&_svg]:text-muted-foreground"
    : StatusToneClass[StatusTones[state]];
}

/** The icon's motion: the deploying spinner turns slowly, and holds still under reduced motion. */
export function statusIconClass(state: StatusState): string {
  return state === "deploying" ? "animate-spin [animation-duration:2s] motion-reduce:animate-none" : "";
}
