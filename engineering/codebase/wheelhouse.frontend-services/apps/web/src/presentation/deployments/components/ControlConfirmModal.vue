<script lang="ts">
import type { ControlRequest } from "@/domain/deployments/controls";

/** The control the owner asked for and whether its confirmation is open. */
export interface ControlConfirmModalProps {
  /** The stop, start, restart, database reset, set reset or run to confirm; null while none is asked for. */
  readonly request: ControlRequest | null;
  /** The controlled dialog state. */
  readonly open: boolean;
}
</script>

<script setup lang="ts">
import { computed, ref, shallowRef, watch } from "vue";
import { Eraser, Pause, Play, RotateCw, SquareTerminal } from "lucide-vue-next";

import { Button, CopyButton } from "@wow-two-beta/fe/presentation/actions";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import { Field, FilePicker, TextInput } from "@wow-two-beta/fe/presentation/forms";
import { TypedConfirmModal } from "@wow-two-beta/fe/presentation/overlays";

import { useTargetControl } from "@/application/deployments";
import { JobStatus } from "@/domain/deployments";
import { CommandLine, ControlOperation, RunFiles, ServiceControls, type ControlRun } from "@/domain/deployments/controls";
import { failureReason } from "@/integration/common";

/**
 * Confirms a stop, start, restart, database reset, set reset or run before it is queued: dev asks once, prod asks for its
 * target ID typed and a reason. The job then reads in the environment's deployments like a deploy; the dialog stays until
 * the target ends it. A run also asks for its command line, split into arguments the way a shell would, and stays open
 * after it to show the exit code and the last output line. The typed command lives in this dialog only — never logged,
 * stored or kept once the dialog opens again.
 */
defineOptions({ name: "ControlConfirmModal" });
const props = defineProps<ControlConfirmModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const control = useTargetControl();
const reason = ref("");
/** A run's command line as typed; empty whenever the dialog opens. */
const command = ref("");
/** A file the run's command reads on its stdin, if any; it lives in this dialog only and goes to the target once. */
const file = shallowRef<File | null>(null);
/** Why the chosen file cannot go to the run's stdin; null while it can, or none is chosen. */
const fileProblem = computed(() => (file.value ? RunFiles.problem(file.value.size) : null));
/** While the queued control runs on its target: what it is doing. */
const progress = ref<string | null>(null);
/** Why the target failed or refused the control, once it ended so. */
const outcome = ref<string | null>(null);
/** How a run's command ended, once it succeeded: the dialog stays open on it. */
const finished = ref<ControlRun | null>(null);
/** How a run's command ended, once it failed or timed out: the target's reason shows it, and its last line copies. */
const failed = ref<ControlRun | null>(null);
/** While the control is queued and followed: nothing typed changes it any more. */
const submitting = ref(false);
/** A finished run whose last line the target kept as printed, for one read: its target and job. */
const revealable = ref<{ target: string; job: string } | null>(null);
/** The run's last line as printed, once read; it lives in this dialog only. */
const revealed = ref<string | null>(null);
/** Why the one read was refused. */
const revealProblem = ref<string | null>(null);
const label = computed(() => (props.request ? ServiceControls.labelOfRequest(props.request) : ""));
const title = computed(() => (props.request ? ServiceControls.titleOf(props.request) : ""));
const isRun = computed(() => props.request?.operation === ControlOperation.Run);
const parsed = computed(() => CommandLine.split(command.value));
/** The arguments the typed command line splits to; none while it is empty or cannot be split. */
const args = computed(() => (parsed.value.ok ? parsed.value.args : []));
/** Why the typed command line cannot be split; empty while it can, or is empty. */
const commandProblem = computed(() => (parsed.value.ok ? "" : parsed.value.error));
const guarded = computed(() => (props.request ? ServiceControls.isGuarded(props.request.target) : false));
/** Whether everything the control needs is filled: a prod reason, a run's command. */
const complete = computed(
  () =>
    (!guarded.value || reason.value.trim().length > 0) &&
    (!isRun.value || args.value.length > 0) &&
    !fileProblem.value &&
    !finished.value,
);
const tone = computed(() =>
  props.request?.operation === ControlOperation.Stop || props.request?.operation === ControlOperation.Reset
    ? "danger"
    : props.request?.operation === ControlOperation.Restart || props.request?.operation === ControlOperation.Run
      ? "warning"
      : "primary",
);
const icon = computed(() =>
  props.request?.operation === ControlOperation.Reset
    ? Eraser
    : props.request?.operation === ControlOperation.Stop
      ? Pause
      : props.request?.operation === ControlOperation.Restart
        ? RotateCw
        : props.request?.operation === ControlOperation.Run
          ? SquareTerminal
          : Play,
);
const error = computed(() => outcome.value ?? failureReason(control.error.value));

/** Clears the reason, the command and the last result or refusal whenever the dialog opens on another control.
 * @internal */
watch(
  () => [props.open, props.request?.target.id, props.request?.operation, props.request?.service, props.request?.set?.name],
  () => {
    reason.value = "";
    command.value = "";
    file.value = null;
    progress.value = null;
    outcome.value = null;
    finished.value = null;
    failed.value = null;
    revealable.value = null;
    revealed.value = null;
    revealProblem.value = null;
    control.reset();
  },
);

/** Queues the control — prod sends the typed ID and the reason, a run its split command — and stays until the target
 * ends it: a success closes the dialog, a failure or refusal keeps it open with the target's reason. A run that
 * succeeded keeps it open too, on how its command ended. @internal */
async function submit(typed: string) {
  const request = props.request;
  if (!request) return;
  // A run again from the same dialog starts clean: the last run's result and its show-once line belong to its own job.
  outcome.value = null;
  finished.value = null;
  failed.value = null;
  revealable.value = null;
  revealed.value = null;
  revealProblem.value = null;
  submitting.value = true;
  try {
    const input = isRun.value && file.value ? RunFiles.toBase64(new Uint8Array(await file.value.arrayBuffer())) : null;
    const result = await control.mutateAsync({
      target: request.target.id,
      operation: request.operation,
      service: request.service,
      set: request.set?.name ?? null,
      command: isRun.value ? args.value : null,
      input,
      ...(guarded.value ? { reason: reason.value, confirm: typed } : {}),
    });
    if (!result.ok) return result;
    progress.value = `${label.value}…`;
    const ended = await control.follow(result.value.id, request.target.id);
    progress.value = null;
    if (ended?.run?.reveal) revealable.value = { target: request.target.id, job: result.value.id };
    if (ended && ended.status !== JobStatus.Succeeded) {
      failed.value = ended.run ?? null;
      outcome.value =
        ended.reason || (ended.run ? ServiceControls.runResult(ended.run) : `The target could not ${request.operation} it (${ended.status}).`);
      throw new Error(outcome.value);
    }
    if (ended?.run) {
      // The modal closes on a settled confirm and stays open on a refused one; the run's result is neither an error nor
      // shown as one, only a reason to keep the dialog up until the owner closes it.
      finished.value = ended.run;
      throw new Error(ServiceControls.runResult(ended.run));
    }
    return result;
  } finally {
    submitting.value = false;
  }
}

/** Keeps the first file the picker chose, or none. @internal */
function pick(files: FileList | null) {
  file.value = files?.[0] ?? null;
}

/** Reads the run's last line as printed, once: the target removes it with this read. @internal */
async function reveal() {
  const job = revealable.value;
  if (!job) return;
  revealProblem.value = null;
  const read = await control.reveal(job.target, job.job);
  revealable.value = null;
  if (read.ok) revealed.value = read.value.line;
  else revealProblem.value = failureReason(read.failure) || "The line could not be read.";
}
</script>

<template>
  <TypedConfirmModal
    :open="props.open && Boolean(props.request)"
    :title="title"
    :description="props.request ? ServiceControls.consequence(props.request) : ''"
    v-bind="guarded && props.request ? { expected: props.request.target.id } : {}"
    :can-confirm="complete"
    :confirm-label="props.request ? ServiceControls.verb(props.request.operation) : ''"
    :cancel-label="finished ? 'Close' : 'Cancel'"
    :tone="tone"
    :error="error"
    @update:open="emit('update:open', $event)"
    @confirm="submit"
  >
    <template #confirm-icon><component :is="icon" :size="16" aria-hidden="true" /></template>
    <Field v-if="isRun" label="Command" is-required :is-read-only="submitting || Boolean(finished)" :error="commandProblem">
      <TextInput
        v-model="command"
        class="font-mono"
        autocomplete="off"
        autocapitalize="off"
        spellcheck="false"
        placeholder="dotnet Identity.Api.dll admin create --slug …"
      />
    </Field>
    <Field
      v-if="isRun"
      label="Stdin file"
      helper="Optional: a file the command reads on its stdin, 2 MiB at most; sent once, kept nowhere"
      :is-read-only="submitting || Boolean(finished)"
      :error="fileProblem ?? ''"
    >
      <FilePicker
        label="Choose a file"
        :preview="file ? `${file.name} · ${file.size} bytes` : 'No file'"
        @files-change="pick"
      />
    </Field>
    <Field v-if="guarded" label="Reason" is-required>
      <TextInput v-model="reason" autocomplete="off" maxlength="300" />
    </Field>
    <p v-if="progress" role="status" class="text-sm text-muted-foreground">{{ progress }}</p>
    <Alert v-if="finished" role="status" severity="success" :title="ServiceControls.runResult(finished)">
      <div v-if="finished.lastLine" class="flex items-start gap-2">
        <p class="min-w-0 flex-1 break-all font-mono text-xs">{{ finished.lastLine }}</p>
        <CopyButton
          :text="finished.lastLine"
          size="sm"
          variant="outline"
          tone="neutral"
          shape="square"
          class="max-md:size-11"
          aria-label="Copy the last line"
          copied-aria-label="Last line copied"
        />
      </div>
      <p v-else class="text-xs">The command printed no output.</p>
      <p v-if="finished.input" class="mt-1 text-xs">{{ RunFiles.label(finished.input) }}</p>
      <details v-if="finished.tail?.length" class="mt-2 text-xs">
        <summary class="cursor-pointer">Earlier output ({{ finished.tail.length }} lines)</summary>
        <pre class="mt-1 max-h-60 overflow-auto font-mono break-all whitespace-pre-wrap">{{ finished.tail.join("\n") }}</pre>
      </details>
    </Alert>
    <details v-if="failed?.tail?.length" class="text-xs">
      <summary class="cursor-pointer">Earlier output ({{ failed.tail.length }} lines)</summary>
      <pre class="mt-1 max-h-60 overflow-auto font-mono break-all whitespace-pre-wrap">{{ failed.tail.join("\n") }}</pre>
    </details>
    <div v-if="failed?.lastLine" class="flex items-center justify-end gap-2">
      <span class="text-xs text-muted-foreground">Copy the last line it printed</span>
      <CopyButton
        :text="failed.lastLine"
        size="sm"
        variant="outline"
        tone="neutral"
        shape="square"
        class="max-md:size-11"
        aria-label="Copy the last line"
        copied-aria-label="Last line copied"
      />
    </div>
    <div v-if="revealable" class="flex flex-wrap items-center gap-2">
      <Button size="sm" variant="outline" @click="reveal">Show the full line once</Button>
      <span class="text-xs text-muted-foreground">It holds something the job keeps cut, such as an invitation link.</span>
    </div>
    <Alert v-if="revealed" severity="warning" title="Shown once: copy it now">
      <div class="flex items-start gap-2">
        <p class="min-w-0 flex-1 select-all break-all font-mono text-xs">{{ revealed }}</p>
        <CopyButton
          :text="revealed"
          size="sm"
          variant="outline"
          tone="neutral"
          shape="square"
          class="max-md:size-11"
          aria-label="Copy the full line"
          copied-aria-label="Full line copied"
        />
      </div>
      <p class="text-xs">Wheelhouse keeps no copy; it is gone once this dialog closes.</p>
    </Alert>
    <Alert v-if="revealProblem" severity="danger" :title="revealProblem" />
  </TypedConfirmModal>
</template>
