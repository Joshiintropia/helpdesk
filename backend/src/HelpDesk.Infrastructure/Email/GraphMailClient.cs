using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using HelpDesk.Infrastructure.Attachments;
using Microsoft.Extensions.Options;

namespace HelpDesk.Infrastructure.Email;

/// <summary>
/// Reads and sends from a Microsoft 365 mailbox with Microsoft Graph, using an app registration
/// (client credentials) instead of a user password, so it keeps working with MFA and without basic auth.
/// </summary>
public sealed class GraphMailClient(HttpClient http, IOptions<EmailOptions> options, TimeProvider clock)
    : IMailboxReader, IMailSender
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";

    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? cachedToken;
    private static DateTimeOffset cachedTokenExpires;

    private GraphOptions Graph => options.Value.Graph;

    public async Task<IReadOnlyList<InboundEmail>> FetchUnreadAsync(CancellationToken cancellationToken)
    {
        var url = $"{MailboxUrl}/mailFolders/inbox/messages"
            + "?$filter=isRead eq false&$orderby=receivedDateTime&$top=25"
            + "&$select=id,internetMessageId,subject,from,body,receivedDateTime,internetMessageHeaders,hasAttachments";
        using var request = await AuthorizedAsync(HttpMethod.Get, url, cancellationToken);
        request.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");

        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var page = await response.Content.ReadFromJsonAsync<GraphPage>(cancellationToken)
            ?? throw new InvalidOperationException("Microsoft Graph devolvió una respuesta vacía.");

        var emails = new List<InboundEmail>();
        foreach (var m in page.Value.Where(m => !string.IsNullOrWhiteSpace(m.From?.EmailAddress?.Address)))
        {
            emails.Add(new InboundEmail(
                m.Id,
                string.IsNullOrWhiteSpace(m.InternetMessageId) ? $"graph-{m.Id}" : m.InternetMessageId,
                m.From!.EmailAddress!.Address!,
                m.From.EmailAddress.Name,
                m.Subject ?? "",
                m.Body?.ContentType?.Equals("html", StringComparison.OrdinalIgnoreCase) == true
                    ? ImapMailboxReader.HtmlToText(m.Body.Content)
                    : m.Body?.Content ?? "",
                m.ReceivedDateTime ?? clock.GetUtcNow(),
                IsAutoReply(m.InternetMessageHeaders),
                m.HasAttachments ? await AttachmentsAsync(m.Id, cancellationToken) : null));
        }

        return emails;
    }

    /// <summary>File attachments of one message; inline images and attached emails or links are skipped.</summary>
    private async Task<IReadOnlyList<InboundFile>> AttachmentsAsync(string messageId, CancellationToken cancellationToken)
    {
        using var request = await AuthorizedAsync(HttpMethod.Get, $"{MailboxUrl}/messages/{Uri.EscapeDataString(messageId)}/attachments", cancellationToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var page = await response.Content.ReadFromJsonAsync<GraphAttachmentPage>(cancellationToken);
        return (page?.Value ?? [])
            .Where(a => a.ODataType == "#microsoft.graph.fileAttachment" && !a.IsInline && a.ContentBytes is not null)
            .Select(a => new InboundFile(a.Name ?? "archivo", a.ContentType, Convert.FromBase64String(a.ContentBytes!)))
            .ToList();
    }

    public async Task MarkProcessedAsync(InboundEmail email, CancellationToken cancellationToken)
    {
        using var request = await AuthorizedAsync(HttpMethod.Patch, $"{MailboxUrl}/messages/{Uri.EscapeDataString(email.ProviderId)}", cancellationToken);
        request.Content = JsonContent.Create(new { isRead = true });
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SendAsync(OutboundEmail email, CancellationToken cancellationToken)
    {
        var outbound = options.Value.Outbound;
        var from = string.IsNullOrWhiteSpace(outbound.FromAddress) ? Graph.Mailbox : outbound.FromAddress;
        var payload = new
        {
            message = new
            {
                subject = email.Subject,
                body = new { contentType = "Text", content = email.Body },
                from = new { emailAddress = new { address = from, name = outbound.FromName } },
                toRecipients = new[] { new { emailAddress = new { address = email.To, name = email.ToName ?? email.To } } },
            },
            // Notifications don't need to fill the help mailbox's "Sent Items".
            saveToSentItems = false,
        };

        using var request = await AuthorizedAsync(HttpMethod.Post, $"{MailboxUrl}/sendMail", cancellationToken);
        request.Content = JsonContent.Create(payload);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private string MailboxUrl => $"{GraphBase}/users/{Uri.EscapeDataString(Graph.Mailbox)}";

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(cancellationToken));
        return request;
    }

    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (cachedToken is not null && cachedTokenExpires > clock.GetUtcNow().AddMinutes(5))
            {
                return cachedToken;
            }

            using var response = await http.PostAsync(
                $"https://login.microsoftonline.com/{Uri.EscapeDataString(Graph.TenantId)}/oauth2/v2.0/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = Graph.ClientId,
                    ["client_secret"] = Graph.ClientSecret,
                    ["scope"] = "https://graph.microsoft.com/.default",
                }),
                cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Microsoft Entra ID no devolvió un token.");

            cachedToken = token.AccessToken;
            cachedTokenExpires = clock.GetUtcNow().AddSeconds(token.ExpiresIn);
            return cachedToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Microsoft Graph respondió {(int)response.StatusCode}: {(body.Length > 500 ? body[..500] : body)}",
                null,
                response.StatusCode);
        }
    }

    private static bool IsAutoReply(List<GraphHeader>? headers) =>
        headers?.Any(h =>
            (h.Name.Equals("Auto-Submitted", StringComparison.OrdinalIgnoreCase) && !h.Value.Equals("no", StringComparison.OrdinalIgnoreCase))
            || h.Name.Equals("X-Autoreply", StringComparison.OrdinalIgnoreCase)
            || h.Name.Equals("X-Autorespond", StringComparison.OrdinalIgnoreCase)) == true;

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record GraphPage(List<GraphMessage> Value);

    private sealed record GraphMessage(
        string Id,
        string? InternetMessageId,
        string? Subject,
        GraphRecipient? From,
        GraphBody? Body,
        DateTimeOffset? ReceivedDateTime,
        List<GraphHeader>? InternetMessageHeaders,
        bool HasAttachments = false);

    private sealed record GraphAttachmentPage(List<GraphAttachment> Value);

    private sealed record GraphAttachment(
        [property: JsonPropertyName("@odata.type")] string? ODataType,
        string? Name,
        string? ContentType,
        bool IsInline,
        string? ContentBytes);

    private sealed record GraphRecipient(GraphAddress? EmailAddress);

    private sealed record GraphAddress(string? Address, string? Name);

    private sealed record GraphBody(string? ContentType, string? Content);

    private sealed record GraphHeader(string Name, string Value);
}
