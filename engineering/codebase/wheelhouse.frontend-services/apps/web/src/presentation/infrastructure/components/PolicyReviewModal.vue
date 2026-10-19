<script lang="ts">
/** The tailnet policy review: what Wheelhouse would apply, Tailscale's verdict, the guard and the diff. */
export interface PolicyReviewModalProps {
  readonly open: boolean;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { CircleCheck, Undo2 } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Alert, DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import {
  ConfirmPopover,
  Modal,
  ModalBody,
  ModalContent,
  ModalFooter,
  ModalHeader,
  ModalSize,
  ModalTitle,
} from "@wow-two-beta/fe/presentation/overlays";
import { usePolicyChanges, usePolicyReview } from "@/application/infrastructure";
import { Measures } from "@/domain/common";
import { PolicyDiff, TailnetRules } from "@/domain/infrastructure";
import { failureReason } from "@/integration/common";
import PolicyDiffView from "./PolicyDiffView.vue";

/**
 * Reviews the access policy Wheelhouse renders from its inventory against the live one. Apply is offered only when the
 * policy changes, Tailscale validates it and the control-path guard holds; it asks once and sends the reviewed ETag
 * and digest, so only what the owner saw is written. The previous policy, when one was kept, goes back the same way.
 * Both read the review and the tailnet status again; a refusal shows here.
 */
defineOptions({ name: "PolicyReviewModal" });
const props = defineProps<PolicyReviewModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const review = usePolicyReview(() => props.open);
const changes = usePolicyChanges();
const error = ref("");
const done = ref("");
const data = computed(() => review.data.value ?? null);
const canApply = computed(() => TailnetRules.canApply(data.value));
const savedAt = computed(() => Measures.moment(data.value?.previous?.savedAt));

/* Each session starts from a fresh read, without the last one's messages; a first open reads once as it enables. */
watch(
  () => props.open,
  (open) => {
    if (!open) return;
    error.value = "";
    done.value = "";
    if (review.data.value) void review.refetch();
  },
);

/** Applies the reviewed policy. @internal */
async function apply(): Promise<void> {
  const reviewed = data.value;
  if (!reviewed || !TailnetRules.canApply(reviewed)) return;
  error.value = "";
  done.value = "";
  const result = await changes.apply({ etag: reviewed.etag ?? "", digest: reviewed.renderedDigest });
  if (!result.ok) error.value = failureReason(result.failure);
  else done.value = `Applied ${Measures.moment(result.value.appliedAt)}.`;
}

/** Puts back the policy that was live before the last apply. @internal */
async function revert(): Promise<void> {
  const reviewed = data.value;
  if (!reviewed?.previous) return;
  error.value = "";
  done.value = "";
  const result = await changes.revert({ etag: reviewed.etag ?? "" });
  if (!result.ok) error.value = failureReason(result.failure);
  else done.value = `Previous policy put back ${Measures.moment(result.value.revertedAt)}.`;
}
</script>

<template>
  <Modal :open="open" @update:open="emit('update:open', $event)">
    <ModalContent :size="ModalSize.Xl">
      <ModalHeader>
        <ModalTitle>Tailnet policy</ModalTitle>
      </ModalHeader>
      <ModalBody class="flex flex-col gap-4">
        <DataRegion
          :is-first-load="review.isFirstLoad.value"
          :error="data ? null : review.error.value"
          :has-data="Boolean(data)"
          loading-label="Reading the tailnet policy"
          class="flex flex-col gap-4"
          @retry="review.refetch()"
        >
          <template v-if="data">
            <p class="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
              <span class="font-medium">{{ data.changed ? "Changes" : "Already matches the inventory" }}</span>
              <span v-if="data.changed" class="text-muted-foreground">{{ PolicyDiff.summary(data.diff) }}</span>
            </p>
            <section v-if="data.hosts.length" aria-label="Products hosts">
              <h3 class="mb-1.5 text-xs font-medium text-muted-foreground">Products hosts</h3>
              <ul role="list" class="flex flex-wrap gap-1.5">
                <li
                  v-for="host in data.hosts"
                  :key="host.serverId"
                  class="inline-flex items-center gap-1.5 rounded-lg border border-border px-2 py-1 text-xs"
                >
                  <span class="font-medium">{{ host.serverId }}</span
                  ><span class="font-mono text-muted-foreground">{{ host.address }}</span>
                </li>
              </ul>
            </section>
            <Alert v-if="!data.validation.ok" severity="danger" title="Tailscale refuses it">
              <template #description>
                <ul class="list-disc ps-4">
                  <li v-for="problem in data.validation.problems" :key="problem">{{ problem }}</li>
                </ul>
              </template>
            </Alert>
            <p v-else-if="data.changed" class="flex items-center gap-2 text-sm">
              <CircleCheck :size="16" class="text-success" aria-hidden="true" />Tailscale validates it
            </p>
            <Alert
              v-if="!data.guard.ok"
              severity="danger"
              title="Guard refuses it"
              :description="data.guard.reason ?? 'The control host would lose its path to products hosts.'"
            />
            <p v-else class="flex items-center gap-2 text-sm">
              <CircleCheck :size="16" class="text-success" aria-hidden="true" />Control path kept
            </p>
            <PolicyDiffView v-if="data.changed && data.diff.length" :hunks="data.diff" />
          </template>
        </DataRegion>
        <Alert v-if="error" severity="danger" :description="error" />
        <Alert v-if="done" severity="success" :description="done" />
      </ModalBody>
      <ModalFooter class="sm:justify-between">
        <ConfirmPopover
          v-if="data?.previous"
          :title="`Put back the policy that was live before ${savedAt}?`"
          confirm-label="Revert"
          tone="danger"
          placement="top-start"
          :on-confirm="revert"
        >
          <Button type="button" variant="ghost" tone="neutral">
            <template #leading><Undo2 :size="14" /></template>Revert to the previous policy
          </Button>
        </ConfirmPopover>
        <span v-else class="max-sm:hidden" />
        <span class="flex flex-col-reverse gap-2 sm:flex-row">
          <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Close</Button>
          <ConfirmPopover
            title="Apply this policy to the tailnet?"
            description="It replaces the live policy with the one reviewed here."
            confirm-label="Apply"
            placement="top-end"
            :is-disabled="!canApply"
            :on-confirm="apply"
          >
            <Button type="button" :is-disabled="!canApply">Apply</Button>
          </ConfirmPopover>
        </span>
      </ModalFooter>
    </ModalContent>
  </Modal>
</template>
