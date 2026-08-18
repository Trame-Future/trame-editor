using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Xobject;
using TrameEditor.Core.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Pdf;

/// <summary>
/// Editing del testo disegnato dentro un form XObject: è il modo in cui molti
/// gestionali compongono documenti di trasporto e fatture, e fino alla 2.11 la
/// modifica veniva rifiutata perché la riscrittura si fermava al flusso della pagina.
/// Il form è condiviso per natura, quindi qui si verifica anche che la modifica
/// resti confinata all'occorrenza toccata.
/// </summary>
public class PdfTextEditXObjectTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-xobject-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string NewPath() => Path.Combine(_dir, $"{Guid.NewGuid():N}.pdf");

    /// <summary>Riga di testo dentro un form XObject richiamato dalla pagina in
    /// <paramref name="offsetX"/>/<paramref name="offsetY"/>: con uno scostamento le
    /// coordinate interne al modulo e quelle della pagina non coincidono più.</summary>
    private string CreateWithForm(string text, float offsetX = 0, float offsetY = 0)
    {
        var path = NewPath();
        using (var document = new PdfDocument(new PdfWriter(path)))
        {
            var page = document.AddNewPage();
            var form = NewTextForm(document, text);
            new PdfCanvas(page).AddXObjectAt(form, offsetX, offsetY);
        }
        return path;
    }

    private static PdfFormXObject NewTextForm(PdfDocument document, string text)
    {
        var form = new PdfFormXObject(new Rectangle(0, 0, 595, 842));
        new PdfCanvas(form, document)
            .BeginText()
            .SetFontAndSize(PdfFontFactory.CreateFont(StandardFonts.HELVETICA), 12)
            .SetTextMatrix(50, 700)
            .ShowText(text)
            .EndText();
        return form;
    }

    private static PdfTextLine LineContaining(string path, string text)
    {
        using var inspector = new PdfTextInspector(path);
        return inspector.GetLines(1).Single(l => l.Text.Contains(text));
    }

    private static string TextOfPage(string path, int pageNumber)
    {
        using var inspector = new PdfTextInspector(path);
        return string.Join(" ", inspector.GetLines(pageNumber).Select(l => l.Text));
    }

    /// <summary>Quanti XObject sono elencati nelle risorse della pagina: serve a
    /// verificare che un modulo non toccato non venga duplicato.</summary>
    private static int XObjectCount(string path, int pageNumber)
    {
        using var document = new PdfDocument(new PdfReader(path));
        var xobjects = document.GetPage(pageNumber).GetResources().GetResource(PdfName.XObject);
        return xobjects?.KeySet().Count ?? 0;
    }

    [Fact]
    public void Inspector_VedeIlTestoDentroIlModulo()
    {
        var path = CreateWithForm("Spett.le VOTINO");

        var line = LineContaining(path, "VOTINO");

        Assert.True(line.IsEditable);
        Assert.Null(line.NotEditableReason);
    }

    [Fact]
    public void Sostituisce_IlTestoDentroUnFormXObject()
    {
        var source = CreateWithForm("Spett.le VOTINO");
        var target = NewPath();
        var line = LineContaining(source, "VOTINO");

        PdfTextReplacer.Replace(source, target, line, "Spett.le ROSSI",
            PdfTextReplacer.PlanFor(source, line, "Spett.le ROSSI"));

        var text = TextOfPage(target, 1);
        Assert.Contains("ROSSI", text);
        Assert.DoesNotContain("VOTINO", text);
    }

    [Fact]
    public void Sostituisce_AncheQuandoIlModuloEDisegnatoConUnoScostamento()
    {
        var source = CreateWithForm("Spett.le VOTINO", offsetX: 40, offsetY: 25);
        var target = NewPath();
        var line = LineContaining(source, "VOTINO");

        PdfTextReplacer.Replace(source, target, line, "Spett.le ROSSI",
            PdfTextReplacer.PlanFor(source, line, "Spett.le ROSSI"));

        var text = TextOfPage(target, 1);
        Assert.Contains("ROSSI", text);
        Assert.DoesNotContain("VOTINO", text);

        // Il testo nuovo va dove stava il vecchio, non nell'origine del modulo.
        var replaced = LineContaining(target, "ROSSI");
        Assert.Equal(line.BaselineX, replaced.BaselineX, 1);
        Assert.Equal(line.BaselineY, replaced.BaselineY, 1);
    }

    [Fact]
    public void Sostituisce_AncheQuandoIlModuloEDisegnatoInScala()
    {
        // A mezza scala il testo, che dentro il modulo è a 12pt in (50, 700), sulla
        // pagina appare a 6pt in (125, 450): la riga si cerca in coordinate pagina.
        var source = NewPath();
        using (var document = new PdfDocument(new PdfWriter(source)))
        {
            var page = document.AddNewPage();
            var form = NewTextForm(document, "Spett.le VOTINO");
            new PdfCanvas(page).AddXObjectWithTransformationMatrix(form, 0.5f, 0, 0, 0.5f, 100, 100);
        }
        var target = NewPath();
        var line = LineContaining(source, "VOTINO");

        PdfTextReplacer.Replace(source, target, line, "Spett.le ROSSI",
            PdfTextReplacer.PlanFor(source, line, "Spett.le ROSSI"));

        var replaced = LineContaining(target, "ROSSI");
        Assert.DoesNotContain("VOTINO", TextOfPage(target, 1));
        Assert.Equal(line.BaselineX, replaced.BaselineX, 1);
        Assert.Equal(line.BaselineY, replaced.BaselineY, 1);
        // Il corpo del testo nuovo è quello che si vedeva, non quello interno al modulo.
        Assert.Equal(line.FontSizePt, replaced.FontSizePt, 1);
    }

    [Fact]
    public void Sostituisce_IlTestoDentroUnModuloAnnidato()
    {
        var source = NewPath();
        using (var document = new PdfDocument(new PdfWriter(source)))
        {
            var page = document.AddNewPage();
            var inner = NewTextForm(document, "Spett.le VOTINO");
            var outer = new PdfFormXObject(new Rectangle(0, 0, 595, 842));
            new PdfCanvas(outer, document).AddXObjectAt(inner, 0, 0);
            new PdfCanvas(page).AddXObjectAt(outer, 0, 0);
        }
        var target = NewPath();
        var line = LineContaining(source, "VOTINO");

        PdfTextReplacer.Replace(source, target, line, "Spett.le ROSSI",
            PdfTextReplacer.PlanFor(source, line, "Spett.le ROSSI"));

        var text = TextOfPage(target, 1);
        Assert.Contains("ROSSI", text);
        Assert.DoesNotContain("VOTINO", text);
    }

    [Fact]
    public void ModuloCondiviso_CambiaSoloLaPaginaModificata()
    {
        var source = NewPath();
        using (var document = new PdfDocument(new PdfWriter(source)))
        {
            var form = NewTextForm(document, "Spett.le VOTINO");
            new PdfCanvas(document.AddNewPage()).AddXObjectAt(form, 0, 0);
            new PdfCanvas(document.AddNewPage()).AddXObjectAt(form, 0, 0);
        }
        var target = NewPath();
        var line = LineContaining(source, "VOTINO");

        PdfTextReplacer.Replace(source, target, line, "Spett.le ROSSI",
            PdfTextReplacer.PlanFor(source, line, "Spett.le ROSSI"));

        Assert.Contains("ROSSI", TextOfPage(target, 1));
        Assert.DoesNotContain("VOTINO", TextOfPage(target, 1));
        // La seconda pagina richiama lo stesso modulo: non deve essersi accorta di nulla.
        Assert.Contains("VOTINO", TextOfPage(target, 2));
    }

    [Fact]
    public void ModuloNonToccato_RestaIlRiferimentoOriginale()
    {
        var source = NewPath();
        using (var document = new PdfDocument(new PdfWriter(source)))
        {
            var page = document.AddNewPage();
            new PdfCanvas(page).AddXObjectAt(NewTextForm(document, "Intestazione fissa"), 0, 0);
            new PdfCanvas(page)
                .BeginText()
                .SetFontAndSize(PdfFontFactory.CreateFont(StandardFonts.HELVETICA), 12)
                .SetTextMatrix(50, 600)
                .ShowText("Riga sulla pagina")
                .EndText();
        }
        var target = NewPath();
        var line = LineContaining(source, "Riga sulla pagina");

        PdfTextReplacer.Replace(source, target, line, "Riga corretta",
            PdfTextReplacer.PlanFor(source, line, "Riga corretta"));

        Assert.Contains("Riga corretta", TextOfPage(target, 1));
        Assert.Contains("Intestazione fissa", TextOfPage(target, 1));
        // Nessuna copia del modulo: non si duplica ciò che non si è modificato.
        Assert.Equal(XObjectCount(source, 1), XObjectCount(target, 1));
    }

    [Fact]
    public void PianoFont_TrovaIlFontDichiaratoNelleRisorseDelModulo()
    {
        var source = CreateWithForm("Spett.le VOTINO");
        var line = LineContaining(source, "VOTINO");

        var plan = PdfTextReplacer.PlanFor(source, line, "Spett.le ROSSI");

        Assert.Equal(PdfFontStrategy.ReuseEmbedded, plan.Strategy);
    }

    [Fact]
    public void IlTestoCorretto_SopravviveAlRimontaggioDellePagine()
    {
        // "Salva con nome" rimonta le pagine (PdfPageOperations.Build): la copia del
        // modulo dev'essere agganciata bene, o la correzione sparirebbe nel salvataggio.
        var source = CreateWithForm("Spett.le VOTINO");
        var edited = NewPath();
        var line = LineContaining(source, "VOTINO");
        PdfTextReplacer.Replace(source, edited, line, "Spett.le ROSSI",
            PdfTextReplacer.PlanFor(source, line, "Spett.le ROSSI"));

        var saved = NewPath();
        PdfPageOperations.Build(edited, [new PdfPageEdit(0, 0)], saved);

        var text = TextOfPage(saved, 1);
        Assert.Contains("ROSSI", text);
        Assert.DoesNotContain("VOTINO", text);
    }

    [Fact]
    public void Anonimizzazione_NonSaltaPiuLeRigheDentroIModuli()
    {
        var source = CreateWithForm("Spett.le VOTINO");
        var target = NewPath();
        var line = LineContaining(source, "VOTINO");

        var result = PdfTextReplacer.ReplaceMany(source, target, [(line, "Spett.le OMISSIS")]);

        Assert.Equal(1, result.LinesReplaced);
        Assert.Empty(result.SkippedLines);
        Assert.Contains("OMISSIS", TextOfPage(target, 1));
    }
}
