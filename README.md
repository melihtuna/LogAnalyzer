# LogAnalyzer

`LogAnalyzer`, ASP.NET Core Web API ile geliştirilmiş, log verilerini yapay zeka desteğiyle analiz eden bir backend servisidir. Uygulama, gelen logları ön işlemden geçirir, kritik satırları modele gönderir ve standart bir JSON formatında teknik analiz çıktısı üretir.

## Projenin Amacı

- Operasyon ve geliştirme ekiplerinin loglardan hızlı içgörü almasını sağlamak
- Tekrarlayan ve kritik hataları daha net görünür kılmak
- Yapay zekayı karar destek mekanizması olarak kullanarak kök neden analizini hızlandırmak

## Temel Özellikler

- `POST /api/log/analyze` endpoint’i
- Katmanlı akış: `Controller -> Service -> AI Client -> Response`
- Log ön işleme: sadece `ERROR` satırlarını çıkarma, yoksa tüm logu kullanma
- Ollama uyumlu AI entegrasyonu: `http://localhost:11434/api/generate`
- Hash tabanlı in-memory cache ile tekrar eden analizleri hızlandırma
- `async/await` tabanlı asenkron işlem akışı
- Dependency Injection ile gevşek bağlı, test edilebilir yapı

## Mimari Yapı

Proje içinde sorumluluklar sınıflara ayrılmıştır:

- `Controllers/LogController.cs`
  - HTTP isteğini alır, doğrular, servise yönlendirir.
- `Services/LogAnalysisService.cs`
  - İş kurallarını uygular.
  - Cache kontrolü yapar.
  - Log parser ve AI client çağrılarını orkestre eder.
- `AI/AiClient.cs`
  - Ollama `generate` endpoint’ine istek atar.
  - Model çıktısını `LogResponse` yapısına dönüştürür.
- `Tools/LogParser.cs`
  - `ERROR` satırlarını ayıklar.
- `Models/LogRequest.cs` ve `Models/LogResponse.cs`
  - API sözleşmesini (request/response modelini) tanımlar.

## Kullanılan Teknolojiler

- .NET 8
- ASP.NET Core Web API
- Swagger / OpenAPI
- HttpClientFactory
- Ollama (`llama3`)

## Gereksinimler

- .NET SDK 8+
- Çalışan Ollama servisi
- Yüklü model: `llama3`

Model kontrolü için:

```powershell
ollama list
```

## Projeyi Çalıştırma

1. Depoyu klonlayın:

```bash
git clone <repo-url>
cd LogAnalyzer
```

2. API projesine geçin:

```bash
cd LogAnalyzer
```

3. Uygulamayı başlatın:

```bash
dotnet run
```

4. Swagger arayüzüne gidin:

- `http://localhost:<port>/swagger`

Not: Port, `Properties/launchSettings.json` dosyasındaki profile göre değişebilir.

## API Kullanımı

### Endpoint

- `POST /api/log/analyze`

### Örnek İstek

```json
{
  "logs": "2026-04-27 10:00:01 INFO startup\n2026-04-27 10:00:05 ERROR Database timeout on OrdersDb\n2026-04-27 10:00:08 WARN retrying"
}
```

### Örnek Yanıt

```json
{
  "summary": "Database timeout error occurred while processing orders",
  "rootCause": "OrdersDb connection timed out",
  "severity": "High",
  "suggestion": "Check database connections and consider implementing connection pooling or retry logic to mitigate timeouts"
}
```

## Cache Stratejisi

- Cache anahtarı: log metninin `SHA-256` hash değeri
- Aynı log tekrar gönderildiğinde AI çağrısı yapılmadan sonuç cache’den döner
- Bu yaklaşım yanıt süresini düşürür ve model çağrısı maliyetini azaltır

## Prompt Yaklaşımı

Modelden aşağıdaki alanlarda teknik ve kısa analiz istenir:

- `summary`
- `rootCause`
- `severity` (`Low`, `Medium`, `High`)
- `suggestion`

Yanıtın yapılandırılmış JSON dönmesi hedeflenir.

## Geliştirme Notları

- Uygulama, tek servis odaklı basit bir başlangıç mimarisi sunar.
- İlerleyen sürümlerde aşağıdaki iyileştirmeler eklenebilir:
  - Kalıcı cache (`Redis`)
  - Kimlik doğrulama ve oran sınırlama
  - Gözlemlenebilirlik (structured logging, metrics, tracing)
  - Entegrasyon ve yük testleri

## Lisans

Bu proje eğitim ve geliştirme amaçlıdır. Gerekirse depo politikalarınıza uygun bir lisans dosyası (`LICENSE`) ekleyebilirsiniz.
