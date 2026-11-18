FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ExpenseTracker/ExpenseTracker/ExpenseTracker.csproj ./
RUN dotnet restore ExpenseTracker.csproj
COPY ExpenseTracker/ExpenseTracker/ ./
RUN dotnet publish ExpenseTracker.csproj -c Release --no-restore -o /app/publish -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    HOME=/home/app \
    PORT=10000
EXPOSE 10000
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "ExpenseTracker.dll"]
