# Changelog & Version History - Medical Text Expander

เอกสารบันทึกประวัติการพัฒนา การแก้ไขบั๊ก และการปรับปรุงฟีเจอร์ของโปรแกรม **Medical Text Expander** ในแต่ละเวอร์ชันอย่างละเอียด เพื่อเป็นคู่มือทางเทคนิคและบริบทอ้างอิงสำหรับทีมพัฒนาและ AI Agents

---

## [v1.5.6] - 2026-09-30
### ปัญหาที่พบ (Problem)
- เมื่อผู้ใช้พิมพ์หรือวางข้อความที่มีการเคาะขึ้นบรรทัดใหม่จากหน้าเว็บ/มือถือ (Web Portal) ลงในบันทึกเตียงผู้ป่วย แล้วนำมาเปิดดูในโปรแกรม Desktop พบว่า **ข้อความทั้งหมดเชื่อมติดกันเป็นแถวยาวพืดบนบรรทัดเดียว ไม่ยอมตัดบรรทัดตามแพทเทิร์น** ในขณะที่ข้อความที่พิมพ์ในโปรแกรม Desktop ยังตัดบรรทัดได้ปกติ
- **สาเหตุทางเทคนิค**: เบราว์เซอร์บนเว็บ (HTML `<textarea>`) จัดเก็บการขึ้นบรรทัดใหม่เป็น Unix Line Feed (`\n`) ขณะที่กล่องข้อความ `TextBox` บน Windows WinForms (Win32 Edit Control) ต้องการ Carriage Return + Line Feed (`\r\n`) เท่านั้น หากมีเฉพาะ `\n` กล่องข้อความจะไม่ยอมตัดบรรทัด

### การแก้ไขและปรับปรุง (Solution & Implementation)
1. **Universal Newline Normalizer ในโปรแกรม Desktop (`MedicalTextExpander.cs`)**:
   - เพิ่มฟังก์ชัน `BedNotesManager.NormalizeNewlines(string s)` แปลงทุกรูปแบบ (`\r\n`, `\r`, `\n`) ให้เป็น `\r\n` มาตรฐานของ Windows อย่างปลอดภัย (Idempotent)
   - ปรับปรุง `SupabaseSyncClient.UnescapeJson()` ให้แปลง `\\n` จาก Cloud กลายเป็น `\r\n`
   - ปรับปรุง `BedNotesManager.GetBedNote()`, `SaveBedNote()`, `ReadFileSafe()`, และ `WriteFileSafe()` ให้คืนค่าและบันทึกข้อความที่เป็น `\r\n` เสมอ
   - เพิ่มระบบ **Auto-Heal เมื่อวางข้อความ** ใน `TxtNote_TextChanged`: ดักจับหากมีการ Paste ข้อความที่มี `\n` เดี่ยวๆ โปรแกรมจะสมานและเปลี่ยนเป็น `\r\n` ทันทีโดยเคอร์เซอร์ไม่เลื่อนหลุดตำแหน่ง
   - ดักจับคีย์ลัด **Ctrl+V** ใน `TxtNote_KeyDown` เพื่อแปลงคลิปบอร์ดที่มี `\n` เป็น `\r\n` ก่อนแทรกที่ตำแหน่งเคอร์เซอร์
   - ปรับปรุง `ExpanderContext.ExecutePaste()` ให้แปลงข้อความก่อน Paste ลง e-PHIS/HIS เป็น `\r\n` เสมอ
2. **การปรับปรุงฝั่งหน้าเว็บ (`web/app.js` & `docs/app.js`)**:
   - เพิ่มฟังก์ชัน `normalizeToCRLF(text)`
   - ใน `saveCurrentBed()`: แปลงข้อความจาก `<textarea>` เป็น `\r\n` ก่อนส่งขึ้น Supabase Cloud
   - ใน `copyBedContent()`, `copySelectedTemplate()`, `fallbackCopy()`, และ `btnCopyHistoryText`: ข้อความที่คัดลอกลงคลิปบอร์ดจะมี `\r\n` กำกับไว้เสมอ
   - ปรับ Cache busters เป็น `?v=1.5.6` ใน `index.html`

---

## [v1.5.5] - 2026-09-30
### ปัญหาที่พบ (Problem)
- ในหน้าต่างคลังข้อวินิจฉัย (F8) เมื่อผู้ใช้ใช้เมาส์ลากคลุมแถบสีดำ/น้ำเงิน (Highlight Text) เพื่อเลือกคัดลอกข้อความแค่บางส่วน แต่พอกดปุ่ม `Ctrl+C` หรือคลิกปุ่ม "คัดลอก (Ctrl+C)" ตัวโปรแกรมกลับคัดลอก **ข้อความทั้งหมดของเทมเพลต** ไม่ใช่เฉพาะส่วนที่เลือก

### การแก้ไขและปรับปรุง (Solution & Implementation)
1. **Smart Selection Detection (`GetSelectedPreviewText`)**:
   - ตรวจสอบว่ามีการลากคลุมข้อความใน `rtbDar` (RichTextBox preview) หรือ `txtRaw` (TextBox preview) หรือไม่ ถ้ามี ให้ดึงเฉพาะ `SelectedText` ไปใส่คลิปบอร์ด
   - หากไม่มีการเลือกข้อความใดๆ ให้คัดลอกทั้งเทมเพลต (`item.Content`) ตามเดิม
2. **รักษาสถานะแถบสีไฮไลท์ (`HideSelection = false`)**:
   - กำหนดให้ `rtbDar.HideSelection = false` และ `txtRaw.HideSelection = false` ป้องกันไม่ให้แถบสีไฮไลท์หลุดหายเมื่อผู้ใช้เลื่อนเมาส์ไปคลิกปุ่มบนแถบเครื่องมือ
3. **ปุ่มแสดงสถานะชัดเจน**:
   - เมื่อคัดลอกเฉพาะจุด ปุ่มจะเปลี่ยนเป็น `"คัดลอกส่วนที่เลือกแล้ว!"`
   - เมื่อคัดลอกทั้งเทมเพลต ปุ่มจะเปลี่ยนเป็น `"คัดลอกทั้งหมดแล้ว!"`
4. **เพิ่มเมนูคลิกขวา (Right-Click Context Menu)**:
   - เพิ่ม ContextMenuStrip บนข้อความพรีวิว: คัดลอกส่วนที่เลือก (พร้อมนับจำนวนตัวอักษร), คัดลอกทั้งหมด, วางลง e-PHIS, แทรกเตียง, และเลือกทั้งหมด (Ctrl+A)
5. **รองรับการวางและแทรกเฉพาะส่วนที่เลือก**:
   - ฟังก์ชัน `InsertToBed()` และ `PasteSelected()` หากผู้ใช้ไฮไลท์ข้อความเฉพาะจุดไว้ จะนำเฉพาะส่วนที่เลือกไปแทรกหรือวางลง e-PHIS ทันที
6. **ปรับใช้บน BedNotesForm และ Web Portal**:
   - ปุ่มคัดลอกบน BedNotesForm รองรับการคัดลอกเฉพาะข้อความที่เลือกในเตียง
   - หน้าเว็บ `web/app.js` รองรับ `window.getSelection()` ในการคัดลอกเฉพาะส่วนที่เลือกใน Modal

---

## [v1.5.4] - 2026-09-30
### ฟีเจอร์ใหม่ (New Features)
- เพิ่มคลังเทมเพลต Focus Charting (DAR) สำหรับ **ภาวะเกลือแร่ในเลือดผิดปกติ (Electrolyte Imbalances)** และ **ผลแล็บ/ค่าเลือดผิดปกติขั้นวิกฤต (Critical Labs & Abnormal Blood Values)** ครบถ้วน 24 หัวข้อใหม่ (ขยายยอดรวมคลังเป็น 86 หัวข้อ):
  1. **หมวด 14: ภาวะเกลือแร่ในเลือดผิดปกติ (10 หัวข้อ)**:
     - `.hypok`: Hypokalemia (K < 3.5 mEq/L) - EKG U wave, IV KCl Drip precaution, rate limit, vein irritation
     - `.hyperk`: Hyperkalemia (K > 5.0 mEq/L) - Tall peaked T, Calcium gluconate, RI+50% Glucose, Kalimate
     - `.hypona`: Hyponatremia (Na < 135 mEq/L) - Cerebral edema guard, 3% NaCl slow correction (max 8-10 mEq/L/day)
     - `.hyperna`: Hypernatremia (Na > 145 mEq/L) - Free water deficit, 0.45% NaCl / D5W, slow correction
     - `.hypoca`: Hypocalcemia (Ca < 8.5 mg/dL) - Chvostek's & Trousseau's signs, 10% Calcium Gluconate IV
     - `.hyperca`: Hypercalcemia (Ca > 10.5 mg/dL) - Short QT, 0.9% NSS hydration + Lasix
     - `.hypomg`: Hypomagnesemia (Mg < 1.7 mg/dL) - Torsades de Pointes guard, 50% MgSO4 drip
     - `.hypermg`: Hypermagnesemia (Mg > 2.5 mg/dL) - Loss of DTRs, Calcium Gluconate antidote
     - `.hypop`: Hypophosphatemia (PO4 < 2.5 mg/dL) - Refeeding syndrome, weaning failure
     - `.hyperp`: Hyperphosphatemia (PO4 > 4.5 mg/dL) - CKD care, Phosphate binders with meals
  2. **หมวด 15: ผลแล็บและค่าเลือดผิดปกติ (14 หัวข้อ)**:
     - `.anemia`: Severe Anemia (Hb < 7-8 g/dL) - PRC transfusion protocol, crossmatch, VS monitoring
     - `.thrombocyto`: Severe Thrombocytopenia (Plt < 20k-50k) - Bleeding precautions, No IM
     - `.pancytopenia`: Pancytopenia - Triple precautions (infection, bleeding, anemia)
     - `.leukocytosis`: Severe Leukocytosis / Sepsis - Hemoculture x 2, IV ABx within 1 hr
     - `.neutropenia`: Febrile Neutropenia (ANC < 500-1000) - Reverse isolation, No PR
     - `.coagulopathy`: Coagulopathy (Prolonged PT/INR, aPTT) - Bleeding guard, FFP, Vit K, Protamine
     - `.metacid`: Metabolic Acidosis (pH < 7.35, HCO3 < 22) - Kussmaul breathing, 7.5% NaHCO3
     - `.metalk`: Metabolic Alkalosis (pH > 7.45, HCO3 > 26) - NSS hydration, K+/Cl- replacement
     - `.respacid`: Respiratory Acidosis (PaCO2 > 45) - COPD, BiPAP, airway clearance, Narcan
     - `.aki`: Acute Kidney Injury - Cr surge, Oliguria < 0.5 mL/kg/hr, I/O strict, Hold nephrotoxic drugs
     - `.hyperbili`: Hyperbilirubinemia / Jaundice - Tea urine, pruritus care, bleeding guard
     - `.hepatitis`: Acute Hepatitis / Transaminitis (AST/ALT > 500) - NAC, PT/INR monitoring
     - `.hypogly`: Severe Hypoglycemia (DTX < 70 mg/dL) - Rule of 15, 50% Glucose IV
     - `.hypergly`: Severe Hyperglycemia / DKA / HHS - Regular Insulin Drip, serial K+ guard
- เพิ่มปุ่มหมวดหมู่ `[เกลือแร่] K / Na / Ca / Mg` และ `[ค่าเลือดผิดปกติ] Anemia / Labs` ใน PaletteForm (F8)
- อัปเดต `web/templates.json` และ `docs/templates.json` พร้อม Badge ไอคอนหมวดหมู่ใหม่บน Web Portal

---

## [v1.5.3] - 2026-09-30
### การแก้ไขและปรับปรุง (Solution & Implementation)
- **Desktop Category Filter Fix**: ปรับปรุงฟังก์ชัน `MatchesCategoryFilter` ใน `PaletteForm` ให้กรองด้วยรหัสหมวดหมู่ตัวเลข (`9.`, `10.`, `11.`, `12.`, `13.`) ควบคู่กับคำค้นภาษาไทยและ Shortcut code เพื่อป้องกันปัญหา Encoding เพี้ยน
- **Web Template Modal Optimization**:
  - ปรับขนาดตัวหนังสือและการเว้นวรรคใน Modal พรีวิว DAR ให้อ่านง่าย สบายตา
  - เพิ่มระบบป้องกัน Scrollbar เบื้องหลังเลื่อนทะลุ (`touch-action: none`, `overflow: hidden`)
  - จัดแถบเลือกหมวดหมู่เป็นแนวนอนแบบเลื่อนได้ด้วยลูกกลิ้งเมาส์ (Mouse-Wheel Horizontal Scroll)

---

## [v1.5.2] - 2026-09-30
### ปัญหาและการแก้ไข (Problem & Solution)
- **UI Collisions / Overlaps**: หน้าต่างพรีวิวเทมเพลตบน Desktop มีปุ่มล้นทับชื่อโรค แก้ไขโดยแบ่ง Header พรีวิวออกเป็น 2 ชั้น (ชั้นบน: Title Badge, ชั้นล่าง: Action Toolbar) ปุ่มไม่เบียดชิดและไม่ทับชื่อโรค
- **Tofu Boxes / Font Glyphs**: แก้ไขปัญหากล่องสี่เหลี่ยมในตัวอักษรภาษาไทย โดยใช้ฟอนต์ `Leelawadee UI` เป็นค่าเริ่มต้น พร้อม Fallback ไปยัง `Segoe UI`
- **Web Modal Expansion**: ขยาย Modal คลังเทมเพลตบนหน้าเว็บเป็นความกว้างสูงสุด 1240px พร้อมจัดเรียงหมวดหมู่แบบ Flexible Wrap

---

## [v1.5.1] - 2026-09-29
### ฟีเจอร์ใหม่ (New Features)
- **Desktop Interactive Template Catalog & DAR Picker (F8 Hotkey)**:
  - เพิ่มหน้าต่าง `PaletteForm` ค้นหาเทมเพลตด้วยคีย์เวิร์ดหรือคลิกตามหมวดหมู่
  - การจัดหน้า Focus Charting (DAR) แบบ Rich Formatting:
    - `[FOCUS]` สีน้ำเงินคราม Indigo (#4f46e5)
    - `[DATA]` สีฟ้า Sky Blue (#0284c7)
    - `[ACTION]` สีเขียวมรกต Emerald (#059669)
    - `[RESPONSE]` สีเขียวหัวเป็ด Teal (#0d9488)
  - ปุ่ม Action ด่วน: แทรกเตียง (Insert to Bed), แทนที่เตียง (Replace Bed), วางลง e-PHIS ทันที (Paste to e-PHIS), และคัดลอก (Copy)

---

## [v1.5.0] - 2026-09-29
### ฟีเจอร์ใหม่ (New Features)
- เพิ่มคลังเทมเพลตศัลยกรรมกระดูกและข้อ (Orthopedic Nursing):
  - `.tka`: ข้อเข่าเสื่อม ผ่าตัดเปลี่ยนข้อเข่าเทียม (Total Knee Arthroplasty)
  - `.uka`: ผ่าตัดเปลี่ยนข้อเข่าเทียมบางส่วน (Unicompartmental Knee Arthroplasty)
  - `.tha`: ข้อสะโพกเสื่อม ผ่าตัดเปลี่ยนข้อสะโพกเทียม (Total Hip Arthroplasty)
  - `.bha`: ผ่าตัดเปลี่ยนหัวกระดูกสะโพกเทียม (Bipolar Hemiarthroplasty)
  - `.laminectomy`, `.plif`: ผ่าตัดกระดูกสันหลังหมอนรองกระดูกทับเส้นประสาท
  - `.orif`, `.cast`, `.traction`: การดูแลกระดูกหัก เข้าเฝือก ถ่วงน้ำหนัก
- เปิดตัวระบบ Web DAR Template Catalog บน Web Portal

---

## [v1.4.0 - v1.4.1] - 2026-09-28
### ฟีเจอร์ใหม่และการกู้คืนระบบ (Features & Reliability)
- **Ward Bed Notes Web & Mobile Portal**: พัฒนาระบบเว็บแอปพลิเคชัน Responsive HTML5/Vanilla CSS สำหรับสมาร์ทโฟนและแท็บเล็ตในวอร์ด
- **Self-Healing Single-Instance Auto-Recovery (v1.4.1)**:
  - ระบบตรวจสอบอินสแตนซ์ซ้ำซ้อนด้วย Two-Way Handshake (`ShowPalette_Event` + `WakeAck_Event`)
  - ตรวจจับและกำจัดโปรเซสที่ค้าง (Hung/Zombie processes) ใน Task Manager อัตโนมัติ ป้องกันปัญหาโปรแกรมเปิดไม่ขึ้น

---

## [v1.3.0 - v1.3.2] - 2026-09-27
### ฟีเจอร์ใหม่และการซิงค์ข้อมูล (Cloud Sync & Architecture)
- **Supabase Cloud Real-Time Sync (v1.3.0)**:
  - ซิงค์ข้อมูลบันทึกเตียง 1-30 และประวัติย้อนหลัง (History Snapshots) แบบ Real-time ผ่าน Supabase PostgreSQL API
  - สถาปัตยกรรมแบบ Offline-First: บันทึกลงเครื่องก่อน (< 1ms) แล้วส่งขึ้น Cloud ใน ThreadPool เบื้องหลัง ทำให้ UI ลื่นไหล ไม่กระตุก
- **Visual Cloud Indicators (v1.3.1)**: แสดงสถานะการเชื่อมต่อ Cloud และไฟสถานะบันทึกสำเร็จ
- **Safe Splitter Distance (v1.3.2)**: แก้ไขข้อผิดพลาด `ArgumentOutOfRangeException` บนหน้าจอ DPI scaling ต่างๆ

---

## [v1.0.0 - v1.2.2] - 2026-09-25 ถึง 2026-09-26
### รากฐานระบบ (Core Foundation)
- โปรแกรม C# WinForms สำหรับ .NET Framework 4.8 รวมอยู่ในไฟล์เดียว (`MedicalTextExpander.cs`)
- ระบบดักจับแป้นพิมพ์ Low-Level Keyboard Hook (`SetWindowsHookEx` - WH_KEYBOARD_LL) เพื่อขยายคำย่อ (Text Expander) เช่น `.vs`, `.dc`, `.order`
- หน้าต่างจัดการบันทึกเตียงผู้ป่วย 1-30 เตียง (`BedNotesForm`)
- ระบบตรวจสอบและอัปเดตโปรแกรมอัตโนมัติผ่าน GitHub Releases (`AppUpdater` + `version.json`)
