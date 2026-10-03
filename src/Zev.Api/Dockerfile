FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Zev.Api.csproj ./
RUN dotnet restore Zev.Api.csproj
COPY . .
RUN dotnet publish Zev.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /app/storage && chown -R $APP_UID:$APP_UID /app/storage
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Zev.Api.dll"]
