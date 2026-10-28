<script lang="ts">
import type { TargetMigrations } from "@/domain/deployments/migrations";

/** Defines an environment's Migrations panel. */
export interface MigrationsPanelProps {
  /** The environment's target. */
  readonly target: string;
  /** What the target last learned about each migration set, from its state. */
  readonly migrations: TargetMigrations | null | undefined;
}
</script>

<script setup lang="ts">
import { computed, ref } from "vue";
import { DatabaseZap, ShieldCheck } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge, RelativeTime } from "@wow-two-beta/fe/presentation/display";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";

import { useMigrationsCheck } from "@/application/deployments";
import { ReleaseSets } from "@/domain/deployments";
import { MigrationRules, type MigrationCheck } from "@/domain/deployments/migrations";
import { failureReason } from "@/integration/common";
import { InfoPopover, StatusPill } from "@/presentation/common/components";

/**
 * An environment's migration sets as its target last read them (migrations spec M38 stage 1): per set its name, level,
 * newest migration with its kind, its state against the release, and which release read it when. Check now runs
 * `migrate check` from the image the environment runs (`GET /api/deployments/targets/{target}/migrations`, up to about
 * 90 seconds), shows any item that did not pass, and reads the target's state again. The page keys the panel by target,
 * so a check's answer never follows the owner to another environment.
 */
defineOptions({ name: "MigrationsPanel" });
const props = defineProps<MigrationsPanelProps>();
defineSlots<{}>();

const check = useMigrationsCheck();
/** The last check's answer. */
const result = ref<MigrationCheck | null>(null);
const sets = computed(() => MigrationRules.setsOf(props.migrations));
/** Rows name their service only when the sets come from several. */
const namesService = computed(() => MigrationRules.spansServices(sets.value));
const failures = computed(() => (result.value ? MigrationRules.failures(result.value) : []));
const blocking = computed(() => failures.value.some(MigrationRules.isBlocking));

/** Runs Check now; its answer replaces the last one. @internal */
async function checkNow(): Promise<void> {
  if (check.loading.value) return;
  result.value = null;
  const answer = await check.mutateAsync({ target: props.target });
  if (answer.ok) result.value = answer.value;
}
</script>

<template>
  <section class="wh-glass rounded-2xl border border-border p-5" aria-labelledby="migrations-heading" data-info-scope>
    <div class="mb-3 flex flex-wrap items-center gap-2">
      <div class="flex items-center gap-1">
        <h3 id="migrations-heading" class="flex items-center gap-2 text-sm font-semibold">
          <DatabaseZap :size="16" aria-hidden="true" />Migrations
        </h3>
        <InfoPopover title="Migrations">
          <p>
            Each migration set in this environment's databases: its level, the newest migration and its kind, and its state
            against the release — current, ahead but compatible (as after a roll back), or one the release refuses to run
            on. Read by the latest rollout's migrate step, a rollback gate, or Check now.
          </p>
          <p class="mt-2">
            Check now runs a write-free check from the image the environment runs, up to a minute and a half; it is refused
            while a rollout runs.
          </p>
        </InfoPopover>
      </div>
      <Button
        size="sm"
        variant="outline"
        tone="neutral"
        class="ms-auto max-md:min-h-11"
        :is-loading="check.loading.value"
        @click="checkNow"
      >
        <template #leading><ShieldCheck :size="14" /></template>Check now
      </Button>
    </div>
    <div aria-live="polite" class="empty:hidden mb-3 flex flex-col gap-2">
      <Alert
        v-if="check.error.value"
        severity="danger"
        title="Check unavailable"
        :description="failureReason(check.error.value)"
      />
      <Alert v-else-if="failures.length" :severity="blocking ? 'danger' : 'warning'" title="Check did not pass">
        <ul role="list" class="mt-1 flex flex-col gap-1 text-sm">
          <li v-for="item in failures" :key="item.service" class="break-words">
            <span class="font-medium">{{ item.service }}</span> · {{ item.status
            }}<template v-if="item.detail">: {{ item.detail }}</template>
          </li>
        </ul>
      </Alert>
      <p v-else-if="result && !result.items.length" class="text-sm text-muted-foreground">
        Nothing to check in {{ ReleaseSets.text(result.release) || "this release" }}
      </p>
    </div>
    <ul role="list" class="flex flex-col gap-2" aria-label="Migration sets">
      <li
        v-for="set in sets"
        :key="`${set.service}/${set.name}`"
        class="flex flex-wrap items-center gap-x-3 gap-y-1.5 rounded-xl border border-border bg-card px-3 py-2 text-sm"
      >
        <span class="min-w-0 font-medium break-all"
          ><span v-if="namesService" class="font-normal text-muted-foreground">{{ set.service }} · </span
          >{{ MigrationRules.nameOf(set) }}</span
        >
        <span class="text-xs text-muted-foreground tabular-nums">level {{ set.level ?? "—" }}</span>
        <span v-if="set.newestLabel" class="flex min-w-0 items-center gap-1.5">
          <span class="truncate font-mono text-xs" :title="set.newestLabel">{{ set.newestLabel }}</span>
          <Badge v-if="set.newestKind" size="sm" :variant="set.newestKind === 'contract' ? 'warning' : 'outline'">{{
            set.newestKind
          }}</Badge>
        </span>
        <StatusPill :state="MigrationRules.stateOf(set.state)" :label="MigrationRules.wordOf(set.state)" />
        <span class="text-xs text-muted-foreground sm:ms-auto"
          ><template v-if="set.release">{{ ReleaseSets.text(set.release) }} · </template
          >{{ MigrationRules.sourceOf(set.source) }} <RelativeTime :value="set.at"
        /></span>
      </li>
    </ul>
  </section>
</template>
