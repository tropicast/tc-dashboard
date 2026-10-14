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
import { DeviceApproval } from './components/organisms/device/DeviceApproval';
import { Home } from './components/organisms/Home';
import { ErrorBoundary } from './components/organisms/layout/ErrorBoundary';
import { AuthGuard } from './components/organisms/layout/AuthGuard';
import { AppShell } from './components/organisms/layout/AppShell';
import { LegalPage } from './legal/LegalPage';
import { StationDetail } from './components/organisms/stations/StationDetail';
import { Stations } from './components/organisms/stations/Stations';
import '@/styles/globals.css';
import { RouteProvider } from '@/providers/route-provider';
import { ThemeProvider } from '@/providers/theme-provider';
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
          <Route path="device" element={<DeviceApproval />} />
        </Routes>
      </AppShell>
    </AuthGuard>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <RouteProvider>
          <ErrorBoundary>
            <Routes>
              <Route path="/login" element={<Login />} />
              <Route path="/signup" element={<Signup />} />
              <Route path="/confirm-email" element={<EmailAction kind="confirm" />} />
              <Route path="/forgot-password" element={<EmailAction kind="forgot" />} />
              <Route path="/reset-password" element={<Reset />} />
              <Route path="/invitations/accept" element={<AcceptInvite />} />
              <Route path="/privacy" element={<LegalPage document="privacy" />} />
              <Route path="/terms" element={<LegalPage document="terms" />} />
              <Route path="*" element={<ProtectedRoutes />} />
            </Routes>
          </ErrorBoundary>
        </RouteProvider>
      </ThemeProvider>
    </QueryClientProvider>
  );
}
