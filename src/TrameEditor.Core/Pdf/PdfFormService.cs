using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Pdf;

public enum PdfFormFieldKind
{
    Text,
    Checkbox,
    Choice,
}

/// <summary>Un campo del modulo AcroForm. Per le caselle di spunta,
/// <see cref="CheckedValue"/> è lo stato che rappresenta "spuntato".</summary>
public sealed record PdfFormFieldInfo(
    string Name,
    PdfFormFieldKind Kind,
    string Value,
    IReadOnlyList<string> Options,
    string CheckedValue);

/// <summary>Lettura e compilazione dei moduli PDF (AcroForm).</summary>
public static class PdfFormService
{
    public static IReadOnlyList<PdfFormFieldInfo> GetFields(string path)
    {
        using var document = new PdfDocument(new PdfReader(path));
        var form = PdfAcroForm.GetAcroForm(document, createIfNotExist: false);
        if (form is null)
            return [];

        var fields = new List<PdfFormFieldInfo>();
        foreach (var entry in form.GetAllFormFields())
        {
            var field = entry.Value;
            if (field.IsReadOnly())
                continue;

            switch (field)
            {
                case PdfTextFormField:
                    fields.Add(new PdfFormFieldInfo(entry.Key, PdfFormFieldKind.Text,
                        field.GetValueAsString(), [], string.Empty));
                    break;

                case PdfButtonFormField button
                    when (button.GetFieldFlags() & PdfButtonFormField.FF_PUSH_BUTTON) == 0:
                    var states = field.GetAppearanceStates()
                        .Where(s => s != "Off").Distinct().ToList();
                    if (states.Count <= 1)
                        fields.Add(new PdfFormFieldInfo(entry.Key, PdfFormFieldKind.Checkbox,
                            field.GetValueAsString(), [], states.FirstOrDefault() ?? "Yes"));
                    else // gruppo di pulsanti radio: si sceglie uno stato
                        fields.Add(new PdfFormFieldInfo(entry.Key, PdfFormFieldKind.Choice,
                            field.GetValueAsString(), states, string.Empty));
                    break;

                case PdfChoiceFormField choice:
                    fields.Add(new PdfFormFieldInfo(entry.Key, PdfFormFieldKind.Choice,
                        field.GetValueAsString(), ReadOptions(choice), string.Empty));
                    break;
            }
        }
        return fields;
    }

    private static List<string> ReadOptions(PdfChoiceFormField choice)
    {
        var options = new List<string>();
        var array = choice.GetOptions();
        if (array is null)
            return options;
        foreach (var item in array)
        {
            switch (item)
            {
                case PdfString s:
                    options.Add(s.ToUnicodeString());
                    break;
                case PdfArray pair when pair.Size() > 0 && pair.Get(0) is PdfString export:
                    options.Add(export.ToUnicodeString());
                    break;
            }
        }
        return options;
    }

    /// <summary>Compila i campi indicati; con <paramref name="flatten"/> il modulo
    /// viene "appiattito": i valori diventano contenuto fisso non più modificabile.</summary>
    public static void Fill(string sourcePath, string targetPath,
        IReadOnlyDictionary<string, string> values, bool flatten)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Percorso senza cartella: {targetPath}", nameof(targetPath));
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var document = new PdfDocument(new PdfReader(sourcePath), new PdfWriter(tempPath)))
            {
                var form = PdfAcroForm.GetAcroForm(document, createIfNotExist: false)
                    ?? throw new PdfTextEditException("Questo PDF non contiene un modulo compilabile.");

                foreach (var (name, value) in values)
                {
                    var field = form.GetField(name);
                    if (field is null)
                        continue;
                    try
                    {
                        field.SetValue(value);
                    }
                    catch
                    {
                        // valore non applicabile al campo (es. stato inesistente): lasciato invariato
                    }
                }

                if (flatten)
                    form.FlattenFields();
            }

            if (File.Exists(fullTarget))
                File.Replace(tempPath, fullTarget, destinationBackupFileName: null);
            else
                File.Move(tempPath, fullTarget);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
