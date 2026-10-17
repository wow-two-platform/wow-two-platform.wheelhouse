<script lang="ts">
import type { EntityTree } from "@/domain/deployments";

/** Defines the entity tree's checkboxes. */
export interface EntityTreeListProps {
  /** Workspaces, their products and each product's services. */
  readonly tree: EntityTree;
  /** The picked keys: `w:` workspace, `p:` product, `s:` product/service. */
  readonly modelValue: readonly string[];
}
</script>

<script setup lang="ts">
import { ref } from "vue";
import { ChevronDown, ChevronRight } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { CheckboxInput } from "@wow-two-beta/fe/presentation/forms";

import { EntitySelection, type CheckState, type EntityNode } from "@/domain/deployments";

/**
 * The Workspace → Product → Service tree with mix-and-match checkboxes, shared by the picker's popover and its phone
 * sheet. Workspaces start unfolded, products folded.
 */
defineOptions({ name: "EntityTreeList" });
const props = defineProps<EntityTreeListProps>();
const emit = defineEmits<{ "update:modelValue": [value: string[]] }>();
const folded = ref(new Set<string>());
const unfolded = ref(new Set<string>());

function state(node: EntityNode): CheckState {
  return EntitySelection.state(props.tree, props.modelValue, node);
}

function toggle(node: EntityNode): void {
  emit("update:modelValue", EntitySelection.toggle(props.tree, props.modelValue, node));
}

function isOpen(key: string, byDefault: boolean): boolean {
  return byDefault ? !folded.value.has(key) : unfolded.value.has(key);
}

function flip(key: string, byDefault: boolean): void {
  const set = new Set(byDefault ? folded.value : unfolded.value);
  if (set.has(key)) set.delete(key);
  else set.add(key);
  if (byDefault) folded.value = set;
  else unfolded.value = set;
}
</script>

<template>
  <div class="flex min-h-0 flex-col">
      <div class="max-h-[min(26rem,60vh)] overflow-y-auto p-2 max-md:max-h-none">
        <p v-if="!props.tree.length" class="px-2 py-3 text-muted-foreground">No products yet.</p>
        <ul role="tree" aria-label="Workspaces, products and services" aria-multiselectable="true">
          <li
            v-for="workspace in props.tree"
            :key="workspace.slug"
            role="treeitem"
            :aria-expanded="isOpen(`w:${workspace.slug}`, true)"
            :aria-selected="state({ kind: 'workspace', workspace: workspace.slug }) === 'checked'"
          >
            <div class="flex min-h-11 items-center gap-1 rounded-lg pr-2 hover:bg-muted/60 md:min-h-9">
              <button
                type="button"
                class="flex size-8 shrink-0 items-center justify-center rounded-md text-muted-foreground hover:text-foreground"
                :aria-label="`${isOpen(`w:${workspace.slug}`, true) ? 'Fold' : 'Unfold'} ${workspace.name}`"
                @click="flip(`w:${workspace.slug}`, true)"
              >
                <ChevronDown v-if="isOpen(`w:${workspace.slug}`, true)" :size="14" />
                <ChevronRight v-else :size="14" />
              </button>
              <label class="flex min-w-0 flex-1 cursor-pointer items-center gap-2 py-1">
                <CheckboxInput
                  size="sm"
                  :model-value="['checked', 'implied'].includes(state({ kind: 'workspace', workspace: workspace.slug }))"
                  :is-indeterminate="state({ kind: 'workspace', workspace: workspace.slug }) === 'partial'"
                  @update:model-value="toggle({ kind: 'workspace', workspace: workspace.slug })"
                />
                <span class="truncate font-medium">{{ workspace.name }}</span>
                <span class="ml-auto text-xs text-muted-foreground tabular-nums">{{ workspace.products.length }}</span>
              </label>
            </div>
            <ul v-if="isOpen(`w:${workspace.slug}`, true)" role="group" class="ml-5 border-l border-border pl-1">
              <li
                v-for="product in workspace.products"
                :key="product.slug"
                role="treeitem"
                :aria-expanded="product.services.length ? isOpen(`p:${product.slug}`, false) : undefined"
                :aria-selected="state({ kind: 'product', workspace: workspace.slug, product: product.slug }) === 'checked'"
              >
                <div class="flex min-h-11 items-center gap-1 rounded-lg pr-2 hover:bg-muted/60 md:min-h-9">
                  <button
                    v-if="product.services.length"
                    type="button"
                    class="flex size-8 shrink-0 items-center justify-center rounded-md text-muted-foreground hover:text-foreground"
                    :aria-label="`${isOpen(`p:${product.slug}`, false) ? 'Hide' : 'Show'} ${product.name} services`"
                    @click="flip(`p:${product.slug}`, false)"
                  >
                    <ChevronDown v-if="isOpen(`p:${product.slug}`, false)" :size="14" />
                    <ChevronRight v-else :size="14" />
                  </button>
                  <span v-else class="size-8 shrink-0" aria-hidden="true" />
                  <label class="flex min-w-0 flex-1 cursor-pointer items-center gap-2 py-1">
                    <CheckboxInput
                      size="sm"
                      :model-value="
                        ['checked', 'implied'].includes(state({ kind: 'product', workspace: workspace.slug, product: product.slug }))
                      "
                      :is-indeterminate="state({ kind: 'product', workspace: workspace.slug, product: product.slug }) === 'partial'"
                      @update:model-value="toggle({ kind: 'product', workspace: workspace.slug, product: product.slug })"
                    />
                    <span class="truncate">{{ product.name }}</span>
                  </label>
                </div>
                <ul v-if="product.services.length && isOpen(`p:${product.slug}`, false)" role="group" class="ml-5 border-l border-border pl-1">
                  <li
                    v-for="service in product.services"
                    :key="service"
                    role="treeitem"
                    :aria-selected="
                      state({ kind: 'service', workspace: workspace.slug, product: product.slug, service }) === 'checked'
                    "
                  >
                    <label class="flex min-h-11 cursor-pointer items-center gap-2 rounded-lg py-1 pr-2 pl-9 hover:bg-muted/60 md:min-h-9">
                      <CheckboxInput
                        size="sm"
                        :model-value="
                          ['checked', 'implied'].includes(
                            state({ kind: 'service', workspace: workspace.slug, product: product.slug, service }),
                          )
                        "
                        @update:model-value="toggle({ kind: 'service', workspace: workspace.slug, product: product.slug, service })"
                      />
                      <span class="truncate font-mono text-xs">{{ service }}</span>
                    </label>
                  </li>
                </ul>
              </li>
            </ul>
          </li>
        </ul>
      </div>
      <div class="flex items-center justify-between gap-2 border-t border-border px-3 py-2">
        <span class="text-xs text-muted-foreground">Mix workspaces, products and services.</span>
        <Button variant="ghost" tone="neutral" size="sm" :is-disabled="!props.modelValue.length" @click="emit('update:modelValue', [])">Clear</Button>
      </div>
  </div>
</template>
