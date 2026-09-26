---
title: "Consulting request"
translationKey: "consulting"
lead: "Multi-step, with a conditional field, a rating scale and a file upload."
menu:
  main:
    weight: 20
---

This form covers all remaining field types: `section`, `divider`, `page`, `select`,
`multiselect`, `checkbox`, `date`, `number`, `tel`, `rating`, `file`, `appointment` and `hidden`.

Two things are worth a close look: the page break splits the form into two steps, and the
"Namely?" field only appears once "Something else" is chosen above — the condition works on
option indexes so that it stays language-neutral. The appointment selection only shows dates
that have not begun yet, in your browser's time zone.

{{< entriqa "beratung" >}}

<p class="note">The email field is set to business addresses only
(<code>businessOnly</code>) — a freemail address is rejected.</p>
