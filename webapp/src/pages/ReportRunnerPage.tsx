import { useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";

import { describeError } from "../core/auth";
import { downloadCsv } from "../core/api";
import { reportsApi } from "../core/dataApi";
import type { ReportDto, ReportResultDto } from "../core/dataApi";

const PAGE_SIZES: readonly number[] = [25, 50, 100, 200];

/**
 * The runner: one report, page controls, the cache MISS/HIT badge, and the
 * ETag round-trip — a re-send with If-None-Match answering 304 keeps the
 * table already on screen, exactly as the API contract promises.
 */
export function ReportRunnerPage() {
  const { reportKey = "" } = useParams();
  const [report, setReport] = useState<ReportDto | null>(null);
  const [result, setResult] = useState<ReportResultDto | null>(null);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(100);
  const [bypassCache, setBypassCache] = useState(false);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [notModified, setNotModified] = useState(false);

  const run = useCallback(
    async (targetPage: number, targetPageSize: number, sendEtag: boolean) => {
      setBusy(true);
      setError("");
      setNotModified(false);

      try {
        const etag = sendEtag && result ? result.etag : undefined;
        const response = await reportsApi.run(reportKey, targetPage, targetPageSize, bypassCache, etag);

        if (response.notModified) {
          // 304: the server matched our stamp — the current table stays valid.
          setNotModified(true);
          return;
        }

        setResult(response.data as ReportResultDto);
      } catch (err) {
        setError(describeError(err));
      } finally {
        setBusy(false);
      }
    },
    [bypassCache, reportKey, result],
  );

  useEffect(() => {
    let keep = true;

    setReport(null);
    setResult(null);
    setPage(1);
    setError("");

    void reportsApi
      .get(reportKey)
      .then((data) => {
        if (keep) {
          setReport(data);
        }
      })
      .catch((err) => {
        if (keep) {
          setError(describeError(err));
        }
      });

    void run(1, 100, false).catch(() => undefined);

    return () => {
      keep = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reportKey]);

  const nextPage = () => {
    const target = page + 1;
    setPage(target);
    void run(target, pageSize, false);
  };

  const prevPage = () => {
    if (page <= 1) {
      return;
    }
    const target = page - 1;
    setPage(target);
    void run(target, pageSize, false);
  };

  const revalidate = () => {
    // Same page, same stamp: a 200 here means content changed since last view.
    void run(page, pageSize, true);
  };

  const refreshHard = () => {
    setPage(page);
    setNotModified(false);
    void run(page, pageSize, false);
  };

  const exportCsv = () => {
    if (!result || !report) {
      return;
    }
    downloadCsv(`${report.reportKey}-page${result.page}.csv`, result.columns, result.rows);
  };

  const totalPages = result ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : null;
  const atFirst = page <= 1;
  const atLast = totalPages !== null && page >= totalPages;

  return (
    <>
      <div className="page-header">
        <h2 style={{ marginBottom: ".2rem" }}>
          <Link to="/reports">Catalog</Link> <span className="muted">/</span>{" "}
          <span className="mono">{reportKey}</span>
        </h2>
        <p>{report ? `${report.displayName} · source ${report.sourceKey}` : "loading metadata…"}</p>
      </div>

      {error ? (
        <div className="error-banner">
          <span>{error}</span>
          <button className="btn ghost small" onClick={() => setError("")}>
            Dismiss
          </button>
        </div>
      ) : null}
      {notModified ? (
        <div className="notice">
          304 Not Modified — the ETag still matches, so the table below is already current.
        </div>
      ) : null}

      <div className="toolbar">
        <div>
          <label>Page size</label>
          <select
            value={pageSize}
            onChange={(event) => {
              const size = Number(event.target.value);
              setPageSize(size);
              setPage(1);
              void run(1, size, false);
            }}
          >
            {PAGE_SIZES.map((size) => (
              <option key={size} value={size}>
                {size} rows
              </option>
            ))}
          </select>
        </div>
        <div>
          <label>Cache</label>
          <select
            value={bypassCache ? "bypass" : "use"}
            onChange={(event) => setBypassCache(event.target.value === "bypass")}
          >
            <option value="use">Allow cache</option>
            <option value="bypass">Bypass cache</option>
          </select>
        </div>
        <div className="actions">
          <button className="btn" disabled={busy} onClick={refreshHard}>
            Run
          </button>
          <button className="btn ghost" disabled={busy || !result} onClick={revalidate}>
            Revalidate (If-None-Match)
          </button>
          <button className="btn ghost" disabled={busy || !result} onClick={exportCsv}>
            Export CSV
          </button>
        </div>
      </div>

      {result ? (
        <>
          <div className="toolbar" style={{ alignItems: "center" }}>
            <span className={`badge ${result.fromCache ? "hit" : "miss"}`}>
              {result.fromCache ? "CACHE HIT" : "CACHE MISS"}
            </span>
            <span className="mono muted">
              etag <span style={{ color: "var(--c-text)" }}>"{result.etag}"</span>
            </span>
            <span className="spacer" />
            <span className="muted">
              {result.fromCache ? "served from the stamp cache" : `${result.elapsedMilliseconds} ms from the source`}
            </span>
          </div>

          <div className={busy ? "loading" : "muted"} style={busy ? undefined : { padding: 0 }}>
            {busy ? "Running…" : ""}
          </div>

          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  {result.columns.map((column) => (
                    <th key={column}>{column}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {result.rows.map((row, rowIndex) => (
                  <tr key={rowIndex}>
                    {row.map((cell, cellIndex) => (
                      <td key={cellIndex}>{cell === null ? <span className="muted">null</span> : String(cell)}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="pager">
            <button className="btn ghost small" disabled={atFirst || busy} onClick={prevPage}>
              ← Previous
            </button>
            <span className="muted">
              page {result.page} / {totalPages} · {result.totalCount} rows total
            </span>
            <button className="btn ghost small" disabled={atLast || busy} onClick={nextPage}>
              Next →
            </button>
          </div>
        </>
      ) : !busy && !error ? (
        <div className="empty">No result yet — press Run.</div>
      ) : null}
    </>
  );
}
