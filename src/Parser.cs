namespace Pingmint.Yaml;

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
        while (here < yaml.Length)
        {
            var c = yaml[here];
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

            tokens.Add(new LexToken(LexTokenType.Text, start, here - start, yaml[start..here].ToString(), textCol));
        }
        return tokens.ToArray();
    }

    public static List<ParseToken> Parse(ReadOnlySpan<LexToken> lex)
    {
        var debug = true;

        var lexLength = lex.Length;
        var tokens = new List<ParseToken>();

        var op = ParseState.Stream; // op means "inside of"
        var minIndent = -1;
        var here = 0;
        var indent = 0;

        const int MAX_DEPTH = 32;
        var depth = 0;
        var opStack = new ParseState[MAX_DEPTH];
        var inStack = new Int32[MAX_DEPTH];

        // State: Scalar
        String? scalarValue = null;
        Boolean scalarLines = false;

        var cheat = 0; // TODO: this is likely not tracked properly

        AddToken(ParseTokenType.StreamStart, null, here);
        while (true)
        {
            var peek = Peek(lex, here);
            Debug($"=== LOOP {here}={peek} {op} ===");
            if (op == ParseState.Stream)
            {
                if (peek == LexTokenType.End) { AddToken(ParseTokenType.StreamEnd, null, here); break; } // end stream is the final token
                if (peek == LexTokenType.Line) { here++; continue; } // ignore leading lines
                if (peek == LexTokenType.DocumentStart)
                {
                    if (indent != 0) { throw Failed($"Document start token must not be indented, found: {indent}."); }
                    AddToken(ParseTokenType.DocStart, null, here);
                    Push(ParseState.Doc, -1); // document never escapes due to dedent
                    here++; // after document start
                    continue;
                }
                if (peek == LexTokenType.Indent) { AdvanceIndent(lex); continue; }
                if (peek == LexTokenType.Text || peek == LexTokenType.Dash)
                {
                    AddToken(ParseTokenType.DocStart, null, here);
                    Push(ParseState.Doc, -1); // document never escapes due to dedent
                    continue;
                }
            }
            if (op == ParseState.Doc)
            {
                if (peek == LexTokenType.End)
                {
                    AddToken(ParseTokenType.DocEnd, null, here);
                    Pop();
                    continue;
                }
                if (peek == LexTokenType.DocumentEnd)
                {
                    AddToken(ParseTokenType.DocEnd, null, here);
                    Pop();
                    here++; // advance after document end
                    continue;
                }
                if (peek == LexTokenType.Indent) { AdvanceIndent(lex); continue; }
                if (peek == LexTokenType.Line) { here++; continue; } // ignore leading lines
                if (peek == LexTokenType.Text) // document scalar / map key
                {
                    Push(ParseState.Scalar, -1);  // document scalar never escapes due to dedent
                    SetCheat(lex[here].Column);
                    NewScalar(lex[here].Value);
                    here++;
                    continue;
                }
                if (peek == LexTokenType.Dash)
                {
                    var sequenceMinIndent = indent - 1; // TODO: any indentation less than the dash?
                    AddToken(ParseTokenType.SeqStart, null, here);
                    Push(ParseState.Sequence, sequenceMinIndent); // sequence continues until dedent from parent indentation
                    Push(ParseState.SequenceItem, indent); // item must be indented more than the dash
                    NewScalar();
                    here++;
                    continue;
                }
            }
            if (op == ParseState.Scalar)
            {
                if (peek == LexTokenType.End)
                {
                    AddToken(ParseTokenType.Scalar, scalarValue, here);
                    NewScalar(); // TODO: make obsolete
                    Pop();
                    continue;
                }
                if (peek == LexTokenType.Line) { HandleLineForScalar(lex); continue; } // TODO: inline, should be the only caller
                if (peek == LexTokenType.Indent)
                {
                    SetIndent(lex[here].Length);
                    if (indent <= minIndent) { throw Failed($"Unexpected scalar dedent."); } // Line handler should have peeked through indentation
                    here++; // after indent
                    continue;
                }
                if (peek == LexTokenType.Colon)
                {
                    if (scalarLines) { throw Failed($"Map key cannot span multiple lines: {scalarValue}."); }
                    AddToken(ParseTokenType.MapStart, null, here); // TODO: actual start is the scalar start, not here
                    AddToken(ParseTokenType.Scalar, scalarValue, here); // map key // TODO: actual start is the scalar start, not here
                    Swap(ParseState.Mappings); // scalar -> map key

                    var mapValueIndent = GetCheat() /*indent*/; // TODO: this should be the column of the map key (are we just lucky?)
                    Push(ParseState.MapValue, mapValueIndent); // map value must be indented more than current indent
                    NewScalar(); // TODO: make obsolete
                    here++; // advance after colon
                    continue;
                }
                if (peek == LexTokenType.Text)
                {
                    if (scalarValue is not null) { AddScalar(lex[here].Value); }
                    else
                    {
                        Debug($"Scalar starts at column {lex[here].Column}");
                        SetCheat(lex[here].Column);
                        SetScalar(lex[here].Value);
                    }

                    here++; // after text
                    continue;
                }
            }
            if (op == ParseState.Mappings)
            {
                if (peek == LexTokenType.End) { AddToken(ParseTokenType.MapEnd, null, here); Pop(); continue; }
                if (peek == LexTokenType.DocumentEnd) { AddToken(ParseTokenType.MapEnd, null, here); Pop(); continue; }
                if (peek == LexTokenType.Indent)
                {
                    if (AdvanceIndentIfEmptyLine(lex)) { continue; } // avoid escaping scope for empty lines without indentation

                    SetIndent(lex[here].Length);
                    if (indent <= minIndent)
                    {
                        AssertNoScalar();
                        AddToken(ParseTokenType.MapEnd, null, here);
                        Pop();
                        continue;
                    }
                    here++; // after indent
                    continue;
                }
                if (peek == LexTokenType.Line) { AssertNoScalar(); here++; continue; }
                if (peek == LexTokenType.Colon)
                {
                    // TODO: OBSOLETE. This should have been moved to the scalar op
                    if (scalarLines) { throw Failed($"Map key cannot span multiple lines: {scalarValue}."); }
                    AddToken(ParseTokenType.Scalar, scalarValue, here); // map key
                    Push(ParseState.MapValue, indent); // map value must be indented more than current indent
                    NewScalar();
                    here++; // advance after colon
                    continue;
                }
                if (peek == LexTokenType.Text)
                {
                    // TODO: OBSOLETE. This should cause the op to swap to scalar
                    if (scalarValue is null)
                    {
                        Debug($"Map key starts at column {lex[here].Column}");
                        SetCheat(lex[here].Column);
                        SetScalar(lex[here].Value);
                    }
                    else
                    {
                        AddScalar(lex[here].Value);
                    }
                    here++; // after text
                    continue;
                }
            }
            if (op == ParseState.MapValue)
            {
                if (peek == LexTokenType.End) { AddToken(ParseTokenType.Scalar, scalarValue, here); Pop(); continue; }
                if (peek == LexTokenType.DocumentEnd)
                {
                    // TODO: OBSOLETE. SCALAR PROCESSING ELSEWHERE
                    // TODO: AFTER MIGRATION, THIS ADDS NULL MAP VALUE INSTEAD
                    AddToken(ParseTokenType.Scalar, scalarValue, here); // TODO: here=scalarStart
                    Pop();
                    continue;
                }
                if (peek == LexTokenType.Indent)
                {
                    // TODO: OBSOLETE. SCALAR PROCESSING ELSEWHERE
                    SetIndent(lex[here].Length);
                    if (indent <= minIndent)
                    {
                        // TODO: AFTER MIGRATION, THIS ADDS NULL MAP VALUE INSTEAD
                        // Scalar should already have been handled
                        AddToken(ParseTokenType.Scalar, scalarValue, here); // TODO: here=scalarStart
                        NewScalar();
                        Pop();
                        continue;
                    }

                    here++; // after indent
                    continue;
                }
                if (peek == LexTokenType.Colon)
                {
                    // TODO: OBSOLETE. SCALAR PROCESSING ELSEWHERE
                    // Current mapping is nested, so scalar is a new map key
                    if (scalarLines) { throw Failed($"Map key cannot span multiple lines: {scalarValue}."); }
                    AddToken(ParseTokenType.MapStart, null, here);
                    AddToken(ParseTokenType.Scalar, scalarValue, here); // map key
                    Swap(ParseState.Mappings); // map value -> map key
                    Push(ParseState.MapValue, indent); // map value must be indented more than current indent
                    NewScalar();
                    here++; // advance after colon
                    continue;
                }
                if (peek == LexTokenType.Line)
                {
                    // TODO: OBSOLETE. SCALAR PROCESSING ELSEWHERE
                    HandleLineForScalar(lex); // TODO: swap to scalar op
                    continue;
                }
                if (peek == LexTokenType.Spaces)
                {
                    // TODO: OBSOLETE. SCALAR PROCESSING ELSEWHERE
                    HandlesSpacesForScalar(lex);
                    continue;
                }
                if (peek == LexTokenType.Text)
                {
                    // TODO: OBSOLETE. THIS CONVERTS TO SCALAR OP
                    var segment = lex[here].Value;
                    Debug($"Text: {segment}");
                    if (scalarValue is null)
                    {
                        Debug($"Map value starts at column {lex[here].Column}");
                        SetCheat(lex[here].Column);
                        SetScalar(segment);
                    }
                    else
                    {
                        AddScalar(segment);
                    }
                    here++; // after text
                    continue;
                }
                if (peek == LexTokenType.Dash)
                {
                    AssertNoScalar();
                    AddToken(ParseTokenType.SeqStart, null, here);
                    Swap(ParseState.Sequence); // map value -> sequence
                    Push(ParseState.SequenceItem, lex[here].Column); // item must be indented more than the dash
                    NewScalar(); // TODO: make obsolete
                    here++; // after the dash
                    continue;
                }
            }
            if (op == ParseState.Sequence)
            {
                if (peek == LexTokenType.End)
                {
                    AddToken(ParseTokenType.SeqEnd, null, here);
                    Pop();
                    continue;
                }
                if (peek == LexTokenType.Indent)
                {
                    if (AdvanceIndentIfEmptyLine(lex)) { continue; } // avoid escaping scope for empty lines without indentation
                    SetIndent(lex[here].Length);
                    if (indent <= minIndent) { AddToken(ParseTokenType.SeqEnd, null, here); Pop(); continue; }
                    here++; // after indent
                    continue;
                }
                if (peek == LexTokenType.Line)
                {
                    AssertNoScalar();
                    here++; // after line
                    continue;
                }
                if (peek == LexTokenType.Dash)
                {
                    var sequenceItemIndent = lex[here].Column; /* TODO: was: indent */;
                    Push(ParseState.SequenceItem, sequenceItemIndent); // item must be indented more than the dash
                    NewScalar(); // TODO: make obsolete
                    here++;
                    continue;
                }
            }

            // TODO: THIS IS WHERE YOU LEFT OFF
            if (op == ParseState.SequenceItem) // TODO: item should be temporary until a specific token is found
            {
                if (peek == LexTokenType.Line)
                {
                    HandleLineForScalar(lex); // TODO: swap to scalar op
                    continue;
                }
                if (peek == LexTokenType.Indent)
                {
                    SetIndent(lex[here].Length);
                    if (indent <= minIndent)
                    {
                        // Scalar should already have been handled
                        if (scalarValue is not null) { throw Failed($"TODO: SequenceItem scalar was not handled? text={scalarValue}."); }
                        NewScalar();
                        Pop();
                        continue;
                    }
                    here++; // after indent
                    continue;
                }
                if (peek == LexTokenType.Spaces)
                {
                    HandlesSpacesForScalar(lex);
                    continue;
                }
                if (peek == LexTokenType.Text)
                {
                    var segment = lex[here].Value;
                    Debug($"Text: {segment}");
                    if (scalarValue is null)
                    {
                        Debug($"Sequence item starts at column {lex[here].Column}");
                        SetCheat(lex[here].Column);
                        SetScalar(segment);
                    }
                    else
                    {
                        AddScalar(segment);
                    }
                    here++; // after text
                    continue;
                }
                if (peek == LexTokenType.Dash)
                {
                    // Nested sequence
                    var itemCol = lex[here].Column;
                    AddToken(ParseTokenType.SeqStart, null, here);
                    Swap(ParseState.Sequence); // sequence item -> sequence
                    Push(ParseState.SequenceItem, itemCol); // item must be indented more than the dash
                    NewScalar();
                    here++;
                    continue;
                }
                if (peek == LexTokenType.Colon)
                {
                    if (scalarValue is null) { throw Failed($"Sequence item found mapping without a key."); }
                    if (scalarLines) { throw Failed($"Map key cannot span multiple lines: {scalarValue}."); }
                    AddToken(ParseTokenType.MapStart, null, here);
                    AddToken(ParseTokenType.Scalar, scalarValue, here); // map key
                    Swap(ParseState.Mappings); // sequence item -> map key
                    Push(ParseState.MapValue, GetCheat()); // FAKE!!!!!!!!! // map value must be indented more than current indent
                    NewScalar();
                    here++; // after colon
                    continue;
                }
            }
            throw Failed($"Loop unhandled: op={op} peek={peek}.");
        }
        return tokens;

        ///////////////////////////////////////////////////////////////////////////////////////////////////
        ///
        void AssertNoScalar()
        {
            if (scalarValue is not null) { throw Failed($"Unexpected scalar: {scalarValue}."); }
        }
        void SetCheat(int value)
        {
            Debug($"Set cheat: {value}");
            cheat = value;
        }
        int GetCheat()
        {
            Debug($"Get cheat: {cheat}");
            return cheat;
        }
        void AdvanceIndent(ReadOnlySpan<LexToken> lex)
        {
            Debug("Advancing indent");
            var it = lex[here];
            if (it.Type != LexTokenType.Indent) { throw Failed($"Expected indent, found: {it.Type}."); }
            here++; // after indent
        }
        bool AdvanceIndentIfEmptyLine(ReadOnlySpan<LexToken> lex)
        {
            Debug("Advancing indent if empty line");

            var it = lex[here];
            if (it.Type != LexTokenType.Indent) { throw Failed($"Expected indent, found: {it.Type}."); }

            var peek1 = Peek(lex, here + 1);
            if (peek1 == LexTokenType.Line)
            {
                here += 2; // ignore blank lines
                return true;
            }
            return false;
        }
        void SetIndent(int newIndent)
        {
            Debug($"Set indent: {newIndent}");
            indent = newIndent;
        }

        void HandleLineForScalar(ReadOnlySpan<LexToken> lex)
        {
            Debug("Handle line for scalar");
            var peek = Peek(lex, here);
            if (peek != LexTokenType.Line) { throw Failed($"Expected line, found: {peek}."); }

            // Before appending line to scalar, check to see if the line starts a new node
            var here1 = here + 1;
            var peek1 = Peek(lex, here1);
            if (peek1 == LexTokenType.Indent) { SetIndent(lex[here1].Length); }
            else if (peek1 != LexTokenType.End) { throw Failed($"Expected indent, found: {peek1}."); }

            here++; // after line
            if (indent <= minIndent)
            {
                AddToken(ParseTokenType.Scalar, scalarValue, here);
                NewScalar();
                Pop();
                return; // at indent, for recursive pop
            }

            // Line is part of the current scalar
            if (scalarValue is not null)
            {
                AddScalarLine();
            }
        }

        void HandlesSpacesForScalar(ReadOnlySpan<LexToken> lex)
        {
            Debug("Handle spaces for scalar");

            if (scalarValue is null)
            {
                Debug("Ignore leading spaces");
                // ignore leading spaces
                here++;
                return;
            }
            var spaces = lex[here].Value.Length;
            here++; // after spaces
            var peek1 = Peek(lex, here);
            if (peek1 == LexTokenType.Line)
            {
                // ignore leading whitespace
                here++; // after line
                return;
            }
            if (peek1 == LexTokenType.Text)
            {
                var segment = lex[here].Value;
                Debug($"Text: sp({spaces}){segment}");
                AddScalar(new String(' ', spaces));
                AddScalar(segment);
                here++; // after text
                return;
            }
            throw Failed($"Scalar spaces followed by {peek1}.");
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////
        void NewScalar(String? initialValue = null)
        {
            Debug($"Reset scalar: {initialValue}");
            scalarValue = initialValue;
            scalarLines = false;
        }
        void SetScalar(String? value)
        {
            Debug($"Set scalar: {value}");
            scalarValue = value;
            scalarLines = false;
        }
        void AddScalar(String? value)
        {
            if (scalarValue is null) { throw Failed($"Add scalar: null + {value}."); }
            var old = scalarValue;
            scalarValue += value;
            Debug($"Add scalar: {old} => {scalarValue}");
        }
        void AddScalarLine()
        {
            Debug($"Add scalar line");
            scalarValue += "\n";
            scalarLines = true;
        }

        void Swap(ParseState nextOp)
        {
            Debug($"=== SWAP: {op} -> {nextOp}", -1);
            op = nextOp;
        }
        void Push(ParseState nextOp, int parentMinIndent)
        {
            Debug($"=== PUSH: {op}({minIndent}) -> {nextOp}({parentMinIndent})", 1);
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
            var childMinIndent = minIndent;

            if (depth <= 0) { throw new InvalidOperationException("Stack underflow"); }
            depth--;
            op = opStack[depth];
            minIndent = inStack[depth];

            Debug($"=== POP: {op}({minIndent}) <- {was}({childMinIndent})", 1);
        }
        void Debug(String message, int add = 0)
        {
            if (!debug) return;
            Console.Write(new String(' ', depth * 2 + add));
            Console.WriteLine(message);
        }
        void AddToken(ParseTokenType type, String? value, int start)
        {
            Debug($"=== ADD: {type} {value} @{start}");
            tokens.Add(new(type, value, start));
        }
        LexTokenType Peek(ReadOnlySpan<LexToken> lex, int i) => i >= lexLength ? LexTokenType.End : lex[i].Type;
        Exception Failed(String message) => new($"PARSE FAIL at {here}: {message}\n{String.Join("\n", tokens.Select(i => i.Type))}");
    }
}
