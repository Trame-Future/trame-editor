using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TrameEditor.Core.Signatures;

namespace TrameEditor.App;

/// <summary>
/// Che cosa dicono le firme di un documento: chi ha firmato, quando, e se il
/// documento è ancora quello firmato. Il riquadro in fondo dice sempre che cosa
/// <b>non</b> abbiamo verificato: un bollino verde che promette più di quanto
/// sappiamo sarebbe peggio di nessun bollino.
/// </summary>
public partial class SignaturesWindow : Window
{
    /// <summary>Pubblica di proposito: WPF non riesce a legare i dati alle
    /// proprietà di un tipo non pubblico, e il pannello resterebbe vuoto.</summary>
    public sealed class Row
    {
        public required string Symbol { get; init; }
        public required Brush Accent { get; init; }
        public required string SignerName { get; init; }
        public required string Verdict { get; init; }
        public required IReadOnlyList<string> Details { get; init; }
    }

    private SignaturesWindow(string header, IEnumerable<Row> rows)
    {
        InitializeComponent();
        HeaderText.Text = header;
        DisclaimerText.Text = PdfSignatureInspector.LegalDisclaimer;
        SignatureList.ItemsSource = rows.ToList();
    }

    /// <summary>Firme di una busta .p7m.</summary>
    public static void ShowFor(string fileName, IReadOnlyList<SignerDetail> signers)
    {
        var header = signers.Count == 1
            ? $"\"{fileName}\" è una busta firmata: dentro c'era il documento che stiamo aprendo."
            : $"\"{fileName}\" è una busta con {signers.Count} firme: dentro c'era il documento che stiamo aprendo.";
        Show(header, signers.Select(s => RowFor(s, null)));
    }

    /// <summary>Firme apposte dentro un PDF.</summary>
    public static void ShowFor(string fileName, IReadOnlyList<PdfSignatureDetail> signatures)
    {
        if (signatures.Count == 0)
        {
            MessageBox.Show($"\"{fileName}\" non contiene firme digitali.",
                "TrameEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var header = signatures.Count == 1
            ? $"\"{fileName}\" contiene una firma digitale."
            : $"\"{fileName}\" contiene {signatures.Count} firme digitali.";
        Show(header, signatures.Select(s => RowFor(s.Signer, s)));
    }

    private static void Show(string header, IEnumerable<Row> rows) =>
        new SignaturesWindow(header, rows) { Owner = Application.Current.MainWindow }.ShowDialog();

    private static Row RowFor(SignerDetail signer, PdfSignatureDetail? signature)
    {
        var problems = new List<string>();
        if (!signer.IntegrityVerified)
            problems.Add(signer.Problem ?? "la firma non risulta verificabile");
        if (!signer.CertificateValidAtSigning && signer.ValidTo != DateTime.MinValue)
            problems.Add("il certificato non era valido nel momento della firma");
        if (signature is { CoversWholeDocument: false })
            problems.Add("la firma non copre tutto il file: il documento è stato modificato dopo");

        var good = problems.Count == 0;
        var details = new List<string>();

        if (signer.SignedAt is { } when)
            details.Add($"Firmato il {when.ToLocalTime().ToString("dd/MM/yyyy 'alle' HH:mm", CultureInfo.GetCultureInfo("it-IT"))}");
        else
            details.Add("Data di firma non dichiarata");

        if (signer.ValidTo != DateTime.MinValue)
            details.Add($"Certificato emesso da {signer.IssuerName}, valido dal " +
                $"{signer.ValidFrom.ToLocalTime():dd/MM/yyyy} al {signer.ValidTo.ToLocalTime():dd/MM/yyyy}");

        if (signature is not null)
        {
            details.Add($"Algoritmo {signature.Algorithm} · campo \"{signature.FieldName}\"");
            if (signature.Reason is not null)
                details.Add($"Motivo: {signature.Reason}");
            if (signature.Location is not null)
                details.Add($"Luogo: {signature.Location}");
        }

        foreach (var problem in problems)
            details.Add("⚠ " + char.ToUpperInvariant(problem[0]) + problem[1..]);

        return new Row
        {
            Symbol = good ? "✔" : "✖",
            Accent = good ? Brushes.SeaGreen : Brushes.Firebrick,
            SignerName = signer.DisplayName,
            Verdict = good
                ? "Firma integra: il documento non è stato modificato dopo la firma."
                : "Attenzione: la firma presenta problemi.",
            Details = details,
        };
    }
}
