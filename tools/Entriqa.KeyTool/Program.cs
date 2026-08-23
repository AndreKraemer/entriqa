// Entriqa-Lizenzwerkzeug – läuft NUR beim Hersteller, nie beim Kunden.
//   dotnet run -- new                                      → erzeugt ein Schlüsselpaar
//   dotnet run -- issue <privKey> <id> <plan> <bis>        → stellt einen Lizenzschlüssel aus
// Der private Schlüssel gehört in den Passwort-Manager, NIE ins Repository.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

switch (args.FirstOrDefault())
{
    case "new":
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Console.WriteLine("PRIVATE (PKCS8, geheim halten!):");
        Console.WriteLine(Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()));
        Console.WriteLine();
        Console.WriteLine("PUBLIC (SPKI, in LicenseService.VendorPublicKey einsetzen):");
        Console.WriteLine(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        return 0;
    }
    case "issue" when args.Length == 5:
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = args[2], plan = args[3], until = args[4] }));
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(args[1]), out _);
        var sig = ecdsa.SignData(payload, HashAlgorithmName.SHA256);
        static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Console.WriteLine($"ENTRIQA-{B64Url(payload)}.{B64Url(sig)}");
        return 0;
    }
    default:
        Console.WriteLine("Aufrufe: new | issue <privKeyBase64> <lizenzId> <plan> <yyyy-MM-dd>");
        return 1;
}
