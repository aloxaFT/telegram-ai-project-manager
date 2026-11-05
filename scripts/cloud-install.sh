#!/usr/bin/env bash
# Managed Linux x64 environment setup. Project code and lockfiles are never rewritten.
set -euo pipefail
repo=/workspace/telegram-ai-project-manager
setup=/workspace/.setup
tools=/workspace/.tools
mkdir -p "$setup" "$tools/dotnet"
test "$(uname -s)/$(uname -m)" = Linux/x86_64 || { echo 'This cloud setup requires Linux x64.' >&2; exit 1; }
export DOTNET_ROOT="$tools/dotnet"
export DOTNET_CLI_HOME="$tools/dotnet-home"
export NUGET_PACKAGES="$tools/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
sdk_version=10.0.403
if [ ! -x "$DOTNET_ROOT/dotnet" ] || [ "$("$DOTNET_ROOT/dotnet" --version)" != "$sdk_version" ]; then
    archive="$setup/dotnet-sdk-$sdk_version.tar.gz"
    curl --fail --silent --show-error --location --retry 3 \
        "https://builds.dotnet.microsoft.com/dotnet/Sdk/$sdk_version/dotnet-sdk-$sdk_version-linux-x64.tar.gz" -o "$archive"
    echo "b7b3d8461cf98b07d2552ce9199702fa3ad010ac2671a5f8cd5136f7534e50f6855ba7bbb02b11494563f1d04156276a31cda1103b900e5eecd0d9473dc7b9c5  $archive" | sha512sum --check --status
    tar -xzf "$archive" -C "$DOTNET_ROOT"
    rm "$archive"
fi
cat > "$setup/dotnet" <<'WRAPPER'
#!/usr/bin/env bash
export DOTNET_ROOT=/workspace/.tools/dotnet
export DOTNET_CLI_HOME=/workspace/.tools/dotnet-home
export NUGET_PACKAGES=/workspace/.tools/nuget
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
exec "$DOTNET_ROOT/dotnet" "$@"
WRAPPER
chmod +x "$setup/dotnet"
cd "$repo"
"$setup/dotnet" restore --locked-mode
"$setup/dotnet" tool restore
"$setup/dotnet" build --no-restore -m:4
# Local-only credentials, generated once and preserved with the database; never application/API credentials.
python3 - <<'PY'
from pathlib import Path
import os, secrets, shlex
setup=Path('/workspace/.setup')
pg=setup/'postgres.env'
if not pg.exists():
    pg.write_text('POSTGRES_USER=telegram_pm\nPOSTGRES_DB=telegram_pm\nPOSTGRES_PASSWORD='+secrets.token_hex(24)+'\n')
    pg.chmod(0o600)
values=dict(line.split('=',1) for line in pg.read_text().splitlines())
# This setup owns a single loopback-only development container, not a remote database.
connection='Host=127.0.0.1;Port=54329;Database=telegram_pm;Username=telegram_pm;Password='+values['POSTGRES_PASSWORD']
env=setup/'env.sh'
if not env.exists():
    env.write_text('export ConnectionStrings__Database='+shlex.quote(connection)+'\nexport TEST_DATABASE_CONNECTION='+shlex.quote(connection)+'\nexport PATH=/workspace/.setup:$PATH\n')
    env.chmod(0o600)
PY
# Explicit local daemon only. Do not start/reconfigure a managed daemon if it is unavailable.
docker_local() {
    env -u DOCKER_HOST -u DOCKER_CONTEXT -u DOCKER_TLS -u DOCKER_TLS_VERIFY -u DOCKER_CERT_PATH \
        docker --host=unix:///var/run/docker.sock "$@"
}
docker_local info --format '{{.ServerVersion}}' >/dev/null
if docker_local container inspect telegram-pm-postgres >/dev/null 2>&1; then
    docker_local start telegram-pm-postgres >/dev/null
else
    docker_local run -d --name telegram-pm-postgres --env-file "$setup/postgres.env" \
        -p 127.0.0.1:54329:5432 --health-cmd 'pg_isready -U telegram_pm -d telegram_pm' \
        --health-interval 2s --health-retries 20 postgres:17-bookworm >/dev/null
fi
ready=false
for ((attempt=0; attempt<30; attempt++)); do
    if docker_local exec telegram-pm-postgres pg_isready -U telegram_pm -d telegram_pm >/dev/null 2>&1; then ready=true; break; fi
    sleep 1
done
[ "$ready" = true ] || { echo 'Local PostgreSQL did not become ready.' >&2; exit 1; }
source "$setup/env.sh"
"$setup/dotnet" run --project src/TelegramProjectManager --no-build -- --migrate-only
"$setup/dotnet" test --no-build --logger 'trx;LogFileName=results.trx' --results-directory artifacts/test-results
