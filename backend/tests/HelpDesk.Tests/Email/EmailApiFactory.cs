using System.Collections.Concurrent;
using HelpDesk.Infrastructure.Email;
using HelpDesk.Tests.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HelpDesk.Tests.Email;

/// <summary>The API with notifications turned on, no background workers, and a mail server that only records.</summary>
public sealed class EmailApiFactory : HelpDeskApiFactory
{
    public const string HelpMailbox = "ayuda@test.local";

    public FakeMailSender Mail { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Email:RunWorkers", "false");
        builder.UseSetting("Email:AppUrl", "https://ayuda.test.local");
        builder.UseSetting("Email:Outbound:Provider", "Smtp");
        builder.UseSetting("Email:Outbound:FromAddress", HelpMailbox);
        builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<IMailSender>(Mail)));
    }

    /// <summary>Runs one email through the same code the inbound worker uses.</summary>
    public async Task<InboundOutcome?> ReceiveAsync(InboundEmail email)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EmailIngestionService>().ProcessAsync(email, CancellationToken.None);
    }

    /// <summary>Runs the outbox once, like the outbound worker does.</summary>
    public async Task SendPendingAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationSender>().SendPendingAsync(CancellationToken.None);
    }
}

public sealed class FakeMailSender : IMailSender
{
    public ConcurrentQueue<OutboundEmail> Sent { get; } = new();

    public bool Fail { get; set; }

    public Task SendAsync(OutboundEmail email, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Servidor de correo caído");
        }

        Sent.Enqueue(email);
        return Task.CompletedTask;
    }

    public IReadOnlyList<OutboundEmail> To(string address) =>
        Sent.Where(e => e.To.Equals(address, StringComparison.OrdinalIgnoreCase)).ToList();
}
