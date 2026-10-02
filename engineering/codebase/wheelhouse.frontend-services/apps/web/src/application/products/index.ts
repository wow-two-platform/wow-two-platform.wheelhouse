import { computed } from "vue";
import { useAppMutation, useAppQuery } from "@/bootstrap/query";
import type { ProductLifecycle, SaveProductRequest } from "@/domain/products";
import { productsApi } from "@/integration/products";
import { DeploymentKeys } from "@/application/deployments/DeploymentKeys";

/** Query keys for products; inventory writes invalidate through these. */
export const ProductKeys = { list: ["products"] as const };

/** The portfolio's products, with the operator's confirmed edits and lifecycle changes. */
export function useProducts() {
  const list = useAppQuery({
    key: ProductKeys.list,
    queryFn: ({ signal }) => productsApi.listProducts(signal),
  });
  const invalidates = () => [ProductKeys.list, DeploymentKeys.targets, DeploymentKeys.releases];
  const lifecycle = useAppMutation({
    mutationFn: (
      { slug, value }: { slug: string; value: ProductLifecycle },
      { signal },
    ) => productsApi.updateLifecycle(slug, value, signal),
    invalidates: () => [ProductKeys.list],
  });
  const create = useAppMutation({
    mutationFn: (body: SaveProductRequest, { signal }) => productsApi.createProduct(body, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });
  const update = useAppMutation({
    mutationFn: ({ slug, body }: { slug: string; body: SaveProductRequest }, { signal }) =>
      productsApi.updateProduct(slug, body, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });
  const remove = useAppMutation({
    mutationFn: (slug: string, { signal }) => productsApi.deleteProduct(slug, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });
  const products = computed(() => list.data.value ?? []);
  const error = computed(() => list.error.value?.message ?? null);

  return {
    products,
    loading: list.loading,
    error,
    reload: async () => {
      await list.refetch();
    },
    setLifecycle: (slug: string, value: ProductLifecycle) =>
      lifecycle.mutateAsync({ slug, value }),
    create: (body: SaveProductRequest) => create.mutateAsync(body),
    update: (slug: string, body: SaveProductRequest) => update.mutateAsync({ slug, body }),
    remove: (slug: string) => remove.mutateAsync(slug),
  };
}

/** Operations the product form receives. */
export type ProductOperations = ReturnType<typeof useProducts>;
