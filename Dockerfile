# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY global.json Directory.Build.props TelegramProjectManager.sln ./
COPY src/TelegramProjectManager/TelegramProjectManager.csproj src/TelegramProjectManager/packages.lock.json src/TelegramProjectManager/
COPY tests/TelegramProjectManager.Tests/TelegramProjectManager.Tests.csproj tests/TelegramProjectManager.Tests/packages.lock.json tests/TelegramProjectManager.Tests/
# Optional public CA bundle for managed build proxies; never bake session trust into the image.
RUN --mount=type=secret,id=proxy_ca \
    if [ -f /run/secrets/proxy_ca ]; then export SSL_CERT_FILE=/run/secrets/proxy_ca; fi; \
    dotnet restore TelegramProjectManager.sln --locked-mode
COPY src/ src/
RUN dotnet publish src/TelegramProjectManager/TelegramProjectManager.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS runtime
WORKDIR /app
COPY --from=build /out ./
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
ENTRYPOINT ["dotnet", "TelegramProjectManager.dll"]
