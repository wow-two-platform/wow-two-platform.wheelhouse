import { computed } from "vue";
import { usePortfolio } from "@/application/portfolio";
import { useLastEnvironment } from "@/application/preferences";
import { resolvePins } from "./PinnedEntries";
import { usePins } from "./usePins";

/** The pins resolved against the inventory, with the pin operations; what the sidebar and home page show. */
export function usePinned() {
  const pins = usePins();
  const portfolio = usePortfolio();
  const environment = useLastEnvironment();
  const entries = computed(() =>
    resolvePins(
      pins.pins.value,
      portfolio.workspaces.workspaces.value,
      portfolio.products.products.value,
      environment.value,
    ),
  );
  return {
    ...pins,
    /** Every pin in custom order, including ones whose target is gone, for editing. */
    entries,
    /** The pins that still open something, in custom order. */
    visible: computed(() => entries.value.filter((entry) => entry.location !== null)),
    isFirstLoad: computed(() => pins.isFirstLoad.value || portfolio.isFirstLoad.value),
  };
}
