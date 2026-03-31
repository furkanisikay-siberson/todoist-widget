# Todoist Widget - Claude Code Talimatlari

## Proje Ozeti

Windows 11 Widget Board'da calisan Todoist gorev listesi widget'i. Headless COM server mimarisi, Adaptive Cards UI, Todoist API v1 entegrasyonu.

## Teknoloji

- .NET 8 (`net8.0-windows10.0.22621.0`)
- Windows App SDK 1.6 (self-contained)
- Adaptive Cards 1.6
- Todoist API v1 (`https://api.todoist.com/api/v1/`)
- MSIX loose file deployment (Developer Mode gerekli)

## Proje Yapisi

```
src/TodoistWidget/
  Program.cs                    # COM server entry point
  WidgetProviderFactory.cs      # COM class factory (IClassFactory)
  TodoistWidgetProvider.cs      # IWidgetProvider - tum yasam dongusu
  TodoistApiClient.cs           # Todoist REST API v1 istemcisi
  TemplateRenderer.cs           # Adaptive Card sablon + veri baglama
  Models/
    TodoistTask.cs              # Task + Due + ApiResponse modelleri
    WidgetState.cs              # Widget state + JSON source gen context
  Templates/
    SetupCard.json              # Token giris formu
    TaskListCard.json           # Gorev listesi (responsive)
    ErrorCard.json              # Hata kartlari
    EmptyCard.json              # Bos durum
  Package.appxmanifest          # MSIX + COM + Widget declaration
  Assets/                       # Widget ikonlari (placeholder)
```

## Kritik Kurallar

### GUID Eslesmesi (3 Yerde Ayni Olmali)
1. `TodoistWidgetProvider.cs` -> `[Guid("E7A4B53C-1F8D-4D2A-9C6E-3B5A8D2F1E70")]`
2. `Package.appxmanifest` -> `<com:Class Id="E7A4B53C-...">`
3. `Package.appxmanifest` -> `<CreateInstance ClassId="E7A4B53C-...">`

### Definition ID Eslesmesi
- `TodoistWidgetProvider.DefinitionId` == `Package.appxmanifest <Definition Id=...>`

### Todoist API v1 Farkliliklari
- Base URL: `https://api.todoist.com/api/v1/` (v2 artik 410 Gone)
- Filtre: `tasks/filter?query=today%20%7C%20overdue` (`tasks?filter=` calismaz)
- Yanit formati: `{"results": [...]}` wrapper (duz array degil)
- Alan farklari: `checked` (v2: `is_completed`), `child_order` (v2: `order`)
- Due date: ISO datetime `"2026-02-11T07:00:00"` formati

### HttpClient
- `SocketsHttpHandler` singleton paylasimi (socket exhaustion onlemi)
- `disposeHandler: false` ile HttpClient olustur
- BaseAddress sonunda `/` olmali, relative URI basinda `/` olmamali

### Thread Safety
- `_states` dictionary erisimi `lock(_lock)` ile
- `WidgetState` property mutasyonlari da `lock(_lock)` altinda
- IWidgetProvider metodlari senkron, async isler `_ = SomeAsync()` ile

### Adaptive Cards
- Widget Board'da scroll yok
- Medium: max 7 gorev, Large: max 15 gorev
- `TruncateContent` StringInfo ile - surrogate pair/emoji guvenli
- `$when` ile boyuta gore responsive layout

## Build ve Deploy

```bash
# Build
dotnet build src/TodoistWidget/TodoistWidget.csproj -c Debug -p:Platform=x64

# Deploy (Developer Mode gerekli)
powershell -Command "Get-AppxPackage -Name '*TodoistWidget*' | Remove-AppxPackage -ErrorAction SilentlyContinue"
powershell -Command "Add-AppxPackage -Register 'path\to\bin\x64\Debug\...\AppxManifest.xml'"
taskkill /F /IM "Widgets.exe"
```

## Debug Log
- Widget provider: `%LOCALAPPDATA%\TodoistWidget\widget.log`
- COM server: `%LOCALAPPDATA%\TodoistWidget\debug.log`
