# DynamicsAI — Dynamics 365 için AI Destekli MCP Server

Dynamics 365 CRM verilerine **doğal dil** ile sorgu yapmanızı sağlayan, çok kiracılı (multi-tenant) bir **Model Context Protocol (MCP) Server** ve **Gateway API** projesi.

Claude AI ile konuşur gibi Dynamics 365 verilerinizi sorgulayabilir, kayıt oluşturabilir, güncelleyebilir ve Excel'e aktarabilirsiniz.

---

## İçindekiler

- [Mimari](#mimari)
- [Gereksinimler](#gereksinimler)
- [Kurulum](#kurulum)
- [MCP Server Kullanımı (Claude Desktop)](#mcp-server-kullanımı-claude-desktop)
- [Gateway API Kullanımı](#gateway-api-kullanımı)
- [Dynamics 365 WebResource](#dynamics-365-webresource)
- [Yapılandırma Referansı](#yapılandırma-referansı)
- [MCP Tool Listesi](#mcp-tool-listesi)
- [Teknoloji Yığını](#teknoloji-yığını)

---

## Mimari

```
┌─────────────────────────────────────────────────────────────┐
│                        İstemciler                           │
│   Claude Desktop  │  Web Uygulama  │  Dynamics WebResource  │
└────────┬──────────┴───────┬────────┴───────────┬────────────┘
         │                  │                    │
         ▼                  ▼                    ▼
┌─────────────────┐  ┌──────────────────────────────────────┐
│  MCP Server     │  │         Gateway API (REST)            │
│  (stdio)        │  │         http://localhost:5050         │
│                 │  │  POST /api/chat                       │
│  4 MCP Tool     │  │  GET  /api/files                      │
│  + Auth         │  │  GET  /api/chat/sessions              │
└────────┬────────┘  └──────────────┬───────────────────────┘
         │                          │
         └──────────┬───────────────┘
                    ▼
         ┌──────────────────────┐
         │  Dynamics 365 Web API│
         │  (OData v4)          │
         │  AAD OAuth2 Token    │
         └──────────────────────┘
```

### Proje Katmanları

```
DynamicsAI.McpServer/       ← MCP Host + Tool kayıt (Claude Desktop)
DynamicsAI.Application/     ← Use case'ler, servis interface'leri
DynamicsAI.Infrastructure/  ← Dynamics Web API impl, Auth, Cache
DynamicsAI.Domain/          ← Entity modeller, Value Object, Exception
DynamicsAI.GatewayApi/      ← REST API + Claude agentic loop + SQLite
DynamicsAI.Tests/           ← Unit + Integration testler
```

---

## Gereksinimler

| Bileşen | Minimum Versiyon |
|---------|-----------------|
| .NET SDK | 8.0 |
| Claude Desktop | Güncel sürüm |
| Anthropic API Key | `sk-ant-...` |
| Dynamics 365 | Online veya OnPrem |
| (Opsiyonel) Redis | 7.x |

---

## Kurulum

### 1. Repoyu Klonlayın

```bash
git clone https://github.com/kullaniciadi/DynamicsAI.git
cd DynamicsAI
```

### 2. Bağımlılıkları Yükleyin

```bash
dotnet restore
```

### 3. Projeyi Derleyin

```bash
dotnet build -c Release
```

### 4. Testleri Çalıştırın

```bash
dotnet test
```

---

## MCP Server Kullanımı (Claude Desktop)

MCP Server, **Claude Desktop** ile doğrudan entegre olur. Yapılandırma tamamlandığında Claude'a doğal dil ile Dynamics 365 soruları sorabilirsiniz.

### Adım 1 — appsettings.json Yapılandırması

`src/DynamicsAI.McpServer/appsettings.json` dosyasını düzenleyin:

```json
{
  "DynamicsAI": {
    "DefaultTenant": {
      "DynamicsUrl": "https://ORGID.crm.dynamics.com/",
      "DeploymentType": "Online",

      "ClientId": "",
      "ClientSecret": "",

      "Username": "kullanici@domain.onmicrosoft.com",
      "Password": "SifreNiz"
    }
  }
}
```

> **Not:** `ClientId` + `ClientSecret` (Service Principal) **veya** `Username` + `Password` (ROPC) kullanabilirsiniz. İkisini birden doldurmak zorunda değilsiniz.

#### Service Principal (Önerilen — Üretim için)

```json
{
  "DynamicsAI": {
    "DefaultTenant": {
      "DynamicsUrl": "https://ORGID.crm.dynamics.com/",
      "TenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "ClientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "ClientSecret": "gizli-deger",
      "DeploymentType": "Online"
    }
  }
}
```

#### ROPC (Kullanıcı adı/şifre — Geliştirme/Test için)

```json
{
  "DynamicsAI": {
    "DefaultTenant": {
      "DynamicsUrl": "https://ORGID.crm.dynamics.com/",
      "Username": "kullanici@domain.onmicrosoft.com",
      "Password": "SifreNiz",
      "DeploymentType": "Online"
    }
  }
}
```

### Adım 2 — MCP Server'ı Publish Edin

```bash
dotnet publish src/DynamicsAI.McpServer -c Release -o ./publish/McpServer
```

### Adım 3 — Claude Desktop Yapılandırması

Claude Desktop yapılandırma dosyasını açın:

- **Windows:** `%APPDATA%\Claude\claude_desktop_config.json`
- **macOS:** `~/Library/Application Support/Claude/claude_desktop_config.json`

Aşağıdaki bloğu ekleyin:

```json
{
  "mcpServers": {
    "DynamicsAI": {
      "command": "dotnet",
      "args": [
        "C:\\tam\\yol\\publish\\McpServer\\DynamicsAI.McpServer.dll"
      ]
    }
  }
}
```

> **Windows için örnek:**
> ```json
> "args": ["C:\\Users\\KullaniciAdi\\DynamicsAI\\publish\\McpServer\\DynamicsAI.McpServer.dll"]
> ```

### Adım 4 — Claude Desktop'ı Yeniden Başlatın

Claude Desktop'ı kapatıp açın. Sol altta **MCP** ikonunu görürseniz bağlantı başarılıdır.

### Kullanım Örnekleri (Claude Desktop)

```
"Account tablosundaki tüm kayıtları listele"
"contacts tablosunda email alanı boş olan kayıtları bul"
"Yeni bir lead kaydı oluştur: Ad: Ahmet, Soyad: Yılmaz, Email: ahmet@test.com"
"opportunity tablosunu Excel'e aktar"
"account tablosunda kaç kayıt var?"
```

---

## Gateway API Kullanımı

Gateway API, web uygulamaları veya Dynamics 365 WebResource için kullanılan **REST endpoint**'idir.

### Adım 1 — GatewayApi Yapılandırması

`src/DynamicsAI.GatewayApi/appsettings.json` dosyasını düzenleyin:

```json
{
  "Anthropic": {
    "ApiKey": "sk-ant-api03-...",
    "Model": "claude-opus-4-7"
  },
  "DynamicsAI": {
    "DefaultTenant": {
      "DynamicsUrl": "https://ORGID.crm.dynamics.com/",
      "Username": "kullanici@domain.onmicrosoft.com",
      "Password": "SifreNiz",
      "DeploymentType": "Online"
    }
  },
  "Storage": {
    "BasePath": "files"
  },
  "Urls": "http://localhost:5050"
}
```

### Adım 2 — GatewayApi'yi Başlatın

```bash
dotnet run --project src/DynamicsAI.GatewayApi
```

veya publish edip çalıştırın:

```bash
dotnet publish src/DynamicsAI.GatewayApi -c Release -o ./publish/GatewayApi
dotnet ./publish/GatewayApi/DynamicsAI.GatewayApi.dll
```

API `http://localhost:5050` adresinde çalışır.

Swagger UI: [http://localhost:5050/swagger](http://localhost:5050/swagger)

### API Endpoint'leri

#### POST /api/chat — Sohbet

```bash
curl -X POST http://localhost:5050/api/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Account tablosunda kaç kayıt var?",
    "user_id": "kullanici1",
    "session_id": "opsiyonel-oturum-id",
    "anthropic_api_key": "sk-ant-...",
    "model": "claude-opus-4-7",
    "tenant_context": {
      "dynamics_url": "https://ORGID.crm.dynamics.com/",
      "username": "kullanici@domain.com",
      "password": "Sifre"
    }
  }'
```

> `tenant_context` opsiyoneldir. Gönderilmezse `appsettings.json`'daki `DefaultTenant` kullanılır.

**Yanıt:**
```json
{
  "session_id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "message": "Account tablosunda toplam 1.247 kayıt bulunmaktadır.",
  "tool_calls_made": 2
}
```

#### Dosya Yükleme (base64)

```bash
curl -X POST http://localhost:5050/api/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Bu Excel dosyasındaki verileri analiz et",
    "user_id": "kullanici1",
    "anthropic_api_key": "sk-ant-...",
    "file": {
      "name": "rapor.xlsx",
      "mime_type": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
      "data": "<base64-encoded-content>"
    }
  }'
```

Desteklenen dosya türleri: `jpg`, `png`, `gif`, `webp`, `pdf`, `xlsx`, `csv`, `txt`, `json`, `xml`

#### GET /api/files — Dosya Listesi

```bash
# Tüm dosyalar
curl http://localhost:5050/api/files

# Sadece Excel exportları
curl http://localhost:5050/api/files?category=export

# Sadece yüklenen dosyalar
curl http://localhost:5050/api/files?category=upload
```

#### GET /api/files/{fileId} — Dosya İndir

```bash
curl http://localhost:5050/api/files/3fa85f64-5717-4562-b3fc-2c963f66afa6 \
  --output rapor.xlsx
```

#### GET /api/chat/sessions — Oturum Listesi

```bash
curl "http://localhost:5050/api/chat/sessions?user_id=kullanici1"
```

#### GET /api/chat/sessions/{id} — Oturum Geçmişi

```bash
curl http://localhost:5050/api/chat/sessions/OTURUM-ID
```

#### DELETE /api/chat/{id} — Oturum Sil

```bash
curl -X DELETE http://localhost:5050/api/chat/OTURUM-ID
```

#### GET /api/chat/health — Sağlık Kontrolü

```bash
curl http://localhost:5050/api/chat/health
```

---

## Dynamics 365 WebResource

Projeyi doğrudan Dynamics 365 form veya dashboard'una gömebilirsiniz.

### Dosyalar

```
webresource/
  chat.html   ← Ana HTML dosyası (WebResource olarak yükleyin)
  chat.css    ← Stil dosyası
  chat.js     ← Xrm.WebApi entegrasyonu + chat mantığı
```

### Kurulum

1. `webresource/chat.html`, `chat.css`, `chat.js` dosyalarını Dynamics 365'e **Web Resource** olarak yükleyin
2. `ai_configuration` custom entity'sinde bir **varsayılan kayıt** oluşturun (GatewayApi URL, API key vb.)
3. Web Resource'u bir form veya dashboard'a ekleyin

### ai_configuration Entity Alanları

| Alan | Tür | Açıklama |
|------|-----|----------|
| `name` | Metin | Yapılandırma adı |
| `gatewayapiurl` | Metin | Gateway API adresi (`http://localhost:5050`) |
| `anthropicapikey` | Metin | `sk-ant-...` |
| `model` | Metin | Kullanılacak Claude modeli |
| `dynamicsurl` | Metin | Dynamics URL (opsiyonel, override) |
| `isdefault` | Evet/Hayır | Varsayılan yapılandırma mı? |

> `isdefault = true` olan kayıt otomatik olarak yüklenir. Xrm.WebApi yoksa `localStorage` fallback devreye girer.

---

## Yapılandırma Referansı

### DefaultTenant Alanları

| Alan | Zorunlu | Açıklama |
|------|---------|----------|
| `DynamicsUrl` | Evet | `https://ORGID.crm.dynamics.com/` |
| `DeploymentType` | Evet | `Online` veya `OnPrem` |
| `TenantId` | Service Principal için | AAD Tenant GUID |
| `ClientId` | Service Principal için | App Registration GUID |
| `ClientSecret` | Service Principal için | Client secret değeri |
| `Username` | ROPC için | UPN formatında kullanıcı adı |
| `Password` | ROPC için | Kullanıcı şifresi |

### Token Limitleri (ClaudeAgentService)

| Parametre | Değer | Açıklama |
|-----------|-------|----------|
| `MaxToolResultChars` | 4.000 | Araç yanıtı maksimum karakter |
| `RecentTurnsVerbatim` | 3 | Tam gönderilen son tur sayısı |
| `MaxTextBlockChars` | 200.000 | Dosya içeriği maksimum karakter |
| `MaxExcelRows` | 200 | Claude'a gönderilen maksimum Excel satırı |

### Redis Cache (Opsiyonel)

Redis bağlantı dizesi boş bırakılırsa otomatik olarak `IMemoryCache` kullanılır:

```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

---

## MCP Tool Listesi

| Tool | Açıklama |
|------|----------|
| `dynamics_get_metadata` | Entity listesini döndürür (logical name, display name) |
| `dynamics_get_count` | Entity kayıt sayısını döndürür (filtreli veya filtresiz) |
| `dynamics_execute_query` | OData GET sorgusu — kayıt listeleme |
| `dynamics_execute_crud` | Create / Update / Delete operasyonları |
| `dynamics_export_to_excel` | Kayıtları Excel dosyasına aktarır |

### Örnek Tool Akışı

```
Kullanıcı: "Account tablosundaki İstanbul'daki firmaları Excel'e aktar"
    ↓
dynamics_get_metadata        → entity listesi
    ↓
dynamics_execute_query       → accounts?$filter=address1_city eq 'Istanbul'
    ↓
dynamics_export_to_excel     → files/exports/accounts_20260507_143022.xlsx
    ↓
"Dosya hazır: /api/files/3fa85f64-... adresinden indirebilirsiniz."
```

---

## Teknoloji Yığını

| Katman | Teknoloji |
|--------|-----------|
| Runtime | .NET 8 |
| MCP Protokolü | ModelContextProtocol (official .NET SDK) |
| AI | Anthropic Claude API |
| Web Framework | ASP.NET Core 8 |
| Veritabanı | SQLite + Entity Framework Core 8 |
| Cache | IMemoryCache / Redis (opsiyonel) |
| Excel | MiniExcel (streaming) |
| Logging | Serilog (Console + File) |
| API Docs | Swagger / Swashbuckle |
| Test | xUnit + Moq |
| Auth | Raw HTTP — AAD OAuth2 (`/oauth2/v2.0/token`) |

---

## Güvenlik Notları

- `client_secret` ve `password` değerleri **asla loglara yazılmaz** — maskeleme zorunludur
- Her tenant'ın token cache'i birbirinden izole tutulur
- Token'lar expire süresi dolmadan 5 dakika önce yenilenir
- `appsettings.json` içindeki kimlik bilgilerini **git'e commit etmeyin** — `.gitignore`'a ekleyin

### Önerilen .gitignore Eklemeleri

```
src/*/appsettings.json
**/appsettings.Production.json
**/conversations.db
files/
```

---

## Katkıda Bulunma

1. Fork edin
2. Feature branch oluşturun: `git checkout -b feature/yeni-ozellik`
3. Testleri çalıştırın: `dotnet test`
4. Commit edin: `git commit -m 'feat: yeni özellik eklendi'`
5. Pull Request açın

---

## Lisans

MIT License — detaylar için [LICENSE](LICENSE) dosyasına bakın.
