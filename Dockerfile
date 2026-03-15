# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS gateway-build
WORKDIR /src
COPY src/gateway/ ./src/gateway/
RUN dotnet restore ./src/gateway/MicroClaw.sln
RUN dotnet publish ./src/gateway/MicroClaw.Gateway.WebApi/MicroClaw.Gateway.WebApi.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS gateway
WORKDIR /app
COPY --from=gateway-build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MicroClaw.Gateway.WebApi.dll"]

FROM node:22-alpine AS webui-build
WORKDIR /src/webui
COPY src/webui/package*.json ./
RUN npm install
COPY src/webui/ ./
RUN npm run build

FROM gateway AS all-in-one
COPY --from=webui-build /src/webui/dist /app/wwwroot