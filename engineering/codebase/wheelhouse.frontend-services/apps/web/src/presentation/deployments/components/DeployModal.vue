<script lang="ts">
/** Defines a target and release bundle preselected for a new deployment session. */
export interface DeploySelection {
  readonly target: string;
  readonly release: string;
}

/** Defines the controlled deployment dialog and its initial selection. */
export interface DeployModalProps {
  /** The controlled dialog state. */
  readonly open: boolean;
  /** The selection applied on opening; each new session resets the workflow. */
  readonly selection?: DeploySelection | null;
}
</script>

<script setup lang="ts">
import { computed, ref, shallowRef, watch } from "vue";
import { ArrowLeft, Hammer, Rocket, ShieldCheck } from "lucide-vue-next";

import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { Alert, Spinner } from "@wow-two-beta/ui-vue/presentation/feedback";
import {
  Field,
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
  CheckboxField,
  TextInput,
} from "@wow-two-beta/ui-vue/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalDescription,
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from "@wow-two-beta/ui-vue/presentation/overlays";

import {
  useDeploymentOutcome,
  useDeploymentTargets,
  useProductBranches,
  useProductCommits,
  useReleaseArtifacts,
  useRequestBuild,
  useStartDeployment,
  useTargetCheck,
  useTargetState,
} from "@/application/deployments";
import {
  JobStatus,
  type DeploymentTarget,
  type ReleaseArtifact,
  type TargetCheck,
} from "@/domain/deployments";

import CheckResultList from "./CheckResultList.vue";
import JobStatusBadge from "./JobStatusBadge.vue";
import DeploymentSteps from "./DeploymentSteps.vue";
import TargetSites from "./TargetSites.vue";
import { failureReason } from "@/integration/common";

/** Renders choose, confirm and follow stages for deploying one complete release bundle. */
defineOptions({ name: "DeployModal" });
const props = defineProps<DeployModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
const targets = useDeploymentTargets();
const releases = useReleaseArtifacts();
const check = useTargetCheck();
const start = useStartDeployment();
const step = ref<"choose" | "confirm" | "follow">("choose");
const target = ref("");
const release = ref("");
const jobId = ref<string | null>(null);
const readiness = shallowRef<TargetCheck | null>(null);
const outcome = useDeploymentOutcome(jobId);
const selectedTarget = computed(() =>
  targets.data.value?.find((item) => item.id === target.value),
);
// Dev takes a build of any commit or branch; test also takes a `test` branch build; prod takes releases only.
const takesBuilds = computed(
  () => selectedTarget.value?.acceptsCandidates === true,
);
const available = computed(() =>
  (releases.data.value ?? []).filter(
    (item) =>
      item.product === selectedTarget.value?.product &&
      (takesBuilds.value ||
        item.kind === "release" ||
        (selectedTarget.value?.acceptsTestBuilds === true &&
          item.branch === "test")),
  ),
);
const typed = ref("");
const skipTest = ref(false);
const branch = ref("main");
const requested = ref<string | null>(null);
const buildProduct = () =>
  takesBuilds.value ? (selectedTarget.value?.product ?? null) : null;
const branches = useProductBranches(buildProduct);
const commits = useProductCommits(buildProduct, branch);
/** True when the product has no build workflow: unbuilt commits then offer no Build action. */
const cannotBuild = computed(() =>
  (commits.data.value ?? []).some((entry) => entry.canBuild === false),
);
const build = useRequestBuild();
// Local prod always needs the typed target ID; any prod needs it to skip the test pass.
const typedNeeded = computed(
  () => Boolean(selectedTarget.value?.needsConfirmation) || skipTest.value,
);
const confirmed = computed(
  () => !typedNeeded.value || typed.value === target.value,
);
const typedLabel = computed(() =>
  selectedTarget.value?.needsConfirmation && skipTest.value
    ? `Type ${target.value} to confirm`
    : selectedTarget.value?.needsConfirmation
      ? `Type ${target.value} to deploy prod to the local server`
      : `Type ${target.value} to deploy without a test pass`,
);
const settled = computed(
  () => outcome.data.value?.status === JobStatus.Succeeded,
);
const deployed = useTargetState(() =>
  step.value === "follow" && settled.value ? target.value : null,
);
const selectedRelease = computed(() =>
  available.value.find((item) => item.id === release.value),
);
const inventoryError = computed(
  () => targets.error.value ?? releases.error.value,
);
const canChoose = computed(() =>
  Boolean(selectedTarget.value && selectedRelease.value),
);
const lockedFailure = computed(
  () =>
    outcome.data.value?.mutationStarted &&
    (outcome.data.value.status === JobStatus.Failed ||
      outcome.data.value.status === JobStatus.RollbackFailed),
);

/** Starts a fresh selection when the controlled dialog opens. */
watch(
  () => props.open,
  (open) => {
    if (!open) return;
    step.value = "choose";
    target.value = props.selection?.target ?? "";
    release.value = props.selection?.release ?? "";
    jobId.value = null;
    readiness.value = null;
    typed.value = "";
    skipTest.value = false;
    requested.value = null;
    check.reset();
    start.reset();
    build.reset();
    void releases.refetch();
  },
  { immediate: true },
);

/** Clears readiness whenever its exact target or release changes. */
watch([target, release], () => {
  readiness.value = null;
  typed.value = "";
  skipTest.value = false;
  check.reset();
});

/** Reads the verified release's sites once the rollout succeeds, so the dialog can open them. */
watch(settled, (value) => {
  if (value) void deployed.refetch();
});

/** Formats an actual runner target for the environment picker. */
function targetLabel(item?: DeploymentTarget): string {
  return item ? `${item.product} · ${item.environment} · ${item.host}` : "";
}

/** Formats a catalog release without substituting a bundle identifier; a commit's build names its branch. */
function releaseLabel(item?: ReleaseArtifact): string {
  if (!item) return "";
  if (item.kind === "candidate")
    return `${item.release} · ${item.branch ?? "commit"} build`;
  return item.release + (item.prerelease ? " (prerelease)" : "");
}

/** Selects a commit's existing build, re-reading the catalog when the build is newer than it. */
async function useBuild(buildId: string): Promise<void> {
  if (!available.value.some((item) => item.id === buildId))
    await releases.refetch();
  release.value = buildId;
}

/** Starts a build of a commit that has none; the build joins the catalog when its workflow finishes. */
async function requestBuild(commit: string): Promise<void> {
  const product = selectedTarget.value?.product;
  if (!product || build.loading.value) return;
  requested.value = commit;
  const result = await build.mutateAsync({ product, commit });
  if (!result.ok) requested.value = null;
}

/** Changes targets and clears the incompatible bundle selection. */
function selectTarget(value: string | null): void {
  target.value = value ?? "";
  release.value = "";
}

/** Records readiness only if the inspected target and bundle remain selected. */
async function inspect(): Promise<void> {
  if (!selectedTarget.value || check.loading.value) return;
  const inspectedTarget = target.value;
  const inspectedRelease = release.value;
  const result = await check.mutateAsync({
    target: inspectedTarget,
    ...(inspectedRelease ? { release: inspectedRelease } : {}),
  });
  if (
    result.ok &&
    target.value === inspectedTarget &&
    release.value === inspectedRelease
  )
    readiness.value = result.value;
}

/** Enters confirmation only for a bundle belonging to the selected target's literal product. */
function confirmSelection(): void {
  if (canChoose.value) step.value = "confirm";
}

/** Submits the confirmed bundle and follows only an acknowledged submission. */
async function deploy(): Promise<void> {
  if (!canChoose.value || start.loading.value) return;
  if (!confirmed.value) return;
  const result = await start.mutateAsync({
    target: target.value,
    release: release.value,
    ...(typedNeeded.value ? { confirm: typed.value } : {}),
    ...(skipTest.value ? { skipTestPass: true } : {}),
  });
  if (!result.ok) return;
  jobId.value = result.value.id;
  step.value = "follow";
}

/** Requests dismissal without interrupting an already submitted rollout. */
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
    <ModalContent class="flex max-h-[calc(100dvh-2rem)] w-full max-w-xl flex-col">
      <template v-if="step === 'choose'">
        <ModalHeader>
          <ModalTitle>Deploy a release</ModalTitle>
          <ModalDescription
            >Choose an environment and a published release, then check
            readiness.</ModalDescription
          >
        </ModalHeader>
        <ModalBody class="-mx-1 flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-1">
          <div
            v-if="targets.loading.value || releases.loading.value"
            class="flex justify-center py-6"
          >
            <Spinner label="Loading targets and releases" />
          </div>
          <Alert
            v-else-if="inventoryError"
            severity="danger"
            title="Couldn't load deployment inventory"
            :description="inventoryError.message"
          >
            <template #actions>
              <Button
                variant="soft"
                tone="danger"
                size="sm"
                @click="
                  targets.refetch();
                  releases.refetch();
                "
                >Retry</Button
              >
            </template>
          </Alert>
          <Alert
            v-else-if="!targets.data.value?.length"
            severity="info"
            title="No deployment targets"
            description="No deployment environment has been configured for this workspace."
          />
          <template v-else>
            <Field label="Environment">
              <SelectPicker
                :model-value="target || null"
                :is-disabled="check.loading.value"
                :get-option-label="
                  (key: string) =>
                    targetLabel(
                      targets.data.value?.find((item) => item.id === key),
                    )
                "
                @update:model-value="selectTarget"
              >
                <SelectPickerTrigger aria-label="Environment">
                  <SelectPickerValue placeholder="Select an environment" />
                </SelectPickerTrigger>
                <SelectPickerContent>
                  <SelectPickerItem
                    v-for="item in targets.data.value"
                    :key="item.id"
                    :item-key="item.id"
                    :label="targetLabel(item)"
                  />
                </SelectPickerContent>
              </SelectPicker>
            </Field>
            <Field
              label="Release"
              :helper="
                target && !available.length
                  ? 'No published release is available yet.'
                  : ''
              "
            >
              <SelectPicker
                :model-value="release || null"
                :is-disabled="!available.length || check.loading.value"
                :get-option-label="
                  (key: string) =>
                    releaseLabel(available.find((item) => item.id === key))
                "
                @update:model-value="
                  (value: string | null) => (release = value ?? '')
                "
              >
                <SelectPickerTrigger aria-label="Release">
                  <SelectPickerValue placeholder="Select a published release" />
                </SelectPickerTrigger>
                <SelectPickerContent>
                  <SelectPickerItem
                    v-for="item in available"
                    :key="item.id"
                    :item-key="item.id"
                    :label="releaseLabel(item)"
                  />
                </SelectPickerContent>
              </SelectPicker>
            </Field>
            <details
              v-if="takesBuilds"
              class="rounded-xl border border-border p-4 text-sm"
            >
              <summary class="cursor-pointer font-medium">
                Deploy a commit
              </summary>
              <div class="mt-3 flex flex-col gap-3">
                <Field label="Branch">
                  <SelectPicker
                    :model-value="branch"
                    :get-option-label="(key: string) => key"
                    @update:model-value="
                      (value: string | null) => (branch = value ?? 'main')
                    "
                  >
                    <SelectPickerTrigger aria-label="Branch">
                      <SelectPickerValue placeholder="Select a branch" />
                    </SelectPickerTrigger>
                    <SelectPickerContent>
                      <SelectPickerItem
                        v-for="name in branches.data.value ?? [branch]"
                        :key="name"
                        :item-key="name"
                        :label="name"
                      />
                    </SelectPickerContent>
                  </SelectPicker>
                </Field>
                <Alert
                  v-if="commits.error.value || branches.error.value"
                  severity="warning"
                  title="Couldn't read commits"
                  :description="
                    (commits.error.value ?? branches.error.value)?.message ?? ''
                  "
                />
                <div v-else-if="commits.loading.value" class="py-2">
                  <Spinner size="sm" label="Reading commits" />
                </div>
                <p
                  v-else-if="cannotBuild"
                  class="text-xs text-muted-foreground"
                >
                  This product has no build workflow, so unbuilt commits cannot be built here.
                </p>
                <ul
                  v-if="!commits.error.value && !branches.error.value && !commits.loading.value"
                  class="flex flex-col divide-y divide-border"
                  aria-label="Commits"
                >
                  <li
                    v-for="entry in (commits.data.value ?? []).slice(0, 10)"
                    :key="entry.sha"
                    class="flex items-center gap-3 py-2"
                  >
                    <span class="flex min-w-0 flex-1 items-baseline gap-2">
                      <a
                        v-if="entry.url"
                        :href="entry.url"
                        target="_blank"
                        rel="noopener noreferrer"
                        class="shrink-0 font-mono text-xs text-primary hover:underline"
                        :title="`Open ${entry.sha.slice(0, 7)} on GitHub`"
                        >{{ entry.sha.slice(0, 7) }}</a
                      >
                      <span v-else class="shrink-0 font-mono text-xs">{{
                        entry.sha.slice(0, 7)
                      }}</span>
                      <span
                        class="min-w-0 truncate text-muted-foreground"
                        :title="entry.message"
                        >{{ entry.message }}</span
                      >
                    </span>
                    <Button
                      v-if="entry.buildId"
                      size="sm"
                      variant="outline"
                      tone="neutral"
                      @click="useBuild(entry.buildId)"
                      >Use build</Button
                    >
                    <Button
                      v-else-if="entry.canBuild !== false"
                      size="sm"
                      variant="soft"
                      tone="primary"
                      :is-loading="build.loading.value && requested === entry.sha"
                      :is-disabled="requested === entry.sha"
                      @click="requestBuild(entry.sha)"
                    >
                      <template #leading><Hammer :size="14" /></template>
                      {{ requested === entry.sha ? "Build requested" : "Build" }}
                    </Button>
                  </li>
                </ul>
                <Alert
                  v-if="build.error.value"
                  severity="danger"
                  title="Build refused"
                  :description="failureReason(build.error.value)"
                />
                <p v-else-if="requested" class="text-xs text-muted-foreground">
                  The build joins the release list when its workflow finishes.
                </p>
              </div>
            </details>
            <Alert
              v-if="check.error.value"
              severity="danger"
              title="Check unavailable"
              :description="failureReason(check.error.value)"
            />
            <div
              v-if="readiness"
              class="flex flex-col gap-3 rounded-xl border border-border p-4"
            >
              <p class="text-sm font-medium">
                {{
                  readiness.ok
                    ? "Ready to deploy"
                    : "Resolve failed checks before deploying"
                }}
              </p>
              <CheckResultList :check="readiness" />
            </div>
          </template>
        </ModalBody>
        <ModalFooter>
          <Button
            variant="outline"
            tone="neutral"
            :is-loading="check.loading.value"
            :is-disabled="!selectedTarget"
            @click="inspect"
          >
            <template #leading><ShieldCheck :size="16" /></template>
            Check readiness
          </Button>
          <Button
            variant="solid"
            tone="primary"
            :is-disabled="!canChoose || check.loading.value"
            @click="confirmSelection"
            >Continue</Button
          >
        </ModalFooter>
      </template>
      <template v-else-if="step === 'confirm'">
        <ModalHeader>
          <ModalTitle
            >Deploy {{ selectedRelease?.release }} to {{ target }}?</ModalTitle
          >
          <ModalDescription>
            The target pulls every image, replaces its containers, and waits for
            health and smoke checks. Replacement includes a short restart
            window.
          </ModalDescription>
        </ModalHeader>
        <ModalBody class="-mx-1 flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-1">
          <dl
            class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-3 rounded-xl border border-border p-4 text-sm"
          >
            <dt class="text-muted-foreground">Environment</dt>
            <dd>{{ targetLabel(selectedTarget) }}</dd>
            <dt class="text-muted-foreground">Release</dt>
            <dd class="font-mono">{{ releaseLabel(selectedRelease) }}</dd>
            <dt class="text-muted-foreground">Readiness</dt>
            <dd>
              {{
                !readiness
                  ? "Not checked"
                  : readiness.ok
                    ? "Ready"
                    : "Failed checks; the runner refuses deployment"
              }}
            </dd>
          </dl>
          <template v-if="selectedTarget?.requiresTestPass">
            <p class="text-sm text-muted-foreground">
              Prod takes a release only after it succeeded on test. Check
              readiness to see whether this one has.
            </p>
            <CheckboxField
              v-model="skipTest"
              label="Deploy without a test pass"
              :disabled="start.loading.value"
            />
          </template>
          <Field v-if="typedNeeded" :label="typedLabel">
            <TextInput
              v-model="typed"
              :disabled="start.loading.value"
              autocomplete="off"
            />
          </Field>
          <Alert
            v-if="!canChoose"
            severity="warning"
            description="The selected target or release is no longer available."
          />
          <Alert
            v-if="start.error.value"
            severity="danger"
            title="Deployment refused"
            :description="failureReason(start.error.value)"
          />
        </ModalBody>
        <ModalFooter>
          <Button
            variant="outline"
            tone="neutral"
            :is-disabled="start.loading.value"
            @click="step = 'choose'"
          >
            <template #leading><ArrowLeft :size="16" /></template>Back
          </Button>
          <Button
            variant="solid"
            tone="primary"
            :is-loading="start.loading.value"
            :is-disabled="!canChoose || !confirmed"
            @click="deploy"
          >
            <template #leading><Rocket :size="16" /></template>Deploy
          </Button>
        </ModalFooter>
      </template>
      <template v-else>
        <ModalHeader>
          <ModalTitle
            >{{
              outcome.pending.value || !outcome.data.value
                ? "Deploying"
                : "Deployment settled"
            }}
            {{ selectedRelease?.release }}</ModalTitle
          >
          <ModalDescription
            >Closing keeps the rollout running. Deployment history records its
            outcome.</ModalDescription
          >
        </ModalHeader>
        <ModalBody class="-mx-1 flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-1">
          <div
            role="status"
            class="flex flex-col gap-3 rounded-xl border border-border p-4"
          >
            <div class="flex flex-wrap items-center gap-2 text-sm">
              <span
                >Deployment
                <span class="font-mono">{{ jobId?.slice(0, 8) }}</span></span
              >
              <JobStatusBadge
                v-if="outcome.data.value"
                :status="outcome.data.value.status"
              />
              <Spinner v-else size="sm" label="Waiting for the runner" />
            </div>
            <p
              v-if="outcome.data.value?.reason"
              class="text-sm text-muted-foreground"
            >
              {{ outcome.data.value.reason }}
            </p>
            <DeploymentSteps :steps="outcome.data.value?.steps" />
          </div>
          <Alert
            v-for="warning in outcome.data.value?.warnings ?? []"
            :key="warning"
            severity="warning"
            :description="warning"
          />
          <TargetSites
            v-if="settled"
            :sites="deployed.data.value?.current?.sites"
            :versions="deployed.data.value?.current?.versions"
          />
          <Alert
            v-if="lockedFailure"
            severity="warning"
            description="The target stays locked until an operator inspects and reconciles it."
          />
          <Alert
            v-if="outcome.error.value"
            severity="warning"
            title="Outcome unavailable"
            :description="outcome.error.value.message"
          >
            <template #actions>
              <Button
                size="sm"
                variant="outline"
                tone="neutral"
                @click="outcome.refetch()"
                >Retry</Button
              >
            </template>
          </Alert>
        </ModalBody>
        <ModalFooter
          ><Button variant="outline" tone="neutral" @click="close(false)"
            >Close</Button
          ></ModalFooter
        >
      </template>
    </ModalContent>
  </Modal>
</template>
