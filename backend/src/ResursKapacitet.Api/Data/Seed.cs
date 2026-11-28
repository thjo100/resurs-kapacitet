using Microsoft.EntityFrameworkCore;
using ResursKapacitet.Api.Services;

namespace ResursKapacitet.Api.Data;

public static class Seed
{
    public static async Task RunAsync(AppDbContext db, IConfiguration config, ILogger logger)
    {
        if (!await db.Settings.AnyAsync())
        {
            db.Settings.Add(new AppSettings());
            await db.SaveChangesAsync();
        }

        if (!await db.Users.AnyAsync())
        {
            var password = config["ADMIN_PASSWORD"];
            var isDefault = string.IsNullOrWhiteSpace(password);
            var admin = new User
            {
                UserName = "admin", DisplayName = "Administratör", Role = UserRole.Admin,
                MustChangePassword = isDefault,
            };
            admin.PasswordHash = Passwords.Hash(admin, isDefault ? "admin" : password!);
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            logger.LogWarning(isDefault
                ? "Skapade användaren 'admin' med lösenordet 'admin'. Byt lösenord vid första inloggningen!"
                : "Skapade användaren 'admin' med lösenord från ADMIN_PASSWORD.");
        }

        var seedDemo = config.GetValue<bool>("SEED_DEMO");
        if (seedDemo && string.IsNullOrWhiteSpace(config["SEED_FILE"]) && !await db.Teams.AnyAsync() && !await db.Resources.AnyAsync())
        {
            await SeedDemoAsync(db, DateTime.Now.Year);
            logger.LogInformation("Demodata skapad.");
        }

        // Egen data: SEED_FILE pekar på en JSON-fil som läses in när databasen är tom (se docs/egen-data.md).
        var seedFile = config["SEED_FILE"];
        if (!string.IsNullOrWhiteSpace(seedFile) && !await db.Teams.AnyAsync() && !await db.Resources.AnyAsync())
        {
            if (!File.Exists(seedFile)) logger.LogError("SEED_FILE {File} finns inte.", seedFile);
            else
            {
                var ds = JsonImport.Parse(await File.ReadAllTextAsync(seedFile));
                var result = ds is null ? null : await JsonImport.ApplyAsync(db, ds, dryRun: false);
                if (result is { Applied: true })
                    logger.LogInformation("Data inläst från {File}: {R} resurser, {A} uppdrag, {F} fördelningar.",
                        seedFile, result.NewResources, result.NewAssignments, result.NewAllocations);
                else
                    logger.LogError("Kunde inte läsa {File}: {Errors}", seedFile, string.Join(" | ", result?.Errors ?? ["tom fil"]));
            }
        }

        // Rensa gammal ändringslogg enligt inställd lagringstid.
        var settings = await db.Settings.AsNoTracking().FirstAsync();
        var cutoff = DateTime.UtcNow.AddMonths(-settings.ChangeLogRetentionMonths);
        await db.ChangeLog.Where(c => c.Timestamp < cutoff).ExecuteDeleteAsync();
    }

    public static async Task SeedDemoAsync(AppDbContext db, int year)
    {
        if (!await db.Users.AnyAsync(u => u.UserName == "planerare"))
        {
            var planner = new User { UserName = "planerare", DisplayName = "Pia Planerare", Role = UserRole.Planerare };
            planner.PasswordHash = Passwords.Hash(planner, "planerare");
            db.Users.Add(planner);
        }

        var team1 = new Team { Name = "Team 1", Lead = "Anna Svensson", Color = "#4f6bed", SortOrder = 1, Description = "Plattform och kärnsystem" };
        var team2 = new Team { Name = "Team 2", Lead = "Sara Nilsson", Color = "#0f9d8a", SortOrder = 2, Description = "Integration och e-tjänster" };
        var team3 = new Team { Name = "Team 3", Lead = "Lisa Berg", Color = "#c2410c", SortOrder = 3, Description = "Webb och UX" };
        db.Teams.AddRange(team1, team2, team3);

        var areaA = new WorkObject { Name = "Område A", Type = ObjectType.Forvaltning, Owner = "Karin Holm", Color = "#4f6bed" };
        var areaB = new WorkObject { Name = "Område B", Type = ObjectType.Forvaltning, Owner = "Per Ström", Color = "#0f9d8a" };
        var areaC = new WorkObject { Name = "Område C", Type = ObjectType.Forvaltning, Owner = "Karin Holm", Color = "#7c3aed" };
        var nova = new WorkObject { Name = "Projekt Nova", Type = ObjectType.Projekt, Owner = "Ola Dahl", Color = "#c2410c", BudgetHoursPerYear = 4000 };
        db.Objects.AddRange(areaA, areaB, areaC, nova);

        Resource R(string name, Team team, string role, ResourceType type = ResourceType.Intern, int employment = 100, int order = 0) =>
            new() { Name = name, Team = team, Role = role, Type = type, EmploymentPercent = employment, SortOrder = order };

        var kalle = R("Kalle Anka", team1, "Systemutvecklare", order: 1);
        var anna = R("Anna Svensson", team1, "Lösningsarkitekt", order: 2);
        var erik = R("Erik Lindqvist", team1, "Systemutvecklare", ResourceType.Konsult, order: 3);
        var joe = R("Joe Doe", team2, "Systemutvecklare", order: 4);
        var sara = R("Sara Nilsson", team2, "Testledare", order: 5);
        var mohammed = R("Mohammed Ali", team2, "Systemutvecklare", order: 6);
        var lisa = R("Lisa Berg", team3, "UX-designer", order: 7);
        var johan = R("Johan Ek", team3, "Systemutvecklare", employment: 80, order: 8);
        db.Resources.AddRange(kalle, anna, erik, joe, sara, mohammed, lisa, johan);

        var p = 0;
        Assignment A(string name, WorkObject obj, int estimate, AssignmentStatus status, string? requester = null) =>
            new() { Name = name, Object = obj, EstimatedHours = estimate, Status = status, Priority = ++p, Requester = requester };

        var x = A("UppdragX", areaA, 1800, AssignmentStatus.Pagaende, "Karin Holm");
        var y = A("UppdragY", areaA, 2400, AssignmentStatus.Pagaende, "Karin Holm");
        var z = A("UppdragZ", areaB, 300, AssignmentStatus.Pagaende, "Per Ström");
        var portal = A("Kundportal 2.0", nova, 4600, AssignmentStatus.Pagaende, "Ola Dahl");
        var integration = A("Integrationsplattform", areaB, 1600, AssignmentStatus.Planerad, "Per Ström");
        var backlog = new[]
        {
            A("Ny inloggningstjänst (e-legitimation)", areaA, 400, AssignmentStatus.Backlogg, "Karin Holm"),
            A("Migrering till .NET 10", areaB, 600, AssignmentStatus.Backlogg, "Per Ström"),
            A("Tillgänglighetsanpassning webb", nova, 320, AssignmentStatus.Backlogg, "Ola Dahl"),
            A("Rapportmodul", areaC, 250, AssignmentStatus.Backlogg, "Karin Holm"),
            A("API för öppna data", areaC, 180, AssignmentStatus.Backlogg),
        };
        db.Assignments.AddRange(x, y, z, portal, integration);
        db.Assignments.AddRange(backlog);

        void Alloc(Resource r, Assignment a, params int?[] hours)
        {
            var allocation = new Allocation { Resource = r, Assignment = a };
            for (var m = 0; m < 12 && m < hours.Length; m++)
                if (hours[m] is int h && h > 0)
                    allocation.Months.Add(new AllocationMonth { Year = year, Month = m + 1, Hours = h, UpdatedBy = "Demodata" });
            db.Allocations.Add(allocation);
        }

        // Från Excel-underlaget
        Alloc(kalle, x, 100, 100, 100, 100, 100, 50, null, 75, 75, 75, 75, 75);
        Alloc(kalle, y, 50, 50, 50, 50, 50, 20, null, 75, 75, 75, 75, 75);
        Alloc(joe, z, 20, 20, 20, 20, 20, 20);
        Alloc(joe, y, 90, 90, 90, 90, 90, 90, null, 90, 90, 90);
        // Övriga
        Alloc(anna, portal, 80, 80, 80, 80, 80, 70, 20, 70, 80, 80, 80, 70);
        Alloc(anna, integration, 40, 40, 40, 40, 40, 30, null, 40, 40, 40, 40, 30);
        Alloc(erik, portal, 140, 140, 140, 140, 140, 130, 40, 130, 140, 140, 140, 120);
        Alloc(sara, x, 60, 60, 60, 60, 60, 50, null, 50, 60, 60, 60, 50);
        Alloc(sara, portal, 60, 60, 60, 60, 60, 50, null, 50, 60, 30, 60, 50);
        Alloc(mohammed, integration, null, null, 120, 120, 120, 110, 30, 110, 120, 120, 120, 100);
        Alloc(lisa, portal, 100, 100, 100, 100, 100, 90, null, 90, 100, 100, 100, 90);
        Alloc(johan, y, 60, 60, 60, 60, 60, 50, null, 50, 60, 60, 60, 50);
        Alloc(johan, z, 40, 40, 40, 40, 40);

        db.CapacityOverrides.Add(new CapacityOverride { Resource = kalle, Year = year, Month = 7, Hours = 0, Reason = "Semester" });
        db.CapacityOverrides.Add(new CapacityOverride { Resource = sara, Year = year, Month = 10, Hours = 60, Reason = "Utbildning" });
        db.CapacityOverrides.Add(new CapacityOverride { Resource = lisa, Year = year, Month = 7, Hours = 0, Reason = "Semester" });

        db.ChangeLog.Add(new ChangeLogEntry
        {
            User = "System", EntityType = "System", Action = "Skapad",
            Summary = $"Demodata skapades för {year} (3 team, 8 resurser, 10 uppdrag)",
        });

        await db.SaveChangesAsync();
    }
}
