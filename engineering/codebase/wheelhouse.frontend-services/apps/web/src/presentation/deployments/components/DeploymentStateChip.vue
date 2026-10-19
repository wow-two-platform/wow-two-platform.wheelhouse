<script lang="ts">
import type { DeploymentStateReading } from "@/domain/deployments";

/** Defines a deployment's state chip. */
export interface DeploymentStateChipProps {
  /** The deployment in the status taxonomy (`DeploymentStates.of`). */
  readonly reading: DeploymentStateReading;
  /** Writes the cause on a second line: its kind's icon, where it started and why. Default `false`. */
  readonly hasCause?: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { LoaderCircle } from "lucide-vue-next";

import { DeploymentStates } from "@/domain/deployments";
import { CauseIcons, deployStateClass, deployStateIcon } from "@/presentation/common/components";

/**
 * The one chip every deployment wears: the state's icon (what happened), its word, then `· sub-state`, in the colour of
 * the attention it needs — so `Not applied · check failed` is amber while `Not applied · superseded` stays neutral. Not
 * applied wears a dashed outline and Unknown a dotted one, so the chip reads in greyscale too. A second line can carry
 * the cause: its kind's icon, where it started, and why. Without that line the cause rides in the title and the
 * accessible name.
 */
defineOptions({ name: "DeploymentStateChip" });
const props = withDefaults(defineProps<DeploymentStateChipProps>(), { hasCause: false });
defineSlots<{}>();

const icon = computed(() => deployStateIcon(props.reading.state, props.reading.substate));
const label = computed(() => DeploymentStates.label(props.reading));
/** The cause's line: `jobs · health check failed`, its origin left out when the message already names it. */
const causeLine = computed(() => {
  const cause = props.reading.cause;
  if (!cause?.message) return null;
  return cause.origin && !cause.message.startsWith(cause.origin) ? `${cause.origin} · ${cause.message}` : cause.message;
});
const causeIcon = computed(() => (props.reading.cause ? CauseIcons[props.reading.cause.kind] : undefined));
</script>

<template>
  <span class="inline-flex max-w-full min-w-0 flex-col items-start gap-1">
    <span
      class="inline-flex h-6 max-w-full shrink-0 items-center gap-1.5 rounded-full border ps-1.5 pe-2.5 text-xs whitespace-nowrap"
      :class="deployStateClass(props.reading)"
      :title="label"
      :aria-label="label"
      role="img"
    >
      <component
        :is="icon"
        :size="14"
        aria-hidden="true"
        class="shrink-0"
        :class="icon === LoaderCircle ? 'animate-spin [animation-duration:2s] motion-reduce:animate-none' : ''"
      />
      <span class="shrink-0 font-medium">{{ props.reading.word }}</span>
      <span v-if="props.reading.detail" data-detail class="min-w-0 truncate text-muted-foreground">· {{ props.reading.detail }}</span>
    </span>
    <span v-if="props.hasCause && causeLine" class="flex max-w-full min-w-0 items-start gap-1 text-xs text-muted-foreground">
      <component :is="causeIcon" v-if="causeIcon" :size="12" aria-hidden="true" class="mt-0.5 shrink-0" />
      <span class="min-w-0 break-words">{{ causeLine }}</span>
    </span>
  </span>
</template>
