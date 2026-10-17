<script lang="ts">
import type { PortfolioGroup } from '@/application/portfolio/Portfolio';

/** One workspace in the full sidebar: its row and, when expanded, its products. */
export interface WorkspaceNavGroupProps {
  readonly group: PortfolioGroup;
}
</script>
<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { ChevronRight } from 'lucide-vue-next';
import { SidebarMenuItem } from '@wow-two-beta/fe/presentation/nav';
import { ConsolePaths } from '@/application/navigation';
import { useWorkspaceExpansion } from '@/application/preferences';
import { ProductStatusRules } from '@/domain/products';
import { ProductIcon } from '@/presentation/products';

/**
 * A workspace row whose name opens the workspace's page and whose chevron folds its products, remembered per
 * workspace. The SDK `SidebarMenuGroup` makes its whole row the toggle, so this split row stays local.
 */
defineOptions({ name: 'WorkspaceNavGroup' });
const props = defineProps<WorkspaceNavGroupProps>();
const emit = defineEmits<{ navigate: [] }>();
defineSlots<{}>();
const route = useRoute();
const expansion = useWorkspaceExpansion();
const key = computed(() => props.group.workspace?.id ?? 'none');
const name = computed(() => props.group.workspace?.name ?? 'No workspace');
const expanded = computed(() => expansion.isExpanded(key.value));
const listId = computed(() => `workspace-nav-${key.value}`);
</script>
<template>
  <li class="flex flex-col">
    <div class="flex items-center gap-0.5">
      <ul role="list" class="min-w-0 flex-1">
        <SidebarMenuItem
          v-if="props.group.workspace"
          as-child
          :is-active="route.params['workspace'] === props.group.workspace.slug"
        >
          <template #icon><ProductIcon :name="name" size="xs" /></template>
          <template #trailing
            ><span class="text-xs tabular-nums text-muted-foreground">{{ props.group.products.length }}</span></template
          >
          <RouterLink :to="ConsolePaths.workspace(props.group.workspace.slug)" @click="emit('navigate')"
            ><span class="min-w-0 flex-1 truncate text-start">{{ name }}</span></RouterLink
          >
        </SidebarMenuItem>
        <li v-else class="flex h-9 items-center gap-2.5 px-2.5 text-sm font-medium text-muted-foreground">
          <ProductIcon :name="name" size="xs" /><span class="min-w-0 flex-1 truncate">{{ name }}</span
          ><span class="text-xs tabular-nums">{{ props.group.products.length }}</span>
        </li>
      </ul>
      <button
        type="button"
        class="inline-flex size-7 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring"
        :aria-expanded="expanded"
        :aria-controls="listId"
        :aria-label="`${expanded ? 'Hide' : 'Show'} the products in ${name}`"
        @click="expansion.toggle(key)"
      >
        <ChevronRight
          :size="14"
          class="transition-transform duration-150 motion-reduce:transition-none"
          :class="expanded ? 'rotate-90' : ''"
        />
      </button>
    </div>
    <ul
      v-show="expanded"
      :id="listId"
      role="list"
      :aria-label="name"
      class="ms-4 flex flex-col gap-0.5 border-s border-border ps-2 pt-0.5"
    >
      <SidebarMenuItem
        v-for="product in props.group.products"
        :key="product.slug"
        as-child
        :is-active="route.params['product'] === product.slug"
      >
        <template #icon><ProductIcon :product-slug="product.slug" :name="product.name" size="xs" /></template>
        <RouterLink
          :to="ConsolePaths.product(product.slug)"
          :class="ProductStatusRules.isActive(product) ? '' : 'opacity-60'"
          @click="emit('navigate')"
          ><span class="min-w-0 flex-1 truncate text-start">{{ product.name }}</span></RouterLink
        >
      </SidebarMenuItem>
      <li v-if="!props.group.products.length" class="px-2.5 py-1.5 text-xs text-muted-foreground">
        {{ props.group.total ? 'Only inactive products' : 'No products yet' }}
      </li>
    </ul>
  </li>
</template>
