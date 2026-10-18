import { createAppRouter, type AppRoute, type LazyRoute } from '@wow-two-beta/fe/router';
import { legacyProductRedirect, workspaceHash } from '@/application/navigation/ConsolePaths';

/** The portfolio home: every workspace and its products. */
export const HomePlace = {
  path: '/',
  label: 'Home',
  description: 'Every workspace and its products, with what needs you first.',
} as const;

/**
 * The primary destinations after Home — the entities the console navigates by — in the order the sidebar, the icon
 * rail and the phone's bottom bar show them.
 */
export const PrimaryPlaces = [
  { path: '/products', label: 'Products', description: 'Every workspace, then its products.' },
  {
    path: '/deployments',
    label: 'Deployments',
    description: 'Published releases, readiness checks, and rollout outcomes.',
  },
  { path: '/servers', label: 'Servers', description: 'Your servers, the products they host and their readings.' },
] as const;

/**
 * Portfolio-wide views the profile menu opens. Secrets and activity belong to products — each product page has them
 * as tabs — so these are the views across every product.
 */
export const GlobalPlaces = [
  { path: '/activity', label: 'Activity', description: 'Every action, chained so an edited entry shows.' },
  { path: '/secrets', label: 'Secrets', description: 'Vault namespaces, write-only secrets, and product tokens.' },
] as const;

/** Account-level destinations, under Settings in the profile menu. */
export const SettingsPlaces = [
  { path: '/settings/keys', label: 'Integration keys', description: 'Scoped keys other programs present to read Wheelhouse.' },
  { path: '/settings/catalog', label: 'Catalog', description: 'Every wow2 app the workspace catalog lists, ready to import.' },
] as const;

/** A fixed place in the console: its URL, its title and the line under it. */
type Place =
  | typeof HomePlace
  | (typeof PrimaryPlaces)[number]
  | (typeof GlobalPlaces)[number]
  | (typeof SettingsPlaces)[number];

/** The page lazily loaded for each place. @internal */
const Pages: Record<Place['path'], LazyRoute> = {
  '/': () => import('@/presentation/home/pages/HomePage.vue'),
  '/deployments': () => import('@/presentation/deployments/pages/DeploymentsPage.vue'),
  '/servers': () => import('@/presentation/servers/pages/ServersPage.vue'),
  '/secrets': () => import('@/presentation/secrets/pages/SecretsPage.vue'),
  '/products': () => import('@/presentation/products/pages/ProductsPage.vue'),
  '/activity': () => import('@/presentation/audit/pages/ActivityPage.vue'),
  '/settings/keys': () => import('@/presentation/integrations/pages/IntegrationKeysPage.vue'),
  '/settings/catalog': () => import('@/presentation/catalog/pages/CatalogPage.vue'),
};

/** A place's route: its handle carries the title (scope bar and document title) and the description. @internal */
function placeRoute(place: Place): AppRoute {
  return { path: place.path, lazy: Pages[place.path], handle: { title: place.label, description: place.description } };
}

/** Reads a path parameter as one string. @internal */
function param(value: unknown): string {
  return Array.isArray(value) ? String(value[0] ?? '') : String(value ?? '');
}

/**
 * Resolves the public URLs to route components. vue-router carries the query through a redirect, so `/fleet?…` lands on
 * `/servers?…`; the explicit catch-all is matched before the SDK's own not-found route. An old Workspace link
 * (`/?product=x&target=…`) opens the product's own page with its other parameters.
 */
export const router = createAppRouter(
  [
    placeRoute(HomePlace),
    // Workspaces have no page of their own: an old workspace link opens its group on the Products page.
    {
      path: '/w/:workspace',
      lazy: Pages['/products'],
      guard: ({ params }) => ({
        redirect: { path: '/products', hash: workspaceHash(param(params['workspace'])) },
      }),
    },
    // One route for every tab, so moving between tabs keeps the page and its reads; the page answers an unknown tab.
    {
      path: '/p/:product/:tab?',
      lazy: () => import('@/presentation/product/ProductPage.vue'),
      handle: { title: (route) => param(route.params['product']), description: 'A product, environment by environment.' },
    },
    ...[...PrimaryPlaces, ...GlobalPlaces, ...SettingsPlaces].map(placeRoute),
    // One deployment: its rollout, services, sites and actions. The id is a submission's UUID.
    {
      path: '/deployments/:id',
      lazy: () => import('@/presentation/deployments/pages/DeploymentDetailPage.vue'),
      handle: {
        title: (route) => `Deployment ${param(route.params['id']).slice(0, 8)}`,
        description: 'One rollout: its steps, services, sites and what needs a look.',
      },
    },
    { path: '/fleet', redirect: '/servers' },
    { path: '*', redirect: '/' },
  ],
  { titleSuffix: 'Wheelhouse', routePersistence: false, scrollRestoration: false },
);

/* A global guard rather than the home route's own: a route's guard runs only when the route is entered, and an old
   link followed from the home page changes only the query. */
router.beforeEach((to) => (to.path === HomePlace.path ? (legacyProductRedirect(to.query) ?? true) : true));
