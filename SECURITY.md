# Sicherheitsrichtlinie

## Unterstützte Versionen

Sicherheitskorrekturen werden für die jeweils neueste veröffentlichte Version von Limit Lens bereitgestellt.

## Sicherheitsproblem melden

Bitte veröffentliche vermutete Sicherheitslücken, versehentlich eingecheckte Zugangsdaten oder Datenschutzprobleme nicht zuerst als öffentliches Issue.

Nutze nach Veröffentlichung des Repositorys bevorzugt eine private GitHub Security Advisory unter **Security → Advisories → New draft security advisory**. Falls diese Funktion nicht verfügbar ist, kontaktiere die Maintainer über einen privaten, im Repository-Profil genannten Kanal.

Eine hilfreiche Meldung enthält:

- betroffene Version oder Commit
- nachvollziehbare Schritte
- erwartete und tatsächliche Auswirkung
- betroffene lokale Dateien oder Netzwerkziele
- einen möglichen Lösungsvorschlag, falls vorhanden

Teile keine echten API-Keys, OAuth-Tokens, Prompts, Projektdateien oder vollständigen Diagnoseprotokolle. Verwende redigierte Beispiele.

## Sicherheitsmodell

Limit Lens liest lokale Nutzungsinformationen mehrerer CLI-Werkzeuge. Besonders schützenswert ist der OpenCode-Key, der nur im Arbeitsspeicher zur direkten HTTPS-Abfrage der OpenCode Go Usage API verwendet wird. Der Key darf weder in Logs noch in Diagnoseausgaben oder den Verlaufsspeicher gelangen.

Weitere Details stehen im Abschnitt **Datenschutz und Sicherheit** der README.
