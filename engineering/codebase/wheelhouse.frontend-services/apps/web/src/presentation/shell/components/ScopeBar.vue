<script lang="ts">
import type { ScopeCrumb } from '@/application/navigation';

/** The top bar's scope and the current page's actions. */
export interface ScopeBarProps {
  /** The scope, outermost first; the last crumb is the current page. */
  readonly crumbs: readonly ScopeCrumb[];
}
</script>
<script setup lang="ts">
import { computed } from 'vue';
import { useRouter } from 'vue-router';
import { Breadcrumb } from '@wow-two-beta/fe/presentation/nav';
import { PageHeader } from '@wow-two-beta/fe/presentation/layout';
import { ProductIcon } from '@/presentation/products';

/**
 * Names where the operator stands — Home › Workspace › Product › Service — and holds the actions a page contributes
 * through `PageHeaderActions`. The page's heading is the current crumb, kept for assistive technology; the trail
 * shows it on screen. On phones the trail drops Home and keeps its last two steps.
 */
defineOptions({ name: 'ScopeBar' });
const props = defineProps<ScopeBarProps>();
defineSlots<{}>();
const router = useRouter();
const items = computed(() =>
  props.crumbs.map((crumb) => ({ label: crumb.label, href: router.resolve(crumb.location).href })),
);
const current = computed(() => props.crumbs.at(-1)?.label ?? '');

/**
 * Opens a trail link through the router, so the console never reloads; a modified click keeps the browser's own
 * behaviour, such as a new tab. @internal
 */
function follow(event: MouseEvent): void {
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey)
    return;
  const href = (event.target as Element | null)?.closest('a')?.getAttribute('href');
  if (!href?.startsWith('/')) return;
  event.preventDefault();
  void router.push(href);
}
</script>
<template>
  <!-- `contents`: the trail and the actions are items of the top bar itself, so on phones the actions wrap to a
       row of their own under the trail, the mark and the profile button. -->
  <div class="contents">
    <Breadcrumb
      :items="items"
      class="wh-scope-trail min-w-0 flex-1 text-sm"
      @click="follow"
    >
      <template #label="{ index }">
        <ProductIcon
          v-if="props.crumbs[index]?.level === 'product'"
          :product-slug="props.crumbs[index]?.productSlug ?? null"
          :name="props.crumbs[index]?.label ?? ''"
          size="xs"
          class="mr-1.5 inline-flex -translate-y-px align-middle"
        />{{ props.crumbs[index]?.label }}</template
      >
    </Breadcrumb>
    <!-- The page's actions land here; a page without any leaves the row empty, and CSS drops it. -->
    <PageHeader :level="1" size="md" class="wh-page-actions min-w-0 items-center gap-2 pb-0">
      <template #title><span class="sr-only">{{ current }}</span></template>
    </PageHeader>
  </div>
</template>
