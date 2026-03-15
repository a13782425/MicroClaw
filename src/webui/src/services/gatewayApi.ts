import axios from "axios";

export type GatewayHealth = {
  status: string;
  service: string;
  utcNow: string;
  version: string;
};

export async function getGatewayHealth(): Promise<GatewayHealth> {
  const { data } = await axios.get<GatewayHealth>("/api/health");
  return data;
}

export async function login(username: string, password: string) {
  const { data } = await axios.post("/api/auth/login", { username, password });
  return data as {
    token: string;
    username: string;
    role: string;
    expiresAtUtc: string;
  };
}