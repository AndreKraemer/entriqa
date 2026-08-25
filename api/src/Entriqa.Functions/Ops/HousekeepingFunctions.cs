using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;
using Entriqa.Application;
using Entriqa.Domain.Errors;
using Entriqa.Domain.UseCases;

namespace Entriqa.Functions.Ops;

/// <summary>
/// Called by the DevOps schedule (cron every 15 min) - SWA managed functions cannot do timer triggers,
/// hence HTTP plus a secret header. Without a configured key the endpoint is dead (no default secret).
/// </summary>
public sealed class HousekeepingFunctions(IRunHousekeepingUseCase housekeeping, IOptions<EntriqaOptions> options)
{
    [Function("Housekeeping")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "housekeeping")] HttpRequest req, CancellationToken ct)
    {
        var expected = options.Value.HousekeepingKey;
        var provided = req.Headers["x-housekeeping-key"].FirstOrDefault() ?? "";
        if (string.IsNullOrEmpty(expected) || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
            throw new ForbiddenException("Housekeeping nicht autorisiert.");

        return new OkObjectResult(await housekeeping.ExecuteAsync(ct));
    }
}
