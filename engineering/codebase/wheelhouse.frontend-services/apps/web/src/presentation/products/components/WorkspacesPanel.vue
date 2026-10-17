<script setup lang="ts">
import { computed, ref } from "vue";
import { RouterLink } from "vue-router";
import { ArrowDown, ArrowUp, GripVertical, Pencil, Plus } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Text } from "@wow-two-beta/fe/presentation/display";
import { DataRegion, useSkeletonItems } from "@wow-two-beta/fe/presentation/feedback";
import {
  SortableGroup,
  SortableGroupHandle,
  SortableGroupItem,
  SortableGroupMoveButton,
} from "@wow-two-beta/fe/presentation/forms";
import { ConsolePaths } from "@/application/navigation";
import { useProducts } from "@/application/products";
import { useWorkspaces } from "@/application/workspaces";
import type { Workspace } from "@/domain/workspaces";
import { failureReason } from "@/integration/common";
import { DeleteConfirm, Panel } from "@/presentation/common/components";
import ProductIcon from "./ProductIcon.vue";
import WorkspaceFormModal from "./WorkspaceFormModal.vue";

/**
 * The workspaces in the operator's order: add, rename, delete and reorder them. A drag handle, arrow keys on the
 * handle, or the up and down buttons move one; the order saves at once and rolls back if the host refuses it.
 */
defineOptions({ name: "WorkspacesPanel" });
defineSlots<{}>();
const operations = useWorkspaces();
const products = useProducts();
const form = ref<{ open: boolean; workspace: Workspace | null }>({ open: false, workspace: null });
const removeError = ref<string | null>(null);
const rows = useSkeletonItems(() => (operations.hasData.value ? operations.workspaces.value : undefined), 3);
const counts = computed(() => {
  const map = new Map<string, number>();
  for (const product of products.products.value)
    if (product.workspaceId) map.set(product.workspaceId, (map.get(product.workspaceId) ?? 0) + 1);
  return map;
});

/** Removes an empty workspace; one that still holds products stays, and the host says why. @internal */
async function remove(workspace: Workspace): Promise<void> {
  removeError.value = null;
  const result = await operations.remove(workspace.id);
  if (!result.ok) removeError.value = failureReason(result.failure) || `${workspace.name} could not be deleted.`;
}

/** Saves a dragged or stepped move. @internal */
function reorder(from: number, to: number): void {
  removeError.value = null;
  void operations.move(from, to);
}

/** @internal The move buttons' look; the SDK button carries behaviour only. Touch targets grow below `md`. */
const moveClass =
  "inline-flex size-11 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring disabled:pointer-events-none disabled:opacity-30 md:size-8";
</script>

<template>
  <Panel title="Workspaces">
    <template #actions>
      <Button size="sm" variant="outline" tone="neutral" @click="form = { open: true, workspace: null }">
        <template #leading><Plus :size="14" /></template>New workspace
      </Button>
    </template>
    <DataRegion
      :is-first-load="operations.isFirstLoad.value"
      :error="operations.error.value"
      :has-data="operations.hasData.value"
      :is-empty="operations.hasData.value && !operations.workspaces.value.length"
      empty-title="No workspaces yet"
      empty-description="Group products into workspaces, or import them from the catalog."
      loading-label="Loading workspaces"
      @retry="operations.reload"
    >
      <SortableGroup aria-label="Workspace order" class="-my-2 divide-y divide-border" @reorder="reorder">
        <SortableGroupItem
          v-for="(workspace, index) in rows"
          :key="workspace?.id ?? `placeholder-${index}`"
          :index="index"
          class="flex min-w-0 items-center gap-2 py-2 data-[over]:bg-primary-soft/40"
        >
          <SortableGroupHandle
            class="hidden size-8 shrink-0 rounded-md hover:bg-muted md:inline-flex"
            :aria-label="workspace ? `Drag ${workspace.name} to reorder` : 'Drag to reorder'"
            ><GripVertical :size="16"
          /></SortableGroupHandle>
          <ProductIcon :name="workspace?.name ?? ''" size="sm" />
          <div class="min-w-0 flex-1">
            <Text class="truncate text-sm font-medium" skeleton-width="8rem">
              <RouterLink v-if="workspace" :to="ConsolePaths.workspace(workspace.slug)" class="hover:text-primary">{{
                workspace.name
              }}</RouterLink>
            </Text>
            <Text class="truncate text-xs text-muted-foreground" skeleton-width="10rem"
              ><template v-if="workspace"
                ><span class="font-mono">{{ workspace.slug }}</span> · {{ counts.get(workspace.id) ?? 0 }} product{{
                  (counts.get(workspace.id) ?? 0) === 1 ? "" : "s"
                }}</template
              ></Text
            >
          </div>
          <template v-if="workspace">
            <SortableGroupMoveButton direction="previous" :label="`Move ${workspace.name} up`" :class="moveClass"
              ><ArrowUp :size="15"
            /></SortableGroupMoveButton>
            <SortableGroupMoveButton direction="next" :label="`Move ${workspace.name} down`" :class="moveClass"
              ><ArrowDown :size="15"
            /></SortableGroupMoveButton>
            <Button
              size="sm"
              variant="ghost"
              tone="neutral"
              :aria-label="`Rename ${workspace.name}`"
              @click="form = { open: true, workspace }"
              ><Pencil :size="14"
            /></Button>
            <DeleteConfirm
              label="Delete"
              :question="`Delete ${workspace.name}? Only an empty workspace can go.`"
              :on-confirm="() => remove(workspace)"
            />
          </template>
        </SortableGroupItem>
      </SortableGroup>
      <p v-if="removeError" role="alert" class="mt-3 text-sm text-destructive">{{ removeError }}</p>
    </DataRegion>
  </Panel>
  <WorkspaceFormModal
    :open="form.open"
    :workspace="form.workspace"
    :operations="operations"
    @update:open="(open) => (form = { ...form, open })"
  />
</template>
