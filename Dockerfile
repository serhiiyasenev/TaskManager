# syntax=docker/dockerfile:1
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.401-noble
ARG ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.12-noble
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0.12-noble
ARG NODE_IMAGE=node:24.18.0-bookworm-slim

FROM ${NODE_IMAGE} AS frontend-build
WORKDIR /workspace
RUN npm install --global npm@12.2.0 --ignore-scripts
COPY package.json package-lock.json .npmrc .node-version ./
COPY src/frontend/package.json src/frontend/package.json
COPY tests/frontend/package.json tests/frontend/package.json
RUN npm ci --ignore-scripts && npm audit --audit-level=high
COPY src/frontend/ src/frontend/
RUN npm run build:frontend

FROM ${SDK_IMAGE} AS build
WORKDIR /src
COPY global.json ./
COPY .config/dotnet-tools.json .config/
COPY src/backend/DAL/DAL.csproj src/backend/DAL/packages.lock.json src/backend/DAL/
COPY src/backend/BLL/BLL.csproj src/backend/BLL/packages.lock.json src/backend/BLL/
COPY src/backend/WebAPI/WebAPI.csproj src/backend/WebAPI/packages.lock.json src/backend/WebAPI/
COPY src/backend/Notifier/Notifier.csproj src/backend/Notifier/packages.lock.json src/backend/Notifier/
COPY src/backend/Client/Client.csproj src/backend/Client/packages.lock.json src/backend/Client/
COPY src/backend/Hosting/Hosting.csproj src/backend/Hosting/packages.lock.json src/backend/Hosting/
COPY src/backend/WebHost/WebHost.csproj src/backend/WebHost/packages.lock.json src/backend/WebHost/
COPY tools/TaskManager.SqlBootstrap/TaskManager.SqlBootstrap.csproj tools/TaskManager.SqlBootstrap/packages.lock.json tools/TaskManager.SqlBootstrap/
RUN dotnet restore src/backend/WebAPI/WebAPI.csproj --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904 \
    && dotnet restore src/backend/Notifier/Notifier.csproj --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904 \
    && dotnet restore src/backend/Client/Client.csproj --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904 \
    && dotnet restore src/backend/WebHost/WebHost.csproj --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904 \
    && dotnet restore tools/TaskManager.SqlBootstrap/TaskManager.SqlBootstrap.csproj --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904
COPY src/backend/DAL/ src/backend/DAL/
COPY src/backend/BLL/ src/backend/BLL/
COPY src/backend/WebAPI/ src/backend/WebAPI/
COPY src/backend/Notifier/ src/backend/Notifier/
COPY src/backend/Client/ src/backend/Client/
COPY src/backend/Hosting/ src/backend/Hosting/
COPY src/backend/WebHost/ src/backend/WebHost/
COPY tools/TaskManager.SqlBootstrap/ tools/TaskManager.SqlBootstrap/

FROM build AS publish-web
RUN dotnet publish src/backend/WebHost/WebHost.csproj --configuration Release --no-restore \
    --output /out/web -p:UseAppHost=false -warnaserror:NU1901,NU1902,NU1903,NU1904

FROM build AS publish-webapi
RUN dotnet publish src/backend/WebAPI/WebAPI.csproj --configuration Release --no-restore \
    --output /out/webapi -p:UseAppHost=false -warnaserror:NU1901,NU1902,NU1903,NU1904

FROM build AS publish-notifier
RUN dotnet publish src/backend/Notifier/Notifier.csproj --configuration Release --no-restore \
    --output /out/notifier -p:UseAppHost=false -warnaserror:NU1901,NU1902,NU1903,NU1904

FROM build AS publish-client
RUN dotnet publish src/backend/Client/Client.csproj --configuration Release --no-restore \
    --output /out/client -p:UseAppHost=false -warnaserror:NU1901,NU1902,NU1903,NU1904

FROM build AS publish-sql-bootstrap
RUN dotnet publish tools/TaskManager.SqlBootstrap/TaskManager.SqlBootstrap.csproj --configuration Release --no-restore \
    --output /out/sql-bootstrap -p:UseAppHost=false -warnaserror:NU1901,NU1902,NU1903,NU1904

FROM build AS publish-migrations
RUN dotnet tool restore
# TaskContextDesignFactory constructs tooling without the API/auth host.
# Only the real database connection is supplied when the bundle executes.
RUN DOTNET_ENVIRONMENT=Production \
    ASPNETCORE_ENVIRONMENT=Production \
    BootstrapAdmin__Enabled=false \
    Reminders__Enabled=false \
    ConnectionStrings__DbConnection="Server=localhost;Database=TaskManagerDesign;Integrated Security=True;TrustServerCertificate=True" \
    dotnet tool run dotnet-ef migrations bundle --project src/backend/DAL/DAL.csproj \
    --startup-project src/backend/WebAPI/WebAPI.csproj --configuration Release \
    --target-runtime linux-x64 --output /out/efbundle

FROM ${ASPNET_IMAGE} AS aspnet-base
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    HOME=/home/app
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys \
    && chown -R app:app /app /home/app/.aspnet
USER $APP_UID
EXPOSE 8080

FROM aspnet-base AS webapi
COPY --from=publish-webapi --chown=app:app /out/webapi/ ./
ENTRYPOINT ["dotnet", "WebAPI.dll"]

FROM aspnet-base AS web
COPY --from=publish-web --chown=app:app /out/web/ ./
COPY --from=frontend-build --chown=app:app /workspace/src/frontend/dist/ ./wwwroot/
EXPOSE 8443
ENTRYPOINT ["dotnet", "TaskManager.WebHost.dll"]

FROM aspnet-base AS notifier
COPY --from=publish-notifier --chown=app:app /out/notifier/ ./
ENTRYPOINT ["dotnet", "Notifier.dll"]

FROM aspnet-base AS migrations
COPY --from=publish-migrations --chown=app:app /out/efbundle ./efbundle
ENTRYPOINT ["./efbundle"]

FROM aspnet-base AS sql-bootstrap
COPY --from=publish-sql-bootstrap --chown=app:app /out/sql-bootstrap/ ./
ENTRYPOINT ["dotnet", "TaskManager.SqlBootstrap.dll"]

FROM ${RUNTIME_IMAGE} AS client
WORKDIR /app
ENV HOME=/home/app
USER $APP_UID
COPY --from=publish-client --chown=app:app /out/client/ ./
ENTRYPOINT ["dotnet", "Client.dll"]
