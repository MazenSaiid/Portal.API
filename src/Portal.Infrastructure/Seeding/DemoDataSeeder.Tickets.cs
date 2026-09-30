using Microsoft.EntityFrameworkCore;
using Portal.Domain.Entities;
using Portal.Domain.Entities.Auditing;
using Portal.Domain.Entities.Sla;
using Portal.Domain.Entities.Tickets;

namespace Portal.Infrastructure.Seeding;

public sealed partial class DemoDataSeeder
{
    private readonly Dictionary<string, Ticket> _tickets = [];

    /// <summary>
    /// Each ticket is played forward step by step with explicit times, writing the same history lines, notifications and
    /// rule executions the application would have written. SLA due dates come from the real targets, so "at risk" and
    /// "breached" are genuine. Rules that match today are recorded as already run, except on the tickets meant to show the
    /// SLA monitor at work (see docs/demo.md).
    /// </summary>
    private async Task SeedTicketsAsync(CancellationToken ct)
    {
        var (admin, sara, omar, lina, youssef, nadia) =
            (_users["admin"], _users["sara"], _users["omar"], _users["lina"], _users["youssef"], _users["nadia"]);
        var resolutionMissed = _rules["Resolution missed"];
        var atRisk = _rules["Warn owner when resolution is at risk"];
        var unassigned = _rules["Unassigned for an hour"];

        // ----- Closed and resolved history (oldest first) -----
        await Script("sunrise-export", "sunrise", "Data export for the annual audit", "Please send a full export of our patient appointment statistics for 2025 (no personal data).",
                "General", TicketPriority.Medium, TicketChannel.Email, Ago(14), sara)
            .Assign(sara, sara, 5)
            .Status(TicketStatus.InProgress, sara, 30, "Running the anonymised export.")
            .Comment(sara, 60 * 20, "Export shared through the secure link; waiting for Dr. Aisha to confirm.")
            .Status(TicketStatus.Resolved, sara, 60 * 26, "Customer confirmed the file is complete.")
            .Status(TicketStatus.Closed, sara, 60 * 24 * 3)
            .SaveAsync(ct);

        await Script("alnoor-billing-contact", "alnoor", "Change billing contact to Mariam Yousef", "From now on invoices should go to Mariam Yousef in Finance.",
                "Account", TicketPriority.Low, TicketChannel.Email, Ago(12), nadia)
            .Assign(omar, nadia, 10)
            .Comment(omar, 90, "Billing contact updated; next invoice will go to Mariam.")
            .Status(TicketStatus.Resolved, omar, 95)
            .Status(TicketStatus.Closed, omar, 60 * 24 * 2)
            .SaveAsync(ct);

        await Script("youssefb-wrong-item", "youssefb", "Wrong item delivered", "Ordered a coffee machine (order 55120) but received a blender.",
                "Delivery", TicketPriority.High, TicketChannel.Phone, Ago(10), sara)
            .Assign(sara, sara, 3)
            .Status(TicketStatus.InProgress, sara, 45, "Courier booked to collect the blender.")
            .Status(TicketStatus.OnHold, sara, 60 * 5, "Waiting for the warehouse to ship the replacement.")
            .Status(TicketStatus.Resolved, sara, 60 * 30, "Coffee machine delivered; blender collected.") // after the 24 h target: resolution breached
            .Status(TicketStatus.Closed, sara, 60 * 24 * 3)
            .SaveAsync(ct);

        await Script("fatima-phone", "fatima", "Update phone number on account", "Please change my phone number to +971 55 555 1111.",
                "Account", TicketPriority.Low, TicketChannel.Sms, Ago(8), omar)
            .Assign(omar, omar, 0)
            .Comment(omar, 60, "Verified identity by SMS code and updated the number.")
            .Status(TicketStatus.Resolved, omar, 120)
            .Status(TicketStatus.Closed, omar, 60 * 48)
            .SaveAsync(ct);

        await Script("blueharbor-printer", "blueharbor", "Wi-Fi voucher printer offline", "The lobby printer that prints guest Wi-Fi vouchers shows 'offline'.",
                "Technical", TicketPriority.Medium, TicketChannel.Phone, Ago(7), omar)
            .Assign(youssef, omar, 15)
            .Status(TicketStatus.InProgress, youssef, 40, "Remote session booked with the hotel IT desk.")
            .Status(TicketStatus.Resolved, youssef, 60 * 12, "Printer driver reinstalled; test voucher printed.")
            .Status(TicketStatus.Open, youssef, 60 * 24, "Reopened: the printer went offline again overnight.")
            .Status(TicketStatus.InProgress, youssef, 60 * 25)
            .Comment(youssef, 60 * 40, "Root cause: the printer's IP address changed after a router restart. Fixed IP reserved.")
            .Status(TicketStatus.Resolved, youssef, 60 * 48, "Stable for 24 hours after the fix.")
            .SaveAsync(ct);

        await Script("daniel-currency", "daniel", "Charged in the wrong currency", "My card was charged 120 USD instead of 440 AED.",
                "Billing", TicketPriority.Medium, TicketChannel.Email, Ago(5), omar)
            .Assign(omar, omar, 0)
            .Comment(omar, 120, "Confirmed with finance: currency setting on the account was wrong. Refunding the difference.")
            .Status(TicketStatus.Resolved, omar, 60 * 24, "Currency fixed and difference refunded.")
            .SaveAsync(ct);

        await Script("sarah-refund", "sarah", "Refund for cancelled subscription", "I cancelled my subscription on the 2nd and would like the unused month refunded.",
                "Billing", TicketPriority.Medium, TicketChannel.Email, Ago(2), lina)
            .Assign(lina, lina, 0)
            .Comment(lina, 45, "Refund of 49 GBP approved by finance.")
            .Status(TicketStatus.Resolved, lina, 60 * 24, "Refund confirmed by email.")
            .SaveAsync(ct);

        // ----- Active work -----
        await Script("alnoor-invoice", "alnoor", "Invoice INV-2291 charged twice", "The March invoice INV-2291 (4,800 AED) was charged twice on the company card.",
                "Billing", TicketPriority.High, TicketChannel.Email, Ago(3), nadia)
            .Assign(omar, nadia, 20)
            .Comment(omar, 60, "Payment gateway shows two captures 40 seconds apart. Refund of the duplicate requested.")
            .Status(TicketStatus.InProgress, omar, 65)
            .Status(TicketStatus.OnHold, omar, 60 * 6, "Waiting for the bank to confirm the refund reference.")
            .RuleFired(resolutionMissed) // 24 h target passed while waiting on the bank
            .Status(TicketStatus.InProgress, omar, 60 * 30, "Bank asked for the card statement; requested it from Mariam.")
            .SaveAsync(ct);

        await Script("gulfstar-report", "gulfstar", "Monthly report export times out", "The monthly delivery report export spins for 10 minutes and then fails.",
                "Technical", TicketPriority.Medium, TicketChannel.Email, Ago(3, hours: 20), lina)
            .Assign(lina, lina, 0)
            .Status(TicketStatus.InProgress, lina, 90, "Reproduced with the March data set (1.2 M rows).")
            .Comment(lina, 60 * 26, "Export query needs an index; fix scheduled for the next release.")
            .RuleFired(resolutionMissed) // escalation also raises Medium to High
            .SaveAsync(ct);

        await Script("daniel-darkmode", "daniel", "Feature request: dark mode in the mobile app", "Would love a dark mode — the app is very bright at night.",
                "General", TicketPriority.Low, TicketChannel.WebForm, Ago(4), youssef)
            .Assign(youssef, youssef, 0)
            .Comment(youssef, 30, "Thanks Daniel! Logged with the product team as FR-311.")
            .Status(TicketStatus.OnHold, youssef, 35, "Waiting for the product team's roadmap decision.")
            .RuleFired(atRisk)
            .SaveAsync(ct);

        await Script("blueharbor-accounts", "blueharbor", "Add three new receptionist accounts", "Please create accounts for Amira, Joel and Sanjay who start next week.",
                "Account", TicketPriority.Low, TicketChannel.Email, Ago(2), omar)
            .Assign(omar, omar, 0)
            .Comment(omar, 60, "Sent the authorisation form to Elena.")
            .Status(TicketStatus.OnHold, omar, 65, "Waiting for the signed authorisation form.")
            .SaveAsync(ct);

        await Script("delta-complaint", "delta", "Complaint: delivery driver was rude", "Our procurement manager says the driver was rude and left the goods outside in the heat.",
                "Complaint", TicketPriority.High, TicketChannel.Phone, Ago(1, hours: 6), sara)
            .Assign(sara, sara, 2)
            .Comment(sara, 20, "Called Mostafa and apologised. Asked the courier company for the driver's report.")
            .Escalate(sara, 25, "Customer is considering ending the contract; needs management attention.")
            .Status(TicketStatus.InProgress, sara, 30)
            .RuleFired(resolutionMissed)
            .SaveAsync(ct);

        await Script("nile-branch", "nile", "Add the new Zayed branch to the contract", "We open a new branch in Sheikh Zayed next month; please add it to our contract.",
                "General", TicketPriority.Medium, TicketChannel.Email, Ago(1), nadia)
            .Assign(lina, sara, 30)
            .Comment(lina, 80, "Sent the updated contract annex for signature.")
            .SaveAsync(ct);

        await Script("cedar-bulk", "cedar", "API returns 500 on bulk upload", "Bulk upload of more than 500 records fails with HTTP 500.",
                "Technical", TicketPriority.High, TicketChannel.LiveChat, Ago(hours: 20), youssef)
            .Assign(youssef, youssef, 0)
            .Status(TicketStatus.InProgress, youssef, 25, "Reproduced with 600 records; request times out at 30 s.")
            .Comment(youssef, 60 * 6, "Suggested batches of 250 as a workaround while the limit is raised.")
            .RuleFired(atRisk)
            .SaveAsync(ct);

        await Script("sarah-password", "sarah", "Password reset email not arriving", "I requested a password reset three times but no email arrives.",
                "Account", TicketPriority.High, TicketChannel.Email, Ago(hours: 10), youssef)
            .Assign(youssef, youssef, 0)
            .Comment(youssef, 40, "Our mail log shows the emails bounced — her mailbox was full. Asked her to free up space.")
            .Status(TicketStatus.InProgress, youssef, 45)
            .SaveAsync(ct);

        await Script("gulfstar-eta", "gulfstar", "Tracking page shows the wrong delivery ETA", "ETAs on the public tracking page are two hours later than the real arrival time.",
                "Delivery", TicketPriority.Medium, TicketChannel.WebForm, Ago(hours: 5), sara)
            .Assign(sara, sara, 5)
            .Status(TicketStatus.InProgress, sara, 20, "Looks like a time-zone issue (UTC vs. Riyadh time).")
            .SaveAsync(ct);

        await Script("nile-pos", "nile", "Pharmacy POS cannot sync stock levels", "Since this morning none of our 12 branches can sync stock levels with the head office.",
                "Technical", TicketPriority.Urgent, TicketChannel.Phone, Ago(hours: 3, minutes: 10), omar)
            .Assign(lina, omar, 5)
            .Status(TicketStatus.InProgress, lina, 10, "Sync service is rejecting the branches' certificates.")
            .Escalate(lina, 40, "All 12 branches affected; the certificate vendor has to be involved.")
            .Comment(lina, 70, "Vendor ticket VND-8812 opened; they are issuing new certificates.")
            .RuleFired(atRisk) // resolution is missed about 50 minutes after seeding; the monitor then notifies again
            .SaveAsync(ct);

        await Script("delta-terms", "delta", "Question about payment terms", "Can we move from 30-day to 45-day payment terms?",
                "Billing", TicketPriority.Low, TicketChannel.Phone, Ago(hours: 3), nadia)
            .Assign(admin, nadia, 10)
            .Comment(admin, 50, "Checking with finance whether 45 days is possible for this contract size.")
            .SaveAsync(ct);

        await Script("cedar-sso", "cedar", "SSO login loop for some users", "About 20 users are sent back to the login page after signing in with SSO.",
                "Technical", TicketPriority.High, TicketChannel.Email, Ago(hours: 1, minutes: 30), sara)
            .Assign(admin, sara, 10)
            .Comment(admin, 15, "Affected users all use Safari; asking Maya for browser logs.")
            .Status(TicketStatus.InProgress, admin, 20)
            .SaveAsync(ct);

        // ----- The queue: nobody owns these yet -----
        await Script("horizon-invoices", "horizon", "Copies of last year's invoices", "Our auditors need copies of all 2025 invoices by the end of the week.",
                "Billing", TicketPriority.Low, TicketChannel.Email, Ago(hours: 6), nadia)
            .RuleFired(unassigned)
            .SaveAsync(ct);

        // Left for the SLA monitor: about a minute after the API starts it escalates this one ("Urgent work not answered in
        // time"), and some minutes later "Unassigned for an hour" alerts the supervisors.
        await Script("sunrise-sms", "sunrise", "Appointment reminders not sending SMS", "Since this morning patients no longer receive SMS reminders for their appointments.",
                "Technical", TicketPriority.Urgent, TicketChannel.Phone, Ago(minutes: 50), sara)
            .SaveAsync(ct);

        await Script("horizon-upload", "horizon", "Teachers can't upload exam files", "Uploads bigger than 20 MB fail for all teachers. Exams start on Sunday.",
                "Technical", TicketPriority.High, TicketChannel.LiveChat, Ago(minutes: 30), nadia)
            .SaveAsync(ct);

        await Script("ahmed-login", "ahmed", "Can't log in to the customer portal", "The portal keeps saying my password is wrong even after a reset.",
                "Account", TicketPriority.Medium, TicketChannel.WhatsApp, Ago(minutes: 25), omar)
            .SaveAsync(ct);
    }

    private TicketScript Script(string key, string customer, string subject, string description, string category,
        TicketPriority priority, TicketChannel channel, DateTime createdAt, ApplicationUser createdBy) =>
        new(this, key, new Ticket
        {
            CustomerId = _customers[customer].Id,
            Subject = subject,
            Description = description,
            CategoryId = _categories[category].Id,
            Priority = priority,
            Channel = channel,
            Status = TicketStatus.New,
            CreatedAt = createdAt,
            CreatedById = createdBy.Id,
            LastActivityAt = createdAt,
        }, createdBy);

    /// <summary>Plays one ticket forward. Steps take minutes after creation and must be called in time order.</summary>
    private sealed class TicketScript
    {
        private readonly DemoDataSeeder _s;
        private readonly string _key;
        private readonly Ticket _t;
        private readonly List<(Guid User, NotificationType Type, Func<string, string> Title, string Message, DateTime At)> _notices = [];
        private readonly List<(EscalationRule Rule, DateTime At)> _executions = [];
        private readonly List<(DateTime At, ApplicationUser? By, string Field, string? From, string? To)> _changes = [];
        private DateTime _clock;

        public TicketScript(DemoDataSeeder seeder, string key, Ticket ticket, ApplicationUser createdBy)
        {
            (_s, _key, _t, _clock) = (seeder, key, ticket, ticket.CreatedAt);
            Add(TicketEventType.Created, createdBy, _t.CreatedAt, to: nameof(TicketStatus.New));
        }

        private DateTime At(double minutes) => _clock = _t.CreatedAt.AddMinutes(minutes);

        private void Add(TicketEventType type, ApplicationUser? by, DateTime at, string? from = null, string? to = null, string? message = null) =>
            _t.History.Add(new TicketHistoryEntry { Type = type, FromValue = from, ToValue = to, Message = message, CreatedAt = at, CreatedById = by?.Id });

        private void Touch(DateTime at, ApplicationUser? by)
        {
            _t.LastActivityAt = at;
            _t.UpdatedAt = at;
            _t.UpdatedById = by?.Id;
        }

        public TicketScript Assign(ApplicationUser to, ApplicationUser by, double minutes)
        {
            var at = At(minutes);
            var from = _t.Assignee?.FullName;
            _t.Assignee = to;
            _t.AssigneeId = to.Id;
            Add(TicketEventType.Assigned, by, at, from, to.FullName);
            _changes.Add((at, by, "AssigneeId", from, to.FullName));
            if (to.Id != by.Id)
                _notices.Add((to.Id, NotificationType.TicketAssigned, code => $"{code} assigned to you", $"\"{_t.Subject}\" ({_t.Priority} priority).", at));
            if (_t.Status == TicketStatus.New) // W4
            {
                Add(TicketEventType.StatusChanged, by, at, nameof(TicketStatus.New), nameof(TicketStatus.Open), "Opened on assignment.");
                _t.Status = TicketStatus.Open;
            }
            Touch(at, by);
            return this;
        }

        public TicketScript Status(TicketStatus to, ApplicationUser by, double minutes, string? comment = null)
        {
            var at = At(minutes);
            var from = _t.Status;
            _t.Status = to;
            switch (to)
            {
                case TicketStatus.Resolved: _t.ResolvedAt = at; break;
                case TicketStatus.Closed: _t.ClosedAt = at; break;
                default: _t.ResolvedAt = null; _t.ClosedAt = null; break;
            }
            Add(TicketEventType.StatusChanged, by, at, from.ToString(), to.ToString(), comment);
            _changes.Add((at, by, "Status", from.ToString(), to.ToString()));
            if (_t.IsEscalated && to is TicketStatus.Resolved or TicketStatus.Closed)
            {
                _t.IsEscalated = false;
                Add(TicketEventType.DeEscalated, by, at, message: $"Cleared automatically when the ticket was {to.ToString().ToLowerInvariant()}.");
            }
            _t.FirstRespondedAt ??= at; // Spec 007, S3
            Touch(at, by);
            return this;
        }

        public TicketScript Comment(ApplicationUser by, double minutes, string text)
        {
            var at = At(minutes);
            Add(TicketEventType.Comment, by, at, message: text);
            _t.FirstRespondedAt ??= at;
            _t.LastActivityAt = at;
            return this;
        }

        public TicketScript Escalate(ApplicationUser by, double minutes, string reason)
        {
            var at = At(minutes);
            EscalateAt(at, by, reason);
            if (_t.AssigneeId is { } owner && owner != by.Id)
                _notices.Add((owner, NotificationType.TicketEscalated, code => $"{code} was escalated", reason, at));
            return this;
        }

        private void EscalateAt(DateTime at, ApplicationUser? by, string reason)
        {
            _t.IsEscalated = true;
            _t.EscalatedAt = at;
            _t.EscalationReason = reason;
            Add(TicketEventType.Escalated, by, at, message: reason);
            _changes.Add((at, by, "IsEscalated", "False", "True"));
            if (_t.Priority < TicketPriority.High) // Spec 004, E1
            {
                Add(TicketEventType.PriorityChanged, by, at, _t.Priority.ToString(), nameof(TicketPriority.High), "Raised by escalation.");
                _changes.Add((at, by, "Priority", _t.Priority.ToString(), nameof(TicketPriority.High)));
                _t.Priority = TicketPriority.High;
            }
            Touch(at, by);
        }

        /// <summary>Records what the SLA engine did when <paramref name="rule"/> first matched (a minute after its trigger time).</summary>
        public TicketScript RuleFired(EscalationRule rule)
        {
            var dates = SlaCalculator.For(_t.CreatedAt, _s._policies[_t.Priority]);
            var at = (rule.Trigger switch
            {
                SlaTrigger.FirstResponseBreached => dates.FirstResponseDueAt,
                SlaTrigger.ResolutionAtRisk => dates.ResolutionAtRiskAt,
                SlaTrigger.ResolutionBreached => dates.ResolutionDueAt,
                _ => _t.CreatedAt.AddMinutes(rule.ThresholdMinutes ?? 0),
            }).AddMinutes(1);
            if (at < _clock || at > _s._now) throw new InvalidOperationException($"Demo script for {_key}: rule \"{rule.Name}\" can't fire at {at:u}.");
            _clock = at;

            var why = rule.Trigger switch
            {
                SlaTrigger.FirstResponseBreached => "first response target missed",
                SlaTrigger.ResolutionBreached => "resolution target missed",
                SlaTrigger.ResolutionAtRisk => "resolution target at risk",
                _ => $"unassigned for over {rule.ThresholdMinutes} minutes",
            };
            if (rule.Escalate && !_t.IsEscalated) EscalateAt(at, null, $"Automatic — {rule.Name}: {why}.");

            var message = $"\"{_t.Subject}\" — rule \"{rule.Name}\".";
            if (rule.NotifyAssignee && _t.AssigneeId is { } assignee)
                _notices.Add((assignee, NotificationType.SlaAlert, code => $"{code}: {why}", message, at));
            if (rule.NotifySupervisors)
                foreach (var supervisor in new[] { _s._users["admin"], _s._users["sara"] })
                    if (!(rule.NotifyAssignee && supervisor.Id == _t.AssigneeId))
                        _notices.Add((supervisor.Id, NotificationType.SlaAlert, code => $"{code}: {why}", message, at));

            _executions.Add((rule, at));
            _t.LastActivityAt = at;
            return this;
        }

        public async Task SaveAsync(CancellationToken ct)
        {
            var dates = SlaCalculator.For(_t.CreatedAt, _s._policies[_t.Priority]); // S4 — from creation, with the final priority
            (_t.FirstResponseDueAt, _t.ResolutionDueAt, _t.ResolutionAtRiskAt) = (dates.FirstResponseDueAt, dates.ResolutionDueAt, dates.ResolutionAtRiskAt);

            _s.Db.Tickets.Add(_t);
            await _s.Db.SaveChangesAsync(ct); // one at a time so ticket codes follow the script order
            _s._tickets[_key] = _t;
            var code = Ticket.FormatCode(_t.Id);

            foreach (var (user, type, title, message, at) in _notices)
                _s.Db.Notifications.Add(new Notification
                {
                    UserId = user, Type = type, Title = title(code), Message = message, TicketId = _t.Id, CreatedAt = at,
                    IsRead = at < _s.Ago(hours: 12),
                });
            foreach (var (rule, at) in _executions)
                _s.Db.EscalationRuleExecutions.Add(new EscalationRuleExecution { RuleId = rule.Id, TicketId = _t.Id, ExecutedAt = at });
            await _s.Db.SaveChangesAsync(ct);

            var creator = _s._users.Values.First(u => u.Id == _t.CreatedById);
            _s.Audit(_t.CreatedAt, creator, AuditAction.Created, "Ticket", _t.Id.ToString(), $"Ticket {code} \"{_t.Subject}\" created",
                ("Subject", null, _t.Subject), ("Priority", null, PriorityAtCreation()), ("Channel", null, _t.Channel.ToString()));
            foreach (var (at, by, field, from, to) in _changes)
                _s.Audit(at, by, AuditAction.Updated, "Ticket", _t.Id.ToString(), $"Ticket {code} \"{_t.Subject}\" updated", (field, from, to));
        }

        /// <summary>The priority before any escalation raised it.</summary>
        private string PriorityAtCreation() =>
            _t.History.FirstOrDefault(h => h.Type == TicketEventType.PriorityChanged)?.FromValue ?? _t.Priority.ToString();
    }
}
