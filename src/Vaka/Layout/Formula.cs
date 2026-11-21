using System.Globalization;

namespace Vaka.Layout;

/// <summary>
/// Arithmetic over sensor keys for computed aliases, e.g. <c>(cpu.power + gpu.power + 50) / 0.88</c>.
/// <list type="bullet">
/// <item>Numbers, keys (<c>cpu.power</c>, <c>drive.C.usedPct</c>), <c>+ - * /</c>, parentheses.</item>
/// <item>Keys with other characters (dashes, raw ids) go in brackets: <c>[/amdcpu/0/power/0]</c>.</item>
/// <item><c>min(a, b, …)</c>, <c>max(…)</c>, <c>abs(x)</c>.</item>
/// <item><c>a ?? b</c> uses b when a has no value. Otherwise a missing value (or ÷0) makes the result null.</item>
/// </list>
/// The editor has a TypeScript twin in <c>src/editor-ui/src/formula.ts</c>; keep them in sync.
/// </summary>
public sealed class Formula
{
    private abstract record Node;
    private sealed record Num(double Value) : Node;
    private sealed record Key(string Name) : Node;
    private sealed record Neg(Node Inner) : Node;
    private sealed record Bin(char Op, Node Left, Node Right) : Node; // + - * / and '?' for ??
    private sealed record Call(string Fn, Node[] Args) : Node;

    private readonly Node _root;
    public string Text { get; }
    public IReadOnlySet<string> Keys { get; }

    private Formula(string text, Node root, IReadOnlySet<string> keys) { Text = text; _root = root; Keys = keys; }

    /// <exception cref="FormatException">The formula is malformed.</exception>
    public static Formula Parse(string text)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var p = new Parser(text, keys);
        var root = p.ParseAll();
        return new Formula(text, root, keys);
    }

    public static bool TryParse(string text, out Formula? formula, out string? error)
    {
        try { formula = Parse(text); error = null; return true; }
        catch (FormatException ex) { formula = null; error = ex.Message; return false; }
    }

    public double? Evaluate(Func<string, double?> lookup)
    {
        var v = Eval(_root, lookup);
        return v is { } d && double.IsFinite(d) ? d : null;
    }

    private static double? Eval(Node n, Func<string, double?> lookup) => n switch
    {
        Num x => x.Value,
        Key k => lookup(k.Name) is { } v && double.IsFinite(v) ? v : null,
        Neg x => -Eval(x.Inner, lookup),
        Bin { Op: '?' } b => Eval(b.Left, lookup) ?? Eval(b.Right, lookup),
        Bin b => Eval(b.Left, lookup) is { } l && Eval(b.Right, lookup) is { } r
            ? b.Op switch
            {
                '+' => l + r,
                '-' => l - r,
                '*' => l * r,
                _ => r == 0 ? null : l / r,
            }
            : null,
        Call c => EvalCall(c, lookup),
        _ => null,
    };

    private static double? EvalCall(Call c, Func<string, double?> lookup)
    {
        var args = new double[c.Args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            if (Eval(c.Args[i], lookup) is not { } v) return null;
            args[i] = v;
        }
        return c.Fn switch
        {
            "min" => args.Min(),
            "max" => args.Max(),
            _ => Math.Abs(args[0]),
        };
    }

    private sealed class Parser(string s, HashSet<string> keys)
    {
        private int _i;

        public Node ParseAll()
        {
            if (string.IsNullOrWhiteSpace(s)) throw new FormatException("Formula is empty.");
            if (s.Length > 500) throw new FormatException("Formula is too long.");
            var n = Coalesce();
            Skip();
            if (_i < s.Length) throw Error($"Unexpected '{s[_i]}'");
            return n;
        }

        private FormatException Error(string msg) => new($"{msg} at position {_i + 1}.");

        private void Skip() { while (_i < s.Length && char.IsWhiteSpace(s[_i])) _i++; }

        private bool Eat(string tok)
        {
            Skip();
            if (string.CompareOrdinal(s, _i, tok, 0, tok.Length) != 0) return false;
            _i += tok.Length;
            return true;
        }

        private Node Coalesce()
        {
            var n = Sum();
            while (Eat("??")) n = new Bin('?', n, Sum());
            return n;
        }

        private Node Sum()
        {
            var n = Product();
            while (true)
            {
                if (Eat("+")) n = new Bin('+', n, Product());
                else if (Eat("-")) n = new Bin('-', n, Product());
                else return n;
            }
        }

        private Node Product()
        {
            var n = Unary();
            while (true)
            {
                if (Eat("*")) n = new Bin('*', n, Unary());
                else if (Eat("/")) n = new Bin('/', n, Unary());
                else return n;
            }
        }

        private Node Unary()
        {
            if (Eat("-")) return new Neg(Unary());
            if (Eat("+")) return Unary();
            return Atom();
        }

        private Node Atom()
        {
            Skip();
            if (_i >= s.Length) throw Error("Unexpected end");
            char c = s[_i];
            if (Eat("("))
            {
                var n = Coalesce();
                if (!Eat(")")) throw Error("Missing ')'");
                return n;
            }
            if (c == '[')
            {
                int end = s.IndexOf(']', _i + 1);
                if (end < 0) throw Error("Missing ']'");
                var name = s[(_i + 1)..end].Trim();
                if (name.Length == 0) throw Error("Empty [ ]");
                _i = end + 1;
                keys.Add(name);
                return new Key(name);
            }
            if (char.IsDigit(c) || c == '.')
            {
                int start = _i;
                while (_i < s.Length && (char.IsDigit(s[_i]) || s[_i] == '.')) _i++;
                if (!double.TryParse(s.AsSpan(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    throw Error("Bad number");
                return new Num(v);
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = _i;
                while (_i < s.Length && (char.IsLetterOrDigit(s[_i]) || s[_i] is '_' or '.')) _i++;
                var name = s[start.._i].TrimEnd('.');
                _i = start + name.Length;
                if (Eat("("))
                {
                    var fn = name.ToLowerInvariant();
                    if (fn is not ("min" or "max" or "abs")) throw Error($"Unknown function '{name}'");
                    var args = new List<Node>();
                    if (!Eat(")"))
                    {
                        do args.Add(Coalesce()); while (Eat(","));
                        if (!Eat(")")) throw Error("Missing ')'");
                    }
                    if (args.Count == 0 || (fn == "abs" && args.Count != 1)) throw Error($"Wrong number of arguments for {fn}()");
                    return new Call(fn, args.ToArray());
                }
                keys.Add(name);
                return new Key(name);
            }
            throw Error($"Unexpected '{c}'");
        }
    }

    /// <summary>Keys an alias depends on, transitively (formula aliases expanded; cycles stop).</summary>
    public static IReadOnlySet<string> ExpandKeys(PanelLayout layout, IEnumerable<string> keys)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        var todo = new Stack<string>(keys);
        while (todo.Count > 0)
        {
            var k = todo.Pop();
            if (!all.Add(k)) continue;
            if (layout.Aliases.TryGetValue(k, out var a) && !string.IsNullOrWhiteSpace(a.Expr) &&
                TryParse(a.Expr, out var f, out _))
                foreach (var dep in f!.Keys) todo.Push(dep);
        }
        return all;
    }
}
