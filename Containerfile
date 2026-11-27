# Resurs & Kapacitet – en container med .NET-backend som även serverar Vue-frontenden.
# Bygg:  podman build --format docker -t resurs-kapacitet .
# Kör:   podman run -d -p 8080:8080 -v resurs-data:/data resurs-kapacitet

# ---------- Frontend (Vue 3 + Vite) ----------
FROM docker.io/library/node:22-alpine AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json frontend/.npmrc ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

# ---------- Backend (.NET 10) ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY backend/src/ResursKapacitet.Api/ResursKapacitet.Api.csproj backend/src/ResursKapacitet.Api/
RUN dotnet restore backend/src/ResursKapacitet.Api/ResursKapacitet.Api.csproj
COPY backend/src/ backend/src/
RUN dotnet publish backend/src/ResursKapacitet.Api/ResursKapacitet.Api.csproj \
      -c Release -o /app --no-restore /p:UseAppHost=false

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
ARG APP_VERSION=""
ARG GIT_SHA=""
ARG BUILD_DATE=""
LABEL org.opencontainers.image.title="Resurs & Kapacitet" \
      org.opencontainers.image.description="Resurs- och kapacitetsplanering för systemutvecklingsteam" \
      org.opencontainers.image.version="${APP_VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}" \
      org.opencontainers.image.created="${BUILD_DATE}" \
      io.containers.autoupdate="registry"

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DATA_DIR=/data \
    TZ=Europe/Stockholm \
    APP_VERSION=${APP_VERSION} \
    GIT_SHA=${GIT_SHA} \
    BUILD_DATE=${BUILD_DATE}

WORKDIR /app
COPY --from=backend /app ./
COPY --from=frontend /src/frontend/dist ./wwwroot

USER root
RUN mkdir -p /data && chown "$APP_UID":"$APP_UID" /data
USER $APP_UID

VOLUME /data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["dotnet", "ResursKapacitet.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "ResursKapacitet.Api.dll"]
