/*
 * Computed-alias formulas, e.g. "(cpu.power + gpu.power + 50) / 0.88".
 * Twin of src/SensorPanel/Layout/Formula.cs — keep the grammar in sync:
 *   numbers, keys (cpu.power), [any-key or /raw/id], + - * /, ( ), min/max/abs(…), a ?? b.
 * A missing value (or ÷0) makes the result null unless caught by ??.
 */

type Node =
  | { k: 'num'; v: number }
  | { k: 'key'; name: string }
  | { k: 'neg'; a: Node }
  | { k: 'bin'; op: '+' | '-' | '*' | '/' | '?'; a: Node; b: Node }
  | { k: 'call'; fn: 'min' | 'max' | 'abs'; args: Node[] };

export interface Formula { root: Node; keys: Set<string> }

export function parseFormula(s: string): Formula {
  let i = 0;
  const keys = new Set<string>();
  const fail = (msg: string): never => { throw new Error(`${msg} at position ${i + 1}.`); };
  const skip = () => { while (i < s.length && /\s/.test(s[i])) i++; };
  const eat = (t: string) => { skip(); if (s.startsWith(t, i)) { i += t.length; return true; } return false; };

  const coalesce = (): Node => { let n = sum(); while (eat('??')) n = { k: 'bin', op: '?', a: n, b: sum() }; return n; };
  const sum = (): Node => {
    let n = product();
    for (;;) {
      if (eat('+')) n = { k: 'bin', op: '+', a: n, b: product() };
      else if (eat('-')) n = { k: 'bin', op: '-', a: n, b: product() };
      else return n;
    }
  };
  const product = (): Node => {
    let n = unary();
    for (;;) {
      if (eat('*')) n = { k: 'bin', op: '*', a: n, b: unary() };
      else if (eat('/')) n = { k: 'bin', op: '/', a: n, b: unary() };
      else return n;
    }
  };
  const unary = (): Node => (eat('-') ? { k: 'neg', a: unary() } : eat('+') ? unary() : atom());
  const atom = (): Node => {
    skip();
    if (i >= s.length) fail('Unexpected end');
    const c = s[i];
    if (eat('(')) { const n = coalesce(); if (!eat(')')) fail("Missing ')'"); return n; }
    if (c === '[') {
      const end = s.indexOf(']', i + 1);
      if (end < 0) fail("Missing ']'");
      const name = s.slice(i + 1, end).trim();
      if (!name) fail('Empty [ ]');
      i = end + 1;
      keys.add(name);
      return { k: 'key', name };
    }
    if (/[0-9.]/.test(c)) {
      const m = /^[0-9.]+/.exec(s.slice(i))![0];
      if (!/^(\d+\.?\d*|\.\d+)$/.test(m)) fail('Bad number');
      i += m.length;
      return { k: 'num', v: parseFloat(m) };
    }
    if (/[A-Za-z_]/.test(c)) {
      const name = /^[A-Za-z_][\w.]*/.exec(s.slice(i))![0].replace(/\.+$/, '');
      i += name.length;
      if (eat('(')) {
        const fn = name.toLowerCase();
        if (fn !== 'min' && fn !== 'max' && fn !== 'abs') fail(`Unknown function '${name}'`);
        const args: Node[] = [];
        if (!eat(')')) {
          do args.push(coalesce()); while (eat(','));
          if (!eat(')')) fail("Missing ')'");
        }
        if (!args.length || (fn === 'abs' && args.length !== 1)) fail(`Wrong number of arguments for ${fn}()`);
        return { k: 'call', fn: fn as 'min' | 'max' | 'abs', args };
      }
      keys.add(name);
      return { k: 'key', name };
    }
    return fail(`Unexpected '${c}'`);
  };

  if (!s.trim()) throw new Error('Formula is empty.');
  if (s.length > 500) throw new Error('Formula is too long.');
  const root = coalesce();
  skip();
  if (i < s.length) fail(`Unexpected '${s[i]}'`);
  return { root, keys };
}

export function evalFormula(f: Formula, lookup: (key: string) => number | null | undefined): number | null {
  const ev = (n: Node): number | null => {
    switch (n.k) {
      case 'num': return n.v;
      case 'key': { const v = lookup(n.name); return v != null && Number.isFinite(v) ? v : null; }
      case 'neg': { const v = ev(n.a); return v == null ? null : -v; }
      case 'bin': {
        if (n.op === '?') return ev(n.a) ?? ev(n.b);
        const a = ev(n.a), b = ev(n.b);
        if (a == null || b == null) return null;
        return n.op === '+' ? a + b : n.op === '-' ? a - b : n.op === '*' ? a * b : b === 0 ? null : a / b;
      }
      case 'call': {
        const vals = n.args.map(ev);
        if (vals.some(v => v == null)) return null;
        const nums = vals as number[];
        return n.fn === 'min' ? Math.min(...nums) : n.fn === 'max' ? Math.max(...nums) : Math.abs(nums[0]);
      }
    }
  };
  const v = ev(f.root);
  return v != null && Number.isFinite(v) ? v : null;
}

/** Parses, returning an error message instead of throwing. */
export function tryParseFormula(s: string): { formula: Formula | null; error: string | null } {
  try { return { formula: parseFormula(s), error: null }; } catch (e) { return { formula: null, error: (e as Error).message }; }
}
