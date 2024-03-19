using System.Diagnostics;
using System.Text.Json;

namespace Pingmint.Yaml;

public enum YamlNodeType
{
    None,
    StreamStart,
    StreamEnd,
    DocumentStart,
    DocumentEnd,
    MappingStart,
    MappingEnd,
    SequenceStart,
    SequenceEnd,
    Scalar,
}


public ref struct YamlReader
{
    private ReadOnlySpan<Char> yaml;

    private List<Node> nodes;
    private Int32 nodeIndex = -1;

    public YamlReader(ReadOnlySpan<Char> yaml)
    {
        this.yaml = yaml;
    }

    public YamlReader(List<Node> nodes)
    {
        this.yaml = default;
        this.nodes = nodes;
    }

    public Boolean Read()
    {
        if (this.nodes is null)
        {
            var lex = Parser.Lex(yaml);
            this.nodes = Parser.Parse(lex);
        }

    retry:
        nodeIndex++;
        if (nodeIndex >= nodes.Count)
        {
            nodeIndex = nodes.Count;
            return false;
        }

        var node = nodes[nodeIndex];
        if (node.Type == Pingmint.Yaml.NodeType.Dedent) { goto retry; } // HACK: skip dedent nodes
        this.NodeType = node.Type switch
        {
            Pingmint.Yaml.NodeType.StreamStart => YamlNodeType.StreamStart,
            Pingmint.Yaml.NodeType.StreamEnd => YamlNodeType.StreamEnd,
            Pingmint.Yaml.NodeType.DocStart => YamlNodeType.DocumentStart,
            Pingmint.Yaml.NodeType.DocEnd => YamlNodeType.DocumentEnd,
            Pingmint.Yaml.NodeType.MapStart => YamlNodeType.MappingStart,
            Pingmint.Yaml.NodeType.MapEnd => YamlNodeType.MappingEnd,
            Pingmint.Yaml.NodeType.SeqStart => YamlNodeType.SequenceStart,
            Pingmint.Yaml.NodeType.SeqEnd => YamlNodeType.SequenceEnd,
            Pingmint.Yaml.NodeType.Scalar => YamlNodeType.Scalar,
            Pingmint.Yaml.NodeType.End => YamlNodeType.None,
            Pingmint.Yaml.NodeType.Dedent => YamlNodeType.None, // unreachable
            _ => YamlNodeType.None, // unreachable
        };

        if (this.NodeType == YamlNodeType.None)
        {
            this.Value = null;
            return false;
        }

        this.Value = node.Value;
        return true;
    }

    public YamlNodeType NodeType { get; private set; } = YamlNodeType.None;

    public String? Value { get; private set; } = null;
}
