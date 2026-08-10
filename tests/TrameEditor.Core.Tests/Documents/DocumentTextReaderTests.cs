using TrameEditor.Core.Documents;
using TrameEditor.Core.Markdown;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Documents;

/// <summary>
/// Il punto in cui PDF, TXT e Markdown smettono di essere formati diversi. Ciò
/// che conta è che il riferimento sia vero: la pagina in un PDF, il numero di
/// riga <b>reale</b> in un file di testo — altrimenti l'assistente e il confronto
/// citerebbero posizioni inesistenti.
/// </summary>
public class DocumentTextReaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-doctext-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void ReadText_NumeraLeSezioniConLaLoroPrimaRiga()
    {
        var content = DocumentTextReader.ReadText(
            string.Join("\n", Enumerable.Range(1, 45).Select(i => $"riga {i}")));

        Assert.Equal(DocumentUnit.Riga, content.Unit);
        Assert.Equal([1, 21, 41], content.Sections.Select(s => s.Reference));
        Assert.StartsWith("riga 21", content.Sections[1].Text);
    }

    [Fact]
    public void ReadText_TollerraIFineRigaDiWindows()
    {
        var content = DocumentTextReader.ReadText("prima\r\nseconda\r\nterza");

        Assert.Single(content.Sections);
        Assert.DoesNotContain('\r', content.Sections[0].Text);
        Assert.Equal(1, content.Sections[0].Reference);
    }

    [Fact]
    public void ReadText_DocumentoVuoto_NessunaSezione()
    {
        Assert.Empty(DocumentTextReader.ReadText("   \n\n  \n").Sections);
    }

    [Fact]
    public void Read_Pdf_CitaLePagine()
    {
        var path = Path.Combine(_dir, "documento.pdf");
        MarkdownPdfExporter.Export("# Titolo\n\nUn paragrafo di prova.", "documento", path);

        var content = DocumentTextReader.Read(path);

        Assert.Equal(DocumentUnit.Pagina, content.Unit);
        Assert.Equal(1, content.Sections[0].Reference);
        Assert.Contains("Titolo", content.Sections[0].Text);
    }

    [Fact]
    public void ReadLines_FileDiTesto_SaltaLeRigheVuoteMaNonPerdeIlConto()
    {
        var path = Path.Combine(_dir, "appunti.txt");
        File.WriteAllText(path, "prima\n\n\nquarta\n");

        var (unit, lines) = DocumentTextReader.ReadLines(path);

        Assert.Equal(DocumentUnit.Riga, unit);
        Assert.Equal(2, lines.Count);
        Assert.Equal(("prima", 1), lines[0]);
        Assert.Equal(("quarta", 4), lines[1]); // le righe vuote non si citano ma si contano
    }

    [Theory]
    [InlineData("relazione.pdf", true)]
    [InlineData("appunti.txt", false)]
    [InlineData("guida.md", false)]
    public void IsSupported_RiconosceIFormatiConfrontabili(string name, bool isPdf)
    {
        Assert.True(DocumentTextReader.IsSupported(name));
        Assert.Equal(isPdf, DocumentTextReader.IsPdf(name));
    }
}
