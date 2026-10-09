# syntax=docker/dockerfile:1
# One image: the ASP.NET Core API serving the built React SPA from wwwroot.

FROM node:24-alpine AS web
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /out ./
COPY --from=web /out/wwwroot ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Tropicast.Dashboard.Api.dll"]
