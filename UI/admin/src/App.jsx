import { Navigate, Route, Routes } from 'react-router-dom';
import Sidebar from './components/Sidebar.jsx';
import Toast, { useToast } from './components/Toast.jsx';
import CustomersPage from './pages/CustomersPage.jsx';
import TendersPage from './pages/TendersPage.jsx';
import MatchesPage from './pages/MatchesPage.jsx';
import { matches } from './data/matches.js';
import { useMatchingRun } from './lib/useMatchingRun.js';

// Pages: #/tenders, #/customers, #/matches
export default function App() {
  const [toast, showToast] = useToast();
  const [run, startMatching] = useMatchingRun(matches.length);

  return (
    <>
      <Sidebar />

      <main className="px-4 py-8 sm:px-8 md:ml-60 md:px-8 md:py-10">
        <div className="mx-auto max-w-6xl">
          <Routes>
            <Route path="/customers" element={<CustomersPage onToast={showToast} />} />
            <Route path="/tenders" element={<TendersPage onToast={showToast} />} />
            <Route path="/matches" element={<MatchesPage run={run} onStart={startMatching} />} />
            <Route path="*" element={<Navigate to="/tenders" replace />} />
          </Routes>
        </div>
      </main>

      <Toast {...toast} />
    </>
  );
}
