using Entriqa.Domain.Localization;

namespace Entriqa.Domain.Validation;

/// <summary>
/// Visitor-facing messages and UI texts per language. One source for the server (validator) and the browser
/// (forms.js receives them as <c>strings</c> in the PublicFormView). Unknown language -> German.
/// </summary>
public static class ValidationMessages
{
    public const string Required = "required", ConsentRequired = "consentRequired", ConsentIfEmail = "consentIfEmail",
        Email = "email", BusinessEmail = "businessEmail", Number = "number", NumberRange = "numberRange", Date = "date", Tel = "tel",
        Choice = "choice", MaxLength = "maxLength", TooLong = "tooLong", TooBig = "tooBig", QuizMissing = "quizMissing",
        UploadInvalid = "uploadInvalid", UploadTooBig = "uploadTooBig", UploadType = "uploadType", Uploading = "uploading", Uploaded = "uploaded",
        Choose = "choose", Next = "next", Back = "back", AnswerRequired = "answerRequired", QuestionLabel = "question",
        LoadError = "loadError", SubmitError = "submitError",
        // #7: an appointment that is not (or no longer) on offer, and the notice when none is left.
        AppointmentChoice = "appointmentChoice", NoAppointments = "noAppointments",
        // #21: the admin test protocol, rendered by forms.js in the form's language (like every other string here).
        TestReportTitle = "testReportTitle", TestMailTo = "testMailTo", TestNoAddress = "testNoAddress",
        TestNoteList = "testNoteList", TestNoteCompany = "testNoteCompany", TestNoteFile = "testNoteFile",
        TestNoteTemplate = "testNoteTemplate", TestNoteTarget = "testNoteTarget", TestNoteTeams = "testNoteTeams";

    private static readonly Dictionary<string, string> De = new()
    {
        [Required] = "Pflichtfeld.",
        [ConsentRequired] = "Bitte bestätige die Einwilligung.",
        [ConsentIfEmail] = "Bitte bestätige die Einwilligung, wenn du eine E-Mail-Adresse angibst.",
        [Email] = "Bitte eine gültige E-Mail-Adresse eingeben.",
        [BusinessEmail] = "Bitte eine geschäftliche E-Mail-Adresse verwenden.",
        [Number] = "Bitte eine Zahl eingeben.",
        [NumberRange] = "Bitte einen Wert zwischen {min} und {max} eingeben.",
        [Date] = "Bitte ein gültiges Datum eingeben.",
        [Tel] = "Bitte eine gültige Telefonnummer eingeben.",
        [Choice] = "Ungültige Auswahl.",
        [MaxLength] = "Höchstens {max} Zeichen.",
        [TooLong] = "Zu lang.",
        [TooBig] = "Die Eingaben sind insgesamt zu umfangreich – bitte kürzen.",
        [QuizMissing] = "Keine Antworten übermittelt.",
        [Choose] = "Bitte wählen",
        [Next] = "Weiter",
        [Back] = "Zurück",
        [AnswerRequired] = "Bitte eine Antwort wählen.",
        [QuestionLabel] = "Frage",
        [LoadError] = "Das Formular konnte nicht geladen werden.",
        [SubmitError] = "Das hat leider nicht geklappt. Bitte versuche es erneut.",
        [UploadInvalid] = "Die Datei konnte nicht übernommen werden – bitte erneut hochladen.",
        [UploadTooBig] = "Die Datei ist zu groß (höchstens {max} MB).",
        [UploadType] = "Dieser Dateityp ist nicht erlaubt.",
        [Uploading] = "lädt hoch …",
        [Uploaded] = "hochgeladen",
        [TestReportTitle] = "Testprotokoll",
        [TestMailTo] = "Testmails an:",
        [TestNoAddress] = "Keine Adresse des angemeldeten Admins – der Testversand wurde nicht ausgeführt.",
        [TestNoteList] = "Listen",
        [TestNoteCompany] = "Firma",
        [TestNoteFile] = "Datei",
        [TestNoteTemplate] = "Vorlage",
        [TestNoteTarget] = "Zieladresse",
        [TestNoteTeams] = "Teams-Webhook",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        [Required] = "Required.",
        [ConsentRequired] = "Please confirm your consent.",
        [ConsentIfEmail] = "Please confirm your consent when providing an email address.",
        [Email] = "Please enter a valid email address.",
        [BusinessEmail] = "Please use a business email address.",
        [Number] = "Please enter a number.",
        [NumberRange] = "Please enter a value between {min} and {max}.",
        [Date] = "Please enter a valid date.",
        [Tel] = "Please enter a valid phone number.",
        [Choice] = "Invalid choice.",
        [MaxLength] = "At most {max} characters.",
        [TooLong] = "Too long.",
        [TooBig] = "Your input is too large overall – please shorten it.",
        [QuizMissing] = "No answers submitted.",
        [Choose] = "Please choose",
        [Next] = "Next",
        [Back] = "Back",
        [AnswerRequired] = "Please choose an answer.",
        [QuestionLabel] = "Question",
        [LoadError] = "The form could not be loaded.",
        [SubmitError] = "Something went wrong. Please try again.",
        [UploadInvalid] = "The file could not be accepted – please upload it again.",
        [UploadTooBig] = "The file is too large (at most {max} MB).",
        [UploadType] = "This file type is not allowed.",
        [Uploading] = "uploading …",
        [Uploaded] = "uploaded",
        [TestReportTitle] = "Test log",
        [TestMailTo] = "Test mails to:",
        [TestNoAddress] = "No address for the signed-in admin – the test mail was not sent.",
        [TestNoteList] = "Lists",
        [TestNoteCompany] = "Company",
        [TestNoteFile] = "File",
        [TestNoteTemplate] = "Template",
        [TestNoteTarget] = "Target address",
        [TestNoteTeams] = "Teams webhook",
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

    public static string Get(string? lang, string key) => For(lang).TryGetValue(key, out var v) ? v : key;
}

/// <summary>Default blocklist for <c>businessOnly</c> email fields; extendable per instance via app setting.</summary>
public static class FreemailDomains
{
    public static readonly IReadOnlySet<string> Default = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "gmx.de", "gmx.net", "gmx.at", "gmx.ch", "web.de",
        "outlook.com", "outlook.de", "hotmail.com", "hotmail.de", "live.com", "live.de", "msn.com",
        "yahoo.com", "yahoo.de", "t-online.de", "icloud.com", "me.com", "mac.com",
        "freenet.de", "aol.com", "posteo.de", "mail.de", "email.de", "protonmail.com", "proton.me", "pm.me",
    };

    public static bool IsFreemail(string email, IReadOnlySet<string>? extra = null)
    {
        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1) return false;
        var domain = email[(at + 1)..].Trim();
        return Default.Contains(domain) || (extra?.Contains(domain) ?? false);
    }
}
