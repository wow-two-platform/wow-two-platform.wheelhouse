<script lang="ts">
import type { DeploymentTarget, ReleaseArtifact } from "@/domain/deployments";

/** Defines a product's environments to compare and the catalog that promotions draw from. */
export interface EnvironmentCompareProps {
  /** Every environment of one product. */
  readonly targets: readonly DeploymentTarget[];
  /** The release catalog; a promotion needs the release's bundle. */
  readonly releases: readonly ReleaseArtifact[];
  /** Services left out of the rows: the inactive ones while they are hidden. */
  readonly hiddenServices?: readonly string[];
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { ArrowRight } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@wow-two-beta/fe/presentation/display";

import { useTargetStates } from "@/application/deployments";
import { compareEnvironments } from "@/domain/deployments";
import { InfoPopover } from "@/presentation/common/components";

/** Lays a product's environments side by side: each service's version per environment, and the release each
 * environment could hand to the next one along dev → prod. How to read it sits behind the info button. */
defineOptions({ name: "EnvironmentCompare" });
const props = withDefaults(defineProps<EnvironmentCompareProps>(), { hiddenServices: () => [] });
const emit = defineEmits<{ promote: [selection: { target: string; release: string }] }>();

const states = useTargetStates(() => props.targets);
const comparison = computed(() =>
  compareEnvironments(props.targets, states.data.value, props.releases),
);
/** The rows shown: every service but the hidden ones. Promotions still move whole environments. */
const rows = computed(() => comparison.value.rows.filter((row) => !props.hiddenServices.includes(row.service)));
</script>

<template>
  <section
    v-if="props.targets.length > 1"
    class="wh-glass rounded-2xl border border-border p-5"
    aria-labelledby="environments-heading"
    data-info-scope
  >
    <div class="mb-3 flex items-center gap-1">
      <h3 id="environments-heading" class="text-sm font-semibold">Environments</h3>
      <InfoPopover title="Environments">
        <p>
          Each service's version per environment, from the release last verified there; a column's head names that
          release. A highlighted version has not reached the next environment along dev → prod yet, and Promote takes
          it there. Point at a version for the release it last changed in.
        </p>
      </InfoPopover>
    </div>
    <Table density="compact" phone-layout="scroll" container-class-name="overflow-auto">
      <TableHead>
        <TableRow>
          <TableHeaderCell>Service</TableHeaderCell>
          <TableHeaderCell v-for="column in comparison.columns" :key="column.targetId">
            <span class="block capitalize">{{ column.environment }}</span>
            <span class="block font-mono text-[11px] font-normal text-muted-foreground">{{
              !column.read ? "Reading…" : (column.release ?? "Not deployed")
            }}</span>
          </TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        <TableRow v-for="row in rows" :key="row.service">
          <TableCell class="text-sm font-medium">{{ row.service }}</TableCell>
          <TableCell
            v-for="(version, index) in row.versions"
            :key="comparison.columns[index]!.targetId"
            class="font-mono text-xs"
            :title="version ? `Last changed in ${version.changedIn}` : ''"
          >
            <span
              v-if="version"
              :class="row.pending[index] ? 'rounded-md bg-primary-soft px-1.5 py-0.5 text-primary-soft-foreground' : ''"
              >{{ version.version }}</span
            ><span v-else class="text-muted-foreground">—</span>
            <span v-if="row.pending[index]" class="sr-only">
              , not yet on {{ comparison.columns[index + 1]?.environment }}</span
            >
          </TableCell>
        </TableRow>
        <TableRow v-if="!rows.length">
          <TableCell :col-span="comparison.columns.length + 1" class="text-sm text-muted-foreground">
            No environment has a verified release with service versions yet.
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
    <div
      v-if="comparison.columns.some((column) => column.promotion)"
      class="mt-3 flex flex-wrap gap-2"
      aria-label="Promotions"
    >
      <template v-for="column in comparison.columns" :key="column.targetId">
        <Button
          v-if="column.promotion"
          size="sm"
          variant="outline"
          @click="
            emit('promote', {
              target: column.promotion.targetId,
              release: column.promotion.bundleId,
            })
          "
          >Promote {{ column.promotion.release }} to {{ column.promotion.environment
          }}<template #trailing><ArrowRight :size="14" /></template
        ></Button>
      </template>
    </div>
  </section>
</template>
