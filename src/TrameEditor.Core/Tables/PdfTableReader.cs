using UglyToad.PdfPig;

namespace TrameEditor.Core.Tables;

/// <summary>Una parola con il suo ingombro sulla pagina: il mattone da cui si ricostruisce
/// la griglia.</summary>
public sealed record TableWord(string Text, double Left, double Right, double BaselineY);

/// <summary>Come riconoscere la griglia. I valori predefiniti valgono per i documenti dei
/// gestionali; si spostano quando un file vero non si lascia leggere.</summary>
public sealed record TableReadOptions
{
    /// <summary>Quanto possono differire due basi di scrittura e valere ancora come stessa
    /// riga: le tabelle non sono mai allineate al punto.</summary>
    public double RowTolerance { get; init; } = 3.0;

    /// <summary>Quanto può scostarsi l'inizio di una cella dal bordo della sua colonna e
    /// contare ancora come allineato.</summary>
    public double ColumnTolerance { get; init; } = 2.0;

    /// <summary>Quante righe devono cominciare a una certa distanza dal margine perché quella
    /// sia un bordo di colonna e non una coincidenza.</summary>
    public int MinimumRowsPerColumn { get; init; } = 2;

    /// <summary>Il vuoto minimo, in punti, perché una parola cominci una cella nuova. È solo
    /// un pavimento: la misura vera si ricava dagli spazi del documento.</summary>
    public double MinimumCellGap { get; init; } = 4.0;

    /// <summary>Quanti caratteri di larghezza deve valere un vuoto per separare due celle.
    /// Poco più di uno: uno spazio normale non separa niente, due sì.</summary>
    public double CellGapInCharacters { get; init; } = 1.2;

    /// <summary>Quante celle piene deve avere una riga per essere considerata parte della
    /// tabella. Con 2, i titoli e le frasi sciolte restano fuori.</summary>
    public int MinimumFilledCells { get; init; } = 2;
}

/// <summary>Una tabella letta da una pagina.</summary>
public sealed record PdfTable(
    int PageNumber,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<double> ColumnLefts,
    int IgnoredLines)
{
    public int RowCount => Rows.Count;

    public int ColumnCount => ColumnLefts.Count;

    public bool IsEmpty => Rows.Count == 0;
}

/// <summary>
/// Riconosce la griglia di una pagina. Il ragionamento è quello dell'occhio: le righe sono i
/// testi alla stessa altezza, le colonne sono le fasce verticali separate da vuoti.
/// <para>
/// Si parte dalle <b>parole</b>, non dai segmenti di riga già aggregati: quei segmenti sono
/// uniti con una soglia pensata per la lettura del testo, e su una tabella capita che due
/// celle vicine finiscano appiccicate in un pezzo solo — «Ctrl+S / Ctrl+Maiusc+S Salva / Salva
/// con nome» era una riga sola, e andava persa. Le colonne si indovinano dalle righe che si
/// separano bene, poi si applicano a tutte le parole, anche a quelle che nessuno aveva diviso.
/// </para>
/// <para>
/// Non si guardano le linee disegnate: moltissime tabelle non ne hanno, e quelle che le hanno
/// spesso le disegnano dove il testo non è. Il testo, invece, c'è sempre.
/// </para>
/// <para>
/// <b>Limite dichiarato:</b> una griglia per pagina. Due tabelle affiancate con
/// incolonnamenti diversi escono come una sola con le colonne di entrambe.
/// </para>
/// </summary>
public static class PdfTableReader
{
    public static PdfTable Read(string path, int pageNumber, TableReadOptions? options = null)
    {
        using var document = PdfDocument.Open(path);
        var page = document.GetPage(pageNumber);
        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .Select(w => new TableWord(
                w.Text,
                w.BoundingBox.Left,
                w.BoundingBox.Right,
                w.BoundingBox.Bottom))
            .ToList();
        return FromWords(words, pageNumber, options);
    }

    /// <summary>La stessa lettura a partire dalle parole già misurate: senza file di mezzo si
    /// può provare la geometria con casi costruiti a mano.</summary>
    public static PdfTable FromWords(IReadOnlyList<TableWord> words, int pageNumber,
        TableReadOptions? options = null)
    {
        options ??= new TableReadOptions();
        if (words.Count == 0)
            return new PdfTable(pageNumber, [], [], 0);

        var rows = GroupIntoRows(words, options.RowTolerance);

        // Prima passata: bordi da tutta la pagina, per capire quali righe sono incolonnate.
        var borders = FindColumnBorders(rows, options);
        if (borders.Count < options.MinimumFilledCells)
            return new PdfTable(pageNumber, [], [], rows.Count);

        var gap = CellGapOf(rows, options);
        var tableRows = rows.Where(r => IsTableRow(r, borders, options, gap)).ToList();
        if (tableRows.Count == 0)
            return new PdfTable(pageNumber, [], [], rows.Count);

        // Seconda passata: i bordi si ricalcolano sulle sole righe della tabella. Serve a
        // togliere le colonne che venivano dal resto della pagina — il margine sinistro della
        // prosa vota come una colonna, e senza questo passaggio la tabella esce con una
        // colonna vuota davanti.
        var refined = FindColumnBorders(tableRows, options);
        if (refined.Count >= options.MinimumFilledCells)
            borders = refined;

        var grid = new List<IReadOnlyList<string>>();
        var ignored = rows.Count - tableRows.Count;
        foreach (var row in tableRows)
        {
            var cells = ToCells(row, borders, options, gap);
            if (cells is null || cells.Count(c => c.Length > 0) < options.MinimumFilledCells)
            {
                ignored++;
                continue;
            }
            grid.Add(cells);
        }

        return new PdfTable(pageNumber, grid, borders, ignored);
    }

    /// <summary>Una riga sta nella tabella se si lascia distribuire nelle colonne e ne
    /// riempie almeno due.</summary>
    private static bool IsTableRow(List<TableWord> row, List<double> borders, TableReadOptions options, double gap)
    {
        var cells = ToCells(row, borders, options, gap);
        return cells is not null && cells.Count(c => c.Length > 0) >= options.MinimumFilledCells;
    }

    /// <summary>Righe: parole alla stessa altezza, dall'alto verso il basso. Le coordinate PDF
    /// crescono verso l'alto, quindi si ordina al contrario di come si legge.</summary>
    private static List<List<TableWord>> GroupIntoRows(IReadOnlyList<TableWord> words, double tolerance)
    {
        var rows = new List<List<TableWord>>();
        foreach (var word in words.OrderByDescending(w => w.BaselineY).ThenBy(w => w.Left))
        {
            var row = rows.FirstOrDefault(r => Math.Abs(r[0].BaselineY - word.BaselineY) <= tolerance);
            if (row is null)
                rows.Add([word]);
            else
                row.Add(word);
        }

        foreach (var row in rows)
            row.Sort((a, b) => a.Left.CompareTo(b.Left));
        return rows;
    }

    /// <summary>
    /// I bordi delle colonne: le distanze dal margine a cui <b>più righe</b> cominciano una
    /// cella. È l'allineamento a fare una tabella, ed è l'unico segnale che regge sia dove la
    /// pagina è quasi tutta tabella sia dove è quasi tutta prosa.
    /// <para>
    /// Misurare invece il vuoto fra le parole non regge: in una fattura, che è tabella dal
    /// primo rigo all'ultimo, i vuoti fra colonne sono la norma e non c'è un «vuoto normale»
    /// con cui confrontarli.
    /// </para>
    /// </summary>
    private static List<double> FindColumnBorders(List<List<TableWord>> rows, TableReadOptions options)
    {
        // Una riga vota una sola volta per ciascuna posizione: dieci parole che cominciano
        // tutte allo stesso punto della stessa riga non fanno una colonna.
        var gap = CellGapOf(rows, options);
        var votes = new Dictionary<double, HashSet<int>>();
        var leftmost = new Dictionary<double, double>();
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (var w = 0; w < row.Count; w++)
            {
                var word = row[w];

                // Vota solo chi comincia davvero una cella: la prima parola della riga, o
                // una preceduta da un vuoto largo. Senza questo filtro bastano due parole
                // che per caso cominciano alla stessa distanza dal margine — «Melinda» sotto
                // «Abate» — per inventare una colonna in mezzo a una cella di testo.
                if (w > 0 && word.Left - row[w - 1].Right < gap)
                    continue;

                // La chiave raggruppa le posizioni vicine; il bordo però è la posizione vera
                // più a sinistra del gruppo. Arrotondando anche il bordo si finirebbe per
                // metterlo a destra di qualche parola che dovrebbe cominciarci sopra, e
                // quella riga non risulterebbe più allineata a niente.
                var key = Math.Floor(word.Left / options.ColumnTolerance) * options.ColumnTolerance;
                if (!votes.TryGetValue(key, out var voters))
                {
                    votes[key] = voters = [];
                    leftmost[key] = word.Left;
                }
                leftmost[key] = Math.Min(leftmost[key], word.Left);
                voters.Add(r);
            }
        }

        var borders = votes
            .Where(v => v.Value.Count >= options.MinimumRowsPerColumn)
            .Select(v => leftmost[v.Key])
            .OrderBy(x => x)
            .ToList();

        // Bordi a un soffio l'uno dall'altro sono lo stesso bordo visto storto.
        var merged = new List<double>();
        foreach (var border in borders)
        {
            if (merged.Count > 0 && border - merged[^1] <= options.ColumnTolerance * 2)
                continue;
            merged.Add(border);
        }
        return merged;
    }

    /// <summary>
    /// Il vuoto oltre il quale una parola comincia una cella nuova invece di continuare
    /// quella in corso. Si stima dal quarto più basso dei vuoti della pagina — che sono gli
    /// spazi fra le parole, i più piccoli e i più numerosi — e si moltiplica: uno spazio
    /// normale non separa niente, il doppio di uno spazio sì.
    /// <para>
    /// Si prende il quartile e non la mediana perché in una fattura, che è tabella dal primo
    /// rigo all'ultimo, metà dei vuoti sono salti di colonna e la mediana ne verrebbe
    /// trascinata in alto fino a non riconoscere più nulla.
    /// </para>
    /// </summary>
    private static double CellGapOf(List<List<TableWord>> rows, TableReadOptions options)
    {
        // Si misura la larghezza tipica di un carattere e da lì si stima quanto vale uno
        // spazio. Contare invece i vuoti fra le parole non funziona dove le celle sono di una
        // parola sola — una tabella di codici e importi non ha spazi da misurare, e la stima
        // schizzerebbe in alto fino a non riconoscere più nessuna colonna.
        var widths = new List<double>();
        foreach (var row in rows)
        {
            foreach (var word in row)
            {
                if (word.Text.Length > 0 && word.Right > word.Left)
                    widths.Add((word.Right - word.Left) / word.Text.Length);
            }
        }
        if (widths.Count == 0)
            return options.MinimumCellGap;

        widths.Sort();
        var typicalCharacter = widths[widths.Count / 2];
        return Math.Max(options.MinimumCellGap, typicalCharacter * options.CellGapInCharacters);
    }

    /// <summary>
    /// Distribuisce le parole della riga nelle colonne. Restituisce <c>null</c> se la riga
    /// non è allineata alla griglia: è così che la prosa resta fuori: le sue parole cadono
    /// dove capita, mentre una riga di dati comincia ogni cella sul bordo della sua colonna.
    /// </summary>
    private static string[]? ToCells(List<TableWord> row, List<double> borders,
        TableReadOptions options, double gap)
    {
        var cells = new string[borders.Count];
        Array.Fill(cells, string.Empty);

        foreach (var group in GroupIntoCells(row, gap))
        {
            var index = ColumnOf(group.Left, borders);
            if (index < 0)
                return null; // comincia prima della prima colonna: non è di questa griglia

            // Il criterio non è che la cella cominci sul bordo — le intestazioni sono spesso
            // centrate e gli importi allineati a destra, e chiedere l'allineamento a sinistra
            // butterebbe via proprio le colonne dei numeri. Il criterio è che una cella non
            // scavalchi il bordo della colonna accanto: la prosa lo fa a ogni riga, un dato mai.
            var nextBorder = index + 1 < borders.Count ? borders[index + 1] : double.MaxValue;
            if (group.Right > nextBorder + options.ColumnTolerance)
                return null;

            cells[index] = cells[index].Length == 0 ? group.Text : $"{cells[index]} {group.Text}";
        }
        return cells;
    }

    /// <summary>Le celle di una riga: parole vicine fra loro, separate da vuoti larghi.</summary>
    private static List<(string Text, double Left, double Right)> GroupIntoCells(
        List<TableWord> row, double gap)
    {
        var cells = new List<(string Text, double Left, double Right)>();
        foreach (var word in row)
        {
            if (cells.Count > 0 && word.Left - cells[^1].Right < gap)
            {
                cells[^1] = ($"{cells[^1].Text} {word.Text}", cells[^1].Left,
                    Math.Max(cells[^1].Right, word.Right));
                continue;
            }
            cells.Add((word.Text, word.Left, word.Right));
        }
        return cells;
    }

    /// <summary>L'ultima colonna che comincia prima della parola, cioè quella in cui cade.</summary>
    private static int ColumnOf(double left, List<double> borders)
    {
        var index = -1;
        for (var i = 0; i < borders.Count; i++)
        {
            if (left + 0.5 >= borders[i])
                index = i;
        }
        return index;
    }
}
