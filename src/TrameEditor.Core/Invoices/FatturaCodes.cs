namespace TrameEditor.Core.Invoices;

/// <summary>
/// I codici della fattura elettronica tradotti in italiano leggibile: "TD01"
/// non dice niente a nessuno, "Fattura" sì.
/// <para>
/// Un codice che non conosciamo viene mostrato <b>com'è</b>, non nascosto e non
/// tradotto a caso: le tabelle dell'Agenzia delle Entrate cambiano nel tempo e
/// inventare una descrizione sarebbe peggio che non darla.
/// </para>
/// </summary>
public static class FatturaCodes
{
    public static string Describe(string? code, IReadOnlyDictionary<string, string> table)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "(non indicato)";
        var key = code.Trim().ToUpperInvariant();
        return table.TryGetValue(key, out var description) ? $"{description} ({key})" : key;
    }

    public static string TipoDocumento(string? code) => Describe(code, TipiDocumento);

    public static string ModalitaPagamento(string? code) => Describe(code, ModalitaPagamenti);

    public static string Natura(string? code) => Describe(code, Nature);

    public static string RegimeFiscale(string? code) => Describe(code, RegimiFiscali);

    public static string CondizioniPagamento(string? code) => Describe(code, CondizioniPagamenti);

    public static string EsigibilitaIva(string? code) => Describe(code, Esigibilita);

    public static readonly IReadOnlyDictionary<string, string> TipiDocumento =
        new Dictionary<string, string>
        {
            ["TD01"] = "Fattura",
            ["TD02"] = "Acconto/anticipo su fattura",
            ["TD03"] = "Acconto/anticipo su parcella",
            ["TD04"] = "Nota di credito",
            ["TD05"] = "Nota di debito",
            ["TD06"] = "Parcella",
            ["TD07"] = "Fattura semplificata",
            ["TD08"] = "Nota di credito semplificata",
            ["TD09"] = "Nota di debito semplificata",
            ["TD16"] = "Integrazione fattura reverse charge interno",
            ["TD17"] = "Integrazione/autofattura per acquisto servizi dall'estero",
            ["TD18"] = "Integrazione per acquisto beni intracomunitari",
            ["TD19"] = "Integrazione/autofattura per acquisto beni ex art. 17 c. 2",
            ["TD20"] = "Autofattura per regolarizzazione",
            ["TD21"] = "Autofattura per splafonamento",
            ["TD22"] = "Estrazione beni da deposito IVA",
            ["TD23"] = "Estrazione beni da deposito IVA con versamento dell'IVA",
            ["TD24"] = "Fattura differita",
            ["TD25"] = "Fattura differita (art. 21 c. 4 lett. b)",
            ["TD26"] = "Cessione di beni ammortizzabili",
            ["TD27"] = "Fattura per autoconsumo o cessioni gratuite",
            ["TD28"] = "Acquisti da San Marino con IVA",
        };

    public static readonly IReadOnlyDictionary<string, string> ModalitaPagamenti =
        new Dictionary<string, string>
        {
            ["MP01"] = "Contanti",
            ["MP02"] = "Assegno",
            ["MP03"] = "Assegno circolare",
            ["MP04"] = "Contanti presso Tesoreria",
            ["MP05"] = "Bonifico",
            ["MP06"] = "Vaglia cambiario",
            ["MP07"] = "Bollettino bancario",
            ["MP08"] = "Carta di pagamento",
            ["MP09"] = "RID",
            ["MP10"] = "RID utenze",
            ["MP11"] = "RID veloce",
            ["MP12"] = "RIBA",
            ["MP13"] = "MAV",
            ["MP14"] = "Quietanza erario",
            ["MP15"] = "Giroconto su conti di contabilità speciale",
            ["MP16"] = "Domiciliazione bancaria",
            ["MP17"] = "Domiciliazione postale",
            ["MP18"] = "Bollettino di c/c postale",
            ["MP19"] = "SEPA Direct Debit",
            ["MP20"] = "SEPA Direct Debit CORE",
            ["MP21"] = "SEPA Direct Debit B2B",
            ["MP22"] = "Trattenuta su somme già riscosse",
            ["MP23"] = "PagoPA",
        };

    public static readonly IReadOnlyDictionary<string, string> Nature =
        new Dictionary<string, string>
        {
            ["N1"] = "Escluse ex art. 15",
            ["N2"] = "Non soggette",
            ["N2.1"] = "Non soggette ad IVA ex artt. da 7 a 7-septies",
            ["N2.2"] = "Non soggette — altri casi",
            ["N3"] = "Non imponibili",
            ["N3.1"] = "Non imponibili — esportazioni",
            ["N3.2"] = "Non imponibili — cessioni intracomunitarie",
            ["N3.3"] = "Non imponibili — cessioni verso San Marino",
            ["N3.4"] = "Non imponibili — operazioni assimilate alle esportazioni",
            ["N3.5"] = "Non imponibili — a seguito di dichiarazione d'intento",
            ["N3.6"] = "Non imponibili — altre operazioni",
            ["N4"] = "Esenti",
            ["N5"] = "Regime del margine / IVA non esposta in fattura",
            ["N6"] = "Inversione contabile (reverse charge)",
            ["N6.1"] = "Inversione contabile — rottami e materiali di recupero",
            ["N6.2"] = "Inversione contabile — oro e argento puro",
            ["N6.3"] = "Inversione contabile — subappalto nel settore edile",
            ["N6.4"] = "Inversione contabile — cessione di fabbricati",
            ["N6.5"] = "Inversione contabile — cessione di telefoni cellulari",
            ["N6.6"] = "Inversione contabile — cessione di prodotti elettronici",
            ["N6.7"] = "Inversione contabile — prestazioni comparto edile e settori connessi",
            ["N6.8"] = "Inversione contabile — operazioni settore energetico",
            ["N6.9"] = "Inversione contabile — altri casi",
            ["N7"] = "IVA assolta in altro Stato UE",
        };

    public static readonly IReadOnlyDictionary<string, string> RegimiFiscali =
        new Dictionary<string, string>
        {
            ["RF01"] = "Regime ordinario",
            ["RF02"] = "Contribuenti minimi",
            ["RF04"] = "Agricoltura e attività connesse e pesca",
            ["RF05"] = "Vendita sali e tabacchi",
            ["RF06"] = "Commercio dei fiammiferi",
            ["RF07"] = "Editoria",
            ["RF08"] = "Gestione di servizi di telefonia pubblica",
            ["RF09"] = "Rivendita di documenti di trasporto pubblico e di sosta",
            ["RF10"] = "Intrattenimenti e giochi",
            ["RF11"] = "Agenzie di viaggi e turismo",
            ["RF12"] = "Agriturismo",
            ["RF13"] = "Vendite a domicilio",
            ["RF14"] = "Rivendita di beni usati, oggetti d'arte, d'antiquariato o da collezione",
            ["RF15"] = "Agenzie di vendite all'asta di oggetti d'arte, antiquariato o da collezione",
            ["RF16"] = "IVA per cassa (P.A.)",
            ["RF17"] = "IVA per cassa",
            ["RF18"] = "Altro",
            ["RF19"] = "Regime forfettario",
        };

    public static readonly IReadOnlyDictionary<string, string> CondizioniPagamenti =
        new Dictionary<string, string>
        {
            ["TP01"] = "Pagamento a rate",
            ["TP02"] = "Pagamento completo",
            ["TP03"] = "Anticipo",
        };

    public static readonly IReadOnlyDictionary<string, string> Esigibilita =
        new Dictionary<string, string>
        {
            ["I"] = "IVA a esigibilità immediata",
            ["D"] = "IVA a esigibilità differita",
            ["S"] = "Scissione dei pagamenti (split payment)",
        };
}
