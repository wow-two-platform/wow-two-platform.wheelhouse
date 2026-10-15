<script lang="ts">
/** Namespace-scoped token minting and one-time reveal. */
export interface MintTokenModalProps {
  vault: string;
  ns: string;
  open: boolean;
}
</script>

<script setup lang="ts">
import { onBeforeUnmount, shallowRef, watch } from "vue";
import { Button, CopyButton } from "@wow-two-beta/ui-vue/presentation/actions";
import { CodeText } from "@wow-two-beta/ui-vue/presentation/display";
import { Alert } from "@wow-two-beta/ui-vue/presentation/feedback";
import { Field, TextInput } from "@wow-two-beta/ui-vue/presentation/forms";
import {
  Modal,
  ModalContent,
  ModalHeader,
  ModalTitle,
  ModalDescription,
  ModalBody,
  ModalFooter,
} from "@wow-two-beta/ui-vue/presentation/overlays";
import { useAppForm } from "@/bootstrap/form";
import { useTokenChanges } from "@/application/secrets";
import {
  createTokenForm,
  tokenFormSchema,
} from "@/application/secrets/VaultForms";
import type { MintedToken } from "@/domain/secrets";

/** Shows freshly minted plaintext once and forgets it when the dialog session ends. */
defineOptions({ name: "MintTokenModal" });
const props = defineProps<MintTokenModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const changes = useTokenChanges(
  () => props.vault,
  () => props.ns,
);
const form = useAppForm({
  defaultValues: createTokenForm(),
  schema: tokenFormSchema,
  onSubmit: async (values, context) => {
    const result = await changes.mint(values.name, context.signal);
    if (!result.ok) return result;
    if (!context.signal.aborted && props.open) minted.value = result.value;
    // The form receives a verdict only; plaintext is held exclusively by this reveal state.
    return { ok: true, value: undefined };
  },
});
const minted = shallowRef<MintedToken | null>(null);

/** Clears the one-time reveal when visibility, vault, or namespace changes. */
watch([() => props.open, () => props.vault, () => props.ns], clear, {
  flush: "sync",
});
onBeforeUnmount(clear);

/** Empties the local reveal and aborts any pending mint request. */
function clear(): void {
  minted.value = null;
  form.invalidateSession(createTokenForm());
  changes.reset();
}

/** Closes without retaining the minted token anywhere in application state. */
function close(open: boolean): void {
  if (!open) clear();
  emit("update:open", open);
}
</script>

<template>
  <Modal :open="open" @update:open="close">
    <ModalContent>
      <ModalHeader>
        <ModalTitle>{{
          minted ? "Copy the token now" : "Mint product token"
        }}</ModalTitle>
        <ModalDescription>
          {{
            minted
              ? "This is the only time the token is shown. The vault keeps only its hash."
              : `The token reads secrets in ${ns} only. Mount it into the product; never commit it.`
          }}
        </ModalDescription>
      </ModalHeader>
      <template v-if="minted">
        <ModalBody class="flex flex-col gap-3">
          <CodeText class="break-all">{{ minted.token }}</CodeText>
          <p class="text-sm text-muted-foreground">
            Token {{ minted.name }} for {{ minted.namespace }}
          </p>
        </ModalBody>
        <ModalFooter>
          <CopyButton
            :text="minted.token"
            aria-label="Copy token"
            variant="outline"
            tone="neutral"
          />
          <Button @click="close(false)">Done</Button>
        </ModalFooter>
      </template>
      <form v-else @submit="form.handleSubmit">
        <ModalBody class="flex flex-col gap-4">
          <form.Field name="name" is-required v-slot="field">
            <Field label="Name" helper="Who uses it, e.g. management">
              <TextInput
                v-model="field.value"
                autocomplete="off"
                @blur="field.onBlur"
              />
            </Field>
          </form.Field>
          <Alert
            v-if="form.state.submitError"
            severity="danger"
            :description="form.state.submitError.message"
          />
        </ModalBody>
        <ModalFooter>
          <Button
            type="button"
            variant="outline"
            tone="neutral"
            @click="close(false)"
            >Cancel</Button
          >
          <Button type="submit" :is-loading="form.state.isSubmitting"
            >Mint token</Button
          >
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
