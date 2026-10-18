<script lang="ts">
import type { EnvironmentPill } from "@/domain/portfolio";

/** One environment's pill on a product card or row. */
export interface EnvironmentPillProps {
  /** The pill; null draws its placeholder while the targets load. */
  readonly pill: EnvironmentPill | null;
  /** The environment a placeholder names. */
  readonly environment?: string;
}

/** @internal Each state's outline, fill and text. Healthy and CI stay neutral, so problems stand out. */
const StateClass = {
  healthy: "border-border bg-card text-foreground [&_svg]:text-success",
  ci: "border-border bg-card text-foreground [&_svg]:text-muted-foreground",
  deploying:
    "border-info/40 bg-info-soft text-info-soft-foreground [&_svg]:text-info-soft-foreground [&_[data-detail]]:text-info-soft-foreground",
  failed:
    "border-destructive/45 bg-destructive-soft text-destructive-soft-foreground [&_svg]:text-destructive-soft-foreground [&_[data-detail]]:text-destructive-soft-foreground",
  "rolled-back":
    "border-warning/45 bg-warning-soft text-warning-soft-foreground [&_svg]:text-warning-soft-foreground [&_[data-detail]]:text-warning-soft-foreground",
  degraded:
    "border-warning/45 bg-warning-soft text-warning-soft-foreground [&_svg]:text-warning-soft-foreground [&_[data-detail]]:text-warning-soft-foreground",
  none: "border-dashed border-border-strong bg-transparent text-subtle-foreground [&_svg]:text-subtle-foreground [&_[data-env]]:font-medium",
  unknown: "border-border bg-transparent text-muted-foreground [&_svg]:text-muted-foreground",
} as const;
</script>
<script setup lang="ts">
import { computed } from "vue";
import {
  CircleCheck,
  CircleDashed,
  CircleHelp,
  CircleX,
  LoaderCircle,
  TriangleAlert,
  Undo2,
  Workflow,
} from "lucide-vue-next";
import { RelativeTime } from "@wow-two-beta/fe/presentation/display";
import { SkeletonState } from "@wow-two-beta/fe/presentation/feedback";
import { HoverCard, HoverCardContent, HoverCardTrigger } from "@wow-two-beta/fe/presentation/overlays";
import { PillState, PillStateWords } from "@/domain/portfolio";

/**
 * An outlined pill per environment: a status icon, the environment's name, then its serving version when calm or its
 * state word when not; "not deployed" is dashed and names nothing more. Every state differs by icon shape and the
 * non-calm ones by a word, so colour is never the only signal. Hovering or focusing it opens its details: the release
 * serving, and the latest rollout's release, status, time and actor.
 */
defineOptions({ name: "EnvironmentPill" });
const props = withDefaults(defineProps<EnvironmentPillProps>(), { environment: "" });
defineSlots<{}>();

const Icons = {
  healthy: CircleCheck,
  ci: Workflow,
  deploying: LoaderCircle,
  failed: CircleX,
  "rolled-back": Undo2,
  degraded: TriangleAlert,
  none: CircleDashed,
  unknown: CircleHelp,
} as const;

const word = computed(() => (props.pill ? PillStateWords[props.pill.state] : ""));
const label = computed(() => {
  const pill = props.pill;
  if (!pill) return "";
  return pill.release && pill.state !== PillState.None ? `${pill.environment}: ${word.value} · ${pill.release}` : `${pill.environment}: ${word.value}`;
});
/** The line that explains a calm or empty state in the details. @internal */
const note = computed(() => {
  const pill = props.pill;
  if (!pill) return null;
  if (pill.state === PillState.Ci)
    return pill.release
      ? "Deployed by its own CI; this console runs this version."
      : "Deployed by its own CI; this console reads only the instance it runs as.";
  if (pill.state === PillState.None) return pill.hasTarget ? "Nothing deployed yet." : "No target in this environment.";
  if (pill.state === PillState.Unknown) return "The target could not be read.";
  if (pill.state === PillState.Degraded) return "Serving, with restarts or host pressure to look at.";
  return null;
});
const showsLatest = computed(
  () => props.pill?.lastDeploy && props.pill.state !== PillState.Ci && props.pill.lastDeploy.id !== props.pill.running?.id,
);
</script>
<template>
  <span
    v-if="!props.pill"
    class="inline-flex h-6 shrink-0 items-center gap-1.5 rounded-full border border-border px-2 text-xs text-muted-foreground"
    aria-hidden="true"
    ><SkeletonState shape="circle" class="size-3.5" /><span class="font-semibold">{{ props.environment }}</span
    ><SkeletonState class="h-3 w-8 rounded"
  /></span>
  <HoverCard v-else :open-delay="250" :close-delay="150" placement="top">
    <HoverCardTrigger as-child>
      <span
        tabindex="0"
        :aria-label="label"
        class="relative z-10 inline-flex h-6 max-w-full shrink-0 cursor-default items-center gap-1.5 rounded-full border ps-1.5 pe-2.5 text-xs whitespace-nowrap outline-none focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring"
        :class="StateClass[props.pill.state]"
      >
        <component
          :is="Icons[props.pill.state]"
          :size="14"
          aria-hidden="true"
          class="shrink-0"
          :class="props.pill.state === PillState.Deploying ? 'animate-spin [animation-duration:2s]' : ''"
        />
        <span data-env aria-hidden="true" class="font-semibold">{{ props.pill.environment }}</span>
        <span
          v-if="props.pill.detail"
          data-detail
          aria-hidden="true"
          class="min-w-0 truncate text-muted-foreground tabular-nums"
          >{{ props.pill.detail }}</span
        >
      </span>
    </HoverCardTrigger>
    <HoverCardContent class="w-72 p-3 text-xs">
      <p class="flex items-center gap-1.5 text-sm font-semibold">
        <component :is="Icons[props.pill.state]" :size="14" aria-hidden="true" />{{ props.pill.environment }}
        <span class="font-normal text-muted-foreground">{{ word }}</span>
      </p>
      <p v-if="note" class="mt-1 text-muted-foreground">{{ note }}</p>
      <dl class="mt-2 grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1">
        <template v-if="props.pill.release && props.pill.state !== PillState.None">
          <dt class="text-muted-foreground">Serving</dt>
          <dd class="truncate font-medium tabular-nums">
            {{ props.pill.release }}
            <RelativeTime
              v-if="props.pill.running?.completedAt ?? props.pill.running?.submittedAt"
              :value="(props.pill.running?.completedAt ?? props.pill.running?.submittedAt)!"
              class="font-normal text-muted-foreground"
            />
          </dd>
        </template>
        <template v-if="showsLatest && props.pill.lastDeploy">
          <dt class="text-muted-foreground">Latest</dt>
          <dd class="min-w-0">
            <span class="font-medium tabular-nums">{{ props.pill.lastDeploy.release ?? "Build" }}</span>
            · {{ props.pill.lastDeploy.status.replaceAll("_", " ") }}
            · <RelativeTime :value="props.pill.lastDeploy.submittedAt" />
            <template v-if="props.pill.lastDeploy.actor"> · {{ props.pill.lastDeploy.actor }}</template>
          </dd>
          <template v-if="props.pill.lastDeploy.reason">
            <dt class="text-muted-foreground">Why</dt>
            <dd class="line-clamp-3">{{ props.pill.lastDeploy.reason }}</dd>
          </template>
        </template>
        <template v-else-if="props.pill.running?.actor">
          <dt class="text-muted-foreground">By</dt>
          <dd class="truncate">{{ props.pill.running.actor }}</dd>
        </template>
      </dl>
    </HoverCardContent>
  </HoverCard>
</template>
