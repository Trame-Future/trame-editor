using PdfSharp.Drawing;
using PdfSharp.Fonts;
using TrameEditor.Core.Tables;

namespace TrameEditor.Core.Tests.Tables;

/// <summary>
/// Riconoscimento della griglia. La maggior parte delle prove parte da parole costruite a
/// mano: la geometria è tutto quello che conta, e senza un PDF di mezzo si possono descrivere
/// i casi difficili con precisione.
/// </summary>
public class PdfTableReaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-tabelle-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static PdfTableReaderTests()
    {
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new ArialResolver();
    }

    private sealed class ArialResolver : IFontResolver
    {
        public byte[]? GetFont(string faceName) => File.ReadAllBytes(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf"));

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) => new("Arial#");
    }

    /// <summary>Una parola larga 6 punti per carattere, come un Courier a corpo 10: basta che
    /// le proporzioni siano quelle vere.</summary>
    private static TableWord Word(string text, double left, double baseline) =>
        new(text, left, left + text.Length * 6, baseline);

    private static PdfTable Read(params TableWord[] words) =>
        PdfTableReader.FromWords(words, 1);

    [Fact]
    public void ReadsARowOfColumns()
    {
        var table = Read(
            Word("MELINDA", 60, 700), Word("75/80", 110, 700),
            Word("64", 258, 700),
            Word("10", 390, 700),
            Word("PERE", 60, 685), Word("ABATE", 100, 685),
            Word("30", 258, 685),
            Word("12", 390, 685));

        Assert.Equal(2, table.RowCount);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(["MELINDA 75/80", "64", "10"], table.Rows[0]);
    }

    /// <summary>
    /// Limite dichiarato: una riga sola non fa una tabella. Senza una seconda riga
    /// incolonnata allo stesso modo, non c'è modo di distinguere tre celle da una frase con
    /// spazi larghi — e inventarsi colonne che non ci sono è peggio che non trovarle.
    /// </summary>
    [Fact]
    public void ASingleRow_IsNotEnough()
    {
        var table = Read(
            Word("MELINDA", 60, 700), Word("75/80", 110, 700),
            Word("64", 258, 700), Word("10", 390, 700));

        Assert.True(table.IsEmpty);
    }

    [Fact]
    public void KeepsTheColumnsAlignedAcrossRows()
    {
        var table = Read(
            Word("Codice", 60, 700), Word("Quantita", 258, 700), Word("Prezzo", 390, 700),
            Word("AB1", 60, 685), Word("12", 258, 685), Word("3,50", 390, 685),
            Word("CD2", 60, 670), Word("7", 258, 670), Word("9,90", 390, 670));

        Assert.Equal(3, table.RowCount);
        Assert.All(table.Rows, row => Assert.Equal(3, row.Count));
        Assert.Equal(["CD2", "7", "9,90"], table.Rows[2]);
    }

    /// <summary>Le righe si leggono dall'alto verso il basso, mentre le coordinate PDF
    /// crescono verso l'alto: sbagliare il verso capovolgerebbe la tabella.</summary>
    [Fact]
    public void ReadsRowsFromTopToBottom()
    {
        var table = Read(
            Word("secondo", 60, 600), Word("B", 258, 600),
            Word("primo", 60, 700), Word("A", 258, 700));

        Assert.Equal("primo", table.Rows[0][0]);
        Assert.Equal("secondo", table.Rows[1][0]);
    }

    /// <summary>Il caso che ha fatto riscrivere il motore: due celle così vicine che
    /// l'estrazione le restituisce come un pezzo unico. Le colonne dedotte dalle altre righe
    /// devono comunque separarle.</summary>
    [Fact]
    public void SeparatesCellsThatAlmostTouch()
    {
        var table = Read(
            Word("Ctrl+N", 70, 700), Word("Nuovo", 207, 700),
            // Qui la prima cella arriva quasi a toccare la seconda: il vuoto è appena 7 punti.
            Word("Ctrl+Maiusc+S", 70, 685), Word("Salva", 207, 685),
            Word("F1", 70, 670), Word("Guida", 207, 670));

        Assert.Equal(3, table.RowCount);
        Assert.Equal(["Ctrl+Maiusc+S", "Salva"], table.Rows[1]);
    }

    /// <summary>La prosa cade sotto le stesse colonne della tabella ma non è tabella: si
    /// riconosce perché le sue parole si susseguono senza salti.</summary>
    [Fact]
    public void LeavesProseOut()
    {
        var table = Read(
            Word("Questa", 60, 720), Word("e", 100, 720), Word("una", 115, 720),
            Word("frase", 140, 720), Word("normale", 175, 720),
            Word("Codice", 60, 700), Word("Prezzo", 258, 700),
            Word("AB1", 60, 685), Word("3,50", 258, 685));

        Assert.Equal(2, table.RowCount);
        Assert.Equal(1, table.IgnoredLines);
        Assert.DoesNotContain(table.Rows, r => r[0].Contains("frase"));
    }

    [Fact]
    public void EmptyCellsStayEmpty()
    {
        var table = Read(
            Word("A", 60, 700), Word("B", 258, 700), Word("C", 390, 700),
            Word("solo", 60, 685), Word("terza", 390, 685),
            Word("D", 60, 670), Word("E", 258, 670), Word("F", 390, 670));

        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(["solo", "", "terza"], table.Rows[1]);
    }

    /// <summary>Una pagina di solo testo non è una tabella: meglio dire che non c'è niente
    /// che restituire una griglia inventata di una colonna.</summary>
    [Fact]
    public void APageWithoutTables_ReturnsNothing()
    {
        var table = Read(
            Word("Solo", 60, 700), Word("una", 90, 700), Word("frase", 110, 700),
            Word("e", 60, 685), Word("un'altra", 70, 685), Word("riga", 120, 685));

        Assert.True(table.IsEmpty);
        Assert.Equal(2, table.IgnoredLines);
    }

    [Fact]
    public void NoWords_IsNotAnError()
    {
        var table = PdfTableReader.FromWords([], 1);

        Assert.True(table.IsEmpty);
        Assert.Equal(0, table.ColumnCount);
    }

    /// <summary>
    /// È l'allineamento a fare una tabella, non la distanza. Queste parole sono lontanissime
    /// fra loro — molto più che in una tabella fitta — ma ogni riga comincia dove capita:
    /// nessuna colonna.
    /// </summary>
    [Fact]
    public void DistanceWithoutAlignment_IsNotATable()
    {
        var scattered = new[]
        {
            new TableWord("Titolo", 60, 120, 700),
            new TableWord("del", 190, 220, 700),
            new TableWord("documento", 310, 400, 700),
            new TableWord("Seconda", 95, 155, 685),
            new TableWord("riga", 240, 270, 685),
            new TableWord("qualunque", 360, 450, 685),
        };

        Assert.True(PdfTableReader.FromWords(scattered, 1).IsEmpty);
    }

    /// <summary>Il rovescio: parole vicinissime, ma incolonnate riga dopo riga. È una tabella
    /// stretta, e va letta come tale.</summary>
    [Fact]
    public void AlignmentWithoutDistance_IsATable()
    {
        var table = Read(
            Word("A1", 60, 700), Word("B1", 80, 700),
            Word("A2", 60, 685), Word("B2", 80, 685),
            Word("A3", 60, 670), Word("B3", 80, 670));

        Assert.Equal(3, table.RowCount);
        Assert.Equal(["A2", "B2"], table.Rows[1]);
    }

    [Fact]
    public void ReadsATableFromARealPdf()
    {
        var path = Path.Combine(_dir, "tabella.pdf");
        using (var document = new PdfSharp.Pdf.PdfDocument())
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 11);
            gfx.DrawString("Relazione sulle vendite del trimestre", font, XBrushes.Black, new XPoint(50, 60));
            var rows = new[]
            {
                ("Articolo", "Quantita", "Prezzo"),
                ("Mele Melinda", "64", "3,50"),
                ("Pere Abate", "10", "2,90"),
            };
            var y = 100.0;
            foreach (var (a, b, c) in rows)
            {
                gfx.DrawString(a, font, XBrushes.Black, new XPoint(50, y));
                gfx.DrawString(b, font, XBrushes.Black, new XPoint(250, y));
                gfx.DrawString(c, font, XBrushes.Black, new XPoint(400, y));
                y += 20;
            }
            document.Save(path);
        }

        var table = PdfTableReader.Read(path, 1);

        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(3, table.RowCount);
        Assert.Equal(["Articolo", "Quantita", "Prezzo"], table.Rows[0]);
        Assert.Equal(["Pere Abate", "10", "2,90"], table.Rows[2]);
        // Il titolo sopra la tabella non è una riga di dati.
        Assert.Equal(1, table.IgnoredLines);
    }
}
