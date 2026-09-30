using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portal.Application.Common.Interfaces;
using Portal.Domain.Authorization;
using Portal.Domain.Entities;
using Portal.Domain.Entities.Auditing;
using Portal.Domain.Entities.Customers;
using Portal.Domain.Entities.Sla;
using Portal.Domain.Entities.Tickets;
using Portal.Domain.Entities.Work;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Storage;
using P = Portal.Domain.Authorization.Permissions;

namespace Portal.Infrastructure.Seeding;

/// <summary>
/// Development only: wipes the database and uploaded files, then fills every module with realistic, back-dated demo data
/// that tells one consistent story (docs/demo.md walks through it). Started with <c>dotnet run -- --reset-demo</c>.
/// Everything is written directly with explicit dates, so SLA states, timelines and the audit log look like weeks of real use.
/// </summary>
public sealed partial class DemoDataSeeder(
    AppDbContext db,
    DatabaseInitializer initializer,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IFileStorage storage,
    IOptions<StorageOptions> storageOptions,
    IOptions<SeedOptions> seedOptions,
    IHostEnvironment environment,
    ILogger<DemoDataSeeder> logger)
{
    /// <summary>Password of every demo account except the administrator (which keeps <c>Seed:AdminPassword</c>).</summary>
    public const string DemoPassword = "Demo@12345";

    private readonly DateTime _now = DateTime.UtcNow;
    private readonly List<AuditLog> _audit = [];
    private readonly Dictionary<string, ApplicationUser> _users = [];
    private readonly Dictionary<string, Customer> _customers = [];
    private readonly Dictionary<string, TicketCategory> _categories = [];
    private readonly Dictionary<string, EscalationRule> _rules = [];
    private Dictionary<TicketPriority, SlaPolicy> _policies = [];

    /// <summary>Drops the database and deletes uploaded files, then seeds everything from scratch.</summary>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        await db.Database.EnsureDeletedAsync(ct);
        ClearUploads();
        logger.LogWarning("Demo reset: database and uploaded files deleted");
        await SeedAsync(ct);
    }

    /// <summary>Seeds demo data into a database that has no customers yet (after <see cref="ResetAsync"/>, or in tests).</summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        db.DemoSeeding = true;
        try
        {
            await initializer.InitializeAsync(ct);
            if (await db.Customers.AnyAsync(ct))
                throw new InvalidOperationException("Demo data can only be added to an empty database. Start the API with --reset-demo.");

            await SeedRolesAsync(ct);
            await SeedUsersAsync(ct);
            await SeedCategoriesAsync(ct);
            await SeedSlaAsync(ct);
            await SeedCustomersAsync(ct);
            await SeedTicketsAsync(ct);
            await SeedWorkAsync(ct);
            await SeedSecurityEventsAsync(ct);

            db.AuditLogs.AddRange(_audit.OrderBy(a => a.OccurredAt));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Demo data seeded: {Users} users, {Customers} customers, {Tickets} tickets, {Audit} audit entries",
                _users.Count, _customers.Count, await db.Tickets.CountAsync(ct), _audit.Count);
        }
        finally
        {
            db.DemoSeeding = false;
        }
    }

    private void ClearUploads()
    {
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, storageOptions.Value.RootPath));
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root)) File.Delete(file);
    }

    private AppDbContext Db => db;

    private DateTime Ago(double days = 0, double hours = 0, double minutes = 0) =>
        _now - TimeSpan.FromDays(days) - TimeSpan.FromHours(hours) - TimeSpan.FromMinutes(minutes);

    // ---------- Roles and permissions ----------

    private static readonly string[] AgentPermissions =
    [
        P.Dashboard.View, P.Tickets.View, P.Tickets.Create, P.Tickets.Edit, P.Tickets.Work, P.Tickets.Escalate,
        P.Customers.View, P.Customers.Create, P.Customers.Edit, P.Customers.AddActivity,
    ];

    private static readonly string[] SupervisorPermissions =
    [
        .. AgentPermissions, P.Tickets.Assign, P.Tickets.Delete, P.Tickets.ManageCategories, P.Customers.Delete,
        P.QuickReplies.Manage, P.Sla.Manage, P.AuditLogs.View, P.Users.View, P.Roles.View,
    ];

    private static readonly string[] CustomerSuccessPermissions =
    [
        P.Customers.View, P.Customers.Create, P.Customers.Edit, P.Customers.Delete, P.Customers.AddActivity,
        P.Tickets.View, P.Tickets.Create,
    ];

    private static readonly string[] AuditorPermissions =
        [P.AuditLogs.View, P.Users.View, P.Roles.View, P.Tickets.View, P.Customers.View];

    private static readonly string[] TraineePermissions = [P.Tickets.View, P.Customers.View];

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        var admin = await userManager.FindByEmailAsync(seedOptions.Value.AdminEmail)
            ?? throw new InvalidOperationException("Demo data needs the administrator account; set Seed:AdminPassword.");
        admin.CreatedAt = Ago(60);
        admin.LastLoginAt = Ago(minutes: 90);
        _users["admin"] = admin;

        var administrator = await roleManager.FindByNameAsync(SystemRoles.Administrator);
        administrator!.CreatedAt = Ago(60);

        var agent = await roleManager.FindByNameAsync("Agent");
        agent!.CreatedAt = Ago(60);
        agent.Description = "Front-line support: works tickets and keeps customer records up to date.";
        await GrantAsync(agent, AgentPermissions, Ago(45), ct);

        await CreateRoleAsync("Supervisor", "Team lead: assigns work, manages SLA rules and quick replies, reviews the audit log.", SupervisorPermissions, Ago(45), ct);
        await CreateRoleAsync("Customer Success", "Owns customer relationships: manages customer records and logs requests.", CustomerSuccessPermissions, Ago(44), ct);
        await CreateRoleAsync("Auditor", "Read-only access for compliance reviews, including the audit log.", AuditorPermissions, Ago(30), ct);
        await CreateRoleAsync("Trainee", "New starters: can look at tickets and customers but not change anything.", TraineePermissions, Ago(7), ct);
    }

    private async Task CreateRoleAsync(string name, string description, string[] permissions, DateTime at, CancellationToken ct)
    {
        var role = new ApplicationRole { Name = name, Description = description, CreatedAt = at };
        var result = await roleManager.CreateAsync(role);
        if (!result.Succeeded) throw new InvalidOperationException($"Could not create role {name}.");
        Audit(at, _users["admin"], AuditAction.Created, "Role", role.Id.ToString(), $"Role {name} created",
            ("Name", null, name), ("Description", null, description));
        await GrantAsync(role, permissions, at.AddMinutes(2), ct);
    }

    private async Task GrantAsync(ApplicationRole role, string[] keys, DateTime at, CancellationToken ct)
    {
        var permissionIds = await db.Permissions.ToDictionaryAsync(p => p.Key, p => p.Id, ct);
        foreach (var key in keys)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permissionIds[key] });
            Audit(at, _users["admin"], AuditAction.Created, "RolePermission", $"{role.Id}:{permissionIds[key]}", $"Permission {key} granted to role {role.Name}");
        }
        await db.SaveChangesAsync(ct);
    }

    // ---------- Users ----------

    private sealed record DemoUser(string Key, string First, string Last, string Role, double JoinedDaysAgo, double? LastLoginHoursAgo, bool Active = true);

    private static readonly DemoUser[] DemoUsers =
    [
        new("sara", "Sara", "Khan", "Supervisor", 44, 2),
        new("omar", "Omar", "Haddad", "Agent", 40, 1),
        new("lina", "Lina", "Farouk", "Agent", 38, 3),
        new("youssef", "Youssef", "Nasser", "Agent", 35, 5),
        new("nadia", "Nadia", "Saleh", "Customer Success", 33, 20),
        new("karim", "Karim", "Aziz", "Auditor", 29, 26),
        new("rami", "Rami", "Toufic", "Trainee", 6, 30),
        new("hana", "Hana", "Ibrahim", "Agent", 37, 24 * 12, Active: false),
    ];

    private async Task SeedUsersAsync(CancellationToken ct)
    {
        var admin = _users["admin"];
        foreach (var u in DemoUsers)
        {
            var email = $"{u.First}.{u.Last}@portal.local".ToLowerInvariant();
            var joined = Ago(u.JoinedDaysAgo);
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = u.First,
                LastName = u.Last,
                IsActive = true,
                CreatedAt = joined,
                LastLoginAt = u.LastLoginHoursAgo is { } h ? Ago(hours: h) : null,
            };
            var result = await userManager.CreateAsync(user, DemoPassword);
            if (result.Succeeded) result = await userManager.AddToRoleAsync(user, u.Role);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Could not create demo user {email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            _users[u.Key] = user;

            Audit(joined, admin, AuditAction.Created, "User", user.Id.ToString(), $"User {email} created",
                ("Email", null, email), ("FirstName", null, u.First), ("LastName", null, u.Last), ("IsActive", null, "True"));
            Audit(joined, admin, AuditAction.Created, "UserRole", $"{user.Id}", $"User {email} assigned to role {u.Role}");

            if (!u.Active)
            {
                user.IsActive = false;
                user.UpdatedAt = Ago(10);
                Audit(Ago(10), admin, AuditAction.Updated, "User", user.Id.ToString(), $"User {email} updated", ("IsActive", "True", "False"));
            }
        }
        await db.SaveChangesAsync(ct);
    }

    // ---------- Categories and SLA ----------

    private async Task SeedCategoriesAsync(CancellationToken ct)
    {
        db.TicketCategories.AddRange(
            new TicketCategory { Name = "Delivery", Description = "Late, missing or wrong deliveries" },
            new TicketCategory { Name = "Legacy hardware", Description = "Devices we no longer sell; kept for old tickets", IsActive = false });
        await db.SaveChangesAsync(ct);
        foreach (var c in await db.TicketCategories.ToListAsync(ct)) _categories[c.Name] = c;

        Audit(Ago(40), _users["sara"], AuditAction.Created, "TicketCategory", null, "Ticket category Delivery created", ("Name", null, "Delivery"));
        Audit(Ago(39), _users["sara"], AuditAction.Updated, "TicketCategory", null, "Ticket category Legacy hardware updated", ("IsActive", "True", "False"));
    }

    private async Task SeedSlaAsync(CancellationToken ct)
    {
        _policies = await db.SlaPolicies.ToDictionaryAsync(p => p.Priority, ct);
        var admin = _users["admin"];
        Audit(Ago(42), admin, AuditAction.Updated, "SlaPolicy", null, "SLA targets for Urgent priority updated",
            ("FirstResponseMinutes", "60", "30"));

        // Auto-assignment stays off so the demo can switch it on and watch the next ticket go to the least busy agent.
        EscalationRule Rule(string name, SlaTrigger trigger, double daysAgo, int? threshold = null, TicketPriority? min = null,
            bool escalate = false, bool notifyAssignee = false, bool notifySupervisors = false, bool active = true)
        {
            var rule = new EscalationRule
            {
                Name = name, Trigger = trigger, ThresholdMinutes = threshold, MinPriority = min, Escalate = escalate,
                NotifyAssignee = notifyAssignee, NotifySupervisors = notifySupervisors, IsActive = active,
                CreatedAt = Ago(daysAgo), CreatedById = admin.Id,
            };
            db.EscalationRules.Add(rule);
            _rules[name] = rule;
            Audit(Ago(daysAgo), admin, AuditAction.Created, "EscalationRule", null, $"Escalation rule \"{name}\" created",
                ("Trigger", null, trigger.ToString()), ("IsActive", null, active.ToString()));
            return rule;
        }

        Rule("Urgent work not answered in time", SlaTrigger.FirstResponseBreached, 42, min: TicketPriority.High, escalate: true, notifySupervisors: true);
        Rule("Warn owner when resolution is at risk", SlaTrigger.ResolutionAtRisk, 42, notifyAssignee: true);
        Rule("Resolution missed", SlaTrigger.ResolutionBreached, 42, escalate: true, notifyAssignee: true, notifySupervisors: true);
        Rule("Unassigned for an hour", SlaTrigger.UnassignedFor, 41, threshold: 60, notifySupervisors: true);
        Rule("Weekend backlog sweep", SlaTrigger.UnassignedFor, 20, threshold: 24 * 60, notifySupervisors: true, active: false);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Customers ----------

    private sealed record Contact(string Name, string? JobTitle, string Email, string Phone, bool Primary = false);

    private sealed record Interaction(InteractionType Type, InteractionDirection Direction, string Subject, string Summary, double DaysAgo, string By);

    private sealed record DemoCustomer(
        string Key, CustomerType Type, string Name, string Email, string Phone, ContactChannel Channel, PreferredLanguage Language,
        string Address, string City, string Country, double CreatedDaysAgo, string CreatedBy,
        Contact[] Contacts, Interaction[] Interactions, string[] Notes, bool Active = true);

    private static readonly InteractionDirection In = InteractionDirection.Inbound;
    private static readonly InteractionDirection Out = InteractionDirection.Outbound;

    private static readonly DemoCustomer[] DemoCustomers =
    [
        new("alnoor", CustomerType.Company, "Al Noor Trading LLC", "support@alnoor-trading.example", "+971 4 555 0101", ContactChannel.Email, PreferredLanguage.English,
            "Office 1204, Bay Square", "Dubai", "United Arab Emirates", 40, "nadia",
            [new("Khalid Rahman", "Operations Manager", "khalid.rahman@alnoor-trading.example", "+971 50 555 0111", true),
             new("Mariam Yousef", "Finance Officer", "mariam.yousef@alnoor-trading.example", "+971 50 555 0112")],
            [new(InteractionType.Meeting, Out, "Quarterly service review", "Reviewed ticket volumes and renewal dates. Customer happy overall; wants faster billing answers.", 21, "nadia"),
             new(InteractionType.Email, In, "Double charge on invoice INV-2291", "Mariam reported the March invoice was charged twice on the company card.", 3, "omar"),
             new(InteractionType.Call, Out, "Called about the double charge", "Explained the refund process and the expected timeline (3–5 working days).", 2.9, "omar")],
            ["Key account. Renewal due in June; Khalid is the decision maker.", "Prefers email for anything financial, phone for urgent issues."]),
        new("nile", CustomerType.Company, "Nile Valley Pharmacies", "it@nilevalley-pharma.example", "+20 2 5555 0202", ContactChannel.Phone, PreferredLanguage.Arabic,
            "15 El Tahrir Street, Dokki", "Cairo", "Egypt", 36, "nadia",
            [new("Dr. Hesham Adel", "Managing Director", "hesham.adel@nilevalley-pharma.example", "+20 100 555 0221", true),
             new("Rana Samir", "IT Coordinator", "rana.samir@nilevalley-pharma.example", "+20 100 555 0222")],
            [new(InteractionType.Call, In, "POS stock sync failing", "Rana called: 12 branches can't sync stock levels since the morning.", 0.13, "omar"),
             new(InteractionType.Meeting, Out, "Onboarding of 3 new branches", "Agreed the go-live plan for the new Heliopolis, Maadi and Zayed branches.", 12, "nadia")],
            ["Chain of 12 pharmacies. Speak Arabic with Dr. Hesham.", "Business hours 9:00–23:00; outages after 20:00 are critical."]),
        new("gulfstar", CustomerType.Company, "Gulf Star Logistics", "service@gulfstar-logistics.example", "+966 11 555 0303", ContactChannel.Email, PreferredLanguage.English,
            "King Fahd Road, Al Olaya", "Riyadh", "Saudi Arabia", 34, "sara",
            [new("Faisal Al-Otaibi", "Head of Operations", "faisal.otaibi@gulfstar-logistics.example", "+966 55 555 0331", true),
             new("Priya Menon", "Dispatch Lead", "priya.menon@gulfstar-logistics.example", "+966 55 555 0332")],
            [new(InteractionType.Email, In, "Monthly report export times out", "Priya: the monthly delivery report export never finishes.", 3.8, "lina"),
             new(InteractionType.Chat, In, "Tracking ETA question", "Asked why the ETA on the tracking page is two hours off.", 0.2, "sara")],
            ["Heavy user of reports; exports run on the 1st of every month."]),
        new("blueharbor", CustomerType.Company, "Blue Harbor Hotels", "guestservices@blueharbor.example", "+968 24 555 0404", ContactChannel.Email, PreferredLanguage.English,
            "Shatti Al Qurum", "Muscat", "Oman", 30, "nadia",
            [new("Elena Rossi", "Guest Services Manager", "elena.rossi@blueharbor.example", "+968 9555 0441", true)],
            [new(InteractionType.Email, In, "Three new staff accounts", "Elena asked for accounts for three new receptionists.", 2, "omar"),
             new(InteractionType.Email, Out, "Authorisation form sent", "Sent the account authorisation form for signature.", 1.9, "omar")],
            ["Account changes need a signed authorisation form (company policy)."]),
        new("cedar", CustomerType.Company, "Cedar Tech Solutions", "support@cedartech.example", "+961 1 555 0505", ContactChannel.WhatsApp, PreferredLanguage.English,
            "Hamra Street, Bldg 22", "Beirut", "Lebanon", 28, "sara",
            [new("Joseph Haddad", "CTO", "joseph.haddad@cedartech.example", "+961 3 555 0551", true),
             new("Maya Khoury", "Integration Engineer", "maya.khoury@cedartech.example", "+961 3 555 0552")],
            [new(InteractionType.WhatsApp, In, "Bulk upload errors", "Maya shared screenshots of HTTP 500 errors on bulk upload.", 0.8, "youssef"),
             new(InteractionType.Call, Out, "SSO investigation call", "Walked through the SSO login loop with Joseph; collecting browser logs.", 0.05, "admin")],
            ["Technical contact is Maya; send API details to her directly."]),
        new("delta", CustomerType.Company, "Delta Foods Co.", "info@deltafoods.example", "+20 3 5555 0606", ContactChannel.Phone, PreferredLanguage.Arabic,
            "Industrial Zone, Borg El Arab", "Alexandria", "Egypt", 26, "nadia",
            [new("Mostafa Kamal", "Procurement Manager", "mostafa.kamal@deltafoods.example", "+20 122 555 0661", true)],
            [new(InteractionType.Call, In, "Complaint about delivery driver", "Mostafa complained that the driver was rude and left goods outside.", 1.25, "sara"),
             new(InteractionType.Call, Out, "Apology call", "Sara apologised and promised a written follow-up within 24 hours.", 1.1, "sara")],
            ["Sensitive account after the delivery complaint — handle with care."]),
        new("horizon", CustomerType.Company, "Horizon Academy", "admin@horizon-academy.example", "+962 6 555 0707", ContactChannel.Email, PreferredLanguage.English,
            "Abdoun, Street 7", "Amman", "Jordan", 22, "nadia",
            [new("Laila Mansour", "Administration Director", "laila.mansour@horizon-academy.example", "+962 79 555 0771", true)],
            [new(InteractionType.Email, In, "Invoice copies for the auditors", "Laila needs copies of last year's invoices for the school audit.", 0.25, "nadia")],
            ["Exam season in June: expect upload questions from teachers."]),
        new("sunrise", CustomerType.Company, "Sunrise Clinics", "operations@sunrise-clinics.example", "+974 4 555 0808", ContactChannel.Phone, PreferredLanguage.English,
            "West Bay, Tower 3", "Doha", "Qatar", 25, "sara",
            [new("Dr. Aisha Al-Thani", "Medical Director", "aisha.althani@sunrise-clinics.example", "+974 5555 0881", true)],
            [new(InteractionType.Call, In, "SMS reminders stopped", "Front desk says patients stopped receiving appointment reminders.", 0.03, "sara")],
            []),
        new("ahmed", CustomerType.Individual, "Ahmed Mostafa", "ahmed.mostafa@mail.example", "+20 111 555 0909", ContactChannel.WhatsApp, PreferredLanguage.Arabic,
            "12 Pyramids Road", "Giza", "Egypt", 18, "omar", [],
            [new(InteractionType.WhatsApp, In, "Can't log in", "Says the portal keeps rejecting his password.", 0.02, "omar")], []),
        new("sarah", CustomerType.Individual, "Sarah Johnson", "sarah.johnson@mail.example", "+44 20 5555 1010", ContactChannel.Email, PreferredLanguage.English,
            "221 Baker Street", "London", "United Kingdom", 16, "lina", [],
            [new(InteractionType.Email, In, "Refund request", "Cancelled her subscription and asked for the unused month back.", 2, "lina"),
             new(InteractionType.Email, Out, "Refund confirmed", "Confirmed the refund of 49 GBP.", 1, "lina")],
            ["Very responsive by email."]),
        new("fatima", CustomerType.Individual, "Fatima Al-Zahra", "fatima.alzahra@mail.example", "+971 55 555 1111", ContactChannel.Sms, PreferredLanguage.Arabic,
            "Al Majaz 2", "Sharjah", "United Arab Emirates", 15, "omar", [],
            [new(InteractionType.Sms, In, "New phone number", "Asked to update her phone number on the account.", 8, "omar")], []),
        new("daniel", CustomerType.Individual, "Daniel Kim", "daniel.kim@mail.example", "+971 52 555 1212", ContactChannel.Email, PreferredLanguage.English,
            "Marina Gate 2, Apt 1803", "Dubai", "United Arab Emirates", 14, "youssef", [],
            [new(InteractionType.Email, In, "Charged in the wrong currency", "Card was charged in USD instead of AED.", 5, "omar"),
             new(InteractionType.Chat, In, "Dark mode suggestion", "Suggested a dark mode for the mobile app.", 4, "youssef")],
            ["Power user; often sends product suggestions."]),
        new("youssefb", CustomerType.Individual, "Youssef Barakat", "youssef.barakat@mail.example", "+20 100 555 1313", ContactChannel.Phone, PreferredLanguage.Arabic,
            "30 Abbas El Akkad, Nasr City", "Cairo", "Egypt", 12, "sara", [],
            [new(InteractionType.Call, In, "Wrong item delivered", "Received a blender instead of the coffee machine he ordered.", 10, "sara")], []),
        new("oldtown", CustomerType.Company, "Old Town Bakery", "hello@oldtownbakery.example", "+20 2 5555 1414", ContactChannel.Email, PreferredLanguage.Arabic,
            "Al Muizz Street", "Cairo", "Egypt", 38, "nadia",
            [new("Samir Fawzy", "Owner", "samir.fawzy@oldtownbakery.example", "+20 109 555 1441", true)],
            [new(InteractionType.Email, In, "Closing the account", "The bakery closed its online shop and asked to stop the service.", 9, "nadia")],
            ["Account closed at the customer's request. Kept for history."], Active: false),
    ];

    private async Task SeedCustomersAsync(CancellationToken ct)
    {
        foreach (var c in DemoCustomers)
        {
            var creator = _users[c.CreatedBy];
            var created = Ago(c.CreatedDaysAgo);
            var customer = new Customer
            {
                Type = c.Type, Name = c.Name, Email = c.Email, NormalizedEmail = c.Email.ToUpperInvariant(), Phone = c.Phone,
                PreferredChannel = c.Channel, PreferredLanguage = c.Language, AddressLine = c.Address, City = c.City, Country = c.Country,
                IsActive = true, CreatedAt = created, CreatedById = creator.Id,
            };
            foreach (var contact in c.Contacts)
                customer.Contacts.Add(new CustomerContact
                {
                    Name = contact.Name, JobTitle = contact.JobTitle, Email = contact.Email, Phone = contact.Phone, IsPrimary = contact.Primary,
                    CreatedAt = created.AddMinutes(5), CreatedById = creator.Id,
                });
            foreach (var i in c.Interactions)
                customer.Interactions.Add(new CustomerInteraction
                {
                    Type = i.Type, Direction = i.Direction, Subject = i.Subject, Summary = i.Summary, OccurredAt = Ago(i.DaysAgo),
                    CreatedAt = Ago(i.DaysAgo).AddMinutes(10), CreatedById = _users[i.By].Id,
                });
            for (var n = 0; n < c.Notes.Length; n++)
                customer.Notes.Add(new CustomerNote { Content = c.Notes[n], CreatedAt = created.AddDays(n + 1), CreatedById = creator.Id });

            db.Customers.Add(customer);
            await db.SaveChangesAsync(ct); // one at a time so codes follow the list order
            _customers[c.Key] = customer;

            var code = Customer.FormatCode(customer.Id);
            Audit(created, creator, AuditAction.Created, "Customer", customer.Id.ToString(), $"Customer {c.Name} ({code}) created",
                ("Name", null, c.Name), ("Type", null, c.Type.ToString()), ("Email", null, c.Email), ("City", null, c.City));
            foreach (var contact in c.Contacts)
                Audit(created.AddMinutes(5), creator, AuditAction.Created, "CustomerContact", null, $"Contact {contact.Name} of {code} created");
            foreach (var i in c.Interactions)
                Audit(Ago(i.DaysAgo).AddMinutes(10), _users[i.By], AuditAction.Created, "CustomerInteraction", null, $"Interaction \"{i.Subject}\" on {code} created");

            if (!c.Active)
            {
                customer.IsActive = false;
                customer.UpdatedAt = Ago(9);
                customer.UpdatedById = creator.Id;
                Audit(Ago(9), creator, AuditAction.Updated, "Customer", customer.Id.ToString(), $"Customer {c.Name} ({code}) updated", ("IsActive", "True", "False"));
            }
        }

        await AttachAsync("alnoor", "Service agreement 2026 - summary.txt", "nadia", 20,
            "Service agreement summary — Al Noor Trading LLC\n\nTerm: 1 January 2026 – 31 December 2026\nSupport hours: Sun–Thu 08:00–20:00 (GST)\nResponse targets: Urgent 30 min, High 2 h, Medium 8 h, Low 1 day\nAccount manager: Nadia Saleh\n", ct);
        await AttachAsync("gulfstar", "Delivery zones.txt", "lina", 10,
            "Gulf Star Logistics — delivery zones\n\nZone A: Riyadh central (same day)\nZone B: Riyadh suburbs (next day)\nZone C: Eastern Province (2 days)\n", ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task AttachAsync(string customerKey, string fileName, string by, double daysAgo, string content, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var key = await storage.SaveAsync(new MemoryStream(bytes), ".txt", ct);
        var customer = _customers[customerKey];
        customer.Attachments.Add(new CustomerAttachment
        {
            FileName = fileName, ContentType = "text/plain", SizeBytes = bytes.Length, StorageKey = key,
            CreatedAt = Ago(daysAgo), CreatedById = _users[by].Id,
        });
        Audit(Ago(daysAgo), _users[by], AuditAction.Created, "CustomerAttachment", null, $"File {fileName} on {Customer.FormatCode(customer.Id)} created");
    }

    // ---------- Quick replies and tasks ----------

    private async Task SeedWorkAsync(CancellationToken ct)
    {
        var sara = _users["sara"];
        QuickReply Reply(string title, string body, ApplicationUser by, ApplicationUser? owner, double daysAgo) =>
            new() { Title = title, Body = body, OwnerId = owner?.Id, CreatedAt = Ago(daysAgo), CreatedById = by.Id };

        db.QuickReplies.AddRange(
            Reply("Escalation acknowledgement",
                "Hello {customer},\n\nI'm sorry for the trouble. {ticket} has been escalated to our senior team and is now our top priority. I'll update you within the hour.\n\nRegards,\n{agent}",
                sara, null, 30),
            Reply("Closing after no response",
                "Hello {customer},\n\nWe haven't heard back about {ticket} for a few days, so we'll close it for now. Just reply to this message and we'll pick it up again.\n\nBest regards,\n{agent}",
                sara, null, 28),
            Reply("Arabic greeting",
                "مرحباً {customer}،\n\nشكراً لتواصلك معنا. تم تسجيل طلبك برقم {ticket} وسنعود إليك في أقرب وقت.\n\nمع التحية،\n{agent}",
                _users["admin"], _users["admin"], 20),
            Reply("Callback scheduled",
                "Hi {customer}, I've scheduled a call back about {ticket} for tomorrow morning. Talk soon! — {agent}",
                _users["omar"], _users["omar"], 15));
        Audit(Ago(30), sara, AuditAction.Created, "QuickReply", null, "Shared quick reply \"Escalation acknowledgement\" created");
        Audit(Ago(28), sara, AuditAction.Created, "QuickReply", null, "Shared quick reply \"Closing after no response\" created");

        AgentTask Todo(string owner, string title, string? notes, DateTime? due, double createdDaysAgo, string? ticket = null,
            string? customer = null, DateTime? done = null) => new()
        {
            OwnerId = _users[owner].Id, Title = title, Notes = notes, DueAt = due, IsDone = done is not null, CompletedAt = done,
            TicketId = ticket is null ? null : _tickets[ticket].Id, CustomerId = customer is null ? null : _customers[customer].Id,
            CreatedAt = Ago(createdDaysAgo), CreatedById = _users[owner].Id,
        };

        db.AgentTasks.AddRange(
            Todo("admin", "Call Khalid Rahman about the double charge", "Confirm the refund reached the company card.", _now.AddHours(2), 1, "alnoor-invoice", "alnoor"),
            Todo("admin", "Review this week's SLA breaches with Sara", null, Ago(hours: 20), 3),
            Todo("admin", "Prepare the monthly support report", "Include SLA met % per priority.", _now.AddDays(3), 2),
            Todo("admin", "Send SSO logs to the identity vendor", null, Ago(hours: 30), 2, "cedar-sso", done: Ago(hours: 26)),
            Todo("sara", "Written follow-up to Delta Foods", "Promised within 24 hours of the apology call.", Ago(hours: 2), 1, "delta-complaint", "delta"),
            Todo("sara", "1:1 with Omar", null, _now.AddDays(1), 2),
            Todo("omar", "Chase Blue Harbor for the signed form", null, _now.AddHours(1), 1, "blueharbor-accounts", "blueharbor"),
            Todo("lina", "Follow up with the POS vendor", "Ticket with vendor: VND-8812.", _now.AddMinutes(30), 0.1, "nile-pos", "nile"),
            Todo("youssef", "Check if dark mode is on the roadmap", null, _now.AddDays(2), 3, "daniel-darkmode", "daniel"));
        await db.SaveChangesAsync(ct);
    }

    // ---------- Audit log ----------

    private void Audit(DateTime at, ApplicationUser? user, AuditAction action, string entityType, string? entityId, string summary,
        params (string Field, string? From, string? To)[] changes)
    {
        _audit.Add(new AuditLog
        {
            OccurredAt = at,
            UserId = user?.Id,
            UserName = user?.FullName ?? "System",
            IpAddress = user is null ? null : IpOf(user),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            Changes = changes.Length == 0 ? null : JsonSerializer.Serialize(changes.Select(c => new AuditChange(c.Field, c.From, c.To))),
        });
    }

    /// <summary>Each person works from their own office desk.</summary>
    private string IpOf(ApplicationUser user) =>
        $"10.20.1.{11 + _users.Values.ToList().FindIndex(u => u.Id == user.Id)}";

    private Task SeedSecurityEventsAsync(CancellationToken ct)
    {
        void Security(DateTime at, AuditAction action, string summary, ApplicationUser? user, string? userName = null, string? ip = null)
        {
            _audit.Add(new AuditLog
            {
                OccurredAt = at, UserId = user?.Id, UserName = user?.FullName ?? userName, IpAddress = ip ?? (user is null ? null : IpOf(user)),
                Action = action, EntityType = "Security", EntityId = user?.Id.ToString() ?? userName, Summary = summary,
            });
        }

        // Daily sign-ins for the active team over the last week.
        foreach (var (key, user) in _users.Where(u => u.Value.IsActive))
        {
            for (var day = 6; day >= 1; day--)
                Security(Ago(day, hours: 1 + key.Length % 3), AuditAction.SignedIn, $"{user.Email} signed in", user);
            if (user.LastLoginAt is { } last) Security(last, AuditAction.SignedIn, $"{user.Email} signed in", user);
        }

        var youssef = _users["youssef"];
        Security(Ago(2, hours: 3, minutes: 4), AuditAction.SignInFailed, $"Failed sign-in for {youssef.Email} (wrong password)", youssef);
        Security(Ago(2, hours: 3, minutes: 3), AuditAction.SignInFailed, $"Failed sign-in for {youssef.Email} (wrong password)", youssef);

        var karim = _users["karim"];
        for (var i = 0; i < 4; i++)
            Security(Ago(6, hours: 2, minutes: 10 - i), AuditAction.SignInFailed, $"Failed sign-in for {karim.Email} (wrong password)", karim);
        Security(Ago(6, hours: 2, minutes: 6), AuditAction.LockedOut, $"{karim.Email} locked out after repeated failed sign-ins", karim);
        Security(Ago(6, hours: 2, minutes: 4), AuditAction.SignInFailed, $"Sign-in blocked for {karim.Email}: account is locked", karim);

        const string outsider = "203.0.113.45";
        foreach (var guess in new[] { "admin@portal.com", "administrator@portal.local", "root@portal.local" })
            Security(Ago(4, hours: 22), AuditAction.SignInFailed, $"Failed sign-in for {guess} (no such account)", null, guess, outsider);

        var hana = _users["hana"];
        Security(Ago(1, hours: 4), AuditAction.SignInFailed, $"Sign-in refused for {hana.Email}: account disabled", hana);

        var lina = _users["lina"];
        Security(Ago(9), AuditAction.PasswordChanged, $"{lina.Email} changed their password", lina);
        Security(Ago(1, hours: 9), AuditAction.SignedOut, $"{lina.Email} signed out", lina);
        Security(Ago(hours: 20), AuditAction.SignedOut, $"{_users["nadia"].Email} signed out", _users["nadia"]);
        return Task.CompletedTask;
    }
}
