<script lang="ts">
/** Namespace navigation within one vault. */
export interface NamespaceListProps {
  vault: string;
  selected: string;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { Plus } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { ListGroup, ListGroupItem, Text } from "@wow-two-beta/fe/presentation/display";
import { DataRegion, useSkeletonItems } from "@wow-two-beta/fe/presentation/feedback";
import { useVaultNamespaces } from "@/application/secrets";
import NamespaceModal from "./NamespaceModal.vue";

/** Selects a namespace and exposes scoped namespace creation. */
defineOptions({ name: "NamespaceList" });
const props = defineProps<NamespaceListProps>();
const emit = defineEmits<{ select: [slug: string] }>();
defineSlots<{}>();

const { data, error, isRefreshing } = useVaultNamespaces(() => props.vault);
const adding = ref(false);
/** True until the vault's first catalog arrives, including while no vault is selected yet. */
const pending = computed(() => data.value === undefined && !error.value);
/** The namespaces, or placeholder rows until the first catalog arrives. */
const rows = useSkeletonItems(() => data.value, 3);

/** Closes namespace creation when the vault changes. */
watch(
  () => props.vault,
  () => {
    adding.value = false;
  },
);
</script>

<template>
  <div class="flex flex-col gap-3">
    <div class="flex h-8 items-center justify-between gap-2">
      <span
        class="text-xs font-semibold uppercase tracking-wide text-muted-foreground"
        >Namespaces</span
      >
      <Button variant="ghost" tone="neutral" size="sm" @click="adding = true">
        <template #leading><Plus :size="14" /></template>
        New
      </Button>
    </div>
    <DataRegion
      :is-first-load="pending"
      :is-refreshing="isRefreshing"
      :error="error"
      :has-data="data !== undefined"
      :is-empty="data !== undefined && !data.length"
      empty-title="No namespaces yet"
      empty-description="Create one per product environment."
      loading-label="Loading namespaces"
    >
      <ListGroup class="flex flex-col gap-1 [&>li]:py-0">
        <ListGroupItem
          v-for="(item, index) in rows"
          :key="item?.slug ?? `placeholder-${index}`"
          :description="item?.name ?? ''"
          :is-selected="Boolean(item) && item?.slug === selected"
          class="[&>button]:min-h-14 [&>button]:rounded-xl"
          @select="item && emit('select', item.slug)"
        >
          <Text as="span" class="w-full truncate font-mono text-xs font-medium text-inherit" skeleton-width="70%">{{
            item?.slug
          }}</Text>
        </ListGroupItem>
      </ListGroup>
    </DataRegion>
    <NamespaceModal
      :vault="vault"
      v-model:open="adding"
      @created="emit('select', $event)"
    />
  </div>
</template>
