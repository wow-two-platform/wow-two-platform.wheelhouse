<script lang="ts">
import { ReleaseSets, type DeploySettings } from "@/domain/deployments";

/** Defines the bar that applies an environment's edited settings and secrets. */
export interface ApplyBarProps {
  readonly plan: DeploySettings;
  /** Whether the target asks for its ID to be typed before a deploy (prod on the local server). */
  readonly needsConfirmation?: boolean;
}
</script>

<script setup lang="ts">
import { computed, ref } from "vue";
import { CalendarClock, Play, X } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { RelativeTime } from "@wow-two-beta/fe/presentation/display";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import { Field, TextInput, TypedConfirmField, useTypedConfirmation } from "@wow-two-beta/fe/presentation/forms";
import { useApplySettings, useCancelApply } from "@/application/settings";
import { EnvironmentSettingsRules } from "@/domain/settings";
import { failureReason } from "@/integration/common";

/**
 * After an edit, what the environment runs still has the old values until a deploy renders them again. The bar names
 * the keys that changed since the last deploy and applies them: now, as a recorded deploy of the running release that
 * restarts only the services whose settings changed, or at a set time. A scheduled apply shows with its cancel.
 */
defineOptions({ name: "ApplyBar" });
const props = withDefaults(defineProps<ApplyBarProps>(), { needsConfirmation: false });
defineSlots<{}>();

const apply = useApplySettings();
const cancel = useCancelApply();
const mode = ref<"idle" | "now" | "later">("idle");
const reason = ref("");
const at = ref("");
const done = ref<string | null>(null);

const bounds = computed(() => EnvironmentSettingsRules.scheduleBounds(new Date()));
const shown = computed(() => props.plan.unapplied.length > 0 || props.plan.pending !== null || done.value !== null);
/** The typed target ID: what the API receives as `confirm`, only while the target asks for it. */
const {
  typed,
  isConfirmed: confirmed,
  confirm: confirmation,
  reset: resetTyped,
} = useTypedConfirmation(() => props.plan.target, { isNeeded: () => props.needsConfirmation });

async function run(): Promise<void> {
  done.value = null;
  const result = await apply.mutateAsync({
    target: props.plan.target,
    ...(mode.value === "later" && at.value ? { at: new Date(at.value).toISOString() } : {}),
    ...(reason.value.trim() ? { reason: reason.value.trim() } : {}),
    ...(confirmation.value !== undefined ? { confirm: confirmation.value } : {}),
  });
  if (!result.ok) return;
  done.value =
    result.value.mode === "now"
      ? `Deploying ${ReleaseSets.text(result.value.release) || "the running release"} again; only services whose settings changed restart.`
      : "Scheduled.";
  mode.value = "idle";
  reason.value = "";
  resetTyped();
}
</script>

<template>
  <section v-if="shown" class="flex flex-col gap-3 rounded-xl border border-border bg-muted/30 p-4" aria-label="Apply settings">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div class="min-w-0 text-sm">
        <p v-if="props.plan.unapplied.length" class="font-medium">
          {{ props.plan.unapplied.length }} {{ props.plan.unapplied.length === 1 ? "change waits" : "changes wait" }} for a deploy
        </p>
        <p v-if="props.plan.unapplied.length" class="truncate font-mono text-xs text-muted-foreground" :title="props.plan.unapplied.join(', ')">
          {{ props.plan.unapplied.join(", ") }}
        </p>
        <p v-if="props.plan.pending" class="flex items-center gap-1.5 text-xs text-muted-foreground">
          <CalendarClock :size="14" aria-hidden="true" />Applies <RelativeTime :value="props.plan.pending.at" /> ({{
            new Date(props.plan.pending.at).toLocaleString()
          }}), scheduled by {{ props.plan.pending.by }}
        </p>
        <p v-if="done" class="text-xs text-success" role="status">{{ done }}</p>
      </div>
      <div v-if="props.plan.release" class="flex flex-wrap gap-2">
        <Button size="sm" :is-disabled="apply.loading.value" @click="mode = mode === 'now' ? 'idle' : 'now'">
          <template #leading><Play :size="14" aria-hidden="true" /></template>Apply now
        </Button>
        <Button size="sm" variant="outline" tone="neutral" :is-disabled="apply.loading.value" @click="mode = mode === 'later' ? 'idle' : 'later'">
          <template #leading><CalendarClock :size="14" aria-hidden="true" /></template>Apply at…
        </Button>
        <Button
          v-if="props.plan.pending"
          size="sm"
          variant="ghost"
          tone="danger"
          :is-loading="cancel.loading.value"
          @click="cancel.mutateAsync(props.plan.target)"
        >
          <template #leading><X :size="14" aria-hidden="true" /></template>Cancel schedule
        </Button>
      </div>
      <p v-else class="text-xs text-muted-foreground">Nothing runs here yet: the values apply with its first deploy.</p>
    </div>
    <div v-if="mode !== 'idle'" class="flex flex-col gap-3">
      <Field v-if="mode === 'later'" label="Apply at" helper="Your local time; within 90 days" is-required>
        <TextInput v-model="at" type="datetime-local" :min="bounds.min" :max="bounds.max" />
      </Field>
      <Field label="Reason" helper="Kept on the deploy, such as rotated the Stripe key">
        <TextInput v-model="reason" autocomplete="off" maxlength="200" />
      </Field>
      <TypedConfirmField v-if="props.needsConfirmation" v-model="typed" :expected="props.plan.target" />
      <div class="flex gap-2">
        <Button
          size="sm"
          :is-loading="apply.loading.value"
          :is-disabled="!confirmed || (mode === 'later' && !at)"
          @click="run"
          >{{ mode === "now" ? `Deploy ${ReleaseSets.short(props.plan.release ?? "")} again now` : "Schedule the apply" }}</Button
        >
        <Button size="sm" variant="ghost" tone="neutral" @click="mode = 'idle'">Cancel</Button>
      </div>
    </div>
    <Alert v-if="apply.error.value || cancel.error.value" severity="danger" :description="failureReason(apply.error.value ?? cancel.error.value)" />
  </section>
</template>
