# Rastro em um container só (ADR-002, Docker opcional): API com a tela embutida e SQLite em /data.
# Contexto de build: raiz do repositório (precisa do arquivo VERSION).
FROM node:20-alpine AS web
WORKDIR /src
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY VERSION ./
COPY backend/ ./backend/
RUN dotnet publish backend/src/Rastro.Api/Rastro.Api.csproj -c Release -o /app
COPY --from=web /src/dist/rastro-web/browser /app/wwwroot

# A imagem final é o SDK (não o runtime): a análise carrega solutions com o MSBuild e roda
# "dotnet restore" na cópia de trabalho. O git lê o commit dos repositórios montados.
FROM mcr.microsoft.com/dotnet/sdk:10.0
RUN git config --system --add safe.directory '*'
WORKDIR /app
COPY --from=build /app ./
# ASPNETCORE_URLS (e não HTTP_PORTS): sem endereço configurado, o Rastro só escuta no loopback.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_HTTP_PORTS= \
    RASTRO_HOME=/data \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
VOLUME /data
ENTRYPOINT ["dotnet", "Rastro.Api.dll"]
