# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS gateway-build
WORKDIR /src
COPY src/gateway/ ./src/gateway/
RUN dotnet restore ./src/gateway/MicroClaw.sln
RUN dotnet publish ./src/gateway/MicroClaw/MicroClaw.csproj -c Release -o /out/gateway /p:UseAppHost=false

FROM node:22-bookworm-slim AS webui-build
WORKDIR /src/webui
COPY src/webui/package.json src/webui/package-lock.json ./
RUN npm ci
COPY src/webui/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS all-in-one
WORKDIR /app
# 从 webui-build 阶段复制 Node.js（避免重复安装，版本与构建阶段一致）
COPY --from=webui-build /usr/local/bin/node /usr/local/bin/node
COPY --from=webui-build /usr/local/bin/npm /usr/local/bin/npm
COPY --from=webui-build /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN apt-get update \
	&& apt-get install -y --no-install-recommends bash python3\
	&& rm -rf /var/lib/apt/lists/*

COPY --from=gateway-build /out/gateway/ /app/gateway/
COPY --from=webui-build /src/webui/dist/ /app/webui/
COPY docker/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN mkdir -p /app/.microclaw \
    && sed -i 's/\r//' /usr/local/bin/entrypoint.sh \
    && chmod +x /usr/local/bin/entrypoint.sh
ENV MICROCLAW_HOME=/app/.microclaw \
	MICROCLAW_WEBUI_PATH=/app/webui
EXPOSE 8080
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]