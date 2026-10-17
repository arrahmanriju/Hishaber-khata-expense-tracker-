# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project file preserving exact repo folder hierarchy
COPY ExpenseTracker/ExpenseTracker.sln ./ExpenseTracker/
COPY ExpenseTracker/ExpenseTracker/ExpenseTracker.csproj ./ExpenseTracker/ExpenseTracker/
RUN dotnet restore ExpenseTracker/ExpenseTracker/ExpenseTracker.csproj

# Copy the rest of the source code and publish
COPY ExpenseTracker/ ./ExpenseTracker/
RUN dotnet publish ExpenseTracker/ExpenseTracker/ExpenseTracker.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV PORT=8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080
ENTRYPOINT ["dotnet", "ExpenseTracker.dll"]
