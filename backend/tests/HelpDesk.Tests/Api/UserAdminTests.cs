using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HelpDesk.Api.Endpoints;

namespace HelpDesk.Tests.Api;

/// <summary>Accounts an administrator creates for people without Microsoft 365.</summary>
public class UserAdminTests(HelpDeskApiFactory factory) : IClassFixture<HelpDeskApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task New_user_signs_in_with_the_temporary_password_and_must_change_it_before_anything_else()
    {
        var admin = await factory.SignInAsync(HelpDeskApiFactory.AdminEmail, register: false);
        var area = await (await admin.PostAsJsonAsync("/api/areas", new { name = "Soporte TI" })).Content.ReadFromJsonAsync<AreaDto>(Json);

        var created = await admin.PostAsJsonAsync("/api/admin/users", new { email = "luis@empresa.test", displayName = "Luis Pérez", areaIds = new[] { area!.Id } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = (await created.Content.ReadFromJsonAsync<UserWithPasswordDto>(Json))!;
        Assert.True(body.User.MustChangePassword);
        Assert.Contains("Agent", body.User.Roles);
        Assert.Equal([area.Id], body.User.AreaIds);

        var luis = factory.CreateClient();
        (await Login(luis, "luis@empresa.test", body.TemporaryPassword)).EnsureSuccessStatusCode();

        var me = await luis.GetFromJsonAsync<MeDto>("/api/me", Json);
        Assert.True(me!.MustChangePassword);
        var blocked = await luis.GetAsync("/api/tickets/queue");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("must_change_password", await blocked.Content.ReadAsStringAsync());

        var weak = await luis.PostAsJsonAsync("/api/account/change-password", new { currentPassword = body.TemporaryPassword, newPassword = "corta" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, weak.StatusCode);
        Assert.Contains("al menos 10 caracteres", await weak.Content.ReadAsStringAsync());

        var changed = await luis.PostAsJsonAsync("/api/account/change-password", new { currentPassword = body.TemporaryPassword, newPassword = "MiNuevaClave2026" });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await luis.GetAsync("/api/tickets/queue")).StatusCode);
        Assert.False((await luis.GetFromJsonAsync<MeDto>("/api/me", Json))!.MustChangePassword);
    }

    [Fact]
    public async Task Deactivated_users_cannot_sign_in_and_a_reset_gives_a_new_temporary_password()
    {
        var admin = await factory.SignInAsync(HelpDeskApiFactory.AdminEmail, register: false);
        var created = await (await admin.PostAsJsonAsync("/api/admin/users", new { email = "marta@empresa.test", displayName = "Marta" }))
            .Content.ReadFromJsonAsync<UserWithPasswordDto>(Json);
        var id = created!.User.Id;

        var deactivated = await admin.PutAsJsonAsync($"/api/admin/users/{id}", new { displayName = "Marta", isActive = false });
        deactivated.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(factory.CreateClient(), "marta@empresa.test", created.TemporaryPassword)).StatusCode);

        (await admin.PutAsJsonAsync($"/api/admin/users/{id}", new { displayName = "Marta", isActive = true })).EnsureSuccessStatusCode();
        var reset = await (await admin.PostAsync($"/api/admin/users/{id}/reset-password", null)).Content.ReadFromJsonAsync<UserWithPasswordDto>(Json);
        Assert.NotEqual(created.TemporaryPassword, reset!.TemporaryPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(factory.CreateClient(), "marta@empresa.test", created.TemporaryPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(factory.CreateClient(), "marta@empresa.test", reset.TemporaryPassword)).StatusCode);
    }

    [Fact]
    public async Task Administrators_cannot_lock_themselves_out()
    {
        var admin = await factory.SignInAsync(HelpDeskApiFactory.AdminEmail, register: false);
        var me = await admin.GetFromJsonAsync<MeDto>("/api/me", Json);

        var response = await admin.PutAsJsonAsync($"/api/admin/users/{me!.Id}", new { displayName = "Admin", isActive = true, roles = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Only_administrators_manage_users()
    {
        var requester = await factory.SignInAsync("curioso@empresa.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await requester.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Microsoft_sign_in_is_off_unless_configured()
    {
        var providers = await factory.CreateClient().GetFromJsonAsync<AuthProvidersDto>("/api/auth/providers", Json);

        Assert.False(providers!.Microsoft);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/api/auth/external/microsoft")).StatusCode);
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password });
}
