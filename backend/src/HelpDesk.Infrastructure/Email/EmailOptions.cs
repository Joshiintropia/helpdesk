namespace HelpDesk.Infrastructure.Email;

/// <summary>
/// "Email" settings. Reading the help mailbox and sending notifications are independent: each one can use
/// Microsoft 365 (Graph) or a standard server (IMAP / SMTP), or be turned off.
/// </summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>Public address of the web app, used for links in notifications (e.g. https://ayuda.miempresa.com).</summary>
    public string AppUrl { get; set; } = "http://localhost:8080";

    /// <summary>Background polling and sending. Tests turn it off and drive the processors directly.</summary>
    public bool RunWorkers { get; set; } = true;

    public InboundOptions Inbound { get; set; } = new();
    public OutboundOptions Outbound { get; set; } = new();
    public GraphOptions Graph { get; set; } = new();
}

public enum MailProvider
{
    None,
    Graph,
    Imap,
    Smtp,
}

public sealed class InboundOptions
{
    /// <summary>None, Graph or Imap.</summary>
    public MailProvider Provider { get; set; } = MailProvider.None;

    public int PollSeconds { get; set; } = 60;

    /// <summary>Area that receives new tickets created by email. Empty: the first area that accepts requests.</summary>
    public int? DefaultAreaId { get; set; }

    /// <summary>When true, a sender without an account gets a requester account. When false, the email is ignored.</summary>
    public bool CreateUnknownSenders { get; set; } = true;

    public ImapOptions Imap { get; set; } = new();
}

public sealed class OutboundOptions
{
    /// <summary>None, Graph or Smtp.</summary>
    public MailProvider Provider { get; set; } = MailProvider.None;

    /// <summary>Usually the same help mailbox, so replies come back as ticket messages.</summary>
    public string FromAddress { get; set; } = "";

    public string FromName { get; set; } = "Mesa de ayuda";

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class ImapOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;

    /// <summary>STARTTLS on 587 (default); set false for implicit TLS on 465.</summary>
    public bool UseStartTls { get; set; } = true;

    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

/// <summary>App registration with the Mail.ReadWrite and Mail.Send application permissions.</summary>
public sealed class GraphOptions
{
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>The shared help mailbox, e.g. ayuda@miempresa.com.</summary>
    public string Mailbox { get; set; } = "";
}
