<script lang="ts">
import type { DeploymentServiceResult } from "@/domain/deployments";

/** Defines a deployment's service strip. */
export interface ServiceStripProps {
  /** Each service's result in rollout order; null when the target did not report them. */
  readonly services: readonly DeploymentServiceResult[] | null;
  /** Says it in words (`2 of 3 updated`) at every width, as a phone does. */
  readonly isWords?: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";

import { ServiceResultTones } from "@/domain/common";
import { DeploymentStates } from "@/domain/deployments";
import { ServiceResultIcons, statusIconClass } from "@/presentation/common/components";

/**
 * Which services a deployment touched and how each ended: one glyph per service in rollout order, its shape the result
 * (updated, new, unchanged, not reached, restored, failed, affected, waiting, starting) and its colour the result's
 * tone; the service where the trouble started wears a ring. On a phone, or with `isWords`, the strip says it in words:
 * `2 of 3 updated`. Every glyph names its service and result.
 */
defineOptions({ name: "ServiceStrip" });
const props = withDefaults(defineProps<ServiceStripProps>(), { isWords: false });
defineSlots<{}>();

const summary = computed(() => DeploymentStates.stripSummary(props.services));
const label = computed(() => props.services?.map((service) => DeploymentStates.serviceLabel(service)).join("; ") ?? "");

/** A glyph's colour by its result's tone. @internal */
const ToneText: Readonly<Record<string, string>> = {
  calm: "text-success",
  info: "text-info",
  warning: "text-warning",
  danger: "text-destructive",
  quiet: "text-muted-foreground",
  empty: "text-subtle-foreground",
};
const OriginRing: Readonly<Record<string, string>> = {
  danger: "ring-destructive/70",
  warning: "ring-warning/70",
  calm: "ring-border-strong",
  info: "ring-info/60",
  quiet: "ring-border-strong",
  empty: "ring-border-strong",
};
</script>

<template>
  <span v-if="props.services?.length" class="inline-flex min-w-0 items-center">
    <span
      :class="['items-center gap-0.5', props.isWords ? 'hidden' : 'hidden md:inline-flex']"
      role="img"
      :aria-label="`Services: ${label}`"
      :title="label"
    >
      <span
        v-for="service in props.services"
        :key="service.service"
        :class="[
          'inline-flex size-5 shrink-0 items-center justify-center rounded-full',
          ToneText[ServiceResultTones[service.result]],
          service.origin ? `ring-2 ${OriginRing[ServiceResultTones[service.result]]}` : '',
        ]"
        :title="DeploymentStates.serviceLabel(service)"
      >
        <component
          :is="ServiceResultIcons[service.result]"
          :size="14"
          aria-hidden="true"
          :class="statusIconClass(service.result)"
        />
      </span>
    </span>
    <span :class="['text-xs text-muted-foreground tabular-nums', props.isWords ? '' : 'md:hidden']">{{ summary }}</span>
  </span>
  <span v-else class="text-xs text-muted-foreground" aria-label="Services not reported">—</span>
</template>
