using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using TrameEditor.Core.Pdf;
using Path = System.IO.Path;

namespace TrameEditor.Core.Tests.Pdf;

public class PdfFormServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trameeditor-form-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    /// <summary>Modulo con un campo testo e una casella di spunta.</summary>
    private string CreateFormPdf()
    {
        var path = PathFor($"{Guid.NewGuid():N}.pdf");
        using (var document = new PdfDocument(new PdfWriter(path)))
        {
            document.AddNewPage();
            var form = PdfAcroForm.GetAcroForm(document, createIfNotExist: true);
            var nome = new TextFormFieldBuilder(document, "nome")
                .SetWidgetRectangle(new Rectangle(50, 700, 200, 20)).CreateText();
            form.AddField(nome);
            var privacy = new CheckBoxFormFieldBuilder(document, "privacy")
                .SetWidgetRectangle(new Rectangle(50, 650, 15, 15)).CreateCheckBox();
            form.AddField(privacy);
        }
        return path;
    }

    [Fact]
    public void GetFields_ReadsTextAndCheckbox()
    {
        var path = CreateFormPdf();

        var fields = PdfFormService.GetFields(path);

        Assert.Equal(2, fields.Count);
        Assert.Contains(fields, f => f is { Name: "nome", Kind: PdfFormFieldKind.Text });
        var checkbox = fields.Single(f => f.Name == "privacy");
        Assert.Equal(PdfFormFieldKind.Checkbox, checkbox.Kind);
        Assert.False(string.IsNullOrEmpty(checkbox.CheckedValue));
    }

    [Fact]
    public void GetFields_PdfWithoutForm_ReturnsEmpty()
    {
        var path = PathFor("noform.pdf");
        using (var document = new PdfDocument(new PdfWriter(path)))
            document.AddNewPage();

        Assert.Empty(PdfFormService.GetFields(path));
    }

    [Fact]
    public void Fill_WritesValues_Roundtrip()
    {
        var source = CreateFormPdf();
        var checkedValue = PdfFormService.GetFields(source).Single(f => f.Name == "privacy").CheckedValue;
        var target = PathFor("filled.pdf");

        PdfFormService.Fill(source, target,
            new Dictionary<string, string> { ["nome"] = "Pietro Ricciardi", ["privacy"] = checkedValue },
            flatten: false);

        var fields = PdfFormService.GetFields(target);
        Assert.Equal("Pietro Ricciardi", fields.Single(f => f.Name == "nome").Value);
        Assert.Equal(checkedValue, fields.Single(f => f.Name == "privacy").Value);
    }

    [Fact]
    public void Fill_WithFlatten_RemovesFormButKeepsText()
    {
        var source = CreateFormPdf();
        var target = PathFor("flattened.pdf");

        PdfFormService.Fill(source, target,
            new Dictionary<string, string> { ["nome"] = "Testo definitivo" }, flatten: true);

        Assert.Empty(PdfFormService.GetFields(target));
        using var inspector = new PdfTextInspector(target);
        Assert.Contains(inspector.GetLines(1), l => l.Text.Contains("Testo definitivo"));
    }

    [Fact]
    public void Fill_PdfWithoutForm_ThrowsHonestError()
    {
        var path = PathFor("noform2.pdf");
        using (var document = new PdfDocument(new PdfWriter(path)))
            document.AddNewPage();

        Assert.Throws<PdfTextEditException>(() =>
            PdfFormService.Fill(path, PathFor("out.pdf"), new Dictionary<string, string>(), false));
    }
}
