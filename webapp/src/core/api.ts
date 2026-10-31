/**
 * The one fetch wrapper: Bearer token injection, ProblemDetails → typed
 * error (stable `code` extension member), cache/ETag coordination helpers.
 * No HTTP client dependency — the platform primitive is enough.
 */

export type Problem = {
  status: number;
  title: string;
  code?: string;
};

/** Carries the machine-readable rule code the API contract guarantees. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;

  constructor(status: number, code: string, title: string) {
    super(title || code || `HTTP ${status}`);
    this.status = status;
    this.code = code ?? "UNKNOWN";
  }
}

export const tokenStore = {
  get: (): string => localStorage.getItem("datahub.token") ?? "",
  set: (value: string): void => localStorage.setItem("datahub.token", value),
  clear: (): void => localStorage.removeItem("datahub.token"),
};

async function readProblem(response: Response): Promise<Problem> {
  let body: unknown = null;
  try {
    body = await response.json();
  } catch {
    body = null;
  }

  const problem = body as Partial<Problem> | null;
  return {
    status: response.status,
    title: problem?.title ?? response.statusText,
    code: problem?.code,
  };
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers ?? {});
  if (!headers.has("Content-Type") && init.body) {
    headers.set("Content-Type", "application/json");
  }

  const token = tokenStore.get();
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  const response = await fetch(path, { ...init, headers, cache: "no-store" });

  if (!response.ok) {
    const problem = await readProblem(response);
    throw new ApiError(problem.status, problem.code ?? "UNKNOWN", problem.title);
  }

  return (await response.json()) as T;
}

export async function apiVoid(path: string, init: RequestInit = {}): Promise<void> {
  const headers = new Headers(init.headers ?? {});
  const token = tokenStore.get();
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  const response = await fetch(path, { ...init, headers, cache: "no-store" });
  if (!response.ok) {
    const problem = await readProblem(response);
    throw new ApiError(problem.status, problem.code ?? "UNKNOWN", problem.title);
  }
}

/** Runs a request honouring the ETag/304 contract: a 304 returns
 *  notModified instead of parsing an empty body, letting callers keep the
 *  table they already rendered. */
export async function apiMaybeNotModified(
  path: string,
  init: RequestInit,
): Promise<{ data: unknown; notModified: boolean }> {
  const headers = new Headers(init.headers ?? {});
  if (!headers.has("Content-Type") && init.body) {
    headers.set("Content-Type", "application/json");
  }
  const token = tokenStore.get();
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  const response = await fetch(path, { ...init, headers, cache: "no-store" });

  if (response.status === 304) {
    return { data: null, notModified: true };
  }

  if (!response.ok) {
    const problem = await readProblem(response);
    throw new ApiError(problem.status, problem.code ?? "UNKNOWN", problem.title);
  }

  return { data: await response.json(), notModified: false };
}

export function downloadCsv(filename: string, columns: readonly string[], rows: readonly (readonly string[])[]): void {
  const escape = (value: string): string =>
    /[",\r\n]/.test(value) ? "\"" + value.replaceAll("\"", "\"\"") + "\"" : value;

  const lines = [
    columns.join(","),
    ...rows.map((row) => row.map((cell) => escape(cell ?? "")).join(",")),
  ];

  const blob = new Blob([lines.join("\r\n")], { type: "text/csv" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  URL.revokeObjectURL(url);
}
