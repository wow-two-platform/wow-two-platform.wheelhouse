<script lang="ts">
import type { ControlTarget } from "@/domain/deployments/controls";

/** Defines the dev environment whose legacy files move into Files, and whether the dialog is open. */
export interface FilesMigrationModalProps {
  /** The dev environment (never Wheelhouse's own) whose files move. */
  readonly target: ControlTarget;
  /** The controlled dialog state. */
  readonly open: boolean;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ArrowLeft, FolderInput, Upload } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Alert, Spinner } from "@wow-two-beta/fe/presentation/feedback";
import {
  CheckboxField,
  Field,
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
  TextInput,
} from "@wow-two-beta/fe/presentation/forms";
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

import {
  useCancelFilesMigration,
  useDeploymentOutcome,
  useFilesMigrationManifest,
  useLatestFilesImport,
  useStartFilesMigration,
} from "@/application/deployments";
import { JobStatus } from "@/domain/deployments";
import {
  FilesMigrations,
  FilesMigrationStep,
  type FilesMigrationScope,
} from "@/domain/deployments/controls";
import { failureReason } from "@/integration/common";

import DeploymentSteps from "./DeploymentSteps.vue";
import FilesMigrationResults from "./FilesMigrationResults.vue";
import JobStatusBadge from "./JobStatusBadge.vue";

/**
 * Moves a dev environment's legacy files into Files in three steps over one wave or a set of sources, into one of the
 * environment's Files targets at one of its addresses. A plan (the default) counts and writes nothing; a push and a verify
 * ask a confirmation that names the source count and the Files host. A verify, and a push that resumes, take the import
 * id, prefilled from the target's newest files-migration job that kept one. The job then follows like a deploy: each
 * source's step with its `N files · B bytes`, a Cancel that reads cancelled — never failed — and, at the end, the
 * per-source results with the import id to copy.
 */
defineOptions({ name: "FilesMigrationModal" });
const props = defineProps<FilesMigrationModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const AllScope = "all";
const SourcesScope = "sources";

const manifest = useFilesMigrationManifest(
  () => props.target.id,
  () => props.open,
);
const latest = useLatestFilesImport(
  () => props.target.id,
  () => props.target.product,
  () => props.open,
);
const start = useStartFilesMigration();
const cancel = useCancelFilesMigration();

const stage = ref<"choose" | "confirm" | "follow">("choose");
const asked = ref<FilesMigrationStep>(FilesMigrationStep.Plan);
const filesTarget = ref("");
const filesUrl = ref("");
/** `all`, `wave:N` or `sources`. */
const scopeKey = ref(AllScope);
const ids = ref<string[]>([]);
const importId = ref("");
/** Whether the owner changed the import field, which the newest job's import then never overwrites. */
const importTouched = ref(false);
const jobId = ref<string | null>(null);
const outcome = useDeploymentOutcome(jobId, () => (jobId.value ? JobStatus.Queued : null));

const sources = computed(() => manifest.data.value?.sources ?? []);
const waves = computed(() => FilesMigrations.waves(sources.value));
const filesTargets = computed(() => manifest.data.value?.filesTargets ?? []);
const urls = computed(() => filesTargets.value.find((item) => item.target === filesTarget.value)?.urls ?? []);
const scope = computed<FilesMigrationScope>(() =>
  scopeKey.value === SourcesScope
    ? { kind: "sources", ids: ids.value }
    : scopeKey.value.startsWith("wave:")
      ? { kind: "wave", wave: Number(scopeKey.value.slice(5)) }
      : { kind: "all" },
);
const picked = computed(() => FilesMigrations.pick(sources.value, scope.value));
const ask = computed(() => ({
  step: asked.value,
  scope: scope.value,
  filesTarget: filesTarget.value,
  filesUrl: filesUrl.value,
  import: importId.value,
}));
const importProblem = computed(() =>
  importId.value.trim() && !FilesMigrations.isImport(importId.value.trim())
    ? "An import id is letters, digits, _ and -, up to 64."
    : "",
);
const refusal = computed(() => (manifest.error.value ? failureReason(manifest.error.value) : ""));
const busy = computed(() => start.loading.value);
const job = computed(() => outcome.data.value);
const following = computed(() => !job.value || outcome.pending.value);
const cancelled = computed(() => job.value?.status === JobStatus.Cancelled);
const succeeded = computed(() => job.value?.status === JobStatus.Succeeded);
/** The import id the job worked on: the one it kept, else the one it was asked for. */
const resultImport = computed(() => job.value?.filesMigration?.import ?? null);
const sourceOrder = computed(() => job.value?.operation?.sources ?? picked.value.map((source) => source.id));
const stepWord = computed(() => FilesMigrations.verb(asked.value));
const title = computed(() => {
  const status = job.value?.status;
  if (stage.value === "choose") return "Move files to Files";
  if (stage.value === "confirm") return FilesMigrations.question(asked.value, picked.value.length, filesUrl.value);
  if (!status || outcome.pending.value) return `${stepWord.value} in progress`;
  return status === JobStatus.Succeeded
    ? `${stepWord.value} finished`
    : status === JobStatus.Cancelled
      ? `${stepWord.value} cancelled`
      : `${stepWord.value} failed`;
});
/** Starts clean whenever the dialog opens. @internal */
watch(
  () => props.open,
  (open) => {
    if (!open) return;
    stage.value = "choose";
    asked.value = FilesMigrationStep.Plan;
    filesTarget.value = "";
    filesUrl.value = "";
    scopeKey.value = AllScope;
    ids.value = [];
    importId.value = "";
    importTouched.value = false;
    jobId.value = null;
    start.reset();
    cancel.reset();
    void manifest.refetch();
    void latest.refetch();
  },
  { immediate: true },
);

/** Preselects what the manifest leaves no choice about: the only Files target, its first address, the lowest wave. @internal */
watch(
  () => manifest.data.value,
  (read) => {
    if (!read || stage.value !== "choose") return;
    if (!filesTarget.value && read.filesTargets.length === 1) pickTarget(read.filesTargets[0]!.target);
    const first = FilesMigrations.waves(read.sources)[0];
    if (scopeKey.value === AllScope && first !== undefined) scopeKey.value = `wave:${first}`;
  },
  { immediate: true },
);

/** Prefills the import from the newest job that kept one, unless the owner has typed. @internal */
watch(
  () => latest.data.value,
  (value) => {
    if (value && !importTouched.value) importId.value = value;
  },
  { immediate: true },
);

/** Once a followed job ends, its import is what a verify or a resumed push names next. @internal */
watch(
  () => [job.value?.status, resultImport.value] as const,
  ([status, kept]) => {
    if (status && !outcome.pending.value && kept) {
      importId.value = kept;
      importTouched.value = true;
    }
  },
);

/** Picks a Files target and its first address. @internal */
function pickTarget(value: string | null): void {
  filesTarget.value = value ?? "";
  filesUrl.value = filesTargets.value.find((item) => item.target === filesTarget.value)?.urls[0] ?? "";
}

/** Toggles one source in the picked set. @internal */
function toggle(id: string, on: boolean): void {
  ids.value = on ? [...ids.value, id] : ids.value.filter((item) => item !== id);
}

/** Runs a plan at once; a push and a verify ask their confirmation first. @internal */
function choose(step: FilesMigrationStep): void {
  if (!readyFor(step)) return;
  asked.value = step;
  if (FilesMigrations.confirms(step)) stage.value = "confirm";
  else void submit();
}

/** Whether the choices are whole for a step: a Files target and address, sources, and the import a verify needs. @internal */
function readyFor(step: FilesMigrationStep): boolean {
  const candidate = { ...ask.value, step };
  return (
    FilesMigrations.isComplete(candidate, picked.value.length) && !importProblem.value
    && (step !== FilesMigrationStep.Verify || FilesMigrations.isImport(importId.value.trim()))
  );
}

/** Queues the asked step and follows its job. @internal */
async function submit(): Promise<void> {
  if (busy.value) return;
  const result = await start.mutateAsync({ target: props.target.id, ask: ask.value });
  if (!result.ok) {
    stage.value = "choose";
    return;
  }
  jobId.value = result.value.id;
  stage.value = "follow";
}

/** Asks the target to stop the running job; the job then reads cancelled. @internal */
async function stop(): Promise<void> {
  if (!jobId.value || cancel.loading.value) return;
  await cancel.mutateAsync({ target: props.target.id, job: jobId.value });
}

/** Returns to the choices with the job's import kept for the next step. @internal */
function another(): void {
  jobId.value = null;
  stage.value = "choose";
  start.reset();
  cancel.reset();
}

/** Closing a followed job only stops watching it: it keeps running on its target and stays in the deployments history. */
function close(open: boolean): void {
  emit("update:open", open);
}
</script>

<template>
  <Modal
    :open="props.open"
    :can-dismiss-on-outside-click="stage !== 'follow' && !busy"
    :can-dismiss-on-escape="!busy"
    @update:open="close"
  >
    <ModalContent size="lg" height="fixed">
      <ModalClose />
      <ModalHeader class="pr-8">
        <ModalTitle>{{ title }}</ModalTitle>
        <ModalDescription v-if="stage === 'choose'">
          Moves {{ props.target.id }}'s legacy files into a Files host of the same environment. A plan only counts; a push
          uploads; a verify compares.
        </ModalDescription>
        <ModalDescription v-else-if="stage === 'confirm'">
          {{ FilesMigrations.consequence(asked, props.target.id, filesUrl) }}
        </ModalDescription>
        <ModalDescription v-else>
          {{ props.target.id }} · {{ stepWord.toLowerCase() }} into {{ filesTarget }} at {{ FilesMigrations.host(filesUrl) }}
        </ModalDescription>
      </ModalHeader>

      <template v-if="stage === 'choose'">
        <ModalBody class="flex flex-col gap-4">
          <div v-if="manifest.loading.value" class="flex items-center gap-2 text-sm text-muted-foreground" role="status">
            <Spinner size="sm" label="Reading the manifests" />Reading the release's manifests…
          </div>
          <Alert v-else-if="refusal" severity="warning" :description="refusal" />
          <template v-else-if="manifest.data.value">
            <Alert
              v-if="!filesTargets.length"
              severity="warning"
              description="This environment has no Files target with an app site to move files into."
            />
            <Field label="Files target" is-required>
              <SelectPicker :model-value="filesTarget || null" @update:model-value="pickTarget">
                <SelectPickerTrigger aria-label="Files target"><SelectPickerValue placeholder="Choose a Files target" /></SelectPickerTrigger>
                <SelectPickerContent>
                  <SelectPickerItem v-for="item in filesTargets" :key="item.target" :item-key="item.target" :label="item.target" />
                </SelectPickerContent>
              </SelectPicker>
            </Field>
            <Field label="Address" is-required helper="Its app site's host or one of its aliases">
              <SelectPicker :model-value="filesUrl || null" @update:model-value="(value: string | null) => (filesUrl = value ?? '')">
                <SelectPickerTrigger aria-label="Files address" :is-disabled="!urls.length"><SelectPickerValue placeholder="Choose an address" /></SelectPickerTrigger>
                <SelectPickerContent>
                  <SelectPickerItem v-for="url in urls" :key="url" :item-key="url" :label="url" />
                </SelectPickerContent>
              </SelectPicker>
            </Field>
            <Field label="Sources" is-required :helper="`${picked.length} of ${sources.length} picked · tenant ${manifest.data.value.tenant}`">
              <SelectPicker :model-value="scopeKey" @update:model-value="(value: string | null) => (scopeKey = value ?? 'all')">
                <SelectPickerTrigger aria-label="Sources"><SelectPickerValue /></SelectPickerTrigger>
                <SelectPickerContent>
                  <SelectPickerItem :item-key="AllScope" label="Every source" />
                  <SelectPickerItem v-for="wave in waves" :key="wave" :item-key="`wave:${wave}`" :label="`Wave ${wave}`" />
                  <SelectPickerItem :item-key="SourcesScope" label="Pick sources…" />
                </SelectPickerContent>
              </SelectPicker>
            </Field>
            <ul v-if="scopeKey === SourcesScope" class="flex flex-col gap-1 rounded-xl border border-border p-3" aria-label="Sources to move">
              <li v-for="source in sources" :key="source.id">
                <CheckboxField
                  :model-value="ids.includes(source.id)"
                  :label="source.id"
                  :description="`${source.kind}${source.wave ? ` · wave ${source.wave}` : ''}${source.repository ? ` · ${source.repository}` : ''}`"
                  @update:model-value="(on: boolean) => toggle(source.id, on)"
                />
              </li>
            </ul>
            <Field
              label="Import"
              :helper="
                importId
                  ? 'A verify checks this import and a push resumes it; clear it to open a new one on a push.'
                  : 'A verify needs an import; a push without one opens a new import.'
              "
              :error="importProblem"
            >
              <TextInput
                v-model="importId"
                class="font-mono"
                autocomplete="off"
                autocapitalize="off"
                spellcheck="false"
                placeholder="imp-…"
                @update:model-value="importTouched = true"
              />
            </Field>
          </template>
          <Alert v-if="start.error.value" severity="danger" :description="failureReason(start.error.value)" />
        </ModalBody>
        <ModalFooter>
          <Button variant="outline" tone="neutral" @click="emit('update:open', false)">Close</Button>
          <Button variant="outline" tone="neutral" :is-disabled="!readyFor('verify') || busy" @click="choose('verify')">Verify</Button>
          <Button variant="outline" tone="warning" :is-disabled="!readyFor('push') || busy" @click="choose('push')">
            <template #leading><Upload :size="16" /></template>
            Push
          </Button>
          <Button variant="solid" tone="primary" :is-disabled="!readyFor('plan')" :is-loading="busy" @click="choose('plan')">
            <template #leading><FolderInput :size="16" /></template>
            Plan
          </Button>
        </ModalFooter>
      </template>

      <template v-else-if="stage === 'confirm'">
        <ModalBody class="flex flex-col gap-4">
          <dl class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-3 rounded-xl border border-border p-4 text-sm">
            <dt class="text-muted-foreground">Environment</dt>
            <dd class="font-mono text-xs">{{ props.target.id }}</dd>
            <dt class="text-muted-foreground">Files host</dt>
            <dd class="font-mono text-xs">{{ FilesMigrations.host(filesUrl) }} · {{ filesTarget }}</dd>
            <dt class="text-muted-foreground">Sources</dt>
            <dd class="font-mono text-xs break-all">{{ picked.map((source) => source.id).join(", ") }}</dd>
            <dt class="text-muted-foreground">Import</dt>
            <dd class="font-mono text-xs">
              {{ importId.trim() || "A new import opens on Files" }}
            </dd>
          </dl>
          <Alert v-if="start.error.value" severity="danger" :description="failureReason(start.error.value)" />
        </ModalBody>
        <ModalFooter>
          <Button variant="outline" tone="neutral" :is-disabled="busy" @click="stage = 'choose'">
            <template #leading><ArrowLeft :size="16" /></template>
            Back
          </Button>
          <Button variant="solid" tone="warning" :is-loading="busy" @click="submit()">
            {{ FilesMigrations.verb(asked) }} {{ picked.length }} {{ picked.length === 1 ? "source" : "sources" }}
          </Button>
        </ModalFooter>
      </template>

      <template v-else>
        <ModalBody class="flex flex-col gap-4">
          <div v-if="job" class="flex items-center gap-2 text-sm">
            <JobStatusBadge :status="job.status" />
            <span v-if="following" class="text-muted-foreground">{{ cancel.loading.value ? "Cancelling…" : "Following the job…" }}</span>
          </div>
          <div v-else class="flex items-center gap-2 text-sm text-muted-foreground" role="status">
            <Spinner size="sm" label="Queued" />Queued…
          </div>
          <DeploymentSteps :steps="job?.steps" />
          <Alert v-if="cancelled" severity="info" title="Cancelled" description="The mover was stopped. Files already pushed stay in the import; a push can resume it." />
          <Alert
            v-else-if="job && !following && !succeeded"
            severity="danger"
            :title="`${stepWord} failed`"
            :description="job.reason ?? 'The target could not finish the job.'"
          />
          <FilesMigrationResults :run="job?.filesMigration" :order="sourceOrder" />
          <Alert v-if="cancel.error.value" severity="danger" :description="failureReason(cancel.error.value)" />
        </ModalBody>
        <ModalFooter>
          <Button v-if="following" variant="outline" tone="danger" :is-loading="cancel.loading.value" :is-disabled="!job" @click="stop()">Cancel</Button>
          <template v-else>
            <Button variant="outline" tone="neutral" @click="another()">Another step</Button>
            <Button variant="solid" tone="primary" @click="emit('update:open', false)">Close</Button>
          </template>
        </ModalFooter>
      </template>
    </ModalContent>
  </Modal>
</template>
