<script setup lang="ts">
import { ref, watch } from 'vue';
import { Snowflake } from 'lucide-vue-next';

import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Field, TextInput } from '@wow-two-beta/fe/presentation/forms';
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalDescription,
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from '@wow-two-beta/fe/presentation/overlays';

import { useFreezeDeploys } from '@/application/deployments';

/** Asks for an optional reason, then holds deploys on one scope: everything, a product, or a product's environment. */
defineOptions({ name: 'FreezeDialog' });
const props = defineProps<{
  readonly open: boolean;
  /** The frozen product; null for every product. */
  readonly product?: string | null;
  readonly environment?: string | null;
  /** What it covers in words, for the title. */
  readonly covers: string;
}>();
const emit = defineEmits<{ 'update:open': [open: boolean] }>();
defineSlots<{}>();

const reason = ref('');
const freeze = useFreezeDeploys();
watch(
  () => props.open,
  (open) => {
    if (open) reason.value = '';
  },
);

async function submit(): Promise<void> {
  const result = await freeze.mutateAsync({ product: props.product ?? null, environment: props.environment ?? null, reason: reason.value.trim() || null });
  if (result.ok) emit('update:open', false);
}
</script>

<template>
  <Modal :open="props.open" @update:open="emit('update:open', $event)">
    <ModalContent class="w-full max-w-md">
      <ModalHeader>
        <ModalTitle>Freeze deploys to {{ props.covers }}?</ModalTitle>
        <ModalDescription>Automatic deploys skip it, and a deploy by hand needs an override with a reason.</ModalDescription>
      </ModalHeader>
      <form class="contents" @submit.prevent="submit">
        <ModalBody>
          <Field label="Reason" helper="Optional; shown beside the freeze">
            <TextInput v-model="reason" autocomplete="off" maxlength="300" placeholder="Launch day" />
          </Field>
        </ModalBody>
        <ModalFooter>
          <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
          <Button type="submit" tone="warning" :is-loading="freeze.loading.value">
            <template #leading><Snowflake :size="16" aria-hidden="true" /></template>Freeze
          </Button>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
