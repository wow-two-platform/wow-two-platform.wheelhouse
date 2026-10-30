import type { AuditEntry } from './AuditEntry';

/** One keyset page of the trail, newest first: the entries and the token that reads the older ones. */
export interface AuditPage {
  items: AuditEntry[];
  /** The page size the server served, after its cap. */
  pageSize: number;
  /** Reads the next, older page; null on the last page. */
  nextPageToken: string | null;
}
