FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_VERSION=0.0.0
WORKDIR /src

#COPY ["SyncUpRocks.sln", "./"]
COPY ["SyncUpRocks.Api/SyncUpRocks.Api.csproj", "SyncUpRocks.Api/"]
COPY ["SyncUpRocks.Data.Access/SyncUpRocks.Data.Access.csproj", "SyncUpRocks.Data.Access/"]
COPY ["SyncUpRocks.Data.Importers/SyncUpRocks.Data.Importers.csproj", "SyncUpRocks.Data.Importers/"]

RUN dotnet restore "SyncUpRocks.Api/SyncUpRocks.Api.csproj"

# Copy the rest of the source code
COPY . .

WORKDIR "/src/SyncUpRocks.Api"
RUN dotnet publish "SyncUpRocks.Api.csproj" \
    -c Release \
    -o /app/publish \
    -p:Version=$BUILD_VERSION \
    -p:InformationalVersion=$BUILD_VERSION \
    /p:UseAppHost=false

# --- STAGE 2: Final Runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update && apt-get install -y libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=9001
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_NOLOGO=true
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

# Culture Invariant (Optional: keeps image small if you don't need localizations)
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

# Security: Run as the built-in non-root 'app' user
USER $APP_UID
COPY --from=build /app/publish .
EXPOSE 9001

ENTRYPOINT ["dotnet", "SyncUpRocks.Api.dll"]