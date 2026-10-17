# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and csproj
COPY ExpenseTracker.sln ./
COPY ExpenseTracker/ExpenseTracker.csproj ./ExpenseTracker/
RUN dotnet restore ExpenseTracker/ExpenseTracker.csproj

# Copy everything else and publish
COPY . ./
RUN dotnet publish ExpenseTracker/ExpenseTracker.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV PORT=8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080
ENTRYPOINT ["dotnet", "ExpenseTracker.dll"]
