import { computed, onScopeDispose, readonly, ref, watch } from 'vue';

/** Names the operator's persisted theme preference. */
export const ColorScheme = { System: 'system', Light: 'light', Dark: 'dark' } as const;
export type ColorScheme = (typeof ColorScheme)[keyof typeof ColorScheme];

/** The theme the console shows once the scheme and the operating system's preference resolve. */
export type ActiveTheme = 'light' | 'dark';

/** Starts from the pre-paint script's result, so a theme-specific asset asks for the right variant on first render. @internal */
const activeTheme = ref<ActiveTheme>(
  typeof document !== 'undefined' && document.documentElement.classList.contains('dark') ? 'dark' : 'light',
);

/** The theme the console shows now, for anything that picks a theme-specific asset; follows every scheme change. */
export function useActiveTheme() {
  return readonly(activeTheme);
}

/** Applies the saved scheme while tracking operating-system changes. */
export function useColorScheme() {
  const media = window.matchMedia('(prefers-color-scheme: dark)');
  const scheme = ref<ColorScheme>(read());
  const prefersDark = ref(media.matches);
  const isDark = computed(() => scheme.value === 'dark' || (scheme.value === 'system' && prefersDark.value));
  const onChange = (event: MediaQueryListEvent) => {
    prefersDark.value = event.matches;
  };
  media.addEventListener('change', onChange);
  onScopeDispose(() => media.removeEventListener('change', onChange));
  watch(
    isDark,
    (dark) => {
      document.documentElement.classList.toggle('dark', dark);
      document.documentElement.style.colorScheme = dark ? 'dark' : 'light';
      activeTheme.value = dark ? 'dark' : 'light';
    },
    { immediate: true },
  );
  function setScheme(value: ColorScheme): void {
    scheme.value = value;
    try {
      localStorage.setItem('wheelhouse.theme', value);
    } catch {
      /* This visit still receives the selected theme. */
    }
  }
  return { scheme, isDark, setScheme };
}

/** Reads the value shared with the pre-paint theme script. @internal */
function read(): ColorScheme {
  try {
    const stored = localStorage.getItem('wheelhouse.theme');
    return stored === 'light' || stored === 'dark' ? stored : 'system';
  } catch {
    return 'system';
  }
}
