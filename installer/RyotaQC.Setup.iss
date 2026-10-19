#ifndef AppName
  #define AppName "RyotaQC V5"
#endif

#ifndef AppVersion
  #define AppVersion "5.1.0"
#endif

#ifndef AppPublisher
  #define AppPublisher "RyotaQC"
#endif

#ifndef AppExeName
  #define AppExeName "RyotaQC.App.exe"
#endif

#ifndef AppSourceDir
  #define AppSourceDir "..\release\staging\RyotaQC-V5"
#endif

#ifndef OutputDir
  #define OutputDir "..\release\setup"
#endif

[Setup]
AppId={{F4FCF1F5-916B-4D6D-9A99-0A94C4A8A6B7}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\RyotaQC\RyotaQC V5
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=RyotaQC-V5-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
SetupIconFile={#AppSourceDir}\Assets\Images\Tiles\logo.ico
UninstallDisplayIcon={app}\{#AppExeName}
DiskSpanning=yes
DiskSliceSize=2000000000

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "{#AppSourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  InstallStartTick: LongWord;
  LastCurProgress: Integer;
  LastMaxProgress: Integer;
  ProgressInfoLabel: TNewStaticText;
  RemainingInfoLabel: TNewStaticText;
  TotalEstimateLabel: TNewStaticText;
  FooterDevLabel: TNewStaticText;

function GetTickCount: LongWord;
external 'GetTickCount@kernel32.dll stdcall';

function FormatDuration(SecondsValue: Integer): string;
var
  HoursValue, MinutesValue, SecsValue: Integer;
begin
  if SecondsValue < 0 then
    SecondsValue := 0;

  HoursValue := SecondsValue div 3600;
  MinutesValue := (SecondsValue mod 3600) div 60;
  SecsValue := SecondsValue mod 60;
  Result := Format('%.2d:%.2d:%.2d', [HoursValue, MinutesValue, SecsValue]);
end;

function GetElapsedSeconds: Integer;
begin
  Result := Integer((GetTickCount - InstallStartTick) div 1000);
  if Result < 0 then
    Result := 0;
end;

procedure UpdateInstallTimingLabels;
var
  ElapsedSeconds, RemainingSeconds, TotalSeconds: Integer;
  ProgressRatio: Extended;
begin
  ElapsedSeconds := GetElapsedSeconds;

  if LastMaxProgress <= 0 then
  begin
    ProgressInfoLabel.Caption := Format('Elapsed: %s | Progress: 0%%', [FormatDuration(ElapsedSeconds)]);
    RemainingInfoLabel.Caption := 'Estimated remaining: calculating...';
    TotalEstimateLabel.Caption := 'Estimated total duration: calculating...';
    exit;
  end;

  ProgressRatio := LastCurProgress / LastMaxProgress;

  if ProgressRatio > 0 then
  begin
    TotalSeconds := Round(ElapsedSeconds / ProgressRatio);
    RemainingSeconds := TotalSeconds - ElapsedSeconds;
  end
  else
  begin
    TotalSeconds := 0;
    RemainingSeconds := 0;
  end;

  if RemainingSeconds < 0 then
    RemainingSeconds := 0;

  if TotalSeconds < 0 then
    TotalSeconds := 0;

  ProgressInfoLabel.Caption :=
    Format('Elapsed: %s | Progress: %d%%', [FormatDuration(ElapsedSeconds), (LastCurProgress * 100) div LastMaxProgress]);
  RemainingInfoLabel.Caption := 'Estimated remaining: ' + FormatDuration(RemainingSeconds);
  TotalEstimateLabel.Caption := 'Estimated total duration: ' + FormatDuration(TotalSeconds);
end;

procedure InitializeWizard;
begin
  ProgressInfoLabel := TNewStaticText.Create(WizardForm);
  ProgressInfoLabel.Parent := WizardForm;
  ProgressInfoLabel.Left := WizardForm.ProgressGauge.Left;
  ProgressInfoLabel.Top := WizardForm.ProgressGauge.Top + WizardForm.ProgressGauge.Height + ScaleY(8);
  ProgressInfoLabel.Width := WizardForm.ProgressGauge.Width;
  ProgressInfoLabel.Caption := 'Elapsed: 00:00:00 | Progress: 0%';

  RemainingInfoLabel := TNewStaticText.Create(WizardForm);
  RemainingInfoLabel.Parent := WizardForm;
  RemainingInfoLabel.Left := WizardForm.ProgressGauge.Left;
  RemainingInfoLabel.Top := ProgressInfoLabel.Top + ProgressInfoLabel.Height + ScaleY(4);
  RemainingInfoLabel.Width := WizardForm.ProgressGauge.Width;
  RemainingInfoLabel.Caption := 'Estimated remaining: calculating...';

  TotalEstimateLabel := TNewStaticText.Create(WizardForm);
  TotalEstimateLabel.Parent := WizardForm;
  TotalEstimateLabel.Left := WizardForm.ProgressGauge.Left;
  TotalEstimateLabel.Top := RemainingInfoLabel.Top + RemainingInfoLabel.Height + ScaleY(4);
  TotalEstimateLabel.Width := WizardForm.ProgressGauge.Width;
  TotalEstimateLabel.Caption := 'Estimated total duration: calculating...';

  FooterDevLabel := TNewStaticText.Create(WizardForm);
  FooterDevLabel.Parent := WizardForm;
  FooterDevLabel.AutoSize := False;
  FooterDevLabel.Left := ScaleX(10);
  FooterDevLabel.Top := WizardForm.ClientHeight - ScaleY(28);
  FooterDevLabel.Width := WizardForm.ClientWidth - ScaleX(20);
  FooterDevLabel.Height := ScaleY(16);
  FooterDevLabel.Alignment := taCenter;
  FooterDevLabel.Caption := 'Dev By IlhamRyota for RyotaQC V5 apps Freeware';

  InstallStartTick := GetTickCount;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    InstallStartTick := GetTickCount;
    LastCurProgress := 0;
    LastMaxProgress := 0;
    UpdateInstallTimingLabels;
  end;

  if (CurStep = ssPostInstall) or (CurStep = ssDone) then
  begin
    UpdateInstallTimingLabels;
  end;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  LastCurProgress := CurProgress;
  LastMaxProgress := MaxProgress;
  UpdateInstallTimingLabels;
end;
