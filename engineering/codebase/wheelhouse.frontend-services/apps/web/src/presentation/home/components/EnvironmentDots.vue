<script lang="ts">
import type { EnvironmentReading } from '@/domain/overview';

/** A product's dev and prod health as two labelled dots. */
export interface EnvironmentDotsProps {
  /** The readings in promotion order; undefined draws placeholders during a first load. */
  readonly environments: readonly EnvironmentReading[] | undefined;
}

/** @internal Each health's dot. A missing environment is a hollow ring; an unread one a faint dot. */
const DotClass = {
  missing: 'border border-border-strong bg-transparent',
  unknown: 'bg-muted-foreground/35',
  idle: 'bg-muted-foreground/70',
  healthy: 'bg-success',
  warning: 'bg-warning',
  failing: 'bg-destructive',
} as const;
</script>
<script setup lang="ts">
import { EnvironmentHealthLabels } from '@/domain/overview';
import { EnvironmentOrder } from '@/domain/targets';
import { SkeletonState } from '@wow-two-beta/fe/presentation/feedback';

/** Shows dev and prod side by side, each named, with a dot for its health; colour is never the only signal. */
defineOptions({ name: 'EnvironmentDots' });
const props = defineProps<EnvironmentDotsProps>();
defineSlots<{}>();
</script>
<template>
  <ul role="list" class="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs" aria-label="Environments">
    <template v-if="props.environments">
      <li
        v-for="reading in props.environments"
        :key="reading.environment"
        class="inline-flex items-center gap-1.5"
        :class="reading.health === 'missing' ? 'text-subtle-foreground' : 'text-muted-foreground'"
        :title="`${reading.environment}: ${EnvironmentHealthLabels[reading.health]}`"
      >
        <span class="size-2 shrink-0 rounded-full" :class="DotClass[reading.health]" aria-hidden="true" />
        <span>{{ reading.environment }}</span>
        <span class="sr-only">: {{ EnvironmentHealthLabels[reading.health] }}</span>
      </li>
    </template>
    <template v-else>
      <li v-for="environment in EnvironmentOrder" :key="environment" class="inline-flex items-center gap-1.5">
        <SkeletonState shape="circle" class="size-2" />
        <SkeletonState class="h-3 w-7 rounded" />
      </li>
    </template>
  </ul>
</template>
