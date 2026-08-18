# TrameEditor

Editor desktop per **Windows** — gratuito e open source — per file **PDF, TXT e Markdown**.
Un prodotto di [Trame Future srls](https://www.tramefuture.com).

![Licenza](https://img.shields.io/badge/licenza-AGPL--3.0-blue)

## Cosa fa

- **Testo (.txt)** — editor con rilevamento e preservazione di encoding (UTF-8/UTF-16/ANSI) e fine riga; salvataggio atomico
- **Markdown (.md)** — evidenziazione sintassi, anteprima live affiancata con scroll sincronizzato, toolbar di formattazione, export HTML
- **PDF** — visualizzazione fedele (PDFium), miniature, zoom, ricerca nel testo; ruota/elimina/riordina/estrai pagine, unione di più PDF
- **Modifica del testo interno dei PDF** — la funzione distintiva: clic su una riga, la riscrivi, il PDF viene aggiornato rimuovendo davvero gli operatori originali dal content stream (niente testo nascosto). Vale anche per il testo disegnato dentro i form XObject, come nei documenti dei gestionali: il modulo viene copiato, così le altre pagine che lo condividono non cambiano. Politica font a tre livelli (riuso incorporato → font di sistema → sostituto dichiarato) con avviso **prima** di applicare; rifiuto onesto nei casi non gestibili (testo ruotato, scansioni senza OCR)
- **Annotazioni** — evidenziazione, note a comparsa, timbro immagine (firma/logo)
- **Moduli (AcroForm)** — compilazione dei campi con opzione di appiattimento
- **OCR offline** (Tesseract, italiano+inglese) — le scansioni ricevono un layer di testo invisibile e diventano ricercabili; nessun dato esce dal computer
- **Comprimi** — copia alleggerita del PDF (ricompressione delle immagini), utile per gli allegati con limiti di dimensione

Tutto **offline**: nessun account, nessuna telemetria, i documenti non lasciano mai il computer.

## Compilare

Prerequisiti: [.NET SDK 10](https://dotnet.microsoft.com/download) su Windows 10/11 x64.

```powershell
dotnet build            # compila
dotnet test             # esegue i test (TrameEditor.Core.Tests)
dotnet run --project src/TrameEditor.App
```

### Creare l'installer

```powershell
dotnet publish src/TrameEditor.App -c Release -r win-x64 --self-contained true -o dist/publish
# ISCC.exe è in "%LOCALAPPDATA%\Programs\Inno Setup 6" o "C:\Program Files (x86)\Inno Setup 6"
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\TrameEditor.iss
# → dist\TrameEditor-Setup-<versione>.exe
```

## Architettura

- `src/TrameEditor.Core` — logica testabile, indipendente dalla UI: file di testo, Markdown→HTML (Markdig), operazioni pagine PDF (PDFsharp), ispezione testo (PdfPig) e riscrittura content stream (iText)
- `src/TrameEditor.App` — WPF (.NET 10): ribbon Fluent.Ribbon, editor AvalonEdit, anteprima WebView2, rendering PDFium via Docnet.Core
- `tests/TrameEditor.Core.Tests` — xUnit, inclusi test end-to-end sull'editing del testo PDF

## Licenza

[AGPL-3.0](LICENSE.txt) — © Trame Future srls. TrameEditor usa iText Core (AGPL),
PDFsharp (MIT), PdfPig (Apache-2.0), AvalonEdit (MIT), Markdig (BSD-2),
Docnet.Core (MIT), Fluent.Ribbon (MIT), CommunityToolkit.Mvvm (MIT).
