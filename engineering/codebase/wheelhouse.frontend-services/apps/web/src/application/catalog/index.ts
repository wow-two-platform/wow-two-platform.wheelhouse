import { useAppMutation, useAppQuery } from "@/bootstrap/query";
import { catalogApi } from "@/integration/catalog";
import { ProductKeys } from "@/application/products";
import { WorkspaceKeys } from "@/application/workspaces";
import { DeploymentKeys } from "@/application/deployments/DeploymentKeys";

/** Query keys for the merged hub catalog. */
export const CatalogKeys = { catalog: ["catalog"] as const };

/** The merged hub catalog and its import, which reloads the products and workspaces it changed. */
export function useCatalog() {
  const catalog = useAppQuery({
    key: CatalogKeys.catalog,
    queryFn: ({ signal }) => catalogApi.getCatalog(signal),
  });
  const importEntries = useAppMutation({
    mutationFn: (slugs: readonly string[], { signal }) => catalogApi.importEntries(slugs, signal),
    invalidates: () => [CatalogKeys.catalog, ProductKeys.list, WorkspaceKeys.list, DeploymentKeys.targets],
    meta: { suppressGlobalError: true },
  });
  return {
    catalog,
    importing: importEntries.loading,
    importEntries: (slugs: readonly string[]) => importEntries.mutateAsync(slugs),
  };
}
