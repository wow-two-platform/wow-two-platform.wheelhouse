<script lang="ts">
import type { BuildFailure, DeploymentIssue, GitHubAccessIssue } from "@/domain/deployments";

/** Defines the targets that need attention. */
export interface IssuesPanelProps {
  /** The issues; the page leaves the panel out while there are none. */
  readonly items: readonly DeploymentIssue[];
  /** Product names by slug. */
  readonly productNames: ReadonlyMap<string, string>;
  /** The target whose state is being read before its reconcile dialog opens. */
  readonly checkingTarget?: string | null;
  /** The products whose newest finished main build failed. */
  readonly builds?: readonly BuildFailure[];
  /** What keeps the control plane from reaching GitHub, per owner. */
  readonly access?: readonly GitHubAccessIssue[];
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { RouterLink, useRouter } from "vue-router";
import { ArrowUpRight, Hammer, KeyRound, RotateCcw, TriangleAlert, Wrench } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge, RelativeTime } from "@wow-two-beta/fe/presentation/display";

import { ConsolePaths } from "@/application/navigation";
import { DeploymentIssueKind, DeploymentIssues } from "@/domain/deployments";
import { Panel } from "@/presentation/common/components";

import FrozenBadge from "../automation/FrozenBadge.vue";

/**
 * Each target whose latest rollout failed or rolled back with no success since, and each rollout stuck long enough to
 * need a reconcile: product, environment, release, what happened and when, with Open, Redeploy and Reconcile. Those
 * likely locked come first.
 */
defineOptions({ name: "IssuesPanel" });
const props = withDefaults(defineProps<IssuesPanelProps>(), { checkingTarget: null, builds: () => [], access: () => [] });
const total = computed(() => props.items.length + props.builds.length + props.access.length);
const emit = defineEmits<{ redeploy: [issue: DeploymentIssue]; reconcile: [issue: DeploymentIssue] }>();
const router = useRouter();

function productOf(issue: DeploymentIssue): string {
  const slug = issue.deployment.product;
  return slug ? (props.productNames.get(slug) ?? slug) : (issue.deployment.targetId ?? "Unknown product");
}

function pathOf(issue: DeploymentIssue): string {
  return `${ConsolePaths.deployments}/${encodeURIComponent(issue.deployment.id)}`;
}

/**
 * The issue's tone in the console's status vocabulary: a roll back and a regression after the deploy are degraded
 * (amber); a failure, a failed roll back, an interruption and a stuck rollout are red. @internal
 */
function toneOf(issue: DeploymentIssue): string {
  return issue.kind === DeploymentIssueKind.RolledBack || issue.kind === DeploymentIssueKind.Degraded
    ? "text-warning"
    : "text-destructive";
}

function describe(issue: DeploymentIssue): string {
  return `${productOf(issue)} ${issue.deployment.environment ?? issue.deployment.targetId ?? ""}`.trim();
}
</script>

<template>
  <Panel
    title="Issues"
    :description="`${total} ${total === 1 ? 'issue needs' : 'issues need'} attention.`"
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
          :class="['mt-0.5 md:mt-0', toneOf(issue)]"
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
            <FrozenBadge :product="issue.deployment.product" :environment="issue.deployment.environment" />
          </p>
          <p class="mt-1 text-sm">
            <span :class="toneOf(issue)">{{
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
      <li
        v-for="build in props.builds"
        :key="`build-${build.product}`"
        class="grid grid-cols-[1.25rem_minmax(0,1fr)] gap-x-3 gap-y-3 py-3 first:pt-0 last:pb-0 md:grid-cols-[1.25rem_minmax(0,1fr)_auto] md:items-center"
      >
        <Hammer :size="18" class="mt-0.5 text-destructive md:mt-0" aria-hidden="true" />
        <div class="min-w-0">
          <p class="flex flex-wrap items-center gap-x-2 gap-y-1">
            <RouterLink :to="ConsolePaths.product(build.product)" class="font-medium hover:text-primary focus-visible:outline-primary">{{
              props.productNames.get(build.product) ?? build.product
            }}</RouterLink>
            <Badge variant="neutral">main</Badge>
            <span v-if="build.commit" class="font-mono text-xs">{{ build.commit.slice(0, 7) }}</span>
          </p>
          <p class="mt-1 text-sm">
            <span class="text-destructive">{{ build.conclusion === "timed_out" ? "Main build timed out" : "Main build failed" }}</span>
            <span v-if="build.title" class="text-muted-foreground"> · {{ build.title }}</span>
          </p>
          <p class="mt-0.5 text-xs text-muted-foreground">
            <RelativeTime v-if="build.at" :value="build.at" /> · dev gets no new release until a build succeeds
          </p>
        </div>
        <div class="col-start-2 flex flex-wrap gap-2 md:col-start-3 md:justify-end">
          <Button as-child variant="ghost" tone="neutral" size="sm" class="max-md:min-h-11">
            <a
              :href="build.url"
              target="_blank"
              rel="noopener noreferrer"
              :aria-label="`Open the failed build of ${props.productNames.get(build.product) ?? build.product} on GitHub`"
              ><ArrowUpRight :size="14" aria-hidden="true" />Run</a
            >
          </Button>
        </div>
      </li>
      <li
        v-for="problem in props.access"
        :key="`github-${problem.owner ?? 'app'}-${problem.cause}`"
        class="grid grid-cols-[1.25rem_minmax(0,1fr)] gap-x-3 py-3 first:pt-0 last:pb-0"
      >
        <KeyRound :size="18" class="mt-0.5 text-warning" aria-hidden="true" />
        <div class="min-w-0">
          <p class="flex flex-wrap items-center gap-x-2 gap-y-1">
            <span class="font-medium">GitHub access</span>
            <Badge variant="neutral">{{ problem.owner ?? "GitHub App" }}</Badge>
          </p>
          <p class="mt-1 text-sm text-warning">{{ problem.cause }}</p>
          <p class="mt-0.5 text-xs text-muted-foreground"><RelativeTime :value="problem.at" /></p>
        </div>
      </li>
    </ul>
  </Panel>
</template>
