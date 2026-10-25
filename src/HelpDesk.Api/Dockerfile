FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props global.json ./
COPY src/HelpDesk.Api/HelpDesk.Api.csproj src/HelpDesk.Api/
COPY src/HelpDesk.Application/HelpDesk.Application.csproj src/HelpDesk.Application/
COPY src/HelpDesk.Domain/HelpDesk.Domain.csproj src/HelpDesk.Domain/
COPY src/HelpDesk.Infrastructure/HelpDesk.Infrastructure.csproj src/HelpDesk.Infrastructure/
RUN dotnet restore src/HelpDesk.Api/HelpDesk.Api.csproj
COPY src/HelpDesk.Api/ src/HelpDesk.Api/
COPY src/HelpDesk.Application/ src/HelpDesk.Application/
COPY src/HelpDesk.Domain/ src/HelpDesk.Domain/
COPY src/HelpDesk.Infrastructure/ src/HelpDesk.Infrastructure/
RUN dotnet publish src/HelpDesk.Api/HelpDesk.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/attachments /home/app/.aspnet/DataProtection-Keys \
    && chown app:app /data/attachments /home/app/.aspnet/DataProtection-Keys
COPY --from=build /app/publish ./
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "HelpDesk.Api.dll"]
