<script lang="ts">
import type { DeploymentActivity, StatusFilter } from "@/domain/deployments";

/** Defines the deployment activity calendar. */
export interface ActivityGridProps {
  /** Finished rollouts per local day over the range; undefined until the first read, which draws an empty range. */
  readonly activity: DeploymentActivity | undefined;
  /** The status filter, which decides how a day is coloured. */
  readonly status: StatusFilter;
  /** The day expanded below the calendar, `YYYY-MM-DD`. */
  readonly selected: string | null;
  /** A rollout runs now: today's cell beats. */
  readonly isLive?: boolean;
}
</script>

<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";

import { ActivityCalendar, DeploymentFilters, type CalendarCell } from "@/domain/deployments";

/**
 * A GitHub-style calendar of deployments: week columns over weekday rows, month labels above. A day is green when
 * every rollout succeeded, red when every one failed, amber when both; four steps of fill follow the count (1, 2–3,
 * 4–6, 7+). Failed and mixed days also carry an inner mark, every day names its date and counts, and today keeps a
 * ring — beating while a rollout runs, still under reduced motion. Arrow keys move between days; a day toggles its list.
 */
defineOptions({ name: "ActivityGrid" });
const props = withDefaults(defineProps<ActivityGridProps>(), { isLive: false });
const emit = defineEmits<{ select: [day: string | null] }>();

const scroller = ref<HTMLElement | null>(null);
const surface = ref<HTMLElement | null>(null);
const today = DeploymentFilters.localDate(new Date());
/** The day that holds the tab stop; arrow keys move it. */
const active = ref<string | null>(null);
/** The day the shared tooltip describes, and where it sits. */
const hint = ref<{ label: string; left: number; top: number } | null>(null);

const calendar = computed(() => {
  const activity = props.activity;
  if (activity) return ActivityCalendar.build(activity.from, activity.to, activity.days, props.status);
  const end = new Date();
  const start = new Date(end);
  start.setMonth(start.getMonth() - 6);
  return ActivityCalendar.build(DeploymentFilters.localDate(start), DeploymentFilters.localDate(end), [], props.status);
});
const cells = computed(() => calendar.value.weeks.flatMap((week) => week.cells.filter((cell): cell is CalendarCell => cell !== null)));
const first = computed(() => cells.value[0]?.date ?? today);
const last = computed(() => cells.value.at(-1)?.date ?? today);
const tabStop = computed(() => {
  const wanted = active.value ?? props.selected ?? (today <= last.value && today >= first.value ? today : last.value);
  return wanted >= first.value && wanted <= last.value ? wanted : last.value;
});
const totals = computed(() =>
  cells.value.reduce(
    (sum, cell) => ({ succeeded: sum.succeeded + cell.succeeded, failed: sum.failed + cell.failed }),
    { succeeded: 0, failed: 0 },
  ),
);

const fills: Record<CalendarCell["tone"], readonly string[]> = {
  empty: ["bg-muted", "bg-muted", "bg-muted", "bg-muted", "bg-muted"],
  success: ["bg-muted", "bg-success/30", "bg-success/55", "bg-success/80", "bg-success"],
  danger: ["bg-muted", "bg-destructive/30", "bg-destructive/55", "bg-destructive/80", "bg-destructive"],
  warning: ["bg-muted", "bg-warning/35", "bg-warning/60", "bg-warning/85", "bg-warning"],
};

function fill(cell: CalendarCell): string {
  return fills[cell.tone][cell.level] ?? "bg-muted";
}

/**
 * A mark readable on its fill in either scheme. The strong steps take the tone's own foreground — white on the light
 * theme's deep fills, dark on the dark theme's pale ones; the faint steps take the text colour, except the dark theme's
 * second step, already pale enough for the tone's dark foreground.
 */
function markColour(cell: CalendarCell): string {
  const warning = cell.tone === "warning";
  if (cell.level >= 3) return warning ? "bg-warning-foreground/90" : "bg-destructive-foreground/90";
  if (cell.level === 2)
    return warning ? "bg-foreground/65 dark:bg-warning-foreground/90" : "bg-foreground/65 dark:bg-destructive-foreground/90";
  return "bg-foreground/65";
}

/** Keeps the newest weeks in view: on a phone the calendar scrolls sideways and opens at today. */
watch(
  () => calendar.value.weeks.length,
  async () => {
    await nextTick();
    if (scroller.value) scroller.value.scrollLeft = scroller.value.scrollWidth;
  },
  { immediate: true, flush: "post" },
);

function show(event: Event, cell: CalendarCell): void {
  const target = event.currentTarget as HTMLElement | null;
  const box = surface.value?.getBoundingClientRect();
  const rect = target?.getBoundingClientRect();
  if (!box || !rect) return;
  hint.value = { label: cell.label, left: rect.left - box.left + rect.width / 2, top: rect.top - box.top };
}

function hide(): void {
  hint.value = null;
}

function choose(cell: CalendarCell): void {
  active.value = cell.date;
  emit("select", props.selected === cell.date ? null : cell.date);
}

async function move(event: KeyboardEvent, cell: CalendarCell): Promise<void> {
  let next: string | null = null;
  if (event.key === "Home") next = first.value;
  else if (event.key === "End") next = last.value;
  else next = ActivityCalendar.step(cell.date, event.key);
  if (!next) return;
  event.preventDefault();
  if (next < first.value || next > last.value) return;
  active.value = next;
  await nextTick();
  const button = surface.value?.querySelector<HTMLButtonElement>(`[data-date="${next}"]`);
  button?.focus();
  button?.scrollIntoView({ block: "nearest", inline: "nearest" });
}
</script>

<template>
  <div ref="surface" class="relative min-w-0">
    <div ref="scroller" class="overflow-x-auto overscroll-x-contain pb-1" tabindex="-1">
      <div
        class="grid w-full gap-[3px] p-1.5"
        :style="{
          gridTemplateColumns: `2rem repeat(${calendar.weeks.length}, minmax(0.875rem, 2.5rem))`,
          minWidth: `calc(2.75rem + ${calendar.weeks.length} * (0.875rem + 3px))`,
        }"
        role="group"
        :aria-label="`Deployments per day, ${first} to ${last}`"
      >
        <span
          v-for="month in calendar.months"
          :key="`${month.label}-${month.column}`"
          class="row-start-1 overflow-visible text-[0.6875rem] whitespace-nowrap text-muted-foreground"
          :style="{ gridColumn: `${month.column + 2} / span ${Math.max(1, Math.min(3, calendar.weeks.length - month.column))}` }"
          aria-hidden="true"
          >{{ month.label }}</span
        >
        <span
          v-for="(weekday, row) in calendar.weekdays"
          :key="weekday"
          class="sticky left-0 z-[1] col-start-1 flex items-center bg-card pr-1 text-[0.6875rem] text-muted-foreground"
          :style="{ gridRow: row + 2 }"
          aria-hidden="true"
          >{{ row % 2 === 0 ? weekday : "" }}</span
        >
        <template v-for="(week, column) in calendar.weeks" :key="week.start">
          <template v-for="cell in week.cells" :key="cell?.date ?? `${week.start}-gap`">
            <button
              v-if="cell"
              type="button"
              :data-date="cell.date"
              :tabindex="cell.date === tabStop ? 0 : -1"
              :aria-label="cell.label"
              :aria-pressed="props.selected === cell.date"
              :aria-current="cell.date === today ? 'date' : undefined"
              :class="[
                '@container relative flex aspect-square w-full items-center justify-center rounded-[3px] transition-[outline-color] focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-primary',
                fill(cell),
                props.selected === cell.date ? 'outline-2 outline-offset-1 outline-foreground' : '',
                cell.date === today ? 'ring-2 ring-primary ring-offset-1 ring-offset-card' : '',
              ]"
              :style="{ gridColumn: column + 2, gridRow: cell.row + 2 }"
              @click="choose(cell)"
              @keydown="move($event, cell)"
              @mouseenter="show($event, cell)"
              @mouseleave="hide"
              @focus="show($event, cell)"
              @blur="hide"
            >
              <span v-if="cell.mark === 'failed'" :class="['size-[clamp(0.375rem,20cqw,0.625rem)] rounded-full', markColour(cell)]" aria-hidden="true" />
              <span v-else-if="cell.mark === 'mixed'" :class="['h-[clamp(0.125rem,6cqw,0.1875rem)] w-[clamp(0.5rem,32cqw,0.875rem)] rounded-full', markColour(cell)]" aria-hidden="true" />
              <span v-if="cell.date === today && props.isLive" class="wh-heartbeat" aria-hidden="true" />
            </button>
          </template>
        </template>
      </div>
    </div>
    <div
      v-if="hint"
      role="tooltip"
      class="pointer-events-none absolute z-10 -translate-x-1/2 -translate-y-full rounded-md bg-foreground px-2 py-1 text-xs whitespace-nowrap text-background shadow-md"
      :style="{ left: `${hint.left}px`, top: `${hint.top - 6}px` }"
    >
      {{ hint.label }}
    </div>
    <div class="mt-3 flex flex-wrap items-center justify-between gap-x-4 gap-y-2 text-xs text-muted-foreground">
      <span class="tabular-nums">
        <template v-if="props.status === 'failed'">{{ totals.failed }} failed</template>
        <template v-else-if="props.status === 'succeeded'">{{ totals.succeeded }} succeeded</template>
        <template v-else>{{ totals.succeeded + totals.failed }} finished · {{ totals.succeeded }} succeeded · {{ totals.failed }} failed</template>
        <template v-if="props.isLive"> · <span class="text-primary">a rollout runs today</span></template>
      </span>
      <span class="flex flex-wrap items-center gap-3" aria-label="Legend">
        <span class="flex items-center gap-1"><span class="size-3 rounded-[3px] bg-success" aria-hidden="true" />succeeded</span>
        <span class="flex items-center gap-1">
          <span class="flex size-3 items-center justify-center rounded-[3px] bg-destructive" aria-hidden="true"
            ><span class="size-1 rounded-full bg-destructive-foreground/90"
          /></span>
          failed
        </span>
        <span class="flex items-center gap-1">
          <span class="flex size-3 items-center justify-center rounded-[3px] bg-warning" aria-hidden="true"
            ><span class="h-0.5 w-1.5 rounded-full bg-warning-foreground/90"
          /></span>
          mixed
        </span>
        <span class="flex items-center gap-1" aria-hidden="true">
          less
          <span v-for="step in 4" :key="step" :class="['size-3 rounded-[3px]', fills[props.status === 'failed' ? 'danger' : 'success'][step]]" />
          more
        </span>
      </span>
    </div>
  </div>
</template>

<style scoped>
/* Today's heartbeat while a rollout runs: a ring that swells and fades. Under reduced motion it holds still and
   doubles the ring instead, so the state still shows. An outline rather than a scaled border: an outline never widens
   the scroller, so the newest week can sit at the panel's edge without a sideways scrollbar. */
.wh-heartbeat {
  position: absolute;
  inset: 0;
  border-radius: 3px;
  outline: 2px solid var(--color-primary);
  outline-offset: 1px;
  pointer-events: none;
}

@media (prefers-reduced-motion: no-preference) {
  .wh-heartbeat {
    animation: wh-heartbeat 1.6s ease-out infinite;
  }
}

@media (prefers-reduced-motion: reduce) {
  .wh-heartbeat {
    outline-style: dashed;
    outline-offset: 3px;
  }
}

@keyframes wh-heartbeat {
  0% {
    opacity: 0.9;
    outline-offset: 1px;
  }
  70% {
    opacity: 0;
    outline-offset: 9px;
  }
  100% {
    opacity: 0;
    outline-offset: 9px;
  }
}
</style>
