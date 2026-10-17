<script lang="ts">
/** Sizes the Wheelhouse mark. */
export interface BrandMarkProps {
  /** The rendered size in CSS pixels; the mark is square. */
  readonly size?: number;
}
</script>
<script setup lang="ts">
/**
 * Shows the Soft folds mark alone, where the wordmark does not fit: the icon rail and the phone's top bar. Dark ink on
 * light surfaces, light ink on dark ones — the same files the favicon uses.
 */
defineOptions({ name: 'BrandMark' });
const props = withDefaults(defineProps<BrandMarkProps>(), { size: 28 });
defineSlots<{}>();
</script>
<template>
  <span class="inline-flex shrink-0">
    <img src="/favicon.png" alt="Wheelhouse" :width="props.size" :height="props.size" class="dark:hidden" />
    <img src="/favicon-dark.png" alt="Wheelhouse" :width="props.size" :height="props.size" class="hidden dark:block" />
  </span>
</template>
