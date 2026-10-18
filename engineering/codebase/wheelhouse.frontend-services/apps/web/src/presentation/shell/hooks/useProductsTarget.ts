import { computed } from "vue";
import { useRoute } from "vue-router";
import { productsDestination, type RememberedProduct } from "@/application/navigation";
import { usePortfolio } from "@/application/portfolio/usePortfolio";
import { useLastProduct } from "@/application/preferences";

/**
 * The product page last opened, while its product still exists, and where the primary Products destination leads
 * from the current page: back to that product from elsewhere, up to the grid from inside Products.
 */
export function useProductsTarget() {
  const route = useRoute();
  const last = useLastProduct();
  const portfolio = usePortfolio();
  const remembered = computed<RememberedProduct | null>(() => {
    const memory = last.value;
    if (!memory || !portfolio.products.hasData.value) return memory;
    return portfolio.products.products.value.some((product) => product.slug === memory.slug) ? memory : null;
  });
  return {
    remembered,
    /** The remembered product, resolved. */
    rememberedProduct: computed(
      () => portfolio.products.products.value.find((product) => product.slug === remembered.value?.slug) ?? null,
    ),
    target: computed(() => productsDestination(route.path, remembered.value)),
  };
}
