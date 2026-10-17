<script lang="ts">
/** Optional failure from resolving or clearing the private session. */
export interface SignInScreenProps {
  readonly error?: { readonly message: string } | null;
}
</script>

<script setup lang="ts">
import { Github } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Card } from "@wow-two-beta/fe/presentation/display";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import { BrandWordmark } from "@/presentation/shell";

/** Renders the private operator sign-in gate. */
defineOptions({ name: "SignInScreen" });
const props = defineProps<SignInScreenProps>();
const emit = defineEmits<{ "sign-in": [] }>();
</script>

<template>
  <main
    class="flex min-h-dvh items-center justify-center bg-background px-5 py-16 text-foreground"
  >
    <Card
      class="flex w-full max-w-sm flex-col items-center rounded-[2rem] border border-border p-9 text-center"
    >
      <h1><BrandWordmark :height="40" /></h1>
      <p class="mt-2 text-sm text-muted-foreground">
        Private operations console
      </p>
      <Alert
        v-if="props.error"
        class="mt-6 w-full text-left"
        severity="danger"
        title="Session unavailable"
        :description="props.error.message"
      />
      <Button class="mt-8" size="lg" is-full-width @click="emit('sign-in')">
        <template #leading><Github :size="18" /></template>Sign in with GitHub
      </Button>
    </Card>
  </main>
</template>
