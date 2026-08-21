using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrameEditor.Cli;

/// <summary>Come finisce un comando. Il numero conta per gli script: 0 è andata,
/// 1 è "hai sbagliato a chiamarmi", 2 è "il documento non si è lasciato fare".</summary>
public enum ExitCode
{
    Ok = 0,
    Usage = 1,
    Failed = 2,
}

/// <summary>
/// L'uscita è <b>sempre</b> JSON su stdout, anche quando le cose vanno male: chi legge
/// è un programma, e un errore in prosa lo costringerebbe a indovinare. I messaggi
/// dentro il JSON restano quelli in italiano che leggerebbe una persona.
/// </summary>
public static class Output
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Senza questo gli accenti diventerebbero \u00e0 e i messaggi illeggibili.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);

    public static string Error(string message) =>
        Serialize(new Dictionary<string, object?> { ["ok"] = false, ["errore"] = message });
}
