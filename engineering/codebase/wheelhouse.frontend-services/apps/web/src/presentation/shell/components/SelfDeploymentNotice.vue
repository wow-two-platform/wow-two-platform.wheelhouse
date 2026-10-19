<script setup lang="ts">
import { computed } from 'vue';

import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Alert } from '@wow-two-beta/fe/presentation/feedback';

import { useSelfDeployment } from '@/application/deployments';
import { useDismissedSelfNotices } from '@/application/preferences';
import { SelfDeployRules } from '@/domain/deployments';

/**
 * Says how the rollout that started this console ended: a self-promotion replaces the console that launched it, so the
 * new one (or the old one again, after a roll back) reads the host's journal at boot and tells the owner here, until it
 * is dismissed or a day has passed.
 */
defineOptions({ name: 'SelfDeploymentNotice' });
const { reading } = useSelfDeployment();
const dismissed = useDismissedSelfNotices();
const notice = computed(() => SelfDeployRules.notice(reading.value, new Date(), new Set(dismissed.value)));

/** Hides this rollout's notice for good. @internal */
function dismiss(): void {
  if (notice.value) dismissed.value = [...dismissed.value, notice.value.id];
}
</script>

<template>
  <Alert
    v-if="notice"
    class="mb-4"
    :severity="notice.tone"
    :title="notice.title"
    :description="notice.description"
    role="status"
  >
    <template #actions>
      <Button size="sm" variant="ghost" tone="neutral" @click="dismiss">Dismiss</Button>
    </template>
  </Alert>
</template>
