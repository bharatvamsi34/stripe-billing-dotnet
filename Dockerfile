# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props .editorconfig StripeBilling.sln ./
COPY src/ src/
COPY tests/ tests/
RUN dotnet restore src/StripeBilling.Api/StripeBilling.Api.csproj
RUN dotnet publish src/StripeBilling.Api/StripeBilling.Api.csproj -c Release -o /app --no-restore

# Run as a non-root user on the slim ASP.NET runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "StripeBilling.Api.dll"]
