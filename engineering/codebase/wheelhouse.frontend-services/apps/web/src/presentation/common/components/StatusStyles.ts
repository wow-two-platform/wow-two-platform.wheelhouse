import type { Component } from "vue";
import {
  Activity,
  CircleArrowUp,
  CircleCheck,
  CircleDashed,
  CircleHelp,
  CircleMinus,
  CirclePause,
  CirclePlus,
  CircleX,
  Clock,
  Contrast,
  Crosshair,
  Hourglass,
  ListOrdered,
  LoaderCircle,
  Server,
  Shield,
  ShieldX,
  SkipForward,
  Snowflake,
  TriangleAlert,
  Undo2,
  Unplug,
  UserRound,
  Workflow,
} from "lucide-vue-next";

import {
  StatusTones,
  type DeployState,
  type ServiceResult,
  type StatusState,
  type StatusTone,
} from "@/domain/common";

/** Each state's icon: every state has its own shape, so colour is never the only signal. */
export const StatusIcons: Readonly<Record<StatusState, Component>> = {
  healthy: CircleCheck,
  succeeded: CircleCheck,
  ci: Workflow,
  deploying: LoaderCircle,
  failed: CircleX,
  degraded: TriangleAlert,
  mixed: Contrast,
  none: CircleDashed,
  stopped: CirclePause,
  unknown: CircleHelp,
};

/**
 * Each deployment state's icon — what happened; the colour says how much attention it needs. Partial's half-filled
 * circle, Not applied's minus and Unknown's question mark read in greyscale too.
 */
export const DeployStateIcons: Readonly<Record<DeployState, Component>> = {
  in_progress: LoaderCircle,
  succeeded: CircleCheck,
  degraded: TriangleAlert,
  rolled_back: Undo2,
  partial: Contrast,
  failed: CircleX,
  not_applied: CircleMinus,
  unknown: CircleHelp,
};

/** An in-progress deployment's icon by its sub-state: a clock while scheduled, an hourglass while queued or waiting. */
export function deployStateIcon(state: DeployState, substate: string | null): Component {
  if (state === "in_progress" && substate === "scheduled") return Clock;
  if (state === "in_progress" && (substate === "queued" || substate === "waiting")) return Hourglass;
  return DeployStateIcons[state];
}

/** Each service result's glyph in a service strip, a map node or the services table. */
export const ServiceResultIcons: Readonly<Record<ServiceResult, Component>> = {
  updated: CircleArrowUp,
  new: CirclePlus,
  unchanged: CircleMinus,
  not_reached: CircleDashed,
  restored: Undo2,
  failed: CircleX,
  affected: TriangleAlert,
  waiting: Hourglass,
  starting: LoaderCircle,
  unknown: CircleHelp,
};

/** Each cause kind's icon, before its message: where the trouble started. */
export const CauseIcons: Readonly<Record<string, Component>> = {
  service: Crosshair,
  step: ListOrdered,
  gate: Shield,
  freeze: Snowflake,
  validation: ShieldX,
  platform: Server,
  busy: Activity,
  runner: Unplug,
  operator: UserRound,
  queue: SkipForward,
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
    "border-dashed border-border-strong bg-transparent text-subtle-foreground [&_svg]:text-subtle-foreground [&_[data-detail]]:text-subtle-foreground [&_[data-env]]:font-medium",
};

/** A state's classes: its tone's, except CI, which is calm with a muted icon since nothing here deployed it. */
export function statusClass(state: StatusState): string {
  return state === "ci"
    ? "border-border bg-card text-foreground [&_svg]:text-muted-foreground"
    : StatusToneClass[StatusTones[state]];
}

/**
 * A deployment chip's classes: the attention's tone, a calm success with only its icon green, Not applied in a dashed
 * outline and Unknown in a dotted one, whatever the tone.
 */
export function deployStateClass(reading: { readonly state: DeployState; readonly tone: StatusTone }): string {
  const tone =
    reading.tone === "calm" && reading.state !== "succeeded"
      ? "border-border bg-card text-foreground [&_svg]:text-muted-foreground"
      : StatusToneClass[reading.tone];
  return `${tone} ${reading.state === "not_applied" ? "border-dashed" : reading.state === "unknown" ? "border-dotted" : ""}`;
}

/** The icon's motion: the deploying spinner turns slowly, and holds still under reduced motion. */
export function statusIconClass(state: StatusState | DeployState | ServiceResult): string {
  return state === "deploying" || state === "in_progress" || state === "starting"
    ? "animate-spin [animation-duration:2s] motion-reduce:animate-none"
    : "";
}
