import { Ordering } from '@/domain/common/Ordering';
import { PinKind, type CreatePinRequest, type Pin, type PinProduct, type PinSubject } from './Pin';

/** Whether a pin's product reference — a row identifier or a slug — names this product. @internal */
function namesProduct(reference: string | null, product: PinProduct): boolean {
  return reference !== null && (reference === product.slug || (product.id != null && reference === product.id));
}

/** The prefix of a pin that exists only on screen until the host answers the pin request. */
const PendingPrefix = 'pending:';

/** Pure rules for pins: finding one, the request that adds one and the optimistic list changes. */
export const PinRules = {
  /** Whether the pin points at the subject. */
  matches: (pin: Pin, subject: PinSubject): boolean => {
    if (pin.kind !== subject.kind) return false;
    if (subject.kind === PinKind.Workspace) return pin.workspaceId === subject.workspaceId;
    if (!namesProduct(pin.productId, subject.product)) return false;
    return subject.kind === PinKind.Product || pin.service === subject.service;
  },

  /** The pin that points at the subject, if any. */
  find: (pins: readonly Pin[] | undefined, subject: PinSubject): Pin | undefined =>
    (pins ?? []).find((pin) => PinRules.matches(pin, subject)),

  /** The request that pins the subject; a product is named by its row identifier when the host sends one. */
  requestOf: (subject: PinSubject): CreatePinRequest =>
    subject.kind === PinKind.Workspace
      ? { kind: subject.kind, workspaceId: subject.workspaceId }
      : subject.kind === PinKind.Product
        ? { kind: subject.kind, productId: subject.product.id ?? subject.product.slug }
        : { kind: subject.kind, productId: subject.product.id ?? subject.product.slug, service: subject.service },

  /** The list with the subject pinned at the end, for an optimistic update; an existing pin leaves it unchanged. */
  withPin: (pins: readonly Pin[] | undefined, subject: PinSubject): Pin[] => {
    const list = Ordering.sort(pins ?? []);
    if (PinRules.find(list, subject)) return list;
    const request = PinRules.requestOf(subject);
    const pending: Pin = {
      id: `${PendingPrefix}${request.kind}:${request.workspaceId ?? request.productId}:${request.service ?? ''}`,
      kind: request.kind,
      workspaceId: request.workspaceId ?? null,
      productId: request.productId ?? null,
      service: request.service ?? null,
      order: list.length,
    };
    return [...list, pending];
  },

  /** The list without one pin, renumbered, for an optimistic update. */
  withoutPin: (pins: readonly Pin[] | undefined, id: string): Pin[] =>
    Ordering.renumber(Ordering.sort(pins ?? []).filter((pin) => pin.id !== id)),

  /** Whether the pin exists only on screen while its request is pending. */
  isPending: (pin: Pin): boolean => pin.id.startsWith(PendingPrefix),

  /** The pin ids in a new order after one moved, the body of the reorder request. */
  reorder: (pins: readonly Pin[], from: number, to: number): string[] =>
    Ordering.move(Ordering.sort(pins), from, to).map((pin) => pin.id),
} as const;
