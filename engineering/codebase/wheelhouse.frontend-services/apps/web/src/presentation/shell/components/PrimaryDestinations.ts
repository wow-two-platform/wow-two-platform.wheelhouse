import type { Component } from 'vue';
import { Boxes, House, Rocket, Server } from 'lucide-vue-next';
import { ConsolePaths, type PrimaryPath } from '@/application/navigation';

/** One primary destination as the sidebar, the rail and the phone's bottom bar draw it. */
export interface PrimaryDestination {
  readonly path: PrimaryPath;
  readonly label: string;
  readonly icon: Component;
}

/** Home, Products, Deployments, Servers — the same four, in the same order, on every screen class. */
export const PrimaryDestinations: readonly PrimaryDestination[] = [
  { path: ConsolePaths.home, label: 'Home', icon: House },
  { path: ConsolePaths.products, label: 'Products', icon: Boxes },
  { path: ConsolePaths.deployments, label: 'Deployments', icon: Rocket },
  { path: ConsolePaths.servers, label: 'Servers', icon: Server },
];
