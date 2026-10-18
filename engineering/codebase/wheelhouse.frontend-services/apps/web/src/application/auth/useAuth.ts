import { onMounted, onScopeDispose, ref, type Ref } from "vue";
import {
  ApiFailureFactory,
  type ApiFailure,
} from "@wow-two-beta/fe/foundation/http";
import type { Result } from "@wow-two-beta/fe/foundation/results";
import { clearQuerySession } from "@/bootstrap/query";
import type { CurrentUser } from "@/domain/auth";
import { authApi } from "@/integration/auth";
import { clearHttpSession } from "@/integration/common";

/** The session held by the private console's root. */
export interface AuthSession {
  readonly user: Ref<CurrentUser | null>;
  readonly loading: Ref<boolean>;
  readonly error: Ref<ApiFailure | null>;
  readonly signIn: () => void;
  readonly signOut: () => Promise<void>;
}

let sessionRequest: Promise<Result<CurrentUser, ApiFailure>> | undefined;

/** Resolves one shared session read and invalidates private data when the owner leaves. */
export function useAuth(): AuthSession {
  const user = ref<CurrentUser | null>(null);
  const loading = ref(true);
  const error = ref<ApiFailure | null>(null);
  let active = true;

  onMounted(async () => {
    try {
      sessionRequest ??= authApi.getCurrentUser();
      const result = await sessionRequest;
      if (!active) return;
      if (result.ok) user.value = result.value;
      else if (result.failure.status !== 401) error.value = result.failure;
    } catch {
      if (active) error.value = ApiFailureFactory.create("transport");
    } finally {
      if (active) loading.value = false;
    }
  });
  onScopeDispose(() => {
    active = false;
  });

  function signIn(): void {
    window.location.assign(authApi.signInUrl());
  }

  async function signOut(): Promise<void> {
    try {
      const result = await authApi.signOut();
      if (!result.ok) {
        error.value = {
          ...result.failure,
          message:
            "Sign-out could not be confirmed. Your server session may still be active.",
        };
      }
    } catch {
      error.value = {
        ...ApiFailureFactory.create("transport"),
        message:
          "Sign-out could not be confirmed. Your server session may still be active.",
      };
    } finally {
      sessionRequest = undefined;
      user.value = null;
      clearHttpSession();
      clearQuerySession();
    }
  }

  return { user, loading, error, signIn, signOut };
}
