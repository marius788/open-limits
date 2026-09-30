# Zu Limit Lens beitragen

Danke für dein Interesse an Limit Lens.

## Entwicklungsumgebung

Benötigt werden Windows 11, PowerShell und die .NET-Framework-4.8-Entwicklungswerkzeuge. Das Projekt verwendet absichtlich keine externen NuGet-Pakete.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\LimitLens.exe --preview .\preview.png
```

## Änderungen vorbereiten

1. Einen thematisch klaren Branch erstellen.
2. Bestehenden Stil und die kompatible C#-Sprachversion des klassischen Framework-Compilers beibehalten.
3. Providerfehler isolieren, damit ein ausgefallener Dienst die übrigen Karten nicht blockiert.
4. Zugangsdaten niemals protokollieren oder in Diagnoseausgaben aufnehmen.
5. Bei UI-Änderungen `preview.png` neu rendern und visuell prüfen.
6. Bei Icon-Änderungen `tools/build-icon.ps1` ausführen.

## Pull Requests

Ein Pull Request sollte kurz erklären:

- welches Problem gelöst wird
- wie sich das sichtbare Verhalten ändert
- wie die Änderung geprüft wurde
- welche Datenschutz- oder Kompatibilitätsfolgen bestehen

Bitte keine generierten Diagnoseberichte, lokalen Logs, Konto-Konfigurationen oder kompilierten EXE-Dateien committen.

## Neue Provider

Ein neuer Adapter implementiert `IUsageProvider` und liefert normalisierte `ProviderSnapshot`-Objekte. Dokumentiere dabei:

- offizielle Datenquelle oder lokales Dateiformat
- benötigte Berechtigungen
- Netzwerkziele
- gelesene und gespeicherte Felder
- Verhalten bei fehlender Anmeldung und Timeouts

Scraping, inoffizielle OAuth-Wiederverwendung und geschätzte Aboquoten sollten vermieden werden, wenn keine belastbare Quelle vorhanden ist.
