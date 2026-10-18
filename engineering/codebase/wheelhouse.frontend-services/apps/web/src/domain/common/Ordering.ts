/** An item the owner sorts by hand: its identifier and its place in the custom order. */
export interface OrderedItem {
  readonly id: string;
  readonly order: number;
}

/** Pure rules for lists the owner orders by hand: workspaces and pins. */
export const Ordering = {
  /** The items in their custom order; equal places keep the order they arrived in. */
  sort: <T extends OrderedItem>(items: readonly T[]): T[] =>
    items
      .map((item, index) => ({ item, index }))
      .sort((left, right) => left.item.order - right.item.order || left.index - right.index)
      .map(({ item }) => item),

  /** Moves the item at `from` to `to`; an index outside the list, or no move at all, returns an unchanged copy. */
  move: <T>(items: readonly T[], from: number, to: number): T[] => {
    const next = [...items];
    if (!Number.isInteger(from) || !Number.isInteger(to) || from === to) return next;
    if (from < 0 || to < 0 || from >= next.length || to >= next.length) return next;
    const [moved] = next.splice(from, 1);
    next.splice(to, 0, moved as T);
    return next;
  },

  /** Numbers the items' places from zero in the sequence given, the shape a reorder request leaves behind. */
  renumber: <T extends OrderedItem>(items: readonly T[]): T[] =>
    items.map((item, order) => (item.order === order ? item : { ...item, order })),

  /**
   * Applies an order the owner saved: the listed ids first, in their sequence, then any item the list missed in
   * its previous place. Ids that name no item are ignored.
   */
  apply: <T extends OrderedItem>(items: readonly T[], ids: readonly string[]): T[] => {
    const byId = new Map(items.map((item) => [item.id, item]));
    const listed = ids.flatMap((id) => {
      const item = byId.get(id);
      if (!item) return [];
      byId.delete(id);
      return [item];
    });
    return Ordering.renumber([...listed, ...Ordering.sort([...byId.values()])]);
  },

  /** The ids in custom order, the body a reorder request sends. */
  ids: (items: readonly OrderedItem[]): string[] => Ordering.sort(items).map((item) => item.id),
} as const;
