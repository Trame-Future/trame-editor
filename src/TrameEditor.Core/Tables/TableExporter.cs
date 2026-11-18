using System.Text;
using ClosedXML.Excel;

namespace TrameEditor.Core.Tables;

/// <summary>In che forma salvare la tabella.</summary>
public enum TableFormat
{
    /// <summary>Testo separato da un carattere: si apre ovunque, anche fra dieci anni.</summary>
    Csv,

    /// <summary>Cartella di lavoro Excel.</summary>
    Xlsx,
}

/// <summary>
/// Salva su file la griglia riconosciuta.
/// <para>
/// Due scelte che sembrano dettagli e non lo sono, perché decidono se il file si apre bene o
/// male in Excel su un computer italiano: il separatore predefinito è il <b>punto e
/// virgola</b> — con la virgola Excel in italiano mette tutta la riga in una cella sola — e
/// il CSV viene scritto con la firma UTF-8 in testa, senza la quale gli accenti diventano
/// caratteri strani.
/// </para>
/// </summary>
public static class TableExporter
{
    /// <summary>Il formato che il nome del file lascia intendere; CSV se non lo dice.</summary>
    public static TableFormat FormatOf(string path) =>
        Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? TableFormat.Xlsx
            : TableFormat.Csv;

    public static void Export(PdfTable table, string targetPath, TableFormat? format = null,
        char separator = ';')
    {
        Export([table], targetPath, format, separator);
    }

    /// <summary>
    /// Più pagine in un file solo: in XLSX ognuna ha il suo foglio, in CSV si accodano
    /// separate da una riga vuota — un CSV non sa cosa sia un foglio, e inventarsi un modo
    /// per fingerlo renderebbe il file illeggibile ai programmi che lo aprono.
    /// </summary>
    public static void Export(IReadOnlyList<PdfTable> tables, string targetPath,
        TableFormat? format = null, char separator = ';')
    {
        var chosen = format ?? FormatOf(targetPath);
        var full = Path.GetFullPath(targetPath);
        // Si scrive di fianco e si sposta a lavoro finito: se qualcosa va storto a metà,
        // chi guarda non trova un file mezzo scritto al posto di quello di prima.
        var temporary = Path.Combine(Path.GetDirectoryName(full)!,
            $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");

        try
        {
            if (chosen == TableFormat.Xlsx)
                WriteXlsx(tables, temporary);
            else
                WriteCsv(tables, temporary, separator);

            if (File.Exists(full))
                File.Replace(temporary, full, destinationBackupFileName: null);
            else
                File.Move(temporary, full);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void WriteCsv(IReadOnlyList<PdfTable> tables, string path, char separator)
    {
        var text = new StringBuilder();
        for (var i = 0; i < tables.Count; i++)
        {
            if (i > 0)
                text.AppendLine();
            foreach (var row in tables[i].Rows)
                text.AppendLine(string.Join(separator, row.Select(cell => Escape(cell, separator))));
        }

        // Con la firma UTF-8 in testa Excel riconosce la codifica e gli accenti restano tali.
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>Una cella va fra virgolette se contiene il separatore, virgolette o un a
    /// capo; le virgolette interne si raddoppiano, come vuole il formato.</summary>
    private static string Escape(string cell, char separator)
    {
        if (!cell.Contains(separator) && !cell.Contains('"') && !cell.Contains('\n') && !cell.Contains('\r'))
            return cell;
        return $"\"{cell.Replace("\"", "\"\"")}\"";
    }

    private static void WriteXlsx(IReadOnlyList<PdfTable> tables, string path)
    {
        using var workbook = new XLWorkbook();
        foreach (var table in tables)
        {
            var sheet = workbook.Worksheets.Add($"Pagina {table.PageNumber}");
            for (var r = 0; r < table.Rows.Count; r++)
            {
                for (var c = 0; c < table.Rows[r].Count; c++)
                {
                    // Il testo si scrive come testo: lasciando indovinare a Excel, un codice
                    // articolo come 00123 perderebbe gli zeri e una data verrebbe reinterpretata.
                    sheet.Cell(r + 1, c + 1).SetValue(table.Rows[r][c]);
                }
            }
            if (table.Rows.Count > 0)
                sheet.Columns().AdjustToContents();
        }

        if (workbook.Worksheets.Count == 0)
            workbook.Worksheets.Add("Vuoto");

        // Si salva sullo stream e non sul percorso: il file di lavoro finisce in .tmp, e
        // ClosedXML guarda l'estensione e si rifiuta di scrivere dove non riconosce un foglio.
        using var output = File.Create(path);
        workbook.SaveAs(output);
    }
}
