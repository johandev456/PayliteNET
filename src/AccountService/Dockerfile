# Build: desde la raíz del repo →  docker build -f src/AccountService/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero solo el .csproj: la capa de restore se cachea mientras no cambien las dependencias.
COPY src/AccountService/AccountService.csproj src/AccountService/
RUN dotnet restore src/AccountService/AccountService.csproj

COPY src/AccountService/ src/AccountService/
RUN dotnet publish src/AccountService/AccountService.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Usuario sin privilegios que trae la imagen oficial.
USER $APP_UID
ENTRYPOINT ["dotnet", "AccountService.dll"]
