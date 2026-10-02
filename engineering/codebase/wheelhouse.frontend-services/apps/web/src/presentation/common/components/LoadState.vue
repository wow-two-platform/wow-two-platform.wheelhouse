<script lang="ts">
/** Defines the state of an operational read, including retained data during errors. */
export interface LoadStateProps {
  /** The first load: nothing to show yet, so the region renders its skeleton. */
  readonly loading: boolean;
  /** A refresh the user asked for: the content stays and only its `SkeletonStateSlot` values turn into placeholders. */
  readonly refreshing?: boolean | undefined;
  readonly error?: { readonly message: string } | string | null;
  readonly empty?: boolean;
  readonly emptyTitle?: string;
  readonly emptyDescription?: string;
  readonly hasData?: boolean;
}
</script>
<script setup lang="ts">
import { Button } from '@wow-two-beta/ui-vue/presentation/actions';
import { EmptyState } from '@wow-two-beta/ui-vue/presentation/display';
import { SkeletonState, SkeletonStateGroup } from '@wow-two-beta/ui-vue/presentation/feedback';

/** Keeps pending, failed, empty, and retained operational data distinct; a refresh never blanks the region. */
defineOptions({ name: 'LoadState' });
/* `refreshing: undefined` is load-bearing: Vue casts an absent `Boolean` prop to `false`, and a region that
   does not own a refresh must not open a group that hides its parent's. */
const props = withDefaults(defineProps<LoadStateProps>(), { refreshing: undefined });
const emit = defineEmits<{ retry: [] }>();
defineSlots<{ default(): unknown; skeleton(): unknown; emptyActions(): unknown }>();
</script>
<template>
  <div v-if="props.loading" role="status" aria-label="Loading data" class="space-y-3">
    <slot name="skeleton"> <SkeletonState class="h-8 w-2/3" /><SkeletonState class="h-24 w-full" /> </slot>
  </div>
  <template v-else>
    <div
      v-if="props.error"
      role="alert"
      class="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-destructive/30 bg-destructive-soft p-4 text-sm text-destructive-soft-foreground"
    >
      <span>{{ typeof props.error === 'string' ? props.error : props.error.message }}</span>
      <Button size="sm" variant="outline" @click="emit('retry')">Retry</Button>
    </div>
    <template v-if="!props.error || props.hasData">
      <div v-if="props.empty" class="py-8 text-center">
        <EmptyState :title="props.emptyTitle ?? 'Nothing here yet'" :description="props.emptyDescription ?? ''" />
        <slot name="emptyActions" />
      </div>
      <!-- Only a region that owns its refresh opens a group; a nested read follows its region's refresh. -->
      <SkeletonStateGroup v-else-if="props.refreshing !== undefined" class="contents" :is-loading="props.refreshing"
        ><slot
      /></SkeletonStateGroup>
      <slot v-else />
    </template>
  </template>
</template>
