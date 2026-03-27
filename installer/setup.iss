; AutoSizeStrategy Installer
; Built with Inno Setup — https://jrsoftware.org/isinfo.php
; Install Inno Setup
;    winget install JRSoftware.InnoSetup
;
; To compile locally:
;   iscc setup.iss
;
; To compile with a version override (used by CI):
;   iscc /DMyAppVersion=1.3.0 setup.iss

#ifndef MyAppVersion
  #define MyAppVersion "2.0.0"
#endif

#define MyAppName "AutoSizeStrategy"
#define MyAppPublisher "Structured Trading LLC"
#define MyAppURL "https://github.com/moravsky/auto-size-strategy"
#define StrategySubDir "\Settings\Scripts\Strategies\AutoSizeStrategy"

[Setup]
AppId={{B7E3A1F0-5C82-4D9A-8F1E-2A6B9C0D4E5F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName=C:\Quantower{#StrategySubDir}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=AutoSizeStrategy-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
Uninstallable=no
UsePreviousAppDir=no
DirExistsWarning=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
SelectDirLabel3=This is where the strategy files will be installed.%nIf your Quantower is in a different location, click Browse and navigate to it.
SelectDirBrowseLabel=Click Next to install, or click Browse to choose a different folder.

[Files]
Source: "..\AutoSizeStrategy\bin\Release\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\AutoSizeStrategy\bin\Release\*.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\AutoSizeStrategy\bin\Release\*.deps.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Code]
function IsQuantowerRoot(const Path: string): Boolean;
begin
  Result := DirExists(Path + '\TradingPlatform');
end;

// Gets Quantower root from a running Starter.exe process via WMI
function GetQuantowerPathFromProcess: string;
var
  WbemLocator, WbemServices, WbemObjectSet, WbemObject: Variant;
  ExePath, RootPath: string;
  SlashPos, I: Integer;
begin
  Result := '';
  try
    WbemLocator := CreateOleObject('WbemScripting.SWbemLocator');
    WbemServices := WbemLocator.ConnectServer('', 'root\cimv2');
    WbemObjectSet := WbemServices.ExecQuery(
      'SELECT ExecutablePath FROM Win32_Process WHERE Name = ''Starter.exe'''
    );

    if not VarIsNull(WbemObjectSet) then
    begin
      for I := 0 to WbemObjectSet.Count - 1 do
      begin
        WbemObject := WbemObjectSet.ItemIndex(I);
        if not VarIsNull(WbemObject) then
        begin
          ExePath := WbemObject.ExecutablePath;
          if ExePath <> '' then
          begin
            // Walk up from ..\TradingPlatform\vXXX\Starter.exe to root
            SlashPos := RPos('\', ExePath);
            if SlashPos > 0 then
              RootPath := Copy(ExePath, 1, SlashPos - 1);
            SlashPos := RPos('\', RootPath);
            if SlashPos > 0 then
              RootPath := Copy(ExePath, 1, SlashPos - 1);
            SlashPos := RPos('\', RootPath);
            if SlashPos > 0 then
              RootPath := Copy(RootPath, 1, SlashPos - 1);

            if IsQuantowerRoot(RootPath) then
            begin
              Result := RootPath;
              Exit;
            end;
          end;
        end;
      end;
    end;
  except
  end;
end;

function DetectQuantowerRoot: string;
begin
  // 1. Standard location
  if IsQuantowerRoot('C:\Quantower') then
  begin
    Result := 'C:\Quantower';
    Exit;
  end;

  // 2. Running process (catches non-standard paths)
  Result := GetQuantowerPathFromProcess;
  if Result <> '' then
    Exit;

  // 3. Fallback
  Result := 'C:\Quantower';
end;

procedure InitializeWizard;
begin
  WizardForm.DirEdit.Text := DetectQuantowerRoot + ExpandConstant('{#StrategySubDir}');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  SelectedDir, QuantowerRoot: string;
  SuffixPos: Integer;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    SelectedDir := WizardForm.DirEdit.Text;

    // Try to extract the Quantower root by stripping the strategy suffix
    SuffixPos := Pos('\Settings\Scripts\Strategies\AutoSizeStrategy', SelectedDir);
    if SuffixPos > 0 then
      QuantowerRoot := Copy(SelectedDir, 1, SuffixPos - 1)
    else
      QuantowerRoot := SelectedDir;

    if not IsQuantowerRoot(QuantowerRoot) then
    begin
      if MsgBox('No TradingPlatform folder found at:' + #13#10 +
                QuantowerRoot + #13#10#13#10 +
                'This might not be a Quantower installation.' + #13#10 +
                'Continue anyway?',
                mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
      end;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  DestDir: string;
begin
  if CurStep = ssInstall then
  begin
    DestDir := ExpandConstant('{app}');
    if DirExists(DestDir) then
      DelTree(DestDir, True, True, True);
  end;
end;
