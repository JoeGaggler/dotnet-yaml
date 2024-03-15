List<String> log = new();
foreach (var yamlPath in Directory.GetFiles("cases", "*.yml").OrderBy(i => i))
{
    // Skip override files the reference parser
    if (yamlPath.EndsWith(".expect.yml", StringComparison.InvariantCultureIgnoreCase)) { continue; }

    Console.WriteLine($"--- {yamlPath} ---");
    var yaml = File.ReadAllText(yamlPath);

    List<Pingmint.Yaml.Node> expect;
    var overridePath = yamlPath.Replace(".yml", ".expect.yml");
    if (File.Exists(overridePath))
    {
        Console.WriteLine("Using expect-override.");

        var overrideTokens = Translate.FromOverride(overridePath);
        expect = overrideTokens;
    }
    else
    {
        expect = Translate.FromDocument(yaml);
        Console.WriteLine("Using reference parser.");
    }

    Console.WriteLine("Expect:");
    foreach (var line in expect)
    {
        Console.WriteLine($"{line.Name} {line.Run.Start,4} {line.Run.Length,3} => {line.Value}");
    }

    var tokens = Pingmint.Yaml.Parser.Lex(yaml);
    Console.WriteLine();
    Console.WriteLine("Lex Tokens:");
    var tokenIndex = 0;
    foreach (var token in tokens)
    {
        Console.WriteLine($"{tokenIndex++,4} {token.Column,-4} {token.Type} => {token.Value}");
    }

    Console.WriteLine();
    Console.WriteLine("Parsing:");
    var actual = Pingmint.Yaml.Parser.Parse(tokens);
    Console.WriteLine();
    Console.WriteLine("Actual:");
    foreach (var line in actual)
    {
        Console.WriteLine($"{line.Name} {line.Run.Start,4} {line.Run.Length,3} => {line.Value}");
    }

    Console.WriteLine();

    if (expect.Count != actual.Count)
    {
        Console.WriteLine("FAIL (count)");
        return 1;
    }
    var zipped = expect.Zip(actual);
    int index = -1;
    String Escape(String? value)
    {
        if (value is null) { return "<null>"; }
        value = value.Replace("\r\n", "<rn>");
        value = value.Replace("\n", "<n>");
        value = value.Replace("\r", "<r>");
        value = value.Replace("\t", "<t>");
        value = value.Replace(" ", "_");
        return value;
    }
    foreach (var (e, a) in zipped)
    {
        index++;
        if (e.Type != a.Type)
        {
            Console.WriteLine($"<=== FAIL at {index} (type): {e.Type} != {a.Type}");
            return 1;
        }
        if (e.Value != a.Value)
        {
            log.Add($"{yamlPath} FAIL at {index} (value): {Escape(e.Value)}<{e.Value?.Length}> != {Escape(a.Value)}<{a.Value?.Length}>");
            Console.WriteLine($"FAIL at {index} (value): {Escape(e.Value)}<{e.Value?.Length}> != {Escape(a.Value)}<{a.Value?.Length}>");
            // return 1;
        }
    }

    Console.WriteLine();
}

foreach (var line in log)
{
    Console.WriteLine(line);
}

return 0;
