# One image: the ASP.NET Core API serving the built React SPA from wwwroot.

# Docker Hub images via Google's mirror: shared CI runners hit Docker Hub's anonymous pull limit.
FROM mirror.gcr.io/library/node:24-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci
COPY web/ ./
RUN npm run build -- --outDir /out/wwwroot --emptyOutDir

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
# The OpenAPI document is generated in CI, not in the image build.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/Tropicast.Dashboard.Api -c Release -o /out -p:OpenApiGenerateDocuments=false

# Migration bundle image: deploys run it against the database before swapping the API (deploy/deploy.sh).
# It reads ConnectionStrings__Default (direct connection, not the pooler).
FROM api AS bundle
COPY dotnet-tools.json ./
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet tool restore && \
    dotnet ef migrations bundle --project src/Tropicast.Dashboard.Infrastructure --configuration Release \
      --self-contained -r linux-x64 -o /out/efbundle --force

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS migrations
COPY --from=bundle /out/efbundle /efbundle
USER $APP_UID
ENTRYPOINT ["/efbundle"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# Data Protection keys (DataProtection:KeysDirectory): a volume in production, owned by the app user.
RUN mkdir -p /data/keys && chown $APP_UID /data/keys
COPY --from=api /out ./
COPY --from=web /out/wwwroot ./wwwroot
# 8080: public API and SPA (behind the gateway). 8081: internal source-auth for the streaming node only.
ENV ASPNETCORE_HTTP_PORTS=8080;8081
EXPOSE 8080 8081
USER $APP_UID
ENTRYPOINT ["dotnet", "Tropicast.Dashboard.Api.dll"]
