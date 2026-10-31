import { useCallback, useEffect, useState } from "react";

import { describeError, useAuth } from "../core/auth";
import { syncApi } from "../core/dataApi";
import type { SyncRunDto, SyncTileDto } from "../core/dataApi";

/**
 * The Blob-sync control room: one tile per cadence plus the ledger trail.
 * Analysts read; triggering (run + run-due) stays an admin action â€” the
 * buttons disappear rather than serving 403s.
 */
export function SyncPage() {
  const { isAdmin } = useAuth();
  const [tiles, setTiles] = useState<SyncTileDto[]>([]);
  const [runs, setRuns] = useState<SyncRunDto[]>([]);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setError("");
    setLoading(true);
    try {
      const [tileData, runData] = await Promise.all([syncApi.dashboard(), syncApi.runs()]);
      setTiles(tileData);
      setRuns(runData.items);
    } catch (err) {
      setError(describeError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const triggerOne = async (reportKey: string) => {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const run = await syncApi.run(reportKey);
      setNotice(`${reportKey}: ${run.status} Â· ${run.rowCount} rows â†’ ${run.blobPath}`);
      await load();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusy(false);
    }
  };

  const triggerDue = async () => {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await syncApi.runDue();
      setNotice("run-due sweep finished â€” overdue cadences were pulled.");
      await load();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <div className="page-header">
        <h1>Sync jobs</h1>
        <p>
          Cadence-declared reports are pulled page-wise into Blob Storage as CSV
          partitions; the ledger records every attempt.
        </p>
      </div>

      {error ? (
        <div className="error-banner">
          <span>{error}</span>
          <button className="btn ghost small" onClick={() => setError("")}>
            Dismiss
          </button>
        </div>
      ) : null}
      {notice ? (
        <div className="notice">
          {notice}
          <button className="btn ghost small" style={{ marginLeft: "1rem" }} onClick={() => setNotice("")}>
            Dismiss
          </button>
        </div>
      ) : null}

      <div className="kpi-grid">
        {tiles.map((tile) => (
          <div key={tile.reportKey} className="card kpi">
            <div className="label mono">{tile.reportKey}</div>
            <div className="value" style={{ fontSize: "1.05rem" }}>
              {tile.lastStatus ? (
                <span className={`badge ${tile.lastStatus === "completed" ? "done" : "fail"}`}>
                  {tile.lastStatus}
                </span>
              ) : (
                <span className="muted">no runs yet</span>
              )}
            </div>
            <div className="sub">
              every {tile.intervalMinutes ?? "â€”"} min
              {tile.lastRunUtc ? ` Â· last ${new Date(tile.lastRunUtc).toLocaleString()}` : ""}
              {tile.lastRowCount !== null && tile.lastRowCount !== undefined ? ` Â· ${tile.lastRowCount} rows` : ""}
            </div>
            {tile.lastBlobPath ? (
              <div className="sub mono" style={{ wordBreak: "break-all" }}>
                {tile.lastBlobPath}
              </div>
            ) : null}
            {isAdmin ? (
              <button
                className="btn small mt"
                style={{ marginTop: ".8rem" }}
                disabled={busy}
                onClick={() => void triggerOne(tile.reportKey)}
              >
                Run now
              </button>
            ) : null}
          </div>
        ))}
      </div>

      {isAdmin ? (
        <div className="toolbar">
          <div className="actions">
            <button className="btn" disabled={busy} onClick={() => void triggerDue()}>
              Run-due sweep
            </button>
            <span className="muted">
              pulls every cadence whose window has elapsed â€” the scheduler's logic on demand
            </span>
          </div>
        </div>
      ) : null}

      <div className="mt" />

      <h2>Ledger</h2>
      {loading ? (
        <div className="loading">Loading runsâ€¦</div>
      ) : runs.length === 0 ? (
        <div className="card empty">No sync runs recorded.</div>
      ) : (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>#</th>
                <th>Report</th>
                <th>Status</th>
                <th>Rows</th>
                <th>Blob path</th>
                <th>Triggered by</th>
                <th>Started</th>
                <th>Completed</th>
              </tr>
            </thead>
            <tbody>
              {runs.map((run) => (
                <tr key={run.id}>
                  <td>{run.id}</td>
                  <td>
                    <span className="mono">{run.reportKey}</span>
                  </td>
                  <td>
                    <span className={`badge ${run.status === "completed" ? "done" : "fail2"}`}>
                      {run.status}
                    </span>
                    {run.failureCode ? <div className="sub neg mono">{run.failureCode}</div> : null}
                  </td>
                  <td>{run.rowCount}</td>
                  <td>
                    <span className="mono" style={{ fontSize: ".78rem", wordBreak: "break-all" }}>
                      {run.blobPath}
                    </span>
                  </td>
                  <td className="mono">{run.triggeredBy}</td>
                  <td>{new Date(run.startedUtc).toLocaleString()}</td>
                  <td>{run.completedUtc ? new Date(run.completedUtc).toLocaleString() : <span className="muted">â€”</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
