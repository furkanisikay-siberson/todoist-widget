# Todoist Widget - Kullanim Kilavuzu

## Ilk Kurulum

### Adim 1: Developer Mode

Windows 11'de sideload uygulamalar icin Developer Mode gereklidir.

1. **Ayarlar** uygulamasini ac (`Win+I`)
2. **Gizlilik ve guvenlik** > **Gelistiriciler icin** menusune git
3. **Gelistirici Modu** toggle'ini ac
4. Onay dialogunda **Evet** tikla

### Adim 2: Projeyi Derle

```bash
# Repoyu klonla
git clone <repo-url>
cd todoist-widget

# Bagimlilikları yukle ve derle
dotnet restore src/TodoistWidget/TodoistWidget.csproj
dotnet build src/TodoistWidget/TodoistWidget.csproj -c Debug -p:Platform=x64
```

### Adim 3: Widget'i Sisteme Kaydet

PowerShell'de (yonetici yetkisi gerekmez):

```powershell
Add-AppxPackage -Register "D:\path\to\todoist-widget\src\TodoistWidget\bin\x64\Debug\net8.0-windows10.0.22621.0\AppxManifest.xml"
```

### Adim 4: Widget Board'a Ekle

1. `Win+W` tusuna bas (veya gorev cubugundaki Widgets simgesine tikla)
2. Sag ustteki **+** butonuna tikla
3. Listede **"Todoist Gorevlerim"** widget'ini bul
4. **Pin** butonuna tikla

### Adim 5: API Token Girisi

1. Widget'ta token giris ekrani gorunecek
2. Todoist API token'ini al:
   - [Todoist web](https://app.todoist.com) > **Ayarlar** > **Entegrasyonlar** > **Gelistirici** sekmesi
   - "API token" bolumundeki token'i kopyala
3. Token'i widget'taki metin kutusuna yapistir
4. **Kaydet** butonuna tikla
5. Gorevlerin gorunmesini bekle

---

## Gunluk Kullanim

### Gorevleri Goruntuleme

Widget, iki kategoriyi gosterir:
- **Geciken**: Tarihi gecmis gorevler (kirmizi baslik)
- **Bugun**: Bugunun gorevleri

Gorevler oncelik sirasina gore listelenir:
- \ud83d\udd34 P1 (Acil)
- \ud83d\udfe0 P2 (Yuksek)
- \ud83d\udd35 P3 (Orta)
- \u26aa P4 (Normal)

### Gorev Tamamlama

Her gorevin yanindaki **Done** butonuna tiklayin. Gorev aninda listeden kalkar ve Todoist'te tamamlanir.

Tekrarlayan gorevlerde (orn: "Her gun kitap oku") bir sonraki tekrar otomatik olusturulur.

### Listeyi Yenileme

Widget'in altindaki **Yenile** butonuna tiklayin. Todoist'ten guncel gorevler cekilir.

### Widget Boyutunu Degistirme

1. Widget'a sag tiklayin
2. **Boyut** menusunden **Orta** veya **Buyuk** secin
3. Buyuk boyutta daha fazla gorev gorunur

---

## Sorun Giderme

### Widget Board'da gorunmuyor

1. Developer Mode aktif mi kontrol edin
2. Paketi kaldirip yeniden kaydedin:
   ```powershell
   Get-AppxPackage -Name '*TodoistWidget*' | Remove-AppxPackage
   Add-AppxPackage -Register "...\AppxManifest.xml"
   ```
3. Widgets.exe'yi yeniden baslatin:
   ```powershell
   taskkill /F /IM "Widgets.exe"
   ```
4. Widget Board'u tekrar acin (`Win+W`)

### Token kabul edilmiyor

1. Token'in dogru kopyalandigindan emin olun (bosluk yok)
2. Todoist'te token'i yenileyin ve tekrar deneyin
3. Internet baglantinizi kontrol edin

### Gorevler gorunmuyor / bos ekran

1. Widget'taki **Yenile** butonuna tiklayin
2. Log dosyasini kontrol edin: `%LOCALAPPDATA%\TodoistWidget\widget.log`
3. Widget'i unpin edip tekrar pin edin

### "Token gecersiz" hatasi

1. Todoist > Ayarlar > Entegrasyonlar > Gelistirici'den yeni token alin
2. Widget'taki **Token Degistir** butonuna tiklayin
3. Yeni token'i girin

### Offline durumda

Widget internet baglantisi olmadan son bilinen gorev listesini gosterir. Baglantiyi kontrol etmek icin **Yenile** butonunu kullanin.

---

## Guncelleme

Yeni bir surum mevcut oldugunda:

```bash
git pull
dotnet build src/TodoistWidget/TodoistWidget.csproj -c Debug -p:Platform=x64
```

Sonra paketi yeniden kaydedin:

```powershell
Get-AppxPackage -Name '*TodoistWidget*' | Remove-AppxPackage
Add-AppxPackage -Register "...\AppxManifest.xml"
taskkill /F /IM "Widgets.exe"
```

---

## Kaldirma

```powershell
Get-AppxPackage -Name '*TodoistWidget*' | Remove-AppxPackage
```

Widget otomatik olarak Widget Board'dan kalkar.
