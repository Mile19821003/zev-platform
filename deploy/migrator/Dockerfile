# Builds the reviewed, idempotent EF migration script from the API sources and runs it with sqlcmd.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS script
RUN dotnet tool install --global dotnet-ef --version 10.0.12
ENV PATH="${PATH}:/root/.dotnet/tools"
WORKDIR /source
COPY --from=api Zev.Api.csproj ./
RUN dotnet restore Zev.Api.csproj
COPY --from=api . .
RUN dotnet ef migrations script --idempotent --project Zev.Api.csproj --output /out/migrate.sql

# The SQL Server image ships sqlcmd (mssql-tools18) and is already present on the host.
FROM mcr.microsoft.com/mssql/server:2022-latest
COPY --from=script /out/migrate.sql /migrator/migrate.sql
COPY migrate.sh app-login.sql /migrator/
ENTRYPOINT ["/bin/bash", "/migrator/migrate.sh"]
