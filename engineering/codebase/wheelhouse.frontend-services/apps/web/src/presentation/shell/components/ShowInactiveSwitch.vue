<script setup lang="ts">
import { SwitchField } from '@wow-two-beta/fe/presentation/forms';
import { useShowInactive } from '@/application/preferences';

/** The one "Show inactive" switch: inactive products and services stay hidden everywhere until it is on. */
defineOptions({ name: 'ShowInactiveSwitch' });
defineSlots<{}>();
const showInactive = useShowInactive();
</script>
<template>
  <!-- A plain root, so a caller's class sizes the row rather than reaching the switch. -->
  <div class="flex">
  <SwitchField
    v-model="showInactive"
    size="sm"
    side="right"
    label="Show inactive"
    wrapper-class-name="min-h-[inherit] w-full cursor-pointer items-center gap-3 rounded-md px-2.5 text-sm [&_.font-medium]:font-normal [&_.font-medium]:text-muted-foreground"
  />
  </div>
</template>
