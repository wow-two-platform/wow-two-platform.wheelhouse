<script lang="ts">
/** Drives the first-load splash. */
export interface BootSplashProps {
  /** Whether the splash still covers the console; closing fades it out. */
  readonly isOpen: boolean;
  /** The boot progress, 0–100. */
  readonly value: number;
}
</script>
<script setup lang="ts">
import { SplashScreen } from '@wow-two-beta/ui-vue/presentation/feedback';
import BrandWordmark from './BrandWordmark.vue';

/**
 * Covers the console's first load with the wordmark over its ambient surface. It matches the static splash in
 * `index.html` and `public/boot-splash.css`, which it replaces when the app mounts.
 */
defineOptions({ name: 'BootSplash' });
const props = defineProps<BootSplashProps>();
defineSlots<{}>();
</script>
<template>
  <SplashScreen :is-open="props.isOpen" :value="props.value" is-overlay label="Loading Wheelhouse" class="surface-ambient">
    <template #logo><BrandWordmark :height="40" /></template>
  </SplashScreen>
</template>
