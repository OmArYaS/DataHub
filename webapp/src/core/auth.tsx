import { createContext, useCallback, useContext, useMemo, useState } from "react";
import type { ReactNode } from "react";

import { api, ApiError, tokenStore } from "./api";

export type AuthUser = {
  userName: string;
  userKey: string;
  roles: string[];
  accessToken: string;
  expiresUtc: string;
};

const PERSIST_KEY = "datahub.auth";

type TokenResponse = {
  accessToken: string;
  tokenType: string;
  expiresUtc: string;
  userKey: string;
  roles: string[];
};

type AuthContextValue = {
  user: AuthUser | null;
  busy: boolean;
  isAdmin: boolean;
  hasRole: (role: string) => boolean;
  login: (userName: string) => Promise<void>;
  logout: () => void;
};

const AuthContext = createContext<AuthContextValue | null>(null);

function loadPersisted(): AuthUser | null {
  try {
    const raw = localStorage.getItem(PERSIST_KEY);
    if (!raw) {
      return null;
    }

    const user = JSON.parse(raw) as AuthUser;
    const expired = new Date(user.expiresUtc).getTime() <= Date.now();

    if (expired) {
      localStorage.removeItem(PERSIST_KEY);
      return null;
    }

    tokenStore.set(user.accessToken);
    return user;
  } catch {
    localStorage.removeItem(PERSIST_KEY);
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(() => loadPersisted());
  const [busy, setBusy] = useState(false);

  const login = useCallback(async (userName: string) => {
    setBusy(true);
    try {
      const issued = await api<TokenResponse>("/api/auth/token", {
        method: "POST",
        body: JSON.stringify({ userName }),
      });

      const next: AuthUser = {
        userName,
        userKey: issued.userKey,
        roles: issued.roles,
        accessToken: issued.accessToken,
        expiresUtc: issued.expiresUtc,
      };

      tokenStore.set(next.accessToken);
      localStorage.setItem(PERSIST_KEY, JSON.stringify(next));
      setUser(next);
    } finally {
      setBusy(false);
    }
  }, []);

  const logout = useCallback(() => {
    tokenStore.clear();
    localStorage.removeItem(PERSIST_KEY);
    setUser(null);
  }, []);

  const value = useMemo<AuthContextValue>(() => {
    const roles = user?.roles ?? [];
    return {
      user,
      busy,
      isAdmin: roles.includes("admin"),
      hasRole: (role: string) => roles.includes(role),
      login,
      logout,
    };
  }, [user, busy, login, logout]);

  return (
    <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) {
    throw new Error("useAuth must be used inside the AuthProvider.");
  }
  return value;
}

/** Human message for any thrown failure (ApiError code first, text second). */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    return `${error.code}: ${error.message}`;
  }

  if (error instanceof Error) {
    return error.message;
  }

  return "Unexpected failure.";
}
