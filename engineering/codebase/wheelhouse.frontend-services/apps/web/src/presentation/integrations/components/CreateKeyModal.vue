<script lang="ts">
import type { IntegrationKeyOperations } from "@/application/integrations";

/** Key creation and one-time secret reveal. */
export interface CreateKeyModalProps {
  readonly open: boolean;
  /** Creates the key; the host answers with its secret this once. */
  readonly create: IntegrationKeyOperations["create"];
}
</script>

<script setup lang="ts">
import { onBeforeUnmount, ref, shallowRef, watch } from "vue";
import { Button, CopyButton } from "@wow-two-beta/ui-vue/presentation/actions";
import { CodeText } from "@wow-two-beta/ui-vue/presentation/display";
import { Alert } from "@wow-two-beta/ui-vue/presentation/feedback";
import { CheckboxField, Field, TextInput } from "@wow-two-beta/ui-vue/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalDescription,
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from "@wow-two-beta/ui-vue/presentation/overlays";
import { useAppForm } from "@/bootstrap/form";
import {
  createIntegrationKeyForm,
  integrationKeyFormSchema,
} from "@/application/integrations/IntegrationKeyForms";
import {
  IntegrationScope,
  IntegrationScopes,
  type IntegrationKeyWithSecret,
} from "@/domain/integrations";

/** Shows a new key's secret once and forgets it when the dialog session ends. */
defineOptions({ name: "CreateKeyModal" });
const props = defineProps<CreateKeyModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const scopes = ref<IntegrationScope[]>([IntegrationScope.CatalogRead]);
const scopeError = ref<string | null>(null);
const created = shallowRef<IntegrationKeyWithSecret | null>(null);
const form = useAppForm({
  defaultValues: createIntegrationKeyForm(),
  schema: integrationKeyFormSchema,
  onSubmit: async (values, context) => {
    if (scopes.value.length === 0) {
      scopeError.value = "Choose at least one scope.";
      return { ok: true, value: undefined };
    }
    const result = await props.create({ name: values.name.trim(), scopes: [...scopes.value] });
    if (!result.ok) return result;
    if (!context.signal.aborted && props.open) created.value = result.value;
    // The form receives a verdict only; the secret is held exclusively by this reveal state.
    return { ok: true, value: undefined };
  },
});

/** Clears the one-time reveal whenever the dialog opens or closes. */
watch(() => props.open, clear, { flush: "sync" });
onBeforeUnmount(clear);

/** Empties the reveal and the form. */
function clear(): void {
  created.value = null;
  scopes.value = [IntegrationScope.CatalogRead];
  scopeError.value = null;
  form.invalidateSession(createIntegrationKeyForm());
}

/** Adds or drops one scope. */
function toggle(scope: IntegrationScope, on: boolean | "indeterminate"): void {
  scopeError.value = null;
  scopes.value = on === true
    ? [...new Set([...scopes.value, scope])]
    : scopes.value.filter((item) => item !== scope);
}

/** Closes without retaining the secret anywhere in application state. */
function close(open: boolean): void {
  if (!open) clear();
  emit("update:open", open);
}
</script>

<template>
  <Modal :open="open" @update:open="close">
    <ModalContent>
      <ModalHeader>
        <ModalTitle>{{ created ? "Copy the key now" : "Create integration key" }}</ModalTitle>
        <ModalDescription>
          {{
            created
              ? "This is the only time the key is shown. Wheelhouse keeps only its hash."
              : "Another program presents the key to read Wheelhouse — only what its scopes allow."
          }}
        </ModalDescription>
      </ModalHeader>
      <template v-if="created">
        <ModalBody class="flex flex-col gap-3">
          <CodeText class="break-all">{{ created.secret }}</CodeText>
          <p class="text-sm text-muted-foreground">
            Send it as <code>Authorization: Bearer …</code> or in the <code>X-Api-Key</code> header.
          </p>
        </ModalBody>
        <ModalFooter>
          <CopyButton :text="created.secret" aria-label="Copy key" variant="outline" tone="neutral" />
          <Button @click="close(false)">Done</Button>
        </ModalFooter>
      </template>
      <form v-else @submit="form.handleSubmit">
        <ModalBody class="flex flex-col gap-4">
          <form.Field name="name" is-required v-slot="field">
            <Field label="Name" helper="The program that presents it, e.g. Claude">
              <TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" />
            </Field>
          </form.Field>
          <fieldset class="flex flex-col gap-2">
            <legend class="mb-1 text-sm font-medium">Scopes</legend>
            <CheckboxField
              v-for="scope in IntegrationScopes"
              :key="scope.value"
              :model-value="scopes.includes(scope.value)"
              :label="scope.value"
              :description="scope.description"
              @update:model-value="toggle(scope.value, $event)"
            />
            <p v-if="scopeError" role="alert" class="text-sm text-destructive">{{ scopeError }}</p>
          </fieldset>
          <Alert v-if="form.state.submitError" severity="danger" :description="form.state.submitError.message" />
        </ModalBody>
        <ModalFooter>
          <Button type="button" variant="outline" tone="neutral" @click="close(false)">Cancel</Button>
          <Button type="submit" :is-loading="form.state.isSubmitting">Create key</Button>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
