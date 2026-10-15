import { createAppRouter, type AppRoute, type LazyRoute } from '@wow-two-beta/ui-vue/router';

/** Keeps primary destinations stable while route content loads on demand. */
export const AppPlaces = [
  { path: '/', label: 'Workspace', description: 'Your products, environments, and the details that matter.' },
  {
    path: '/deployments',
    label: 'Deployments',
    description: 'Published releases, readiness checks, and rollout outcomes.',
  },
  { path: '/servers', label: 'Servers', description: 'Your servers and their services, with current resource readings.' },
  { path: '/secrets', label: 'Secrets', description: 'Vault namespaces, write-only secrets, and product tokens.' },
  { path: '/products', label: 'Products', description: 'Your portfolio, as the product catalog defines it.' },
  { path: '/activity', label: 'Activity', description: 'Every operator action, chained so an edited entry shows.' },
] as const;

/** Keeps account-level destinations out of the primary navigation; the profile menu opens them. */
export const SettingsPlaces = [
  { path: '/settings/keys', label: 'Integration keys', description: 'Scoped keys other programs present to read Wheelhouse.' },
] as const;

/** A place in the console: its URL, its title and the line under it. */
type Place = (typeof AppPlaces)[number] | (typeof SettingsPlaces)[number];

/** The page lazily loaded for each place. @internal */
const Pages: Record<Place['path'], LazyRoute> = {
  '/': () => import('@/presentation/workspace/WorkspacePage.vue'),
  '/deployments': () => import('@/presentation/deployments/pages/DeploymentsPage.vue'),
  '/servers': () => import('@/presentation/servers/pages/ServersPage.vue'),
  '/secrets': () => import('@/presentation/secrets/pages/SecretsPage.vue'),
  '/products': () => import('@/presentation/products/pages/ProductsPage.vue'),
  '/activity': () => import('@/presentation/audit/pages/ActivityPage.vue'),
  '/settings/keys': () => import('@/presentation/integrations/pages/IntegrationKeysPage.vue'),
};

/** A place's route: its handle carries the title (header and document title) and the description. @internal */
function placeRoute(place: Place): AppRoute {
  return { path: place.path, lazy: Pages[place.path], handle: { title: place.label, description: place.description } };
}

/**
 * Resolves the existing public URLs to Vue route components. vue-router carries the query and hash through a redirect,
 * so `/fleet?…` lands on `/servers?…`; the explicit catch-all is matched before the SDK's own not-found route.
 */
export const router = createAppRouter(
  [
    ...[...AppPlaces, ...SettingsPlaces].map(placeRoute),
    { path: '/fleet', redirect: '/servers' },
    { path: '*', redirect: '/' },
  ],
  { titleSuffix: 'Wheelhouse', routePersistence: false, scrollRestoration: false },
);
