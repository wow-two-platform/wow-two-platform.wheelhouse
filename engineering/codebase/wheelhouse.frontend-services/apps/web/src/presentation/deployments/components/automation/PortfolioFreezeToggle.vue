<script setup lang="ts">
import { computed, ref } from 'vue';
import { Snowflake } from 'lucide-vue-next';

import { Button } from '@wow-two-beta/fe/presentation/actions';

import { useDeployFreezes, useUnfreezeDeploys } from '@/application/deployments';
import { DeployFreezes } from '@/domain/deployments';

import FreezeDialog from './FreezeDialog.vue';

/** Freezes or unfreezes deploys to every product, from the Deployments page header. */
defineOptions({ name: 'PortfolioFreezeToggle' });
defineSlots<{}>();

const freezes = useDeployFreezes();
const unfreeze = useUnfreezeDeploys();
const portfolio = computed(() => DeployFreezes.of(freezes.data.value, null, null));
const asking = ref(false);
</script>

<template>
  <Button
    v-if="portfolio"
    variant="soft"
    tone="warning"
    :title="DeployFreezes.describe(portfolio)"
    :is-loading="unfreeze.loading.value"
    @click="unfreeze.mutate({})"
  >
    <template #leading><Snowflake :size="16" aria-hidden="true" /></template>Unfreeze all
  </Button>
  <Button v-else variant="outline" tone="neutral" @click="asking = true">
    <template #leading><Snowflake :size="16" aria-hidden="true" /></template>Freeze all
  </Button>
  <FreezeDialog v-model:open="asking" covers="every product" />
</template>
