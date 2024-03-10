using System.Collections.Immutable;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.VisualBasic;

namespace Pingmint.Yaml;

public enum LexTokenType : int
{
    End = 0,
    DocumentStart,
    DocumentEnd,
    Indent,
    Spaces,
    Line,
    Map,
    Dash,
    Text,
}

public struct LexToken
{
    public LexTokenType Type;
    public int Start;
    public int Length;
    public String Value;

    public LexToken(LexTokenType type, int start, int length, String value)
    {
        Type = type;
        Start = start;
        Length = length;
        Value = value;
    }

    public String Name => Type switch
    {
        LexTokenType.End => "end",
        LexTokenType.Indent => "-->",
        LexTokenType.Spaces => "<->",
        LexTokenType.Line => "line",
        LexTokenType.Map => "map",
        LexTokenType.Dash => "dash",
        LexTokenType.Text => "text",
        LexTokenType.DocumentStart => "doc+",
        _ => throw new NotImplementedException($"unexpected token: {Type}")
    };
}

public enum ParseTokenType : int
{
    End = 0,
    StreamStart,
    StreamEnd,
    DocStart,
    DocEnd,
    MapStart,
    MapEnd,
    SeqStart,
    SeqEnd,
    Scalar,
}

public struct ParseToken
{
    public ParseTokenType Type;
    public String? Value; // TODO: span
    public int Start;

    public ParseToken(ParseTokenType type, String? value, int start)
    {
        Type = type;
        Start = start;
        Value = value;
    }

    public int Length => Value?.Length ?? 0;

    public String Name => Type switch
    {
        ParseTokenType.StreamStart => "str+",
        ParseTokenType.StreamEnd => "str-",
        ParseTokenType.DocStart => "doc+",
        ParseTokenType.DocEnd => "doc-",
        ParseTokenType.MapStart => "map+",
        ParseTokenType.MapEnd => "map-",
        ParseTokenType.SeqStart => "seq+",
        ParseTokenType.SeqEnd => "seq-",
        ParseTokenType.Scalar => "text",
        _ => throw new NotImplementedException($"unexpected token: {Type}")
    };
}

public enum ParseState : int
{
    Stream = 0,
    Doc,
    Map,
    Sequence,
}
public static class Parser
{
    public static ReadOnlySpan<LexToken> Lex(ReadOnlySpan<Char> yaml)
    {
        var tokens = new List<LexToken>();
        var here = 0;
        var isIndent = true;
        while (here < yaml.Length)
        {
            var c = yaml[here];
            if (c == '-')
            {
                isIndent = false;
                if (here + 2 < yaml.Length && yaml[here + 1] == '-' && yaml[here + 2] == '-')
                {
                    tokens.Add(new LexToken(LexTokenType.DocumentStart, here, 3, "---"));
                    here += 3;
                    continue;
                }
                tokens.Add(new LexToken(LexTokenType.Dash, here, 1, "-"));
                here++;
                continue;
            }

            if (c == ' ')
            {
                var start2 = here;
                here++;
                while (here < yaml.Length && yaml[here] == ' ')
                {
                    here++;
                }
                var type = isIndent ? LexTokenType.Indent : LexTokenType.Spaces;
                tokens.Add(new LexToken(type, start2, here - start2, yaml[start2..here].ToString()));
                continue;
            }

            if (c == '\r')
            {
                if (here + 1 >= yaml.Length)
                {
                    throw new InvalidOperationException($"Unexpected char: {c} at position {here}");
                }

                if (yaml[here + 1] == '\n')
                {
                    tokens.Add(new LexToken(LexTokenType.Line, here, 2, yaml[here..(here + 2)].ToString()));
                    here += 2;
                    isIndent = true;
                    continue;
                }

                throw new InvalidOperationException($"Unexpected char: {c} at position {here}");
            }

            if (c == '\n')
            {
                tokens.Add(new LexToken(LexTokenType.Line, here, 1, "\n"));
                here++;
                isIndent = true;
                continue;
            }

            if (c == ':')
            {
                isIndent = false;
                tokens.Add(new LexToken(LexTokenType.Map, here, 1, ":"));
                here++;
                continue;
            }

            // TODO: ignoring comments for now
            if (c == '#')
            {
                while (here < yaml.Length)
                {
                    if (yaml[here] == '\r' || yaml[here] == '\n')
                    {
                        break;
                    }
                    here++;
                }
                continue;
            }

            if (c is '|' or '>')
            {
                throw new InvalidOperationException($"String modes not implemented, char: {c} at position {here}");
            }

            if (c is '.')
            {
                if (here + 2 < yaml.Length && yaml[here + 1] == '.' && yaml[here + 2] == '.')
                {
                    tokens.Add(new LexToken(LexTokenType.DocumentEnd, here, 3, "..."));
                    here += 3;
                    continue;
                }
            }

            isIndent = false;
            var start = here;
            while (here < yaml.Length)
            {
                // TODO: this is likely wrong when the string contains lexer tokens
                var c2 = yaml[here];
                if (Char.IsLetterOrDigit(c2)) { }
                else if (c2 == ' ') { }
                else if (c2 == '\r') { break; }
                else if (c2 == '\n') { break; }
                else if (c2 == ':') { break; }
                else if (c2 == '-') { break; }
                else if (c2 == '#') { break; }
                here++;
            }

            tokens.Add(new LexToken(LexTokenType.Text, start, here - start, yaml[start..here].ToString()));
        }
        return tokens.ToArray();
    }

    public static List<ParseToken> Parse(ReadOnlySpan<LexToken> lex)
    {
        var lexLength = lex.Length;
        var tokens = new List<ParseToken>();

        var op = ParseState.Stream;
        var minIndent = -1;
        var here = 0;

        const int MAX_DEPTH = 32;
        var depth = 0;
        var opStack = new ParseState[MAX_DEPTH];
        var inStack = new Int32[MAX_DEPTH];

        AddToken(ParseTokenType.StreamStart, null, here);
        while (true)
        {
            var start = here;
            var (peek, left, indent) = PeekStartLine(lex, start);
            while (indent < minIndent && depth >= 0)
            {
                Pop();
            }
            switch (op)
            {
                case ParseState.Stream:
                {
                    switch (peek)
                    {
                        case LexTokenType.End:
                        {
                            if (depth != 0) { throw ParseFailed($"unexpected end of input, depth {depth}"); }
                            AddToken(ParseTokenType.StreamEnd, null, here);
                            return tokens;
                        }
                        case LexTokenType.DocumentEnd:
                        {
                            here++;
                            break;
                        }
                        case LexTokenType.DocumentStart:
                        {
                            AddToken(ParseTokenType.DocStart, null, here);
                            Push(ParseState.Doc, -1);
                            here++;
                            break;
                        }
                        case LexTokenType.Text:
                        case LexTokenType.Dash:
                        {
                            AddToken(ParseTokenType.DocStart, null, here);
                            Push(ParseState.Doc, -1);
                            break;
                        }
                        default: throw ParseFailed($"unexpected peek token {peek} for op {op}");
                    }
                    break;
                }
                case ParseState.Doc:
                {
                    switch (peek)
                    {
                        case LexTokenType.End:
                        case LexTokenType.DocumentEnd:
                        {
                            Pop();
                            break;
                        }
                        case LexTokenType.Text:
                        {
                            here = ProcessText(lex, indent, left);
                            break;
                        }
                        case LexTokenType.Dash:
                        {
                            AddToken(ParseTokenType.SeqStart, null, left);
                            Push(ParseState.Sequence, indent);
                            break;
                        }
                        default: throw ParseFailed($"unexpected peek token {peek} for op {op}");
                    }
                    break;
                }
                case ParseState.Map:
                {
                    switch (peek)
                    {
                        case LexTokenType.End:
                        case LexTokenType.DocumentEnd:
                        {
                            Pop();
                            break;
                        }
                        case LexTokenType.Text:
                        {
                            here = ProcessText(lex, indent, left);
                            break;
                        }
                        default: throw ParseFailed($"map: unexpected peek token {peek}");
                    }
                    break;
                }
                case ParseState.Sequence:
                {
                    switch (peek)
                    {
                        case LexTokenType.End:
                        {
                            Pop();
                            break;
                        }
                        case LexTokenType.Dash:
                        {
                            var newIndent = indent + 1; // track nested sequence indent
                            var left1 = left + 1;
                        repeat:
                            var peek1 = PeekAt(lex, left1);
                            if (peek1 == LexTokenType.Spaces) { newIndent += lex[left1].Value.Length; left1++; goto repeat; }
                            if (peek1 == LexTokenType.Text) { here = ProcessText(lex, indent, left1); break; }
                            if (peek1 == LexTokenType.Line) { here = left1 + 1; AddToken(ParseTokenType.Scalar, null, left1); break; }
                            if (peek1 == LexTokenType.Dash)
                            {
                                AddToken(ParseTokenType.SeqStart, null, left1);
                                Push(ParseState.Sequence, newIndent);
                                left1++;
                                goto repeat;
                            }
                            throw ParseFailed($"sequence: unexpected peek token {peek1}");
                        }
                        case LexTokenType.Text:
                        {
                            here = ProcessText(lex, indent, left);
                            break;
                        }
                        default: throw ParseFailed($"sequence: unexpected peek token {peek}");
                    }
                    break;
                }
                default: throw ParseFailed($"unexpected op {op}");
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////

        // text in a node can be scalar or map start
        int ProcessText(ReadOnlySpan<LexToken> lex, int indent, int left)
        {
            // scalar / mapping / end
            var didSeeNewLines = 0;
            var didSeeSpaces = 0;
            var didSeeNewLineBeforeText = false;
            var scalarValue = lex[left].Value;
            var there = left + 1;
            while (true)
            {
                var peek1 = PeekAt(lex, there);
                if (peek1 == LexTokenType.Spaces)
                {
                    didSeeSpaces += lex[there].Value.Length;
                    there++;
                    continue;
                }
                if (peek1 == LexTokenType.Text)
                {
                    if (didSeeNewLineBeforeText) { didSeeNewLineBeforeText = false; scalarValue += ' '; }
                    if (didSeeSpaces > 0) { scalarValue += new String(' ', didSeeSpaces); didSeeSpaces = 0; }
                    scalarValue += lex[there].Value; there++; continue;
                }
                if (peek1 == LexTokenType.End)
                {
                    AddToken(ParseTokenType.Scalar, scalarValue, left);
                    return there;
                }
                if (peek1 == LexTokenType.Line)
                {
                    didSeeSpaces = 0; // ignore spaces at the end of the line
                    didSeeNewLineBeforeText = true;

                    var lineStart = there++;

                    peek1 = PeekAt(lex, there);
                    var newIndent = 0;
                    if (peek1 == LexTokenType.Indent)
                    {
                        newIndent = lex[there].Length;
                        there++; // skip indent
                    }
                    if (newIndent <= minIndent)
                    {
                        AddToken(ParseTokenType.Scalar, scalarValue, left);
                        return lineStart;
                    }
                    if (newIndent < indent)
                    {
                        throw ParseFailed($"unexpected line indent in ProcessText for op {op}, newIndent={newIndent} indent={indent} minIndent={minIndent} at {there}");
                    }
                    if (newIndent > indent)
                    {
                        // Increased indentation adds the difference to the scalar
                        scalarValue += new String(' ', newIndent - indent);
                    }

                    // Subsequent lines have different processing
                    didSeeNewLines++;
                    continue;
                }
                if (peek1 == LexTokenType.Map)
                {
                    if (didSeeNewLines != 0) { throw ParseFailed($"unexpected map start after new line in ProcessText for op {op} at {there}"); }

                    if (indent > minIndent)
                    {
                        AddToken(ParseTokenType.MapStart, null, left);
                        Push(ParseState.Map, indent);
                    }
                    else if (indent == minIndent && op != ParseState.Map)
                    {
                        throw ParseFailed($"unexpected map start in ProcessText for op {op} at {there}");
                    }
                    AddToken(ParseTokenType.Scalar, scalarValue, left);

                    // Find text or new line
                    there += 1;
                    while (true)
                    {
                        peek1 = PeekAt(lex, there);
                        if (peek1 == LexTokenType.Spaces) { there++; continue; } // ignore spaces after colon
                        if (peek1 == LexTokenType.Text) { return ProcessText(lex, indent, there); } // TODO
                        if (peek1 == LexTokenType.Line) { return there + 1; } // main loop will process the line
                        throw ParseFailed($"unexpected peek token in ProcessText {peek1} for op {op} after colon");
                    }
                }
                throw ParseFailed($"unexpected peek token {peek1} for op {op} after text");
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////

        void AddToken(ParseTokenType type, String? value, int start)
        {
            tokens.Add(new(type, value, start));
        }

        LexTokenType PeekAt(ReadOnlySpan<LexToken> lex, int i) => i >= lexLength ? LexTokenType.End : lex[i].Type;

        (LexTokenType, int, int) PeekStartLine(ReadOnlySpan<LexToken> lexTokens, int where)
        {
            var start = where;
        restart:
            var peek = PeekAt(lexTokens, start);
            if (peek == LexTokenType.End) { return (peek, start, -1); }
            if (peek == LexTokenType.Text) { return (peek, start, 0); }
            if (peek == LexTokenType.Dash) { return (peek, start, 0); }
            if (peek == LexTokenType.DocumentStart) { return (peek, start, 0); }
            if (peek == LexTokenType.DocumentEnd) { return (peek, start, 0); }
            if (peek == LexTokenType.Line) { start++; goto restart; } // skip empty lines
            if (peek == LexTokenType.Indent)
            {
                var peek2 = PeekAt(lexTokens, start + 1);
                if (peek2 == LexTokenType.Line) { start += 2; goto restart; } // skip empty lines
                return (peek2, start + 1, lexTokens[start].Length);
            }
            throw ParseFailed($"PeekStartLine: unexpected peek token {peek}");
        }

        Exception ParseFailed(String message, int? where = null) => new($"PARSE FAIL at {where ?? here}: {message}\n{String.Join("\n", tokens.Select(i => i.Type))}");

        void Push(ParseState nextOp, int parentMinIndent)
        {
            var was = op;

            if (depth >= MAX_DEPTH) { throw new InvalidOperationException("Stack overflow"); }
            opStack[depth] = op;
            inStack[depth] = minIndent;
            depth++;

            op = nextOp;
            minIndent = parentMinIndent;
        }

        void Pop()
        {
            var was = op;
            switch (op)
            {
                case ParseState.Doc:
                    tokens.Add(new(ParseTokenType.DocEnd, null, here));
                    break;
                case ParseState.Map:
                    tokens.Add(new(ParseTokenType.MapEnd, null, here));
                    break;
                case ParseState.Sequence:
                    tokens.Add(new(ParseTokenType.SeqEnd, null, here));
                    break;
                default: throw ParseFailed($"unexpected op {op}");
            }

            if (depth <= 0) { throw new InvalidOperationException("Stack underflow"); }
            depth--;
            op = opStack[depth];
            minIndent = inStack[depth];
        }
    }
}
