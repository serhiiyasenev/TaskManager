# syntax=docker/dockerfile:1
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.401-noble
ARG ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.12-noble
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0.12-noble
ARG NODE_IMAGE=node:24.18.0-bookworm-slim

FROM ${NODE_IMAGE} AS frontend-build
WORKDIR /frontend
RUN npm install --global npm@12.2.0 --ignore-scripts
COPY frontend/package.json frontend/package-lock.json frontend/.npmrc ./
RUN npm ci --ignore-scripts && npm audit --audit-level=high
COPY frontend/ ./
RUN npm run build

FROM ${SDK_IMAGE} AS build
WORKDIR /src
COPY global.json ./
COPY .config/dotnet-tools.json .config/
COPY DAL/DAL.csproj DAL/packages.lock.json DAL/
COPY BLL/BLL.csproj BLL/packages.lock.json BLL/
COPY WebAPI/WebAPI.csproj WebAPI/packages.lock.json WebAPI/
COPY Notifier/Notifier.csproj Notifier/packages.lock.json Notifier/
COPY Client/Client.csproj Client/packages.lock.json Client/
COPY Hosting/Hosting.csproj Hosting/packages.lock.json Hosting/
COPY WebHost/WebHost.csproj WebHost/packages.lock.json WebHost/
RUN dotnet restore WebAPI/WebAPI.csproj --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904 \
    && dotnet restore Notifier/Notifier.csproj --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904 \
    && dotnet restore Client/Client.csproj --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904 \
    && dotnet restore WebHost/WebHost.csproj --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904
COPY DAL/ DAL/
COPY BLL/ BLL/
COPY WebAPI/ WebAPI/
COPY Notifier/ Notifier/
COPY Client/ Client/
COPY Hosting/ Hosting/
COPY WebHost/ WebHost/

FROM build AS publish-web
RUN dotnet publish WebHost/WebHost.csproj --configuration Release --no-restore \
    --output /out/web -p:UseAppHost=false -warnaserror:NU1901,NU1902,NU1903,NU1904

FROM build AS publish-webapi
RUN dotnet publish WebAPI/WebAPI.csproj --configuration Release --no-restore \
    --output /out/webapi -p:UseAppHost=false -warnaserror:NU1901,NU1902,NU1903,NU1904

FROM build AS publish-notifier
RUN dotnet publish Notifier/Notifier.csproj --configuration Release --no-restore \
    --output /out/notifier -p:UseAppHost=false -warnaserror:NU1901,NU1902,NU1903,NU1904

FROM build AS publish-client
RUN dotnet publish Client/Client.csproj --configuration Release --no-restore \
    --output /out/client -p:UseAppHost=false -warnaserror:NU1901,NU1902,NU1903,NU1904

FROM build AS publish-migrations
RUN dotnet tool restore
# TaskContextDesignFactory constructs tooling without the API/auth host.
# Only the real database connection is supplied when the bundle executes.
RUN DOTNET_ENVIRONMENT=Production \
    ASPNETCORE_ENVIRONMENT=Production \
    BootstrapAdmin__Enabled=false \
    Reminders__Enabled=false \
    ConnectionStrings__DbConnection="Server=localhost;Database=TaskManagerDesign;Integrated Security=True;TrustServerCertificate=True" \
    dotnet tool run dotnet-ef migrations bundle --project DAL/DAL.csproj \
    --startup-project WebAPI/WebAPI.csproj --configuration Release \
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
COPY --from=frontend-build --chown=app:app /frontend/dist/ ./wwwroot/
EXPOSE 8443
ENTRYPOINT ["dotnet", "TaskManager.WebHost.dll"]

FROM aspnet-base AS notifier
COPY --from=publish-notifier --chown=app:app /out/notifier/ ./
ENTRYPOINT ["dotnet", "Notifier.dll"]

FROM aspnet-base AS migrations
COPY --from=publish-migrations --chown=app:app /out/efbundle ./efbundle
ENTRYPOINT ["./efbundle"]

FROM ${RUNTIME_IMAGE} AS client
WORKDIR /app
ENV HOME=/home/app
USER $APP_UID
COPY --from=publish-client --chown=app:app /out/client/ ./
ENTRYPOINT ["dotnet", "Client.dll"]
