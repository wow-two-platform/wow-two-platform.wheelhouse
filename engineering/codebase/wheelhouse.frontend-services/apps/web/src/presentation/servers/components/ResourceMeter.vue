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
import { MeterBar } from '@wow-two-beta/ui-vue/presentation/feedback';
import { USAGE_THRESHOLDS } from '@/domain/servers';

/** Presents a current resource snapshot without inventing history or unknown values. */
defineOptions({ name: 'ResourceMeter' });
const props = defineProps<ResourceMeterProps>();
</script>
<template>
  <div class="min-w-0 rounded-xl border border-border bg-card p-3">
    <MeterBar
      v-if="props.value != null || props.refreshing"
      :value="props.value ?? 0"
      :thresholds="USAGE_THRESHOLDS"
      :label="props.label"
      is-label-visible
      :is-loading="props.refreshing"
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
    <p v-if="props.detail" class="mt-2 truncate text-xs text-muted-foreground" :title="props.detail">
      {{ props.detail }}
    </p>
  </div>
</template>
