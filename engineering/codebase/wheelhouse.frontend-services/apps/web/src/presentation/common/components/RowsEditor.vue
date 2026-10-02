<script lang="ts">
/** One column of a rows editor. */
export interface RowsEditorColumn {
  readonly key: string;
  readonly label: string;
  readonly placeholder?: string;
  /** The value a new row starts with; a number keeps the column numeric. */
  readonly initial?: string | number;
}

/** Edits a short list of records, one row each, such as a target's settings files. */
export interface RowsEditorProps {
  readonly label: string;
  readonly columns: readonly RowsEditorColumn[];
  readonly addLabel?: string;
}
</script>
<script setup lang="ts">
import { computed } from "vue";
import { Plus, X } from "lucide-vue-next";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { TextInput } from "@wow-two-beta/ui-vue/presentation/forms";

/** Renders a labelled list of rows with an input per column, a remove control per row and one add control. */
defineOptions({ name: "RowsEditor" });
const props = withDefaults(defineProps<RowsEditorProps>(), { addLabel: "Add" });
const rows = defineModel<Record<string, string | number>[]>({ required: true });
defineSlots<{}>();

const grid = computed(() => ({
  gridTemplateColumns: `repeat(${props.columns.length}, minmax(0, 1fr)) auto`,
}));

/** Appends a row holding each column's initial value. */
function add(): void {
  rows.value = [...rows.value, Object.fromEntries(props.columns.map((column) => [column.key, column.initial ?? ""]))];
}

/** Drops one row. */
function remove(index: number): void {
  rows.value = rows.value.filter((_, position) => position !== index);
}

/** Replaces one cell, keeping a numeric column numeric. */
function set(index: number, column: RowsEditorColumn, value: string | number | undefined): void {
  const next = typeof column.initial === "number" ? Number(value ?? 0) : String(value ?? "");
  rows.value = rows.value.map((row, position) => (position === index ? { ...row, [column.key]: next } : row));
}
</script>
<template>
  <fieldset class="flex flex-col gap-2">
    <legend class="mb-1 text-sm font-medium">{{ props.label }}</legend>
    <div v-for="(row, index) in rows" :key="index" class="grid items-center gap-2" :style="grid">
      <TextInput
        v-for="column in props.columns"
        :key="column.key"
        :model-value="row[column.key] ?? ''"
        :aria-label="`${column.label} ${index + 1}`"
        :placeholder="column.placeholder ?? column.label"
        autocomplete="off"
        @update:model-value="set(index, column, $event)"
      />
      <Button type="button" variant="ghost" tone="neutral" size="sm" :aria-label="`Remove row ${index + 1}`" @click="remove(index)">
        <X :size="14" />
      </Button>
    </div>
    <Button type="button" variant="outline" tone="neutral" size="sm" class="self-start" @click="add">
      <template #leading><Plus :size="14" /></template>{{ props.addLabel }}
    </Button>
  </fieldset>
</template>
