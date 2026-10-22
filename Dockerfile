# syntax=docker/dockerfile:1
# One image for the whole app: the ASP.NET Core API (REST, SignalR hub, MCP server) serving the
# built Angular fan app from wwwroot. Runs as a non-root user on port 8080.

# ---- fan app (Angular) ----
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npx ng build --configuration production \
 && mkdir /out && cp -r dist/*/browser/. /out/

# ---- API (.NET 10) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/TwelfthVoice.Api/TwelfthVoice.Api.csproj -c Release -o /app /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /out ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "TwelfthVoice.Api.dll"]
