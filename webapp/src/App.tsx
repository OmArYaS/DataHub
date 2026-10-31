import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";

import { AuthProvider, useAuth } from "./core/auth";
import { DashboardPage } from "./pages/DashboardPage";
import { Layout } from "./pages/Layout";
import { LoginPage } from "./pages/LoginPage";
import { ReportRunnerPage } from "./pages/ReportRunnerPage";
import { ReportsPage } from "./pages/ReportsPage";
import { SourcesPage } from "./pages/SourcesPage";
import { SyncPage } from "./pages/SyncPage";

function RequireAuth({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  return user ? <>{children}</> : <Navigate to="/login" replace />;
}

function RequireRole({ role, children }: { role: string; children: React.ReactNode }) {
  const { hasRole } = useAuth();
  return hasRole(role) ? <>{children}</> : <Navigate to="/" replace />;
}

export function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route
            path="/"
            element={
              <RequireAuth>
                <Layout />
              </RequireAuth>
            }
          >
            <Route index element={<DashboardPage />} />
            <Route path="reports" element={<ReportsPage />} />
            <Route path="reports/:reportKey" element={<ReportRunnerPage />} />
            <Route
              path="admin/sources"
              element={
                <RequireRole role="admin">
                  <SourcesPage />
                </RequireRole>
              }
            />
            <Route
              path="admin/sync"
              element={
                <RequireRole role="analyst">
                  <SyncPage />
                </RequireRole>
              }
            />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Route>
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}
