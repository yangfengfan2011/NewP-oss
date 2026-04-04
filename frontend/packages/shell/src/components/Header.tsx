import { useAuth } from '../contexts/AuthContext';

export function Header() {
  const auth = useAuth();

  return (
    <header style={{
      padding: '0 1.5rem',
      borderBottom: '1px solid #e8e8e8',
      backgroundColor: '#fff',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      height: '64px'
    }}>
      <div style={{ display: 'flex', alignItems: 'center' }}>
        <h1 style={{ margin: 0, fontSize: '1.5rem', fontWeight: 600 }}>ICP Platform</h1>
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
        {auth.isAuthenticated && auth.user && (
          <>
            <span>欢迎, {auth.user.displayName}</span>
            <button
              onClick={() => auth.logout()}
              style={{
                padding: '0.5rem 1rem',
                border: '1px solid #d9d9d9',
                borderRadius: '4px',
                backgroundColor: '#fff',
                cursor: 'pointer'
              }}
            >
              退出
            </button>
          </>
        )}
      </div>
    </header>
  );
}
