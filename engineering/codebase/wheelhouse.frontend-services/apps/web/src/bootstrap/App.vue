<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { RouterView, useRouter } from 'vue-router';
import { ColorModeProvider } from '@wow-two-beta/fe/foundation/primitives';
import { FeedbackToastHost } from '@wow-two-beta/fe/presentation/feedback';
import { useAuth } from '@/application/auth';
import SignInScreen from '@/presentation/auth/SignInScreen.vue';
import { BootSplash } from '@/presentation/shell';
import AppLayout from './AppLayout.vue';
import { ThemeStorageKey } from './theme';

/** Names the boot stages the splash bar reports; the static splash in `index.html` stops at `Scripts`. */
const BootProgress = { Scripts: 35, Session: 70, Ready: 100 } as const;

/** Holds the full bar long enough for its width transition to finish before the splash fades. */
const SettleMs = 250;

/** Gates every private workspace route on the owner's GitHub session, behind the first-load splash. */
defineOptions({ name: 'App' });
const auth = useAuth();
const routed = ref(false);
const booting = ref(true);

void useRouter()
  .isReady()
  .finally(() => (routed.value = true));

const progress = computed(() =>
  auth.loading.value ? BootProgress.Scripts : routed.value ? BootProgress.Ready : BootProgress.Session,
);

watch(progress, (value) => {
  if (value === BootProgress.Ready) setTimeout(() => (booting.value = false), SettleMs);
});
</script>
<template>
  <ColorModeProvider :storage-key="ThemeStorageKey">
    <AppLayout v-if="auth.user.value" :user="auth.user.value" @sign-out="auth.signOut">
      <RouterView />
    </AppLayout>
    <SignInScreen v-else-if="!auth.loading.value" :error="auth.error.value" @sign-in="auth.signIn" />
    <BootSplash :is-open="booting" :value="progress" />
    <!-- Carries query failures: a region that fails to load shows neutral copy, the reason arrives here. -->
    <FeedbackToastHost />
  </ColorModeProvider>
</template>
