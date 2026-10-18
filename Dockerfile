# 1. Front React
FROM mirror.gcr.io/library/node:26-alpine AS front
WORKDIR /src/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# 2. API .NET
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS back
# Numéro de version affiché dans l'application et comparé aux versions publiées (« 1.2.0 ») ; donné par la CI.
ARG VERSION=0.0.0-dev
WORKDIR /src/backend
COPY backend/ ./
RUN dotnet publish src/GestionMep.Api/GestionMep.Api.csproj -c Release -o /app -p:Version=$VERSION

# 3. Image finale
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=back /app ./
COPY --from=front /src/frontend/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
# Dossiers des pièces jointes, des clés de session et des sauvegardes (volumes « pieces-jointes », « cles », « sauvegardes ») : ils doivent appartenir à
# l'utilisateur non root de l'image.
RUN mkdir -p /data/pieces-jointes /data/cles /data/sauvegardes /data/mise-a-jour \
 && chown $APP_UID /data/pieces-jointes /data/cles /data/sauvegardes /data/mise-a-jour
# Commandes d'exploitation : « docker compose exec app gestion-mep aide ».
RUN printf '#!/bin/sh\nexec dotnet /app/GestionMep.Api.dll "$@"\n' > /usr/local/bin/gestion-mep && chmod 755 /usr/local/bin/gestion-mep
USER $APP_UID
# L'image aspnet n'a ni curl ni wget : l'application interroge elle-même /api/sante.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s \
  CMD ["dotnet", "GestionMep.Api.dll", "--sante"]
ENTRYPOINT ["dotnet", "GestionMep.Api.dll"]
