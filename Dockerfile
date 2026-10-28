# syntax=docker/dockerfile:1
# SDK tag must satisfy global.json (10.0.401, ADR-07).
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY global.json Directory.Build.props GoalFlow.sln ./
COPY GoalFlow.Domain/GoalFlow.Domain.csproj GoalFlow.Domain/
COPY GoalFlow.Application/GoalFlow.Application.csproj GoalFlow.Application/
COPY GoalFlow.Infrastructure/GoalFlow.Infrastructure.csproj GoalFlow.Infrastructure/
COPY GoalFlow.Api/GoalFlow.Api.csproj GoalFlow.Api/
RUN dotnet restore GoalFlow.Api/GoalFlow.Api.csproj
COPY GoalFlow.Domain/ GoalFlow.Domain/
COPY GoalFlow.Application/ GoalFlow.Application/
COPY GoalFlow.Infrastructure/ GoalFlow.Infrastructure/
COPY GoalFlow.Api/ GoalFlow.Api/
RUN dotnet publish GoalFlow.Api/GoalFlow.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
# Non-root user provided by the base image.
USER $APP_UID
ENTRYPOINT ["dotnet", "GoalFlow.Api.dll"]
