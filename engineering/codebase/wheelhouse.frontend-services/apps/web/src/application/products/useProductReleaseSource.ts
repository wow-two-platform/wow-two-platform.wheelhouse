import { computed, toValue, type MaybeRefOrGetter } from "vue";
import { useDebouncedValue } from "@wow-two-beta/fe/foundation/async";
import { useAppQuery, useRefresh } from "@/bootstrap/query";
import { productsApi, type ReleaseSourceQuery } from "@/integration/products";

/** Query keys for the release source the host derives; apart from the products list, so a product write keeps it. */
export const ReleaseSourceKeys = {
  of: (query: ReleaseSourceQuery | null) =>
    ["release-source", query?.slug ?? "", query?.repository ?? "", query?.branch ?? ""] as const,
};

/**
 * The release source the host derives for the product form: the shared publish workflow's asset and build workflow,
 * and the images the repository's `deploy.yml` names at the branch head. A query settles a moment after the owner
 * stops typing; `refresh` asks GitHub for the branch head now, so a push of a minute ago shows.
 */
export function useProductReleaseSource(query: MaybeRefOrGetter<ReleaseSourceQuery | null>) {
  const settled = useDebouncedValue(() => toValue(query), 400);
  /** Set for the one read a refresh starts. @internal */
  let fresh = false;
  const source = useAppQuery({
    key: () => ReleaseSourceKeys.of(settled.value),
    queryFn: ({ signal }) => {
      const refresh = fresh;
      fresh = false;
      return productsApi.getReleaseSource({ ...settled.value!, refresh }, signal);
    },
    enabled: () => settled.value !== null,
    meta: { suppressGlobalError: true },
  });
  const { refresh, refreshing } = useRefresh(() => {
    fresh = true;
    return source.refetch();
  });
  /** True while the form's query differs from the one read, so the shown values belong to an earlier repository. */
  const isSettling = computed(() => {
    const now = toValue(query);
    const read = settled.value;
    return now?.slug !== read?.slug || now?.repository !== read?.repository || now?.branch !== read?.branch;
  });
  return { ...source, refresh, refreshing, isSettling };
}
