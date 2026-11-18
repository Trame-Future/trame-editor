using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrameEditor.Core.Tables;

/// <summary>
/// Un campo da leggere: un nome e il rettangolo della pagina in cui cercarlo, in punti PDF
/// con l'origine in basso a sinistra (le stesse coordinate che l'estrazione già restituisce).
/// </summary>
public sealed record TemplateField(
    string Name,
    int PageNumber,
    double Left,
    double Bottom,
    double Width,
    double Height)
{
    public double Right => Left + Width;

    public double Top => Bottom + Height;

    /// <summary>Vero se la parola cade dentro l'area: si guarda il <b>centro</b>, così una
    /// parola che sporge di poco dal rettangolo disegnato a mano vale lo stesso.</summary>
    public bool Contains(double left, double right, double baseline)
    {
        var centre = (left + right) / 2;
        return centre >= Left && centre <= Right && baseline >= Bottom && baseline <= Top;
    }
}

/// <summary>
/// Il modello di un documento ricorrente: si segna una volta dove stanno i campi su un
/// esemplare, e da lì in poi ogni documento fatto allo stesso modo restituisce quei campi.
/// Si salva in un file JSON leggibile, così lo si può correggere a mano e passare a qualcun
/// altro senza esportare niente.
/// </summary>
public sealed record ExtractionTemplate(string Name, IReadOnlyList<TemplateField> Fields)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ExtractionTemplate Load(string path)
    {
        var text = File.ReadAllText(path);
        var template = JsonSerializer.Deserialize<ExtractionTemplate>(text, Options)
            ?? throw new InvalidOperationException(
                $"Il file del modello è vuoto o illeggibile: {path}");

        if (template.Fields.Count == 0)
            throw new InvalidOperationException(
                $"Il modello «{template.Name}» non contiene nessun campo: non c'è niente da estrarre.");
        return template;
    }

    public void Save(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(full, JsonSerializer.Serialize(this, Options));
    }

    /// <summary>Le pagine toccate dal modello, in ordine: serve a leggere solo quelle.</summary>
    public IReadOnlyList<int> Pages() => [.. Fields.Select(f => f.PageNumber).Distinct().Order()];
}
