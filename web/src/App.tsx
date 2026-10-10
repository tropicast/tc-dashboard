import { QueryClientProvider } from '@tanstack/react-query';
import { Route, Routes } from 'react-router-dom';
import { Account } from './components/organisms/account/Account';
import {
  AcceptInvite,
  EmailAction,
  Login,
  Reset,
  Signup,
} from './components/organisms/auth/AuthPages';
import { Home } from './components/organisms/Home';
import { ErrorBoundary } from './components/organisms/layout/ErrorBoundary';
import { AuthGuard } from './components/organisms/layout/AuthGuard';
import { AppShell } from './components/organisms/layout/AppShell';
import { StationDetail } from './components/organisms/stations/StationDetail';
import { Stations } from './components/organisms/stations/Stations';
import './dashboard.css';
import './lib/i18n';
import { queryClient } from './lib/query-client';

function ProtectedRoutes() {
  return (
    <AuthGuard>
      <AppShell>
        <Routes>
          <Route index element={<Home />} />
          <Route path="stations" element={<Stations />} />
          <Route path="stations/:id" element={<StationDetail />} />
          <Route path="account" element={<Account />} />
        </Routes>
      </AppShell>
    </AuthGuard>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ErrorBoundary>
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route path="/signup" element={<Signup />} />
          <Route path="/confirm-email" element={<EmailAction kind="confirm" />} />
          <Route path="/forgot-password" element={<EmailAction kind="forgot" />} />
          <Route path="/reset-password" element={<Reset />} />
          <Route path="/invitations/accept" element={<AcceptInvite />} />
          <Route path="*" element={<ProtectedRoutes />} />
        </Routes>
      </ErrorBoundary>
    </QueryClientProvider>
  );
}
