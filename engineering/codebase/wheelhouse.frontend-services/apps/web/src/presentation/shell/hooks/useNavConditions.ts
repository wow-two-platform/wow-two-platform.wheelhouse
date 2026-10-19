import { computed } from "vue";
import { usePortfolioSummary } from "@/application/portfolio/usePortfolioSummary";
import { rolloutConditionOf, type ProductCondition } from "@/domain/portfolio";

/**
 * Each product's condition as the navigation marks it, from the portfolio summary's latest rollouts alone, so the
 * always-visible sidebar never asks the hosts for vitals.
 */
export function useNavConditions() {
  const summary = usePortfolioSummary();
  return computed(
    () =>
      new Map<string, ProductCondition>(
        (summary.data.value?.products ?? []).map((item) => [item.product, rolloutConditionOf(item)]),
      ),
  );
}
