import type { RootKeyOfflineCopy } from "./models";

/** What a failed root key file check found, in words. */
const problems: Record<string, string> = {
  missing: "the file is missing",
  unreadable: "the console may not read it: give it to the console's user",
  exposed: "its mode lets others than its owner in: chmod 0400",
  invalid: "it holds no base64 256-bit key",
  mismatch: "it belongs to another vault",
};

/** Pure rules of the root key's checks: the owner's offline copy and the scheduled key file check. */
export const RootKeyRules = {
  /** Prints the fingerprint of a root key on the owner's own machine, the same way the console does: the key comes
   *  from stdin (a file redirect) or a hidden prompt, never an argument, and never reaches the console. */
  command:
    `python3 -c 'import sys,base64,hashlib,getpass;t=getpass.getpass("Root key: ") if sys.stdin.isatty() else sys.stdin.read();` +
    `k=base64.b64decode(t.strip(),validate=True);assert len(k)==32,"not a 256-bit key";` +
    `h=hashlib.sha256(b"wheelhouse.vault.root-key.v1\\0"+k).hexdigest()[:20];print(" ".join(h[i:i+4] for i in range(0,20,4)))' < root.key`,

  /** What a failed file check found. */
  problemText(problem: string): string {
    return problems[problem] ?? "it could not be checked";
  },

  /** The offline copy's state in one line; dates as the caller formats them. */
  offlineCopyText(copy: RootKeyOfflineCopy, date: (iso: string) => string): string {
    if (!copy.fingerprint) return "The fingerprint shows once the vault unseals.";
    if (copy.changed) return "The key in use changed since your copy was last verified: compare it again.";
    if (!copy.verifiedAt) return "Your offline copy was never verified against this key.";
    const by = copy.verifiedBy ? ` by ${copy.verifiedBy}` : "";
    const next = copy.dueAt ? `; ${copy.due ? "due since" : "next due"} ${date(copy.dueAt)}` : "";
    return `Verified ${date(copy.verifiedAt)}${by}${next}.`;
  },
};
