# 1. Front React
FROM public.ecr.aws/docker/library/node:26-alpine AS front
WORKDIR /src/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# 2. API .NET
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS back
WORKDIR /src/backend
COPY backend/ ./
RUN dotnet publish src/GestionMep.Api/GestionMep.Api.csproj -c Release -o /app

# 3. Image finale
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=back /app ./
COPY --from=front /src/frontend/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
# Dossiers des pièces jointes et des clés de session (volumes « pieces-jointes » et « cles ») : ils doivent appartenir à
# l'utilisateur non root de l'image.
RUN mkdir -p /data/pieces-jointes /data/cles && chown $APP_UID /data/pieces-jointes /data/cles
# Commandes d'exploitation : « docker compose exec app gestion-mep aide ».
RUN printf '#!/bin/sh\nexec dotnet /app/GestionMep.Api.dll "$@"\n' > /usr/local/bin/gestion-mep && chmod 755 /usr/local/bin/gestion-mep
USER $APP_UID
# L'image aspnet n'a ni curl ni wget : l'application interroge elle-même /api/sante.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s \
  CMD ["dotnet", "GestionMep.Api.dll", "--sante"]
ENTRYPOINT ["dotnet", "GestionMep.Api.dll"]
