# Telegram AI Project Manager

A .NET 10 / ASP.NET Core modular monolith for an authorized Telegram team. PostgreSQL stores project records; Telegram.Bot long polling receives updates. A configurable OpenAI-compatible HTTPS chat-completions provider extracts **proposals**, never approved commitments. No public webhook is needed.

TradeFlow AI starts with the goals **20 discovery interviews → 2 paid pilots → 5 recurring customers**. Tasks can cover discovery, sales, engineering and operations. Reports distinguish actual task changes from unconfirmed AI observations; milestone achievement counts are not automatically inferred from chat.

## Run locally

Requirements: the SDK in `global.json`, PostgreSQL 17, and Docker with Compose (or an existing disposable PostgreSQL database).

1. Copy `.env.example` to `.env`. Generate a local database password and set it in both `POSTGRES_PASSWORD` and `ConnectionStrings__Database`. Keep Telegram/AI disabled for development without external credentials.
2. `docker compose up -d db`
3. `dotnet restore --locked-mode && dotnet tool restore`
4. Load the `.env` variables into your process using your IDE or a dotenv tool. `dotnet run` does **not** automatically load this file. For Bash, quote values containing spaces/semicolons in `.env`, then use `set -a; source .env; set +a`. Do not echo environment values.
5. `dotnet run --project src/TelegramProjectManager -- --migrate-only`
6. `dotnet run --project src/TelegramProjectManager --urls http://127.0.0.1:5088`
7. Make local requests to `/health/live` and `/health/ready` on that port. Readiness checks connectivity **and applied migrations**, not Telegram or AI authentication.

Alternatively, `docker compose --profile app up --build` runs the app and database using `.env`; Compose overrides the database hostname and applies migrations for local development. Production uses a separate migration step. The image runs as a non-root user.

In the managed Codex Linux x64 environment, run `bash scripts/cloud-install.sh`, then `source /workspace/.setup/env.sh`. The script uses workspace-local SDK/package caches, creates a loopback-only development PostgreSQL container, generates local credentials once outside the repository, applies migrations and runs the complete test suite. Each task already has an isolated checkout; use it directly, without creating worktrees. Live processes must be restarted after snapshot restoration.

## Telegram setup and consent

1. Create a bot with Telegram's verified **@BotFather**; store its token in a secret environment setting, never source control, chat or shell history. Set `Telegram__Enabled=true` and `Telegram__Token`.
2. Configure each authorized **negative** group chat ID, its manager's numeric user ID and IANA time zone, e.g. `Telegram__Groups__0__ChatId`, `Telegram__Groups__0__ManagerUserIds__0`, `Telegram__Groups__0__TimeZone`. Add index 1, 2, etc. for other groups/managers. Names/usernames are not authorization identifiers. Obtain IDs using Telegram's official API/tools you trust; do not publish token-bearing API URLs. Unauthorized groups/private chats receive no replies and no conversation is stored.
3. Explain the purpose, exact AI provider and processing jurisdiction, data transmitted, retention, access and deletion policy to **every participant**. Obtain informed agreement before giving the bot access to ordinary discussions. If necessary use @BotFather `/setprivacy` → select bot → Disable, then remove/re-add the bot. Group admins also see messages regardless of privacy mode; do not make it admin unnecessarily. Only text from explicitly opted-in users is retained or analyzed.
4. In the configured group, a configured manager sends `/start`; each participating human, including managers, sends `/join`. The bot is quiet for nonparticipants. `/leave` withdraws consent, deletes that user's retained raw messages and the group's stored analysis batches, and stops future collection. Accepted tasks, proposals, decisions, audit records, and messages already posted to Telegram are project records; leaving does not erase those or a provider's retained data. Agree an administrator-assisted deletion policy before use.
5. Ensure no webhook is configured for this token. Stop other pollers. Use Telegram's official `deleteWebhook` operation if migrating from a webhook, without dropping pending updates unless intentional. Run **one bot token per database** and one worker replica.

Anonymous admin/sender-chat posts, forwarded messages, bot messages, media/captions and edits are not collected. Old conversation before `/join` is not imported. Telegram retains pending updates only for a limited period, so polling downtime can lose discussion.

## Commands and task ownership

| Command | Behavior |
| --- | --- |
| `/start`, `/help` | Setup/commands in authorized groups; only a manager can create a project's initial record |
| `/join`, `/leave` | Opt in / withdraw from collection and AI analysis |
| `/tasks`, `/today` | Up to 25 active tasks; today's list includes accepted work due today/overdue or unscheduled |
| `/task NUMBER` | Details, source message ID, missing information and inline action buttons |
| `/analyze` | Manager requests bounded analysis of recent opted-in conversation; rate/cooldown limited |
| `/status`, `/report` | Project summary / weekly-style report with recent activity |
| `/done NUMBER`, `/doing NUMBER` | Owner completes or starts/resumes work |
| `/blocked NUMBER reason` | Owner reports a blocker and marks the task blocked |
| `/cancel NUMBER` | Owner or configured manager cancels accepted work |
| `/due NUMBER ISO_TIMESTAMP` | Confirmed owner sets a future timestamp with `Z` or an explicit offset |
| `/blockers`, `/resolve ID` | Show unresolved blockers; manager resolves an observation by its displayed UUID |

Lifecycle: **Proposed → Confirmed → InProgress ↔ Blocked → Completed / Cancelled**. Accepted work can also complete directly from Confirmed/Blocked. Terminal tasks cannot reopen. A nominated owner must accept personally. Any opted-in member may claim an unassigned proposal; a manager cannot accept for another person. Managers may reject proposals or cancel accepted work, but cannot mark another owner's work completed. Unknown/stale/foreign-group callback references cannot mutate tasks. Repeated button clicks are safe. Deadline/ownership ambiguity is presented in each proposal; the accepting owner can set a deadline explicitly.

## AI and proactive management

Set `Ai__Enabled=true`, `Ai__Provider=OpenAICompatible`, `Ai__Endpoint` to the exact **HTTPS** chat-completions URL, `Ai__Model`, and secret `Ai__ApiKey`. The provider must support bearer authentication and `response_format: {type: json_object}`. No vendor is assumed, and disabling AI leaves task tracking/reminders available. Unsupported APIs need another `IAiAnalysisService` adapter.

Only the latest opted-in text (default 30 messages / 12,000 text characters, within retention), member IDs/display names and project name are sent. No attachments or unrelated groups' records are included. Provider responses have bounded size/depth and a closed JSON contract. Referenced source messages/assignees must exist in that exact input; timestamps must have offsets and a plausible range. Unknown fields, invented IDs and invalid enums/dates are rejected. Low-confidence suggestions are discarded. Content is evidence, never executable commands. Titles deduplicate per group after whitespace/case normalization, including completed work; a deliberately repeated activity needs a distinct title. Similar but differently worded proposals still require human review.

AI tasks arrive as unconfirmed proposals with accept/reject buttons and source IDs. AI-derived decisions/blockers stay labeled **unconfirmed observations**. Confirmed task blocking is recorded separately. Automatic analysis waits for a minimum new-message count and cooldown; it creates at most **one new task proposal per automatic pass**, versus up to five on explicit `/analyze`. It does not interrupt for every message.

`Management__*` settings configure deadline lead time (24h), stale-work check-ins (2 days), daily summaries (17:00), weekly reports (Friday 16:00), quiet hours (20:00–08:00), and a shared proactive cap (4 messages/group/local day). Each group's IANA time zone handles daylight saving. Interactive command replies are immediate and outside this cap. Proposals, deadline reminders, check-ins and reports share it. Notifications have persistent deduplication keys; work that is completed/cancelled before dispatch is not reminded. Quiet periods defer proactive messages; stale queued notices expire after a day. Summary generation is deterministic, not another LLM call.

## Persistence, delivery, and privacy

EF migrations define projects, members, messages, tasks, task history, decisions, blockers, AI suggestions, the notification outbox/history, update ledger and polling cursor. Per-project composite foreign keys prevent cross-group task ownership/history links. Unique source-message, task-title fingerprint, update and notification keys prevent duplicates. PostgreSQL advisory locks serialize state changes across processes and elect one polling leader. An update, its effects, outbox entries and cursor commit together.

External Telegram sends cannot be exactly-once. The outbox marks an attempt before sending. Explicit 429 rejections are retried with bounded server-directed delays; permanent 4xx failures are recorded. Timeouts, 5xx and crash windows become **Uncertain** and are not automatically resent. This favors avoiding spam but can lose a notification; inspect `Notifications` state in secure database tooling and have a manager request `/task` or `/report` again. One global send/second plus Telegram rate-limit handling keeps normal traffic bounded. Monitoring/automatic alert routing for failed/uncertain deliveries is a deployment responsibility.

Raw messages and validated analysis batches expire after 14 days by default. Notification bodies/update ledgers and old **terminal-task** audit history expire after 90 days. Active task history and project artifacts (tasks, members, decisions, blockers) are retained until explicitly deleted by an administrator; deletion/export is currently a database administration operation, not a self-service command. The durable cursor continues to reject old updates after ledger pruning. Retention runs while integrations/proactive management are disabled, provided the process and database are running. Provider/Telegram copies and database backups have separate retention policies.

Logs exclude chat content, API keys, token-bearing URLs and SQL parameter values. Health endpoints expose no task data and provide no administrative API. Set production logging conservatively; never enable sensitive EF/HTTP request tracing. Backups, least-privilege database roles, provider data-use terms, operational monitoring and consent administration must be configured by the operator.

## Validation

```sh
dotnet restore --locked-mode
dotnet tool restore
dotnet build --no-restore --configuration Release
# Point only at a disposable PostgreSQL DB; tests create/drop isolated schemas.
# Set TEST_DATABASE_CONNECTION securely in your environment, without echoing it.
dotnet test --no-build --configuration Release --logger trx
dotnet ef migrations has-pending-model-changes --project src/TelegramProjectManager --configuration Release
docker build -t telegram-pm:mvp .
```

Without `TEST_DATABASE_CONNECTION`, PostgreSQL tests are explicitly **skipped**; that is not full validation. CI supplies PostgreSQL and runs them, checks migrations, and builds Docker. Local fake transports exercise the application boundary and real database without external API calls. Live Telegram, AI provider compatibility and Render deployment require operator configuration and a separate smoke test; do not infer those results from unit tests or health endpoints.

For managed proxy Docker builds, use `docker build --secret id=proxy_ca,src=/etc/ssl/certs/ca-certificates.crt -t telegram-pm:mvp .`. The CA is mounted only for restore; certificate and package verification stay enabled. Allow Microsoft's actual MCR redirect hosts in environment network settings if pulls are denied. See [DEPLOY_RENDER.md](DEPLOY_RENDER.md) for deployment.
