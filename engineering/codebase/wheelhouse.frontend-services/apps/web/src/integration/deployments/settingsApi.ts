import type { DeploySettings, DeploySettingValue } from '@/domain/deployments';
import { requestData } from '@/integration/common';
import { DeploySettingsSavedSchema, DeploySettingsSchema } from './settingsSchemas';

/** A target's settings for one release, and the values the owner gives its environment. */
export const deploySettingsApi = {
  /** Where the next deploy's render takes each setting from; key names and non-secret values only. */
  get: (target: string, release: string, signal?: AbortSignal) =>
    requestData<DeploySettings>(
      `/api/deployments/targets/${encodeURIComponent(target)}/settings?release=${encodeURIComponent(release)}`,
      DeploySettingsSchema,
      { signal },
    ),

  /** Saves values: secrets go to the environment's vault, settings to the store; nothing is read back. */
  save: (target: string, values: readonly DeploySettingValue[], signal?: AbortSignal) =>
    requestData(`/api/deployments/targets/${encodeURIComponent(target)}/settings`, DeploySettingsSavedSchema, {
      signal,
      method: 'PUT',
      action: 'settings',
      body: { values },
    }),
};
