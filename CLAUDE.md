# DynamicsAI MCP Server — Project Guide

## Proje Özeti
Dynamics 365 CRM (Online & OnPrem) için çok kiracılı (multi-tenant), yapay zeka destekli MCP Server.
Claude bu dosyayı okuyarak projenin mevcut durumunu anlar — her oturumda tekrar sormaz.

---

## Teknoloji Yığını
- **Runtime:** .NET 8
- **MCP SDK:** ModelContextProtocol (official .NET SDK)
- **DI / Host:** Microsoft.Extensions.Hosting (Generic Host)
- **HTTP:** HttpClient + Dynamics Web API (OData v4)
- **Auth:** Raw HTTP ile AAD token endpoint (MSAL kaldırıldı)
- **Cache:** IMemoryCache → ileride IDistributedCache (Redis)
- **Logging:** Serilog (Console + File sink)
- **Config:** appsettings.json + environment variables
- **Test:** xUnit + Moq

---

## Mimari — Katmanlar

```
DynamicsAI.McpServer/          ← MCP Host + Tool kayıt
DynamicsAI.Application/        ← Use case'ler, servis interface'leri
DynamicsAI.Infrastructure/     ← Dynamics Web API impl, Auth, Cache
DynamicsAI.Domain/             ← Entity model, Value object, Exception
DynamicsAI.Tests/              ← Unit + Integration testler
```

---

## Klasör Yapısı (Güncel)

```
/src
  /DynamicsAI.McpServer
    Program.cs
    Tools/
      MetadataTool.cs
      QueryTool.cs
      CrudTool.cs
      ExportTool.cs              ← dynamics_export_to_excel — tam sayfalama + file:// URI
    appsettings.json

  /DynamicsAI.Application
    Interfaces/
      IDynamicsMetadataService.cs
      IDynamicsQueryService.cs
      IDynamicsCrudService.cs
      IDynamicsExportService.cs
      ICacheService.cs
    DTOs/
      TenantContext.cs
      EntityMetadata.cs
      QueryRequest.cs
      QueryResult.cs
      CrudRequest.cs
      CrudResult.cs
      ExportRequest.cs           ← MaxRecords (int?) + OutputPath alanları mevcut
      ExportResult.cs
    UseCases/
      GetMetadataUseCase.cs
      ExecuteQueryUseCase.cs
      ExecuteCrudUseCase.cs
      ExportToExcelUseCase.cs

  /DynamicsAI.Infrastructure
    Dynamics/
      DynamicsAuthProvider.cs      ← Raw HTTP AAD token, well-known client ID fallback
      DynamicsMetadataService.cs   ← /api/data/v9.2/EntityDefinitions, parallel fetch
      DynamicsQueryService.cs      ← OData GET sorgular + FetchPageAsync (pagination)
      DynamicsCrudService.cs       ← POST/PATCH/DELETE
      DynamicsExportService.cs     ← Sayfalı export + MiniExcel streaming yazım
      DynamicsHttpClientFactory.cs
    Cache/
      MetadataCacheService.cs      ← TTL tabanlı entity schema cache
    Logging/
      AuditLogger.cs

  /DynamicsAI.Domain
    Models/
      TenantConfig.cs
      EntitySchema.cs
      FieldSchema.cs
    Exceptions/
      DynamicsException.cs
      TenantAuthException.cs

  /DynamicsAI.GatewayApi
    Program.cs
    StorageOptions.cs              ← files/exports/ ve files/uploads/ yönetimi
    Controllers/
      ChatController.cs            ← Tek JSON endpoint; opsiyonel base64 dosya desteği
      FilesController.cs           ← GET /api/files (liste), GET /api/files/{id} (indir)
    Data/
      AppDbContext.cs
      Entities/
        SessionEntity.cs
        MessageEntity.cs
        ExportedFileEntity.cs      ← Id | FilePath | Category | CreatedAt
    Models/
      ChatRequest.cs               ← FileAttachment? File (opsiyonel, base64)
      ChatResponse.cs
      ClaudeApiModels.cs
    Services/
      ConversationService.cs       ← Cache + SQLite kalıcı session yönetimi
      ClaudeAgentService.cs        ← Claude API agentic loop; turn sıkıştırma; token koruması
      DynamicsToolExecutor.cs      ← Tool çağrılarını use case'lere iletir; export→exports/ klasörü
      ExportedFileRegistry.cs      ← GUID→(path, category) mapping; bellek + SQLite kalıcı
      FileProcessingService.cs     ← FileAttachment (base64) → Claude content block dönüşümü
    appsettings.json
    conversations.db               ← SQLite veritabanı (runtime'da oluşur, git'e ekleme)

  # Runtime'da oluşan dosya klasörleri (exe yanında):
  files/
    exports/   ← Claude'un ürettiği Excel dosyaları
    uploads/   ← Kullanıcının chat'e yüklediği dosyalar

/tests
  /DynamicsAI.Tests
    DynamicsAuthProviderTests.cs
    AuditLoggerTests.cs
    DistributedCacheServiceTests.cs
    ConversationServiceTests.cs
```

---

## MCP Tool Tanımları

### 1. `dynamics_get_metadata`
**Amaç:** Sadece entity listesini döndürür (logical name, display name, plural name). Field detayı YOK — token tasarrufu için iki adıma bölündü.

### 2. `dynamics_get_entity_fields`
**Amaç:** Belirli bir entity'nin tüm field'larını döndürür. Sorgu/CRUD öncesi çağrılır.

### 3. `dynamics_execute_query`
**Amaç:** OData GET sorgusu. Kayıt listeleme/görüntüleme için kullanılır. `top` ile sonuç sınırlanır.

### 4. `dynamics_get_count`
**Amaç:** Entity kayıt sayısı (filtreli veya filtresiz).

### 5. `dynamics_execute_crud`
**Amaç:** Create / Update / Delete operasyonları.

### 6. `dynamics_export_to_excel`
**Amaç:** Kayıtları Excel'e aktarır. SADECE kullanıcı açıkça "excel/indir/aktar/export" dediğinde çağrılır.
- GatewayApi: dosya `files/exports/{entity}_{timestamp}.xlsx` yoluna yazılır, indirme URL'i döner
- McpServer: masaüstüne yazar, `file://` URI döner

---

## GatewayApi — Chat Endpoint

### POST /api/chat
```json
{
  "message": "Hesapları listele",
  "user_id": "user1",
  "session_id": "opsiyonel-guid",
  "anthropic_api_key": "sk-ant-...",
  "model": "claude-opus-4-7",
  "tenant_context": { ... },
  "file": {
    "name": "rapor.xlsx",
    "mime_type": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    "data": "<base64>"
  }
}
```
- `file` opsiyoneldir — göndermek zorunda değilsiniz
- Dosya gönderilirse `files/uploads/` altına kaydedilir, session geçmişinde tıklanabilir link saklanır
- Desteklenen dosya türleri: resim (jpg/png/gif/webp), PDF, Excel (.xlsx), CSV, TXT, JSON, XML

### GET /api/files
```
GET /api/files               → tüm dosyalar
GET /api/files?category=export  → sadece Excel exportları
GET /api/files?category=upload  → sadece yüklenen dosyalar
GET /api/files/{fileId}      → dosyayı indir
```

---

## Token Yönetimi (ClaudeAgentService)

- `MaxToolResultChars = 4000` — araç sonuçları bu karakterle kırpılır (entity fields taşmasını önler)
- `RecentTurnsVerbatim = 3` — son 3 tur tam gönderilir, eskiler sıkıştırılır
- `MaxTextBlockChars = 200_000` — dosya içerikleri (Excel CSV, metin) bu karakterde kesilir
- `MaxExcelRows = 200` — Excel'den Claude'a gönderilen maksimum satır sayısı
- Dosya base64'ü session'a kaydedilmez — sadece özet (URL'li markdown link) saklanır

---

## SQLite Şema

- `Sessions`: Id | UserId | Title | CreatedAt | LastActivityAt
- `Messages`: Id | SessionId | Role | ContentJson | CreatedAt
- `ExportedFiles`: Id (GUID) | FilePath | Category ('export'|'upload') | CreatedAt

**Migration stratejisi:** `EnsureCreated()` + `CREATE TABLE IF NOT EXISTS` + `pragma_table_info` ile kolon varlığı kontrol edilerek `ALTER TABLE` — yeni kolonlar her restart'ta güvenle eklenir.

---

## Metadata-First Akış

```
Kullanıcı: "Account varlığında kaç kayıt var?"
    ↓
dynamics_get_metadata → entity listesi (cache veya Dynamics)
    ↓
dynamics_get_entity_fields("account") → field listesi
    ↓
dynamics_get_count("accounts") → sayı
    ↓
Sonuç kullanıcıya döner
```

---

## TenantContext Güvenliği
- `client_secret` loglanmaz (maskelenir)
- Her tool çağrısında tenant context doğrulanır
- Token cache: per-tenant, TTL aware (expires_in - 5dk tampon)
- Farklı müşterilerin token'ları hiçbir zaman karışmaz

---

## Metadata Cache Stratejisi
| Durum | Aksiyon |
|-------|---------|
| İlk istek | Dynamics'ten çek, cache'e yaz (TTL: 60 dk) |
| Cache var | Direk döndür |
| `force_refresh: true` | Cache'i geçersiz kıl, tekrar çek |
| Token expire | Re-auth, cache temizle |

---

## Geliştirme Aşamaları

### ✅ FAZ 1 — MCP Server İskeleti (TAMAMLANDI)
- [x] Solution ve proje yapısı oluştur (.NET 8)
- [x] MCP SDK entegrasyonu (ModelContextProtocol 1.0.0 NuGet)
- [x] Generic Host + DI kurulumu
- [x] Serilog kurulumu
- [x] `TenantContext` ve Domain modelleri
- [x] 4 MCP tool stub implementasyonu
- [x] appsettings.json şablonu

### ✅ FAZ 2 — Dynamics Web API Bağlantısı (TAMAMLANDI)
- [x] OAuth2 token akışı — MSAL kaldırıldı, raw HTTP ile AAD token endpoint
- [x] tenant_id auto-discovery (WWW-Authenticate header parsing)
- [x] ROPC (username+password) + Service principal akışları
- [x] Token cache — per-tenant, TTL aware
- [x] OData GET sorgu implementasyonu
- [x] DefaultTenant konfigürasyonu — credentials opsiyonel
- [x] Claude Desktop entegrasyonu — gerçek Dynamics verisi alınıyor ✅
- [x] EntityDefinitions parallel fetch
- [x] CRUD endpoint implementasyonu

### ✅ FAZ 3 — Cache + Logging (TAMAMLANDI)
- [x] IMemoryCache ile metadata cache
- [x] TTL ve force_refresh mekanizması
- [x] AuditLogger tool'lara entegre edildi
- [x] Redis'e geçiş altyapısı (DistributedCacheService; Redis yoksa IMemoryCache fallback)

### ✅ FAZ 4 — Gateway API (TAMAMLANDI)
- [x] ASP.NET Core Web API — `DynamicsAI.GatewayApi` (http://localhost:5050)
- [x] Claude API agentic loop (raw HTTP, tool_use/tool_result döngüsü)
- [x] Session yönetimi — ConversationService (bellek + SQLite)
- [x] Swagger UI — http://localhost:5050/swagger
- [x] POST /api/chat — mesaj + opsiyonel dosya (base64 JSON)
- [x] GET /api/chat/sessions, GET /api/chat/sessions/{id}, DELETE /api/chat/{id}
- [x] GET /api/chat/health
- [x] SQLite kalıcı konuşma geçmişi
- [x] Per-request Anthropic API key + model override
- [x] İki aşamalı metadata (entity listesi + field detayı ayrı)
- [x] Excel export — `dynamics_export_to_excel`, MiniExcel streaming, OData nextLink sayfalama
- [x] ExportedFileRegistry — SQLite kalıcı, restart sonrası linkler geçerli
- [x] GET /api/files/{fileId} — dosya indirme
- [x] Dosya yükleme (base64 JSON) — resim/PDF/Excel/CSV/TXT/JSON/XML
- [x] Yüklenen dosyalar `files/uploads/` klasörüne kaydedilir — session geçmişinde tıklanabilir link
- [x] Export dosyaları `files/exports/` klasörüne kaydedilir
- [x] GET /api/files?category=export|upload — dosya listesi endpoint'i
- [x] Token taşması koruması: tool result kırpma + turn sıkıştırma + dosya içerik limiti
- [x] Turn boundary bug düzeltmesi (dosyalı mesajlar artık ayrı tur olarak tanınıyor)
- [x] Swagger çakışması giderildi — tek JSON endpoint, multipart kaldırıldı

**SQLite şema:**
- `Sessions`: Id | UserId | Title | CreatedAt | LastActivityAt
- `Messages`: Id | SessionId | Role | ContentJson | CreatedAt
- `ExportedFiles`: Id (GUID) | FilePath | Category | CreatedAt

### ⬜ FAZ 5 — WebResource (Dynamics UI)
- [ ] HTML/JS chat arayüzü
- [ ] `ai_configuration` custom entity
- [ ] Solution paketi (.zip)

---

## Önemli Kurallar (Claude Code için)

1. **Her fazı bitirince bu dosyadaki checkbox'ları güncelle**
2. **Yeni dosya eklenince Klasör Yapısı bölümünü güncelle**
3. **Breaking change olursa aşağıdaki "Mimari Notlar" bölümüne tarihli not ekle**
4. **Test yazmadan faz kapatılmaz**
5. **`client_secret` hiçbir zaman loga yazılmaz — maskeleme zorunlu**
6. **Tool input/output şemaları değişince yukarıdaki tanımları güncelle**
7. **Her oturumda önce bu dosyayı oku, sonra çalışmaya başla**
8. **Tamamlanan iş için "Son Yapılan İşlemler" bölümünü güncelle**

---

## Son Yapılan İşlemler
> Claude Code her oturumda bu bölümü okuyarak kaldığı yerden devam eder.

- `[2025-05-01]` — CLAUDE.md oluşturuldu, mimari ve faz planı tanımlandı
- `[2026-05-01]` — FAZ 1 tamamlandı: 5 proje, 4 MCP tool (stub), 7 unit test (7/7 geçti).
- `[2026-05-01]` — FAZ 2 kısmen: DynamicsAuthProvider implement edildi. 11/11 test geçiyor.
- `[2026-05-01]` — DefaultTenant mimarisi eklendi. DynamicsQueryService gerçek OData HTTP çağrısı yapıyor.
- `[2026-05-01]` — FAZ 2 auth tamamlandı: MSAL kaldırıldı, raw HTTP ile AAD token. Claude Desktop üzerinde gerçek Dynamics verisi başarıyla alındı ✅
- `[2026-05-02]` — DynamicsMetadataService yeniden yazıldı: parallel per-entity `/Attributes` fetch. Build: 0 error.
- `[2026-05-02]` — FAZ 3 tamamlandı: AuditLogger, DistributedCacheService, Redis koşullu kayıt. 21/21 test geçiyor.
- `[2026-05-02]` — FAZ 4 tamamlandı: GatewayApi projesi, Claude agentic loop, ConversationService, DynamicsToolExecutor. 27/27 test geçiyor.
- `[2026-05-02]` — FAZ 4 genişletildi: SQLite kalıcı konuşma geçmişi. 29/29 test geçiyor.
- `[2026-05-02]` — Token limiti düzeltmeleri: dynamics_get_entity_fields ayrıldı, tool result kırpma (4000 char), RecentTurnsVerbatim=3. Per-request API key eklendi.
- `[2026-05-03]` — Excel export eklendi: dynamics_export_to_excel (McpServer + GatewayApi), MiniExcel streaming, ExportedFileRegistry SQLite kalıcı, GET /api/files/{fileId}.
- `[2026-05-03]` — Dosya yükleme eklendi: base64 JSON (FileAttachment), FileProcessingService, multipart/Swagger çakışması giderildi. Token taşması koruması (MaxTextBlockChars=200k, MaxExcelRows=200). Turn boundary bug düzeltildi.
- `[2026-05-03]` — Dosya kalıcılığı eklendi: yüklenen dosyalar files/uploads/, exportlar files/exports/ klasörüne kaydedilir. ExportedFileRegistry Category alanı eklendi. GET /api/files?category= liste endpoint'i. StorageOptions singleton. SQLite migration pragma_table_info ile güvenli hale getirildi.

---

## Mimari Notlar
> Breaking change, önemli karar veya tasarım değişikliği olursa buraya tarihli not eklenir.

- `[2025-05-01]` — Generic/metadata-first yaklaşım benimsendi. Entity başına tool yerine generic tool'lar kullanılıyor.
- `[2026-05-01]` — DefaultTenant pattern: credentials appsettings.json'da, araç çağrılarında tenant_context opsiyonel.
- `[2026-05-01]` — Auth mimarisi: MSAL kaldırıldı, raw HTTP ile AAD `/oauth2/v2.0/token`. Binary değişikliklerinde process durdur → build → restart.
- `[2026-05-01]` — KRİTİK: Çalışan binary'yi bozmadan geliştirmek için her değişiklikten önce onay al, build et, test et.
- `[2026-05-02]` — Per-request API key: her firma kendi `anthropic_api_key`'ini gönderir. Metadata iki tool'a bölündü (entity listesi + field detayı).
- `[2026-05-03]` — Dosya yükleme tasarımı: multipart yerine base64 JSON seçildi. Sebep: Dynamics WebResource'dan JS ile dosya gönderimi için JSON daha uygun (Xrm.WebApi + FileReader). Dynamics annotation.documentbody zaten base64 döndürür.
- `[2026-05-03]` — Dosya depolama: yüklenen dosyalar `files/uploads/`, exportlar `files/exports/` altında tutulur. StorageOptions singleton ile path yönetimi merkezi. Session geçmişinde dosyalar URL'li markdown link olarak saklanır (base64 blob değil) — token tasarrufu + yeniden yüklenebilirlik.

---

## Bağımlılıklar (NuGet)

```xml
<!-- McpServer projesi -->
<PackageReference Include="ModelContextProtocol" Version="0.2.*" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.*" />
<PackageReference Include="Serilog.Extensions.Hosting" Version="8.0.*" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.0.*" />
<PackageReference Include="Serilog.Sinks.File" Version="5.0.*" />

<!-- Infrastructure projesi -->
<PackageReference Include="Microsoft.Extensions.Http" Version="8.0.*" />
<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="8.0.*" />
<PackageReference Include="System.Text.Json" Version="8.0.*" />

<!-- GatewayApi projesi -->
<PackageReference Include="MiniExcel" Version="1.*" />
<PackageReference Include="Serilog.AspNetCore" Version="8.0.*" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.9.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.*" />

<!-- Test projesi -->
<PackageReference Include="xunit" Version="2.9.*" />
<PackageReference Include="Moq" Version="4.20.*" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.*" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.8.*" />
```

---

## Referans Linkler
- [MCP .NET SDK — GitHub](https://github.com/modelcontextprotocol/csharp-sdk)
- [Dynamics Web API OData Docs](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/overview)
- [EntityDefinitions API](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/query-metadata-web-api)
- [Serilog .NET](https://serilog.net)
- [MiniExcel GitHub](https://github.com/mini-software/MiniExcel)
