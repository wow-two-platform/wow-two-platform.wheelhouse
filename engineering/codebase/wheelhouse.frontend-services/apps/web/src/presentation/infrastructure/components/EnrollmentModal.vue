<script lang="ts">
import type { EnrollmentOperations } from "@/application/infrastructure";
import type { Enrollment, OneTimeKey } from "@/domain/infrastructure";
import type { Server } from "@/domain/servers";

/** Starts a server's enrollment, or reruns one that stopped short. */
export interface EnrollmentModalProps {
  readonly open: boolean;
  /** The server to enroll; null while closed. */
  readonly server: Server | null;
  /** The server's enrollment as last read, if any: a rerun repeats its request. */
  readonly enrollment?: Enrollment | null;
  /** The one-time key, when the caller already holds it; otherwise the dialog asks for it. */
  readonly oneTimeKey?: OneTimeKey | null;
  /** Why enrollment cannot run here, such as on deployed dev; the form stays read-only. */
  readonly blocked?: string | null;
  readonly operations: EnrollmentOperations;
}
</script>

<script setup lang="ts">
import { computed, ref, shallowRef } from "vue";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Alert, Spinner } from "@wow-two-beta/fe/presentation/feedback";
import { Field, NumberInput, TextAreaInput, TextInput } from "@wow-two-beta/fe/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalFooter,
  ModalHeader,
  ModalSize,
  ModalTitle,
} from "@wow-two-beta/fe/presentation/overlays";
import { useFormDialogSession } from "@wow-two-beta/fe/forms-engine";
import { useAppForm } from "@/bootstrap/form";
import {
  adminAfterLogin,
  enrollmentFormOf,
  enrollmentFormSchema,
  enrollmentRequestOf,
  type EnrollmentFormModel,
} from "@/application/infrastructure/EnrollmentForms";
import { EnrollmentRules, InfraJobStatus } from "@/domain/infrastructure";
import { failureReason } from "@/integration/common";
import OneTimeKeyBlock from "./OneTimeKeyBlock.vue";

/**
 * The one-time key to add to the server, then how Wheelhouse signs in the first time: the public address (fixed on a
 * rerun once the first contact succeeded), SSH port, sign-in account, administrator and its key, tailnet hostname and
 * the host key fingerprint the provider shows. Starting queues the enrollment; the server's panel follows it live.
 */
defineOptions({ name: "EnrollmentModal" });
const props = withDefaults(defineProps<EnrollmentModalProps>(), {
  enrollment: null,
  oneTimeKey: null,
  blocked: null,
});
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const fallback: EnrollmentFormModel = {
  publicAddress: "",
  sshPort: 22,
  loginUser: "root",
  admin: "ubuntu",
  adminKey: "",
  tailnetHostname: "",
  expectedHostKey: "",
};
const key = shallowRef<OneTimeKey | null>(null);
const keyLoading = ref(false);
const keyError = ref("");
const isRerun = computed(() => EnrollmentRules.isRerun(props.enrollment));
const addressFixed = computed(() => isRerun.value && EnrollmentRules.contactSucceeded(props.enrollment));

const form = useAppForm({
  defaultValues: fallback,
  schema: enrollmentFormSchema,
  onSubmit: async (values) => {
    const server = props.server;
    if (!server) return { ok: true, value: undefined };
    const result = await props.operations.start(server.slug, enrollmentRequestOf(values));
    if (result.ok) emit("update:open", false);
    return result;
  },
});
const loginUser = form.useFormState((state) => state.values.loginUser);
const publicAddress = form.useFormState((state) => state.values.publicAddress);

useFormDialogSession(form, {
  open: () => props.open,
  initialValues: () => (props.server ? enrollmentFormOf(props.server, props.enrollment) : fallback),
  onOpen: () => void readKey(),
});

/** The key this session shows: the caller's or the enrollment's, else a new one while nothing has started. @internal */
async function readKey(): Promise<void> {
  keyError.value = "";
  key.value = props.oneTimeKey ?? props.enrollment?.oneTimeKey ?? null;
  const server = props.server;
  const notStarted = !props.enrollment || props.enrollment.status === InfraJobStatus.NotStarted;
  if (key.value || !server || !notStarted || props.blocked) return;
  keyLoading.value = true;
  try {
    const result = await props.operations.createKey(server.slug);
    if (!props.open || props.server?.slug !== server.slug) return;
    if (result.ok) key.value = { publicKey: result.value.publicKey, fingerprint: result.value.fingerprint };
    else keyError.value = failureReason(result.failure);
  } finally {
    keyLoading.value = false;
  }
}

/** Follows a new sign-in account: any account but root is the administrator itself. @internal */
function changeLogin(value: string): void {
  const previous = form.state.values.loginUser;
  form.setValue("loginUser", value);
  form.setValue("admin", adminAfterLogin(previous, value, form.state.values.admin));
}
</script>

<template>
  <Modal :open="open" @update:open="emit('update:open', $event)">
    <ModalContent :size="ModalSize.Lg">
      <ModalHeader>
        <ModalTitle>{{ isRerun ? "Rerun enrollment" : "Enroll" }} {{ server?.name ?? "" }}</ModalTitle>
      </ModalHeader>
      <form class="flex min-h-0 flex-1 flex-col" @submit="form.handleSubmit">
        <ModalBody class="flex flex-col gap-4">
          <Alert v-if="blocked" severity="warning" :description="blocked" />
          <section v-if="key || keyLoading || keyError" aria-label="One-time key" class="flex flex-col gap-2">
            <h3 class="text-sm font-medium">One-time key</h3>
            <Spinner v-if="keyLoading" size="sm" label="Creating the one-time key" />
            <Alert v-else-if="keyError" severity="danger" :description="keyError" />
            <OneTimeKeyBlock v-else-if="key" :one-time-key="key" :login-user="loginUser" :address="publicAddress" />
          </section>
          <div class="grid gap-4 sm:grid-cols-[minmax(0,1fr)_7rem]">
            <form.Field name="publicAddress" is-required :is-read-only="addressFixed" v-slot="field">
              <Field label="Public address" v-bind="addressFixed ? { helper: 'Fixed once the first contact succeeded' } : {}">
                <TextInput v-model="field.value" autocomplete="off" placeholder="203.0.113.10" @blur="field.onBlur" />
              </Field>
            </form.Field>
            <form.Field name="sshPort" is-required v-slot="field">
              <Field label="SSH port"><NumberInput v-model="field.value" @blur="field.onBlur" /></Field>
            </form.Field>
          </div>
          <div class="grid gap-4 sm:grid-cols-2">
            <form.Field name="loginUser" is-required v-slot="field">
              <Field label="Sign-in account" helper="root on a root-only image such as Contabo">
                <TextInput
                  :model-value="field.value"
                  autocomplete="off"
                  autocapitalize="off"
                  @update:model-value="changeLogin(String($event ?? ''))"
                  @blur="field.onBlur"
                />
              </Field>
            </form.Field>
            <form.Field name="admin" is-required :is-read-only="loginUser.trim() !== 'root'" v-slot="field">
              <Field label="Administrator" v-bind="loginUser.trim() !== 'root' ? { helper: 'The sign-in account itself' } : {}">
                <TextInput v-model="field.value" autocomplete="off" autocapitalize="off" @blur="field.onBlur" />
              </Field>
            </form.Field>
          </div>
          <form.Field name="adminKey" v-slot="field">
            <Field label="Administrator SSH public key" helper="Only when the administrator does not exist yet">
              <TextAreaInput v-model="field.value" :rows="2" spellcheck="false" placeholder="ssh-ed25519 AAAA…" @blur="field.onBlur" />
            </Field>
          </form.Field>
          <div class="grid gap-4 sm:grid-cols-2">
            <form.Field name="tailnetHostname" is-required v-slot="field">
              <Field label="Tailnet hostname">
                <TextInput v-model="field.value" autocomplete="off" autocapitalize="off" @blur="field.onBlur" />
              </Field>
            </form.Field>
            <form.Field name="expectedHostKey" v-slot="field">
              <Field label="Host key fingerprint" helper="From the provider's console; optional">
                <TextInput v-model="field.value" autocomplete="off" spellcheck="false" placeholder="SHA256:…" @blur="field.onBlur" />
              </Field>
            </form.Field>
          </div>
          <Alert v-if="form.state.submitError" severity="danger" :description="failureReason(form.state.submitError)" />
        </ModalBody>
        <ModalFooter>
          <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
          <Button type="submit" :is-disabled="Boolean(blocked) || !server" :is-loading="form.state.isSubmitting">{{
            isRerun ? "Rerun" : "Enroll"
          }}</Button>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
