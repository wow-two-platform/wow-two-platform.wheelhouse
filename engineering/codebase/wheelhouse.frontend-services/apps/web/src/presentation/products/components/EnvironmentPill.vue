<script lang="ts">
import type { EnvironmentPill } from "@/domain/portfolio";

/** One environment's pill on a product card or row. */
export interface EnvironmentPillProps {
  /** The pill; null draws its placeholder while the targets load. */
  readonly pill: EnvironmentPill | null;
  /** The environment a placeholder names. */
  readonly environment?: string;
}
</script>
<script setup lang="ts">
import { ReleaseSets } from "@/domain/deployments";
import { computed } from "vue";
import { Clock, Eye, X } from "lucide-vue-next";
import { RelativeTime } from "@wow-two-beta/fe/presentation/display";
import { SkeletonState } from "@wow-two-beta/fe/presentation/feedback";
import { HoverCard, HoverCardContent, HoverCardTrigger } from "@wow-two-beta/fe/presentation/overlays";
import { DeployStateWords, SubstateWords } from "@/domain/common";
import { PillState, PillStateWords } from "@/domain/portfolio";
import { statusClass, StatusIcons, statusIconClass } from "@/presentation/common/components";

/**
 * An outlined pill per environment: a status icon, the environment's name, then its serving version when calm or its
 * state word when not; "not deployed" is dashed and names nothing more. Every state differs by icon shape and the
 * non-calm ones by a word, so colour is never the only signal; states, words, tones and icons are the console's status
 * vocabulary, shared with every deployment's pill. A pill shows the environment, not the attempt: a mix of releases
 * reads `mixed` (`2/3 on 0.4.0`), and one corner marker at most says more — a cross when the last attempt changed
 * nothing but needs a look, a clock while a deploy waits, an eye while the serving release is watched. Hovering or
 * focusing it opens its details: the cause of a degraded, mixed or failed state (`rolled back to v1.2.0`), the release
 * serving, and the latest rollout's release, state, time and actor.
 */
defineOptions({ name: "EnvironmentPill" });
const props = withDefaults(defineProps<EnvironmentPillProps>(), { environment: "" });
defineSlots<{}>();

const word = computed(() => (props.pill ? PillStateWords[props.pill.state] : ""));
const label = computed(() => {
  const pill = props.pill;
  if (!pill) return "";
  const state = pill.state === PillState.Degraded && pill.cause ? `${word.value}, ${pill.cause}` : word.value;
  const base = pill.release && pill.state !== PillState.None ? `${pill.environment}: ${state} · ${ReleaseSets.text(pill.release)}` : `${pill.environment}: ${state}`;
  return marker.value ? `${base}; ${marker.value.words}` : base;
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
  if (pill.cause) return pill.cause.charAt(0).toUpperCase() + pill.cause.slice(1);
  return null;
});
/** The corner marker's icon, colour and words. @internal */
const marker = computed(() => {
  switch (props.pill?.marker) {
    case "attempt":
      return { icon: X, tone: "border-warning/60 bg-warning-soft", glyph: "text-warning-soft-foreground!", words: "the last attempt changed nothing and needs a look" };
    case "waiting":
      return { icon: Clock, tone: "border-info/50 bg-info-soft", glyph: "text-info-soft-foreground!", words: "a deploy is waiting" };
    case "watching":
      return { icon: Eye, tone: "border-info/50 bg-info-soft", glyph: "text-info-soft-foreground!", words: "being watched after its deploy" };
    default:
      return null;
  }
});
/** A rollout's state in words: `Partial · partly rolled back`. @internal */
function stateWords(deploy: { state: keyof typeof DeployStateWords; substate: string | null }): string {
  const sub = deploy.substate ? (SubstateWords[deploy.substate] ?? deploy.substate.replaceAll("_", " ")) : null;
  return sub ? `${DeployStateWords[deploy.state]} · ${sub}` : DeployStateWords[deploy.state];
}
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
        :class="statusClass(props.pill.state)"
      >
        <component
          :is="StatusIcons[props.pill.state]"
          :size="14"
          aria-hidden="true"
          class="shrink-0"
          :class="statusIconClass(props.pill.state)"
        />
        <span data-env aria-hidden="true" class="font-semibold">{{ props.pill.environment }}</span>
        <span
          v-if="props.pill.detail"
          data-detail
          aria-hidden="true"
          class="min-w-0 truncate text-muted-foreground tabular-nums"
          >{{ props.pill.detail }}</span
        >
        <span
          v-if="marker"
          :class="['absolute -top-1.5 -right-1.5 flex size-3.5 items-center justify-center rounded-full border', marker.tone]"
          aria-hidden="true"
          ><component :is="marker.icon" :size="9" :stroke-width="3" :class="marker.glyph"
        /></span>
      </span>
    </HoverCardTrigger>
    <HoverCardContent class="w-72 p-3 text-xs">
      <p class="flex items-center gap-1.5 text-sm font-semibold">
        <component :is="StatusIcons[props.pill.state]" :size="14" aria-hidden="true" />{{ props.pill.environment }}
        <span class="font-normal text-muted-foreground">{{ word }}</span>
      </p>
      <p v-if="note" class="mt-1 text-muted-foreground">{{ note }}</p>
      <dl class="mt-2 grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1">
        <template v-if="props.pill.release && props.pill.state !== PillState.None">
          <dt class="text-muted-foreground">Serving</dt>
          <dd class="truncate font-medium tabular-nums">
            {{ ReleaseSets.text(props.pill.release) }}
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
            <span class="font-medium tabular-nums">{{ ReleaseSets.text(props.pill.lastDeploy.release) || "Build" }}</span>
            · {{ stateWords(props.pill.lastDeploy) }}
            · <RelativeTime :value="props.pill.lastDeploy.submittedAt" />
            <template v-if="props.pill.lastDeploy.actor"> · {{ props.pill.lastDeploy.actor }}</template>
          </dd>
          <template v-if="props.pill.lastDeploy.reason">
            <dt class="text-muted-foreground">Why</dt>
            <dd class="line-clamp-3">{{ props.pill.lastDeploy.reason }}</dd>
          </template>
        </template>
        <template v-if="props.pill.marker === 'attempt' && props.pill.lastAttempt">
          <dt class="text-muted-foreground">Last try</dt>
          <dd class="min-w-0">
            <span class="font-medium tabular-nums">{{ ReleaseSets.text(props.pill.lastAttempt.release) || "Build" }}</span>
            · {{ stateWords(props.pill.lastAttempt) }} · <RelativeTime :value="props.pill.lastAttempt.submittedAt" />
            <span v-if="props.pill.lastAttempt.reason" class="mt-0.5 line-clamp-2 block text-muted-foreground">{{
              props.pill.lastAttempt.reason
            }}</span>
          </dd>
        </template>
        <template v-if="!showsLatest && props.pill.running?.actor">
          <dt class="text-muted-foreground">By</dt>
          <dd class="truncate">{{ props.pill.running.actor }}</dd>
        </template>
      </dl>
    </HoverCardContent>
  </HoverCard>
</template>
