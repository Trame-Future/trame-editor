using System.Text;
using iText.Kernel.Pdf;
using TrameEditor.Core.Markdown;
using TrameEditor.Core.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Pdf;

/// <summary>
/// Gli strumenti aggiunti nella 2.8: decorazioni della pagina, compressione con
/// un peso da rispettare, ricerca dentro una cartella di PDF.
/// </summary>
public class NewToolsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-tools-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreatePdf(string name, string markdown)
    {
        var path = Path.Combine(_dir, name);
        MarkdownPdfExporter.Export(markdown, name, path);
        return path;
    }

    private static string ExtractText(string path)
    {
        using var inspector = new PdfTextInspector(path);
        var text = new StringBuilder();
        for (var page = 1; page <= inspector.PageCount; page++)
        {
            foreach (var line in inspector.GetLines(page))
                text.AppendLine(line.Text);
        }
        return text.ToString();
    }

    /// <summary>Estrazione che vede anche il testo ruotato: il nostro
    /// <see cref="PdfTextInspector"/> lavora per righe orizzontali e una
    /// filigrana in diagonale non gli risulta.</summary>
    private static string ExtractTextIncludingRotated(string path)
    {
        using var document = new PdfDocument(new PdfReader(path));
        var text = new StringBuilder();
        for (var page = 1; page <= document.GetNumberOfPages(); page++)
        {
            text.AppendLine(iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(
                document.GetPage(page)));
        }
        return text.ToString();
    }

    // ----- Numeri di pagina, filigrana, intestazioni -----

    [Fact]
    public void Decorazioni_NumeriDiPagina_SonoNelDocumentoProdotto()
    {
        var source = CreatePdf("relazione.pdf", "# Relazione\n\nPrima pagina.\n\n\\pagebreak\n\nSeconda.");
        var target = Path.Combine(_dir, "numerato.pdf");

        var result = PdfDecorationService.Apply(source, target,
            numbering: new PageNumbering("Pagina {n} di {tot}"));

        Assert.Contains("numeri di pagina", result.Applied);
        var testo = ExtractText(target);
        Assert.Contains("Pagina 1 di", testo);
        Assert.Contains("Relazione", testo); // il contenuto originale è intatto
    }

    [Fact]
    public void Decorazioni_NumerazioneDaUnNumeroDiverso_ESaltandoLaPrima()
    {
        var source = CreatePdf("doc.pdf", "Solo una pagina");
        var target = Path.Combine(_dir, "saltato.pdf");

        var result = PdfDecorationService.Apply(source, target,
            numbering: new PageNumbering("{n}", SkipFirstPage: true));

        // Unica pagina, saltata: nessuna pagina decorata.
        Assert.Equal(0, result.PagesDecorated);
    }

    [Fact]
    public void Decorazioni_FiligranaEIntestazione_CompaionoNelTesto()
    {
        var source = CreatePdf("contratto.pdf", "# Contratto\n\nTesto del contratto.");
        var target = Path.Combine(_dir, "decorato.pdf");

        PdfDecorationService.Apply(source, target,
            watermark: new Watermark("COPIA"),
            headerFooter: new HeaderFooter(Header: "Trame Future srls", Footer: "Documento riservato"));

        var testo = ExtractTextIncludingRotated(target);
        Assert.Contains("COPIA", testo);
        Assert.Contains("Trame Future srls", testo);
        Assert.Contains("Documento riservato", testo);
        Assert.Contains("Testo del contratto", testo);

        // Limite noto, da conoscere: la ricerca testuale lavora per righe
        // orizzontali, quindi una filigrana in diagonale non è cercabile.
        Assert.DoesNotContain("COPIA", ExtractText(target));
    }

    [Fact]
    public void Decorazioni_SenzaNienteDaFare_Rifiuta()
    {
        var source = CreatePdf("vuoto.pdf", "niente");
        Assert.Throws<ArgumentException>(() =>
            PdfDecorationService.Apply(source, Path.Combine(_dir, "out.pdf")));
    }

    /// <summary>Le decorazioni non devono impedire l'archiviazione: il font usato
    /// viene incorporato, quindi il file resta convertibile in PDF/A.</summary>
    [Fact]
    public void Decorazioni_IlFileRestaConvertibileInPdfA()
    {
        var source = CreatePdf("archiviabile.pdf", "# Documento\n\nContenuto.");
        var decorato = Path.Combine(_dir, "decorato.pdf");
        var archivio = Path.Combine(_dir, "archivio.pdf");

        PdfDecorationService.Apply(source, decorato, numbering: new PageNumbering());
        var result = PdfAConverter.ConvertFaithfully(decorato, archivio, "documento");

        Assert.True(result.VerificationClean, string.Join("; ", result.Verification.Issues));
    }

    // ----- Compressione con un peso da rispettare -----

    [Fact]
    public void CompressToTarget_LimiteIrraggiungibile_LoDichiara()
    {
        var source = CreatePdf("documento.pdf", "# Titolo\n\nUn po' di testo da comprimere.");

        // 200 byte non li rispetta nemmeno un PDF vuoto.
        var result = PdfCompressor.CompressToTarget(source,
            Path.Combine(_dir, "compresso.pdf"), targetBytes: 200);

        Assert.False(result.TargetReached);
        Assert.Contains("nemmeno", result.Sacrifices); // lo dice, non fa finta di avercela fatta
        Assert.True(result.AfterBytes > 0, "il file prodotto esiste comunque");
    }

    [Fact]
    public void CompressToTarget_LimiteAmpio_SiFermaSubito()
    {
        var source = CreatePdf("documento.pdf", "# Titolo\n\nTesto.");
        var target = Path.Combine(_dir, "compresso.pdf");

        var result = PdfCompressor.CompressToTarget(source, target, targetBytes: 10_000_000);

        Assert.True(result.TargetReached);
        // Primo grado: il meno invasivo, il documento non viene degradato inutilmente.
        Assert.Equal(2200, result.MaxDimension);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void CompressToTarget_PesoNonPositivo_Rifiuta()
    {
        var source = CreatePdf("documento.pdf", "testo");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PdfCompressor.CompressToTarget(source, Path.Combine(_dir, "x.pdf"), 0));
    }

    // ----- Ricerca in una cartella -----

    [Fact]
    public void RicercaInCartella_TrovaFileEPagina()
    {
        var cartella = Directory.CreateDirectory(Path.Combine(_dir, "archivio")).FullName;
        MarkdownPdfExporter.Export("# Fattura 1\n\nCodice fiscale RSSMRA80A01H501U del cliente.",
            "f1", Path.Combine(cartella, "fattura-1.pdf"));
        MarkdownPdfExporter.Export("# Fattura 2\n\nAltro cliente, nessun codice.",
            "f2", Path.Combine(cartella, "fattura-2.pdf"));

        var report = FolderSearchService.Search(cartella, "RSSMRA80A01H501U");

        Assert.Equal(2, report.FilesSearched);
        var hit = Assert.Single(report.Hits);
        Assert.Equal("fattura-1.pdf", hit.FileName);
        Assert.Equal(1, hit.PageNumber);
        Assert.Contains("RSSMRA80A01H501U", hit.Snippet);
    }

    [Fact]
    public void RicercaInCartella_IgnoraMaiuscoleEMinuscole()
    {
        var cartella = Directory.CreateDirectory(Path.Combine(_dir, "archivio2")).FullName;
        MarkdownPdfExporter.Export("Contratto di MANUTENZIONE ordinaria",
            "c", Path.Combine(cartella, "contratto.pdf"));

        Assert.Single(FolderSearchService.Search(cartella, "manutenzione").Hits);
    }

    /// <summary>
    /// Un PDF senza testo (scansione senza OCR) non è un PDF in cui la parola
    /// non c'è: va elencato a parte, altrimenti si conclude il falso.
    /// </summary>
    [Fact]
    public void RicercaInCartella_ScansioniSenzaTesto_ElencatePerNonIngannare()
    {
        var cartella = Directory.CreateDirectory(Path.Combine(_dir, "archivio3")).FullName;
        MarkdownPdfExporter.Export("Documento con testo e parola chiave",
            "t", Path.Combine(cartella, "testuale.pdf"));

        // PDF di sola immagine: nessun testo estraibile.
        var scansione = Path.Combine(cartella, "scansione.pdf");
        using (var document = new PdfDocument(new PdfWriter(scansione)))
            document.AddNewPage();

        var report = FolderSearchService.Search(cartella, "parola");

        Assert.Single(report.Hits);
        var senzaTesto = Assert.Single(report.FilesWithoutText);
        Assert.EndsWith("scansione.pdf", senzaTesto);
    }

    [Fact]
    public void RicercaInCartella_SenzaChiaveDiRicerca_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => FolderSearchService.Search(_dir, "   "));
    }
}
