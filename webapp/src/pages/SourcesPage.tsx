import { useCallback, useEffect, useState } from "react";

import { describeError } from "../core/auth";
import { sourcesApi } from "../core/dataApi";
import type { SourceDto, SourceHealthDto } from "../core/dataApi";

type RowState = {
  testing: boolean;
  health: SourceHealthDto | null;
  error: string;
};

/** Admin registry view: ping each federated source, flip enablement. */
export function SourcesPage() {
  const [sources, setSources] = useState<SourceDto[]>([]);
  const [rows, setRows] = useState<Record<string, RowState>>({});
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setError("");
    setLoading(true);
    try {
      setSources(await sourcesApi.list());
    } catch (err) {
      setError(describeError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const patchRow = (key: string, next: Partial<RowState>) => {
    setRows((current) => ({
      ...current,
      [key]: { testing: false, health: null, error: "", ...current[key], ...next },
    }));
  };

  const test = async (key: string) => {
    patchRow(key, { testing: true, health: null, error: "" });
    try {
      const health = await sourcesApi.test(key);
      patchRow(key, { testing: false, health });
    } catch (err) {
      patchRow(key, { testing: false, error: describeError(err) });
    }
  };

  const toggle = async (key: string, enabled: boolean) => {
    patchRow(key, { testing: true, health: null, error: "" });
    try {
      await sourcesApi.setEnabled(key, enabled);
      patchRow(key, { testing: false });
      await load();
    } catch (err) {
      patchRow(key, { testing: false, error: describeError(err) });
    }
  };

  return (
    <>
      <div className="page-header">
        <h1>Federated sources</h1>
        <p>
          Each source carries its provider, config slot and timeout. Secrets never cross
          the API — only the configured/not-configured bit does.
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
      {loading ? <div className="loading">Loading registry…</div> : null}

      {!loading && sources.length > 0 ? (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Source</th>
                <th>Provider</th>
                <th>Config slot</th>
                <th>Timeout</th>
                <th>State</th>
                <th style={{ width: 260 }}>Connectivity</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {sources.map((source) => {
                const row = rows[source.sourceKey] ?? { testing: false, health: null, error: "" };
                return (
                  <tr key={source.sourceKey}>
                    <td>
                      <span className="mono">{source.sourceKey}</span>
                      <div className="muted" style={{ fontSize: ".8rem" }}>
                        {source.displayName}
                      </div>
                    </td>
                    <td>{source.provider}</td>
                    <td className="mono">{source.connectionName}</td>
                    <td>{source.commandTimeoutSeconds}s</td>
                    <td>
                      <span className={`badge ${source.isEnabled ? "ok" : "off"}`}>
                        {source.isEnabled ? "enabled" : "disabled"}
                      </span>
                    </td>
                    <td>
                      {row.health ? (
                        row.health.reachable ? (
                          <span className="badge ok">reachable in {row.health.elapsedMilliseconds} ms</span>
                        ) : (
                          <span className="badge fail" title={row.health.detail ?? undefined}>
                            {row.health.failureCode ?? "UNREACHABLE"}
                          </span>
                        )
                      ) : row.error ? (
                        <span className="badge fail" title={row.error}>
                          error
                        </span>
                      ) : (
                        <span className="muted">not tested</span>
                      )}
                    </td>
                    <td>
                      <span className="chips" style={{ gap: ".4rem" }}>
                        <button
                          className="btn ghost small"
                          disabled={row.testing}
                          onClick={() => void test(source.sourceKey)}
                        >
                          {row.testing ? "…" : "Test"}
                        </button>
                        <button
                          className="btn ghost small"
                          disabled={row.testing || !source.isEnabled}
                          onClick={() => void toggle(source.sourceKey, false)}
                        >
                          Disable
                        </button>
                        <button
                          className="btn ghost small"
                          disabled={row.testing || source.isEnabled}
                          onClick={() => void toggle(source.sourceKey, true)}
                        >
                          Enable
                        </button>
                      </span>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      ) : null}
    </>
  );
}
