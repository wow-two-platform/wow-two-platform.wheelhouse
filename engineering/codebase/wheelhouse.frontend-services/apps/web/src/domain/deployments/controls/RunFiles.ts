/** What a run kept of the file it took on its stdin: its size and SHA-256, never its content. */
export interface RunInput {
  bytes: number;
  sha256: string;
}

/** The most bytes a run takes on its stdin, as the runner and the API allow. */
const Limit = 2 * 1024 * 1024;

/** Pure rules of a file a run takes on its stdin: sent base64 once, piped into the one-off container, kept nowhere. */
export const RunFiles = {
  limit: Limit,

  /** Why a file of `size` bytes cannot go to a run's stdin; null when it can. */
  problem(size: number): string | null {
    if (size <= 0) return "The file is empty.";
    return size > Limit ? `The file holds ${size} bytes; a run takes 2 MiB at most.` : null;
  },

  /** The file's bytes as base64, in chunks so a large file never overflows the call stack. */
  toBase64(bytes: Uint8Array): string {
    let binary = "";
    for (let start = 0; start < bytes.length; start += 0x8000)
      binary += String.fromCharCode(...bytes.subarray(start, start + 0x8000));
    return btoa(binary);
  },

  /** What a finished run says of the file it took: its size and the start of its SHA-256. */
  label(input: RunInput | null | undefined): string | null {
    return input ? `Took a ${input.bytes}-byte file on its stdin · SHA-256 ${input.sha256.slice(0, 16)}…` : null;
  },
} as const;
