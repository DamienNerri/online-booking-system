# --- Étape de build ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restauration (cache des couches sur le csproj)
COPY OnlineBooking.sln .
COPY src/OnlineBooking.Api/OnlineBooking.Api.csproj src/OnlineBooking.Api/
RUN dotnet restore src/OnlineBooking.Api/OnlineBooking.Api.csproj

# Publication
COPY src/ src/
COPY migrations/ migrations/
RUN dotnet publish src/OnlineBooking.Api/OnlineBooking.Api.csproj -c Release -o /app --no-restore

# --- Étape d'exécution ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
COPY migrations/ ./migrations/
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OnlineBooking.Api.dll"]
