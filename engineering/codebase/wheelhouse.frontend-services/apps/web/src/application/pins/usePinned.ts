import { computed } from "vue";
import { usePortfolio } from "@/application/portfolio/usePortfolio";
import { resolvePins } from "./PinnedEntries";
import { usePins } from "./usePins";

/** The pinned products resolved against the inventory, with the pin operations; what the sidebar and home page show. */
export function usePinned() {
  const pins = usePins();
  const portfolio = usePortfolio();
  const entries = computed(() => resolvePins(pins.pins.value, portfolio.products.products.value));
  return {
    ...pins,
    /** Every product pin in custom order, including ones whose product is gone, for editing. */
    entries,
    /** The pins that still open something, in custom order. */
    visible: computed(() => entries.value.filter((entry) => entry.location !== null)),
    isFirstLoad: computed(() => pins.isFirstLoad.value || portfolio.isFirstLoad.value),
  };
}
