import { computed } from "vue";
import { useAppMutation, useAppQuery, useOptimisticMutation } from "@/bootstrap/query";
import { ProductStatusRules, type Product, type ProductStatus, type SaveProductRequest } from "@/domain/products";
import { productsApi } from "@/integration/products";
import { DeploymentKeys } from "@/application/deployments/DeploymentKeys";

export { useProductIcon, type ProductIconSource } from "./useProductIcon";
export { IconAvailability } from "./IconAvailability";

/** Query keys for products; inventory writes invalidate through these. */
export const ProductKeys = { list: ["products"] as const };

/** Applies a change to one product in the cached list, for an optimistic write; an unread list stays unread. @internal */
function patchProduct(
  current: Product[] | undefined,
  reference: string,
  change: (product: Product) => Product,
): Product[] {
  const next = current?.map((product) => (ProductStatusRules.isProduct(product, reference) ? change(product) : product));
  return next as Product[];
}

/** The portfolio's products, with the operator's confirmed edits and optimistic status and workspace changes. */
export function useProducts() {
  const list = useAppQuery({
    key: ProductKeys.list,
    queryFn: ({ signal }) => productsApi.listProducts(signal),
  });
  const invalidates = () => [ProductKeys.list, DeploymentKeys.targets, DeploymentKeys.releases];
  const status = useOptimisticMutation({
    mutationFn: ({ product, value }: { product: string; value: ProductStatus }, { signal }) =>
      productsApi.updateStatus(product, value, signal),
    targets: [
      {
        key: ProductKeys.list,
        apply: (current: Product[] | undefined, { product, value }: { product: string; value: ProductStatus }) =>
          patchProduct(current, product, (item) => ProductStatusRules.withStatus(item, value)),
      },
    ],
  });
  const serviceStatus = useOptimisticMutation({
    mutationFn: (
      { product, service, value }: { product: string; service: string; value: ProductStatus },
      { signal },
    ) => productsApi.updateServiceStatus(product, service, value, signal),
    targets: [
      {
        key: ProductKeys.list,
        apply: (
          current: Product[] | undefined,
          { product, service, value }: { product: string; service: string; value: ProductStatus },
        ) => patchProduct(current, product, (item) => ProductStatusRules.withServiceStatus(item, service, value)),
      },
    ],
  });
  const workspace = useOptimisticMutation({
    mutationFn: ({ product, workspaceId }: { product: string; workspaceId: string }, { signal }) =>
      productsApi.moveToWorkspace(product, workspaceId, signal),
    targets: [
      {
        key: ProductKeys.list,
        apply: (current: Product[] | undefined, { product, workspaceId }: { product: string; workspaceId: string }) =>
          patchProduct(current, product, (item) => ProductStatusRules.withWorkspace(item, workspaceId)),
      },
    ],
  });
  const refreshIdentity = useAppMutation({
    mutationFn: (slug: string, { signal }) => productsApi.refreshIdentity(slug, signal),
    invalidates: () => [ProductKeys.list],
    meta: { suppressGlobalError: true },
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
    /** True until the first catalog arrives: the catalog renders placeholder rows. */
    isFirstLoad: list.isFirstLoad,
    /** Whether a catalog has arrived, so a later failure keeps it on screen. */
    hasData: computed(() => list.data.value !== undefined),
    error,
    reload: async () => {
      await list.refetch();
    },
    /** Marks a product active or inactive; the list changes at once and rolls back if the host refuses. */
    setStatus: (product: Product, value: ProductStatus) =>
      status.mutateAsync({ product: ProductStatusRules.referenceOf(product), value }),
    /** Marks one service of a product active or inactive, optimistically. */
    setServiceStatus: (product: Product, service: string, value: ProductStatus) =>
      serviceStatus.mutateAsync({ product: ProductStatusRules.referenceOf(product), service, value }),
    /** Moves a product into another workspace, optimistically. */
    moveToWorkspace: (product: Product, workspaceId: string) =>
      workspace.mutateAsync({ product: ProductStatusRules.referenceOf(product), workspaceId }),
    refreshIdentity: (slug: string) => refreshIdentity.mutateAsync(slug),
    create: (body: SaveProductRequest) => create.mutateAsync(body),
    update: (slug: string, body: SaveProductRequest) => update.mutateAsync({ slug, body }),
    remove: (slug: string) => remove.mutateAsync(slug),
  };
}

/** Operations the product form receives. */
export type ProductOperations = ReturnType<typeof useProducts>;
