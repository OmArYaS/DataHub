import { NavLink, Outlet, useNavigate } from "react-router-dom";

import { useAuth } from "../core/auth";

/** The authenticated shell: sidenav + outlet + user chip. */
export function Layout() {
  const { user, isAdmin, hasRole, logout } = useAuth();
  const navigate = useNavigate();

  const signOut = () => {
    logout();
    navigate("/login", { replace: true });
  };

  return (
    <div className="app-shell">
      <aside className="sidenav">
        <div className="brand">
          <span className="logo">DH</span>
          <span>
            DataHub
            <span className="tagline">federated reporting</span>
          </span>
        </div>
        <nav>
          <div className="section">Reports</div>
          <NavLink to="/" end className={({ isActive }) => (isActive ? "active" : "")}>
            Dashboard
          </NavLink>
          <NavLink to="/reports" className={({ isActive }) => (isActive ? "active" : "")}>
            Catalog
          </NavLink>
          {isAdmin ? <div className="section">Administration</div> : null}
          {isAdmin ? (
            <NavLink to="/admin/sources" className={({ isActive }) => (isActive ? "active" : "")}>
              Sources
            </NavLink>
          ) : null}
          {hasRole("analyst") ? (
            <NavLink to="/admin/sync" className={({ isActive }) => (isActive ? "active" : "")}>
              Sync jobs
            </NavLink>
          ) : null}
        </nav>
        <div className="user-chip">
          <div className="who">
            <span>
              {user?.userName}{" "}
              <span className="muted mono">· {user?.userKey}</span>
            </span>
          </div>
          <div className="chips" style={{ gap: ".3rem" }}>
            {(user?.roles ?? []).map((role) => (
              <span key={role} className={`badge ${role}`}>
                {role}
              </span>
            ))}
          </div>
          <button className="btn ghost small" onClick={signOut}>
            Sign out
          </button>
        </div>
      </aside>
      <main className="content">
        <Outlet />
      </main>
    </div>
  );
}
