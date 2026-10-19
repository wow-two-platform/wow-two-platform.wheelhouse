import type { DeploySettings, DeploySettingValue } from '@/domain/deployments';
import type { SavedSetting, SecretHistory, SettingKeyRequest, SettingsApply } from '@/domain/settings';
import { requestData, requestEmpty } from '@/integration/common';
import {
  AddedKeySchema,
  DeploySettingsSavedSchema,
  DeploySettingsSchema,
  SecretHistorySchema,
  SecretWrittenSchema,
  SettingsApplySchema,
} from './settingsSchemas';

const segment = encodeURIComponent;
const base = (target: string) => `/api/deployments/targets/${segment(target)}`;

/** A target's settings and secrets: the plan, saves, applies and each secret's history; values only ever travel in. */
export const deploySettingsApi = {
  /** Where the next deploy's render takes each setting from: key names, non-secret values and secret metadata. Without
   *  a release, the plan reads the one the target runs. */
  get: (target: string, release: string | null, signal?: AbortSignal) =>
    requestData<DeploySettings>(
      `${base(target)}/settings${release ? `?release=${segment(release)}` : ''}`,
      DeploySettingsSchema,
      { signal },
    ),

  /** Saves values in one transaction: secrets become new vault versions, settings go to the store; nothing is read
   *  back. One value is one field's save. */
  save: (target: string, values: readonly DeploySettingValue[], release: string | null, signal?: AbortSignal) =>
    requestData<{ settings: number; secrets: number; keys: SavedSetting[] }>(`${base(target)}/settings`, DeploySettingsSavedSchema, {
      signal,
      method: 'PUT',
      action: 'settings',
      body: { values, ...(release ? { release } : {}) },
    }),

  /** Applies the environment's edited values: now, or at a set time. */
  apply: (target: string, body: { at?: string; reason?: string; confirm?: string }, signal?: AbortSignal) =>
    requestData<SettingsApply>(`${base(target)}/settings/apply`, SettingsApplySchema, {
      signal,
      method: 'POST',
      action: 'settings',
      body,
    }),

  /** Drops the target's scheduled apply. */
  cancelApply: (target: string, signal?: AbortSignal) =>
    requestEmpty(`${base(target)}/settings/apply`, { signal, method: 'DELETE', action: 'settings' }),

  /** A secret's versions, never a value. */
  versions: (target: string, key: string, signal?: AbortSignal) =>
    requestData<SecretHistory>(`${base(target)}/secrets/${segment(key)}/versions`, SecretHistorySchema, { signal }),

  /** Restores an earlier version as the newest. */
  restore: (target: string, key: string, version: number, note: string | null, signal?: AbortSignal) =>
    requestData(`${base(target)}/secrets/${segment(key)}/restore`, SecretWrittenSchema, {
      signal,
      method: 'POST',
      action: 'settings',
      body: { version, ...(note ? { note } : {}) },
    }),

  /** Sets or clears a secret's expiry. */
  expiry: (target: string, key: string, expiresAt: string | null, signal?: AbortSignal) =>
    requestEmpty(`${base(target)}/secrets/${segment(key)}/expiry`, {
      signal,
      method: 'PUT',
      action: 'settings',
      body: { expiresAt },
    }),

  /** Adds a key to a product, or changes one the owner added. */
  setKey: (product: string, key: string, body: SettingKeyRequest, signal?: AbortSignal) =>
    requestData(`/api/products/${segment(product)}/setting-keys/${segment(key)}`, AddedKeySchema, {
      signal,
      method: 'PUT',
      action: 'settings',
      body,
    }),

  /** Removes a key the owner added. */
  removeKey: (product: string, key: string, signal?: AbortSignal) =>
    requestEmpty(`/api/products/${segment(product)}/setting-keys/${segment(key)}`, { signal, method: 'DELETE', action: 'settings' }),
};
