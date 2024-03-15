using System.Security.Cryptography.X509Certificates;

namespace Pingmint.Yaml;

public struct Run
{
    public readonly int Start;
    public readonly int Length;

    public Run(int start, int length = 0)
    {
        Start = start;
        Length = length;
    }

    public static implicit operator Run(Range range) => new Run(range.Start.Value, range.End.Value - range.Start.Value);

    public Boolean IsEmpty => Length == 0;
    public Int32 End => Start + Length;

    public static Run Empty => new Run(0, 0);
}

public enum LexTokenType : int
{
    End = 0,
    DocumentStart,
    DocumentEnd,
    Indent,
    Spaces,
    Line,
    Colon,
    Dash,
    Text,
}

public struct LexToken
{
    public LexTokenType Type;
    public int Start;
    public int Length;
    public int Column;
    public String Value;

    public LexToken(LexTokenType type, int start, int length, String value, int column)
    {
        Type = type;
        Start = start;
        Length = length;
        Value = value;
        Column = column;
    }

    public String Name => Type switch
    {
        LexTokenType.End => "end",
        LexTokenType.Indent => "-->",
        LexTokenType.Spaces => "<->",
        LexTokenType.Line => "line",
        LexTokenType.Colon => "map",
        LexTokenType.Dash => "dash",
        LexTokenType.Text => "text",
        LexTokenType.DocumentStart => "doc+",
        _ => throw new NotImplementedException($"unexpected token: {Type}")
    };
}

public enum NodeType : int
{
    End,
    Dedent,
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

public struct Node
{
    public NodeType Type;
    public Run Run;
    public String? Value;

    public Node(NodeType type, Run run, String? value)
    {
        Type = type;
        Run = run;
        Value = value;
    }

    public Node(NodeType type, Run run)
    {
        Type = type;
        Run = run;
        Value = null;
    }

    public Node(NodeType type, int position)
    {
        Type = type;
        Run = new Run(position, 0);
        Value = null;
    }

    public int Length => Value?.Length ?? 0;

    public static Node End(Run run) => new(NodeType.End, run);
    public static Node Dedent(Run run) => new(NodeType.Dedent, run);

    public String Name => Type switch
    {
        NodeType.StreamStart => "str+",
        NodeType.StreamEnd => "str-",
        NodeType.DocStart => "doc+",
        NodeType.DocEnd => "doc-",
        NodeType.MapStart => "map+",
        NodeType.MapEnd => "map-",
        NodeType.SeqStart => "seq+",
        NodeType.SeqEnd => "seq-",
        NodeType.Scalar => "text",
        _ => throw new NotImplementedException($"unexpected token: {Type}")
    };
}

public enum ParseState : int
{
    Stream = 0,
    Doc,
    Mappings,
    MapValue,
    Sequence,
    SequenceItem,
    Scalar,
}
public static class Parser
{
    public static ReadOnlySpan<LexToken> Lex(ReadOnlySpan<Char> yaml)
    {
        var tokens = new List<LexToken>();
        var here = 0;
        var col = 0;

        // HACK: injecting indent at start of document
        if (yaml[0] == ' ') throw new NotImplementedException("First char cannot be a space"); // TODO
        tokens.Add(new LexToken(LexTokenType.Indent, 0, 0, "", 0));

        while (here < yaml.Length)
        {
            var c = yaml[here];
            Console.WriteLine($"LexLoop: {here} - col={col}: {c} {(int)c}");
            if (c == '-')
            {
                if (here + 2 < yaml.Length && yaml[here + 1] == '-' && yaml[here + 2] == '-')
                {
                    tokens.Add(new LexToken(LexTokenType.DocumentStart, here, 3, "---", col));
                    here += 3;
                    col += 3;
                    continue;
                }
                tokens.Add(new LexToken(LexTokenType.Dash, here, 1, "-", col));
                here++;
                col++;
                continue;
            }

            if (c == ' ')
            {
                var start2 = here;
                var myCol = col;
                here++;
                col++;
                while (here < yaml.Length && yaml[here] == ' ')
                {
                    here++;
                    col++;
                }
                tokens.Add(new LexToken(LexTokenType.Spaces, start2, here - start2, yaml[start2..here].ToString(), myCol));
                continue;
            }

            if (c == '\r')
            {
                if (here + 1 >= yaml.Length)
                {
                    throw new InvalidOperationException($"Unexpected char: {c} at position {here}");
                }
                here++;
                // skip col update
                c = yaml[here];
                if (yaml[here] != '\n') { throw new InvalidOperationException($"Unexpected char: {c} at position {here}"); }
            }

            if (c == '\n')
            {
                tokens.Add(new LexToken(LexTokenType.Line, here, 1, "\n", col));
                here++;
                col = 0;
                if (here < yaml.Length)
                {
                    if (yaml[here] == ' ')
                    {
                        var start2 = here;
                        here++; // after 1st space
                        while (here < yaml.Length && yaml[here] == ' ')
                        {
                            here++;
                        }
                        var len = here - start2;
                        tokens.Add(new LexToken(LexTokenType.Indent, start2, here - start2, yaml[start2..here].ToString(), 0));
                        col = len;
                        continue;
                    }
                    else
                    {
                        tokens.Add(new LexToken(LexTokenType.Indent, here, 0, "", 0));
                    }
                }
                continue;
            }

            if (c == ':')
            {
                Console.WriteLine($"Colon: {here} - col={col}");
                tokens.Add(new LexToken(LexTokenType.Colon, here, 1, ":", col));
                here++;
                col++;
                continue;
            }

            // TODO: ignoring comments for now
            if (c == '#')
            {
                while (here < yaml.Length)
                {
                    if (yaml[here] == '\r' || yaml[here] == '\n')
                    {
                        col = 0;
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
                    tokens.Add(new LexToken(LexTokenType.DocumentEnd, here, 3, "...", col));
                    here += 3;
                    col += 3;
                    continue;
                }
            }

            var start = here;
            var textCol = col;
            while (here < yaml.Length)
            {
                // TODO: this is likely wrong when the string contains lexer tokens
                var c2 = yaml[here];
                if (Char.IsLetterOrDigit(c2)) { }
                else if (c2 == ' ') { break; }
                else if (c2 == '\r') { break; }
                else if (c2 == '\n') { break; }
                else if (c2 == ':') { break; }
                else if (c2 == '-') { break; }
                else if (c2 == '#') { break; }
                here++;
                col++;
            }

            Console.WriteLine($"Text: {start} - {here} col({textCol}-{col}) = {yaml[start..here]}");
            tokens.Add(new LexToken(LexTokenType.Text, start, here - start, yaml[start..here].ToString(), textCol));
        }
        return tokens.ToArray();
    }

    public static List<Node> Parse(ReadOnlySpan<LexToken> lex)
    {
        var debug = true;
        var lexLength = lex.Length;
        var tokens = new List<Node>();
        try
        {
            ParseStream(0, lex, 0, -1);
        }
        catch
        {
            Console.WriteLine($"Parse failed, tokens: {tokens.Count}");
            foreach (var token in tokens)
            {
                Console.WriteLine($"{token.Name} {token.Run.Start,4} {token.Run.Length,3} => {token.Value}");
            }
            throw;
        }

        return tokens;

        void AddToken(Node node) => tokens.Add(node);
        LexTokenType Peek(ReadOnlySpan<LexToken> lex, int i) => i >= lexLength ? LexTokenType.End : lex[i].Type;
        NotImplementedException UnexpectedToken(LexTokenType token, int i) => new($"Unexpected token: {token} at {i}");
        NotImplementedException UnexpectedNode(Node node, String? message = null) => new($"Unexpected node: {node.Type} at {node.Run.Start}{(message is null ? "" : $" - {message}")}");
        NotImplementedException NoProgress(int i, String? message = null) => new($"No progress: at {i}{(message is null ? "" : $" - {message}")}");
        NotImplementedException InfiniteLoop(int i) => new($"Infinite loop at {i}");
        void Debug(String message, int indent = 0)
        {
            if (!debug) return;
            Console.Write(new String(' ', indent * 2));
            Console.WriteLine($"{message}");
        }

        bool IsNextTokenColon(ReadOnlySpan<LexToken> lex, int i) => ScanForColon(lex, i) is not null;
        int? ScanForColon(ReadOnlySpan<LexToken> lex, int i)
        {
            while (true)
            {
                var peek = Peek(lex, i);

                // Found
                if (peek == LexTokenType.Colon) { return i; }

                // Found something else
                if (peek == LexTokenType.End) { return null; }
                if (peek == LexTokenType.Text) { return null; }
                if (peek == LexTokenType.DocumentEnd) { return null; }
                if (peek == LexTokenType.DocumentStart) { return null; }
                if (peek == LexTokenType.Dash) { return null; }

                // Keep scanning
                if (peek == LexTokenType.Spaces) { i++; continue; }
                if (peek == LexTokenType.Line) { i++; continue; }
                if (peek == LexTokenType.Indent) { i++; continue; }

                throw UnexpectedToken(peek, i);
            }
        }

        Node ParseStream(int depth, ReadOnlySpan<LexToken> lex, int start, int min_indent)
        {
            depth++;
            Debug($"ParseStream: min={min_indent}", depth);
            var line_indent = 0;
            var here = start;

            AddToken(new(NodeType.StreamStart, start));

            Node left;
            while (true)
            {
                left = ScanLeafNode(depth, lex, here, min_indent);
                Debug($"ParseStream found: {left.Type}", depth);

                // done
                if (left.Type is NodeType.End) { break; }

                left = ParseDocument(depth, lex, here, min_indent, ref line_indent);
                if (left.Type == NodeType.DocEnd) { here = left.Run.End; continue; }

                // unexpected
                throw NoProgress(here, $"ParseDocument returned {left.Type}");
            }

            left = new(NodeType.StreamEnd, here);
            AddToken(left);
            return left;
        }

        Node ParseDocument(int depth, ReadOnlySpan<LexToken> lex, int start, int min_indent, ref int line_indent)
        {
            depth++;
            Debug($"ParseDocument: min={min_indent}", depth);

            // Scan for any valid document indicator: doc+ / seq+ / text
            var left = ParseLeafNode(depth, lex, start, min_indent, ref line_indent);

            // Found 
            var here = left.Run.End;
            if (left.Type == NodeType.DocStart)
            {
                Debug($"Found doc+ at {start} ", depth);
                AddToken(new(NodeType.DocStart, start));
            }
            else
            {
                Debug($"Found implicit doc+ at {start} ", depth);
                AddToken(new(NodeType.DocStart, start));
                here = start; // rewind
            }

            while (true)
            {
                Debug($"ParseDocument loop at {here}", depth);
                var peek = Peek(lex, here);
                if (peek is LexTokenType.End)
                {
                    // implicit doc-
                    Debug($"Found implicit doc- at {here} ", depth);
                    left = new(NodeType.DocEnd, here);
                    AddToken(left);
                    break;
                }

                left = ParseLeafNode(depth, lex, here, min_indent, ref line_indent);
                if (left.Type == NodeType.DocEnd)
                {
                    // explicit doc-
                    Debug($"Found doc- at {here} ", depth);
                    AddToken(left);
                    break;
                }
                if (left.Type is NodeType.End)
                {
                    // explicit doc-
                    Debug($"Found implicit doc- at {here} ", depth);
                    left = new(NodeType.DocEnd, here);
                    AddToken(left);
                    break;
                }
                if (left.Type == NodeType.Scalar)
                {
                    // Detect mapping
                    if (!IsNextTokenColon(lex, left.Run.End)) { AddToken(left); }
                    else
                    {
                        Debug($"Found map at {here}", depth);
                        left = ParseMappings(depth, lex, here, min_indent, ref line_indent);
                        if (left.Type != NodeType.MapEnd) { throw UnexpectedNode(left, $"ParseMappings should return MapEnd"); }
                    }
                    here = left.Run.End;
                    continue;
                }
                if (left.Type == NodeType.SeqStart)
                {
                    Debug($"Found seq+ at {here}", depth);
                    left = ParseSequence(depth, lex, here, min_indent, ref line_indent);
                    if (left.Type != NodeType.SeqEnd) { throw UnexpectedNode(left, $"ParseSequence should return MapEnd"); }
                    here = left.Run.End;
                    continue;
                }

                throw UnexpectedNode(left, $"Document type unexpected.");
            }

            Debug($"ParseDocument return {left.Type} -> {left.Run.End}", depth);
            return left;
        }

        Node ParseSequence(int depth, ReadOnlySpan<LexToken> lex, int start, int min_indent, ref int line_indent)
        {
            depth++;
            Debug($"ParseSequence: min={min_indent}", depth);

            AddToken(new(NodeType.SeqStart, start));
            var here = start;

            Node left;
            while (true)
            {
                left = ParseLeafNode(depth, lex, here, min_indent, ref line_indent);
                if (left.Type is NodeType.End or NodeType.Dedent) { break; }
                else if (left.Type != NodeType.SeqStart) { throw UnexpectedNode(left, $"Expected SeqStart"); }

                var item_indent = line_indent;
                here = left.Run.End;

                left = ParseLeafNode(depth, lex, here, item_indent, ref line_indent);
                if (left.Type is NodeType.End)
                {
                    Debug("No sequence item node", depth);
                    AddToken(new(NodeType.Scalar, here..here, null));
                    break;
                }
                if (left.Type is NodeType.Dedent)
                {
                    Debug("No sequence item node", depth);
                    AddToken(new(NodeType.Scalar, here..here, null));

                    var left2 = ScanLeafNode(depth, lex, here, min_indent);
                    if (left2.Type is NodeType.SeqStart) { continue; }
                    break;
                }

                if (left.Type == NodeType.Scalar)
                {
                    // Detect mapping
                    if (!IsNextTokenColon(lex, left.Run.End)) { AddToken(left); }
                    else
                    {
                        Debug($"Found map at {here}", depth);
                        left = ParseMappings(depth, lex, here, item_indent, ref line_indent);
                        if (left.Type != NodeType.MapEnd) { throw UnexpectedNode(left, $"ParseMappings should return MapEnd"); }
                    }
                    here = left.Run.End;
                    continue;
                }

                // TODO: indents are wrong if multiple sequences start on same line
                if (left.Type == NodeType.SeqStart)
                {
                    Debug($"Found seq+ at {here}", depth);
                    left = ParseSequence(depth, lex, here, item_indent, ref line_indent);
                    if (left.Type != NodeType.SeqEnd) { throw UnexpectedNode(left, $"ParseSequence should return SeqEnd"); }
                    here = left.Run.End;
                    continue;
                }

                throw UnexpectedNode(left, $"Sequence item type unexpected.");
            }

            left = new(NodeType.SeqEnd, left.Run);
            AddToken(left);
            return left;
        }

        Node ParseMappings(int depth, ReadOnlySpan<LexToken> lex, int start, int min_indent, ref int line_indent)
        {
            depth++;
            Debug($"ParseMappings: min={min_indent}", depth);

            AddToken(new(NodeType.MapStart, start));

            var here = start;

            while (true)
            {
                // Key
                var left = ParseLeafNode(depth, lex, here, min_indent, ref line_indent);
                switch (left.Type)
                {
                    // keys
                    case NodeType.Scalar: break; // break switch

                    // non-keys
                    case NodeType.End:
                    case NodeType.Dedent:
                    case NodeType.DocEnd:
                        goto done; // break while

                    default: throw UnexpectedNode(left, $"Mapping key type unexpected.");
                }
                AddToken(left);

                // TODO: TEMPORARY MIN_INDENT IS SET TO LEADING OF KEY
                if (line_indent <= min_indent) { throw new InvalidOperationException("Value indent must be greater than key indent."); }
                var value_indent = line_indent;

                // Advance to value
                here = left.Run.End;
                if (ScanForColon(lex, here) is not { } colon) { throw UnexpectedToken(Peek(lex, here), here); }
                here = colon + 1;

                // Scan for any valid value indicator: seq+ / text
                left = ParseLeafNode(depth, lex, here, value_indent, ref line_indent);
                switch (left.Type)
                {
                    case NodeType.Scalar:
                    {
                        // Detect mapping
                        if (!IsNextTokenColon(lex, left.Run.End)) { AddToken(left); break; }
                        else
                        {
                            Debug($"Found map at {here}", depth);
                            left = ParseMappings(depth, lex, here, value_indent, ref line_indent);
                            if (left.Type != NodeType.MapEnd) { throw UnexpectedNode(left, $"ParseMappings should return MapEnd"); }
                        }
                        break;
                    }
                    case NodeType.SeqStart:
                    {

                        Debug($"Found seq+ at {here}", depth);
                        left = ParseSequence(depth, lex, here, value_indent, ref line_indent);
                        if (left.Type != NodeType.SeqEnd) { throw UnexpectedNode(left, $"ParseSequence should return SeqEnd"); }
                        here = left.Run.End;
                        break;
                    }
                    default: throw UnexpectedNode(left, $"Mapping value type unexpected.");
                }
                here = left.Run.End;
            }
        done:

            Node end = new(NodeType.MapEnd, here);
            AddToken(end);
            Debug($"ParseMappings done at {here}", depth);
            return end;
        }

        Node ScanLeafNode(int depth, ReadOnlySpan<LexToken> lex, int start, int min_indent)
        {
            int ignore = 0;
            return ParseLeafNode(depth, lex, start, min_indent, ref ignore);
        }
        Node ParseLeafNode(int depth, ReadOnlySpan<LexToken> lex, int start, int min_indent, ref int line_indent)
        {
            // TODO: Make sure all nodes have correct start and end positions for Run.
            // End must be where parsing resumes.

            depth++;
            Debug($"ParseLeaf: start={start} min={min_indent}", depth);

            // Find start of scalar, another node, or return empty
            var here = start;
            while (true)
            {
                var peek = Peek(lex, here);

                // Start of scalar
                if (peek == LexTokenType.Text) { break; }

                // Marker tokens
                if (peek == LexTokenType.End) { return Node.End(start..here); }
                if (peek == LexTokenType.DocumentStart) { return new(NodeType.DocStart, here..(here + 1)); }
                if (peek == LexTokenType.DocumentEnd) { return new(NodeType.DocEnd, here..(here + 1)); }
                if (peek == LexTokenType.Dash) { return new(NodeType.SeqStart, here..(here + 1)); }

                // Ignore initial whitespace
                if (peek == LexTokenType.Spaces) { line_indent += lex[here].Length; here++; continue; }
                if (peek == LexTokenType.Line)
                {
                    var here1 = here + 1; // after line
                    var peek1 = Peek(lex, here1);
                    if (peek1 == LexTokenType.Indent) { here = here1; line_indent += lex[here1].Length; continue; }
                    else if (peek1 == LexTokenType.End) { return Node.End(start..here); }

                    throw UnexpectedToken(peek1, here1);
                }
                if (peek == LexTokenType.Indent)
                {
                    var indent = lex[here].Length;

                    var here1 = here + 1;
                    var peek1 = Peek(lex, here1);
                    if (peek1 is LexTokenType.Text or LexTokenType.Dash or LexTokenType.Colon)
                    {
                        if (indent <= min_indent)
                        {
                            return Node.Dedent(start..here);
                        }
                        here = here1; // at first token of line
                        line_indent = indent;
                        continue;
                    }

                    if (peek1 is LexTokenType.Line) { here = here1; line_indent = 0; continue; }

                    line_indent = indent;
                    if (peek1 is LexTokenType.DocumentStart) { return new(NodeType.DocStart, here..(here1 + 1)); }
                    else if (peek1 is LexTokenType.DocumentEnd) { return new(NodeType.DocEnd, here..(here1 + 1)); }
                    else if (peek1 is LexTokenType.End) { return Node.End(start..here1); }
                    else { throw UnexpectedToken(peek1, here1); }
                }

                // TODO: update leading if text starts on subsequent line

                throw UnexpectedToken(peek, start);
            }

            // Find end of scalar
            var end = here;
            end++; // after first text token
            while (true)
            {
                var peek = Peek(lex, end);
                Debug($"Scalar peek: {peek} at {end}", depth);

                if (peek == LexTokenType.End) { break; }
                if (peek == LexTokenType.DocumentEnd) { break; }
                if (peek == LexTokenType.DocumentStart) { break; }
                if (peek == LexTokenType.Dash) { break; }
                if (peek == LexTokenType.Colon) { break; }

                // Scalars consist of text and whitespace
                if (peek == LexTokenType.Text) { end++; continue; }
                if (peek == LexTokenType.Spaces) { end++; continue; }
                if (peek == LexTokenType.Line) { end++; continue; }
                if (peek == LexTokenType.Indent)
                {
                    var indent = lex[end].Length;
                    if (indent <= min_indent) { break; }
                    end++;
                    continue;
                }
            }

            // FIXME: YAML string processing
            String scalarValue = "";
            for (int i = start; i < end; i++)
            {
                if (lex[i].Type == LexTokenType.Text)
                {
                    scalarValue += lex[i].Value;
                }
                else
                {
                    scalarValue += " ";
                }
            }

            Debug($"ParseLeaf done: {start} -> ({here} - {end}) = {scalarValue}", depth);
            Node node = new(NodeType.Scalar, start..end, scalarValue);
            return node;
        }
    }
}
