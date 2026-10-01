# Changelog & Version History - Medical Text Expander

เอกสารบันทึกประวัติการพัฒนา การแก้ไขบั๊ก และการปรับปรุงฟีเจอร์ของโปรแกรม **Medical Text Expander** ในแต่ละเวอร์ชันอย่างละเอียด เพื่อเป็นคู่มือทางเทคนิคและบริบทอ้างอิงสำหรับทีมพัฒนาและ AI Agents

---

## [v1.5.9] - 2026-10-01
### ปัญหาที่พบ (Problem)
1. **ป็อปอัพข้อมูลปิดตัวเองลงทำให้ข้อมูลที่กำลังแก้ไขหายไป (Auto-Closing Modal & Data Loss)**:
   - ขณะพิมพ์บันทึกข้อมูลผู้ป่วยรายเตียงบนหน้าเว็บ/มือถือ หากเผลอใช้เมาส์ลากคลุมข้อความแล้วปลายเคอร์เซอร์หลุดออกนอกกรอบการ์ด (`modal-card`) หรือบนมือถือ/แท็บเล็ตมีนิ้วสัมผัสโดนขอบมาร์จินของหน้าจอ ป็อปอัพ `editModal` จะปิดตัวเองลงทันที ส่งผลให้ข้อความที่กำลังแก้ไขทั้งหมดสูญหาย เนื่องจากระบบเดิมดักจับ `click` บน `editModal` โดยตรงและไม่มีการแจ้งเตือนยืนยัน
2. **จุดแก้ไขเริ่มต้นที่ท้ายสุดของข้อมูล (Caret Position & Scroll at Bottom)**:
   - เมื่อเปิดหน้าต่างแก้ไขข้อมูลเตียงที่มีข้อความยาว เบราว์เซอร์จะโฟกัสเคอร์เซอร์ไปที่ตัวอักษรตัวสุดท้ายที่ล่างสุดของกล่องข้อความ และเลื่อน Scrollbar ลงล่างสุด ทำให้ผู้ใช้ต้องคอยเลื่อนจอกลับขึ้นไปด้านบนทุกครั้งเพื่อตรวจสอบหรือแก้ไข

### การแก้ไขและปรับปรุง (Solution & Implementation)
1. **Safe Backdrop Click & Drag Detection (`web/app.js` & `docs/app.js`)**:
   - เพิ่มการตรวจสอบ `mousedown` คู่กับ `click` (`editModalMouseDownOnBackdrop`) ป้องกันการปิดหน้าต่างเมื่อผู้ใช้ลากเมาส์คลุมข้อความหรือเลื่อน Scrollbar หลุดขอบกล่อง
   - หากมีการแก้ไขข้อความค้างอยู่ (`hasUnsavedEditChanges()`) การคลิกหรือแตะโดนพื้นหลังสีดำจะไม่ปิดหน้าต่าง แต่จะแสดงการสั่นเตือน (`shakeEditModal`) พร้อมแจ้งเตือนให้กดบันทึกหรือกดยกเลิก
2. **Confirm Discard on Cancel / Escape**:
   - เมื่อผู้ใช้กดปุ่ม "X", ปุ่ม "ยกเลิก", หรือกดปุ่ม `Escape` ขณะมีข้อความที่กำลังแก้ไขค้างอยู่ ระบบจะแสดงหน้าต่างยืนยัน (`confirm`) เพื่อป้องกันการปิดโดยไม่ได้ตั้งใจ
3. **Real-Time Auto-Draft (กู้คืนฉบับร่างอัตโนมัติ 100%)**:
   - บันทึกข้อความฉบับร่างลงใน `localStorage` (`ward_bed_draft_{bedNum}`) แบบเรียลไทม์ขณะผู้ใช้พิมพ์
   - เมื่อเปิดหน้าต่างแก้ไขเตียงเดิมอีกครั้ง ระบบจะกู้คืนฉบับร่างล่าสุดให้อัตโนมัติ ป้องกันข้อมูลสูญหายแม้เน็ตหลุด ปิดแท็บ หรือเบราว์เซอร์รีเฟรช
   - ล้างฉบับร่างออกจากหน่วยความจำเมื่อบันทึกลง Cloud สำเร็จหรือกดยืนยันละทิ้งการแก้ไข
4. **Start at Top Normalization (เริ่มจากบรรทัดบนสุด)**:
   - กำหนด `noteTextarea.scrollTop = 0` และ `noteTextarea.setSelectionRange(0, 0)` ทันทีเมื่อเปิดหน้าต่างแก้ไข
   - กำหนดให้ `.modal-body` เลื่อนกลับไปบนสุดเสมอ ผู้ใช้สามารถอ่านและเริ่มพิมพ์จากบรรทัดแรกได้ทันที
5. **ป้องกันการปิดแท็บหรือรีเฟรชหน้าจอ (`beforeunload`)**:
   - แจ้งเตือนเมื่อผู้ใช้พยายามรีเฟรชหรือปิดแท็บเบราว์เซอร์ขณะมีข้อความที่ยังไม่ได้บันทึก
6. **อัปเดตไฟล์ GitHub Pages และ Cache Busters**:
   - ซิงค์ไฟล์ไปยัง `docs/` (`app.js`, `style.css`, `index.html`) และปรับ Cache Busters เป็น `?v=1.5.9`

---

## [v1.5.8] - 2026-10-01
### ฟีเจอร์ใหม่ (New Features)
- **ระบบลากสลับ/ย้ายเตียงผู้ป่วย (Drag & Drop Bed Swapping)**:
  - รองรับการลากการ์ดเตียงด้วยเมาส์ หรือสัมผัสลาก (Touch Drag) บน iPad และสมาร์ตโฟน แล้วปล่อยลงบนเตียงปลายทางเพื่อสลับเตียงหรือย้ายเตียงได้ทันที
  - ไฮไลท์สีเมื่อลากผ่าน (Drop Target Indicator) เพื่อให้มองเห็นเตียงเป้าหมายได้อย่างชัดเจน
- **มุมมองตารางเตียงแบบกะทัดรัด (Compact View Mode)**:
  - เพิ่มปุ่มสลับมุมมอง "กะทัดรัด" (Compact) และ "ขยาย" (Expanded) บนแถบควบคุมของ Web Portal
  - ปรับขนาดการ์ดเตียงให้กระชับ แสดงข้อมูลสำคัญได้ครบทั้ง 30 เตียงโดยไม่ต้องเลื่อนหน้าจอเยอะ
  - ระบบจดจำมุมมองที่ผู้ใช้เลือกไว้ใน `localStorage` อัตโนมัติ

---

## [v1.5.7] - 2026-10-01
### ความต้องการของผู้ใช้ (User Requirement)
- พยาบาลในวอร์ดมักพบกรณีผู้ป่วยต้องย้ายเตียง (เช่น ย้ายจากเตียงสามัญไปเตียงพิเศษ หรือย้ายไปเตียงใกล้ Nurse Station) หรือกรณีผู้ป่วยสลับเตียงกันระหว่าง 2 เตียง จึงต้องการ **ฟังก์ชันสลับเตียงและย้ายเตียง (Bed Swap & Transfer)** ทั้งในโปรแกรม Desktop และหน้าเว็บ/มือถือ (Web & Mobile Portal) เพื่อความสะดวกรวดเร็ว โดยไม่ต้องเสียเวลาคัดลอกและลบด้วยตนเอง

### การวิเคราะห์และการออกแบบระบบ (System Design & Architecture)
1. **2 โหมดการทำงานอัจฉริยะ**:
   - **ย้ายเตียง (Move / Transfer)**: ย้ายข้อมูลผู้ป่วยไปยังเตียงว่าง เตียงปลายทางจะได้รับข้อมูลทั้งหมด ส่วนเตียงต้นทางจะกลายเป็นเตียงว่างลง
   - **สลับเตียง (Swap Beds)**: เมื่อเตียงปลายทางมีข้อมูลผู้ป่วยอยู่แล้ว ระบบจะเสนอทางเลือกให้สลับข้อมูลระหว่าง 2 เตียงแลกเปลี่ยนกันอย่างสมบูรณ์ หรือเลือกย้ายทับแทนที่
2. **ระบบการันตีความปลอดภัยของข้อมูลผู้ป่วย 100% (Patient Safety Guarantee)**:
   - ก่อนจะมีการย้ายหรือสลับเตียง ระบบจะทำการบันทึกประวัติภาพถ่ายข้อมูล (History Snapshot) ของทั้ง 2 เตียงลงในฐานข้อมูลประวัติย้อนหลัง (`bed_history` / `history/bed_XX_history.txt`) โดยอัตโนมัติพร้อมระบุเหตุผล เช่น `"สลับเตียงกับเตียง 05 (ข้อมูลเดิม)"` หรือ `"ย้ายข้อมูลไปยังเตียง 08"` เพื่อป้องกันการสูญหายของข้อมูลเวชระเบียน 100%
3. **การจัดการตัวเตือนหัตถการ (Ward Task Reminders Integration)**:
   - มีตัวเลือกเชื่อมต่อกับระบบตัวเตือนหัตถการรายเตียง ให้ย้ายหรือสลับหมายเลขเตียงของรายการเตือน (`WardReminderItem.BedNum`) ตามไปด้วยโดยอัตโนมัติ

### การพัฒนาในโปรแกรม Desktop (`MedicalTextExpander.cs`)
- เพิ่มเมธอด `BedNotesManager.MoveOrSwapBed(int fromBed, int toBed, bool isSwap, out string message)`
- เพิ่มเมธอด `WardReminderManager.MoveOrSwapBedReminders(int fromBed, int toBed, bool isSwap)`
- เพิ่มปุ่ม **"🔄 สลับ/ย้ายเตียง"** ในแถบเครื่องมือด้านล่าง (`pnlBottom`) ของหน้าต่างสมุดบันทึกรายเตียง (`BedNotesForm`) สีม่วงอินดิโก้อ่อน (`#EDE9FE`) ตัวหนังสือสีน้ำเงินเข้ม (`#4338CA`) ดูหรูหรา โดดเด่น และแตกต่างจากปุ่มล้างข้อมูลเตียง
- รองรับระบบ Responsive Resizing ปรับขนาดปุ่มอัตโนมัติตามความกว้างหน้าต่าง (ปุ่มเต็ม -> `🔄 ย้าย/สลับ` -> `🔄`)
- เพิ่มเมนูลัดคลิกขวา (ContextMenuStrip) บนปุ่มเตียง 1-30 ในแผงควบคุมด้านซ้าย ให้คลิกขวาเลือก "🔄 สลับ/ย้ายเตียงนี้...", "📜 ดูประวัติเตียงย้อนหลัง", หรือ "🗑️ ล้างข้อมูลเตียงนี้" ได้ทันที
- สร้างหน้าต่างโต้ตอบใหม่ **`BedSwapDialog : Form`**:
  - แสดงข้อมูลและพรีวิวเนื้อหาของเตียงต้นทาง
  - มีกล่องดรอปดาวน์เลือกเตียงปลายทาง 1-30 พร้อมระบุสถานะ `[เตียงว่าง ✨]` หรือ `[มีข้อมูล X ตัวอักษร]`
  - ตรวจจับอัตโนมัติ: หากเตียงปลายทางว่าง จะเลือกโหมด "ย้ายเตียง (Move)" ให้อัตโนมัติ แต่หากมีข้อมูล จะเปิดให้เลือก "สลับเตียง (Swap)" หรือ "ย้ายทับ (Move)"
  - กล่องติ๊กเลือกย้าย/สลับรายการเตือนหัตถการ
  - อัปเดตและสลับไปยังเตียงปลายทางให้อัตโนมัติหลังทำรายการสำเร็จ

### การพัฒนาบน Web & Mobile Portal (`web/` & `docs/`)
- เพิ่มปุ่ม **"ย้าย/สลับ"** (`.btn-card-swap`) บนการ์ดเตียงทุกใบใน Grid
- เพิ่มปุ่ม **"🔄 สลับ/ย้ายเตียง"** ในแถบเครื่องมือด้านล่างของหน้าต่างแก้ไขข้อมูลเตียง (`editModal`)
- สร้างหน้าต่าง Modal ใหม่ **`swapModal`**:
  - ออกแบบสไตล์ Modern Glassmorphism สีม่วงอินดิโก้ รองรับ Dark Mode และ Light Mode อย่างสมบูรณ์
  - แสดงการ์ดสรุปเตียงต้นทาง พร้อมไอคอนทิศทางการย้าย/สลับ
  - ดรอปดาวน์เลือกเตียงปลายทาง 1-30 พร้อมแจ้งสถานะเตียงว่างหรือมีข้อมูล
  - ตัวเลือกสลับเตียง (Swap) หรือ ย้ายเตียง (Move)
  - ซิงค์ข้อมูลขึ้น Supabase Cloud Real-time ผ่าน REST API พร้อมบันทึกประวัติ Snapshot ใน `bed_history`
  - ทำการแจ้งเตือน Toast สวยงาม และกระพริบไฮไลท์เตียงที่เปลี่ยนแปลงทั้ง 2 เตียงแบบ Real-time
- ปรับปรุง `web/style.css` และ `docs/style.css` พร้อมอัปเดต Cache Buster เป็น `?v=1.5.7`

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
