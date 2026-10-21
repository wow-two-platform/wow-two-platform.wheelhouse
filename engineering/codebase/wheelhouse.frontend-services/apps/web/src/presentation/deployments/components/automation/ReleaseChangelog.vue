<script setup lang="ts">
import { computed, ref } from 'vue';
import { ExternalLink } from 'lucide-vue-next';

import { Badge } from '@wow-two-beta/fe/presentation/display';
import { Alert, Spinner } from '@wow-two-beta/fe/presentation/feedback';

import { useReleaseChangelog } from '@/application/deployments';
import { ReleaseSets } from '@/domain/deployments';

/**
 * What changed between what a target runs and the release to promote, read when it is first opened: each release's
 * notes as GitHub holds them (plain text, newest first), then the commits between the two, with the changed files that
 * look like schema migrations. `open` starts it expanded, as the prod confirm step does.
 */
defineOptions({ name: 'ReleaseChangelog' });
const props = withDefaults(
  defineProps<{ readonly product: string; readonly from: string | null; readonly to: string; readonly open?: boolean }>(),
  { open: false },
);
defineSlots<{}>();

const expanded = ref(props.open);
const changelog = useReleaseChangelog(
  () => props.product,
  () => props.from,
  () => props.to,
  () => expanded.value,
);
const data = computed(() => changelog.data.value);
const title = computed(() =>
  props.from ? `What changed since ${ReleaseSets.text(props.from)}` : `What ${ReleaseSets.text(props.to)} holds`,
);
</script>

<template>
  <details class="rounded-md border border-border text-sm" :open="props.open" @toggle="expanded = ($event.target as HTMLDetailsElement).open || expanded">
    <summary class="cursor-pointer px-3 py-2 font-medium">{{ title }}</summary>
    <div class="flex flex-col gap-3 border-t border-border px-3 py-3">
      <Spinner v-if="changelog.loading.value && !data" size="sm" label="Reading the changelog" />
      <Alert v-else-if="changelog.error.value" severity="warning" title="Couldn't read the changelog" :description="changelog.error.value.message" />
      <template v-else-if="data">
        <p v-if="data.notYet.length" class="text-xs text-muted-foreground">
          Not in this set yet: {{ ReleaseSets.notYet(data.notYet) }}. A repository joins the product's sets with its first release.
        </p>
        <p v-if="data.migrations.length" class="flex flex-wrap items-center gap-2">
          <Badge variant="warning">Has migrations</Badge>
          <span class="text-xs text-muted-foreground">{{ data.migrations.length }} changed {{ data.migrations.length === 1 ? 'file' : 'files' }} under Migrations/</span>
        </p>
        <section v-for="item in data.releases" :key="item.release" class="flex flex-col gap-1">
          <a :href="item.url" target="_blank" rel="noopener noreferrer" class="inline-flex items-center gap-1 font-mono text-xs font-medium hover:text-primary">
            {{ item.name }}<ExternalLink :size="12" aria-hidden="true" />
          </a>
          <pre v-if="item.body.trim()" class="max-h-48 overflow-auto whitespace-pre-wrap break-words font-sans text-xs text-muted-foreground">{{ item.body }}<template v-if="item.truncated">…</template></pre>
          <p v-else class="text-xs text-muted-foreground">No notes.</p>
        </section>
        <section v-if="data.commits?.length" class="flex flex-col gap-1">
          <p class="text-xs font-medium">
            {{ data.totalCommits ?? data.commits.length }} {{ (data.totalCommits ?? data.commits.length) === 1 ? 'commit' : 'commits' }}
            <a v-if="data.compareUrl" :href="data.compareUrl" target="_blank" rel="noopener noreferrer" class="ml-1 font-normal text-primary hover:underline">compare</a>
          </p>
          <ul class="flex max-h-48 flex-col gap-0.5 overflow-auto">
            <li v-for="commit in data.commits" :key="commit.sha" class="flex min-w-0 items-baseline gap-2 text-xs">
              <a :href="commit.url" target="_blank" rel="noopener noreferrer" class="shrink-0 font-mono text-primary hover:underline">{{ commit.sha.slice(0, 7) }}</a>
              <span class="min-w-0 truncate text-muted-foreground" :title="commit.message">{{ commit.message }}</span>
            </li>
          </ul>
        </section>
        <p v-else-if="data.reason" class="text-xs text-muted-foreground">Commits unavailable: {{ data.reason }}</p>
        <p v-if="!data.releases.length && !data.commits?.length" class="text-xs text-muted-foreground">Nothing recorded between these releases.</p>
      </template>
    </div>
  </details>
</template>
