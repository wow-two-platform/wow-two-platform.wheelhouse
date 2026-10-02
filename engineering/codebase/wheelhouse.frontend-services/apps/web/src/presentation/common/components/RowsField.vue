<script lang="ts">
import type { AppForm } from "@wow-two-beta/ui-vue/forms-engine";

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
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { Field, NumberInput, TextInput } from "@wow-two-beta/ui-vue/presentation/forms";
import { useFieldArray } from "@wow-two-beta/ui-vue/forms-engine";

/** Renders column headings, a row of inputs per record with its remove control, the list's own errors and one add
 * control. Cells and the list take the host's refusals at their own paths. */
defineOptions({ name: "RowsField" });
const props = defineProps<RowsFieldProps<TValues>>();
defineSlots<{}>();

const FormField = props.form.Field;
const rows = useFieldArray<Record<string, string | number>>(props.form, props.name);
const grid = computed(() => ({ gridTemplateColumns: `repeat(${props.columns.length}, minmax(0, 1fr)) auto` }));
</script>
<template>
  <FormField :name="props.name" v-slot="list">
    <fieldset class="flex flex-col gap-2">
      <legend class="mb-1 text-sm font-medium">{{ props.label }}</legend>
      <div v-if="rows.length" class="grid gap-2 text-xs text-muted-foreground" :style="grid" aria-hidden="true">
        <span v-for="column in props.columns" :key="column.key">{{ column.label }}</span>
      </div>
      <div v-for="row in rows.rows" :key="row.key" class="grid items-start gap-2" :style="grid">
        <rows.Field v-for="column in props.columns" :key="column.key" :index="row.index" :name="column.key" v-slot="cell">
          <Field>
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
          size="sm"
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
