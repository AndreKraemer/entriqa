using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Entriqa.Domain.Forms;

namespace Entriqa.Admin.Services;

/// <summary>
/// Editable model of the visual builder. Loads from the definition JSON and writes it back;
/// multi-language texts (string or {locale: text}) are edited per locale and reduced again on write
/// (all equal -> a plain string). The quiz is passed through unchanged
/// and edited in the JSON tab.
/// </summary>
public sealed class FormModel
{

    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "contact";
    public bool Handling { get; set; }
    public List<string> Locales { get; set; } = new() { "de" };
    public LTextModel Intro { get; } = new();
    public LTextModel SubmitLabel { get; } = new();
    public string CompletionMode { get; set; } = "message";
    public LTextModel CompletionMessage { get; } = new();
    public LTextModel CompletionUrl { get; } = new();
    public List<FieldModel> Fields { get; } = new();
    public List<StepModel> Steps { get; } = new();
    public QuizModel? Quiz { get; set; }

    public static FormModel Parse(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new JsonException("Definition ist leer.");
        var m = new FormModel
        {
            Slug = root["slug"]?.GetValue<string>() ?? "",
            Name = root["name"]?.GetValue<string>() ?? "",
            Type = root["type"]?.GetValue<string>() ?? "contact",
            Handling = root["handling"]?.GetValue<bool>() ?? false,
            Locales = root["locales"]?.AsArray().Select(x => x!.GetValue<string>()).ToList() is { Count: > 0 } l ? l : new List<string> { "de" },
        };
        if (root["quiz"] is JsonObject quiz) m.Quiz = QuizModel.Parse(quiz, m.Locales);
        m.Intro.Load(root["intro"], m.Locales);
        m.SubmitLabel.Load(root["submitLabel"], m.Locales);
        if (root["completion"] is JsonObject c)
        {
            m.CompletionMode = c["mode"]?.GetValue<string>() ?? "message";
            m.CompletionMessage.Load(c["message"], m.Locales);
            m.CompletionUrl.Load(c["url"], m.Locales);
        }
        foreach (var f in root["fields"]?.AsArray() ?? new JsonArray())
            m.Fields.Add(FieldModel.Parse(f!.AsObject(), m.Locales));
        foreach (var s in root["pipeline"]?.AsArray() ?? new JsonArray())
            m.Steps.Add(StepModel.Parse(s!.AsObject()));
        return m;
    }

    public string ToJson()
    {
        var root = new JsonObject
        {
            ["slug"] = Slug.Trim(),
            ["name"] = Name.Trim(),
            ["type"] = Type,
            ["locales"] = new JsonArray(Locales.Select(l => (JsonNode)l).ToArray()),
        };
        if (Intro.ToNode(Locales) is { } intro) root["intro"] = intro;
        if (SubmitLabel.ToNode(Locales) is { } submit) root["submitLabel"] = submit;
        root["handling"] = Handling;
        root["fields"] = new JsonArray(Fields.Select(f => (JsonNode)f.ToNode(Locales)).ToArray());
        if (Quiz is not null) root["quiz"] = Quiz.ToNode(Locales);
        root["pipeline"] = new JsonArray(Steps.Select(s => (JsonNode)s.ToNode(Locales)).ToArray());
        var completion = new JsonObject { ["mode"] = CompletionMode };
        if (CompletionMessage.ToNode(Locales) is { } msg) completion["message"] = msg;
        if (CompletionUrl.ToNode(Locales) is { } url) completion["url"] = url;
        root["completion"] = completion;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}

/// <summary>Multi-language text: one input per locale; on write a string (all equal) or an object.</summary>
public sealed class LTextModel
{
    private readonly Dictionary<string, string> _values = new();

    public string Get(string locale) => _values.GetValueOrDefault(locale) ?? "";
    public void Set(string locale, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) _values.Remove(locale);
        else _values[locale] = value;
    }

    public void Load(JsonNode? node, List<string> locales)
    {
        _values.Clear();
        if (node is JsonValue v && v.TryGetValue<string>(out var single))
            foreach (var l in locales) _values[l] = single;      // a plain string applies to every language
        else if (node is JsonObject o)
            foreach (var (k, val) in o) if (val is not null) _values[k] = val.GetValue<string>();
    }

    public JsonNode? ToNode(IReadOnlyList<string> locales)
    {
        var filled = _values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToList();
        if (filled.Count == 0) return null;
        // A plain string means "applies to EVERY language" - so only reduce when every configured
        // language really carries the same text. Otherwise the object stays, so that the publish check
        // can report missing language versions.
        if (locales.All(l => filled.Any(kv => kv.Key == l)) && filled.Select(kv => kv.Value).Distinct().Count() == 1)
            return filled[0].Value;
        return new JsonObject(filled.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value)));
    }
}

public sealed class FieldModel
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "text";
    public LTextModel Label { get; } = new();
    public bool Required { get; set; }
    public LTextModel Placeholder { get; } = new();
    public LTextModel Help { get; } = new();
    public LTextModel ConsentText { get; } = new();
    public bool BusinessOnly { get; set; }
    public string MaxLength { get; set; } = "";
    public string Min { get; set; } = "";
    public string Max { get; set; } = "";
    public string Source { get; set; } = "utm_source";
    public List<LTextModel> Options { get; } = new();

    public string VisibleIfField { get; set; } = "";
    public List<int> VisibleIfOptions { get; } = new();

    public static readonly (string Value, string Label)[] Types =
    {
        ("text", "Textzeile"), ("email", "E-Mail"), ("tel", "Telefon"), ("textarea", "Mehrzeiliger Text"), ("number", "Zahl"),
        ("file", "Datei-Upload"),
        ("select", "Auswahl"), ("multiselect", "Mehrfachauswahl"), ("checkbox", "Checkbox"), ("date", "Datum"),
        ("rating", "Bewertungsskala"), ("consent", "Einwilligung"), ("hidden", "Verstecktes Feld"),
        ("section", "Zwischenüberschrift"), ("divider", "Trennlinie"), ("page", "Seitenumbruch"),
    };

    public bool HasOptions => Type is "select" or "multiselect";
    public bool IsLayout => Type is "section" or "divider" or "page";

    public static FieldModel Parse(JsonObject o, List<string> locales)
    {
        var f = new FieldModel
        {
            Id = o["id"]?.GetValue<string>() ?? "",
            Type = o["type"]?.GetValue<string>() ?? "text",
            Required = o["required"]?.GetValue<bool>() ?? false,
            BusinessOnly = o["businessOnly"]?.GetValue<bool>() ?? false,
            MaxLength = o["maxLength"]?.GetValue<int>().ToString(CultureInfo.InvariantCulture) ?? "",
            Min = o["min"]?.GetValue<decimal>().ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            Max = o["max"]?.GetValue<decimal>().ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            Source = o["source"]?.GetValue<string>() ?? "utm_source",
        };
        f.Label.Load(o["label"], locales);
        f.Placeholder.Load(o["placeholder"], locales);
        f.Help.Load(o["help"], locales);
        f.ConsentText.Load(o["text"], locales);
        if (o["visibleIf"] is JsonObject vi)
        {
            f.VisibleIfField = vi["field"]?.GetValue<string>() ?? "";
            foreach (var idx in vi["options"]?.AsArray() ?? new JsonArray())
                if (idx is not null && idx.GetValueKind() == System.Text.Json.JsonValueKind.Number) f.VisibleIfOptions.Add(idx.GetValue<int>());
        }
        foreach (var opt in o["options"]?.AsArray() ?? new JsonArray())
        {
            var lt = new LTextModel();
            lt.Load(opt, locales);
            f.Options.Add(lt);
        }
        return f;
    }

    public JsonObject ToNode(IReadOnlyList<string> locales)
    {
        var o = new JsonObject { ["id"] = Id.Trim(), ["type"] = Type };
        if (Label.ToNode(locales) is { } label) o["label"] = label;
        if (Required) o["required"] = true;
        if (Placeholder.ToNode(locales) is { } ph) o["placeholder"] = ph;
        if (Help.ToNode(locales) is { } help) o["help"] = help;
        if (HasOptions && Options.Count > 0) o["options"] = new JsonArray(Options.Select(x => x.ToNode(locales)).Where(x => x is not null).ToArray()!);
        if (Type == "consent" && ConsentText.ToNode(locales) is { } text) o["text"] = text;
        if (Type == "hidden") o["source"] = Source;
        if (Type == "email" && BusinessOnly) o["businessOnly"] = true;
        if (VisibleIfField is { Length: > 0 })
        {
            var vi = new JsonObject { ["field"] = VisibleIfField };
            if (VisibleIfOptions.Count > 0) vi["options"] = new JsonArray(VisibleIfOptions.Select(i => (JsonNode)i).ToArray());
            o["visibleIf"] = vi;
        }
        if (int.TryParse(MaxLength, out var ml)) o["maxLength"] = ml;
        if (decimal.TryParse(Min, System.Globalization.CultureInfo.InvariantCulture, out var min)) o["min"] = min;
        if (decimal.TryParse(Max, System.Globalization.CultureInfo.InvariantCulture, out var max)) o["max"] = max;
        return o;
    }
}

public sealed class StepModel
{
    public string Id { get; set; } = "";
    public string Key { get; set; } = "";
    public string When { get; set; } = "always";
    public bool? Critical { get; set; }                          // null = the step's own default
    public Dictionary<string, string> ConfigText { get; } = new();  // Schema-Property → Rohtext
    /// <summary>The localizable properties (issue #3): schema property → language → raw text. Kept apart
    /// from <see cref="ConfigText"/> so the editor cannot read one of them without naming a language.</summary>
    public Dictionary<string, Dictionary<string, string>> ConfigTextByLocale { get; } = new();
    public JsonObject? ConfigRaw { get; set; }                   // the original - the basis for writing back

    public static StepModel Parse(JsonObject o)
    {
        var s = new StepModel
        {
            Id = o["id"]?.GetValue<string>() ?? "",
            Key = o["step"]?.GetValue<string>() ?? "",
            When = o["when"]?.GetValue<string>() ?? "always",
            Critical = o["critical"]?.GetValue<bool>(),
            ConfigRaw = o["config"]?.DeepClone().AsObject(),
        };
        return s;
    }

    /// <summary>Fill the raw configuration texts from the original plus the schema (once, when it is shown).</summary>
    public void LoadConfigText(StepSchema schema, IReadOnlyList<string> locales)
    {
        ConfigText.Clear();
        ConfigTextByLocale.Clear();
        foreach (var p in schema.Properties)
        {
            var node = ConfigRaw?[p.Name];
            if (p.Localizable)
            {
                // A plain value fills every language, an object splits - so the editor never shows the
                // storage format and the operator never types one (AC 5 of issue #3).
                ConfigTextByLocale[p.Name] = LValue.Decompose(node, locales)
                    .ToDictionary(kv => kv.Key, kv => RawText(kv.Value), StringComparer.Ordinal);
                continue;
            }
            ConfigText[p.Name] = RawText(node);
        }
    }

    /// <summary>
    /// The form's language list changed after this step was loaded. A language that is switched on
    /// inherits the value the others already agree on, because a field configured once covers every
    /// language (AC 2 of issue #3) - without this, ticking a language would rewrite every plain value
    /// into a single-language object and the form could no longer be published. A partially translated
    /// field leaves the new language empty, which is what the publish check is there to report.
    /// Languages switched off keep their value, so switching one back on loses nothing.
    /// </summary>
    public void ReconcileLocales(IReadOnlyList<string> locales)
    {
        foreach (var perLocale in ConfigTextByLocale.Values)
        {
            var known = perLocale.Values.ToList();
            var shared = known.Count > 0 && known.All(v => !string.IsNullOrWhiteSpace(v))
                         && known.Distinct(StringComparer.Ordinal).Count() == 1 ? known[0] : "";
            foreach (var locale in locales)
                if (!perLocale.ContainsKey(locale)) perLocale[locale] = shared;
        }
    }

    private static string RawText(JsonNode? node) => node switch
    {
        null => "",
        JsonArray arr => string.Join(", ", arr.Select(x => x?.ToString() ?? "")),
        JsonObject obj => obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
        _ => node.ToString(),
    };

    public JsonObject ToNode(IReadOnlyList<string> locales)
    {
        var o = new JsonObject { ["id"] = Id, ["step"] = Key, ["when"] = When };
        if (Critical is { } c) o["critical"] = c;
        o["config"] = BuildConfig(locales);
        return o;
    }

    private JsonObject BuildConfig(IReadOnlyList<string> locales)
    {
        // The original is the basis (unknown properties are kept), schema properties come from the raw texts.
        var config = ConfigRaw?.DeepClone().AsObject() ?? new JsonObject();
        foreach (var (name, text) in ConfigText)
        {
            if (string.IsNullOrWhiteSpace(text)) { config.Remove(name); continue; }
            config[name] = Typed(name, text);
        }
        foreach (var (name, perLocale) in ConfigTextByLocale)
        {
            var typed = perLocale.ToDictionary(kv => kv.Key,
                kv => string.IsNullOrWhiteSpace(kv.Value) ? null : Typed(name, kv.Value), StringComparer.Ordinal);
            // Compose collapses back to a plain value when every language carries the same one, so a form
            // nobody translated keeps the scalar it was published with.
            if (LValue.Compose(typed, locales) is { } value) config[name] = value;
            else config.Remove(name);
        }
        return config;
    }

    private JsonNode Typed(string name, string text) => Schema?.Properties.FirstOrDefault(p => p.Name == name)?.Type switch
    {
        "integer" => int.TryParse(text.Trim(), out var i) ? (JsonNode)i : text.Trim(),
        "boolean" => bool.TryParse(text.Trim(), out var b) ? (JsonNode)b : text.Trim(),
        "array" => new JsonArray(text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, out var n) ? (JsonNode)n : x).ToArray()),
        "object" => TryParseObject(text),
        _ => text,
    };

    private static JsonNode TryParseObject(string text)
    {
        try { return JsonNode.Parse(text) ?? new JsonObject(); } catch (JsonException) { return text; }
    }

    public StepSchema? Schema { get; set; }
}

/// <summary>
/// Editable quiz: questions with branches, findings and results. The live path check uses the real
/// <c>QuizEngine.Check</c> from Entriqa.Domain - the same rules as when publishing.
/// </summary>
public sealed class QuizModel
{
    /// <summary>One instance: the quiz check runs on every edit in the builder.</summary>
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public string Scoring { get; set; } = "sum";
    public string CollectEmail { get; set; } = "optional";
    public List<QuizQuestionModel> Questions { get; } = new();
    public List<QuizResultModel> Results { get; } = new();
    public bool HasFindings { get; set; }
    public string FindingsMax { get; set; } = "3";
    public string FindingsPriority { get; set; } = "";           // Fragen-IDs, kommasepariert
    public LTextModel WarningTemplate { get; } = new();
    public LTextModel EmptyText { get; } = new();

    public static QuizModel Parse(JsonObject o, List<string> locales)
    {
        var q = new QuizModel
        {
            Scoring = o["scoring"]?.GetValue<string>() ?? "sum",
            CollectEmail = o["collectEmail"]?.GetValue<string>() ?? "optional",
        };
        foreach (var n in o["questions"]?.AsArray() ?? new JsonArray())
            q.Questions.Add(QuizQuestionModel.Parse(n!.AsObject(), locales));
        foreach (var n in o["results"]?.AsArray() ?? new JsonArray())
            q.Results.Add(QuizResultModel.Parse(n!.AsObject(), locales));
        if (o["findings"] is JsonObject f)
        {
            q.HasFindings = true;
            q.FindingsMax = f["max"]?.GetValue<int>().ToString(CultureInfo.InvariantCulture) ?? "3";
            q.FindingsPriority = string.Join(", ", (f["priority"]?.AsArray() ?? new JsonArray()).Select(x => x!.GetValue<string>()));
            q.WarningTemplate.Load(f["warningTemplate"], locales);
            q.EmptyText.Load(f["emptyText"], locales);
        }
        return q;
    }

    public JsonObject ToNode(IReadOnlyList<string> locales)
    {
        var o = new JsonObject
        {
            ["scoring"] = Scoring,
            ["collectEmail"] = CollectEmail,
            ["questions"] = new JsonArray(Questions.Select(x => (JsonNode)x.ToNode(locales)).ToArray()),
            ["results"] = new JsonArray(Results.Select(x => (JsonNode)x.ToNode(locales)).ToArray()),
        };
        if (HasFindings)
        {
            var f = new JsonObject { ["max"] = int.TryParse(FindingsMax, out var m) ? m : 3 };
            var prio = FindingsPriority.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (prio.Length > 0) f["priority"] = new JsonArray(prio.Select(x => (JsonNode)x).ToArray());
            if (WarningTemplate.ToNode(locales) is { } wt) f["warningTemplate"] = wt;
            if (EmptyText.ToNode(locales) is { } et) f["emptyText"] = et;
            o["findings"] = f;
        }
        return o;
    }

    /// <summary>The same check as when publishing, live in the builder (jump targets, loops, reachability, gaps, priority).</summary>
    public IReadOnlyList<string> PathCheck(IReadOnlyList<string> locales)
    {
        try
        {
            var def = System.Text.Json.JsonSerializer.Deserialize<Entriqa.Domain.Forms.QuizDefinition>(
                ToNode(locales).ToJsonString(), WebJson);
            return def is null ? new[] { "Quiz ist leer." } : Entriqa.Domain.Quiz.QuizEngine.Check(def);
        }
        catch (Exception ex) { return new[] { "Prüfung nicht möglich: " + ex.Message }; }
    }

    public static QuizModel Starter()
    {
        var q = new QuizModel();
        q.Questions.Add(new QuizQuestionModel { Id = "q1" });
        q.Questions[0].Text.Set("de", "Erste Frage");
        q.Questions[0].Options.Add(QuizOptionModel.New("a", "Antwort A", 0));
        q.Questions[0].Options.Add(QuizOptionModel.New("b", "Antwort B", 2));
        q.Results.Add(QuizResultModel.New("gruen", 0, 49, "Ergebnis A"));
        q.Results.Add(QuizResultModel.New("rot", 50, 100, "Ergebnis B"));
        return q;
    }
}

public sealed class QuizQuestionModel
{
    public string Id { get; set; } = "";
    public LTextModel Text { get; } = new();
    public LTextModel Topic { get; } = new();
    public List<QuizOptionModel> Options { get; } = new();

    public static QuizQuestionModel Parse(JsonObject o, List<string> locales)
    {
        var q = new QuizQuestionModel { Id = o["id"]?.GetValue<string>() ?? "" };
        q.Text.Load(o["text"], locales);
        q.Topic.Load(o["topic"], locales);
        foreach (var n in o["options"]?.AsArray() ?? new JsonArray())
            q.Options.Add(QuizOptionModel.Parse(n!.AsObject(), locales));
        return q;
    }

    public JsonObject ToNode(IReadOnlyList<string> locales)
    {
        var o = new JsonObject { ["id"] = Id.Trim() };
        if (Text.ToNode(locales) is { } t) o["text"] = t;
        if (Topic.ToNode(locales) is { } tp) o["topic"] = tp;
        o["options"] = new JsonArray(Options.Select(x => (JsonNode)x.ToNode(locales)).ToArray());
        return o;
    }
}

public sealed class QuizOptionModel
{
    public string Id { get; set; } = "";
    public LTextModel Label { get; } = new();
    public string Points { get; set; } = "0";
    public string Next { get; set; } = "";                       // "" = next question | question id | result:<id>
    public string? Category { get; set; }                        // passed through (scoring mode category, later)
    public LTextModel Finding { get; } = new();

    public static QuizOptionModel New(string id, string label, int points)
    {
        var o = new QuizOptionModel { Id = id, Points = points.ToString(CultureInfo.InvariantCulture) };
        o.Label.Set("de", label);
        return o;
    }

    public static QuizOptionModel Parse(JsonObject o, List<string> locales)
    {
        var m = new QuizOptionModel
        {
            Id = o["id"]?.GetValue<string>() ?? "",
            Points = o["points"]?.GetValue<int>().ToString(CultureInfo.InvariantCulture) ?? "0",
            Next = o["next"]?.GetValue<string>() ?? "",
            Category = o["category"]?.GetValue<string>(),
        };
        m.Label.Load(o["label"], locales);
        m.Finding.Load(o["finding"], locales);
        return m;
    }

    public JsonObject ToNode(IReadOnlyList<string> locales)
    {
        var o = new JsonObject { ["id"] = Id.Trim() };
        if (Label.ToNode(locales) is { } l) o["label"] = l;
        o["points"] = int.TryParse(Points, out var p) ? p : 0;
        if (!string.IsNullOrWhiteSpace(Next)) o["next"] = Next;
        if (Category is { Length: > 0 }) o["category"] = Category;
        if (Finding.ToNode(locales) is { } f) o["finding"] = f;
        return o;
    }
}

public sealed class QuizResultModel
{
    public string Id { get; set; } = "";
    public string MinPct { get; set; } = "0";
    public string MaxPct { get; set; } = "100";
    public LTextModel Title { get; } = new();
    public LTextModel Body { get; } = new();

    public static QuizResultModel New(string id, int min, int max, string title)
    {
        var r = new QuizResultModel { Id = id, MinPct = min.ToString(CultureInfo.InvariantCulture), MaxPct = max.ToString(CultureInfo.InvariantCulture) };
        r.Title.Set("de", title);
        return r;
    }

    public static QuizResultModel Parse(JsonObject o, List<string> locales)
    {
        var r = new QuizResultModel
        {
            Id = o["id"]?.GetValue<string>() ?? "",
            MinPct = o["minPct"]?.GetValue<int>().ToString(CultureInfo.InvariantCulture) ?? "0",
            MaxPct = o["maxPct"]?.GetValue<int>().ToString(CultureInfo.InvariantCulture) ?? "100",
        };
        r.Title.Load(o["title"], locales);
        r.Body.Load(o["body"], locales);
        return r;
    }

    public JsonObject ToNode(IReadOnlyList<string> locales)
    {
        var o = new JsonObject
        {
            ["id"] = Id.Trim(),
            ["minPct"] = int.TryParse(MinPct, out var min) ? min : 0,
            ["maxPct"] = int.TryParse(MaxPct, out var max) ? max : 100,
        };
        if (Title.ToNode(locales) is { } t) o["title"] = t;
        if (Body.ToNode(locales) is { } b) o["body"] = b;
        return o;
    }
}

/// <summary>Simplified JSON Schema of a step (flat objects - the steps need no more than that).</summary>
public sealed record StepSchema(List<StepSchemaProperty> Properties, List<string> Required)
{
    public static StepSchema Parse(string configSchema)
    {
        var props = new List<StepSchemaProperty>();
        var required = new List<string>();
        try
        {
            var root = JsonNode.Parse(configSchema)?.AsObject();
            foreach (var r in root?["required"]?.AsArray() ?? new JsonArray()) required.Add(r!.GetValue<string>());
            foreach (var (name, node) in root?["properties"]?.AsObject() ?? new JsonObject())
            {
                var o = node!.AsObject();
                props.Add(new StepSchemaProperty(name,
                    o["type"]?.GetValue<string>() ?? "string",
                    o["title"]?.GetValue<string>() ?? name,
                    o["default"]?.ToString(),
                    o["format"]?.GetValue<string>(),
                    o["enum"]?.AsArray().Select(x => x!.GetValue<string>()).ToList(),
                    // Issue #3: the marker sits on the property, or - for a map whose members are the
                    // localizable leaves (reportingcloud.pdf's templates) - on its additionalProperties.
                    o["localizable"]?.GetValue<bool>() ?? false,
                    (o["additionalProperties"] as JsonObject)?["localizable"]?.GetValue<bool>() ?? false));
            }
        }
        catch (JsonException) { /* empty schema */ }
        return new StepSchema(props, required);
    }
}

public sealed record StepSchemaProperty(string Name, string Type, string Title, string? Default,
    string? Format = null, List<string>? Enum = null, bool Localizable = false, bool LocalizableItems = false);
