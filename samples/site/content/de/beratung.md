---
title: "Beratungsanfrage"
translationKey: "consulting"
lead: "Mehrseitig, mit bedingtem Feld, Bewertungsskala und Datei-Upload."
menu:
  main:
    weight: 20
---

Dieses Formular deckt alle übrigen Feldtypen ab: `section`, `divider`, `page`, `select`,
`multiselect`, `checkbox`, `date`, `number`, `tel`, `rating`, `file` und `hidden`.

Zwei Dinge lohnen einen genauen Blick: Der Seitenumbruch teilt das Formular in zwei
Schritte, und das Feld „Und zwar?" erscheint erst, wenn oben „Etwas anderes" gewählt wird –
die Bedingung arbeitet mit Options-Indizes, damit sie sprachneutral bleibt.

{{< entriqa "beratung" >}}

<p class="note">Das E-Mail-Feld ist auf Geschäftsadressen eingestellt
(<code>businessOnly</code>) – eine Freemail-Adresse wird abgelehnt.</p>
