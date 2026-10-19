# RyotaQC V5 - UI Asset Manifest

Berikut file gambar/icon yang bisa kamu kustomisasi agar UI mendekati desain referensi `QCC.jpg`.

## 1) App Identity
- File: `src/RyotaQC.App/Assets/Images/Tiles/logo.ico`
- Format: `.ico` (32x32 / 64x64)
- Dipakai di: icon aplikasi, title bar, splash/about card

## 2) Main Menu Tile Background
Simpan file format `.jpg` atau `.png` di:
- `src/RyotaQC.App/Assets/Images/Tiles/`

Nama file yang dipakai aplikasi saat ini:
- `system-information.jpg` -> Tile `System Information`
- `driver-pack-system.jpg` -> Tile `Driver Pack System`
- `apps-addons.jpg` -> Tile `Apps Add-ons`
- `wallpaper-desktop.jpg` -> Tile `Wallpaper Desktop`
- `license-activation.jpg` -> Tile `License & Activation`
- `quality-control.jpg` -> Tile `Quality Control`

## 3) Main Window Background Reference
- File: `src/RyotaQC.App/Assets/Images/Backgrounds/qcc-reference.jpg`
- Format: `.jpg` (disarankan 1920x1080 atau lebih)
- Dipakai di: background utama shell (opacity rendah)

## 4) Wallpaper Desktop Thumbnails
Simpan file `.jpg` di:
- `src/RyotaQC.App/Assets/Images/Wallpapers/`

Nama file aktif dari config:
- `WP1.jpg`
- `WP2.jpg`
- `WP3.jpg`
- `WP4.jpg`
- `WP5.jpg`

Mapping dibaca dari:
- `src/RyotaQC.App/Config/wallpaper-map.json`

## 5) Optional Sidebar Icon Images (jika nanti mau pindah dari glyph)
Folder siap pakai:
- `src/RyotaQC.App/Assets/Images/Icons/`

Rekomendasi nama:
- `sidebar-home.png`
- `sidebar-system.png`
- `sidebar-apps.png`
- `sidebar-wallpaper.png`
- `sidebar-about.png`

Saat ini sidebar masih memakai glyph vector internal (bukan file image), jadi folder icons masih opsional.
