# Todoist Widget for Windows 11

Windows 11 Widget Board'da calisan Todoist gorev listesi widget'i.

Bugunun gorevlerini ve geciken gorevleri oncelik sirasina gore listeler. Widget uzerinden gorev tamamlama destegi sunar.

## Ozellikler

- Bugun + geciken gorevleri listeler
- Oncelik gostergesi (P1 kirmizi, P2 turuncu, P3 mavi, P4 beyaz)
- Widget uzerinden gorev tamamlama
- Geciken gorevlerde tarih gosterimi
- Offline durumda cache'den gosterim
- Medium ve Large widget boyut destegi
- Token girisini widget icinden yapar

## Gereksinimler

- Windows 11 (Build 22000+)
- .NET 8 SDK
- Visual Studio 2022/2025 (Windows App SDK workload)
- Developer Mode aktif
- Todoist hesabi + API token

## Kurulum

### 1. Developer Mode'u Ac

Ayarlar > Gizlilik ve guvenlik > Gelistiriciler icin > Gelistirici Modu > Ac

### 2. Projeyi Derle

```bash
git clone <repo-url>
cd todoist-widget
dotnet restore src/TodoistWidget/TodoistWidget.csproj
dotnet build src/TodoistWidget/TodoistWidget.csproj -c Debug -p:Platform=x64
```

### 3. Widget'i Deploy Et

```powershell
Add-AppxPackage -Register "src\TodoistWidget\bin\x64\Debug\net8.0-windows10.0.22621.0\AppxManifest.xml"
```

### 4. Widget Board'a Ekle

1. `Win+W` ile Widget Board'u ac
2. Sag ustteki `+` butonuna tikla
3. "Todoist Gorevlerim" widget'ini bul ve pin et
4. Todoist API token'ini gir (Todoist > Ayarlar > Entegrasyonlar > Gelistirici)
5. Kaydet'e tikla

## Ekran Goruntuleri

*Widget Board'daki gorev listesi gorunumu*

## Mimari

```
Windows Widget Board
    |  COM activation
TodoistWidgetProvider (headless, no window)
    |-- IWidgetProvider lifecycle
    |-- TodoistApiClient (REST API v1)
    |-- Adaptive Cards JSON templates
    |-- CustomState persistence
```

- **COM Server**: Widget Board, uygulamayi `-RegisterProcessAsComServer` argumanıyla baslatir
- **Adaptive Cards**: UI tamamen JSON template + data binding ile tanimlanir
- **MSIX**: Loose file deployment ile sideload edilir (imza gerekmez)

## Teknoloji

| Bilesen | Teknoloji |
|---------|-----------|
| Runtime | .NET 8 |
| Widget SDK | Windows App SDK 1.6 |
| UI | Adaptive Cards 1.6 |
| API | Todoist REST API v1 |
| Paketleme | MSIX (loose file) |

## Lisans

MIT
