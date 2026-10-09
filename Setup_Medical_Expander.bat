@echo off
chcp 65001 >nul
title ติดตั้ง Medical Text Expander (สำหรับ e-PHIS)
color 1F

echo =======================================================================
echo   🏥 ติดตั้งโปรแกรม Medical Text Expander (ระบบช่วยงานพยาบาล e-PHIS)
echo =======================================================================
echo.

set "TARGET_DIR=%~dp0"
set "TARGET_DIR=%TARGET_DIR:~0,-1%"

echo [1/3] กำลังปิดโปรแกรมรุ่นเดิม (หากกำลังเปิดอยู่)...
taskkill /F /IM Medical_Text_Expander.exe >nul 2>&1
timeout /t 1 /nobreak >nul

echo [2/3] สร้างไอคอนทางลัดบน Desktop...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws = New-Object -ComObject WScript.Shell; $d = [Environment]::GetFolderPath('Desktop'); $s = $ws.CreateShortcut($d + '\Medical Text Expander.lnk'); $s.TargetPath = '%TARGET_DIR%\Medical_Text_Expander.exe'; $s.WorkingDirectory = '%TARGET_DIR%'; $s.IconLocation = '%TARGET_DIR%\medical_expander_icon.ico'; $s.Description = 'โปรแกรมช่วยพิมพ์และบันทึกข้อมูลผู้ป่วยรายเตียง'; $s.Save()"

echo [3/3] ตั้งค่าให้เปิดโปรแกรมอัตโนมัติพร้อม Windows (Startup)...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws = New-Object -ComObject WScript.Shell; $st = [Environment]::GetFolderPath('Startup'); $s = $ws.CreateShortcut($st + '\Medical Text Expander.lnk'); $s.TargetPath = '%TARGET_DIR%\Medical_Text_Expander.exe'; $s.WorkingDirectory = '%TARGET_DIR%'; $s.IconLocation = '%TARGET_DIR%\medical_expander_icon.ico'; $s.Save()"

echo.
echo =======================================================================
echo   ✅ ติดตั้งเรียบร้อยแล้ว พร้อมใช้งานทันที!
echo.
echo   📌 ปุ่มลัดสำคัญ:
echo      • F8     : เปิดคลังเทมเพลต (Template Palette)
echo      • F7     : เปิดสมุดบันทึกข้อมูลรายเตียง 1-30 (Bed Notes)
echo      • Alt+T  : เปิดกระดานเตือนหัตถการ (Ward Task Reminders)
echo      • Alt+C  : เปิดเครื่องคิดเลขคำนวณ SOS, ยา, ABG, น้ำเกลือ
echo      • .b1-.b30 : พิมพ์ใน e-PHIS แล้วกด Spacebar เพื่อดึงโน้ตเตียงนั้น
echo =======================================================================
echo.

start "" "%TARGET_DIR%\Medical_Text_Expander.exe"
echo กำลังเปิดโปรแกรม...
timeout /t 3 /nobreak >nul
