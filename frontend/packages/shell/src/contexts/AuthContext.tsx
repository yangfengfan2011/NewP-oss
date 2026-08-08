import { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { AuthContext as IAuthContext } from '@icp-platform/sdk';

const AuthContext = createContext<IAuthContext | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [user, setUser] = useState<IAuthContext['user']>();
  const [isInitialized, setIsInitialized] = useState(false);

  useEffect(() => {
    // 页面刷新后恢复登录态
    const checkCurrentUser = async () => {
      try {
        const response = await fetch('/api/auth/current-user', {
          method: 'GET',
          headers: {
            'Content-Type': 'application/json',
          },
          credentials: 'include' // 确保携带 cookie
        });

        if (response.ok) {
          const userData = await response.json();
          setUser(userData);
          setIsAuthenticated(true);
        } else {
          setUser(undefined);
          setIsAuthenticated(false);
        }
      } catch (error) {
        console.error('Failed to fetch current user', error);
        setUser(undefined);
        setIsAuthenticated(false);
      } finally {
        setIsInitialized(true);
      }
    };

    checkCurrentUser();
  }, []);

  const login = async (username: string, password: string) => {
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ username, password }),
      credentials: 'include' // 确保携带 cookie 和跨域可用
    });

    if (!response.ok) {
      throw new Error('Login failed');
    }

    // 登录成功后拉取最新用户信息
    const userRes = await fetch('/api/auth/current-user', {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
      },
      credentials: 'include'
    });

    if (userRes.ok) {
      const userData = await userRes.json();
      setUser(userData);
      setIsAuthenticated(true);
    } else {
      throw new Error('Failed to get user info after login');
    }
  };

  const logout = async () => {
    try {
      await fetch('/api/auth/logout', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        credentials: 'include'
      });
    } catch (error) {
      console.error('Logout failed', error);
    } finally {
      setIsAuthenticated(false);
      setUser(undefined);
    }
  };

  const hasPermission = (permission: string): boolean => {
    if (!user) return false;
    return user.permissions.includes(permission);
  };

  const hasAnyPermission = (permissions: string[]): boolean => {
    return permissions.some(p => hasPermission(p));
  };

  const hasAllPermissions = (permissions: string[]): boolean => {
    return permissions.every(p => hasPermission(p));
  };

  return (
    <AuthContext.Provider value={{
      isAuthenticated,
      isInitialized,
      user,
      login,
      logout,
      hasPermission,
      hasAnyPermission,
      hasAllPermissions
    }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within AuthProvider');
  }
  return context;
}
