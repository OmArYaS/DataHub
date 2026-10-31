import { api, apiMaybeNotModified, apiVoid, downloadCsv } from "./api";

// ── API contracts (mirrors src/DataHub.Application/Contracts) ─────────────

export type SourceDto = {
  sourceKey: string;
  displayName: string;
  description?: string | null;
  provider: string;
  connectionName: string;
  connectionConfigured: boolean;
  isEnabled: boolean;
  commandTimeoutSeconds: number;
  registeredUtc: string;
};

export type SourceHealthDto = {
  sourceKey: string;
  reachable: boolean;
  failureCode?: string | null;
  detail?: string | null;
  elapsedMilliseconds: number;
};

export type ReportDto = {
  reportKey: string;
  displayName: string;
  description?: string | null;
  sourceKey: string;
  queryText: string;
  allowedRoles: string[];
  isCacheable: boolean;
  cacheSecondsTtl: number;
  syncIntervalMinutes?: number | null;
  registeredUtc: string;
};

export type ReportResultDto = {
  reportKey: string;
  columns: string[];
  rows: (string | null)[][];
  totalCount: number;
  page: number;
  pageSize: number;
  elapsedMilliseconds: number;
  fromCache: boolean;
  etag: string;
};

export type SyncRunDto = {
  id: number;
  reportKey: string;
  blobPath: string;
  rowCount: number;
  status: string;
  triggeredBy: string;
  startedUtc: string;
  completedUtc?: string | null;
  failureCode?: string | null;
};

export type SyncTileDto = {
  reportKey: string;
  lastStatus?: string | null;
  lastRunUtc?: string | null;
  lastRowCount?: number | null;
  lastBlobPath?: string | null;
  intervalMinutes?: number | null;
};

export type PagedResult<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
};

// ── services ─────────────────────────────────────────────────────────────

export const reportsApi = {
  list: async (): Promise<ReportDto[]> => api<ReportDto[]>("/api/reports"),
  get: async (key: string): Promise<ReportDto> => api<ReportDto>(`/api/reports/${key}`),
  register: (body: RegisterReportBody) => api<ReportDto>("/api/reports", { method: "POST", body: JSON.stringify(body) }),
  run: (key: string, page: number, pageSize: number, bypassCache: boolean, etag?: string) => {
    const headers: Record<string, string> = {};
    if (etag) {
      headers["If-None-Match"] = `"${etag}"`;
    }

    return apiMaybeNotModified(`/api/reports/${key}/query`, {
      method: "POST",
      headers,
      body: JSON.stringify({ page, pageSize, bypassCache }),
    });
  },
};

export const sourcesApi = {
  list: async (): Promise<SourceDto[]> => api<SourceDto[]>("/api/sources"),
  test: async (key: string): Promise<SourceHealthDto> =>
    api<SourceHealthDto>(`/api/sources/${key}/test`, { method: "POST" }),
  setEnabled: (key: string, enabled: boolean) =>
    apiVoid(`/api/sources/${key}/${enabled ? "enable" : "disable"}`, { method: "POST" }),
};

export const syncApi = {
  run: async (key: string): Promise<SyncRunDto> =>
    api<SyncRunDto>(`/api/sync/run/${key}`, { method: "POST" }),
  runDue: () => apiVoid("/api/sync/run-due", { method: "POST" }),
  runs: async (reportKey?: string): Promise<PagedResult<SyncRunDto>> =>
    api<PagedResult<SyncRunDto>>(
      `/api/sync/runs?page=1&pageSize=50${reportKey ? `&reportKey=${encodeURIComponent(reportKey)}` : ""}`,
    ),
  dashboard: async (): Promise<SyncTileDto[]> => api<SyncTileDto[]>("/api/sync/dashboard"),
};

export type RegisterReportBody = {
  reportKey: string;
  displayName: string;
  sourceKey: string;
  queryText: string;
  allowedRoles: string[];
  description?: string | null;
  isCacheable?: boolean;
  cacheSecondsTtl?: number;
  syncIntervalMinutes?: number | null;
};

export { downloadCsv };
