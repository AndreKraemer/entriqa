using Entriqa.Domain.Localization;

namespace Entriqa.Domain.Errors;

/// <summary>
/// Texts of the typed exceptions, per language. An exception carries the key plus its arguments,
/// the edge renders the text in the language of the request (see ProblemDetailsMiddleware) -
/// that way a throw site deep inside a use case does not have to know the caller's language.
/// Same shape as <c>ValidationMessages</c> for the visitor-facing field errors. Unknown language -> German.
/// </summary>
public static class ErrorMessages
{
    public const string
        Internal = "internal",
        ValidationFailed = "validationFailed",
        Forbidden = "forbidden",
        NotSignedIn = "notSignedIn",
        RoleRequired = "roleRequired",
        PrincipalInvalid = "principalInvalid",
        HousekeepingUnauthorized = "housekeepingUnauthorized",
        FormNotPublished = "formNotPublished",
        FormUnknown = "formUnknown",
        FormVersionNotFound = "formVersionNotFound",
        SubmissionNotFound = "submissionNotFound",
        SubmissionConflict = "submissionConflict",
        TokenMissing = "tokenMissing",
        TokenInvalid = "tokenInvalid",
        TokenMismatch = "tokenMismatch",
        TokenTooEarly = "tokenTooEarly",
        TokenExpired = "tokenExpired",
        TokenReplayed = "tokenReplayed",
        LinkInvalid = "linkInvalid",
        RateLimited = "rateLimited",
        EmptyRequest = "emptyRequest",
        NoFileSubmitted = "noFileSubmitted",
        FileTooBig = "fileTooBig",
        FileTooBigMb = "fileTooBigMb",
        FileEmpty = "fileEmpty",
        FileNameMissing = "fileNameMissing",
        FormTakesNoFiles = "formTakesNoFiles",
        UploadTooBig = "uploadTooBig",
        UploadType = "uploadType",
        SlugInvalid = "slugInvalid",
        HandlingInvalid = "handlingInvalid",
        HandlingUnsupported = "handlingUnsupported",
        AlreadyConfirmed = "alreadyConfirmed",
        FormWithoutDoi = "formWithoutDoi",
        QuizPathLoop = "quizPathLoop",
        QuizResultMissing = "quizResultMissing",
        StepUnknown = "stepUnknown";

    private static readonly Dictionary<string, string> De = new()
    {
        [Internal] = "Interner Fehler.",
        [ValidationFailed] = "Eingaben sind ungültig.",
        [Forbidden] = "Keine Berechtigung.",
        [NotSignedIn] = "Nicht angemeldet.",
        [RoleRequired] = "Rolle '{role}' erforderlich.",
        [PrincipalInvalid] = "Ungültiger Principal.",
        [HousekeepingUnauthorized] = "Housekeeping nicht autorisiert.",
        [FormNotPublished] = "Formular '{slug}' ist nicht veröffentlicht.",
        [FormUnknown] = "Formular '{slug}' ist unbekannt.",
        [FormVersionNotFound] = "Formularversion nicht gefunden.",
        [SubmissionNotFound] = "Einsendung nicht gefunden.",
        [SubmissionConflict] = "Die Einsendung wurde parallel verändert.",
        [TokenMissing] = "Token fehlt.",
        [TokenInvalid] = "Token ungültig.",
        [TokenMismatch] = "Token passt nicht zu dieser Anfrage.",
        [TokenTooEarly] = "Zu schnell abgeschickt.",
        [TokenExpired] = "Das Formular ist abgelaufen – bitte Seite neu laden.",
        [TokenReplayed] = "Dieses Formular wurde bereits abgeschickt – bitte Seite neu laden.",
        [LinkInvalid] = "Link ungültig.",
        [RateLimited] = "Zu viele Einsendungen – bitte später erneut versuchen.",
        [EmptyRequest] = "Leerer Request.",
        [NoFileSubmitted] = "Keine Datei übermittelt.",
        [FileTooBig] = "Datei zu groß.",
        [FileTooBigMb] = "Datei größer als {max} MB.",
        [FileEmpty] = "Leere Datei.",
        [FileNameMissing] = "Dateiname fehlt.",
        [FormTakesNoFiles] = "Dieses Formular nimmt keine Dateien an.",
        [UploadTooBig] = "Die Datei ist zu groß (höchstens {max} MB).",
        [UploadType] = "Dieser Dateityp ist nicht erlaubt.",
        [SlugInvalid] = "Slug darf nur Kleinbuchstaben, Ziffern und Bindestriche enthalten.",
        [HandlingInvalid] = "Bearbeitungsstatus muss 'open' oder 'done' sein.",
        [HandlingUnsupported] = "Dieses Formular führt keinen Bearbeitungsstatus.",
        [AlreadyConfirmed] = "Schon bestätigt – nichts zu senden.",
        [FormWithoutDoi] = "Dieses Formular hat kein Double-Opt-In.",
        [QuizPathLoop] = "Quiz-Pfad endet nicht.",
        [QuizResultMissing] = "Sprungziel-Ergebnis '{id}' fehlt.",
        [StepUnknown] = "Unbekannter Schritt '{key}'.",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        [Internal] = "Internal error.",
        [ValidationFailed] = "Your input is invalid.",
        [Forbidden] = "Not allowed.",
        [NotSignedIn] = "Not signed in.",
        [RoleRequired] = "Role '{role}' required.",
        [PrincipalInvalid] = "Invalid principal.",
        [HousekeepingUnauthorized] = "Housekeeping is not authorized.",
        [FormNotPublished] = "Form '{slug}' is not published.",
        [FormUnknown] = "Form '{slug}' is unknown.",
        [FormVersionNotFound] = "Form version not found.",
        [SubmissionNotFound] = "Submission not found.",
        [SubmissionConflict] = "The submission was changed at the same time.",
        [TokenMissing] = "Token is missing.",
        [TokenInvalid] = "Token is invalid.",
        [TokenMismatch] = "The token does not match this request.",
        [TokenTooEarly] = "Submitted too quickly.",
        [TokenExpired] = "The form has expired – please reload the page.",
        [TokenReplayed] = "This form has already been submitted – please reload the page.",
        [LinkInvalid] = "The link is invalid.",
        [RateLimited] = "Too many submissions – please try again later.",
        [EmptyRequest] = "Empty request.",
        [NoFileSubmitted] = "No file submitted.",
        [FileTooBig] = "The file is too large.",
        [FileTooBigMb] = "The file is larger than {max} MB.",
        [FileEmpty] = "The file is empty.",
        [FileNameMissing] = "The file name is missing.",
        [FormTakesNoFiles] = "This form does not accept files.",
        [UploadTooBig] = "The file is too large (at most {max} MB).",
        [UploadType] = "This file type is not allowed.",
        [SlugInvalid] = "A slug may only contain lowercase letters, digits and hyphens.",
        [HandlingInvalid] = "The handling state has to be 'open' or 'done'.",
        [HandlingUnsupported] = "This form does not track a handling state.",
        [AlreadyConfirmed] = "Already confirmed – nothing to send.",
        [FormWithoutDoi] = "This form has no double opt-in.",
        [QuizPathLoop] = "The quiz path does not end.",
        [QuizResultMissing] = "Jump target result '{id}' is missing.",
        [StepUnknown] = "Unknown step '{key}'.",
    };

    /// <summary>The reference every other locale is measured against; German is the fallback, so it defines the keys.</summary>
    public const string Reference = "de";

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ByLocale =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [Reference] = De,
            ["en"] = En,
        };

    /// <summary>The locales this catalog can serve completely. Adding a set above is all it takes to extend it.</summary>
    public static IReadOnlyList<string> Locales { get; } = LocaleCatalog.CompleteLocales(ByLocale, Reference);

    /// <summary>
    /// The texts of exactly this locale, or null when the catalog has none. Unlike <see cref="For"/> this
    /// does not fall back to German - a caller that needs to know whether a language is really carried
    /// here cannot ask <see cref="For"/>, because its fallback answers for every locale ever passed.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Texts(string locale) =>
        ByLocale.TryGetValue(locale, out var texts) ? texts : null;

    public static IReadOnlyDictionary<string, string> For(string? lang) =>
        lang is not null && ByLocale.TryGetValue(lang, out var texts) ? texts : De;

    /// <summary>Renders the text of a key; <paramref name="args"/> replaces {placeholders}. Unknown key -> the key itself.</summary>
    public static string Get(string? lang, string key, IReadOnlyDictionary<string, string>? args = null)
    {
        if (!For(lang).TryGetValue(key, out var text)) return key;
        if (args is null) return text;
        foreach (var (name, value) in args) text = text.Replace("{" + name + "}", value);
        return text;
    }
}
