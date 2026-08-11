using iText.Kernel.Exceptions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Tagging;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

/// <summary>
/// Esame di accessibilità di un PDF (PDF/UA): che cosa manca perché il documento
/// sia usabile da chi lo legge con una sintesi vocale o con la tastiera.
/// </summary>
/// <remarks>
/// <para><b>Confine dichiarato.</b> Un PDF accessibile richiede che il contenuto sia
/// <i>marcato</i>: questo è un titolo, questa è una tabella con le sue intestazioni,
/// questa immagine significa X. Sono decisioni che solo chi conosce il documento può
/// prendere: TrameEditor non le inventa. Qui trovi che cosa manca, e mettiamo a posto
/// le tre cose che si possono sistemare senza interpretare niente — lingua, titolo, e
/// come il titolo viene mostrato.</para>
/// <para>Anche il verdetto formale ha un confine, ed è dello standard, non nostro:
/// veraPDF verifica le regole <i>controllabili da una macchina</i>. Un documento che
/// le supera tutte può ancora essere inaccessibile, se le marcature ci sono ma sono
/// sbagliate.</para>
/// </remarks>
public static class PdfUaChecker
{
    /// <summary>Lingua proposta quando il documento non ne dichiara una.</summary>
    public const string DefaultLanguage = "it-IT";

    private static readonly PdfName DisplayDocTitle = new("DisplayDocTitle");

    public static PdfUaReport Analyze(string path)
    {
        var reader = new PdfReader(path);
        reader.SetUnethicalReading(true);

        PdfDocument document;
        try
        {
            document = new PdfDocument(reader);
        }
        catch (BadPasswordException)
        {
            CloseQuietly(reader);
            return new PdfUaReport(
                [new PdfUaIssue(PdfUaSeverity.Bloccante,
                    "Il PDF è protetto da password: aprilo con la sua password prima di esaminarlo.")],
                IsTagged: false, Language: null, Title: null, PageCount: 0);
        }

        using (document)
        {
            var issues = new List<PdfUaIssue>();
            var catalog = document.GetCatalog().GetPdfObject();

            var marked = catalog.GetAsDictionary(PdfName.MarkInfo)?.GetAsBool(PdfName.Marked) == true;
            var hasStructure = catalog.GetAsDictionary(PdfName.StructTreeRoot) is not null;
            var tagged = marked && hasStructure;

            if (!tagged)
            {
                issues.Add(new PdfUaIssue(PdfUaSeverity.Bloccante,
                    hasStructure
                        ? "Il documento ha una struttura ma non è dichiarato marcato: va rifatto con un programma che produce PDF marcati."
                        : "Il contenuto non è marcato: senza marcatura una sintesi vocale legge le parole ma non sa che cosa sono (titoli, elenchi, tabelle). Va prodotto marcato dall'origine — Word, LibreOffice e InDesign lo fanno."));
            }
            else
            {
                issues.Add(new PdfUaIssue(PdfUaSeverity.Corretto, "Il contenuto è marcato."));
            }

            var language = catalog.GetAsString(PdfName.Lang)?.ToUnicodeString();
            issues.Add(string.IsNullOrWhiteSpace(language)
                ? new PdfUaIssue(PdfUaSeverity.Rimediabile,
                    "Manca la lingua del documento: la sintesi vocale non sa con quale pronuncia leggerlo.")
                : new PdfUaIssue(PdfUaSeverity.Corretto, $"Lingua dichiarata: {language}."));

            var title = document.GetDocumentInfo().GetTitle();
            issues.Add(string.IsNullOrWhiteSpace(title)
                ? new PdfUaIssue(PdfUaSeverity.Rimediabile,
                    "Manca il titolo del documento: chi naviga fra le finestre sente solo il nome del file.")
                : new PdfUaIssue(PdfUaSeverity.Corretto, $"Titolo: «{title}»."));

            var showsTitle = catalog.GetAsDictionary(PdfName.ViewerPreferences)
                ?.GetAsBool(DisplayDocTitle) == true;
            issues.Add(showsTitle
                ? new PdfUaIssue(PdfUaSeverity.Corretto,
                    "Il lettore mostrerà il titolo invece del nome del file.")
                : new PdfUaIssue(PdfUaSeverity.Rimediabile,
                    "Il documento non chiede di mostrare il titolo al posto del nome del file."));

            AddFigureIssues(document, tagged, issues);
            AddTextIssues(path, issues);

            return new PdfUaReport(issues, tagged, language, title, document.GetNumberOfPages());
        }
    }

    /// <summary>
    /// Sistema quello che si può sistemare senza interpretare il documento:
    /// lingua, titolo e richiesta di mostrare il titolo. Non tocca il contenuto.
    /// </summary>
    public static PdfUaReport Fix(string sourcePath, string targetPath,
        string? language = null, string? title = null)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var reader = new PdfReader(sourcePath);
            reader.SetUnethicalReading(true);
            using (var document = new PdfDocument(reader, new PdfWriter(tempPath)))
            {
                var catalog = document.GetCatalog();
                var dictionary = catalog.GetPdfObject();

                var chosenLanguage = string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language.Trim();
                dictionary.Put(PdfName.Lang, new PdfString(chosenLanguage));

                var chosenTitle = string.IsNullOrWhiteSpace(title)
                    ? document.GetDocumentInfo().GetTitle()
                    : title.Trim();
                if (string.IsNullOrWhiteSpace(chosenTitle))
                    chosenTitle = Path.GetFileNameWithoutExtension(sourcePath);
                document.GetDocumentInfo().SetTitle(chosenTitle);

                catalog.SetViewerPreferences(
                    new PdfViewerPreferences().SetDisplayDocTitle(true));
            }

            if (File.Exists(fullTarget))
                File.Replace(tempPath, fullTarget, destinationBackupFileName: null);
            else
                File.Move(tempPath, fullTarget);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // best effort
                }
            }
        }

        return Analyze(fullTarget);
    }

    /// <summary>Le immagini: senza marcatura non possono avere un testo
    /// alternativo, con la marcatura si può controllare se ce l'hanno.</summary>
    private static void AddFigureIssues(PdfDocument document, bool tagged, List<PdfUaIssue> issues)
    {
        if (!tagged)
        {
            var images = CountImages(document);
            if (images > 0)
            {
                issues.Add(new PdfUaIssue(PdfUaSeverity.Bloccante,
                    $"{images} immagini senza testo alternativo: chi non le vede non sa che cosa mostrano."));
            }

            return;
        }

        var senzaAlternativo = 0;
        var totali = 0;
        try
        {
            Walk(document.GetStructTreeRoot(), node =>
            {
                if (node is not PdfStructElem element)
                    return;
                if (element.GetRole()?.GetValue() != "Figure")
                    return;

                totali++;
                var content = element.GetPdfObject();
                var alt = content.GetAsString(PdfName.Alt)?.ToUnicodeString();
                var actual = content.GetAsString(PdfName.ActualText)?.ToUnicodeString();
                if (string.IsNullOrWhiteSpace(alt) && string.IsNullOrWhiteSpace(actual))
                    senzaAlternativo++;
            });
        }
        catch (Exception)
        {
            // una struttura malformata non deve impedire il resto dell'esame
            return;
        }

        if (totali == 0)
            return;

        issues.Add(senzaAlternativo == 0
            ? new PdfUaIssue(PdfUaSeverity.Corretto, $"{totali} immagini, tutte con testo alternativo.")
            : new PdfUaIssue(PdfUaSeverity.Bloccante,
                $"{senzaAlternativo} immagini su {totali} senza testo alternativo: va scritto da chi sa che cosa mostrano."));
    }

    private static void Walk(IStructureNode? node, Action<IStructureNode> visit)
    {
        if (node is null)
            return;

        visit(node);
        var kids = node.GetKids();
        if (kids is null)
            return;

        foreach (var kid in kids)
            Walk(kid, visit);
    }

    private static int CountImages(PdfDocument document)
    {
        var count = 0;
        for (var page = 1; page <= document.GetNumberOfPages(); page++)
        {
            var resources = document.GetPage(page).GetResources().GetPdfObject();
            var xObjects = resources.GetAsDictionary(PdfName.XObject);
            if (xObjects is null)
                continue;

            foreach (var name in xObjects.KeySet())
            {
                if (xObjects.GetAsStream(name)?.GetAsName(PdfName.Subtype)?.Equals(PdfName.Image) == true)
                    count++;
            }
        }

        return count;
    }

    /// <summary>Il testo che non si estrae non si legge nemmeno ad alta voce:
    /// riusiamo l'esame dei font già fatto per il PDF/A.</summary>
    private static void AddTextIssues(string path, List<PdfUaIssue> issues)
    {
        try
        {
            var senzaUnicode = PdfAAnalyzer.Analyze(path).FontsWithoutUnicode;
            if (senzaUnicode.Count > 0)
            {
                issues.Add(new PdfUaIssue(PdfUaSeverity.Bloccante,
                    "Il testo di alcuni font non è estraibile in modo affidabile, quindi non è leggibile "
                    + "da una sintesi vocale: " + string.Join(", ", senzaUnicode.Take(5))));
            }
        }
        catch (Exception)
        {
            // l'esame dei font è un di più: se fallisce, il resto vale lo stesso
        }
    }

    private static void CloseQuietly(PdfReader reader)
    {
        try
        {
            reader.Close();
        }
        catch
        {
            // best effort
        }
    }
}
