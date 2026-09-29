# Workspace Guidelines for Medical Text Expander

## Project Location
- **Primary Working Directory**: E:\App\Medical_Text_Expander
- All code modifications, compilations, configuration updates, and documentation must ALWAYS be saved directly to E:\App\Medical_Text_Expander.

## Build & Dependencies
- Architecture: Single-file C# WinForms application for .NET Framework 4.8.
- Compile command:
  C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:Medical_Text_Expander.exe /win32icon:medical_expander_icon.ico /codepage:65001 /utf8output /r:System.Windows.Forms.dll,System.Drawing.dll,System.dll,System.Core.dll MedicalTextExpander.cs
- Must maintain zero external NuGet packages or external runtime dependencies.

## Auto-Update & GitHub Release
- GitHub Repository: oatzilla/Medical_Text_Expander
- Always keep ersion.json and AppUpdater.CurrentVersion in sync when publishing a new version.
- Ensure downloadUrl and download_url point to the GitHub latest release asset.

## Privacy & HIPAA/PHI
- Never commit patient note files (ed_notes/), ward_reminders.txt, or local logs to GitHub.
