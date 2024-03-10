using Pingmint.Yaml;

public static class Translate
{
    public static ParseToken FromEvent(YamlDotNet.Core.Events.ParsingEvent parsingEvent, String? value, int index) => parsingEvent switch
    {
        YamlDotNet.Core.Events.StreamStart _ => new(ParseTokenType.StreamStart, value, index),
        YamlDotNet.Core.Events.StreamEnd _ => new(ParseTokenType.StreamEnd, value, index),
        YamlDotNet.Core.Events.DocumentStart _ => new(ParseTokenType.DocStart, value, index),
        YamlDotNet.Core.Events.DocumentEnd _ => new(ParseTokenType.DocEnd, value, index),
        YamlDotNet.Core.Events.Scalar scalar => new(ParseTokenType.Scalar, value, index),
        YamlDotNet.Core.Events.SequenceStart _ => new(ParseTokenType.SeqStart, value, index),
        YamlDotNet.Core.Events.SequenceEnd _ => new(ParseTokenType.SeqEnd, value, index),
        YamlDotNet.Core.Events.MappingStart _ => new(ParseTokenType.MapStart, value, index),
        YamlDotNet.Core.Events.MappingEnd _ => new(ParseTokenType.MapEnd, value, index),
        _ => throw new NotImplementedException($"unexpected event: {parsingEvent.GetType().FullName}")
    };

    public static List<ParseToken> FromDocument(String yaml)
    {
        var result = new List<ParseToken>();
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

    public static List<ParseToken> FromOverride(String path)
    {
        var input = Translate.FromDocument(File.ReadAllText(path));
        var output = new List<ParseToken>();
        for (int i = 0; i < input.Count; i++)
        {
            var token = input[i];
            if (token.Type != ParseTokenType.Scalar) { continue; }

            var value = token.Value;
            switch (value)
            {
                case "str+": output.Add(new(ParseTokenType.StreamStart, null, i)); break;
                case "str-": output.Add(new(ParseTokenType.StreamEnd, null, i)); break;
                case "doc+": output.Add(new(ParseTokenType.DocStart, null, i)); break;
                case "doc-": output.Add(new(ParseTokenType.DocEnd, null, i)); break;
                case "seq+": output.Add(new(ParseTokenType.SeqStart, null, i)); break;
                case "seq-": output.Add(new(ParseTokenType.SeqEnd, null, i)); break;
                case "map+": output.Add(new(ParseTokenType.MapStart, null, i)); break;
                case "map-": output.Add(new(ParseTokenType.MapEnd, null, i)); break;
                case "text":
                {
                    i++;
                    var next = input[i];
                    if (next.Type != ParseTokenType.Scalar) { throw new InvalidOperationException("expected scalar"); }
                    var scalar = next.Value?.Trim() switch
                    {
                        null => null,
                        "null" => null,
                        "~" => null,
                        _ => next.Value
                    };
                    output.Add(new(ParseTokenType.Scalar, scalar, i));
                    break;
                }
                default: throw new NotImplementedException($"unexpected override value: {value}");
            }
        }
        return output;
    }
}
