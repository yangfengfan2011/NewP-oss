# Shell Auth Integration Smoke Test

## 代码路径验证

### 1. 初始化流程
- ✅ AuthProvider mount 时触发 useEffect
- ✅ 调用 GET /api/auth/current-user (credentials: include)
- ✅ 成功：设置 user + isAuthenticated=true
- ✅ 失败：保持未登录状态
- ✅ 最终：setIsInitialized(true)

### 2. 路由守卫
- ✅ ProtectedRoute 检查 isInitialized
- ✅ 未初始化时返回 null（不跳转）
- ✅ 初始化后根据 isAuthenticated 决定跳转

### 3. 登录流程
- ✅ POST /api/auth/login (credentials: include)
- ✅ 成功后 GET /api/auth/current-user
- ✅ 设置 user + isAuthenticated=true
- ✅ 失败时抛出异常，Login 页面显示错误

### 4. 登出流程
- ✅ POST /api/auth/logout (credentials: include)
- ✅ 清空 user + isAuthenticated=false
- ✅ Header 的退出按钮调用 auth.logout()

### 5. 刷新恢复
- ✅ 页面刷新触发 checkCurrentUser
- ✅ 有效 session 恢复登录态
- ✅ 无效 session 保持未登录

## 关键点
- 所有 API 请求都带 credentials: 'include'
- 初始化期间路由守卫不会误跳转
- 错误处理完整（login 失败显示提示）
