/**
 * The newest export of the inventory snapshot failed: the runner keeps reading the last exported one until an export
 * succeeds again.
 */
export interface InventoryExportIssue {
  /** What went wrong, in plain words. */
  readonly cause: string;
  /** When the export failed. */
  readonly at: string;
}
