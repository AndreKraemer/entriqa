namespace Entriqa.Domain.Validation;

/// <summary>
/// Besucherseitige Melde- und UI-Texte je Sprache. Eine Quelle für Server (Validator) und Browser
/// (forms.js bekommt sie als <c>strings</c> in der PublicFormView). Unbekannte Sprache → Deutsch.
/// </summary>
public static class ValidationMessages
{
    public const string Required = "required", ConsentRequired = "consentRequired", ConsentIfEmail = "consentIfEmail",
        Email = "email", BusinessEmail = "businessEmail", Number = "number", NumberRange = "numberRange", Date = "date", Tel = "tel",
        Choice = "choice", MaxLength = "maxLength", TooLong = "tooLong", TooBig = "tooBig", QuizMissing = "quizMissing",
        UploadInvalid = "uploadInvalid", UploadTooBig = "uploadTooBig", UploadType = "uploadType", Uploading = "uploading", Uploaded = "uploaded",
        Choose = "choose", Next = "next", Back = "back", AnswerRequired = "answerRequired", QuestionLabel = "question",
        LoadError = "loadError", SubmitError = "submitError";

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
    };

    public static IReadOnlyDictionary<string, string> For(string? lang) => lang == "en" ? En : De;

    public static string Get(string? lang, string key) => For(lang).TryGetValue(key, out var v) ? v : key;
}

/// <summary>Standard-Blocklist für <c>businessOnly</c>-E-Mail-Felder; pro Instanz per App-Setting erweiterbar.</summary>
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
