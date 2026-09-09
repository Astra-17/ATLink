#define AppVersion "0.1.0"
[Setup]
AppId={{93D56B0E-EF9F-4C77-9532-9D47A69852A4}
AppName=ATLink Studio
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\ATLink Studio
DefaultGroupName=ATLink Studio
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=ATLink-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\ATLink.exe

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ATLink Studio"; Filename: "{app}\ATLink.exe"

[Run]
Filename: "{app}\ATLink.exe"; Description: "Launch ATLink Studio"; Flags: nowait postinstall skipifsilent

[Code]
var LanguagePage: TInputOptionWizardPage;
procedure InitializeWizard;
begin
  LanguagePage := CreateInputOptionPage(wpSelectDir, 'Database language / Langue de la DB',
    'Choose your database language', 'This can be changed later in System settings.', True, False);
  LanguagePage.Add('English');
  LanguagePage.Add('Français');
  if ActiveLanguage = 'fr' then LanguagePage.SelectedValueIndex := 1
  else LanguagePage.SelectedValueIndex := 0;
end;
procedure CurStepChanged(CurStep: TSetupStep);
var Code: String;
begin
  if CurStep = ssPostInstall then begin
    if LanguagePage.SelectedValueIndex = 1 then Code := 'fre_fr' else Code := 'eng_us';
    if not SaveStringToFile(ExpandConstant('{app}\Data\default-language.txt'), Code, False) then
      RaiseException('Cannot save the database language choice.');
  end;
end;
