<script lang="ts">
import type { DeploymentPromotion } from "@/domain/deployments";

/** Defines the fleet rollout confirm dialog. */
export interface FleetPromoteModalProps {
  /** The controlled dialog state. */
  readonly open: boolean;
  /** The releases to promote, in the order the fleet takes them. */
  readonly items: readonly DeploymentPromotion[];
  /** Product names by slug. */
  readonly productNames: ReadonlyMap<string, string>;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ArrowRight, Layers } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge } from "@wow-two-beta/fe/presentation/display";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import { CheckboxField, Field, TextInput } from "@wow-two-beta/fe/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalClose,
  ModalContent,
  ModalDescription,
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from "@wow-two-beta/fe/presentation/overlays";

import { useDeployFreezes, useDeploymentTargets, useStartFleet } from "@/application/deployments";
import { DeployFreezes, rollbackRisks } from "@/domain/deployments";
import { failureReason } from "@/integration/common";

import FrozenBadge from "./FrozenBadge.vue";
import ReleaseChangelog from "./ReleaseChangelog.vue";

/**
 * Confirms a fleet rollout: every product it promotes, in order, with what prod runs now and what it gets, the risks a
 * failed rollout carries and the changelog behind a link. A frozen target needs the override with a reason, and prod on
 * the local server its ID typed. Nothing deploys until Promote; the fleet then runs on the host, product by product.
 */
defineOptions({ name: "FleetPromoteModal" });
const props = defineProps<FleetPromoteModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean]; started: [] }>();

const start = useStartFleet();
const targets = useDeploymentTargets();
const freezes = useDeployFreezes();
const reason = ref("");
const overrideFreeze = ref(false);
/** The typed target IDs, by target. */
const typed = ref<Record<string, string>>({});

watch(
  () => props.open,
  (open) => {
    if (!open) return;
    reason.value = "";
    overrideFreeze.value = false;
    typed.value = {};
    start.reset();
  },
  { immediate: true },
);

const frozen = computed(() =>
  props.items
    .map((item) => ({ item, freeze: DeployFreezes.covering(freezes.data.value, item.product, "prod") }))
    .filter((entry) => entry.freeze !== null),
);
/** The prod targets on the local server, which take a promotion only with their ID typed. */
const needsTyping = computed(
  () => new Set((targets.data.value ?? []).filter((target) => target.needsConfirmation).map((target) => target.id)),
);
const reasonNeeded = computed(() => frozen.value.length > 0 && overrideFreeze.value);
const canPromote = computed(
  () =>
    props.items.length > 0 &&
    (frozen.value.length === 0 || overrideFreeze.value) &&
    (!reasonNeeded.value || reason.value.trim().length > 0) &&
    props.items.every((item) => !needsTyping.value.has(item.prodTargetId) || typed.value[item.prodTargetId] === item.prodTargetId),
);

function name(item: DeploymentPromotion): string {
  return props.productNames.get(item.product) ?? item.product;
}

async function promote(): Promise<void> {
  if (!canPromote.value || start.loading.value) return;
  const result = await start.mutateAsync({
    items: props.items.map((item) => ({
      target: item.prodTargetId,
      release: item.bundleId,
      ...(needsTyping.value.has(item.prodTargetId) ? { confirm: typed.value[item.prodTargetId] } : {}),
    })),
    reason: reason.value.trim() || null,
    overrideFreeze: frozen.value.length > 0 && overrideFreeze.value,
  });
  if (!result.ok) return;
  emit("started");
  emit("update:open", false);
}

/** Requests dismissal unless the fleet is being submitted. */
function close(open: boolean): void {
  if (start.loading.value) return;
  emit("update:open", open);
}
</script>

<template>
  <Modal
    :open="props.open"
    :can-dismiss-on-outside-click="!start.loading.value"
    :can-dismiss-on-escape="!start.loading.value"
    @update:open="close"
  >
    <ModalContent class="w-full max-w-2xl">
      <ModalClose />
      <ModalHeader class="pr-8">
        <ModalTitle>Promote {{ props.items.length }} {{ props.items.length === 1 ? "product" : "products" }} to prod?</ModalTitle>
        <ModalDescription>
          One at a time, in this order, each through the usual prod gates. The fleet stops at the first failure and the rest
          are not started; you can cancel it between products.
        </ModalDescription>
      </ModalHeader>
      <ModalBody class="flex flex-col gap-4">
        <ol class="flex flex-col divide-y divide-border rounded-xl border border-border" aria-label="Products to promote">
          <li v-for="(item, index) in props.items" :key="`${item.prodTargetId}-${item.bundleId}`" class="flex flex-col gap-2 p-3">
            <p class="flex flex-wrap items-center gap-2">
              <span class="font-mono text-xs text-muted-foreground" aria-hidden="true">{{ index + 1 }}.</span>
              <span class="font-medium">{{ name(item) }}</span>
              <FrozenBadge :product="item.product" environment="prod" />
              <Badge v-for="risk in rollbackRisks(item)" :key="risk" variant="warning">{{ risk }}</Badge>
            </p>
            <p class="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-muted-foreground">
              <span class="font-mono text-xs">{{ item.prodTargetId }}</span>
              <span class="font-mono text-xs text-foreground">{{ item.prodRelease ?? "nothing" }}</span>
              <ArrowRight :size="14" aria-label="to" />
              <span class="font-mono text-xs font-medium text-foreground">{{ item.release }}</span>
            </p>
            <ReleaseChangelog :product="item.product" :from="item.prodRelease" :to="item.release" />
            <Field v-if="needsTyping.has(item.prodTargetId)" :label="`Type ${item.prodTargetId} to promote prod on the local server`">
              <TextInput
                :model-value="typed[item.prodTargetId] ?? ''"
                :disabled="start.loading.value"
                autocomplete="off"
                @update:model-value="typed = { ...typed, [item.prodTargetId]: String($event ?? '') }"
              />
            </Field>
          </li>
        </ol>
        <Alert
          v-if="props.items.some((item) => item.rollbackCompatible === false)"
          severity="warning"
          title="Some releases can't roll back automatically"
          description="If one of them fails after replacing containers, its target stays on it until you recover it by hand, and the fleet stops there."
        />
        <template v-if="frozen.length">
          <Alert
            severity="warning"
            title="Deploys to some of these are frozen"
            :description="frozen.map((entry) => `${name(entry.item)}: ${DeployFreezes.describe(entry.freeze!)}`).join(' · ')"
          />
          <CheckboxField v-model="overrideFreeze" label="Override the freezes" :disabled="start.loading.value" />
        </template>
        <Field
          label="Reason"
          :helper="reasonNeeded ? 'Required to override a freeze; kept on every deployment and in the audit trail' : 'Optional; kept on every deployment and in the audit trail'"
        >
          <TextInput v-model="reason" :disabled="start.loading.value" autocomplete="off" maxlength="300" placeholder="Why this rollout" />
        </Field>
        <Alert v-if="start.error.value" severity="danger" title="Fleet rollout refused" :description="failureReason(start.error.value)" />
      </ModalBody>
      <ModalFooter>
        <Button variant="outline" tone="neutral" :is-disabled="start.loading.value" @click="close(false)">Cancel</Button>
        <Button variant="solid" tone="primary" :is-loading="start.loading.value" :is-disabled="!canPromote" @click="promote">
          <template #leading><Layers :size="16" aria-hidden="true" /></template>Promote {{ props.items.length }}
        </Button>
      </ModalFooter>
    </ModalContent>
  </Modal>
</template>
