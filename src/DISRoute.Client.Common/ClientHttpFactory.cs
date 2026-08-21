using System.Net;
using System.Net.Http.Headers;

namespace DISRoute.Client.Common;

/// <summary>
/// Creates the shared HTTP transport used by DISRoute desktop/display clients.
/// Windows clients use the signed-in user's credentials for Negotiate by default;
/// cookies are retained so the server-issued MFA session cookie survives normal API calls.
/// </summary>
public static class ClientHttpFactory
{
    public static HttpClient Create(TimeSpan timeout)
    {
        var baseUrl = Environment.GetEnvironmentVariable("DISROUTE_SERVER_URL") ?? "http://127.0.0.1:5099/";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException("DISROUTE_SERVER_URL must be an absolute HTTP or HTTPS URI.");
        if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DISROUTE_SERVER_URL must use HTTP or HTTPS.");

        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            UseDefaultCredentials = ResolveUseDefaultCredentials(),
            AllowAutoRedirect = false
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = baseUri,
            Timeout = timeout
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-DISRoute-Request", "1");
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DISRoute", "1"));
        return client;
    }

    private static bool ResolveUseDefaultCredentials()
    {
        var configured = Environment.GetEnvironmentVariable("DISROUTE_USE_DEFAULT_CREDENTIALS");
        if (bool.TryParse(configured, out var parsed)) return parsed;
        return OperatingSystem.IsWindows();
    }
}
