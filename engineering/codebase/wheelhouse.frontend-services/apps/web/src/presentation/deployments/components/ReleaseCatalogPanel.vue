<script setup lang="ts">
import { computed, ref } from "vue";
import { Rocket } from "lucide-vue-next";

import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import {
  Badge,
  RelativeTime,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@wow-two-beta/ui-vue/presentation/display";
import {
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
} from "@wow-two-beta/ui-vue/presentation/forms";

import { useRefresh } from "@/bootstrap/query";
import { useReleaseArtifacts } from "@/application/deployments";
import { LoadState, Panel } from "@/presentation/common/components";
import { RefreshButton } from "@/presentation/common/components";

/** Lists the deployable catalog: published releases and commit builds from approved repositories, newest first. */
defineOptions({ name: "ReleaseCatalogPanel" });
const emit = defineEmits<{ deploy: [bundleId: string] }>();

const KindFilter = { all: "Releases and builds", release: "Releases", candidate: "Commit builds" } as const;
type KindFilter = keyof typeof KindFilter;

const releases = useReleaseArtifacts();
const refresh = useRefresh(releases.refetch);
const product = ref<string | null>(null);
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
</script>

<template>
  <Panel
    title="Releases"
    description="Published releases go anywhere; a commit's build goes to dev only and expires after 14 days."
  >
    <template #actions>
      <SelectPicker v-model="product" is-clearable clear-label="All products"
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
      <SelectPicker v-model="kind" :get-option-label="(value) => KindFilter[value as KindFilter]"
        ><SelectPickerTrigger aria-label="Kind" width="12rem"><SelectPickerValue /></SelectPickerTrigger
        ><SelectPickerContent
          ><SelectPickerItem
            v-for="(label, value) in KindFilter"
            :key="value"
            :item-key="value"
            :value="value"
            :label="label" /></SelectPickerContent
      ></SelectPicker>
      <RefreshButton variant="ghost" :refreshing="refresh.refreshing.value" @refresh="refresh.refresh()" />
    </template>
    <LoadState
      :loading="releases.isFirstLoad.value"
      :error="releases.error.value"
      :has-data="Boolean(releases.data.value)"
      :empty="!visible.length"
      empty-title="No releases"
      empty-description="Publish a release or push a commit; its build appears here once its workflow finishes."
      @retry="releases.refetch()"
    >
      <Table density="compact" is-hoverable has-sticky-header container-class-name="max-h-[24rem] overflow-auto">
        <TableHead>
          <TableRow>
            <TableHeaderCell>Release</TableHeaderCell><TableHeaderCell>Product</TableHeaderCell
            ><TableHeaderCell>Commit</TableHeaderCell><TableHeaderCell>Published</TableHeaderCell
            ><TableHeaderCell><span class="sr-only">Actions</span></TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          <TableRow v-for="artifact in visible" :key="artifact.id">
            <TableCell>
              <span class="block min-w-0">
                <span class="block font-mono text-xs font-medium">{{ artifact.release }}</span>
                <Badge class="mt-1" :variant="artifact.kind === 'release' ? 'success' : 'neutral'">{{
                  artifact.kind === "release" ? (artifact.prerelease ? "Pre-release" : "Release") : "Commit build"
                }}</Badge>
              </span>
            </TableCell>
            <TableCell class="text-xs">{{ artifact.product }}</TableCell>
            <TableCell class="font-mono text-xs">
              <span class="block min-w-0">
                <span class="block">{{ artifact.commit?.slice(0, 7) ?? "—" }}</span>
                <span v-if="artifact.branch" class="mt-1 block text-muted-foreground">{{ artifact.branch }}</span>
              </span>
            </TableCell>
            <TableCell class="whitespace-nowrap text-xs text-muted-foreground">
              <span class="block">
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
                :aria-label="`Deploy ${artifact.release}`"
                title="Deploy this release"
                @click="emit('deploy', artifact.id)"
                ><template #leading><Rocket :size="14" /></template
              ></Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </LoadState>
  </Panel>
</template>
