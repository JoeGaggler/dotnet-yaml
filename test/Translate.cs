using Pingmint.Yaml;

public static class Translate
{
    public static Node FromEvent(YamlDotNet.Core.Events.ParsingEvent parsingEvent, String? value, int index) => parsingEvent switch
    {
        YamlDotNet.Core.Events.StreamStart _ => new(NodeType.StreamStart, index..index, value),
        YamlDotNet.Core.Events.StreamEnd _ => new(NodeType.StreamEnd, index..index, value),
        YamlDotNet.Core.Events.DocumentStart _ => new(NodeType.DocStart, index..index, value),
        YamlDotNet.Core.Events.DocumentEnd _ => new(NodeType.DocEnd, index..index, value),
        YamlDotNet.Core.Events.Scalar scalar => new(NodeType.Scalar, index..index, value),
        YamlDotNet.Core.Events.SequenceStart _ => new(NodeType.SeqStart, index..index, value),
        YamlDotNet.Core.Events.SequenceEnd _ => new(NodeType.SeqEnd, index..index, value),
        YamlDotNet.Core.Events.MappingStart _ => new(NodeType.MapStart, index..index, value),
        YamlDotNet.Core.Events.MappingEnd _ => new(NodeType.MapEnd, index..index, value),
        _ => throw new NotImplementedException($"unexpected event: {parsingEvent.GetType().FullName}")
    };

    public static List<Node> FromDocument(String yaml)
    {
        var result = new List<Node>();
        var stringReader = new StringReader(yaml);
        var parser = new YamlDotNet.Core.Parser(stringReader);
        int index = -1;
        while (parser.MoveNext())
        {
            index++;
            if (parser.Current is not { } current) { throw new InvalidOperationException("unexpected null event"); }
            result.Add(FromEvent(current, current is YamlDotNet.Core.Events.Scalar sc ? sc.Value : null, index));
        }
        return result;
    }

    public static List<Node> FromOverride(String path)
    {
        var input = Translate.FromDocument(File.ReadAllText(path));
        var output = new List<Node>();
        for (int i = 0; i < input.Count; i++)
        {
            var token = input[i];
            if (token.Type != NodeType.Scalar) { continue; }

            var value = token.Value;
            switch (value)
            {
                case "str+": output.Add(new(NodeType.StreamStart, i..i, null)); break;
                case "str-": output.Add(new(NodeType.StreamEnd, i..i, null)); break;
                case "doc+": output.Add(new(NodeType.DocStart, i..i, null)); break;
                case "doc-": output.Add(new(NodeType.DocEnd, i..i, null)); break;
                case "seq+": output.Add(new(NodeType.SeqStart, i..i, null)); break;
                case "seq-": output.Add(new(NodeType.SeqEnd, i..i, null)); break;
                case "map+": output.Add(new(NodeType.MapStart, i..i, null)); break;
                case "map-": output.Add(new(NodeType.MapEnd, i..i, null)); break;
                case "text":
                {
                    i++;
                    var next = input[i];
                    if (next.Type != NodeType.Scalar) { throw new InvalidOperationException("expected scalar"); }
                    var scalar = next.Value?.Trim() switch
                    {
                        null => null,
                        "null" => null,
                        "~" => null,
                        _ => next.Value
                    };
                    output.Add(new(NodeType.Scalar, i..i, scalar));
                    break;
                }
                default: throw new NotImplementedException($"unexpected override value: {value}");
            }
        }
        return output;
    }
}
