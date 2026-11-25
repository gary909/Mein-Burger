using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 3)
{
    Console.WriteLine("Usage: catalogue <bamf.txt> <quiz-data.json> <data-dir> [overrides.json]");
    return 1;
}

var txtPath = args[0];
var datasetPath = args[1];
var dataDir = args[2];
var overridesPath = args.Length >= 4 ? args[3] : Path.Combine(dataDir, "overrides.json");

var pdfQuestions = PdfParser.Parse(txtPath);
PdfParser.Validate(pdfQuestions);

var dataset = Dataset.Load(datasetPath);
var overrides = Overrides.Load(overridesPath);

var finals = new List<FinalQuestion>();
var mapRows = new List<MapRow>();
var unresolved = new List<UnresolvedItem>();
var reviewItems = new List<ReviewItem>();

foreach (var question in pdfQuestions)
{
    var datasetQuestion = dataset.Find(question);
    if (datasetQuestion is null)
    {
        unresolved.Add(new UnresolvedItem(question.Id, "no matching dataset question"));
        continue;
    }

    if (question.Options.Count != 4 || datasetQuestion.Options.Count != 4)
    {
        unresolved.Add(new UnresolvedItem(question.Id, $"option count official={question.Options.Count}, dataset={datasetQuestion.Options.Count}"));
        continue;
    }

    var map = OptionMapper.Map(question.Options, datasetQuestion.Options.Select(option => option.De).ToList());
    var officialCorrect = Array.IndexOf(map.Perm, datasetQuestion.Correct);
    if (officialCorrect < 0)
    {
        unresolved.Add(new UnresolvedItem(question.Id, "dataset answer option could not be mapped"));
        continue;
    }

    var questionSimilarity = Similarity.Jaccard(question.De, datasetQuestion.De);
    var status = "mapped";
    var notes = new List<string>();

    if (map.MinScore < 0.55)
    {
        status = "review";
        notes.Add($"lowest option score {map.MinScore:F2}");
    }

    if (questionSimilarity < 0.75)
    {
        notes.Add($"wording differs from dataset (similarity {questionSimilarity:F2})");
    }

    var english = datasetQuestion.En;
    var optionsEn = new string[4];
    for (var i = 0; i < 4; i++)
    {
        var datasetIndex = map.Perm[i];
        if (Regex.IsMatch(question.Options[i].Trim(), @"^\d+$") &&
            Regex.IsMatch(datasetQuestion.Options[datasetIndex].De.Trim(), @"^(?:Bild\s*)?\d+$"))
        {
            optionsEn[i] = question.Options[i].Trim();
        }
        else
        {
            optionsEn[i] = datasetQuestion.Options[datasetIndex].En;
        }
    }

    var explanationDe = datasetQuestion.ExpDe;
    var explanationEn = datasetQuestion.ExpEn;

    if (overrides.TryGetValue(question.Id, out var value))
    {
        status = "override";
        if (value.Correct.HasValue) officialCorrect = value.Correct.Value;
        if (value.En is not null) english = value.En;
        if (value.OptionsEn is { Length: 4 }) optionsEn = value.OptionsEn;
        if (value.ExpDe is not null) explanationDe = value.ExpDe;
        if (value.ExpEn is not null) explanationEn = value.ExpEn;
    }

    if (officialCorrect is < 0 or > 3)
    {
        unresolved.Add(new UnresolvedItem(question.Id, $"correct index {officialCorrect} out of range"));
        continue;
    }

    var image = datasetQuestion.Image is null ? null : "data/images/" + Path.GetFileName(datasetQuestion.Image);

    finals.Add(new FinalQuestion
    {
        Id = question.Id,
        BamfNo = question.BamfNo,
        State = question.State,
        Topic = question.Topic,
        Page = question.Page,
        De = question.De,
        En = english,
        OptionsDe = question.Options,
        OptionsEn = optionsEn,
        Correct = officialCorrect,
        Image = image,
        ExpDe = explanationDe,
        ExpEn = explanationEn
    });

    mapRows.Add(new MapRow(question.Id, status, questionSimilarity, map.MinScore, map.Perm, officialCorrect, datasetQuestion.Correct, string.Join("; ", notes)));

    reviewItems.Add(new ReviewItem(
        question.Id,
        status,
        question.Page,
        question.De,
        datasetQuestion.De,
        Math.Round(questionSimilarity, 2),
        Math.Round(map.MinScore, 2),
        question.Options.ToArray(),
        datasetQuestion.Options.Select(option => option.De).ToArray(),
        (char)('A' + officialCorrect),
        (char)('A' + datasetQuestion.Correct),
        string.Join("; ", notes)));
}

foreach (var missing in finals
             .Where(final => final.Image is not null)
             .Select(final => Path.Combine(dataDir, "images", Path.GetFileName(final.Image!)))
             .Where(path => !File.Exists(path)))
{
    unresolved.Add(new UnresolvedItem("", "missing image " + missing));
}

Directory.CreateDirectory(dataDir);
WritePdfQuestions(dataDir, pdfQuestions);
WriteCatalogue(dataDir, finals);
WriteSummary(dataDir, finals, pdfQuestions);
WriteAnswerMap(dataDir, mapRows);
WriteReviewCsv(dataDir, reviewItems);
WriteJson(Path.Combine(dataDir, "unresolved.json"), unresolved);

Console.WriteLine($"PDF questions: {pdfQuestions.Count} (federal {pdfQuestions.Count(q => q.State is null)}, state {pdfQuestions.Count(q => q.State is not null)})");
Console.WriteLine($"Mapped: {finals.Count}, review flags: {mapRows.Count(row => row.Status == "review")}, overrides: {mapRows.Count(row => row.Status == "override")}, unresolved: {unresolved.Count}");
foreach (var item in reviewItems.Where(item => item.Status == "review").Take(30))
{
    Console.WriteLine($"REVIEW {item.Id} page {item.Page} qsim={item.QuestionSimilarity} opt={item.LowestOptionScore} :: {item.De}");
}

return unresolved.Count > 0 ? 2 : 0;

static void WritePdfQuestions(string directory, List<PdfQuestion> questions)
{
    var root = new JsonObject
    {
        ["version"] = "2025-05-07",
        ["source"] = "BAMF Gesamtfragenkatalog (Stand 07.05.2025), parsed from pdftotext output",
        ["questions"] = new JsonArray(questions.Select(question => (JsonNode)new JsonObject
        {
            ["id"] = question.Id,
            ["bamfNo"] = question.BamfNo,
            ["state"] = question.State,
            ["topic"] = question.Topic,
            ["page"] = question.Page,
            ["de"] = question.De,
            ["optionsDe"] = new JsonArray(question.Options.Select(option => (JsonNode?)JsonValue.Create(option)).ToArray())
        }).ToArray())
    };

    WriteJson(Path.Combine(directory, "pdf-questions.json"), root);
}

static void WriteCatalogue(string directory, List<FinalQuestion> questions)
{
    var general = questions.Where(question => question.State is null).OrderBy(question => question.BamfNo).ToList();
    var states = new JsonObject();

    foreach (var group in questions.Where(question => question.State is not null)
                 .GroupBy(question => question.State!)
                 .OrderBy(group => group.Key))
    {
        states[group.Key] = new JsonArray(group.OrderBy(question => question.BamfNo).Select(question => (JsonNode)ToJson(question)).ToArray());
    }

    var root = new JsonObject
    {
        ["version"] = "2025-05-07",
        ["generated"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["source"] = new JsonObject
        {
            ["catalog"] = "https://www.bamf.de/SharedDocs/Anlagen/DE/Integration/Einbuergerung/gesamtfragenkatalog-lebenindeutschland.pdf?__blob=publicationFile&v=23",
            ["catalogVersion"] = "2025-05-07",
            ["catalogSha256"] = "EDD3ED5E56A35241B6CC61AA0630482DC9E64498DB508A7D6892CCEE2EC707A9",
            ["wording"] = "Official BAMF catalogue text (parsed from the PDF)",
            ["dataset"] = "https://github.com/abdullahbutt/leben-in-deutschland-test",
            ["license"] = "CC BY-NC 4.0",
            ["attribution"] = "Fragen: Bundesamt für Migration und Flüchtlinge (BAMF). Antworten, englische Übersetzungen, Erläuterungen und Bilder: Abdullah Butt, CC BY-NC 4.0."
        },
        ["general"] = new JsonArray(general.Select(question => (JsonNode)ToJson(question)).ToArray()),
        ["states"] = states
    };

    var options = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    var json = root.ToJsonString(options).Replace("</", "<\\/", StringComparison.Ordinal);
    File.WriteAllText(
        Path.Combine(directory, "questions.js"),
        "/* Auto-generated by tools/Catalogue - do not edit manually. */\nwindow.EINBUERGERUNG_DATA = " + json + ";\n",
        new UTF8Encoding(false));
}

static JsonObject ToJson(FinalQuestion question)
{
    var result = new JsonObject
    {
        ["id"] = question.Id,
        ["bamfNo"] = question.BamfNo,
        ["topic"] = question.Topic,
        ["page"] = question.Page,
        ["de"] = question.De,
        ["en"] = question.En,
        ["optionsDe"] = new JsonArray(question.OptionsDe.Select(option => (JsonNode?)JsonValue.Create(option)).ToArray()),
        ["optionsEn"] = new JsonArray(question.OptionsEn.Select(option => (JsonNode?)JsonValue.Create(option)).ToArray()),
        ["correct"] = question.Correct,
        ["image"] = question.Image,
        ["expDe"] = question.ExpDe,
        ["expEn"] = question.ExpEn
    };

    if (question.State is not null) result["state"] = question.State;
    return result;
}

static void WriteSummary(string directory, List<FinalQuestion> questions, List<PdfQuestion> pdfQuestions)
{
    var summary = new JsonObject
    {
        ["version"] = "2025-05-07",
        ["general"] = questions.Count(question => question.State is null),
        ["states"] = new JsonObject(questions.Where(question => question.State is not null)
            .GroupBy(question => question.State!)
            .OrderBy(group => group.Key)
            .Select(group => new KeyValuePair<string, JsonNode?>(group.Key, JsonValue.Create(group.Count())))),
        ["stateTotal"] = questions.Count(question => question.State is not null),
        ["withImage"] = questions.Count(question => question.Image is not null),
        ["topics"] = new JsonObject(new[]
        {
            new KeyValuePair<string, JsonNode?>("democracy", JsonValue.Create(questions.Count(question => question.Topic == "democracy"))),
            new KeyValuePair<string, JsonNode?>("history", JsonValue.Create(questions.Count(question => question.Topic == "history"))),
            new KeyValuePair<string, JsonNode?>("society", JsonValue.Create(questions.Count(question => question.Topic == "society"))),
            new KeyValuePair<string, JsonNode?>("state", JsonValue.Create(questions.Count(question => question.Topic == "state")))
        }),
        ["pdfQuestions"] = pdfQuestions.Count
    };

    WriteJson(Path.Combine(directory, "summary.json"), summary);
}

static void WriteAnswerMap(string directory, List<MapRow> rows)
{
    var csv = new StringBuilder();
    csv.AppendLine("id;status;questionSimilarity;lowestOptionScore;mapping(official->dataset);officialCorrect;datasetCorrect;notes");
    foreach (var row in rows)
    {
        csv.AppendLine(string.Join(";", new[]
        {
            Csv(row.Id),
            Csv(row.Status),
            row.QuestionSimilarity.ToString("F2", CultureInfo.InvariantCulture),
            row.LowestOptionScore.ToString("F2", CultureInfo.InvariantCulture),
            Csv(string.Join(",", row.Perm)),
            Csv(((char)('A' + row.OfficialCorrect)).ToString()),
            Csv(((char)('A' + row.DatasetCorrect)).ToString()),
            Csv(row.Notes)
        }));
    }

    File.WriteAllText(Path.Combine(directory, "answer-map.csv"), csv.ToString(), new UTF8Encoding(true));
}

static void WriteReviewCsv(string directory, List<ReviewItem> items)
{
    var csv = new StringBuilder();
    csv.AppendLine("id;status;page;question;optionA;optionB;optionC;optionD;correct;datasetText;questionSimilarity;lowestOptionScore;notes");
    foreach (var item in items)
    {
        csv.AppendLine(string.Join(";", new[]
        {
            Csv(item.Id),
            Csv(item.Status),
            item.Page.ToString(CultureInfo.InvariantCulture),
            Csv(item.De),
            Csv(item.OfficialOptions[0]),
            Csv(item.OfficialOptions[1]),
            Csv(item.OfficialOptions[2]),
            Csv(item.OfficialOptions[3]),
            Csv(item.OfficialCorrect.ToString()),
            Csv(item.DatasetText),
            item.QuestionSimilarity.ToString("F2", CultureInfo.InvariantCulture),
            item.LowestOptionScore.ToString("F2", CultureInfo.InvariantCulture),
            Csv(item.Notes)
        }));
    }

    File.WriteAllText(Path.Combine(directory, "review.csv"), csv.ToString(), new UTF8Encoding(true));
}

static void WriteJson(string path, object value)
{
    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    var json = value is JsonNode node ? node.ToJsonString(options) : JsonSerializer.Serialize(value, options);
    File.WriteAllText(path, json, new UTF8Encoding(false));
}

static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

internal sealed class PdfQuestion
{
    public required string Id { get; init; }
    public required int BamfNo { get; init; }
    public string? State { get; init; }
    public required string Topic { get; init; }
    public required int Page { get; init; }
    public string De { get; set; } = "";
    public List<string> Options { get; } = new();
}

internal sealed class DatasetOption
{
    public required string De { get; init; }
    public required string En { get; init; }
}

internal sealed class DatasetQuestion
{
    public required string Id { get; init; }
    public required string De { get; init; }
    public required string En { get; init; }
    public required int Correct { get; init; }
    public string? Image { get; init; }
    public string ExpDe { get; init; } = "";
    public string ExpEn { get; init; } = "";
    public List<DatasetOption> Options { get; } = new();
}

internal sealed class FinalQuestion
{
    public required string Id { get; init; }
    public required int BamfNo { get; init; }
    public string? State { get; init; }
    public required string Topic { get; init; }
    public required int Page { get; init; }
    public required string De { get; init; }
    public required string En { get; init; }
    public required List<string> OptionsDe { get; init; }
    public required string[] OptionsEn { get; init; }
    public required int Correct { get; init; }
    public string? Image { get; init; }
    public required string ExpDe { get; init; }
    public required string ExpEn { get; init; }
}

internal sealed record MapRow(
    string Id,
    string Status,
    double QuestionSimilarity,
    double LowestOptionScore,
    int[] Perm,
    int OfficialCorrect,
    int DatasetCorrect,
    string Notes);

internal sealed record ReviewItem(
    string Id,
    string Status,
    int Page,
    string De,
    string DatasetText,
    double QuestionSimilarity,
    double LowestOptionScore,
    string[] OfficialOptions,
    string[] DatasetOptions,
    char OfficialCorrect,
    char DatasetCorrect,
    string Notes);

internal sealed record UnresolvedItem(string Id, string Reason);

internal sealed class Override
{
    public int? Correct { get; init; }
    public string? En { get; init; }
    public string[]? OptionsEn { get; init; }
    public string? ExpDe { get; init; }
    public string? ExpEn { get; init; }
}

internal static class PdfParser
{
    private static readonly Dictionary<string, string> StateCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Baden-Württemberg"] = "BW",
        ["Bayern"] = "BY",
        ["Berlin"] = "BE",
        ["Brandenburg"] = "BB",
        ["Bremen"] = "HB",
        ["Hamburg"] = "HH",
        ["Hessen"] = "HE",
        ["Mecklenburg-Vorpommern"] = "MV",
        ["Niedersachsen"] = "NI",
        ["Nordrhein-Westfalen"] = "NW",
        ["Rheinland-Pfalz"] = "RP",
        ["Saarland"] = "SL",
        ["Sachsen"] = "SN",
        ["Sachsen-Anhalt"] = "ST",
        ["Schleswig-Holstein"] = "SH",
        ["Thüringen"] = "TH"
    };

    private static readonly HashSet<char> OptionMarkers = new()
    {
        '\uF0A3', '\u25A1', '\u2610', '\u2611', '\u2612', '\u25A0'
    };

    public static List<PdfQuestion> Parse(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var questions = new List<PdfQuestion>();
        var page = 1;
        string? stateName = null;
        PdfQuestion? current = null;

        foreach (var rawLine in lines)
        {
            page += rawLine.Count(character => character == '\f');
            var line = rawLine.Replace("\f", " ").Trim();
            if (line.Length == 0) continue;

            line = Regex.Replace(line, @"\s*Seite\s+\d+\s+von\s+\d+\s*", " ").Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("Fragen für das Bundesland ", StringComparison.Ordinal))
            {
                stateName = line["Fragen für das Bundesland ".Length..].Trim();
                current = null;
                continue;
            }

            if (line == "Teil I") { stateName = null; current = null; continue; }
            if (line == "Teil II") { current = null; continue; }

            var match = Regex.Match(line, @"^Aufgabe (\d+)$");
            if (match.Success)
            {
                var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var code = stateName is null ? null : StateCodes[stateName];
                current = new PdfQuestion
                {
                    BamfNo = number,
                    State = code,
                    Topic = code is null ? TopicFor(number) : "state",
                    Page = page,
                    Id = code is null ? $"B{number:000}" : $"{code}{number:00}"
                };
                questions.Add(current);
                continue;
            }

            if (current is null) continue;
            if (line.StartsWith('©')) continue;
            if (Regex.IsMatch(line, @"^Bild \d(\s+Bild \d)*$")) continue;

            if (OptionMarkers.Contains(line[0]))
            {
                var option = Clean(line[1..]);
                if (option.Length > 0) current.Options.Add(option);
            }
            else if (current.Options.Count > 0)
            {
                current.Options[^1] = Join(current.Options[^1], line);
            }
            else
            {
                current.De = Join(current.De, line);
            }
        }

        return questions;
    }

    public static void Validate(List<PdfQuestion> questions)
    {
        var federal = questions.Where(question => question.State is null).ToList();
        var stateQuestions = questions.Where(question => question.State is not null).ToList();
        var problems = new List<string>();

        if (federal.Count != 300) problems.Add($"federal count {federal.Count}");
        if (stateQuestions.Count != 160) problems.Add($"state count {stateQuestions.Count}");

        foreach (var group in stateQuestions.GroupBy(question => question.State))
        {
            if (group.Count() != 10) problems.Add($"{group.Key} count {group.Count()}");
        }

        foreach (var question in questions)
        {
            if (question.De.Length == 0) problems.Add($"{question.Id}: empty question text");
            if (question.Options.Count != 4) problems.Add($"{question.Id}: {question.Options.Count} options");
            if (question.Options.Any(string.IsNullOrWhiteSpace)) problems.Add($"{question.Id}: empty option");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException("PDF parse validation failed:\n  " + string.Join("\n  ", problems));
        }
    }

    private static string Clean(string value) => Regex.Replace(value, @"\s+", " ").Trim();
    private static string Join(string existing, string value) => Clean(existing.Length == 0 ? value : existing + " " + value);
    private static string TopicFor(int number) => number switch
    {
        <= 150 => "democracy",
        <= 250 => "history",
        _ => "society"
    };
}

internal sealed class Dataset
{
    private static readonly Dictionary<string, string> SlugToCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["baden-wuerttemberg"] = "BW",
        ["bayern"] = "BY",
        ["berlin"] = "BE",
        ["brandenburg"] = "BB",
        ["bremen"] = "HB",
        ["hamburg"] = "HH",
        ["hessen"] = "HE",
        ["mecklenburg-vorpommern"] = "MV",
        ["niedersachsen"] = "NI",
        ["nordrhein-westfalen"] = "NW",
        ["rheinland-pfalz"] = "RP",
        ["saarland"] = "SL",
        ["sachsen"] = "SN",
        ["sachsen-anhalt"] = "ST",
        ["schleswig-holstein"] = "SH",
        ["thueringen"] = "TH"
    };

    private readonly Dictionary<int, DatasetQuestion> federal = new();
    private readonly Dictionary<string, List<DatasetQuestion>> states = new();

    public static Dataset Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var dataset = new Dataset();

        foreach (var item in root.GetProperty("general").EnumerateArray())
        {
            var number = item.GetProperty("id").GetInt32();
            dataset.federal[number] = Read(item, $"B{number:000}");
        }

        foreach (var stateProperty in root.GetProperty("states").EnumerateObject())
        {
            var code = SlugToCode[stateProperty.Name];
            var questions = new List<DatasetQuestion>();
            foreach (var item in stateProperty.Value.GetProperty("questions").EnumerateArray())
            {
                questions.Add(Read(item, item.GetProperty("id").GetString() ?? code));
            }
            dataset.states[code] = questions;
        }

        return dataset;
    }

    public DatasetQuestion? Find(PdfQuestion question)
    {
        if (question.State is null)
        {
            return federal.TryGetValue(question.BamfNo, out var federalQuestion) ? federalQuestion : null;
        }

        return states.TryGetValue(question.State, out var list) && question.BamfNo >= 1 && question.BamfNo <= list.Count
            ? list[question.BamfNo - 1]
            : null;
    }

    private static DatasetQuestion Read(JsonElement item, string id)
    {
        var question = new DatasetQuestion
        {
            Id = id,
            De = item.GetProperty("de").GetString() ?? "",
            En = item.GetProperty("en").GetString() ?? "",
            Correct = item.GetProperty("correct").GetInt32(),
            Image = item.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.String
                ? image.GetString()
                : null,
            ExpDe = item.TryGetProperty("exp_de", out var expDe) ? expDe.GetString() ?? "" : "",
            ExpEn = item.TryGetProperty("exp_en", out var expEn) ? expEn.GetString() ?? "" : ""
        };

        foreach (var option in item.GetProperty("options").EnumerateArray())
        {
            question.Options.Add(new DatasetOption
            {
                De = option.GetProperty("de").GetString() ?? "",
                En = option.GetProperty("en").GetString() ?? ""
            });
        }

        return question;
    }
}

internal static class Overrides
{
    public static Dictionary<string, Override> Load(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, Override>(StringComparer.Ordinal);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var overrides = JsonSerializer.Deserialize<Dictionary<string, Override>>(File.ReadAllText(path), options);
        return overrides is null
            ? new Dictionary<string, Override>(StringComparer.Ordinal)
            : new Dictionary<string, Override>(overrides, StringComparer.Ordinal);
    }
}

internal sealed record MapResult(int[] Perm, double MinScore, double Total);

internal static class OptionMapper
{
    public static MapResult Map(List<string> official, List<string> dataset)
    {
        var scores = new double[4, 4];
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                scores[i, j] = Score(official[i], dataset[j]);
            }
        }

        var permutations = new List<int[]>();
        CollectPermutations(Enumerable.Range(0, 4).ToArray(), 0, permutations);

        var best = permutations[0];
        var bestTotal = double.MinValue;
        var bestMin = double.MinValue;

        foreach (var permutation in permutations)
        {
            var total = 0.0;
            var min = double.MaxValue;
            for (var i = 0; i < 4; i++)
            {
                var score = scores[i, permutation[i]];
                total += score;
                min = Math.Min(min, score);
            }

            if (total > bestTotal + 0.0001 || (Math.Abs(total - bestTotal) <= 0.0001 && min > bestMin))
            {
                best = permutation;
                bestTotal = total;
                bestMin = min;
            }
        }

        return new MapResult((int[])best.Clone(), bestMin, bestTotal);
    }

    private static void CollectPermutations(int[] values, int start, List<int[]> output)
    {
        if (start == values.Length - 1)
        {
            output.Add((int[])values.Clone());
            return;
        }

        for (var i = start; i < values.Length; i++)
        {
            (values[start], values[i]) = (values[i], values[start]);
            CollectPermutations(values, start + 1, output);
            (values[start], values[i]) = (values[i], values[start]);
        }
    }

    private static double Score(string official, string dataset)
    {
        var normalizedOfficial = Similarity.Normalize(official);
        var normalizedDataset = Similarity.Normalize(dataset);
        if (normalizedOfficial.Length == 0 || normalizedDataset.Length == 0) return 0;
        if (normalizedOfficial == normalizedDataset) return 1;

        if (TryImageIndex(official, out var officialIndex) && TryImageIndex(dataset, out var datasetIndex))
        {
            return officialIndex == datasetIndex ? 1 : 0;
        }

        var jaccard = Similarity.Jaccard(official, dataset);
        var dice = Similarity.Dice(normalizedOfficial, normalizedDataset);
        return Math.Max(jaccard, dice);
    }

    private static bool TryImageIndex(string value, out int index)
    {
        var match = Regex.Match(value.Trim(), @"^(?:Bild\s*)?(\d+)$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return true;
        }

        index = -1;
        return false;
    }
}

internal static class Similarity
{
    public static double Jaccard(string left, string right)
    {
        var leftTokens = Tokens(left);
        var rightTokens = Tokens(right);
        if (leftTokens.Count == 0 && rightTokens.Count == 0) return 1;
        if (leftTokens.Count == 0 || rightTokens.Count == 0) return 0;

        var intersection = leftTokens.Count(token => rightTokens.Contains(token));
        var union = leftTokens.Count + rightTokens.Count - intersection;
        return union == 0 ? 1 : intersection / (double)union;
    }

    public static double Dice(string left, string right)
    {
        if (left.Length < 2 || right.Length < 2) return left == right ? 1 : 0;

        var leftBigrams = Bigrams(left);
        var rightBigrams = Bigrams(right);
        var intersection = 0;
        foreach (var (bigram, count) in leftBigrams)
        {
            if (rightBigrams.TryGetValue(bigram, out var rightCount))
            {
                intersection += Math.Min(count, rightCount);
            }
        }

        return 2.0 * intersection / (BigramCount(leftBigrams) + BigramCount(rightBigrams));
    }

    public static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormC).ToLowerInvariant())
        {
            if (character == 'ß')
            {
                builder.Append("ss");
            }
            else if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    public static HashSet<string> Tokens(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormC).ToLowerInvariant())
        {
            if (character == 'ß')
            {
                builder.Append("ss");
            }
            else if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else
            {
                builder.Append(' ');
            }
        }

        return builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, int> Bigrams(string value)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < value.Length - 1; i++)
        {
            var bigram = value.Substring(i, 2);
            result[bigram] = result.TryGetValue(bigram, out var count) ? count + 1 : 1;
        }

        return result;
    }

    private static int BigramCount(Dictionary<string, int> bigrams) => bigrams.Values.Sum();
}
