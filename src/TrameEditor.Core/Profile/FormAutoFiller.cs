namespace TrameEditor.Core.Profile;

public sealed record AutoFillProposal(string FieldName, string ProfileKey, string Value);

/// <summary>
/// Abbina i campi di un modulo PDF ai dati del profilo in base al nome del
/// campo (normalizzato: solo lettere, minuscole). I sinonimi sono provati dal
/// più lungo al più corto, così "cognome" vince su "nome".
/// </summary>
public static class FormAutoFiller
{
    private static readonly (string ProfileKey, string[] Synonyms)[] Rules =
    [
        (ProfileKeys.CodiceFiscale, ["codicefiscale", "codfiscale", "fiscalcode", "cf"]),
        (ProfileKeys.NomeCompleto, ["nomeecognome", "nomecognome", "cognomeenome", "cognomenome",
            "nominativo", "richiedente", "sottoscritto", "dichiarante", "intestatario", "fullname"]),
        (ProfileKeys.Cognome, ["cognome", "surname", "lastname"]),
        (ProfileKeys.Nome, ["nome", "firstname"]),
        (ProfileKeys.DataNascita, ["datadinascita", "datanascita", "natoil", "natail", "dateofbirth"]),
        (ProfileKeys.LuogoNascita, ["luogodinascita", "luogonascita", "comunedinascita", "natoa", "nataa"]),
        (ProfileKeys.Indirizzo, ["indirizzo", "residenza", "domicilio", "viaenumero", "address", "via"]),
        (ProfileKeys.Cap, ["codicepostale", "cap", "zip"]),
        (ProfileKeys.Citta, ["cittadiresidenza", "comunediresidenza", "localita", "comune", "citta", "city"]),
        (ProfileKeys.Provincia, ["provincia", "prov"]),
        (ProfileKeys.Pec, ["pec", "postacertificata"]),
        (ProfileKeys.Email, ["postaelettronica", "email", "mail"]),
        (ProfileKeys.Telefono, ["cellulare", "telefono", "recapitotelefonico", "tel", "cell", "phone"]),
        (ProfileKeys.Iban, ["coordinatebancarie", "iban", "contocorrente"]),
        (ProfileKeys.PartitaIva, ["partitaiva", "piva", "vat"]),
        (ProfileKeys.Documento, ["numerodocumento", "cartadidentita", "cartaidentita", "documento"]),
    ];

    private static readonly IReadOnlyList<(string Synonym, string ProfileKey)> SynonymsByLength =
        Rules.SelectMany(rule => rule.Synonyms.Select(s => (s, rule.ProfileKey)))
            .OrderByDescending(entry => entry.s.Length)
            .ToList();

    public static IReadOnlyList<AutoFillProposal> Match(
        IEnumerable<string> fieldNames, IReadOnlyDictionary<string, string> profile)
    {
        var proposals = new List<AutoFillProposal>();
        foreach (var fieldName in fieldNames)
        {
            var normalized = Normalize(fieldName);
            if (normalized.Length == 0)
                continue;
            foreach (var (synonym, profileKey) in SynonymsByLength)
            {
                if (!normalized.Contains(synonym))
                    continue;
                var value = GetValue(profile, profileKey);
                if (!string.IsNullOrWhiteSpace(value))
                    proposals.Add(new AutoFillProposal(fieldName, profileKey, value));
                break; // il campo è stato riconosciuto: mai ricadere su sinonimi più corti
            }
        }
        return proposals;
    }

    private static string GetValue(IReadOnlyDictionary<string, string> profile, string key)
    {
        profile.TryGetValue(key, out var value);
        if (key == ProfileKeys.NomeCompleto && string.IsNullOrWhiteSpace(value))
        {
            profile.TryGetValue(ProfileKeys.Nome, out var nome);
            profile.TryGetValue(ProfileKeys.Cognome, out var cognome);
            value = $"{nome} {cognome}".Trim();
        }
        return value ?? string.Empty;
    }

    private static string Normalize(string fieldName) =>
        new(fieldName.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
}
