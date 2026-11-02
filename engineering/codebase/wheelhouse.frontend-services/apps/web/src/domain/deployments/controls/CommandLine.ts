/** A command line split into the arguments a run takes, or why it cannot be. */
export type CommandSplit =
  | { readonly ok: true; readonly args: string[] }
  | { readonly ok: false; readonly error: string };

/**
 * The command line a one-off run takes: the owner types one line and the console splits it into arguments the way a
 * shell would, without a shell. Whitespace separates, and single or double quotes group (the quotes go); nothing else
 * expands — no variables, globs or escapes, and a backslash is an ordinary character, so a quote inside an argument
 * sits in the other kind of quote. The host's limits hold: 1-40 arguments of 1-500 characters, no line breaks.
 */
export const CommandLine = {
  /** The most arguments a run takes. */
  MaxArguments: 40,

  /** The most characters one argument holds. */
  MaxArgumentLength: 500,

  /** Splits the typed text: blank text splits to no arguments, which a run cannot take; an unclosed quote, an empty,
   * overlong or multi-line argument, or too many arguments is an error saying so. */
  split: (text: string): CommandSplit => {
    const args: string[] = [];
    let current = '';
    let open = false;
    let quote: '"' | "'" | null = null;
    for (const char of text) {
      if (quote) {
        if (char === quote) quote = null;
        else if (char === '\n' || char === '\r') return fail('An argument cannot contain a line break.');
        else current += char;
      } else if (char === '"' || char === "'") {
        quote = char;
        open = true;
      } else if (/\s/.test(char)) {
        if (open) args.push(current);
        current = '';
        open = false;
      } else {
        current += char;
        open = true;
      }
    }
    if (quote) return fail(`The ${quote} quote is never closed.`);
    if (open) args.push(current);
    if (args.length > CommandLine.MaxArguments)
      return fail(`A run takes at most ${CommandLine.MaxArguments} arguments; this has ${args.length}.`);
    if (args.some((arg) => arg.length === 0)) return fail('An argument cannot be empty.');
    if (args.some((arg) => arg.length > CommandLine.MaxArgumentLength))
      return fail(`An argument is longer than ${CommandLine.MaxArgumentLength} characters.`);
    return { ok: true, args };
  },
} as const;

/** A failed split. @internal */
function fail(error: string): CommandSplit {
  return { ok: false, error };
}
