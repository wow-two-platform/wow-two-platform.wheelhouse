import { computed } from "vue";
import { useAppQuery, useOptimisticMutation } from "@/bootstrap/query";
import { Ordering } from "@/domain/common";
import { PinRules, type Pin, type PinSubject } from "@/domain/pins";
import { pinsApi } from "@/integration/pins";

/** Query keys for pins. */
export const PinKeys = { list: ["pins"] as const };

/** The owner's pins in custom order; pinning, unpinning and reordering show at once and roll back on refusal. */
export function usePins() {
  const list = useAppQuery({
    key: PinKeys.list,
    queryFn: ({ signal }) => pinsApi.listPins(signal),
  });
  const add = useOptimisticMutation({
    mutationFn: (subject: PinSubject, { signal }) => pinsApi.createPin(PinRules.requestOf(subject), signal),
    targets: [{ key: PinKeys.list, apply: (current: Pin[] | undefined, subject: PinSubject) => PinRules.withPin(current, subject) }],
  });
  const remove = useOptimisticMutation({
    mutationFn: (id: string, { signal }) => pinsApi.deletePin(id, signal),
    targets: [{ key: PinKeys.list, apply: (current: Pin[] | undefined, id: string) => PinRules.withoutPin(current, id) }],
  });
  const reorder = useOptimisticMutation({
    mutationFn: (ids: readonly string[], { signal }) => pinsApi.reorderPins(ids, signal),
    targets: [
      {
        key: PinKeys.list,
        apply: (current: Pin[] | undefined, ids: readonly string[]) =>
          (current ? Ordering.apply(current, ids) : current) as Pin[],
      },
    ],
  });
  const pins = computed(() => Ordering.sort(list.data.value ?? []));

  /** The pin for a subject, if it is pinned. */
  function find(subject: PinSubject): Pin | undefined {
    return PinRules.find(pins.value, subject);
  }

  return {
    pins,
    isFirstLoad: list.isFirstLoad,
    hasData: computed(() => list.data.value !== undefined),
    error: computed(() => list.error.value?.message ?? null),
    find,
    isPinned: (subject: PinSubject) => Boolean(find(subject)),
    /** Pins the subject, or unpins it when it is pinned. */
    toggle: (subject: PinSubject) => {
      const pin = find(subject);
      return pin ? remove.mutateAsync(pin.id) : add.mutateAsync(subject);
    },
    unpin: (id: string) => remove.mutateAsync(id),
    /** Moves one pin; the order saves as one list. */
    move: (from: number, to: number) => reorder.mutateAsync(PinRules.reorder(pins.value, from, to)),
  };
}

/** The pin operations a pin toggle receives. */
export type PinOperations = ReturnType<typeof usePins>;
