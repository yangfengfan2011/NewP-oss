# 快速验证

快速验证平台骨架是否正常工作。

## 验证步骤

### 1. 后端验证

```bash
# 构建后端
dotnet build ICP-Platform.sln

# 运行 Host API
cd apps/host-api
dotnet run

# 访问健康检查端点
curl http://localhost:5000/api/health
```

### 2. 前端验证

```bash
# 构建前端
cd frontend
pnpm build

# 启动开发服务器
pnpm dev

# 访问 http://localhost:3000
```

### 3. Phase 2 闭环验证（权限保护流程）

完整验证认证授权闭环：

1. **登录** - 访问 http://localhost:3000，使用有效凭据登录系统
2. **获取当前用户** - 登录后自动调用 `/api/auth/current-user` 获取用户信息和权限
3. **进入 Shell** - 成功进入主应用 Shell，菜单根据权限动态渲染
4. **访问受保护页面** - 导航至 Demo 模块的列表页面（需要 `Demo.View` 权限），验证页面正常显示
5. **登出并验证限制** - 点击登出，清除认证状态后，尝试访问受保护页面应被重定向到登录页

## 预期结果

- 后端构建成功，无错误
- 前端构建成功，无错误
- Host API 可以启动并响应健康检查
- 前端 Shell 可以在浏览器中正常显示
- 认证授权流程完整闭环：登录 → 权限验证 → 访问控制 → 登出限制

## 当前状态

- ✅ 后端构建通过
- ✅ 前端构建通过
- ✅ Phase 2 认证授权闭环完成
- ⏳ 运行时验证（需要手动执行）
