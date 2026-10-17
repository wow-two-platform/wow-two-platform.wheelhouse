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
import { Text } from "@wow-two-beta/fe/presentation/display";
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
      <ul class="flex flex-col gap-1">
        <li v-for="(item, index) in rows" :key="item?.slug ?? `placeholder-${index}`">
          <button
            type="button"
            :aria-current="item && item.slug === selected ? 'true' : undefined"
            class="flex min-h-14 w-full flex-col justify-center rounded-xl px-3 py-2 text-left transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            :class="
              item && item.slug === selected
                ? 'bg-primary-soft text-primary-soft-foreground'
                : 'hover:bg-muted'
            "
            @click="item && emit('select', item.slug)"
          >
            <Text as="span" class="w-full truncate font-mono text-xs font-medium text-inherit" skeleton-width="70%">{{
              item?.slug
            }}</Text>
            <Text
              as="span"
              class="w-full truncate text-xs"
              :class="
                item && item.slug === selected
                  ? 'text-primary-soft-foreground'
                  : 'text-muted-foreground'
              "
              skeleton-width="90%"
            >
              {{ item?.name }}
            </Text>
          </button>
        </li>
      </ul>
    </DataRegion>
    <NamespaceModal
      :vault="vault"
      v-model:open="adding"
      @created="emit('select', $event)"
    />
  </div>
</template>
