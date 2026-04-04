# Host API

后端宿主应用，负责启动和组合所有模块。

## 职责

- 启动 Web 服务
- 加载和组合模块
- 提供认证授权集成
- 路由聚合
- API 网关

## 运行

```bash
cd apps/host-api
dotnet run
```

访问 Swagger: https://localhost:5000/swagger

## 配置

参考 `appsettings.json` 和 `appsettings.Development.json`
