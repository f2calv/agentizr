using CasCap.Client.Constants;
using CasCap.Client.Models;

namespace CasCap.IntegrationTests;

/// <summary>Prepares an in-memory host client the way the browser does before calling the administration API.</summary>
public static class AdministrationTestClient
{
    /// <summary>Optionally identifies a test administrator, then fetches the session and sends its antiforgery token.</summary>
    /// <remarks>The factory client keeps the antiforgery cookie, so later mutations validate like a browser's.</remarks>
    public static async Task<AdministrationSessionResponse> PrepareAsync(HttpClient client, string? administratorId = null)
    {
        if (administratorId is not null)
            client.DefaultRequestHeaders.Add(AdministrationSignInWebApplicationFactory.AdministratorHeader, administratorId);
        var session = await client.GetFromJsonAsync<AdministrationSessionResponse>(
            AdministrationRoutes.Session,
            TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Add(AdministrationRoutes.AntiforgeryHeaderName, session!.AntiforgeryToken);
        return session;
    }
}
