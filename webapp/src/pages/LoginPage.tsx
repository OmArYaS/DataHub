import { useState } from "react";
import { Navigate, useNavigate } from "react-router-dom";

import { describeError, useAuth } from "../core/auth";

type DemoUser = {
  userName: string;
  roles: string[];
  hint: string;
};

const DEMO_USERS: readonly DemoUser[] = [
  { userName: "nadia", roles: ["admin"], hint: "Full control: register reports, sources, sync." },
  { userName: "lucas", roles: ["analyst"], hint: "Runs finance reports, watches sync jobs." },
  { userName: "sara", roles: ["viewer"], hint: "One dashboard report, read-only." },
];

/** Demo credential screen — production swaps in the identity provider. */
export function LoginPage() {
  const { user, busy, login } = useAuth();
  const navigate = useNavigate();
  const [error, setError] = useState("");

  if (user) {
    return <Navigate to="/" replace />;
  }

  const signIn = async (userName: string) => {
    setError("");
    try {
      await login(userName);
      navigate("/", { replace: true });
    } catch (err) {
      setError(describeError(err));
    }
  };

  return (
    <div className="login-wrap">
      <div className="card login-card">
        <div className="brand" style={{ padding: 0 }}>
          <span className="logo">DH</span>
          <span>
            DataHub
            <span className="tagline">one API over every warehouse</span>
          </span>
        </div>

        {error ? (
          <div className="error-banner">
            <span>{error}</span>
            <button className="btn ghost small" onClick={() => setError("")}>
              Dismiss
            </button>
          </div>
        ) : null}

        <p className="fine-print">
          Pick a demo identity. The server issues a JWT carrying role claims; reports,
          sources and sync endpoints all gate on those claims.
        </p>

        <div className="demo-grid">
          {DEMO_USERS.map((demo) => (
            <button
              key={demo.userName}
              className="btn ghost demo-user"
              disabled={busy}
              onClick={() => signIn(demo.userName)}
            >
              <span>
                <strong>{demo.userName}</strong>
                <span className="demo-hint muted">{demo.hint}</span>
              </span>
              <span className={`badge ${demo.roles[0]}`}>{demo.roles[0]}</span>
            </button>
          ))}
        </div>

        {busy ? <p className="muted">Signing in…</p> : null}
      </div>
    </div>
  );
}
