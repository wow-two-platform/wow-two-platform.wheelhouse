<script setup lang="ts">
import { computed } from 'vue';
import { Snowflake } from 'lucide-vue-next';

import { Badge } from '@wow-two-beta/fe/presentation/display';

import { useCoveringFreeze } from '@/application/deployments';
import { DeployFreezes } from '@/domain/deployments';

/** Marks a row whose target a freeze holds: automatic deploys skip it, an owner's deploy needs an override. */
defineOptions({ name: 'FrozenBadge' });
const props = defineProps<{ readonly product: string | null | undefined; readonly environment: string | null | undefined }>();
defineSlots<{}>();

const freeze = useCoveringFreeze(
  () => props.product,
  () => props.environment,
);
const label = computed(() => (freeze.value ? DeployFreezes.describe(freeze.value) : ''));
</script>

<template>
  <Badge v-if="freeze" variant="warning" :title="label" :aria-label="label">
    <Snowflake :size="12" aria-hidden="true" class="mr-1 inline" />Frozen
  </Badge>
</template>
