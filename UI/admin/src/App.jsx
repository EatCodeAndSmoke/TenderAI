import Sidebar from './components/Sidebar.jsx';
import Toast, { useToast } from './components/Toast.jsx';
import CustomersPage from './pages/CustomersPage.jsx';
import TendersPage from './pages/TendersPage.jsx';
import { Navigate, Route, Routes } from 'react-router-dom';

export default function App() {
  const [toast, showToast] = useToast();
  return (
    <>
      <Sidebar />
      <main className="px-4 py-8 sm:px-8 md:ml-60 md:px-8 md:py-10">
        <div className="mx-auto max-w-6xl"><Routes>
          <Route path="/customers" element={<CustomersPage onToast={showToast} />} />
          <Route path="/tenders" element={<TendersPage onToast={showToast} />} />
          <Route path="*" element={<Navigate to="/customers" replace />} />
        </Routes></div>
      </main>
      <Toast {...toast} />
    </>
  );
}
