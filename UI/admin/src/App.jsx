import Sidebar from './components/Sidebar.jsx';
import Toast, { useToast } from './components/Toast.jsx';
import CustomersPage from './pages/CustomersPage.jsx';

export default function App() {
  const [toast, showToast] = useToast();
  return (
    <>
      <Sidebar />
      <main className="px-4 py-8 sm:px-8 md:ml-60 md:px-8 md:py-10">
        <div className="mx-auto max-w-6xl"><CustomersPage onToast={showToast} /></div>
      </main>
      <Toast {...toast} />
    </>
  );
}
