# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Pointer.sln ./
COPY src/Pointer.Core/Pointer.Core.csproj src/Pointer.Core/
COPY src/Pointer.Web/Pointer.Web.csproj src/Pointer.Web/
RUN dotnet restore src/Pointer.Web/Pointer.Web.csproj
COPY src/ src/
RUN dotnet publish src/Pointer.Web/Pointer.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# Listens on 8080 as the non-root "app" user that the base image provides.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Pointer.Web.dll"]
