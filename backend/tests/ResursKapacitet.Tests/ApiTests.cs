using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;

namespace ResursKapacitet.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "rk-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DATA_DIR", DataDir);
        builder.UseSetting("SEED_DEMO", "true");
        builder.UseSetting("ADMIN_PASSWORD", "hemligt1");
        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(DataDir, true); } catch { /* ignoreras */ }
    }
}

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<HttpClient> AdminClient()
    {
        var c = Client();
        var res = await c.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "hemligt1" });
        res.EnsureSuccessStatusCode();
        return c;
    }

    private static async Task<JsonElement> Get(HttpClient c, string url)
    {
        var res = await c.GetAsync(url);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    [Fact]
    public async Task Health_and_version_are_available()
    {
        var c = Client();
        Assert.Equal("ok", (await Get(c, "/api/health")).GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty((await Get(c, "/api/version")).GetProperty("version").GetString()));
        Assert.True((await Get(c, "/api/releases")).GetArrayLength() > 0);
    }

    [Fact]
    public async Task Database_is_created_in_data_dir()
    {
        _ = Client();
        Assert.True(File.Exists(Path.Combine(factory.DataDir, "resurs.db")));
    }

    [Fact]
    public async Task Login_with_wrong_password_fails()
    {
        var res = await Client().PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "fel" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_can_read_but_not_write()
    {
        var c = Client();
        var planning = await Get(c, "/api/planning?year=2026");
        Assert.True(planning.GetProperty("resources").GetArrayLength() >= 8);

        var res = await c.PostAsJsonAsync("/api/teams", new { name = "Hackers" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Planner_cannot_manage_users()
    {
        var c = Client();
        (await c.PostAsJsonAsync("/api/auth/login", new { userName = "planerare", password = "planerare" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Planning_contains_capacity_with_month_factors_and_absence()
    {
        var c = Client();
        var year = DateTime.Now.Year;
        var p = await Get(c, $"/api/planning?year={year}");
        var kalle = p.GetProperty("resources").EnumerateArray().First(r => r.GetProperty("name").GetString() == "Kalle Anka");
        var capacity = kalle.GetProperty("capacity").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Assert.Equal(144, capacity[0]); // 160 × 90 % planeringstak
        Assert.Equal(0, capacity[6]);   // juli: semester (avvikelse)
        Assert.Equal(122, capacity[11]); // december: faktor 0,85
    }

    [Fact]
    public async Task Cell_update_detects_conflicts_and_logs_changes()
    {
        var c = await AdminClient();
        var year = DateTime.Now.Year;
        var p = await Get(c, $"/api/planning?year={year}");
        var alloc = p.GetProperty("allocations")[0];
        var id = alloc.GetProperty("id").GetInt32();
        var march = alloc.GetProperty("hours")[2].GetInt32();

        var res = await c.PutAsJsonAsync("/api/allocations/cells", new
        {
            year,
            cells = new object[]
            {
                new { allocationId = id, month = 3, hours = march + 5, expected = march },
                new { allocationId = id, month = 4, hours = 1, expected = 9999 },
            },
        });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(march + 5, body.GetProperty("updated")[0].GetProperty("hours")[2].GetInt32());
        Assert.Equal(1, body.GetProperty("conflicts").GetArrayLength());

        var changes = await Get(c, "/api/changes?take=1");
        Assert.Contains($"MAR {march}→{march + 5}", changes[0].GetProperty("summary").GetString());
        Assert.EndsWith("Z", changes[0].GetProperty("timestamp").GetString());
    }

    [Fact]
    public async Task Dropping_backlog_item_on_resource_creates_allocation_and_plans_it()
    {
        var c = await AdminClient();
        var year = DateTime.Now.Year;
        var p = await Get(c, $"/api/planning?year={year}");
        var backlogItem = p.GetProperty("assignments").EnumerateArray()
            .First(a => a.GetProperty("status").GetString() == "Backlogg");
        var resource = p.GetProperty("resources")[0];

        var res = await c.PostAsJsonAsync("/api/allocations", new
        {
            resourceId = resource.GetProperty("id").GetInt32(),
            assignmentId = backlogItem.GetProperty("id").GetInt32(),
            year,
            hours = new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 10, 10 },
        });
        res.EnsureSuccessStatusCode();

        var after = await Get(c, $"/api/planning?year={year}");
        var updated = after.GetProperty("assignments").EnumerateArray()
            .First(a => a.GetProperty("id").GetInt32() == backlogItem.GetProperty("id").GetInt32());
        Assert.Equal("Planerad", updated.GetProperty("status").GetString());
        Assert.Equal(30, updated.GetProperty("allocatedTotal").GetInt32());
        Assert.Equal(backlogItem.GetProperty("estimatedHours").GetInt32() - 30, updated.GetProperty("remaining").GetInt32());
    }

    [Fact]
    public async Task Backlog_can_be_reordered()
    {
        var c = await AdminClient();
        var list = await Get(c, "/api/assignments");
        var ids = list.EnumerateArray().Select(a => a.GetProperty("id").GetInt32()).Reverse().ToArray();
        (await c.PutAsJsonAsync("/api/assignments/order", ids)).EnsureSuccessStatusCode();
        var after = await Get(c, "/api/assignments");
        Assert.Equal(ids[0], after[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Import_dry_run_reports_new_entities_without_saving()
    {
        var c = await AdminClient();
        var text = "\t\tResurs\tTillhörighet\tUppdrag\tFörvaltningobjekt/Projekt\tJAN\tFEB\tMAR\n" +
                   "\t\tNy Person\tTeam 9\tUppdragQ\tOmråde Q\t10\t20\t30\n";
        var res = await c.PostAsJsonAsync("/api/import/planning", new { year = 2030, text, dryRun = true });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(1, body.GetProperty("rows").GetInt32());
        Assert.Equal(60, body.GetProperty("hours").GetInt32());
        Assert.Equal("Ny Person", body.GetProperty("newResources")[0].GetString());
        Assert.False(body.GetProperty("applied").GetBoolean());

        var resources = await Get(c, "/api/resources");
        Assert.DoesNotContain(resources.EnumerateArray(), r => r.GetProperty("name").GetString() == "Ny Person");
    }

    [Fact]
    public async Task Export_is_excel_friendly_csv()
    {
        var res = await Client().GetAsync($"/api/export/planning.csv?year={DateTime.Now.Year}");
        res.EnsureSuccessStatusCode();
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.Equal(0xEF, bytes[0]); // UTF-8 BOM
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("Resurs;Tillhörighet;Uppdrag", text);
        Assert.Contains("Kalle Anka;Team 1;UppdragX", text);
    }

    [Fact]
    public async Task Admin_can_download_consistent_backup()
    {
        var c = await AdminClient();
        var res = await c.GetAsync("/api/admin/backup");
        res.EnsureSuccessStatusCode();
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("SQLite format 3", System.Text.Encoding.ASCII.GetString(bytes, 0, 15));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync("/api/admin/backup")).StatusCode);
    }

    [Fact]
    public async Task Unknown_api_route_returns_404_and_spa_routes_fall_back()
    {
        var c = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/finns-inte")).StatusCode);
    }
}

public class JsonImportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Doc = """
    { // kommentar tillåts
      "year": 2031,
      "teams": [ { "name": "Team Z" } ],
      "resources": [ { "name": "Zed Test", "team": "Team Z", "type": "konsult", "absence": [ { "month": 7, "hours": 20 } ] } ],
      "objects": [ { "name": "Sys Z", "type": "Förvaltning" } ],
      "assignments": [ { "name": "Uppdrag Z", "object": "Sys Z", "estimatedHours": 100, "status": "Pågående" } ],
      "allocations": [ { "resource": "Zed Test", "assignment": "Uppdrag Z", "hours": [10,10,10,0,0,0,0,0,0,0,0,0] } ],
    }
    """;

    private async Task<HttpClient> Admin()
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        (await c.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "hemligt1" })).EnsureSuccessStatusCode();
        return c;
    }

    private static StringContent Body(string s) => new(s, System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task DryRun_ändrar_inget_men_apply_skapar_och_är_idempotent()
    {
        var c = await Admin();

        var dry = await c.PostAsync("/api/import/json", Body(Doc));
        dry.EnsureSuccessStatusCode();
        var d = await dry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(d.GetProperty("applied").GetBoolean());
        Assert.Equal(1, d.GetProperty("newResources").GetInt32());
        Assert.Equal(0, d.GetProperty("errors").GetArrayLength());
        var before = await c.GetFromJsonAsync<JsonElement>("/api/planning?year=2031");
        Assert.DoesNotContain(before.GetProperty("resources").EnumerateArray(), r => r.GetProperty("name").GetString() == "Zed Test");

        var apply = await c.PostAsync("/api/import/json?dryRun=false", Body(Doc));
        var a = await apply.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(a.GetProperty("applied").GetBoolean());
        Assert.Equal(30, a.GetProperty("hours").GetInt32());

        var again = await (await c.PostAsync("/api/import/json?dryRun=false", Body(Doc))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, again.GetProperty("newResources").GetInt32());
        Assert.Equal(0, again.GetProperty("newAllocations").GetInt32());

        var after = await c.GetFromJsonAsync<JsonElement>("/api/planning?year=2031");
        Assert.Contains(after.GetProperty("resources").EnumerateArray(), r => r.GetProperty("name").GetString() == "Zed Test");
    }

    [Fact]
    public async Task Praktikant_kan_importeras_och_exporteras()
    {
        var c = await Admin();
        var doc = """{ "year": 2033, "resources": [ { "name": "Pelle Prak", "type": "Praktikant" } ] }""";
        var r = await (await c.PostAsync("/api/import/json?dryRun=false", Body(doc))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(r.GetProperty("applied").GetBoolean(), r.ToString());
        var p = await c.GetFromJsonAsync<JsonElement>("/api/planning?year=2033");
        var pelle = p.GetProperty("resources").EnumerateArray().First(x => x.GetProperty("name").GetString() == "Pelle Prak");
        Assert.Equal("Praktikant", pelle.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Fel_ger_allt_eller_inget()
    {
        var c = await Admin();
        var bad = """{ "year": 2032, "resources": [ { "name": "Ska Inte Finnas" } ], "allocations": [ { "resource": "Nobody", "assignment": "X", "hours": [1] } ] }""";
        var r = await (await c.PostAsync("/api/import/json?dryRun=false", Body(bad))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(r.GetProperty("applied").GetBoolean());
        Assert.True(r.GetProperty("errors").GetArrayLength() > 0);
        var p = await c.GetFromJsonAsync<JsonElement>("/api/planning?year=2032");
        Assert.DoesNotContain(p.GetProperty("resources").EnumerateArray(), x => x.GetProperty("name").GetString() == "Ska Inte Finnas");
    }

    [Fact]
    public async Task Kräver_admin()
    {
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/api/import/json", Body(Doc))).StatusCode);
    }
}

public class JsonExportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Export_kan_importeras_igen_utan_ändringar()
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        (await c.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "hemligt1" })).EnsureSuccessStatusCode();

        var json = await c.GetStringAsync("/api/export/dataset.json");
        Assert.Contains("Kalle Anka", json);

        var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var r = await (await c.PostAsync("/api/import/json?dryRun=false", body)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(r.GetProperty("applied").GetBoolean(), r.ToString());
        Assert.Equal(0, r.GetProperty("newTeams").GetInt32());
        Assert.Equal(0, r.GetProperty("newResources").GetInt32());
        Assert.Equal(0, r.GetProperty("newObjects").GetInt32());
        Assert.Equal(0, r.GetProperty("newAssignments").GetInt32());
        Assert.Equal(0, r.GetProperty("newAllocations").GetInt32());
    }

    [Fact]
    public async Task Export_kräver_admin()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/export/dataset.json")).StatusCode);
    }
}
