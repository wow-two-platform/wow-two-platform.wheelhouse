<script lang="ts">
import type { AppForm } from "@wow-two-beta/fe/forms-engine";

/** One column of a rows field. */
export interface RowsFieldColumn {
  readonly key: string;
  readonly label: string;
  readonly placeholder?: string;
  /** Binds a number input instead of text. */
  readonly isNumeric?: boolean;
}

/** Edits one list of a form's records, a row each, such as an environment's settings files. */
export interface RowsFieldProps<TValues extends object> {
  readonly form: AppForm<TValues>;
  /** The list's path in the form. */
  readonly name: string;
  readonly label: string;
  readonly addLabel: string;
  readonly columns: readonly RowsFieldColumn[];
  /** The row a new line starts from. */
  readonly blank: Readonly<Record<string, string | number>>;
}
</script>
<script setup lang="ts" generic="TValues extends object">
import { computed } from "vue";
import { Plus, X } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Field, NumberInput, TextInput } from "@wow-two-beta/fe/presentation/forms";
import { useFieldArray } from "@wow-two-beta/fe/forms-engine";

/** Renders column headings, a row of inputs per record with its remove control, the list's own errors and one add
 * control. Cells and the list take the host's refusals at their own paths. Below the small breakpoint the headings
 * give way to a label over each cell, and each record stacks in its own bordered block, its remove control last. */
defineOptions({ name: "RowsField" });
const props = defineProps<RowsFieldProps<TValues>>();
defineSlots<{}>();

const FormField = props.form.Field;
const rows = useFieldArray<Record<string, string | number>>(props.form, props.name);
/** The columns from the small breakpoint up; below it each row stacks, one labelled cell per line. @internal */
const grid = computed(() => ({ "--rows-columns": `repeat(${props.columns.length}, minmax(0, 1fr)) auto` }));
</script>
<template>
  <FormField :name="props.name" v-slot="list">
    <fieldset class="flex flex-col gap-2">
      <legend class="mb-1 text-sm font-medium">{{ props.label }}</legend>
      <div
        v-if="rows.length"
        class="hidden gap-2 text-xs text-muted-foreground sm:grid sm:[grid-template-columns:var(--rows-columns)]"
        :style="grid"
        aria-hidden="true"
      >
        <span v-for="column in props.columns" :key="column.key">{{ column.label }}</span>
        <!-- Holds the remove control's column, so each heading sits over its cells. -->
        <span class="w-10" />
      </div>
      <div
        v-for="row in rows.rows"
        :key="row.key"
        class="flex min-w-0 flex-col gap-2 rounded-md border border-border p-3 sm:grid sm:items-start sm:rounded-none sm:border-0 sm:p-0 sm:[grid-template-columns:var(--rows-columns)]"
        :style="grid"
      >
        <rows.Field v-for="column in props.columns" :key="column.key" :index="row.index" :name="column.key" v-slot="cell">
          <Field class="min-w-0">
            <span class="text-xs text-muted-foreground sm:hidden" aria-hidden="true">{{ column.label }}</span>
            <NumberInput
              v-if="column.isNumeric"
              :model-value="Number(cell.value)"
              :aria-label="`${column.label} ${row.index + 1}`"
              @update:model-value="cell.setValue($event ?? 0)"
              @blur="cell.onBlur"
            />
            <TextInput
              v-else
              :model-value="String(cell.value ?? '')"
              :aria-label="`${column.label} ${row.index + 1}`"
              :placeholder="column.placeholder ?? column.label"
              autocomplete="off"
              @update:model-value="cell.setValue($event ?? '')"
              @blur="cell.onBlur"
            />
          </Field>
        </rows.Field>
        <Button
          type="button"
          variant="ghost"
          tone="neutral"
          size="md"
          shape="square"
          class="self-end sm:self-auto"
          :aria-label="`Remove row ${row.index + 1}`"
          @click="rows.remove(row.index)"
        >
          <X :size="14" />
        </Button>
      </div>
      <p v-for="message in list.errors" :key="message" role="alert" class="text-sm text-destructive">{{ message }}</p>
      <Button type="button" variant="outline" tone="neutral" size="sm" class="self-start" @click="rows.push({ ...props.blank })">
        <template #leading><Plus :size="14" /></template>{{ props.addLabel }}
      </Button>
    </fieldset>
  </FormField>
</template>
