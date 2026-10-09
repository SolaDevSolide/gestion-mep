# syntax=docker/dockerfile:1

# 1. Front React
FROM public.ecr.aws/docker/library/node:22-alpine AS front
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
# Dossier des pièces jointes (volume « pieces-jointes ») : il doit appartenir à l'utilisateur non root de l'image.
RUN mkdir -p /data/pieces-jointes && chown $APP_UID /data/pieces-jointes
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s \
  CMD wget -qO- http://localhost:8080/api/sante || exit 1
ENTRYPOINT ["dotnet", "GestionMep.Api.dll"]
