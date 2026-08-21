using TrameEditor.Core.Signatures;

namespace TrameEditor.Cli.Commands;

/// <summary>
/// Chi ha firmato un documento e se è stato alterato dopo la firma. Vale per le buste
/// <c>.p7m</c> e per i PDF con firma incorporata. Il confine è dichiarato in ogni
/// risposta: questo dice se il documento è integro, <b>non</b> se la firma è valida a
/// norma di legge — per quello serve un verificatore qualificato.
/// </summary>
public static class SignaturesCommand
{
    private const string Limite =
        "Verifica l'integrità del documento rispetto alla firma. Non controlla le revoche " +
        "dei certificati né l'accreditamento dell'ente: non sostituisce una verifica legale.";

    public static object Run(CommandLine line)
    {
        var path = Paths.ExistingFile(line.At(0, "file"));
        var result = new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["comando"] = "firme",
            ["file"] = path,
            ["limite"] = Limite,
        };

        if (P7mReader.IsP7m(path))
        {
            var content = P7mReader.Read(path);
            result["tipo"] = "p7m";
            result["contenuto"] = new Dictionary<string, object?>
            {
                ["nomeSuggerito"] = content.SuggestedFileName,
                ["byte"] = content.Data.Length,
                ["ePdf"] = content.IsPdf,
            };
            result["firmatari"] = content.Signers.Select(Describe).ToList();

            if (line.Has("estrai"))
            {
                var folder = Paths.Folder(line.Required("estrai"));
                var extraction = SignedFileExtractor.Extract(path, folder);
                result["estrazione"] = new Dictionary<string, object?>
                {
                    ["riuscita"] = extraction.Success,
                    ["esito"] = extraction.Outcome,
                    ["fileProdotti"] = extraction.OutputPaths,
                };
            }
            return result;
        }

        var signatures = PdfSignatureInspector.Inspect(path);
        result["tipo"] = "pdf";
        result["firmato"] = signatures.Count > 0;
        result["firme"] = signatures.Select(signature => new Dictionary<string, object?>
        {
            ["campo"] = signature.FieldName,
            ["algoritmo"] = signature.Algorithm,
            ["copreTuttoIlDocumento"] = signature.CoversWholeDocument,
            ["nomeDichiarato"] = signature.DeclaredName,
            ["motivo"] = signature.Reason,
            ["luogo"] = signature.Location,
            ["firmatario"] = Describe(signature.Signer),
        }).ToList();
        return result;
    }

    private static object Describe(SignerDetail signer) => new Dictionary<string, object?>
    {
        ["nome"] = signer.DisplayName,
        ["emessoDa"] = signer.IssuerName,
        ["firmatoIl"] = signer.SignedAt,
        ["validoDal"] = signer.ValidFrom,
        ["validoAl"] = signer.ValidTo,
        ["documentoIntegro"] = signer.IntegrityVerified,
        ["problema"] = signer.Problem,
    };
}
