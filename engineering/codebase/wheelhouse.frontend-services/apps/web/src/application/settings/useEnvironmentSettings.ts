import { toValue, type MaybeRefOrGetter } from "vue";
import { useAppMutation, useAppQuery } from "@/bootstrap/query";
import type { SettingKeyRequest } from "@/domain/settings";
import { deploySettingsApi } from "@/integration/deployments/settingsApi";
import { vaultApi } from "@/integration/secrets/vaultApi";
import { SettingsKeys } from "./SettingsKeys";

/** The editors' plan of a target: the release it runs, its keys, values of plain settings and secret metadata. */
export function useEnvironmentSettings(target: MaybeRefOrGetter<string | null>) {
  return useAppQuery({
    key: () => SettingsKeys.running(toValue(target) ?? ""),
    queryFn: ({ signal }) => deploySettingsApi.get(toValue(target) ?? "", null, signal),
    enabled: () => Boolean(toValue(target)),
    meta: { suppressGlobalError: true },
  });
}

/** Applies a target's edited values now or at a set time; its plans read again. */
export function useApplySettings() {
  return useAppMutation({
    mutationFn: ({ target, at, reason, confirm }: { target: string; at?: string; reason?: string; confirm?: string }, { signal }) =>
      deploySettingsApi.apply(
        target,
        { ...(at ? { at } : {}), ...(reason ? { reason } : {}), ...(confirm ? { confirm } : {}) },
        signal,
      ),
    invalidates: ({ target }) => [SettingsKeys.of(target), ["deployments", "history"]],
    meta: { suppressGlobalError: true },
  });
}

/** Drops a target's scheduled apply. */
export function useCancelApply() {
  return useAppMutation({
    mutationFn: (target: string, { signal }) => deploySettingsApi.cancelApply(target, signal),
    invalidates: (target) => [SettingsKeys.of(target)],
    meta: { suppressGlobalError: true },
  });
}

/** A secret's versions, read while its history is open. */
export function useSecretHistory(target: MaybeRefOrGetter<string | null>, key: MaybeRefOrGetter<string | null>) {
  return useAppQuery({
    key: () => SettingsKeys.history(toValue(target) ?? "", toValue(key) ?? ""),
    queryFn: ({ signal }) => deploySettingsApi.versions(toValue(target) ?? "", toValue(key) ?? "", signal),
    enabled: () => Boolean(toValue(target) && toValue(key)),
    meta: { suppressGlobalError: true },
  });
}

/** Restores an earlier version of a secret as the newest. */
export function useRestoreSecret() {
  return useAppMutation({
    mutationFn: ({ target, key, version, note }: { target: string; key: string; version: number; note: string | null }, { signal }) =>
      deploySettingsApi.restore(target, key, version, note, signal),
    invalidates: ({ target }) => [SettingsKeys.of(target), SettingsKeys.vault],
    meta: { suppressGlobalError: true },
  });
}

/** Sets or clears a secret's expiry. */
export function useSecretExpiry() {
  return useAppMutation({
    mutationFn: ({ target, key, expiresAt }: { target: string; key: string; expiresAt: string | null }, { signal }) =>
      deploySettingsApi.expiry(target, key, expiresAt, signal),
    invalidates: ({ target }) => [SettingsKeys.of(target), SettingsKeys.vault],
    meta: { suppressGlobalError: true },
  });
}

/** Adds a key to a product, or removes one the owner added; every plan reads again. */
export function useSettingKeys() {
  const set = useAppMutation({
    mutationFn: ({ product, key, body }: { product: string; key: string; body: SettingKeyRequest }, { signal }) =>
      deploySettingsApi.setKey(product, key, body, signal),
    invalidates: () => [["deployments", "settings"]],
    meta: { suppressGlobalError: true },
  });
  const remove = useAppMutation({
    mutationFn: ({ product, key }: { product: string; key: string }, { signal }) => deploySettingsApi.removeKey(product, key, signal),
    invalidates: () => [["deployments", "settings"]],
    meta: { suppressGlobalError: true },
  });
  return { set, remove };
}

/** The built-in vault's state: sealed or not, the key file, what it holds. */
export function useBuiltInVault() {
  return useAppQuery({
    key: () => SettingsKeys.vaultStatus,
    queryFn: ({ signal }) => vaultApi.status(signal),
    meta: { suppressGlobalError: true },
  });
}

/** What the built-in vault needs the owner for, read again every minute while shown. */
export function useBuiltInVaultIssues() {
  return useAppQuery({
    key: () => SettingsKeys.vaultIssues,
    queryFn: ({ signal }) => vaultApi.issues(signal),
    refetchInterval: 60_000,
    meta: { suppressGlobalError: true },
  });
}

/** The full decryption check and the import from a standalone vault. */
export function useVaultOperations() {
  const verify = useAppMutation({
    mutationFn: (_: void, { signal }) => vaultApi.verify(signal),
    invalidates: () => [SettingsKeys.vault],
    meta: { suppressGlobalError: true },
  });
  const importFrom = useAppMutation({
    mutationFn: (vault: string, { signal }) => vaultApi.importFrom(vault, signal),
    invalidates: () => [SettingsKeys.vault, ["deployments", "settings"]],
    meta: { suppressGlobalError: true },
  });
  return { verify, importFrom };
}
