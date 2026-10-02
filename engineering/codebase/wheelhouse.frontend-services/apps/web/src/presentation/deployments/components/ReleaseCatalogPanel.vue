<script setup lang="ts">
import { computed, ref } from "vue";
import { Rocket } from "lucide-vue-next";

import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import {
  Badge,
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
import { Measures } from "@/domain/common";
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
      <div class="w-44">
        <SelectPicker v-model="product" is-clearable clear-label="All products"
          ><SelectPickerTrigger aria-label="Product"
            ><SelectPickerValue placeholder="All products" /></SelectPickerTrigger
          ><SelectPickerContent
            ><SelectPickerItem
              v-for="name in products"
              :key="name"
              :item-key="name"
              :value="name"
              :label="name" /></SelectPickerContent
        ></SelectPicker>
      </div>
      <div class="w-48">
        <SelectPicker v-model="kind"
          ><SelectPickerTrigger aria-label="Kind"><SelectPickerValue /></SelectPickerTrigger
          ><SelectPickerContent
            ><SelectPickerItem
              v-for="(label, value) in KindFilter"
              :key="value"
              :item-key="value"
              :value="value"
              :label="label" /></SelectPickerContent
        ></SelectPicker>
      </div>
      <RefreshButton variant="ghost" :refreshing="refresh.refreshing.value" @refresh="refresh.refresh()" />
    </template>
    <LoadState
      :loading="releases.loading.value && !releases.data.value"
      :error="releases.error.value"
      :has-data="Boolean(releases.data.value)"
      :empty="!visible.length"
      empty-title="No releases"
      empty-description="Publish a release or push a commit; its build appears here once its workflow finishes."
      @retry="releases.refetch()"
    >
      <Table density="compact" is-hoverable container-class-name="max-h-[24rem] overflow-auto">
        <TableHead class="sticky top-0 z-10 bg-card">
          <TableRow>
            <TableHeaderCell>Release</TableHeaderCell><TableHeaderCell>Product</TableHeaderCell
            ><TableHeaderCell>Commit</TableHeaderCell><TableHeaderCell>Published</TableHeaderCell
            ><TableHeaderCell><span class="sr-only">Actions</span></TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          <TableRow v-for="artifact in visible" :key="artifact.id">
            <TableCell>
              <span class="block font-mono text-xs font-medium">{{ artifact.release }}</span>
              <Badge class="mt-1" :variant="artifact.kind === 'release' ? 'success' : 'neutral'">{{
                artifact.kind === "release" ? (artifact.prerelease ? "Pre-release" : "Release") : "Commit build"
              }}</Badge>
            </TableCell>
            <TableCell class="text-xs">{{ artifact.product }}</TableCell>
            <TableCell class="font-mono text-xs">
              <span class="block">{{ artifact.commit?.slice(0, 7) ?? "—" }}</span>
              <span v-if="artifact.branch" class="mt-1 block text-muted-foreground">{{ artifact.branch }}</span>
            </TableCell>
            <TableCell class="whitespace-nowrap text-xs text-muted-foreground">
              <span class="block" :title="new Date(artifact.publishedAt).toLocaleString()">{{
                Measures.moment(artifact.publishedAt)
              }}</span>
              <span v-if="artifact.expiresAt" class="mt-1 block"
                >expires {{ Measures.moment(artifact.expiresAt) }}</span
              >
            </TableCell>
            <TableCell class="text-right">
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
