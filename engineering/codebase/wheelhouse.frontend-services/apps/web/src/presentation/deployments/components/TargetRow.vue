<script lang="ts">
import type { DeploymentTarget, ReleaseArtifact } from "@/domain/deployments";

/** Defines one target row and its current published release catalog. */
export interface TargetRowProps {
  /** The code-owned target to inspect. */
  readonly target: DeploymentTarget;
  /** The available release catalog used only to calculate drift. */
  readonly releases: readonly ReleaseArtifact[];
}
</script>

<script setup lang="ts">
import { computed, ref } from "vue";

import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { Badge } from "@wow-two-beta/ui-vue/presentation/display";
import { Spinner } from "@wow-two-beta/ui-vue/presentation/feedback";

import { useRefresh } from "@/bootstrap/query";
import { useTargetState } from "@/application/deployments";
import { DeploymentExtensions, TargetCondition } from "@/domain/deployments";

import ConditionBadge from "./ConditionBadge.vue";
import ReconcileModal from "./ReconcileModal.vue";
import TargetSites from "./TargetSites.vue";
import { RefreshButton } from "@/presentation/common/components";

/** Renders a target's verified release and explicit reconciliation action. */
defineOptions({ name: "TargetRow" });
const props = defineProps<TargetRowProps>();
const state = useTargetState(() => props.target.id);
const stateRefresh = useRefresh(state.refetch);
const reconciling = ref(false);
const drift = computed(() =>
  DeploymentExtensions.releaseDrift(
    state.data.value?.current?.release,
    [...props.releases],
    props.target.product,
  ),
);
</script>

<template>
  <li class="flex flex-wrap items-center justify-between gap-4 py-4">
    <div class="min-w-0">
      <p class="font-mono text-sm">{{ props.target.id }}</p>
      <p class="mt-1 text-xs text-muted-foreground">
        {{ props.target.product }} · {{ props.target.environment }} ·
        {{ props.target.host }}
      </p>
      <TargetSites
        class="mt-2"
        :sites="state.data.value?.current?.sites"
        :versions="state.data.value?.current?.versions"
      />
    </div>
    <div class="flex flex-wrap items-center gap-3 text-sm">
      <Spinner
        v-if="state.loading.value || stateRefresh.refreshing.value"
        size="sm"
        label="Reading target"
      />
      <template v-if="state.error.value">
        <span class="text-destructive">{{ state.error.value.message }}</span>
        <RefreshButton
          variant="ghost"
          label="Retry"
          :refreshing="stateRefresh.refreshing.value"
          @refresh="stateRefresh.refresh()"
        />
      </template>
      <template v-if="state.data.value">
        <span v-if="state.error.value" class="text-xs text-muted-foreground"
          >Last observed state</span
        >
        <ConditionBadge :condition="state.data.value.condition" />
        <span class="text-muted-foreground">
          {{
            state.data.value.current?.release
              ? `verified ${state.data.value.current.release}`
              : "No verified release"
          }}
        </span>
        <Badge v-if="drift.behind === 0" variant="success">Latest</Badge>
        <Badge
          v-else-if="drift.behind !== null && drift.behind > 0"
          variant="warning"
          :title="`Latest published: ${drift.latest?.release ?? ''}`"
        >
          {{ drift.behind }}
          {{ drift.behind === 1 ? "release" : "releases" }} behind
        </Badge>
        <span
          v-else-if="state.data.value.current?.release && drift.latest"
          class="text-xs text-muted-foreground"
        >
          Latest published {{ drift.latest.release }}
        </span>
        <template
          v-if="
            state.data.value.condition ===
              TargetCondition.NeedsReconciliation && state.data.value.active
          "
        >
          <Button
            variant="soft"
            tone="danger"
            size="sm"
            @click="reconciling = true"
            >Reconcile</Button
          >
          <ReconcileModal
            :target="props.target.id"
            :active="state.data.value.active"
            v-model:open="reconciling"
          />
        </template>
      </template>
    </div>
  </li>
</template>
