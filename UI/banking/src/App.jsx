import { Navigate, Route, Routes } from 'react-router-dom';
import { useSession } from './session.jsx';
import Shell from './components/Shell.jsx';
import LoginPage from './pages/LoginPage.jsx';
import HomePage from './pages/HomePage.jsx';
import NotificationsPage from './pages/NotificationsPage.jsx';
import LoanPage from './pages/LoanPage.jsx';
import TendersPage from './pages/TendersPage.jsx';

// Routes: #/login, #/home, #/notifications, #/loan/<lotId>
export default function App() {
  const { customerId } = useSession();

  return (
    <Routes>
      <Route path="/login" element={customerId ? <Navigate to="/home" replace /> : <LoginPage />} />
      <Route element={customerId ? <Shell /> : <Navigate to="/login" replace />}>
        <Route path="/home" element={<HomePage />} />
        <Route path="/notifications" element={<NotificationsPage />} />
        <Route path="/tenders" element={<TendersPage />} />
        <Route path="/loan/:lotId" element={<LoanPage />} />
        <Route path="*" element={<Navigate to="/home" replace />} />
      </Route>
    </Routes>
  );
}
