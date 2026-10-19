import { onScopeDispose, reactive, toValue, type MaybeRefOrGetter } from "vue";
import { ApiFailureFactory, type ApiFailure } from "@wow-two-beta/fe/foundation/http";
import type { Result } from "@wow-two-beta/fe/foundation/results";
import { useQueryCache } from "@/bootstrap/query";
import type { DeploySettingValue } from "@/domain/deployments";
import type { SavedSetting } from "@/domain/settings";
import { deploySettingsApi } from "@/integration/deployments/settingsApi";
import { failureReason } from "@/integration/common";
import { SettingsKeys } from "./SettingsKeys";

/** Where one field's save stands. */
export interface FieldSave {
  state: "saving" | "saved" | "failed";
  /** Why it failed, as the API says it; never the value. */
  message: string | null;
  /** A saved secret's new version. */
  version: number | null;
}

/**
 * Saves an environment's values one field at a time, as soon as each is confirmed, and keeps each field's state:
 * saving, saved or failed with the reason. Several values given together save in one transaction. Values go straight to
 * the API and are never kept in a cache; the target's plans read again after each save.
 */
export function useSettingSaver(target: MaybeRefOrGetter<string | null>, release?: MaybeRefOrGetter<string | null>) {
  const cache = useQueryCache();
  const fields = reactive<Record<string, FieldSave>>({});
  const controllers = new Set<AbortController>();

  /** Saves values together; each one's field shows the outcome. */
  async function save(values: readonly DeploySettingValue[]): Promise<Result<{ keys: SavedSetting[] }, ApiFailure>> {
    const slug = toValue(target);
    if (!slug || !values.length) return { ok: false, failure: ApiFailureFactory.create("cancelled") };
    for (const value of values) fields[value.key] = { state: "saving", message: null, version: null };
    const controller = new AbortController();
    controllers.add(controller);
    try {
      const result = await deploySettingsApi.save(slug, values, toValue(release) ?? null, controller.signal);
      for (const value of values) {
        const saved = result.ok ? result.value.keys.find((key) => key.key === value.key) : undefined;
        fields[value.key] = result.ok
          ? { state: "saved", message: null, version: saved?.version ?? null }
          : { state: "failed", message: failureReason(result.failure) || "Not saved.", version: null };
      }
      if (result.ok) void cache.invalidate(SettingsKeys.of(slug));
      return result;
    } catch {
      for (const value of values) fields[value.key] = { state: "failed", message: "Not saved.", version: null };
      return { ok: false, failure: ApiFailureFactory.create("transport") };
    } finally {
      controllers.delete(controller);
    }
  }

  /** Forgets every field's state, as when the dialog closes or the environment changes. */
  function reset(): void {
    for (const key of Object.keys(fields)) delete fields[key];
  }

  /** Whether any save is still on its way: a deploy waits for it. */
  function busy(): boolean {
    return Object.values(fields).some((field) => field.state === "saving");
  }

  onScopeDispose(() => {
    for (const controller of controllers) controller.abort();
  });

  return { fields, save, reset, busy };
}
