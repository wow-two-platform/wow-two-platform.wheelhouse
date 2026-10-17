<script lang="ts">
/** The full sidebar over the content, for tablets. */
export interface NavDrawerProps {
  /** The open state — the `v-model:open` target. */
  readonly open: boolean;
}
</script>
<script setup lang="ts">
import { Drawer, DrawerContent, DrawerTitle } from '@wow-two-beta/fe/presentation/overlays';
import ConsoleNav from './ConsoleNav.vue';

/** Tablets keep the rail beside the content; this drawer opens the full sidebar over it and closes on navigation. */
defineOptions({ name: 'NavDrawer' });
const props = defineProps<NavDrawerProps>();
const emit = defineEmits<{ 'update:open': [open: boolean] }>();
defineSlots<{}>();
</script>
<template>
  <Drawer :open="props.open" side="left" @update:open="emit('update:open', $event)">
    <DrawerContent class="w-72 max-w-[85%] p-3">
      <DrawerTitle class="sr-only">Navigation</DrawerTitle>
      <ConsoleNav @navigate="emit('update:open', false)" />
    </DrawerContent>
  </Drawer>
</template>
