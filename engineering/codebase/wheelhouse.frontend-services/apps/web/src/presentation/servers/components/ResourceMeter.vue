<script lang="ts">
/** Defines a current host or container resource reading. */
export interface ResourceMeterProps {
  readonly label: string;
  readonly value: number | null;
  readonly detail?: string;
  readonly refreshing?: boolean;
}
</script>
<script setup lang="ts">
import { Text } from '@wow-two-beta/fe/presentation/display';
import { MeterBar, useSkeletonState } from '@wow-two-beta/fe/presentation/feedback';
import { USAGE_THRESHOLDS } from '@/domain/servers';

/** Presents a current resource snapshot without inventing history or unknown values. */
defineOptions({ name: 'ResourceMeter' });
const props = defineProps<ResourceMeterProps>();
/** A region's first load draws the meter as its skeleton, not the unavailable line. */
const skeleton = useSkeletonState();
</script>
<template>
  <div class="min-w-0 rounded-xl border border-border bg-card p-3">
    <!-- Only a running refresh is passed: an explicit `false` would override the meter's own first-load skeleton. -->
    <MeterBar
      v-if="props.value != null || props.refreshing || skeleton.isSkeleton.value"
      :value="props.value ?? 0"
      :thresholds="USAGE_THRESHOLDS"
      :label="props.label"
      is-label-visible
      v-bind="props.refreshing ? { isLoading: true } : {}"
      size="sm"
    />
    <!-- An unread value draws no meter: a meter would announce a reading the host never gave. -->
    <div v-else class="flex flex-col gap-1.5">
      <div class="flex items-center justify-between gap-3 text-xs">
        <span class="truncate text-muted-foreground">{{ props.label }}</span
        ><span class="font-medium">Unavailable</span>
      </div>
      <div class="h-1 rounded-full bg-muted" />
    </div>
    <Text v-if="props.detail" class="mt-2 truncate text-xs text-muted-foreground" :title="props.detail">
      {{ props.detail }}
    </Text>
  </div>
</template>
