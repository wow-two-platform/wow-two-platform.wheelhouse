import { computed, toValue, type MaybeRefOrGetter } from "vue";
import { useAppMutation, useAppQuery, useOptimisticMutation } from "@/bootstrap/query";
import { ProductStatusRules, type Product, type ProductStatus, type SaveProductRequest } from "@/domain/products";
import { productsApi } from "@/integration/products";
import { DeploymentKeys } from "@/application/deployments/DeploymentKeys";
import { AutomationKeys } from "@/application/deployments/hooks/useDeploymentAutomation";

export { useProductIcon, type ProductIconSource } from "./useProductIcon";
export { IconAvailability } from "./IconAvailability";

/** Query keys for products; inventory writes invalidate through these, and the list's key prefixes every other. */
export const ProductKeys = {
  list: ["products"] as const,
  descriptor: (product: string) => ["products", product, "descriptor"] as const,
};

/**
 * What a product's deploy.yml declares at its branch head: the services every environment runs, their needs and the
 * sites with their exposure. Hosting, the placement dialog and the environment form read it; a product write or a
 * refresh from the repository reads it again.
 */
export function useProductDescriptor(product: MaybeRefOrGetter<string | null>) {
  return useAppQuery({
    key: () => ProductKeys.descriptor(toValue(product) ?? ""),
    queryFn: ({ signal }) => productsApi.getDescriptor(toValue(product) ?? "", signal),
    enabled: () => Boolean(toValue(product)),
    meta: { suppressGlobalError: true },
  });
}

/** Applies a change to one product in the cached list, for an optimistic write; an unread list stays unread. @internal */
function patchProduct(
  current: Product[] | undefined,
  reference: string,
  change: (product: Product) => Product,
): Product[] {
  const next = current?.map((product) => (ProductStatusRules.isProduct(product, reference) ? change(product) : product));
  return next as Product[];
}

/** The portfolio's products, with the owner's confirmed edits and optimistic status and workspace changes. */
export function useProducts() {
  const list = useAppQuery({
    key: ProductKeys.list,
    queryFn: ({ signal }) => productsApi.listProducts(signal),
  });
  // A product's deploy mode decides its automatic choices and whether Ready to promote offers it.
  const invalidates = () => [
    ProductKeys.list,
    DeploymentKeys.targets,
    DeploymentKeys.releases,
    AutomationKeys.all,
    DeploymentKeys.historyViews,
  ];
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
  // A refresh also brings the product's approved services up to its repositories' deploy.yml, so the release sets
  // and the settings each release takes are read again too.
  const refreshIdentity = useAppMutation({
    mutationFn: (slug: string, { signal }) => productsApi.refreshIdentity(slug, signal),
    invalidates: () => [ProductKeys.list, DeploymentKeys.releases, DeploymentKeys.settingsAll],
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
