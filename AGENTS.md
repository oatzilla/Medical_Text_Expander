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

| **v1.9.5** | 2026-10-05 | Cloud Templates & Cancel SMB Share | ยกเลิกการแชร์และเชื่อมต่อไฟล์ผ่าน SMB LAN (\\192.168.134.1\Users\Public\share) อย่างสมบูรณ์ 100% ตามคำขอ เพื่อตัดปัญหาการอ่านเทมเพลตเก่าและความหน่วงในเครือข่าย รพ., ย้ายระบบจัดเก็บและซิงค์คลัง 118 เทมเพลตขึ้น Supabase Cloud (Row 102) ทำงานร่วมกับ GitHub Raw แบบเรียลไทม์ โหลดตรงถึงกันทันทีทั้ง Desktop และ Web Portal, ปรับปรุงหน้าต่างตั้งค่า Cloud Sync ใหม่เพิ่มปุ่มซิงค์และอัปโหลดเทมเพลต Cloud, และเชื่อมระบบบันทึกทับเทมเพลตบน PaletteForm ให้ซิงค์ขึ้น Cloud อัตโนมัติ |
| **v1.9.4** | 2026-10-05 | Admin Username & Direct Template Edit | อนุญาตให้แก้ไขชื่อผู้ใช้งาน (Username) ของ Admin และผู้ใช้ทั่วไปได้โดยตรงทั้งบน Desktop และ Web Portal โดยคงสิทธิ์การจัดการและเตียง Slot 0 สมบูรณ์ 100%, ปลดล็อกให้สามารถคลิกพิมพ์แก้ไขข้อความในกล่องเทมเพลต (Raw Text) ได้โดยตรงทันที ไม่ติด ReadOnly ทั้งบน Desktop Palette (F8) และ Web Modal, เพิ่มปุ่ม [💾 บันทึกทับเทมเพลต] บน PaletteForm บันทึกทับลง medical_templates.txt ได้ทันทีไม่ต้องเปิด Notepad, และซิงค์คลัง 118 เทมเพลต (รวมมะเร็งกระดูก & ฉายแสง) ขึ้น Cloud สมบูรณ์ |
| **v1.9.3** | 2026-10-05 | Bone Cancer & Radiotherapy DAR | เพิ่มหมวด 20: การพยาบาลผู้ป่วยมะเร็งกระดูกและรังสีรักษา (Bone Cancer & Radiotherapy) รวม 8 ข้อวินิจฉัยและกิจกรรมการพยาบาล Focus Charting (DAR) แยก 3 เวรชัดเจน (ยอดรวมคลังขยายเป็น 118 เทมเพลต): การดูแลผู้ป่วยมะเร็งกระดูก (.boneca), เสี่ยงกระดูกหักจากพยาธิสภาพ (.pathofx), ภาวะแคลเซียมในเลือดสูงฉุกเฉิน (.hypercamal), การดูแลผู้ป่วยก่อน-ระหว่าง-หลังฉายแสง (.radiotherapy), การดูแลผิวหนังบริเวณฉายแสงป้องกันรังสีอักเสบ (.radskin), การจัดการปวดกระดูกและปวดกำเริบหลังฉายแสง (.painflare), ภาวะกดทับไขสันหลังฉุกเฉิน MSCC (.mscc), และภาวะอ่อนเพลีย/เบื่ออาหาร/ไขกระดูกกด (.radfatigue) พร้อมปุ่มตัวกรองใหม่ทั้งบน Desktop Palette (F8) และ Web & Mobile Cloud Portal |
| **v1.9.2** | 2026-10-04 | Top Header Bar Overlap Fix | แก้ไขปัญหาแถบหัวเรื่องด้านบนซ้อนทับกัน (Top Header Bar Overlap Fix): ออกแบบระบบคำนวณพิกัด RepositionTopControls ใหม่ทั้งหมด จัดลำดับชื่อหัวเรื่อง ป้ายสถานะ Cloud ปุ่มบัญชีผู้ใช้ และแถบเครื่องมือด้านขวา ป้องกันปุ่มคำนวณ SOS/ยา และคลังข้อวินิจฉัยซ้อนทับหัวเรื่อง ปรับซ่อนปุ่มลัดซ้ำซ้อนเมื่อหน้าจอปกติ (< 1260px) โดยยังคงมีให้ใช้บนแผงคีย์ด่วนเหนือบันทึกเตียง 100% |
| **v1.9.1** | 2026-10-04 | Dialog Layout Overlap Fix | แก้ไขปัญหาหน้าต่างลงทะเบียนและเข้าสู่ระบบทับซ้อน (Dialog Layout Fix): ปรับสถาปัตยกรรมคอนเทนเนอร์เป็น pnlContent แยกส่วนชัดเจน ป้องกันแถบ Header ทับช่องกรอกข้อมูล Username/Password แสดงผลคมชัด สวยงาม 100% พร้อมจัดลำดับ Docking ของ User Management Dialog |
| **v1.9.0** | 2026-10-04 | Mandatory Login & Self-Registration | ระบบหน้าต่างเข้าสู่ระบบบังคับก่อนใช้งาน (Mandatory Authentication Gate) ทั้งบน Desktop และ Web Portal, ระบบลงทะเบียนผู้ใช้ใหม่ด้วยตนเอง (Self-Registration) และเข้าใช้งานเตียงของตนเองได้ทันที, การแยกข้อมูลเตียง 1-30 และ Local Drafts ของแต่ละคนอย่างสมบูรณ์โดยข้อมูลเดิมคงอยู่ครบถ้วนในบัญชี Admin, ระบบ Admin ควบคุม (สลับดูเตียง/รีเซ็ตรหัส/ระงับบัญชี/ลบบัญชีและล้างเตียง Cloud), และระบบ Remember Me พร้อมความปลอดภัย SHA-256 เต็มรูปแบบ |
| **v1.8.0** | 2026-10-03 | Multi-User Isolated Workspaces & Admin Control | ระบบรองรับหลายผู้ใช้ (Multi-User) โดยแต่ละคนมีชุดเตียง 1-30 แยกอิสระเป็นของตนเอง โดยข้อมูลเตียง 1-30 เดิมคงอยู่ 100% เป็นของ Admin (Slot 0) พร้อมระบบล็อกอิน SHA-256, จัดการผู้ใช้ (เพิ่ม/แก้ไข/ลบ/สลับดูเตียง), แถบแจ้งเตือนสีอำพัน (Amber Banner) เมื่อ Admin เข้าไปตรวจดู/จัดการเตียงของพยาบาล และระบบซิงค์ Row 101 บน Supabase สมบูรณ์ทั้ง Desktop WinForms และ Web Portal |
| **v1.7.0** | 2026-10-03 | Clean Bed Titles & Remove Color Text | นำคำอธิบายชื่อสีตามเลขเตียง (เช่น ฟ้าคราม, เขียวมรกต) ออกจากหัวเรื่องหน้าต่างบันทึกเตียง (Header Title) และ Tooltip บน Desktop Program ให้แสดงเฉพาะหมายเลขเตียง เช่น '🛏️ ข้อมูลผู้ป่วย เตียง 01' สะอาดตา กระชับ เป็นมืออาชีพ โดยยังคงโทนสีประจำเตียงตามเดิม |
| **v1.6.9** | 2026-10-03 | Admin Document Management Center | ปรับปรุงระบบจัดการเอกสารสำหรับ Admin ครบวงจร ทั้งบน Desktop Program และ Web Portal: เพิ่มปุ่ม [✏️ แก้ไขชื่อ & หมวดหมู่] (Edit Name/Category), ปุ่ม [🗑️ ลบเอกสารออกจากระบบ] (Delete Document) พร้อมระบบยืนยันความปลอดภัยและซิงค์ Cloud, ปุ่ม [🔄 เปลี่ยน/แทนที่ไฟล์เดิม] (Replace File), ตาราง ListView แสดงรายละเอียดเอกสารชัดเจน พร้อมช่องค้นหา Real-Time และตัวกรองหมวดหมู่ |
| **v1.6.8** | 2026-10-03 | Flex-Shrink Scroll Fix & Auto-Scroll | แก้ไขปัญหา Flexbox flex-shrink ที่ล็อกความสูงหน้าต่างอัปโหลดเอกสารบนเว็บ ทำให้หน้าต่างเลื่อนลง (Scroll) ไม่ได้บนหน้าจอขนาดกะทัดรัด (เช่น แล็ปท็อป/แท็บเล็ต) ให้แสดง Scrollbar สีเขียวเด่นชัด เลื่อนได้เต็มความสูงจนเห็นปุ่ม [บันทึกและอัปโหลดเอกสารขึ้น Cloud] สมบูรณ์ 100% พร้อมระบบ Auto-Scroll ลงมายังฟอร์มเมื่อปลดล็อกหรือเพิ่มไฟล์ |
| **v1.6.7** | 2026-10-03 | Modal Scroll Fix & Multi-File Upload Queue | แก้ไขปัญหาหน้าต่างอัปโหลดเอกสารเลื่อนหน้าจอ (Scroll) ไม่ได้ ให้สามารถเลื่อนลงมากดปุ่มบันทึกได้ทุกขนาดหน้าจอ, เพิ่มระบบอัปโหลดหลายไฟล์พร้อมกัน (Multi-File Upload Queue) พร้อมแถบพรีวิวคิวไฟล์ ปรับแต่งชื่อเอกสารและหมวดหมู่แยกตามไฟล์ก่อนส่งขึ้น Cloud ทั้งบน Web Portal และ Desktop Application |
| **v1.6.6** | 2026-10-03 | Ward Documents & Forms Center | ปรับเปลี่ยนปุ่มบน Navbar ทั้งบน Web Portal และ Desktop เป็น [ 📁 เอกสาร & แบบฟอร์มวอร์ด ] ยกระดับเป็นศูนย์รวมเอกสารประจำวอร์ด รองรับการดาวน์โหลดและจัดเก็บเอกสารหลากหลายประเภท (Excel I/O, Word, PDF, แบบประเมิน, CPG) พร้อมระบบค้นหา แยกหมวดหมู่ และหน้าต่าง Admin จัดการ/อัปโหลดเอกสารใหม่ |
| **v1.6.5** | 2026-10-03 | Ward Forms & Excel I/O Template Center | เพิ่มระบบคลังแบบฟอร์มประจำวอร์ด รองรับการดาวน์โหลดไฟล์ Excel (.xlsx) บันทึก I/O ทั้งบน Desktop และ Web Portal พร้อมหน้าต่าง Admin Template Manager อัปโหลดไฟล์เวอร์ชันใหม่ขึ้น Cloud ได้ตลอดเวลาโดยไม่ต้องแก้โค้ด |
| **v1.6.4** | 2026-10-02 | Quick Snippet Dialog & Shift Nursing Actions | ปรับปรุงปุ่มคีย์ด่วน 12 ปุ่มให้เปิดหน้าต่าง QuickSnippetSelectorDialog เลือกไฮไลท์คัดลอกหรือแทรกลงเตียงเฉพาะส่วนได้, แยกกิจกรรมการพยาบาล [ACTION] ตามเวรเช้า (08-16), เวรบ่าย (16-24), เวรดึก (24-08) ใน 12 หัวข้อหลัก พร้อมตรวจจับเวรปัจจุบันอัตโนมัติ และระบบกรองเวรบน Desktop Palette (F8) และ Web & Mobile Cloud Portal |
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
