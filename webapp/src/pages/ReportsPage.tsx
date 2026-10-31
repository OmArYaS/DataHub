import { useEffect, useState } from "react";
import { Link } from "react-router-dom";

import { describeError } from "../core/auth";
import { reportsApi } from "../core/dataApi";
import type { ReportDto } from "../core/dataApi";

/** Role-filtered catalog. Grants come from the server, not the client. */
export function ReportsPage() {
  const [reports, setReports] = useState<ReportDto[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let keep = true;

    void reportsApi
      .list()
      .then((data) => {
        if (keep) {
          setReports(data);
        }
      })
      .catch((err) => {
        if (keep) {
          setError(describeError(err));
        }
      })
      .finally(() => {
        if (keep) {
          setLoading(false);
        }
      });

    return () => {
      keep = false;
    };
  }, []);

  return (
    <>
      <div className="page-header">
        <h1>Report catalog</h1>
        <p>Every row runs the same contract: role check, guard battery, cache stamp, ETag.</p>
      </div>

      {error ? (
        <div className="error-banner">
          <span>{error}</span>
          <button className="btn ghost small" onClick={() => setError("")}>
            Dismiss
          </button>
        </div>
      ) : null}
      {loading ? <div className="loading">Loading catalog…</div> : null}

      {!loading && !error && reports.length === 0 ? (
        <div className="card empty">Your roles have no granted reports.</div>
      ) : null}

      {!loading && reports.length > 0 ? (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Report</th>
                <th>Source</th>
                <th>Granted roles</th>
                <th>Caching</th>
                <th>Sync cadence</th>
              </tr>
            </thead>
            <tbody>
              {reports.map((report) => (
                <tr key={report.reportKey}>
                  <td>
                    <Link to={`/reports/${report.reportKey}`} className="mono">
                      {report.reportKey}
                    </Link>
                    <div className="muted" style={{ fontSize: ".8rem" }}>
                      {report.displayName}
                      {report.description ? ` — ${report.description}` : ""}
                    </div>
                  </td>
                  <td className="mono">{report.sourceKey}</td>
                  <td>
                    <span className="chips" style={{ gap: ".3rem" }}>
                      {report.allowedRoles.map((role) => (
                        <span key={role} className={`badge ${role}`}>
                          {role}
                        </span>
                      ))}
                    </span>
                  </td>
                  <td>
                    {report.isCacheable ? (
                      <span className="badge hit">{report.cacheSecondsTtl}s TTL</span>
                    ) : (
                      <span className="badge warn">no cache</span>
                    )}
                  </td>
                  <td>
                    {report.syncIntervalMinutes ? (
                      <>every {report.syncIntervalMinutes} min</>
                    ) : (
                      <span className="muted">—</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </>
  );
}
