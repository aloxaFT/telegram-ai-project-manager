# Deploy to Render (operator action required)

**No production resources have been created by this repository setup.** `render.yaml` defines a paid worker and paid PostgreSQL instance in **Frankfurt**. Review current Render pricing and approve resource creation explicitly before applying the blueprint. Docker builds/automated tests are not a live integration test.

## Prepare credentials and consent

- Create a bot using the verified @BotFather. Keep its token in a password manager until you enter it directly in Render's environment-variable editor. Never paste credentials into chat, commit `.env`, or place tokens in command URLs/logs.
- Obtain the authorized group chat ID and each manager's numeric Telegram user ID with trusted Telegram tools. Use one group at first. The bot ignores private/unauthorized chats, forwarded posts, anonymous admins, bot messages and non-text/edited messages.
- Choose an AI provider available in your jurisdiction with an OpenAI-compatible chat-completions endpoint, bearer authentication and JSON-object response format. Check its data-use/retention policy. AI can remain disabled while task management is used.
- Agree the participant consent and retention policy in README before enabling access to discussions. Disable Telegram privacy mode through @BotFather only after consent; remove/re-add the bot if needed. Group managers send `/start`, then participants individually `/join`.
- Stop any old poller and remove an existing webhook using Telegram's official tooling. Never run staging and production with the same bot token. Keep one token per database.

## Review and create the blueprint

After approving the cost, connect this repository in Render, choose **New → Blueprint**, and select `render.yaml`. Verify **Frankfurt** for both resources and **one worker instance**. Automatic deployments are disabled by the blueprint; deploy deliberately after CI passes.

Fill these in Render's environment settings (secrets directly in that UI):

| Variable | Value |
| --- | --- |
| `Telegram__Enabled` | `true` when ready to poll |
| `Telegram__Token` | Bot token (secret) |
| `Telegram__Groups__0__ChatId` | Exact negative authorized group ID |
| `Telegram__Groups__0__Name` | `TradeFlow AI` or your project name |
| `Telegram__Groups__0__ManagerUserIds__0` | Numeric manager user ID; add more manager array entries as needed |
| `Telegram__Groups__0__TimeZone` | `Europe/Warsaw` or another IANA zone |
| `Ai__Enabled` | Initially `false`; enable only after provider and consent are ready |
| `Ai__Provider` | `OpenAICompatible` |
| `Ai__Endpoint` | Full HTTPS chat-completions URL, without embedded credentials/query parameters |
| `Ai__Model` | Provider model identifier |
| `Ai__ApiKey` | Provider API key (secret) |

`DATABASE_URL` is linked to the Frankfurt database by the blueprint. The application converts Render's PostgreSQL URL to Npgsql syntax and defaults to **VerifyFull TLS**, without logging the URL. Verify the selected Render connection endpoint/certificate supports this mode in staging; do not disable certificate verification to work around a hostname/trust error. If necessary, configure `ConnectionStrings__Database` explicitly in Render with the provider-supported hostname, database, user, password, `SSL Mode=VerifyFull` and any supported CA settings. This explicit connection string takes precedence. Private database network access is restricted by `ipAllowList: []`; external administrative access needs a deliberate, temporary narrow rule.

Never enable sensitive SQL/HTTP tracing. Do not print environment settings to troubleshoot authentication. Do not share screenshots containing values.

## Migration and startup

The Docker entry point is `dotnet TelegramProjectManager.dll`. Render runs `dotnet TelegramProjectManager.dll --migrate-only` as the pre-deploy command. It applies included EF migrations and exits; a failure must stop deployment. Back up existing production data first. Leave `Database__AutoMigrate` unset/false in production.

For roles with separated privileges, run the migration command with a migration credential in a controlled maintenance job, then run the worker with a least-privilege role. The blueprint's single role is an MVP convenience, not a complete database access/backup policy.

The worker uses long polling; no public hostname/webhook is necessary. ASP.NET health routes exist for internal diagnostics. `/health/live` confirms the process is running; `/health/ready` checks database connectivity and migration state. These checks **do not validate bot/provider credentials**. Monitor Render worker state, error counts and notification delivery states independently.

## Staging smoke test before production use

Use a separate bot/group/database with consenting testers:

1. Confirm pre-deploy migrations succeed and the worker logs `Telegram polling leader acquired`. Resolve persistent 401/403 credentials or 409 webhook/poller conflicts; logs intentionally omit token-bearing URLs.
2. In the authorized group, the configured manager sends `/start`. Each tester sends `/join`; verify unauthorized groups/private chats receive no response.
3. Post a specific commitment with owner and deadline. A manager sends `/analyze` (after enabling a configured provider). Expect an **unconfirmed** proposal with source reference and inline accept/reject buttons.
4. Verify another member/manager cannot accept a nomination for somebody else. The nominated member accepts, uses `/doing NUMBER`, `/blocked NUMBER reason`, `/due NUMBER ISO_TIMESTAMP` and `/done NUMBER`. Check `/tasks`, `/today`, `/blockers`, `/status`, `/report`.
5. With a suitably configured time zone/schedule, confirm reminders, daily/weekly reports, quiet hours and the shared proactive limit. Do not flood a production group to test scheduling.
6. Restart the worker. Confirm persisted tasks, no duplicate processing and no duplicate scheduled notices. Check `Notifications` for Failed/Uncertain deliveries using secure DB tooling. Explicit 429 failures retry; ambiguous sends intentionally do not.
7. `/leave` should remove that user's raw retained messages and the group's stored analysis batches, and stop subsequent collection. Verify your provider and backup retention policies separately.

Live Telegram/AI/Render behavior remains unverified until this smoke test actually runs. Provider incompatibility can require another `IAiAnalysisService` adapter. Webhooks, attachments/transcription, semantic duplicate merging, self-service exports/deletion, automatic milestone accounting and operational alert integrations are outside this MVP.

## Operations and recovery

- Keep one replica. A PostgreSQL advisory lock elects one poller, but a singleton deployment also avoids competing requests during network disruptions.
- Configure PostgreSQL backups and test restoration. Messages default to 14-day retention; project artifacts remain until administrative deletion. Active task history is retained; old terminal-task audit records and notification bodies expire after 90 days.
- Deadline reminders/check-ins/reports share a default cap of four proactive messages per group per local day. Interactive replies bypass quiet hours/cap. Review `Management__*` defaults in `.env.example` and README.
- Do not automatically replay Sending/Uncertain notifications: a Telegram send might have succeeded. A manager can issue `/task NUMBER` or `/report` again. An outbox record is not proof of delivery until marked Sent, and a crash after delivery can still leave it Uncertain.
- Rotate bot/AI credentials in Render's environment settings and restart the worker. A new bot token should use a separate database/cursor; do not reset a production cursor casually.
- Roll back an application image only after checking database compatibility; do not auto-reverse destructive migrations. This initial migration is additive.
