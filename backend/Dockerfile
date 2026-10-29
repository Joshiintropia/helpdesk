FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY HelpDesk.slnx ./
COPY src/HelpDesk.Domain/HelpDesk.Domain.csproj src/HelpDesk.Domain/
COPY src/HelpDesk.Infrastructure/HelpDesk.Infrastructure.csproj src/HelpDesk.Infrastructure/
COPY src/HelpDesk.Api/HelpDesk.Api.csproj src/HelpDesk.Api/
RUN dotnet restore src/HelpDesk.Api/HelpDesk.Api.csproj
COPY src/ src/
RUN dotnet publish src/HelpDesk.Api/HelpDesk.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Attachments and data-protection keys live here; mount a volume so they survive updates.
RUN mkdir -p /app/data && chown $APP_UID /app/data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__Path=/app/data
EXPOSE 8080
VOLUME /app/data
USER $APP_UID
ENTRYPOINT ["dotnet", "HelpDesk.Api.dll"]
