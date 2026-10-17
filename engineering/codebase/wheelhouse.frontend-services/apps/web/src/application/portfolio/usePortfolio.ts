import { computed } from "vue";
import { useProducts } from "@/application/products";
import { useShowInactive } from "@/application/preferences";
import { useWorkspaces } from "@/application/workspaces";
import { groupPortfolio } from "./Portfolio";

/**
 * The portfolio grouped by workspace, following the one "Show inactive" switch. A workspaces read that fails leaves
 * every product in the group without a workspace, so the products still show.
 */
export function usePortfolio() {
  const products = useProducts();
  const workspaces = useWorkspaces();
  const showInactive = useShowInactive();
  const groups = computed(() =>
    groupPortfolio(workspaces.workspaces.value, products.products.value, showInactive.value),
  );
  return {
    products,
    workspaces,
    showInactive,
    groups,
    /** True until both reads first answer: the navigation draws placeholder rows. */
    isFirstLoad: computed(() => products.isFirstLoad.value || workspaces.isFirstLoad.value),
    /** Whether the products have arrived; workspaces alone draw nothing. */
    hasData: products.hasData,
    error: computed(() => products.error.value),
  };
}
