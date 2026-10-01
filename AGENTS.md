# Guidelines & Architectural Context for AI Agents - Medical Text Expander

เอกสารนี้สรุปข้อกำหนดทางสถาปัตยกรรม กฎการคอมไพล์ ข้อควรระวังพิเศษ และประวัติการแก้ไขในแต่ละเวอร์ชัน เพื่อให้ AI Agents และนักพัฒนาเข้าใจระบบและทำงานต่อได้อย่างถูกต้อง 100%

---

## 1. ที่ตั้งโปรเจกต์และข้อกำหนดไดเรกทอรีคู่ (Dual-Directory Rule)
1. **Primary Workspace (Git Repository Root)**: `E:\App\Medical_Text_Expander`
   - โค้ดต้นฉบับทั้งหมด การคอมไพล์ และการจัดการ Git (`main`, `gh-pages`) ต้องทำที่โฟลเดอร์นี้เสมอ
2. **User Runtime Directory**: `C:\Users\GORW01\Desktop\Medical_Text_Expander_Setup`
   - ผู้ใช้จะเรียกใช้งานและรันโปรแกรมจริงจากไดเรกทอรีนี้
   - **กฎเหล็ก (CRITICAL RULE)**: ทุกครั้งที่มีการคอมไพล์ `.exe`, อัปเดต `medical_templates.txt`, หรือแก้ไข `version.json` **ต้องคัดลอก (Sync) ไฟล์ดังกล่าวไปยัง `C:\Users\GORW01\Desktop\Medical_Text_Expander_Setup` เสมอ** มิฉะนั้นผู้ใช้จะไม่เห็นการเปลี่ยนแปลง

---

## 2. การคอมไพล์และข้อกำหนดทางเทคนิค (.NET 4.8 WinForms)
- **สถาปัตยกรรม**: C# Windows Forms รวมอยู่ในไฟล์เดียว (`MedicalTextExpander.cs`)
- **การพึ่งพา (Dependencies)**: **Zero external NuGet packages** (ใช้เฉพาะไลบรารีมาตรฐานของ .NET 4.8 เช่น `System.Windows.Forms`, `System.Drawing`, `System.Net`, `System.Diagnostics`)
- **คอมไพเลอร์**: `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
- **คำสั่งคอมไพล์ที่ถูกต้อง**:
  ```cmd
  C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:Medical_Text_Expander.exe /platform:anycpu /optimize+ /codepage:65001 /utf8output MedicalTextExpander.cs
  ```
- **ข้อควรระวังเรื่อง Encoding**:
  - ห้ามใช้แฟล็ก `/utf8` เพราะคอมไพเลอร์ csc.exe v4.8 ไม่รองรับ (จะเกิดข้อผิดพลาด CS2007) ให้ใช้ `/codepage:65001 /utf8output` เสมอเพื่อให้อ่านและแสดงผลภาษาไทยได้ถูกต้อง ไม่เกิดปัญหาตัวอักษรเพี้ยน

---

## 3. สถาปัตยกรรม Web & Mobile Portal (GitHub Pages)
- ไดเรกทอรีต้นทาง: `web/`
- ไดเรกทอรีสำหรับ GitHub Pages: `docs/` (ต้องซิงค์ไฟล์จาก `web/` มายัง `docs/` เสมอ)
- การ Deploy:
  - สาขา `main`: สำหรับจัดเก็บ Source Code ทั้งหมด
  - สาขา `gh-pages`: Deploy ด้วย Git Plumbing: `git write-tree --prefix=docs/`
- **Cache Busting**: ทุกครั้งที่แก้ `web/app.js` หรือ `web/style.css` ต้องเพิ่มเลขเวอร์ชันใน `index.html` (เช่น `app.js?v=1.5.6`) ทั้งใน `web/` และ `docs/`

---

## 4. กฎการจัดการการขึ้นบรรทัดใหม่ (Newline Normalization: CRLF vs LF)
- **ปัญหาทางเทคนิค**: เบราว์เซอร์บนเว็บ (`<textarea>`) จะจัดเก็บการขึ้นบรรทัดใหม่เป็น Unix LF (`\n`) ขณะที่กล่องข้อความ `TextBox` บน Windows WinForms (Win32 Edit Control) ต้องการ Windows CRLF (`\r\n`) เท่านั้น หากมีเฉพาะ `\n` ข้อความจะเรียงต่อกันเป็นแถวยาวพืด ไม่ยอมตัดบรรทัด
- **วิธีจัดการ**:
  - บน C# Desktop: ใช้ `BedNotesManager.NormalizeNewlines(string s)` แปลงทุกการขึ้นบรรทัดใหม่ให้เป็น `\r\n` เสมอ (ใน `UnescapeJson`, `GetBedNote`, `SaveBedNote`, `ReadFileSafe`, `WriteFileSafe`, `TxtNote_TextChanged`, `TxtNote_KeyDown`)
  - บน Web Portal: ใช้ฟังก์ชัน `normalizeToCRLF(text)` ใน `web/app.js` ก่อนส่งข้อมูลขึ้น Supabase Cloud และก่อนใส่คลิปบอร์ด

---

## 5. กฎความปลอดภัยของ UI และฟอนต์ภาษาไทย
- **SplitterDistance**: หลีกเลี่ยงข้อผิดพลาด `ArgumentOutOfRangeException` ของ SplitContainer โดยใช้ฟังก์ชัน `SetSafeSplitterDistance(int dist)` ที่ตรวจสอบ `Panel1MinSize` และ `Width - Panel2MinSize` เสมอ
- **รักษาแถบไฮไลท์ (`HideSelection = false`)**: ใน `RichTextBox` และ `TextBox` พรีวิว ต้องตั้งค่า `HideSelection = false` เพื่อไม่ให้แถบสีไฮไลท์หลุดหายเมื่อคลิกปุ่มบนแถบเครื่องมือ
- **ฟอนต์ภาษาไทย**: ใช้ `Leelawadee UI` เป็นค่าเริ่มต้น พร้อม Fallback ไปยัง `Segoe UI` เพื่อป้องกันปัญหากล่องสี่เหลี่ยม (Tofu boxes)

---

## 6. สรุปประวัติการแก้ไขและเวอร์ชันทั้งหมด (Version History Summary)

| เวอร์ชัน | วันที่ | หัวข้อหลัก | รายละเอียดการปรับปรุง |
|---|---|---|---|
| **v1.6.3** | 2026-10-01 | Goal Identification & Ortho Quick Keys | ระบุเป้าหมายทางการพยาบาล [GOAL] ในข้อวินิจฉัย/DAR ครบทั้ง 110 เทมเพลต และปรับปรุงปุ่มคีย์ด่วนสีขาวเป็นข้อวินิจฉัย Orthopedic ที่พบบ่อย 12 ปุ่ม (Pre-op, Post-op, Pain, Wound, Drain, CMS Check, Rehab, TKA, THA, Spine, Cast, Fall) แทรก DAR ลงเตียงได้ทันที |
| **v1.6.2** | 2026-10-01 | Action Line-by-Line Bullets | จัดรูปแบบกิจกรรมการพยาบาล [ACTION] ในเทมเพลตทั้งหมด 110 หัวข้อ แยกรายการทีละบรรทัดด้วยข้อ (- ) ไม่เรียงต่อกันเป็นเรียงความ ใช้งานง่ายบน Desktop, Web และ e-PHIS |
| **v1.6.1** | 2026-10-01 | Palliative, Neuro & Ventilator DAR | เพิ่ม 24 เทมเพลตใหม่: การพยาบาลระยะสุดท้าย, ระบบประสาท, ใส่เครื่องช่วยหายใจ และการกู้ชีพ/วิกฤต (ยอดรวมคลังขยายเป็น 110 หัวข้อ) พร้อมตัวกรองใหม่ |
| **v1.6.0** | 2026-10-01 | Admin Security & Remove Paste | นำปุ่มวางลง e-PHIS ออกจาก BedNotesForm ตามสั่ง, เพิ่มระบบล็อกรหัสผ่านผู้ดูแลระบบ (Admin Password: 9844) ก่อนเข้าหน้าต่างตั้งค่าเครือข่าย/ซิงค์วอร์ด และแก้ไขเทมเพลต |
| **v1.5.9** | 2026-10-01 | Safe Bed Modal & Start at Top | แก้ปัญหาป็อปอัพข้อมูลผู้ป่วยปิดตัวเองลงทำให้ข้อมูลหาย โดยเพิ่ม Safe Backdrop Click, ยืนยันก่อนปิด (Confirm Discard), ระบบสำรอง Real-Time Auto-Draft ใน LocalStorage 100%, และปรับให้เคอร์เซอร์และมุมมองเริ่มที่บรรทัดบนสุด |
| **v1.5.8** | 2026-10-01 | Drag & Drop Swapping & Compact View | ระบบลากสลับ/ย้ายเตียงผู้ป่วยด้วยเมาส์และทัชสกรีน (Touch Drag บน iPad/มือถือ) พร้อมปุ่มสลับมุมมองตารางเตียงแบบกะทัดรัด (Compact View) ไม่ต้องเลื่อนจอเยอะ |
| **v1.5.7** | 2026-10-01 | Patient Bed Swap & Transfer | เพิ่มฟังก์ชันสลับ/ย้ายเตียงผู้ป่วยทั้ง Desktop (ปุ่มสลับเตียง, BedSwapDialog, เมนูคลิกขวาเตียง 1-30) และ Web Portal (swapModal, ปุ่มย้าย/สลับบนการ์ด) พร้อมสำรองประวัติและสลับตัวเตือนหัตถการอัตโนมัติ |
| **v1.5.6** | 2026-09-30 | Universal Newline Normalization | แก้ปัญหาข้อความจากเว็บเรียงต่อกันไม่ยอมตัดบรรทัดบน Desktop โดยเพิ่ม `NormalizeNewlines` (`\r\n`) ใน C# และ `normalizeToCRLF` บน Web Portal พร้อมระบบ Auto-Heal ใน TextChanged |
| **v1.5.5** | 2026-09-30 | Smart Partial Selection Copy | แก้ปัญหาคัดลอกข้อความเฉพาะส่วนที่เลือก (`SelectedText`) แต่ได้ทั้งเทมเพลต เพิ่ม Right-Click Context Menu, Ctrl+A, และปุ่มแสดงสถานะคัดลอก |
| **v1.5.4** | 2026-09-30 | Electrolytes & Critical Labs DAR | เพิ่ม 24 เทมเพลตใหม่: เกลือแร่ (Hypo/Hyper K, Na, Ca, Mg, PO4 รวม 10 หัวข้อ) และผลแล็บวิกฤต (Anemia, Sepsis, Acidosis, AKI, Hepatitis ฯลฯ รวม 14 หัวข้อ) ยอดรวมคลังขยายเป็น 86 หัวข้อ |
| **v1.5.3** | 2026-09-30 | Category Filter & Modal Polish | แก้ไขตัวกรองหมวดหมู่ใน PaletteForm ให้จับคู่ด้วยรหัสตัวเลข ป้องกัน Encoding หลุด, ปรับแต่ง Web Modal กะทัดรัด ป้องกัน Scrollbar ทะลุ |
| **v1.5.2** | 2026-09-30 | UI Header 2 ชั้น & Tofu Fix | แก้ปัญหาปุ่มล้นทับชื่อโรคด้วยระบบ Header 2 ชั้น, แก้ปัญหากล่องสี่เหลี่ยมภาษาไทยด้วยฟอนต์ Leelawadee UI, ขยาย Web Modal เป็น 1240px |
| **v1.5.1** | 2026-09-29 | Desktop PaletteForm (F8) | เปิดตัวคลังเทมเพลตแบบโต้ตอบบน Desktop (กด F8) จัดรูปแบบสี Focus Charting (DAR) สวยงาม พร้อมปุ่มแทรกเตียงและวางลง e-PHIS ทันที |
| **v1.5.0** | 2026-09-29 | Orthopedic DAR Templates | เพิ่มเทมเพลตศัลยกรรมกระดูกและข้อ (TKA, UKA, THA, BHA, Spine, ORIF, Cast) รวม 62 หัวข้อ และเปิดตัว Web DAR Catalog |
| **v1.4.1** | 2026-09-28 | Self-Healing Single-Instance | ระบบ Two-Way Named Events Handshake ตรวจจับและกำจัดโปรเซสที่ค้าง (Hung processes) ใน Task Manager อัตโนมัติ |
| **v1.4.0** | 2026-09-28 | Web & Mobile Portal | เปิดตัวระบบเว็บแอปพลิเคชัน Responsive สำหรับสมาร์ทโฟน/แท็บเล็ตในวอร์ด พร้อมระบบสแกน QR Code สำหรับกลุ่ม LINE |
| **v1.3.0 - v1.3.2** | 2026-09-27 | Supabase Cloud Real-Time Sync | ซิงค์เตียง 1-30 และประวัติย้อนหลังผ่าน Supabase PostgreSQL API แบบ Offline-First (< 1ms) ปลอดภัย ลื่นไหล ไม่ค้าง |
| **v1.0.0 - v1.2.2** | 2026-09-25 | Core Architecture & Auto-Updater | ระบบ Low-level Keyboard Hook Text Expander, จัดการเตียง 1-30, ระบบอัปเดตอัตโนมัติผ่าน GitHub Releases |

> **หมายเหตุ**: สำหรับบันทึกประวัติการพัฒนาและรายละเอียดการเปลี่ยนแปลงแบบเจาะลึกในแต่ละไฟล์ สามารถอ่านเพิ่มเติมได้ที่ไฟล์ `CHANGELOG.md`
