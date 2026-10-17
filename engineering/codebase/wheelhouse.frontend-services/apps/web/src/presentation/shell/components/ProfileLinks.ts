import type { Component } from 'vue';
import { BookOpen, History, KeyRound, Monitor, Moon, Plug, Sun } from 'lucide-vue-next';
import { ConsolePaths } from '@/application/navigation';

/** One place the profile menu opens. */
export interface ProfileLink {
  readonly path: string;
  readonly label: string;
  readonly icon: Component;
}

/** The profile menu's places, in groups: the portfolio-wide views, then settings. */
export const ProfileLinkGroups: ReadonlyArray<{ readonly label: string; readonly links: readonly ProfileLink[] }> = [
  {
    label: 'Portfolio',
    links: [
      { path: ConsolePaths.activity, label: 'Activity', icon: History },
      { path: ConsolePaths.secrets, label: 'Secrets', icon: KeyRound },
    ],
  },
  {
    label: 'Settings',
    links: [
      { path: ConsolePaths.keys, label: 'Integration keys', icon: Plug },
      { path: ConsolePaths.catalog, label: 'Catalog', icon: BookOpen },
    ],
  },
];

/** The appearance choices, in the order both the menu and the sheet list them. */
export const AppearanceSchemes = [
  { value: 'light', label: 'Light', icon: Sun },
  { value: 'dark', label: 'Dark', icon: Moon },
  { value: 'system', label: 'System', icon: Monitor },
] as const;

/** Shortens a build's `+<commit>` suffix to seven characters. */
export function shortVersion(version: string): string {
  return version.replace(/\+([0-9a-f]{7})[0-9a-f]*$/i, '+$1');
}
