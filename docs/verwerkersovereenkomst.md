# Verwerkersovereenkomst (template)

> Template for the data processing agreement (GDPR art. 28) between Pointer and each shop.
> It is a starting point, not legal advice: have it checked before the first real shop signs.
> Placeholders are in `[brackets]`. Dutch, because the shops are Dutch-speaking first.

## Partijen

1. **[Naam zaak]**, [rechtsvorm], met zetel te [adres], ondernemingsnummer [KBO-nummer], vertegenwoordigd door [naam], hierna de **verwerkingsverantwoordelijke** (de zaak);
2. **[Naam Pointer / uitbater]**, met zetel te [adres], ondernemingsnummer [KBO-nummer], hierna de **verwerker**.

## 1. Onderwerp

De verwerker levert de zaak een online boekingssysteem (boekingspagina, beheeromgeving, e-mails en voorschotbetalingen). Daarbij verwerkt de verwerker persoonsgegevens van de klanten van de zaak, uitsluitend in opdracht van en volgens de schriftelijke instructies van de zaak. Deze overeenkomst hoort bij de dienstverleningsovereenkomst van [datum].

## 2. Welke gegevens, van wie, waarvoor

| | |
| --- | --- |
| Betrokkenen | Klanten van de zaak die een afspraak boeken |
| Gegevens | Naam, e-mailadres, telefoonnummer (optioneel), afspraken (dienst, tijdstip, status), voorschotbetalingen (bedrag, status, referentie van de betaalprovider) |
| Doel | Afspraken plannen, bevestigen, herinneren en annuleren; voorschotten innen en terugbetalen volgens de regels van de zaak |
| Bijzondere gegevens | Geen. De zaak vraagt via het systeem geen gezondheids- of andere bijzondere gegevens |
| Duur | Zolang de dienstverleningsovereenkomst loopt, plus de termijn in artikel 9 |

## 3. Verplichtingen van de verwerker

De verwerker:

1. verwerkt de gegevens alleen voor het doel hierboven en niet voor eigen doeleinden (geen marketing, geen verkoop, geen profilering);
2. zorgt dat iedereen die toegang heeft tot de gegevens tot geheimhouding verplicht is;
3. neemt de beveiligingsmaatregelen uit artikel 5;
4. helpt de zaak bij verzoeken van klanten (inzage, overdracht, wissing). De beheeromgeving heeft daarvoor de pagina **Klantgegevens**: zoeken op e-mailadres, downloaden als JSON en wissen;
5. helpt de zaak bij een gegevenslek en bij een eventuele gegevensbeschermingseffectbeoordeling;
6. stelt alle informatie ter beschikking die nodig is om aan te tonen dat deze overeenkomst wordt nageleefd, en laat een redelijke audit toe na afspraak.

## 4. Subverwerkers

De zaak geeft algemene toestemming voor de volgende subverwerkers:

| Subverwerker | Wat | Waar |
| --- | --- | --- |
| [Hostingprovider, bv. Fly.io / Railway / VPS-leverancier] | Hosting van de applicatie en database | [EU-regio] |
| Brevo (Sendinblue SAS) | Verzenden van e-mails | EU |
| Stripe | Betalingen. **Let op:** voor voorschotten is Stripe een rechtstreekse dienstverlener van de zaak (het Stripe-account staat op naam van de zaak). De verwerker houdt nooit klantengeld bij. | EU / VS (met passende waarborgen) |

De verwerker meldt een nieuwe subverwerker minstens 30 dagen vooraf. De zaak kan binnen die termijn gemotiveerd bezwaar maken. De verwerker legt aan elke subverwerker dezelfde verplichtingen op als in deze overeenkomst.

## 5. Beveiliging

Minstens:

- versleutelde verbindingen (HTTPS) voor alle pagina's en API's;
- toegang tot de beheeromgeving alleen met een persoonlijk wachtwoord (gehasht opgeslagen), met beperking van inlogpogingen;
- elke zaak ziet alleen haar eigen gegevens, ook in de database afgedwongen;
- sleutels en wachtwoorden van systemen staan niet in de broncode;
- e-mailinhoud wordt niet bewaard, alleen aan wie en wanneer een bericht werd verstuurd;
- back-ups versleuteld en binnen de EU;
- [aanvullen: logbeheer, updatebeleid].

## 6. Gegevenslekken

De verwerker meldt een gegevenslek aan de zaak zonder onredelijke vertraging en uiterlijk binnen 48 uur na ontdekking, met wat bekend is over de aard, de betrokken gegevens, de gevolgen en de genomen maatregelen. De zaak beslist of het lek gemeld wordt aan de Gegevensbeschermingsautoriteit en aan de betrokkenen.

## 7. Doorgifte buiten de EER

Gegevens worden opgeslagen in de EER. Doorgifte buiten de EER gebeurt alleen met passende waarborgen (bijvoorbeeld standaardcontractbepalingen of een adequaatheidsbesluit).

## 8. Aansprakelijkheid

[Aan te vullen in lijn met de dienstverleningsovereenkomst.]

## 9. Einde

Bij het einde van de overeenkomst bezorgt de verwerker de zaak op verzoek een export van haar gegevens en wist daarna binnen [30] dagen alle persoonsgegevens van de klanten van de zaak, ook in back-ups binnen de gewone rotatietermijn, tenzij een wettelijke bewaarplicht geldt.

## 10. Toepasselijk recht

Belgisch recht. Geschillen worden voorgelegd aan de rechtbanken van [arrondissement].

Opgemaakt te [plaats] op [datum], in twee exemplaren.

| Voor de zaak | Voor de verwerker |
| --- | --- |
| [naam, functie, handtekening] | [naam, functie, handtekening] |
