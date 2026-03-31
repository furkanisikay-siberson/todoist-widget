# Todoist Widget for Windows 11 - Design Document

## Ozet

Windows 11 Widget Board'da calisan, Todoist API ile entegre, bugunun gorevlerini ve geciken gorevleri gosteren, gorev tamamlama destekli bir widget.

## Gereksinimler

- Bugun + geciken gorevleri listele
- Widget uzerinden gorev tamamlama (checkbox)
- Medium ve Large boyut destegi
- API token girisini widget icerisinden yap (Adaptive Card form)
- Masaustu penceresi yok - sadece arka plan COM server

## Mimari

Headless C# COM server + Windows App SDK. Tum UI Adaptive Cards JSON sablonlariyla tanimlanir.

```
[Windows Widget Board]
    |  COM activation (-RegisterProcessAsComServer)
[TodoistWidgetProvider] (WinExe, no window)
    |--- IWidgetProvider (CreateWidget, DeleteWidget, Activate, Deactivate, OnActionInvoked)
    |--- TodoistApiClient (HttpClient -> Todoist REST v2)
    |--- Adaptive Cards JSON templates (data binding ile)
    |--- CustomState persistence (token + cache)
```

## Proje Yapisi

```
TodoistWidget/
  TodoistWidget.csproj
  Package.appxmanifest
  Program.cs                    (COM server entry point)
  WidgetProvider.cs             (IWidgetProvider)
  TodoistApiClient.cs           (REST v2 client)
  Models/
    TodoistTask.cs
    WidgetState.cs
  Templates/
    SetupCard.json              (token giris formu)
    TaskListMedium.json
    TaskListLarge.json
    ErrorCard.json
    EmptyCard.json
  Assets/
    widget-icon.png (+ scale variants)
```

## Akis

1. Pin -> CreateWidget -> token yoksa SetupCard goster
2. Token girilir -> Action.Execute(verb:saveToken) -> CustomState'e kaydet -> gorev cek
3. GET /tasks?filter=today | overdue -> Adaptive Card'a bind et -> UpdateWidget
4. Checkbox tikla -> Action.Execute(verb:completeTask, taskId) -> POST /tasks/{id}/close -> listeyi yenile
5. Activate (widget gorunur) -> gorevleri yeniden cek

## Widget Boyutlari

### Medium
- 5-6 gorev
- Gorev adi + oncelik renk gostergesi
- Tamamlama checkbox'i

### Large
- 10+ gorev
- Geciken gorevlere ayri baslik
- Proje adi gosterimi
- Oncelik renk gostergesi
- Tamamlama checkbox'i

## Token Saklama

CustomState JSON: {"token": "...", "lastTasks": [...], "lastFetch": "ISO8601"}

## Hata Yonetimi

| Durum | Davranis |
|-------|---------|
| Token yok | SetupCard goster |
| Token gecersiz (401) | ErrorCard + yeniden giris imkani |
| API ulasilamaz | Cache'den goster + "Cevrimdisi" uyarisi |
| 429 Rate Limit | Cache'den goster, Retry-After sonrasi tekrar dene |
| Bos liste | EmptyCard: "Bugun gorev yok!" |

## Teknoloji

- .NET 8 (net8.0-windows10.0.22621.0)
- Windows App SDK 1.6+
- System.Net.Http.Json
- System.Text.Json
- MSIX sideload (Developer Mode)
