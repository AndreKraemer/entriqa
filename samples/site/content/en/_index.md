---
title: "Entriqa Sample Site"
---

This site exists so that Entriqa can be tried out and tested locally. It imports the Hugo
module from `hugo/` exactly the way a customer site would, and shows every form that lives
under `seed/forms/`.

The forms are real: the API publishes them at startup and stores submissions in Azurite.
Mails end up as HTML files in your temp directory, not in anyone's inbox.
