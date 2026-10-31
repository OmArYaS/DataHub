import { useEffect, useState } from "react";
import { Link } from "react-router-dom";

import { useAuth } from "../core/auth";
import { describeError } from "../core/auth";
import { reportsApi, sourcesApi, syncApi } from "../core/dataApi";
import type { ReportDto, SourceDto, SyncTileDto } from "../core/dataApi";

/** The single landing view: role-aware KPIs + one tile per sync cadence. */
export function DashboardPage() {
  const { user } = useAuth();
  const [reports, setReports] = useState<ReportDto[]>([]);
  const [sources, setSources] = useState<SourceDto[]>([]);
  const [tiles, setTiles] = useState<SyncTileDto[] | null>(null);
  const [tileNote, setTileNote] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let keep = true;

    const load = async () => {
      setError("");
      setTileNote("");
      setLoading(true);

      const jobs = [
        reportsApi.list().then(
          (data) => (keep ? setReports(data) : undefined),
          (err) => (keep ? setError(`Reports: ${describeError(err)}`) : undefined),
        ),
        sourcesApi.list().then(
          (data) => (keep ? setSources(data) : undefined),
          (err) => (keep ? setError(`Sources: ${describeError(err)}`) : undefined),
        ),
      ];

      // Viewer never holds the analyst policy; the tiles stay absent, not broken.
      jobs.push(
        syncApi.dashboard().then(
          (data) => (keep ? setTiles(data) : undefined),
          () => (keep ? setTileNote("Sync tiles need the analyst role.") : undefined),
        ),
      );

      await Promise.all(jobs);
      if (keep) {
        setLoading(false);
      }
    };

    void load();
    return () => {
      keep = false;
    };
  }, []);

  const cacheable = reports.filter((report) => report.isCacheable).length;
  const failingSources = sources.filter((source) => !source.isEnabled).length;
  const failingRuns = (tiles ?? []).filter((tile) => tile.lastStatus && tile.lastStatus !== "completed").length;

  return (
    <>
      <div className="page-header">
        <h1>Welcome back, {user?.userName}</h1>
        <p>
          DataHub federates the warehouse, the retail MySQL store and finance Azure SQL
          behind one role-gated API.
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
      {loading ? <div className="loading">Loading dashboard…</div> : null}

      <div className="kpi-grid">
        <div className="card kpi">
          <div className="label">Reports you can run</div>
          <div className="value">{reports.length}</div>
          <div className="sub">
            of the full catalog — {reports.length === 0 ? "none granted yet" : "role grants applied server-side"}
          </div>
        </div>
        <div className="card kpi">
          <div className="label">Cache-backed reports</div>
          <div className="value">{cacheable}</div>
          <div className="sub">Same stamps serve 304 Not Modified.</div>
        </div>
        <div className="card kpi">
          <div className="label">Data sources</div>
          <div className="value">
            {sources.length - failingSources}
            <span className={failingSources > 0 ? "neg" : "pos"}>
              {" / "}
              {sources.length}
            </span>
          </div>
          <div className="sub">
            {failingSources === 0 ? "all enabled" : `${failingSources} disabled`}
          </div>
        </div>
        <div className="card kpi">
          <div className="label">Sync cadences</div>
          <div className="value">{tiles ? tiles.length : "—"}</div>
          <div className="sub">
            {tiles
              ? failingRuns === 0
                ? "all latest runs completed"
                : `${failingRuns} needing attention`
              : tileNote}
          </div>
        </div>
      </div>

      {tiles && tiles.length > 0 ? (
        <div className="card">
          <h2>Sync cadence state</h2>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Report</th>
                  <th>Interval</th>
                  <th>Last run</th>
                  <th>Rows</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {tiles.map((tile) => (
                  <tr key={tile.reportKey}>
                    <td>
                      <Link to={`/reports/${tile.reportKey}`} className="mono">
                        {tile.reportKey}
                      </Link>
                    </td>
                    <td>{tile.intervalMinutes ? `every ${tile.intervalMinutes} min` : <span className="muted">not configured</span>}</td>
                    <td>{tile.lastRunUtc ? new Date(tile.lastRunUtc).toLocaleString() : <span className="muted">never</span>}</td>
                    <td>{tile.lastRowCount ?? "—"}</td>
                    <td>
                      {tile.lastStatus ? (
                        <span className={`badge ${tile.lastStatus === "completed" ? "done" : "fail"}`}>
                          {tile.lastStatus}
                        </span>
                      ) : (
                        <span className="muted">no runs yet</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}

      {!loading && reports.length > 0 ? (
        <div className="card mt">
          <h2>Jump back in</h2>
          <div className="kpi-grid">
            {reports.slice(0, 4).map((report) => (
              <Link key={report.reportKey} to={`/reports/${report.reportKey}`} className="card kpi">
                <div className="label">{report.sourceKey}</div>
                <div className="value" style={{ fontSize: "1.02rem" }}>
                  {report.displayName}
                </div>
                <div className="sub">{report.isCacheable ? `cache ${report.cacheSecondsTtl}s` : "always live"}</div>
              </Link>
            ))}
          </div>
        </div>
      ) : null}
    </>
  );
}
