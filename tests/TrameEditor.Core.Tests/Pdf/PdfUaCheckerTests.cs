using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.IO.Font.Constants;
using TrameEditor.Core.Markdown;
using TrameEditor.Core.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Pdf;

public class PdfUaCheckerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-pdfua-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Un PDF spoglio: niente lingua, niente titolo, niente marcatura —
    /// come quelli che escono da mezzo mondo.</summary>
    private string CreatePdf(string name)
    {
        var path = Path.Combine(_dir, name);
        using var document = new PdfDocument(new PdfWriter(path));
        var page = document.AddNewPage();
        var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        new PdfCanvas(page)
            .BeginText()
            .SetFontAndSize(font, 12)
            .MoveText(60, 700)
            .ShowText("Un paragrafo di prova.")
            .EndText();
        return path;
    }

    /// <summary>Un PDF prodotto da TrameEditor: la lingua e il titolo li mette già lui.</summary>
    private string CreateExportedPdf(string name)
    {
        var path = Path.Combine(_dir, name);
        MarkdownPdfExporter.Export("# Titolo\n\nUn paragrafo di prova.", "Documento di prova", path);
        return path;
    }

    [Fact]
    public void Un_pdf_normale_non_e_marcato_e_lo_dice()
    {
        var report = PdfUaChecker.Analyze(CreatePdf("normale.pdf"));

        Assert.False(report.IsTagged);
        Assert.Contains(report.Blocking, i => i.Description.Contains("non è marcato"));
        Assert.False(report.NothingFound);
    }

    [Fact]
    public void Lingua_titolo_e_visualizzazione_del_titolo_sono_rimediabili()
    {
        var report = PdfUaChecker.Analyze(CreatePdf("spoglio.pdf"));

        Assert.Contains(report.Fixable, i => i.Description.Contains("lingua"));
        Assert.Contains(report.Fixable, i => i.Description.Contains("titolo del documento"));
        Assert.Contains(report.Fixable, i => i.Description.Contains("mostrare il titolo"));
    }

    /// <summary>Il nostro export mette già lingua e titolo: se un giorno smettesse
    /// di farlo, questo test se ne accorge.</summary>
    [Fact]
    public void I_pdf_prodotti_da_TrameEditor_hanno_gia_lingua_e_titolo()
    {
        var report = PdfUaChecker.Analyze(CreateExportedPdf("esportato.pdf"));

        Assert.False(string.IsNullOrWhiteSpace(report.Language));
        Assert.Equal("Documento di prova", report.Title);
    }

    [Fact]
    public void Fix_scrive_lingua_titolo_e_richiesta_di_mostrarlo()
    {
        var source = CreatePdf("da-sistemare.pdf");
        var target = Path.Combine(_dir, "sistemato.pdf");

        var report = PdfUaChecker.Fix(source, target, "it-IT", "Relazione annuale");

        Assert.Equal("it-IT", report.Language);
        Assert.Equal("Relazione annuale", report.Title);
        Assert.DoesNotContain(report.Fixable, i => i.Description.Contains("lingua"));
        Assert.DoesNotContain(report.Fixable, i => i.Description.Contains("titolo"));
        Assert.DoesNotContain(report.Fixable, i => i.Description.Contains("mostrare il titolo"));
    }

    [Fact]
    public void Fix_non_inventa_la_marcatura_e_continua_a_dichiararla_mancante()
    {
        var source = CreatePdf("non-marcato.pdf");
        var target = Path.Combine(_dir, "comunque-non-marcato.pdf");

        var report = PdfUaChecker.Fix(source, target);

        Assert.False(report.IsTagged);
        Assert.Contains(report.Blocking, i => i.Description.Contains("marcato"));
        Assert.False(report.NothingFound);
    }

    [Fact]
    public void Fix_senza_lingua_indicata_usa_l_italiano_e_il_nome_del_file()
    {
        var source = CreatePdf("verbale.pdf");
        var target = Path.Combine(_dir, "verbale-accessibile.pdf");

        var report = PdfUaChecker.Fix(source, target);

        Assert.Equal(PdfUaChecker.DefaultLanguage, report.Language);
        Assert.Equal("verbale", report.Title);
    }

    [Fact]
    public void Fix_non_tocca_l_originale()
    {
        var source = CreatePdf("intatto.pdf");
        var prima = File.ReadAllBytes(source);

        PdfUaChecker.Fix(source, Path.Combine(_dir, "copia.pdf"), "it-IT", "Titolo");

        Assert.Equal(prima, File.ReadAllBytes(source));
        Assert.Null(PdfUaChecker.Analyze(source).Language);
    }

    /// <summary>
    /// Rimontare le pagine non deve cancellare l'identità del documento: senza
    /// titolo e lingua un file accessibile smette di esserlo, e la verifica
    /// segnalerebbe problemi che il documento non ha.
    /// </summary>
    [Fact]
    public void Rimontare_le_pagine_conserva_lingua_titolo_e_visualizzazione()
    {
        var source = CreatePdf("originale.pdf");
        var accessibile = Path.Combine(_dir, "accessibile.pdf");
        PdfUaChecker.Fix(source, accessibile, "it-IT", "Relazione");

        var rimontato = Path.Combine(_dir, "rimontato.pdf");
        PdfPageOperations.Build(accessibile, [new PdfPageEdit(0, 0)], rimontato);

        var report = PdfUaChecker.Analyze(rimontato);
        Assert.Equal("it-IT", report.Language);
        Assert.Equal("Relazione", report.Title);
        Assert.DoesNotContain(report.Fixable, i => i.Description.Contains("mostrare il titolo"));
    }

    [Fact]
    public void Un_pdf_protetto_da_password_viene_dichiarato_non_esaminabile()
    {
        var source = CreatePdf("chiaro.pdf");
        var locked = Path.Combine(_dir, "chiuso.pdf");
        PdfCryptoService.Encrypt(source, locked, "segreto");

        var report = PdfUaChecker.Analyze(locked);

        Assert.Contains(report.Blocking, i => i.Description.Contains("password"));
        Assert.Equal(0, report.PageCount);
    }

    [Fact]
    public void Le_immagini_senza_marcatura_sono_segnalate()
    {
        var immagine = Path.Combine(_dir, "punto.png");
        File.WriteAllBytes(immagine, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        var pdf = Path.Combine(_dir, "con-immagine.pdf");
        ImagesToPdfConverter.Convert([immagine], pdf);

        var report = PdfUaChecker.Analyze(pdf);

        Assert.Contains(report.Blocking, i => i.Description.Contains("testo alternativo"));
    }
}
