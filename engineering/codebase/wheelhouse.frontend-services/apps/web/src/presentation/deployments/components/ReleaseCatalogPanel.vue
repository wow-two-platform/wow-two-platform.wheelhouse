<script lang="ts">
/** The deployable catalog, across products or for one. */
export interface ReleaseCatalogPanelProps {
  /** Keeps one product's releases and drops the product picker and column; every product's without it. */
  readonly product?: string | null;
}
</script>

<script setup lang="ts">
import { ReleaseSets } from "@/domain/deployments";
import { computed, ref } from "vue";
import { Rocket } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import {
  Badge,
  RelativeTime,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Text,
} from "@wow-two-beta/fe/presentation/display";
import { DataRegion, useSkeletonItems } from "@wow-two-beta/fe/presentation/feedback";
import {
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
} from "@wow-two-beta/fe/presentation/forms";

import { useRefresh } from "@/bootstrap/query";
import { useReleaseArtifacts } from "@/application/deployments";
import { InfoPopover, Panel, RefreshButton } from "@/presentation/common/components";

/**
 * Lists the deployable catalog: published releases and commit builds from approved repositories, newest first. A
 * product's Deployments tab passes its product, so the panel lists that product's releases only.
 */
defineOptions({ name: "ReleaseCatalogPanel" });
const props = withDefaults(defineProps<ReleaseCatalogPanelProps>(), { product: null });
const emit = defineEmits<{ deploy: [bundleId: string] }>();

const KindFilter = { all: "Releases and builds", release: "Releases", candidate: "Commit builds" } as const;
type KindFilter = keyof typeof KindFilter;

const releases = useReleaseArtifacts();
const refresh = useRefresh(releases.refetch);
const picked = ref<string | null>(null);
/** The product the list keeps: the page's own, or the one picked. */
const product = computed(() => props.product ?? picked.value);
const kind = ref<KindFilter>("all");
const products = computed(() =>
  [...new Set((releases.data.value ?? []).map((artifact) => artifact.product))].sort(),
);
const visible = computed(() =>
  [...(releases.data.value ?? [])]
    .filter(
      (artifact) =>
        (!product.value || artifact.product === product.value) &&
        (kind.value === "all" || artifact.kind === kind.value),
    )
    .sort((left, right) => Date.parse(right.publishedAt) - Date.parse(left.publishedAt)),
);
/** The shown releases, or placeholder rows until the first catalog arrives. */
const rows = useSkeletonItems(() => (releases.data.value ? visible.value : undefined), 4);
</script>

<template>
  <Panel title="Releases and builds">
    <template #info>
      <InfoPopover title="Releases and builds">
        <p>
          A published release deploys to any environment. A commit's build deploys to dev only and expires after 14
          days.
        </p>
      </InfoPopover>
    </template>
    <template #actions>
      <SelectPicker v-if="!props.product" v-model="picked" is-clearable clear-label="All products"
        ><SelectPickerTrigger aria-label="Product" width="11rem"
          ><SelectPickerValue placeholder="All products" /></SelectPickerTrigger
        ><SelectPickerContent
          ><SelectPickerItem
            v-for="name in products"
            :key="name"
            :item-key="name"
            :value="name"
            :label="name" /></SelectPickerContent
      ></SelectPicker>
      <SelectPicker v-model="kind"
        ><SelectPickerTrigger aria-label="Kind" width="12rem"><SelectPickerValue /></SelectPickerTrigger
        ><SelectPickerContent
          ><SelectPickerItem
            v-for="(label, value) in KindFilter"
            :key="value"
            :item-key="value"
            :value="value"
            :label="label" /></SelectPickerContent
      ></SelectPicker>
      <RefreshButton variant="ghost" size="md" :refreshing="refresh.refreshing.value" @refresh="refresh.refresh()" />
    </template>
    <DataRegion
      :is-first-load="releases.isFirstLoad.value"
      :is-refreshing="releases.isRefreshing.value"
      :error="releases.error.value"
      :has-data="Boolean(releases.data.value)"
      :is-empty="Boolean(releases.data.value) && !visible.length"
      empty-title="No releases"
      empty-description="Publish a release or push a commit; its build appears here once its workflow finishes."
      loading-label="Loading releases"
    >
      <Table density="compact" is-hoverable has-sticky-header container-class-name="max-h-[24rem] overflow-auto">
        <TableHead>
          <TableRow>
            <TableHeaderCell>Release</TableHeaderCell><TableHeaderCell v-if="!props.product">Product</TableHeaderCell
            ><TableHeaderCell>Commit</TableHeaderCell><TableHeaderCell>Published</TableHeaderCell
            ><TableHeaderCell><span class="sr-only">Actions</span></TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          <TableRow v-for="(artifact, index) in rows" :key="artifact?.id ?? `placeholder-${index}`">
            <!-- The kind sits beside the version and wraps under it only when the cell is too narrow. -->
            <TableCell>
              <span class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
                <Text as="span" class="font-mono text-xs font-medium">{{ ReleaseSets.text(artifact?.release) }}</Text>
                <Badge :variant="artifact?.kind === 'candidate' ? 'neutral' : 'success'">{{
                  artifact?.kind === "candidate" ? "Commit build" : artifact?.prerelease ? "Pre-release" : "Release"
                }}</Badge>
              </span>
            </TableCell>
            <TableCell v-if="!props.product" class="text-xs">{{ artifact?.product }}</TableCell>
            <!-- A placeholder row leaves composite cells empty, so each draws one bar. -->
            <TableCell class="font-mono text-xs">
              <span v-if="artifact" class="block min-w-0">
                <span class="block">{{ artifact.commit?.slice(0, 7) ?? "—" }}</span>
                <span v-if="artifact.branch" class="mt-1 block text-muted-foreground">{{ artifact.branch }}</span>
              </span>
            </TableCell>
            <TableCell class="whitespace-nowrap text-xs text-muted-foreground">
              <span v-if="artifact" class="block">
                <RelativeTime class="block" :value="artifact.publishedAt" />
                <span v-if="artifact.expiresAt" class="mt-1 block"
                  >expires <RelativeTime :value="artifact.expiresAt"
                /></span>
              </span>
            </TableCell>
            <TableCell label="" class="justify-end text-right">
              <Button
                variant="ghost"
                tone="neutral"
                size="sm"
                shape="square"
                class="max-md:size-11"
                :aria-label="`Deploy ${ReleaseSets.text(artifact?.release) || 'release'}`"
                title="Deploy this release"
                @click="artifact && emit('deploy', artifact.id)"
                ><template #leading><Rocket :size="14" /></template
              ></Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </DataRegion>
  </Panel>
</template>
