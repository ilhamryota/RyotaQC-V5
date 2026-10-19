# RyotaQC V5 - Tool Placement & Launcher IDs

Semua launcher eksternal dibaca dari:
- `src/RyotaQC.App/Config/tool-map.json`

## Contoh Launcher ID yang aktif
- `disk_management` -> `diskmgmt.msc`
- `user_accounts` -> `control.exe /name Microsoft.UserAccounts`
- `hwinfo` -> `tools/qc/HW Information/HWiNFO64.exe`
- `vcpp` -> `tools/addons/offline/VC++/VisualCppRedist_AIO_x86_x64.exe`
- `chrome` -> `tools/addons/offline/Chrome/ChromeStandaloneSetup64.exe`

## Driver Pack
- Dell (submenu): `dell_netsdk` (dotnet-sdk), `dell_dell` (Dell Command Update)
- Lenovo (single): `driver_lenovo`
- HP (single): `driver_hp`

## Apps Add-ons
- Online: `apps_online_launcher`
- Offline: `vcpp`, `directx`, `canva`, `chrome`, `firefox`, `vlc`, `winrar`, `zoom`, `teamviewer`
- Catatan: halaman Apps & Addons (offline) tidak membaca tool-map.json; ia mem-scan otomatis folder `tools/addons/offline/<NamaApp>/` (lihat `AppsAddonsPageViewModel.ResolveOfflineTargetExe`). Entry di tool-map.json hanya untuk referensi/konsistensi.

## Quality Control External
- `qc_cpu_test`
- `hwinfo`
- `qc_sentinel`
- `qc_battery_health`
- `qc_benchmark`

## Internal QC Windows
- LCD Test
- Keyboard Test (external Keyboard.exe)
- Camera Test (URI `microsoft.windows.camera:`)
- Audio Test
- Touchpad / Touchscreen / Mic Test (internal windows)
- Running Test (VLC + battery logger ke Desktop)
- Battery Report (generate HTML)

## Logging
- `logs/app.log`
