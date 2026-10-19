# RyotaQC V5 Packaging (Setup EXE + Portable)

## Ringkas
Urutan paling aman:
1. Publish dan staging release.
2. Build Setup EXE (Inno Setup).
3. Build Portable folder (opsional ZIP).

## Prasyarat
1. .NET SDK 8 terpasang.
2. Inno Setup 6 terpasang (untuk Setup EXE).
3. Jalankan PowerShell dari root project.

## 1) Publish release + staging
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\01-publish-release.ps1 -Version 5.1.0
```

Output utama:
- `release\staging\RyotaQC-V5\`

## 2) Build Setup EXE (Inno Setup)
Jika Inno Setup terinstall default:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\02-build-setup.ps1 -Version 5.1.0
```

Jika lokasi `ISCC.exe` custom:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\02-build-setup.ps1 -Version 5.1.0 -InnoCompilerPath "C:\Path\To\ISCC.exe"
```

Output:
- `release\setup\RyotaQC-V5-Setup-x64.exe`

## 3) Build Portable
### Opsi cepat (direkomendasikan): folder portable
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\03-build-portable.ps1 -Version 5.1.0
```

Output:
- `release\portable\RyotaQC-V5-Portable-win-x64-v5.1.0\`

### Opsi tambahan: sekalian ZIP
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\03-build-portable.ps1 -Version 5.1.0 -CreateZip
```

Output tambahan:
- `release\portable\RyotaQC-V5-Portable-win-x64-v5.1.0.zip`

## Catatan penting
1. Folder `tools\` otomatis ikut ke paket release.
2. Portable besar karena isi tool installer.
3. Untuk nama Publisher trusted di Windows, tetap perlu code-signing certificate.
