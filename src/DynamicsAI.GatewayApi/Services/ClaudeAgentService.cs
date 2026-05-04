using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DynamicsAI.Application.DTOs;
using DynamicsAI.GatewayApi.Models;

namespace DynamicsAI.GatewayApi.Services;

public class ClaudeAgentService(
    IHttpClientFactory httpClientFactory,
    ConversationService conversationService,
    DynamicsToolExecutor toolExecutor,
    IConfiguration configuration,
    ILogger<ClaudeAgentService> logger)
{
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxTokens = 8096;
    private const int MaxIterations = 10;
    private const int RecentTurnsVerbatim = 3; // older turns are compressed to user+assistant text only
    // Tool result'ları bu boyutun üzerindeyse kesilir — entity fields gibi büyük yanıtlar token limitini patlatır
    private const int MaxToolResultChars = 4000;

    private static readonly JsonSerializerOptions SerOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonNode ToolDefinitions = BuildToolDefinitions();

    public async Task<(string SessionId, string Message, int ToolCallsMade)> SendMessageAsync(
        string? sessionId,
        string userId,
        string userMessage,
        TenantContext? tenantContext,
        string? anthropicApiKey = null,
        string? modelOverride = null,
        JsonNode? fileBlock = null,   // Claude content block (image/document/text)
        string? fileSummary = null,   // Session'da saklanan kısa özet
        CancellationToken ct = default)
    {
        var session = await conversationService.GetOrCreateAsync(sessionId, userId);
        var outSessionId = session.Id;

        // Session'a özet kaydedilir (base64 blob saklanmaz, token şişmesini önler)
        var storedContent = fileBlock is null
            ? (JsonNode)userMessage
            : (JsonNode)new JsonArray
              {
                  new JsonObject { ["type"] = "text", ["text"] = $"{fileSummary} {userMessage}".Trim() }
              };

        session.Messages.Add(new JsonObject
        {
            ["role"]    = "user",
            ["content"] = storedContent
        });

        var apiKey = !string.IsNullOrWhiteSpace(anthropicApiKey)
            ? anthropicApiKey
            : configuration["Anthropic:ApiKey"]
              ?? throw new InvalidOperationException("Anthropic API key sağlanmadı. Request'e anthropic_api_key ekleyin veya appsettings.json > Anthropic:ApiKey ayarlayın.");

        var model = !string.IsNullOrWhiteSpace(modelOverride)
            ? modelOverride
            : configuration["Anthropic:Model"] ?? "claude-opus-4-7";
        var client = httpClientFactory.CreateClient("ClaudeApi");
        var toolCallsMade = 0;

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var messages = BuildMessagesArray(session.Messages);

            // İlk iterasyonda ve dosya varsa: son mesajı (özet) gerçek dosya içeriğiyle değiştir.
            // Sonraki iterasyonlarda Claude zaten dosyayı gördü — sadece özet yeterli.
            if (iteration == 0 && fileBlock is not null)
            {
                var fullContent = new JsonArray
                {
                    fileBlock.DeepClone(),
                    new JsonObject { ["type"] = "text", ["text"] = userMessage }
                };
                messages[messages.Count - 1] = new JsonObject
                {
                    ["role"]    = "user",
                    ["content"] = fullContent
                };
            }

            var requestBody = new JsonObject
            {
                ["model"]      = model,
                ["max_tokens"] = MaxTokens,
                ["system"]     = BuildSystemPrompt(),
                ["tools"]      = ToolDefinitions.DeepClone(),
                ["messages"]   = messages
            };

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
            {
                Content = new StringContent(requestBody.ToJsonString(SerOpts), Encoding.UTF8, "application/json")
            };
            httpReq.Headers.Add("x-api-key", apiKey);
            httpReq.Headers.Add("anthropic-version", AnthropicVersion);

            using var httpResp = await client.SendAsync(httpReq, ct);
            var raw = await httpResp.Content.ReadAsStringAsync(ct);

            if (!httpResp.IsSuccessStatusCode)
            {
                logger.LogError("Claude API hatası {Status}: {Body}", (int)httpResp.StatusCode, raw);
                throw new Exception($"Claude API hatası HTTP {(int)httpResp.StatusCode}: {raw}");
            }

            var response = JsonSerializer.Deserialize<ClaudeApiResponse>(raw, SerOpts)!;

            // Save assistant response to session
            var assistantContent = JsonSerializer.SerializeToNode(response.Content, SerOpts)!;
            session.Messages.Add(new JsonObject
            {
                ["role"]    = "assistant",
                ["content"] = assistantContent
            });

            if (response.StopReason == "end_turn" || response.StopReason == "stop_sequence")
            {
                var text = response.Content.FirstOrDefault(b => b.Type == "text")?.Text ?? "";
                await conversationService.FlushAsync(outSessionId);
                return (outSessionId, text, toolCallsMade);
            }

            if (response.StopReason == "tool_use")
            {
                var toolUseBlocks = response.Content.Where(b => b.Type == "tool_use").ToList();
                var toolResults   = new JsonArray();

                foreach (var block in toolUseBlocks)
                {
                    toolCallsMade++;
                    string toolResultContent;
                    try
                    {
                        toolResultContent = await toolExecutor.ExecuteAsync(
                            block.Name!,
                            block.Input!.Value,
                            tenantContext,
                            ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Araç {Tool} çalıştırılamadı", block.Name);
                        toolResultContent = JsonSerializer.Serialize(new { error = ex.Message }, SerOpts);
                    }

                    toolResults.Add(new JsonObject
                    {
                        ["type"]        = "tool_result",
                        ["tool_use_id"] = block.Id,
                        ["content"]     = toolResultContent
                    });
                }

                session.Messages.Add(new JsonObject
                {
                    ["role"]    = "user",
                    ["content"] = toolResults
                });

                continue;
            }

            // Unexpected stop reason — return whatever text is available
            var fallback = response.Content.FirstOrDefault(b => b.Type == "text")?.Text ?? "";
            await conversationService.FlushAsync(outSessionId);
            return (outSessionId, fallback, toolCallsMade);
        }

        await conversationService.FlushAsync(outSessionId);
        return (outSessionId, "Maksimum iterasyon sınırına ulaşıldı.", toolCallsMade);
    }

    // Groups messages into turns. A new turn starts at each user message with string content.
    // Old turns (beyond RecentTurnsVerbatim) are compressed to user text + final assistant text,
    // dropping all tool_use / tool_result intermediaries to save tokens.
    private static JsonArray BuildMessagesArray(List<JsonNode> messages)
    {
        var turns = new List<List<JsonNode>>();
        List<JsonNode>? current = null;

        foreach (var msg in messages)
        {
            var role = msg["role"]?.GetValue<string>();
            // Yeni turn: user'dan gelen text mesajı veya dosya özeti (JsonArray içinde text).
            // tool_result içeren user mesajları (JsonArray, type=tool_result) ayrı turn başlatmaz.
            if (role == "user" && IsUserTextMessage(msg))
            {
                current = [msg];
                turns.Add(current);
            }
            else
            {
                current?.Add(msg);
            }
        }

        var result = new List<JsonNode>();
        var keepFrom = Math.Max(0, turns.Count - RecentTurnsVerbatim);

        for (var i = 0; i < turns.Count; i++)
        {
            var turn = turns[i];
            if (i >= keepFrom)
            {
                result.AddRange(turn);
            }
            else
            {
                result.Add(turn[0]); // user text message

                var assistantText = turn
                    .LastOrDefault(m => m["role"]?.GetValue<string>() == "assistant"
                                     && GetAssistantText(m) != null);
                if (assistantText is not null)
                    result.Add(new JsonObject
                    {
                        ["role"]    = "assistant",
                        ["content"] = GetAssistantText(assistantText)!
                    });
            }
        }

        var arr = new JsonArray();
        foreach (var msg in result)
            arr.Add(TruncateToolResults(msg.DeepClone()));
        return arr;
    }

    // Tool result içeriğini MaxToolResultChars ile sınırlar.
    // Büyük entity field listesi gibi yanıtlar her istekte tekrar gönderildiğinde token limitini patlatır.
    private static JsonNode TruncateToolResults(JsonNode msg)
    {
        if (msg["role"]?.GetValue<string>() != "user") return msg;
        if (msg["content"] is not JsonArray contentArr) return msg;

        var modified = false;
        var newContent = new JsonArray();

        foreach (var item in contentArr)
        {
            if (item?["type"]?.GetValue<string>() == "tool_result")
            {
                var content = item["content"]?.GetValue<string>() ?? "";
                if (content.Length > MaxToolResultChars)
                {
                    var truncated = new JsonObject
                    {
                        ["type"]        = "tool_result",
                        ["tool_use_id"] = item["tool_use_id"]?.GetValue<string>(),
                        ["content"]     = content[..MaxToolResultChars]
                            + $"\n[... +{content.Length - MaxToolResultChars} karakter kırpıldı]"
                    };
                    newContent.Add(truncated);
                    modified = true;
                }
                else
                {
                    newContent.Add(item.DeepClone());
                }
            }
            else
            {
                newContent.Add(item?.DeepClone());
            }
        }

        if (!modified) return msg;
        return new JsonObject { ["role"] = "user", ["content"] = newContent };
    }

    // User mesajının yeni bir konuşma turu başlatıp başlatmadığını belirler.
    // Düz metin veya dosya özeti (text type'lı array) → yeni tur.
    // tool_result array'i → mevcut tura eklenir.
    private static bool IsUserTextMessage(JsonNode msg)
    {
        if (msg["content"] is JsonValue) return true; // düz string

        if (msg["content"] is JsonArray arr)
        {
            // İlk öğe tool_result ise bu bir tool yanıtı — yeni tur değil
            var firstType = arr.FirstOrDefault()?["type"]?.GetValue<string>();
            return firstType != "tool_result";
        }
        return false;
    }

    private static string? GetAssistantText(JsonNode msg)
    {
        if (msg["content"] is not JsonArray arr) return null;
        return arr.FirstOrDefault(b => b?["type"]?.GetValue<string>() == "text")?
                  ["text"]?.GetValue<string>();
    }

    private static string BuildSystemPrompt() =>
        """
        Sen Dynamics 365 CRM asistanısın.

        Araç sırası: dynamics_get_metadata → dynamics_get_entity_fields → sorgu/CRUD.
        Field listesini her sorgu/CRUD öncesi kontrol et.
        Yanıt dilini kullanıcıyla eşleştir (TR/EN).
        Create/Update/Delete için kullanıcı onayı al.

        ARAÇ SEÇİM KURALI — KESİNLİKLE UY:
        - Kullanıcı kayıt LİSTELEMEK veya GÖRÜNTÜLEMEK istiyorsa → dynamics_execute_query (top parametresiyle sınırla)
        - Kullanıcı SADECE "excel'e aktar", "indir", "dosyaya kaydet" gibi açık bir dosya isteği yapıyorsa → dynamics_export_to_excel
        - "1000 kayıt getir", "hepsini listele", "göster" gibi ifadeler → dynamics_execute_query, ASLA export değil
        """;

    private static JsonNode BuildToolDefinitions()
    {
        return new JsonArray
        {
            new JsonObject
            {
                ["name"]        = "dynamics_get_metadata",
                ["description"] = "Dynamics 365'teki entity listesini getirir (logical name, display name, plural name). " +
                                  "Sadece entity adlarını döndürür — field detayları YOK. " +
                                  "Hangi entity'yi kullanacağını belirlemek için önce bunu çağır, " +
                                  "ardından dynamics_get_entity_fields ile o entity'nin field'larını al.",
                ["input_schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["force_refresh"] = new JsonObject
                        {
                            ["type"]        = "boolean",
                            ["description"] = "true ise cache'i geçip Dynamics'ten tekrar çeker."
                        }
                    }
                }
            },
            new JsonObject
            {
                ["name"]        = "dynamics_get_entity_fields",
                ["description"] = "Belirtilen entity'nin tüm field'larını (logical name, display name, tip, zorunluluk) döndürür. " +
                                  "Sorgu veya CRUD yapmadan önce doğru field adlarını öğrenmek için kullan.",
                ["input_schema"] = new JsonObject
                {
                    ["type"]     = "object",
                    ["required"] = new JsonArray { "logical_name" },
                    ["properties"] = new JsonObject
                    {
                        ["logical_name"] = new JsonObject
                        {
                            ["type"]        = "string",
                            ["description"] = "Entity'nin logical adı, ör. 'account', 'contact', 'lead'"
                        }
                    }
                }
            },
            new JsonObject
            {
                ["name"]        = "dynamics_execute_query",
                ["description"] = "Dynamics 365 entity'sinde kayıtları LİSTELER veya GÖRÜNTÜLER. " +
                                  "Kullanıcı kayıt görmek, saymak veya aramak istediğinde KULLAN. " +
                                  "top parametresiyle sonuç sayısını mutlaka sınırla (örn. top=50, top=1000). " +
                                  "Kullanıcı Excel/dosya/indirme istemiyorsa her zaman bu aracı kullan.",
                ["input_schema"] = new JsonObject
                {
                    ["type"]     = "object",
                    ["required"] = new JsonArray { "entity_plural_name" },
                    ["properties"] = new JsonObject
                    {
                        ["entity_plural_name"] = new JsonObject
                        {
                            ["type"]        = "string",
                            ["description"] = "Çoğul entity adı, ör. 'accounts', 'contacts', 'leads'"
                        },
                        ["select_fields"] = new JsonObject
                        {
                            ["type"]  = "array",
                            ["items"] = new JsonObject { ["type"] = "string" },
                            ["description"] = "Döndürülecek field adları"
                        },
                        ["filter"] = new JsonObject
                        {
                            ["type"]        = "string",
                            ["description"] = "OData filtre ifadesi, ör. \"statecode eq 0\""
                        },
                        ["order_by"] = new JsonObject
                        {
                            ["type"]        = "string",
                            ["description"] = "OData sıralama ifadesi, ör. \"createdon desc\""
                        },
                        ["top"] = new JsonObject
                        {
                            ["type"]        = "integer",
                            ["description"] = "Maksimum kayıt sayısı (varsayılan 50)"
                        }
                    }
                }
            },
            new JsonObject
            {
                ["name"]        = "dynamics_get_count",
                ["description"] = "Bir Dynamics 365 entity'sindeki kayıt sayısını döner, isteğe bağlı filtre uygulanabilir.",
                ["input_schema"] = new JsonObject
                {
                    ["type"]     = "object",
                    ["required"] = new JsonArray { "entity_plural_name" },
                    ["properties"] = new JsonObject
                    {
                        ["entity_plural_name"] = new JsonObject
                        {
                            ["type"]        = "string",
                            ["description"] = "Çoğul entity adı"
                        },
                        ["filter"] = new JsonObject
                        {
                            ["type"]        = "string",
                            ["description"] = "İsteğe bağlı OData filtre ifadesi"
                        }
                    }
                }
            },
            new JsonObject
            {
                ["name"]        = "dynamics_execute_crud",
                ["description"] = "Dynamics 365'te kayıt oluşturur, günceller veya siler.",
                ["input_schema"] = new JsonObject
                {
                    ["type"]     = "object",
                    ["required"] = new JsonArray { "operation", "entity_plural_name" },
                    ["properties"] = new JsonObject
                    {
                        ["operation"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray { "Create", "Update", "Delete" },
                            ["description"] = "Yapılacak işlem"
                        },
                        ["entity_plural_name"] = new JsonObject
                        {
                            ["type"]        = "string",
                            ["description"] = "Çoğul entity adı"
                        },
                        ["record_id"] = new JsonObject
                        {
                            ["type"]        = "string",
                            ["description"] = "GUID — Update ve Delete için zorunlu"
                        },
                        ["payload"] = new JsonObject
                        {
                            ["type"]        = "object",
                            ["description"] = "Oluşturulacak/güncellenecek field değerleri"
                        }
                    }
                }
            },
            new JsonObject
            {
                ["name"]        = "dynamics_export_to_excel",
                ["description"] = "Kayıtları Excel dosyasına AKTARIR ve indirme linki döner. " +
                                  "SADECE kullanıcı açıkça 'excel', 'indir', 'dosya', 'aktar', 'export' gibi kelimeler kullandığında çağır. " +
                                  "Kullanıcı kayıt sayısı belirtmişse (ör. '1000 kayıt excel') max_records parametresini ayarla; tüm kayıtlar isteniyorsa boş bırak. " +
                                  "Kullanıcı sadece kayıt görmek veya listelemek istiyorsa KULLANMA — bunun için dynamics_execute_query kullan.",
                ["input_schema"] = new JsonObject
                {
                    ["type"]     = "object",
                    ["required"] = new JsonArray { "entity_plural_name" },
                    ["properties"] = new JsonObject
                    {
                        ["entity_plural_name"] = new JsonObject
                        {
                            ["type"]        = "string",
                            ["description"] = "Çoğul entity adı, ör. 'accounts', 'contacts'"
                        },
                        ["select_fields"] = new JsonObject
                        {
                            ["type"]  = "array",
                            ["items"] = new JsonObject { ["type"] = "string" },
                            ["description"] = "Dışa aktarılacak field adları (boşsa tüm field'lar)"
                        },
                        ["filter"] = new JsonObject
                        {
                            ["type"]        = "string",
                            ["description"] = "İsteğe bağlı OData filtre, ör. \"statecode eq 0\""
                        },
                        ["order_by"] = new JsonObject
                        {
                            ["type"]        = "string",
                            ["description"] = "İsteğe bağlı sıralama ifadesi"
                        },
                        ["max_records"] = new JsonObject
                        {
                            ["type"]        = "integer",
                            ["description"] = "Dışa aktarılacak maksimum kayıt sayısı. Kullanıcı sayı belirtmişse (ör. '1000 kayıt') buraya yaz. Tüm kayıtlar isteniyorsa belirtme."
                        },
                        ["output_path"] = new JsonObject
                        {
                            ["type"]        = "string",
                            ["description"] = "Dosyanın kaydedileceği tam yol. Belirtilmezse masaüstüne kaydedilir."
                        }
                    }
                }
            }
        };
    }
}
