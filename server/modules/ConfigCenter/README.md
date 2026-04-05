# ConfigCenter Module

閰嶇疆涓績妯″潡 - 骞冲彴缁熶竴閰嶇疆绠＄悊

## 鍔熻兘鐗规€?
- 閰嶇疆椤圭粺涓€绠＄悊
- 鐜绾ч厤缃殧绂伙紙Development銆丳roduction 绛夛級
- 绉熸埛绾ч厤缃殧绂?- 閰嶇疆浼樺厛绾э細绉熸埛+鐜 > 鐜 > 绉熸埛 > 鍏ㄥ眬
- 鏁忔劅閰嶇疆淇濇姢锛堝瘑鐮併€佸瘑閽ョ瓑鑷姩鑴辨晱锛?- 閰嶇疆鍒嗙粍绠＄悊
- 閰嶇疆鍙樻洿瀹¤锛堝緟瀹炵幇锛?
## 鍚庣 API

### 鑾峰彇鎵€鏈夐厤缃?```
GET /api/config?environment={env}&tenantId={tenant}
```

### 鑾峰彇鍒嗙粍閰嶇疆
```
GET /api/config/grouped?environment={env}&tenantId={tenant}
```

### 鑾峰彇鍗曚釜閰嶇疆
```
GET /api/config/{key}?environment={env}&tenantId={tenant}
```

### 鎸夋ā鍧楄幏鍙栭厤缃?```
GET /api/config/module/{moduleId}?environment={env}&tenantId={tenant}
```

### 鍒涘缓閰嶇疆
```
POST /api/config
Body: ConfigItemCreateDto
```

### 鏇存柊閰嶇疆
```
PUT /api/config/{id}
Body: ConfigItemUpdateDto
```

### 鍒犻櫎閰嶇疆
```
DELETE /api/config/{id}
```

## 鍓嶇椤甸潰

璁块棶璺緞锛歚/config/items`

鏉冮檺瑕佹眰锛歚ConfigCenter.Config.View`

## 鏉冮檺鐐?
- `ConfigCenter.Config.View` - 鏌ョ湅閰嶇疆
- `ConfigCenter.Config.Create` - 鍒涘缓閰嶇疆
- `ConfigCenter.Config.Edit` - 缂栬緫閰嶇疆
- `ConfigCenter.Config.Delete` - 鍒犻櫎閰嶇疆

## 閰嶇疆妯″瀷

```csharp
public class ConfigItem
{
    public string Id { get; set; }
    public string Key { get; set; }
    public string? Value { get; set; }
    public string Type { get; set; }
    public string ModuleId { get; set; }
    public string? Group { get; set; }
    public string? Environment { get; set; }
    public string? TenantId { get; set; }
    public string DisplayName { get; set; }
    public string? Description { get; set; }
    public bool IsSensitive { get; set; }
}
```

## 浣跨敤绀轰緥

### 妯″潡澹版槑閰嶇疆鏋舵瀯

```csharp
public IReadOnlyList<ConfigDescriptor> GetConfigSchema()
{
    return new[]
    {
        new ConfigDescriptor
        {
            Key = "MyModule.Setting1",
            DisplayName = "璁剧疆1",
            Type = "String",
            DefaultValue = "default",
            ModuleId = "MyModule"
        }
    };
}
```

### 璇诲彇閰嶇疆

```csharp
var config = await _configService.GetConfigAsync(
    "MyModule.Setting1", 
    environment: "Production", 
    tenantId: "tenant1"
);
```

## 娴嬭瘯

杩愯娴嬭瘯锛?```bash
dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj
```

## 褰撳墠瀹炵幇

- 鍐呭瓨瀛樺偍锛堝緟鏇挎崲涓烘暟鎹簱瀹炵幇锛?- 鍩虹 CRUD 鎿嶄綔
- 鐜鍜岀鎴烽殧绂?- 閰嶇疆浼樺厛绾цВ鏋?- 鏁忔劅閰嶇疆鑴辨晱

## 鍙傝€?
鍙傝€?`docs/modules/configcenter-module-boundary-v1.md` 浜嗚В妯″潡杈圭晫瀹氫箟銆?
