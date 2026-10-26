<script lang="ts">
import type { ReleaseSourceQuery } from "@/integration/products";

/** The release source the host derives for the product being added or edited. */
export interface ReleaseSourceFieldsProps {
  /** The slug, repository and branch to read; null until the form holds all three. */
  readonly query: ReleaseSourceQuery | null;
}
</script>

<script setup lang="ts">
import { Alert, Spinner } from "@wow-two-beta/fe/presentation/feedback";
import { Field, TextInput } from "@wow-two-beta/fe/presentation/forms";
import { useProductReleaseSource } from "@/application/products";
import { failureReason } from "@/integration/common";
import { RefreshButton } from "@/presentation/common/components";

/**
 * Shows, read-only, what Wheelhouse takes from the repository when the product publishes: the shared publish
 * workflow's release asset and build workflow, and one image per service of its `deploy.yml`. Nothing is typed; a
 * save stores what the host derives then, and Refresh reads the branch head again now.
 */
defineOptions({ name: "ReleaseSourceFields" });
const props = defineProps<ReleaseSourceFieldsProps>();
defineSlots<{}>();

const source = useProductReleaseSource(() => props.query);
</script>

<template>
  <section class="flex flex-col gap-3" aria-labelledby="release-source-title">
    <div class="flex items-center justify-between gap-2">
      <div class="flex min-w-0 flex-col">
        <span id="release-source-title" class="text-sm font-medium">Release source</span>
        <span class="text-xs text-muted-foreground">Read from the repository's deploy.yml; nothing to type</span>
      </div>
      <RefreshButton
        v-if="props.query"
        :refreshing="source.refreshing.value"
        icon-only
        label="Read the repository again"
        @refresh="source.refresh"
      />
    </div>
    <p v-if="!props.query" class="text-sm text-muted-foreground">Enter the slug and repository to read it</p>
    <Spinner v-else-if="source.isFirstLoad.value" size="sm" label="Reading the repository" />
    <Alert v-else-if="source.error.value" severity="danger" :description="failureReason(source.error.value)" />
    <template v-else-if="source.data.value">
      <div :class="['flex flex-col gap-3', source.isSettling.value ? 'opacity-60' : '']" :aria-busy="source.isSettling.value">
        <div class="grid gap-4 sm:grid-cols-2">
          <Field label="Release asset" is-read-only>
            <TextInput :model-value="source.data.value.asset" autocomplete="off" />
          </Field>
          <Field label="Build workflow" is-read-only>
            <TextInput :model-value="source.data.value.workflow ?? ''" autocomplete="off" placeholder="None" />
          </Field>
        </div>
        <div class="flex flex-col gap-2" role="group" aria-labelledby="release-images-title">
          <span id="release-images-title" class="text-sm font-medium">Images</span>
          <div
            v-if="source.data.value.images.length"
            class="grid gap-2 text-xs text-muted-foreground max-sm:hidden sm:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]"
            aria-hidden="true"
          >
            <span>Service</span><span>Image</span>
          </div>
          <div
            v-for="image in source.data.value.images"
            :key="image.service"
            class="grid items-start gap-2 max-sm:rounded-md max-sm:border max-sm:border-border max-sm:p-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]"
          >
            <Field is-read-only class="min-w-0">
              <span class="text-xs text-muted-foreground sm:hidden" aria-hidden="true">Service</span>
              <TextInput :model-value="image.service" :aria-label="`Service ${image.service}`" autocomplete="off" />
            </Field>
            <Field is-read-only class="min-w-0">
              <span class="text-xs text-muted-foreground sm:hidden" aria-hidden="true">Image</span>
              <TextInput :model-value="image.image" :aria-label="`Image of ${image.service}`" autocomplete="off" />
            </Field>
          </div>
          <p v-if="!source.data.value.images.length" class="text-sm text-muted-foreground">No services read yet</p>
        </div>
        <Alert v-if="source.data.value.note" severity="warning" :description="source.data.value.note" />
      </div>
    </template>
  </section>
</template>
