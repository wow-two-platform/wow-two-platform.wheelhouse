import { toValue, type MaybeRefOrGetter } from 'vue';

import { useAppMutation, useAppQuery } from '@/bootstrap/query';
import type { DeploySettingValue } from '@/domain/deployments';
import { deploySettingsApi } from '@/integration/deployments/settingsApi';

import { DeploymentKeys } from '../DeploymentKeys';

/** The settings a release takes on a target, read when both are chosen; a failure shows in place. */
export function useDeploySettings(target: MaybeRefOrGetter<string | null>, release: MaybeRefOrGetter<string | null>) {
  return useAppQuery({
    key: () => DeploymentKeys.settings(toValue(target) ?? '', toValue(release) ?? ''),
    queryFn: ({ signal }) => deploySettingsApi.get(toValue(target) ?? '', toValue(release) ?? '', signal),
    enabled: () => Boolean(toValue(target) && toValue(release)),
    meta: { suppressGlobalError: true },
  });
}

/** Saves an environment's values; the target's settings plans read again. */
export function useSaveDeploySettings() {
  return useAppMutation({
    mutationFn: ({ target, values }: { target: string; values: readonly DeploySettingValue[] }, { signal }) =>
      deploySettingsApi.save(target, values, signal),
    invalidates: ({ target }) => [DeploymentKeys.settingsOf(target)],
    meta: { suppressGlobalError: true },
  });
}
