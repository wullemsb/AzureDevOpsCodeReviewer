using AzureDevOpsCodeReviewer.Config;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AzureDevOpsCodeReviewer.Services;

public sealed class WebhookRequestGuard
{
    private readonly WebhookOptions _options;

    public WebhookRequestGuard(IOptions<WebhookOptions> options)
    {
        _options = options.Value;
    }

    public bool IsAuthorized(HttpRequest request)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            return true;
        }

        if (!request.Headers.TryGetValue(_options.TokenHeaderName, out var tokenHeader))
        {
            return false;
        }

        return string.Equals(tokenHeader.ToString(), _options.Token, StringComparison.Ordinal);
    }
}
