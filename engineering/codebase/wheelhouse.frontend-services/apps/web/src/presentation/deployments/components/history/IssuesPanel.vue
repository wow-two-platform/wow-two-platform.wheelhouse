<script lang="ts">
import type { DeploymentIssue } from "@/domain/deployments";

/** Defines the targets that need attention. */
export interface IssuesPanelProps {
  /** The issues; the page leaves the panel out while there are none. */
  readonly items: readonly DeploymentIssue[];
  /** Product names by slug. */
  readonly productNames: ReadonlyMap<string, string>;
  /** The target whose state is being read before its reconcile dialog opens. */
  readonly checkingTarget?: string | null;
}
</script>

<script setup lang="ts">
import { RouterLink, useRouter } from "vue-router";
import { ArrowUpRight, RotateCcw, TriangleAlert, Wrench } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge, RelativeTime } from "@wow-two-beta/fe/presentation/display";

import { ConsolePaths } from "@/application/navigation";
import { DeploymentIssueKind, DeploymentIssues } from "@/domain/deployments";
import { Panel } from "@/presentation/common/components";

/**
 * Each target whose latest rollout failed or rolled back with no success since, and each rollout stuck long enough to
 * need a reconcile: product, environment, release, what happened and when, with Open, Redeploy and Reconcile. Those
 * likely locked come first.
 */
defineOptions({ name: "IssuesPanel" });
const props = withDefaults(defineProps<IssuesPanelProps>(), { checkingTarget: null });
const emit = defineEmits<{ redeploy: [issue: DeploymentIssue]; reconcile: [issue: DeploymentIssue] }>();
const router = useRouter();

function productOf(issue: DeploymentIssue): string {
  const slug = issue.deployment.product;
  return slug ? (props.productNames.get(slug) ?? slug) : (issue.deployment.targetId ?? "Unknown product");
}

function pathOf(issue: DeploymentIssue): string {
  return `${ConsolePaths.deployments}/${encodeURIComponent(issue.deployment.id)}`;
}

function describe(issue: DeploymentIssue): string {
  return `${productOf(issue)} ${issue.deployment.environment ?? issue.deployment.targetId ?? ""}`.trim();
}
</script>

<template>
  <Panel
    title="Issues"
    :description="`${props.items.length} ${props.items.length === 1 ? 'target needs' : 'targets need'} attention.`"
  >
    <ul class="flex flex-col divide-y divide-border">
      <li
        v-for="issue in props.items"
        :key="issue.deployment.id"
        class="grid grid-cols-[1.25rem_minmax(0,1fr)] gap-x-3 gap-y-3 py-3 first:pt-0 last:pb-0 md:grid-cols-[1.25rem_minmax(0,1fr)_auto] md:items-center"
      >
        <component
          :is="issue.needsReconcile ? Wrench : TriangleAlert"
          :size="18"
          :class="['mt-0.5 md:mt-0', issue.kind === DeploymentIssueKind.RolledBack || issue.kind === DeploymentIssueKind.Interrupted ? 'text-warning' : 'text-destructive']"
          aria-hidden="true"
        />
        <div class="min-w-0">
          <p class="flex flex-wrap items-center gap-x-2 gap-y-1">
            <RouterLink
              v-if="issue.deployment.product"
              :to="ConsolePaths.product(issue.deployment.product)"
              class="font-medium hover:text-primary focus-visible:outline-primary"
              >{{ productOf(issue) }}</RouterLink
            >
            <span v-else class="font-medium">{{ productOf(issue) }}</span>
            <Badge :variant="issue.deployment.environment === 'prod' ? 'info' : 'neutral'">{{
              issue.deployment.environment ?? issue.deployment.targetId
            }}</Badge>
            <span class="font-mono text-xs">{{ issue.deployment.release ?? issue.deployment.bundleId ?? "—" }}</span>
          </p>
          <p class="mt-1 text-sm">
            <span :class="issue.kind === DeploymentIssueKind.RolledBack || issue.kind === DeploymentIssueKind.Interrupted ? 'text-warning' : 'text-destructive'">{{
              DeploymentIssues.headline(issue)
            }}</span>
            <span v-if="issue.deployment.reason" class="text-muted-foreground"> · {{ issue.deployment.reason }}</span>
          </p>
          <p class="mt-0.5 text-xs text-muted-foreground">
            <RelativeTime :value="issue.at" /><template v-if="issue.deployment.actor"> · {{ issue.deployment.actor }}</template
            ><template v-if="issue.needsReconcile"> · likely locked until reconciled</template>
          </p>
        </div>
        <div class="col-start-2 flex flex-wrap gap-2 md:col-start-3 md:justify-end">
          <Button
            variant="ghost"
            tone="neutral"
            size="sm"
            class="max-md:min-h-11"
            :aria-label="`Open the deployment of ${issue.deployment.release ?? 'a release'} to ${describe(issue)}`"
            @click="router.push(pathOf(issue))"
          >
            <template #leading><ArrowUpRight :size="14" aria-hidden="true" /></template>Open
          </Button>
          <Button
            v-if="DeploymentIssues.canRedeploy(issue)"
            variant="outline"
            tone="neutral"
            size="sm"
            class="max-md:min-h-11"
            :aria-label="`Redeploy ${issue.deployment.release ?? issue.deployment.bundleId} to ${describe(issue)}`"
            @click="emit('redeploy', issue)"
          >
            <template #leading><RotateCcw :size="14" aria-hidden="true" /></template>Redeploy
          </Button>
          <Button
            v-if="DeploymentIssues.canReconcile(issue)"
            variant="soft"
            tone="danger"
            size="sm"
            class="max-md:min-h-11"
            :is-loading="props.checkingTarget === issue.deployment.targetId"
            :aria-label="`Reconcile ${issue.deployment.targetId}`"
            @click="emit('reconcile', issue)"
          >
            <template #leading><Wrench :size="14" aria-hidden="true" /></template>Reconcile
          </Button>
        </div>
      </li>
    </ul>
  </Panel>
</template>
