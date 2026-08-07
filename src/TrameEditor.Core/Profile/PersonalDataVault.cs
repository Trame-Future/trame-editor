using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrameEditor.Core.Profile;

/// <summary>Chiavi standard del profilo personale, con le etichette per la UI.</summary>
public static class ProfileKeys
{
    public const string Nome = "nome";
    public const string Cognome = "cognome";
    public const string NomeCompleto = "nomecompleto";
    public const string DataNascita = "datanascita";
    public const string LuogoNascita = "luogonascita";
    public const string CodiceFiscale = "codicefiscale";
    public const string Indirizzo = "indirizzo";
    public const string Cap = "cap";
    public const string Citta = "citta";
    public const string Provincia = "provincia";
    public const string Telefono = "telefono";
    public const string Email = "email";
    public const string Pec = "pec";
    public const string Iban = "iban";
    public const string PartitaIva = "partitaiva";
    public const string Documento = "documento";

    public static readonly IReadOnlyList<(string Key, string Label)> Standard =
    [
        (Nome, "Nome"),
        (Cognome, "Cognome"),
        (NomeCompleto, "Nome completo (se diverso)"),
        (DataNascita, "Data di nascita"),
        (LuogoNascita, "Luogo di nascita"),
        (CodiceFiscale, "Codice fiscale"),
        (Indirizzo, "Indirizzo (via e civico)"),
        (Cap, "CAP"),
        (Citta, "Città / Comune"),
        (Provincia, "Provincia"),
        (Telefono, "Telefono"),
        (Email, "Email"),
        (Pec, "PEC"),
        (Iban, "IBAN"),
        (PartitaIva, "Partita IVA"),
        (Documento, "Documento d'identità (numero)"),
    ];
}

/// <summary>
/// Cassaforte locale dei dati personali per "Compila per me": un file cifrato
/// con DPAPI (legato all'account Windows dell'utente: nessuna password da
/// ricordare, nessun dato in chiaro su disco, niente rete).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PersonalDataVault
{
    private readonly string _filePath;

    public PersonalDataVault(string filePath) => _filePath = filePath;

    public static PersonalDataVault CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TrameEditor", "profilo.dat"));

    public Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return [];
            var encrypted = File.ReadAllBytes(_filePath);
            var json = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch
        {
            // file corrotto o di un altro account Windows: profilo vuoto
            return [];
        }
    }

    public void Save(Dictionary<string, string> data)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath))!;
        Directory.CreateDirectory(directory);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)),
            null, DataProtectionScope.CurrentUser);
        var tempPath = _filePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, encrypted);
            if (File.Exists(_filePath))
                File.Replace(tempPath, _filePath, destinationBackupFileName: null);
            else
                File.Move(tempPath, _filePath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
